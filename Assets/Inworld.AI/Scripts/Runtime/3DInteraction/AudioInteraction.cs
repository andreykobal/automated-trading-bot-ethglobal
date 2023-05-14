/*************************************************************************************************
* Copyright 2022 Theai, Inc. (DBA Inworld)
*
* Use of this source code is governed by the Inworld.ai Software Development Kit License Agreement
* that can be found in the LICENSE.md file or at https://www.inworld.ai/sdk-license
*************************************************************************************************/
using Inworld.Packets;
using System.Collections.Concurrent;
using UnityEngine;
using UnityEngine.Events;
namespace Inworld.Audio
{
    /// <summary>
    ///     This component is used to receive/send audio from server.
    /// </summary>
    public class AudioInteraction : Interactions
    {
        #region Callbacks
        protected override void OnPacketEvents(InworldPacket packet)
        {
            base.OnPacketEvents(packet);
            if (packet.Routing.Target.Id != Character.ID && packet.Routing.Source.Id != Character.ID)
                return;
            if (packet is not AudioChunk audioChunk)
                return;
            m_AudioChunksQueue.Enqueue(audioChunk);
        }
        #endregion

        #region Private Properties Variables
        float m_CurrentFixedUpdateTime;
        AudioChunk m_CurrentAudioChunk;
        readonly ConcurrentQueue<AudioChunk> m_AudioChunksQueue = new ConcurrentQueue<AudioChunk>();
        const float k_FixedUpdatePeriod = 0.1f;
        PacketId m_CurrentlyPlayingUtterance;
        string m_LastInteraction;
        #endregion

        #region Properties & API
         /// <summary>
        ///     Get if the Audio Source is Playing.
        /// </summary>
        public bool IsPlaying => Character && Character.PlaybackSource != null && Character.PlaybackSource.isPlaying;
        /// <summary>
        ///     Get the Current Audio Chunk.
        /// </summary>
        public AudioChunk CurrentChunk => m_CurrentAudioChunk;
        protected override void Init()
        {
            base.Init();
            Character.PlaybackSource ??= GetComponent<AudioSource>();
            Character.OnFinishedSpeaking ??= new UnityEvent();
            Character.OnBeginSpeaking ??= new UnityEvent<PacketId>();
        }
        /// <summary>
        ///     Call this func to clean up cached queue.
        /// </summary>
        public override void Clear()
        {
            base.Clear();
            OnChatHistoryListChanged();
        }
        public override void AddText(TextEvent textEvent)
        {
            CancelResponsesEvent cancel = _AddText(textEvent);
            if (cancel == null)
                return;
            // Stoping playback if current interaction is stopped.
            if (m_CurrentlyPlayingUtterance != null &&
                IsInteractionCanceled(m_CurrentlyPlayingUtterance.InteractionId))
            {
                if (Character && Character.PlaybackSource)
                    Character.PlaybackSource.Stop();
                m_CurrentlyPlayingUtterance = null;
            }
            Character.SendEventToAgent(cancel);
        }
        #endregion

        #region MonoBehavior Functions
        void Awake()
        {
            Init();
        }
        void OnEnable()
        {
            InworldController.Instance.OnPacketReceived += OnPacketEvents;
            if (Character && Character.PlaybackSource)
                Character.PlaybackSource.Stop();
        }
        void Update()
        {
            _TimerCountDown();
            _TryGetAudio();
        }
        void OnDisable()
        {
            if (InworldController.Instance)
                InworldController.Instance.OnPacketReceived -= OnPacketEvents;
        }
        #endregion

        #region Private Functions
        /**
         * Signals that there wont be more interaction utterances.
         */
        float _CurrentAudioLength
        {
            get => Character ? Character.CurrentAudioRemainingTime : 0f;
            set
            {
                if (!Character)
                    return;
                Character.CurrentAudioRemainingTime = value;
            }
        }
        void _TimerCountDown()
        {
            if (_CurrentAudioLength <= 0)
                return;
            _CurrentAudioLength -= Time.deltaTime;
            if (_CurrentAudioLength > 0)
                return;
            _CurrentAudioLength = 0;
            if (m_CurrentlyPlayingUtterance != null)
                CompleteUtterance(m_CurrentlyPlayingUtterance);
            m_CurrentlyPlayingUtterance = null;
            InworldController.Instance.TTSEnd(Character.ID);
            if (Character)
                Character.OnFinishedSpeaking?.Invoke();
        }
        void _TryGetAudio()
        {
            m_CurrentFixedUpdateTime += Time.deltaTime;
            if (m_CurrentFixedUpdateTime <= k_FixedUpdatePeriod)
                return;
            m_CurrentFixedUpdateTime = 0f;
            if (IsPlaying || !m_AudioChunksQueue.TryDequeue(out m_CurrentAudioChunk) || !IsAudioChunkAvailable(m_CurrentAudioChunk.PacketId))
                return;
            AudioClip audioClip = WavUtility.ToAudioClip(m_CurrentAudioChunk.Chunk.ToByteArray());
            if (audioClip)
            {
                _CurrentAudioLength = audioClip.length;
                if (Character && Character.PlaybackSource)
                    Character.PlaybackSource.PlayOneShot(audioClip, 1f);
            }
            StartUtterance(m_CurrentAudioChunk.PacketId);
            InworldController.Instance.TTSStart(Character.ID);
            if (Character)
                Character.OnBeginSpeaking?.Invoke(m_CurrentAudioChunk.PacketId);
        }
        bool IsAudioChunkAvailable(PacketId packetID)
        {
            string interactionID = packetID?.InteractionId;
            if (IsInteractionCanceled(interactionID))
                return false;
            if (m_LastInteraction != null && m_LastInteraction != interactionID)
            {
                CompleteInteraction(m_LastInteraction);
            }
            m_LastInteraction = interactionID;
            m_CurrentlyPlayingUtterance = packetID;
            return true;
        }
        #endregion
    }
}
