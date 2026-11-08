using JLChnToZ.VRC.VVMW;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// The record player "brain": which record is on the platter, which track, loop, and the insert/eject
    /// cooldown. Audio playback, pause, seek and late-joiner time sync are delegated to a VizVid
    /// <see cref="Core"/> (one Core dedicated to this player).
    ///
    /// Source of truth (Manual sync, owner writes): album, track, disc, state, loop, last action time.
    /// Anyone may operate the player; the caller takes ownership first.
    ///
    /// Listener events (SendCustomEvent on every registered listener, on every client):
    /// <list type="bullet">
    /// <item><c>_MVSRecordChanged</c>  record inserted / ejected (check <see cref="IsInstantApply"/> to skip animations)</item>
    /// <item><c>_MVSTrackChanged</c>   track index changed (multi-track records)</item>
    /// <item><c>_MVSStateChanged</c>   Empty / Starting / Playing changed</item>
    /// <item><c>_MVSLoopChanged</c>    loop toggled</item>
    /// <item><c>_MVSPlaybackChanged</c> VizVid loading / playing / paused / ended / error</item>
    /// </list>
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    [DisallowMultipleComponent]
    public class MVSPlayer : UdonSharpBehaviour
    {
        public const byte STATE_EMPTY = 0;    // no record on the platter
        public const byte STATE_STARTING = 1; // record placed, waiting for the drop / needle animation
        public const byte STATE_PLAYING = 2;  // audio handed to VizVid (playing, paused, loading or ended)
        public const byte STATE_FLIPPING = 3; // turning the record over (side A <-> B), audio stopped; no eject

        [Header("References")]
        [SerializeField] private MVSLibrary library;
        [Tooltip("VizVid Core used only by this record player.")]
        [SerializeField] private Core core;

        [Header("Timing")]
        [Tooltip("Seconds between insert/eject actions (anti-spam).")]
        [SerializeField, Min(0)] private float actionCooldown = 3f;
        [Tooltip("Seconds from placing a record to the needle drop (audio starts). Match the turntable animation.")]
        [SerializeField, Min(0)] private float startDelay = 2.5f;
        [Tooltip("Seconds the side flip takes (needle up, record turned over, needle down). Match the turntable.")]
        [SerializeField, Min(0)] private float flipDelay = 4.5f;

        [Header("Local Booth (optional)")]
        [Tooltip("Set for a listening booth: only the person wearing the booth headphones can operate this player, " +
                 "and its VizVid Core must have 'Synced' OFF so only they hear it. Everyone still sees the record state.")]
        [SerializeField] private MVSBooth booth;

        [Header("Listeners")]
        [Tooltip("Turntable, UI, etc. They receive the _MVS* events listed in the class summary.")]
        [SerializeField] private UdonSharpBehaviour[] listeners = new UdonSharpBehaviour[0];

        // ---- Synced state -------------------------------------------------------------------------
        [UdonSynced] private int syncAlbum = -1;
        [UdonSynced] private int syncTrack;
        [UdonSynced] private int syncDisc = -1;
        [UdonSynced] private byte syncState = STATE_EMPTY;
        [UdonSynced] private bool syncLoop;
        [UdonSynced] private int syncActionTime;
        [UdonSynced] private int syncRevision;

        // ---- Applied (local) state ----------------------------------------------------------------
        private int album = -1;
        private int track;
        private int disc = -1;
        private byte state = STATE_EMPTY;
        private bool loop;
        private int actionTime;
        private bool hasActionTime;

        private bool instantApply;
        private bool receivedFirstSync;
        private bool takingOwnership;
        private int pendingStartRevision = -1;

        private void Start()
        {
            if (Utilities.IsValid(core)) core._AddListener(this);
        }

        // =========================================================================================
        // Read API
        // =========================================================================================

        public MVSLibrary GetLibrary() { return library; }
        public Core GetCore() { return core; }

        /// <summary>Album index on the platter, -1 when empty.</summary>
        public int GetAlbum() { return album; }
        public int GetTrack() { return track; }
        /// <summary>Id of the physical record object on the platter (set by the caller of InsertRecord), -1 when empty.</summary>
        public int GetDisc() { return disc; }
        public byte GetState() { return state; }
        public bool HasRecord() { return state != STATE_EMPTY; }
        public bool IsLoop() { return loop; }
        public float GetActionCooldown() { return actionCooldown; }
        public float GetStartDelay() { return startDelay; }
        public float GetFlipDelay() { return flipDelay; }
        public bool IsFlipping() { return state == STATE_FLIPPING; }
        public bool HasSideB() { return Utilities.IsValid(library) && library.HasSideB(album); }
        /// <summary>0 = side A, 1 = side B (of the current track).</summary>
        public int GetSide() { return Utilities.IsValid(library) ? library.GetSide(album, track) : 0; }

        public bool IsMultiTrack() { return Utilities.IsValid(library) && library.IsMultiTrack(album); }
        public int GetTrackCount() { return Utilities.IsValid(library) ? library.GetTrackCount(album) : 0; }
        public string GetTrackTitle() { return Utilities.IsValid(library) ? library.GetTrackTitle(album, track) : ""; }

        public bool IsLoading() { return state == STATE_STARTING || state == STATE_FLIPPING || (state == STATE_PLAYING && Utilities.IsValid(core) && core.IsLoading); }
        public bool IsPlaying() { return state == STATE_PLAYING && Utilities.IsValid(core) && core.IsPlaying; }
        public bool IsPaused() { return state == STATE_PLAYING && Utilities.IsValid(core) && core.IsPaused; }
        public float GetTime() { return state == STATE_PLAYING && Utilities.IsValid(core) ? core.Time : 0f; }
        public float GetDuration() { return state == STATE_PLAYING && Utilities.IsValid(core) ? core.Duration : 0f; }

        /// <summary>
        /// True while listeners are being notified because of the first sync after joining (or a re-sync),
        /// so they should snap to the final pose instead of animating.
        /// </summary>
        public bool IsInstantApply() { return instantApply; }

        /// <summary>Seconds until insert/eject is allowed again (0 = ready).</summary>
        public float GetCooldownRemaining()
        {
            if (!hasActionTime || actionCooldown <= 0f) return 0f;
            float elapsed = (Networking.GetServerTimeInMilliseconds() - actionTime) / 1000f;
            return Mathf.Max(0f, actionCooldown - elapsed);
        }

        public bool CanInsert() { return CanControl() && state == STATE_EMPTY && GetCooldownRemaining() <= 0f; }
        public bool CanEject() { return CanControl() && state != STATE_EMPTY && state != STATE_FLIPPING && GetCooldownRemaining() <= 0f; }

        /// <summary>Booth players: only the headphone wearer may operate it. Global players: everyone.</summary>
        public bool CanControl() { return !Utilities.IsValid(booth) || booth.IsLocalWearer(); }
        public bool IsBooth() { return Utilities.IsValid(booth); }
        public MVSBooth GetBooth() { return booth; }

        /// <summary>Who is operating / listening (record player owner).</summary>
        public string GetListenerName()
        {
            VRCPlayerApi owner = Networking.GetOwner(gameObject);
            return Utilities.IsValid(owner) ? owner.displayName : "";
        }

        // =========================================================================================
        // Actions (call locally; the caller becomes owner)
        // =========================================================================================

        /// <summary>Place a record on the platter. Returns false when busy, cooling down or invalid.</summary>
        public bool InsertRecord(int albumIndex, int discId)
        {
            if (!Utilities.IsValid(library) || !library.IsValidAlbum(albumIndex)) return false;
            if (!CanInsert()) return false;
            TakeOwnership();
            album = albumIndex;
            track = 0;
            disc = discId;
            state = STATE_STARTING;
            StampAction();
            syncRevision++;
            pendingStartRevision = syncRevision;
            CommitAndApply();
            SendCustomEventDelayedSeconds(nameof(_OwnerStartPlayback), startDelay);
            return true;
        }

        /// <summary>Lift the needle and remove the record. Returns false when empty or cooling down.</summary>
        public bool Eject()
        {
            if (!CanEject()) return false;
            return EjectInternal();
        }

        /// <summary>Eject ignoring the cooldown and booth gate (e.g. headphones taken off, listener left).</summary>
        public void ForceEject()
        {
            if (state == STATE_EMPTY) return;
            EjectInternal();
        }

        private bool EjectInternal()
        {
            TakeOwnership();
            if (Utilities.IsValid(core)) core.Stop();
            album = -1;
            track = 0;
            disc = -1;
            state = STATE_EMPTY;
            StampAction();
            syncRevision++;
            pendingStartRevision = -1;
            CommitAndApply();
            return true;
        }

        /// <summary>Turn the record over: first track of the other side (two-sided records only).</summary>
        public void FlipSide()
        {
            if (!CanControl() || state != STATE_PLAYING || !HasSideB()) return;
            int b = library.GetSideBStart(album);
            ChangeTrack(GetSide() == 0 ? b : 0);
        }

        public void _FlipSide() { FlipSide(); }

        public void Next()
        {
            if (!CanControl()) return;
            if (state != STATE_PLAYING || !IsMultiTrack()) return;
            int count = GetTrackCount();
            int next = track + 1;
            if (next >= count)
            {
                if (!loop) return;
                next = 0;
            }
            ChangeTrack(next);
        }

        public void Previous()
        {
            if (!CanControl()) return;
            if (state != STATE_PLAYING || !IsMultiTrack()) return;
            // Like a CD player: restart the current track if we're more than 3 s in.
            if (Utilities.IsValid(core) && core.Time > 3f)
            {
                Seek(0f);
                return;
            }
            int prev = track - 1;
            if (prev < 0)
            {
                if (!loop) { Seek(0f); return; }
                prev = GetTrackCount() - 1;
            }
            ChangeTrack(prev);
        }

        public void PlayTrack(int trackIndex)
        {
            if (!CanControl()) return;
            if (state != STATE_PLAYING) return;
            if (trackIndex < 0 || trackIndex >= GetTrackCount()) return;
            ChangeTrack(trackIndex);
        }

        public void TogglePause()
        {
            if (!CanControl()) return;
            if (state != STATE_PLAYING || !Utilities.IsValid(core)) return;
            if (core.IsPaused) core.Play();
            else if (core.IsPlaying) core.Pause();
            else if (core.IsReady) core.Play(); // ended single track without loop: play again
            else PlayCurrentTrack();            // errored / stopped: reload
        }

        public void Pause()
        {
            if (!CanControl()) return;
            if (state == STATE_PLAYING && Utilities.IsValid(core) && core.IsPlaying) core.Pause();
        }

        public void Resume()
        {
            if (!CanControl()) return;
            if (state == STATE_PLAYING && Utilities.IsValid(core) && core.IsPaused) core.Play();
        }

        /// <summary>Seek to a 0..1 position of the current track.</summary>
        public void Seek(float progress)
        {
            if (!CanControl()) return;
            if (state != STATE_PLAYING || !Utilities.IsValid(core)) return;
            core.Progress = Mathf.Clamp01(progress);
        }

        public void SetLoop(bool value)
        {
            if (!CanControl()) return;
            if (loop == value) return;
            TakeOwnership();
            loop = value;
            syncRevision++;
            CommitAndApply();
            ApplyCoreLoop();
        }

        public void ToggleLoop() { SetLoop(!loop); }

        // Parameterless variants for UI Button "SendCustomEvent" bindings.
        public void _Eject() { Eject(); }
        public void _Next() { Next(); }
        public void _Previous() { Previous(); }
        public void _TogglePause() { TogglePause(); }
        public void _ToggleLoop() { ToggleLoop(); }

        /// <summary>Register a listener at runtime (e.g. a UI spawned later).</summary>
        public void _AddListener(UdonSharpBehaviour listener)
        {
            if (!Utilities.IsValid(listener)) return;
            if (!Utilities.IsValid(listeners)) listeners = new UdonSharpBehaviour[0];
            for (int i = 0; i < listeners.Length; i++)
                if (listeners[i] == listener) return;
            var grown = new UdonSharpBehaviour[listeners.Length + 1];
            for (int i = 0; i < listeners.Length; i++) grown[i] = listeners[i];
            grown[listeners.Length] = listener;
            listeners = grown;
        }

        // =========================================================================================
        // Owner-side internals
        // =========================================================================================

        /// <summary>Delayed from InsertRecord: the needle has dropped, hand the URL to VizVid.</summary>
        public void _OwnerStartPlayback()
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (state != STATE_STARTING && state != STATE_FLIPPING) return;
            if (pendingStartRevision != syncRevision) return; // superseded by a newer action
            pendingStartRevision = -1;
            state = STATE_PLAYING;
            syncRevision++;
            CommitAndApply();
            PlayCurrentTrack();
        }

        private void ChangeTrack(int trackIndex)
        {
            TakeOwnership();
            bool flip = HasSideB() && library.GetSide(album, trackIndex) != library.GetSide(album, track);
            track = trackIndex;
            syncRevision++;
            if (flip)
            {
                // Lift the needle, turn the record over, then play (same delayed start as an insert).
                if (Utilities.IsValid(core)) core.Stop();
                state = STATE_FLIPPING;
                pendingStartRevision = syncRevision;
                CommitAndApply();
                SendCustomEventDelayedSeconds(nameof(_OwnerStartPlayback), flipDelay);
                return;
            }
            CommitAndApply();
            PlayCurrentTrack();
        }

        private void PlayCurrentTrack()
        {
            if (!Utilities.IsValid(core) || !Utilities.IsValid(library)) return;
            VRCUrl url = library.GetTrackUrl(album, track);
            if (url == null || VRCUrl.IsNullOrEmpty(url)) return;
            byte playerType = core.GetSuitablePlayerType(url);
            if (playerType == 0) playerType = 1; // fall back to the first backend
            core.PlayUrl(url, null, playerType); // Core takes its own ownership and syncs URL/time
            ApplyCoreLoop();
        }

        /// <summary>Single-track records loop inside VizVid; multi-track records loop the whole record here.</summary>
        private void ApplyCoreLoop()
        {
            if (!Utilities.IsValid(core) || state != STATE_PLAYING) return;
            bool coreLoop = loop && !IsMultiTrack();
            if (core.Loop == coreLoop) return;
            if (!Networking.IsOwner(core.gameObject)) Networking.SetOwner(Networking.LocalPlayer, core.gameObject);
            core.Loop = coreLoop;
        }

        private void TakeOwnership()
        {
            if (Networking.IsOwner(gameObject)) return;
            takingOwnership = true; // OnOwnershipTransferred fires synchronously inside SetOwner
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
            takingOwnership = false;
        }

        private void StampAction()
        {
            actionTime = Networking.GetServerTimeInMilliseconds();
            hasActionTime = true;
        }

        private void CommitAndApply()
        {
            if (!Networking.IsOwner(gameObject)) return;
            syncAlbum = album;
            syncTrack = track;
            syncDisc = disc;
            syncState = state;
            syncLoop = loop;
            syncActionTime = actionTime;
            RequestSerialization();
            NotifyChanges(false);
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (takingOwnership || !Utilities.IsValid(player) || !player.isLocal) return;
            // Booth: the listener left, free the booth.
            if (Utilities.IsValid(booth))
            {
                if (state != STATE_EMPTY) EjectInternal();
                return;
            }
            // The previous owner left while a record was dropping: finish the job here.
            if (state == STATE_STARTING || state == STATE_FLIPPING)
            {
                pendingStartRevision = syncRevision;
                _OwnerStartPlayback();
            }
        }

        // =========================================================================================
        // Remote apply
        // =========================================================================================

        public override void OnDeserialization()
        {
            bool first = !receivedFirstSync;
            receivedFirstSync = true;
            album = syncAlbum;
            track = syncTrack;
            disc = syncDisc;
            state = syncState;
            loop = syncLoop;
            actionTime = syncActionTime;
            hasActionTime = syncRevision > 0;
            NotifyChanges(first);
        }

        // Last values listeners were told about.
        private int notifiedAlbum = -1, notifiedTrack, notifiedDisc = -1;
        private byte notifiedState = STATE_EMPTY;
        private bool notifiedLoop;

        private void NotifyChanges(bool instant)
        {
            instantApply = instant;
            if (album != notifiedAlbum || disc != notifiedDisc)
            {
                notifiedAlbum = album;
                notifiedDisc = disc;
                notifiedTrack = track;
                Broadcast("_MVSRecordChanged");
            }
            if (track != notifiedTrack)
            {
                notifiedTrack = track;
                Broadcast("_MVSTrackChanged");
            }
            if (state != notifiedState)
            {
                notifiedState = state;
                Broadcast("_MVSStateChanged");
            }
            if (loop != notifiedLoop)
            {
                notifiedLoop = loop;
                Broadcast("_MVSLoopChanged");
            }
            instantApply = false;
        }

        private void Broadcast(string eventName)
        {
            if (!Utilities.IsValid(listeners)) return;
            for (int i = 0; i < listeners.Length; i++)
                if (Utilities.IsValid(listeners[i])) listeners[i].SendCustomEvent(eventName);
        }

        // =========================================================================================
        // VizVid events (Core._AddListener(this))
        // =========================================================================================

        public void _onVideoEnd()
        {
            Broadcast("_MVSPlaybackChanged");
            // Only the record player owner advances multi-track records.
            if (!Networking.IsOwner(gameObject) || state != STATE_PLAYING || !IsMultiTrack()) return;
            // VizVid 1.8.3+ also reports "end" when its GameObject is disabled (e.g. an area toggle):
            // only a real end of track may advance the record.
            if (!Utilities.IsValid(core) || !core.gameObject.activeInHierarchy) return;
            float duration = core.Duration;
            if (duration > 0f && !float.IsInfinity(duration) && core.Time < duration - 2f) return;
            int next = track + 1;
            if (next >= GetTrackCount())
            {
                if (!loop) return; // record finished: stay on the last track, stopped
                next = 0;
            }
            ChangeTrack(next);
        }

        public void _OnVideoBeginLoad() { Broadcast("_MVSPlaybackChanged"); }
        public void _onVideoStart() { Broadcast("_MVSPlaybackChanged"); }
        public void _onVideoReady() { Broadcast("_MVSPlaybackChanged"); }
        public void _OnVideoError() { Broadcast("_MVSPlaybackChanged"); }
        public override void OnVideoPlay() { Broadcast("_MVSPlaybackChanged"); }
        public override void OnVideoPause() { Broadcast("_MVSPlaybackChanged"); }
    }
}
