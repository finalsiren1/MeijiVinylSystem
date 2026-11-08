using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Developer test panel for <see cref="MVSPlayer"/>: pick an album, insert/eject, transport controls,
    /// and a live status readout. Not meant for production worlds. Wire UI Buttons to the public
    /// <c>_*</c> methods with SendCustomEvent.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSDebugPanel : UdonSharpBehaviour
    {
        [SerializeField] private MVSPlayer player;
        [SerializeField] private Text albumText;
        [SerializeField] private Text statusText;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.25f;

        private int selected;
        private float nextRefresh;

        private void Start()
        {
            if (Utilities.IsValid(player)) player._AddListener(this);
            RefreshAlbum();
        }

        private void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + refreshInterval;
            RefreshStatus();
        }

        // ---- Album picker ----
        public void _PrevAlbum() { Step(-1); }
        public void _NextAlbum() { Step(1); }
        public void _PrevAlbum10() { Step(-10); }
        public void _NextAlbum10() { Step(10); }

        private void Step(int delta)
        {
            MVSLibrary lib = Library();
            if (!Utilities.IsValid(lib) || lib.GetAlbumCount() == 0) return;
            int n = lib.GetAlbumCount();
            selected = ((selected + delta) % n + n) % n;
            RefreshAlbum();
        }

        // ---- Player actions ----
        public void _Insert()
        {
            if (!Utilities.IsValid(player)) return;
            // Debug panel has no physical record; use disc id 0.
            if (!player.InsertRecord(selected, 0)) Debug.Log("[MVS Debug] Insert refused (busy or cooldown).");
        }

        public void _Eject()
        {
            if (Utilities.IsValid(player) && !player.Eject()) Debug.Log("[MVS Debug] Eject refused (empty or cooldown).");
        }

        public void _TogglePause() { if (Utilities.IsValid(player)) player.TogglePause(); }
        public void _Next() { if (Utilities.IsValid(player)) player.Next(); }
        public void _Previous() { if (Utilities.IsValid(player)) player.Previous(); }
        public void _ToggleLoop() { if (Utilities.IsValid(player)) player.ToggleLoop(); }

        public void _SeekBack() { SeekBy(-0.1f); }
        public void _SeekForward() { SeekBy(0.1f); }

        private void SeekBy(float delta)
        {
            if (!Utilities.IsValid(player)) return;
            float duration = player.GetDuration();
            if (duration <= 0f || float.IsInfinity(duration)) return;
            player.Seek(player.GetTime() / duration + delta);
        }

        // ---- MVSPlayer events ----
        public void _MVSRecordChanged() { RefreshStatus(); }
        public void _MVSTrackChanged() { RefreshStatus(); }
        public void _MVSStateChanged() { RefreshStatus(); }
        public void _MVSLoopChanged() { RefreshStatus(); }
        public void _MVSPlaybackChanged() { RefreshStatus(); }

        // ---- Display ----
        private MVSLibrary Library() { return Utilities.IsValid(player) ? player.GetLibrary() : null; }

        private void RefreshAlbum()
        {
            if (!Utilities.IsValid(albumText)) return;
            MVSLibrary lib = Library();
            if (!Utilities.IsValid(lib) || lib.GetAlbumCount() == 0) { albumText.text = "(library empty)"; return; }
            string tracks = lib.GetTrackCount(selected) > 1 ? "  [" + lib.GetTrackCount(selected) + " tracks]" : "";
            albumText.text = (selected + 1) + "/" + lib.GetAlbumCount() + "  " + lib.GetDisplayName(selected) + tracks;
        }

        private void RefreshStatus()
        {
            if (!Utilities.IsValid(statusText) || !Utilities.IsValid(player)) return;
            MVSLibrary lib = Library();
            string stateName = player.GetState() == MVSPlayer.STATE_EMPTY ? "Empty"
                : player.GetState() == MVSPlayer.STATE_STARTING ? "Dropping needle..."
                : player.IsLoading() ? "Loading" : player.IsPaused() ? "Paused" : player.IsPlaying() ? "Playing" : "Stopped";

            string s = "State: " + stateName + (player.IsLoop() ? "  [Loop]" : "");
            if (player.HasRecord() && Utilities.IsValid(lib))
            {
                s += "\nRecord: " + lib.GetDisplayName(player.GetAlbum());
                if (player.IsMultiTrack())
                    s += "\nTrack " + (player.GetTrack() + 1) + "/" + player.GetTrackCount() + ": " + player.GetTrackTitle();
                s += "\nTime: " + FormatTime(player.GetTime()) + " / " + FormatTime(player.GetDuration());
            }
            float cd = player.GetCooldownRemaining();
            if (cd > 0f) s += "\nCooldown: " + cd.ToString("0.0") + " s";
            VRCPlayerApi owner = Networking.GetOwner(player.gameObject);
            if (Utilities.IsValid(owner)) s += "\nOwner: " + owner.displayName;
            statusText.text = s;
        }

        private string FormatTime(float seconds)
        {
            if (float.IsInfinity(seconds) || seconds < 0f) return "--:--";
            int total = Mathf.FloorToInt(seconds);
            int m = total / 60;
            int sec = total % 60;
            return m + ":" + (sec < 10 ? "0" : "") + sec;
        }
    }
}
