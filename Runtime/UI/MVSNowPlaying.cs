using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Now Playing panel (local UI) for an <see cref="MVSPlayer"/>: cover, title, artist, year/genre, track,
    /// status, time + seek bar, and transport buttons. Refreshes on player events plus a light 4 Hz loop
    /// only while a record is on the platter and the panel is enabled.
    ///
    /// UI wiring (SendCustomEvent): seek Slider onValueChanged → <c>_OnSeek</c>; buttons → <c>_TogglePause</c>,
    /// <c>_Previous</c>, <c>_Next</c>, <c>_ToggleLoop</c>, <c>_Eject</c>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSNowPlaying : UdonSharpBehaviour
    {
        [SerializeField] private MVSPlayer player;

        [Header("States")]
        [Tooltip("Shown when the platter is empty.")]
        [SerializeField] private GameObject emptyState;
        [Tooltip("Shown while a record is on the platter.")]
        [SerializeField] private GameObject recordState;
        [SerializeField] private MVSUIAnim recordStateAnim;

        [Header("Record")]
        [SerializeField] private RawImage cover;
        [SerializeField] private GameObject noCover;
        [SerializeField] private TextMeshProUGUI title;
        [SerializeField] private TextMeshProUGUI artist;
        [SerializeField] private TextMeshProUGUI detail;
        [SerializeField] private TextMeshProUGUI trackText;
        [SerializeField] private TextMeshProUGUI statusText;

        [Header("Time")]
        [SerializeField] private Slider seekBar;
        [SerializeField] private TextMeshProUGUI timeText;
        [SerializeField] private TextMeshProUGUI durationText;

        [Header("Transport")]
        [SerializeField] private Image playPauseIcon;
        [SerializeField] private Sprite playSprite;
        [SerializeField] private Sprite pauseSprite;
        [Tooltip("Hidden for single-track records.")]
        [SerializeField] private GameObject previousButton;
        [SerializeField] private GameObject nextButton;
        [SerializeField] private Image loopIcon;
        [SerializeField] private Button ejectButton;
        [SerializeField] private TextMeshProUGUI ejectLabel;

        [Header("Colors")]
        [SerializeField] private Color textColor = new Color(0.941f, 0.949f, 0.965f, 1f);
        [Tooltip("Loop icon color when loop is off.")]
        [SerializeField] private Color textOffColor = new Color(0.588f, 0.612f, 0.659f, 0.55f);

        [Header("Texts")]
        [SerializeField] private string droppingText = "Dropping the needle";
        [SerializeField] private string loadingText = "Loading";
        [SerializeField] private string pausedText = "Paused";
        [SerializeField] private string playingText = "Now Playing";
        [SerializeField] private string endedText = "End of record";
        [SerializeField] private string trackFormat = "Track {0} of {1}";
        [SerializeField] private string ejectText = "Eject";

        private bool settingSeek;
        private bool loopRunning;
        private int shownAlbum = -2;

        private void Start()
        {
            if (Utilities.IsValid(player)) player._AddListener(this);
            Refresh(true);
        }

        private void OnEnable()
        {
            Refresh(true);
        }

        // =========================================================================================
        // Player events
        // =========================================================================================

        public void _MVSRecordChanged() { Refresh(!player.IsInstantApply()); }
        public void _MVSTrackChanged() { Refresh(false); }
        public void _MVSStateChanged() { Refresh(false); }
        public void _MVSLoopChanged() { Refresh(false); }
        public void _MVSPlaybackChanged() { Refresh(false); }

        // =========================================================================================
        // Buttons
        // =========================================================================================

        public void _TogglePause() { if (Utilities.IsValid(player)) player.TogglePause(); }
        public void _Previous() { if (Utilities.IsValid(player)) player.Previous(); }
        public void _Next() { if (Utilities.IsValid(player)) player.Next(); }
        public void _ToggleLoop() { if (Utilities.IsValid(player)) { player.ToggleLoop(); Refresh(false); } }
        public void _Eject() { if (Utilities.IsValid(player)) { player.Eject(); Refresh(false); } }

        public void _OnSeek()
        {
            if (settingSeek || !Utilities.IsValid(seekBar) || !Utilities.IsValid(player)) return;
            player.Seek(seekBar.value);
        }

        // =========================================================================================
        // Display
        // =========================================================================================

        /// <summary>4 Hz while a record is on: time, seek bar, cooldown.</summary>
        public void _Tick()
        {
            if (!Utilities.IsValid(player) || !player.HasRecord() || !gameObject.activeInHierarchy)
            {
                loopRunning = false;
                RefreshCooldown();
                return;
            }
            RefreshTime();
            RefreshStatus();
            RefreshCooldown();
            SendCustomEventDelayedSeconds(nameof(_Tick), 0.25f);
        }

        private void EnsureLoop()
        {
            if (loopRunning) return;
            loopRunning = true;
            SendCustomEventDelayedSeconds(nameof(_Tick), 0.25f);
        }

        private void Refresh(bool animate)
        {
            if (!Utilities.IsValid(player)) return;
            bool has = player.HasRecord();
            if (Utilities.IsValid(emptyState)) emptyState.SetActive(!has);
            if (Utilities.IsValid(recordState)) recordState.SetActive(has);
            RefreshCooldown();
            if (!has) { shownAlbum = -1; return; }

            int album = player.GetAlbum();
            MVSLibrary lib = player.GetLibrary();
            if (album != shownAlbum && Utilities.IsValid(lib))
            {
                shownAlbum = album;
                Texture2D page = lib.GetCoverPage(album);
                if (Utilities.IsValid(cover))
                {
                    cover.enabled = Utilities.IsValid(page);
                    if (Utilities.IsValid(page))
                    {
                        Vector4 r = lib.GetCoverRect(album);
                        cover.texture = page;
                        cover.uvRect = new Rect(r.x, r.y, r.z, r.w);
                    }
                }
                if (Utilities.IsValid(noCover)) noCover.SetActive(!Utilities.IsValid(page));
                if (Utilities.IsValid(title)) title.text = lib.GetTitle(album);
                if (Utilities.IsValid(artist)) artist.text = lib.GetArtist(album);
                if (Utilities.IsValid(detail))
                {
                    int year = lib.GetYear(album);
                    string genre = lib.GetGenreName(lib.GetGenre(album));
                    string d = year > 0 ? year.ToString() : "";
                    if (genre.Length > 0) d = d.Length > 0 ? d + "  ·  " + genre : genre;
                    detail.text = d;
                }
                if (animate && Utilities.IsValid(recordStateAnim)) recordStateAnim.Appear(0f);
            }

            bool multi = player.IsMultiTrack();
            if (Utilities.IsValid(trackText))
            {
                trackText.gameObject.SetActive(multi);
                if (multi)
                {
                    string t = string.Format(trackFormat, player.GetTrack() + 1, player.GetTrackCount());
                    string name = player.GetTrackTitle();
                    trackText.text = name.Length > 0 ? t + "  ·  " + name : t;
                }
            }
            if (Utilities.IsValid(previousButton)) previousButton.SetActive(multi);
            if (Utilities.IsValid(nextButton)) nextButton.SetActive(multi);
            if (Utilities.IsValid(loopIcon)) loopIcon.color = player.IsLoop() ? textColor : textOffColor;
            if (Utilities.IsValid(playPauseIcon))
                playPauseIcon.sprite = player.IsPlaying() || player.IsLoading() ? pauseSprite : playSprite;

            RefreshStatus();
            RefreshTime();
            EnsureLoop();
        }

        private void RefreshStatus()
        {
            if (!Utilities.IsValid(statusText)) return;
            string s;
            if (player.GetState() == MVSPlayer.STATE_STARTING) s = droppingText;
            else if (player.IsLoading()) s = loadingText;
            else if (player.IsPaused()) s = pausedText;
            else if (player.IsPlaying()) s = playingText;
            else s = endedText;
            statusText.text = s;
            if (Utilities.IsValid(playPauseIcon))
                playPauseIcon.sprite = player.IsPlaying() || player.IsLoading() ? pauseSprite : playSprite;
        }

        private void RefreshTime()
        {
            float t = player.GetTime();
            float d = player.GetDuration();
            bool finite = d > 0f && !float.IsInfinity(d) && !float.IsNaN(d);
            if (Utilities.IsValid(timeText)) timeText.text = FormatTime(t);
            if (Utilities.IsValid(durationText)) durationText.text = finite ? FormatTime(d) : "--:--";
            if (Utilities.IsValid(seekBar))
            {
                settingSeek = true;
                seekBar.interactable = finite;
                seekBar.value = finite ? Mathf.Clamp01(t / d) : 0f;
                settingSeek = false;
            }
        }

        private void RefreshCooldown()
        {
            if (!Utilities.IsValid(player)) return;
            float cd = player.GetCooldownRemaining();
            if (Utilities.IsValid(ejectButton)) ejectButton.interactable = player.CanEject();
            if (Utilities.IsValid(ejectLabel))
                ejectLabel.text = cd > 0f && player.HasRecord() ? ejectText + "  " + Mathf.CeilToInt(cd) : ejectText;
        }

        private string FormatTime(float seconds)
        {
            if (float.IsInfinity(seconds) || float.IsNaN(seconds) || seconds < 0f) return "--:--";
            int total = Mathf.FloorToInt(seconds);
            int h = total / 3600;
            int m = (total / 60) % 60;
            int s = total % 60;
            string ss = (s < 10 ? "0" : "") + s;
            if (h > 0) return h + ":" + (m < 10 ? "0" : "") + m + ":" + ss;
            return m + ":" + ss;
        }
    }
}
