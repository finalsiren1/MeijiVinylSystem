using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Local listening booth: one person wears the booth headphones and listens to the booth's own record player
    /// alone. Everyone sees who is wearing them (headphones follow the wearer's head) and which record is in the
    /// booth (the booth <see cref="MVSPlayer"/> state is synced; its VizVid Core is not, so only the wearer hears it).
    ///
    /// For the wearer only (local): the world music (<see cref="globalSources"/>) fades out and mutes, the booth
    /// audio (<see cref="boothSources"/>) fades in; voice modes adjust other players' voices:
    /// Off, Focus (quieter), Focus + Muffle (quieter, shorter range, low-pass), Silent (others muted).
    /// Taking the headphones off (or leaving) reverses everything and ejects the booth record.
    ///
    /// Synced (Manual, owner writes): wearer player id, time of the last put on / take off (cooldown).
    /// Listener event: <c>_MVSBoothChanged</c>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class MVSBooth : UdonSharpBehaviour
    {
        public const int VOICE_OFF = 0, VOICE_FOCUS = 1, VOICE_MUFFLE = 2, VOICE_SILENT = 3;

        [Header("Player")]
        [Tooltip("The booth's record player (its 'Booth' field must point back to this booth).")]
        [SerializeField] private MVSPlayer player;

        [Header("Headphones")]
        [Tooltip("Visual headphones. Follows the wearer's head; sits at Home otherwise.")]
        [SerializeField] private Transform headphones;
        [SerializeField] private Transform headphonesHome;
        [Tooltip("Offset from the head bone (head local space).")]
        [SerializeField] private Vector3 headOffset = new Vector3(0f, 0.06f, 0f);
        [SerializeField] private Vector3 headRotation = Vector3.zero;
        [Tooltip("Hide the headphones for the wearer themselves (they would block the view).")]
        [SerializeField] private bool hideForWearer = true;
        [Tooltip("Seconds between putting on / taking off (anti-spam).")]
        [SerializeField, Min(0)] private float wearCooldown = 2f;

        [Header("Audio (local for the wearer)")]
        [Tooltip("World music speakers to fade out while wearing (e.g. the global VizVid audio sources).")]
        [SerializeField] private AudioSource[] globalSources = new AudioSource[0];
        [Tooltip("Booth VizVid audio sources: silent unless the local player wears the headphones.")]
        [SerializeField] private AudioSource[] boothSources = new AudioSource[0];
        [SerializeField, Min(0.05f)] private float fadeTime = 1.5f;

        [Header("Voice modes (local for the wearer)")]
        [SerializeField] private float focusGain = 6f;
        [SerializeField] private float muffleGain = 8f;
        [SerializeField] private float muffleFar = 6f;
        [Tooltip("VRChat defaults, restored when the mode is turned off.")]
        [SerializeField] private float defaultGain = 15f;
        [SerializeField] private float defaultNear = 0f;
        [SerializeField] private float defaultFar = 25f;

        [Header("Listeners")]
        [SerializeField] private UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[0];

        [UdonSynced] private int wearerId = -1;
        [UdonSynced] private int wearTime;

        private bool localWearing;
        private int voiceMode = VOICE_OFF;
        private int appliedVoiceMode = VOICE_OFF;
        private bool hasWearTime;

        // fade state (0 = world music, 1 = booth)
        private float mix;
        private bool fading;
        private float[] globalVolumes = new float[0];
        private float[] boothVolumes = new float[0];

        private void Start()
        {
            CaptureVolumes();
            boothVolumes = new float[boothSources.Length];
            for (int i = 0; i < boothSources.Length; i++)
                if (Utilities.IsValid(boothSources[i])) boothVolumes[i] = Fallback(boothSources[i].volume);
            SetMix(0f, true);
            ApplyWearer();
        }

        // =========================================================================================
        // Read API
        // =========================================================================================

        public MVSPlayer GetPlayer() { return player; }
        public bool IsLocalWearer() { return localWearing; }
        public bool IsWorn() { return Utilities.IsValid(GetWearer()); }
        public int GetVoiceMode() { return voiceMode; }

        public VRCPlayerApi GetWearer()
        {
            if (wearerId < 0) return null;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(wearerId);
            return Utilities.IsValid(p) ? p : null;
        }

        public string GetWearerName()
        {
            VRCPlayerApi p = GetWearer();
            return Utilities.IsValid(p) ? p.displayName : "";
        }

        public float GetCooldownRemaining()
        {
            if (!hasWearTime || wearCooldown <= 0f) return 0f;
            float elapsed = (Networking.GetServerTimeInMilliseconds() - wearTime) / 1000f;
            return Mathf.Max(0f, wearCooldown - elapsed);
        }

        /// <summary>Can the local player put the headphones on right now?</summary>
        public bool CanPutOn() { return !IsWorn() && GetCooldownRemaining() <= 0f; }

        // =========================================================================================
        // Actions
        // =========================================================================================

        /// <summary>Headphones Interact / UI: put on if free, take off if wearing.</summary>
        public void _ToggleHeadphones()
        {
            if (localWearing) _TakeOff();
            else _PutOn();
        }

        public void _PutOn()
        {
            if (!CanPutOn()) return;
            TakeOwnership();
            wearerId = Networking.LocalPlayer.playerId;
            Stamp();
            RequestSerialization();
            ApplyWearer();
        }

        public void _TakeOff()
        {
            if (!localWearing) return;
            if (GetCooldownRemaining() > 0f) return;
            // free the booth record first (we own the booth player while listening)
            if (Utilities.IsValid(player)) player.ForceEject();
            TakeOwnership();
            wearerId = -1;
            Stamp();
            RequestSerialization();
            ApplyWearer();
        }

        public void SetVoiceMode(int mode)
        {
            voiceMode = Mathf.Clamp(mode, VOICE_OFF, VOICE_SILENT);
            if (localWearing) ApplyVoice(voiceMode);
            Broadcast();
        }

        public void _VoiceOff() { SetVoiceMode(VOICE_OFF); }
        public void _VoiceFocus() { SetVoiceMode(VOICE_FOCUS); }
        public void _VoiceMuffle() { SetVoiceMode(VOICE_MUFFLE); }
        public void _VoiceSilent() { SetVoiceMode(VOICE_SILENT); }

        public void _AddListener(UdonSharpBehaviour listener)
        {
            if (!Utilities.IsValid(listener)) return;
            for (int i = 0; i < listeners.Length; i++) if (listeners[i] == listener) return;
            var grown = new UdonSharpBehaviour[listeners.Length + 1];
            for (int i = 0; i < listeners.Length; i++) grown[i] = listeners[i];
            grown[listeners.Length] = listener;
            listeners = grown;
        }

        // =========================================================================================
        // Sync / apply
        // =========================================================================================

        public override void OnDeserialization()
        {
            hasWearTime = true;
            ApplyWearer();
        }

        public override void OnPlayerLeft(VRCPlayerApi left)
        {
            if (!Utilities.IsValid(left) || left.playerId != wearerId) return;
            // The wearer left: whoever owns the booth now clears it (ownership already moved if they owned it).
            if (Networking.IsOwner(gameObject))
            {
                wearerId = -1;
                RequestSerialization();
            }
            ApplyWearer();
        }

        public override void OnPlayerJoined(VRCPlayerApi joined)
        {
            // keep voice settings consistent for people who join while we listen
            if (localWearing && voiceMode != VOICE_OFF && Utilities.IsValid(joined) && !joined.isLocal)
                ApplyVoiceTo(joined, voiceMode);
        }

        private void ApplyWearer()
        {
            VRCPlayerApi local = Networking.LocalPlayer;
            bool nowLocal = Utilities.IsValid(local) && wearerId == local.playerId;
            if (nowLocal != localWearing)
            {
                localWearing = nowLocal;
                CaptureVolumes();
                StartFade();
                ApplyVoice(localWearing ? voiceMode : VOICE_OFF);
            }
            PlaceHeadphones();
            Broadcast();
        }

        // =========================================================================================
        // Headphones visual
        // =========================================================================================

        public override void PostLateUpdate()
        {
            if (wearerId >= 0) PlaceHeadphones();
        }

        private void PlaceHeadphones()
        {
            if (!Utilities.IsValid(headphones)) return;
            VRCPlayerApi wearer = GetWearer();
            if (!Utilities.IsValid(wearer))
            {
                if (!headphones.gameObject.activeSelf) headphones.gameObject.SetActive(true);
                if (Utilities.IsValid(headphonesHome)) headphones.SetPositionAndRotation(headphonesHome.position, headphonesHome.rotation);
                return;
            }
            bool hide = hideForWearer && wearer.isLocal;
            if (headphones.gameObject.activeSelf == hide) headphones.gameObject.SetActive(!hide);
            if (hide) return;
            Vector3 headPos = wearer.GetBonePosition(HumanBodyBones.Head);
            Quaternion headRot = wearer.GetBoneRotation(HumanBodyBones.Head);
            if (headPos == Vector3.zero) // no humanoid head: use the viewpoint
            {
                VRCPlayerApi.TrackingData t = wearer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
                headPos = t.position;
                headRot = t.rotation;
            }
            headphones.SetPositionAndRotation(headPos + headRot * headOffset, headRot * Quaternion.Euler(headRotation));
        }

        // =========================================================================================
        // Audio fade (local)
        // =========================================================================================

        private void CaptureVolumes()
        {
            // Remember the volume VizVid set on each source while that side is fully audible,
            // so fades scale it and the end state restores it (VizVid's own volume setting is never touched).
            if (globalVolumes.Length != globalSources.Length) globalVolumes = new float[globalSources.Length];
            if (boothVolumes.Length != boothSources.Length) boothVolumes = new float[boothSources.Length];
            if (mix <= 0f)
                for (int i = 0; i < globalSources.Length; i++)
                    if (Utilities.IsValid(globalSources[i])) globalVolumes[i] = Fallback(globalSources[i].volume);
            if (mix >= 1f)
                for (int i = 0; i < boothSources.Length; i++)
                    if (Utilities.IsValid(boothSources[i])) boothVolumes[i] = Fallback(boothSources[i].volume);
        }

        private float Fallback(float v) { return v > 0.001f ? v : 1f; }

        private void StartFade()
        {
            if (fading) return;
            fading = true;
            SendCustomEventDelayedFrames(nameof(_FadeStep), 1);
        }

        public void _FadeStep()
        {
            float target = localWearing ? 1f : 0f;
            mix = Mathf.MoveTowards(mix, target, Time.deltaTime / fadeTime);
            SetMix(mix, false);
            if (Mathf.Approximately(mix, target))
            {
                fading = false;
                SetMix(target, true);
                return;
            }
            SendCustomEventDelayedFrames(nameof(_FadeStep), 1);
        }

        /// <summary>mix 0 = world music only, 1 = booth only. At the ends the silent side is muted and its volume restored.</summary>
        private void SetMix(float m, bool settle)
        {
            for (int i = 0; i < globalSources.Length; i++)
            {
                AudioSource a = globalSources[i];
                if (!Utilities.IsValid(a)) continue;
                if (settle && m >= 1f) { a.volume = globalVolumes[i]; a.mute = true; }
                else { a.mute = false; a.volume = globalVolumes[i] * (1f - m); }
            }
            for (int i = 0; i < boothSources.Length; i++)
            {
                AudioSource a = boothSources[i];
                if (!Utilities.IsValid(a)) continue;
                if (settle && m <= 0f) { a.volume = boothVolumes[i]; a.mute = true; }
                else { a.mute = false; a.volume = boothVolumes[i] * m; }
            }
        }

        // =========================================================================================
        // Voice (local)
        // =========================================================================================

        private void ApplyVoice(int mode)
        {
            if (mode == appliedVoiceMode) return;
            appliedVoiceMode = mode;
            VRCPlayerApi[] players = new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()];
            VRCPlayerApi.GetPlayers(players);
            for (int i = 0; i < players.Length; i++)
            {
                VRCPlayerApi p = players[i];
                if (Utilities.IsValid(p) && !p.isLocal) ApplyVoiceTo(p, mode);
            }
        }

        private void ApplyVoiceTo(VRCPlayerApi p, int mode)
        {
            if (mode == VOICE_SILENT)
            {
                p.SetVoiceGain(0f);
                p.SetVoiceDistanceNear(0f);
                p.SetVoiceDistanceFar(0f);
                p.SetAvatarAudioGain(0f);
            }
            else if (mode == VOICE_FOCUS)
            {
                p.SetVoiceGain(focusGain);
                p.SetVoiceDistanceNear(defaultNear);
                p.SetVoiceDistanceFar(defaultFar);
                p.SetVoiceLowpass(true);
                p.SetAvatarAudioGain(5f);
            }
            else if (mode == VOICE_MUFFLE)
            {
                p.SetVoiceGain(muffleGain);
                p.SetVoiceDistanceNear(0f);
                p.SetVoiceDistanceFar(muffleFar);
                p.SetVoiceLowpass(true);
                p.SetAvatarAudioGain(3f);
            }
            else
            {
                p.SetVoiceGain(defaultGain);
                p.SetVoiceDistanceNear(defaultNear);
                p.SetVoiceDistanceFar(defaultFar);
                p.SetVoiceLowpass(true);
                p.SetAvatarAudioGain(10f);
            }
        }

        // =========================================================================================

        private void Stamp()
        {
            wearTime = Networking.GetServerTimeInMilliseconds();
            hasWearTime = true;
        }

        private void TakeOwnership()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }

        private void Broadcast()
        {
            for (int i = 0; i < listeners.Length; i++)
                if (Utilities.IsValid(listeners[i])) listeners[i].SendCustomEvent("_MVSBoothChanged");
        }
    }
}
