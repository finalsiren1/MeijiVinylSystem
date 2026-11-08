using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Album menu (local UI): genre filter, search, grid / list view with pages, and a click to take a record
    /// out of the pool (<see cref="MVSDiscManager.Spawn"/>). Shows which albums are playing or already out,
    /// the pool limit, and Reset All.
    ///
    /// Genre tabs are cloned at Start from <see cref="genreTabTemplate"/> (one per library genre + "All"),
    /// so adding genres in the Library Editor needs no UI work. Grid and list use fixed cell pools; the page
    /// size is the number of cells.
    ///
    /// UI wiring (SendCustomEvent): search field onValueChanged → <c>_OnSearchChanged</c>, buttons →
    /// <c>_PrevPage</c>, <c>_NextPage</c>, <c>_ShowGrid</c>, <c>_ShowList</c>, <c>_ClearSearch</c>, <c>_ResetAll</c>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSAlbumBrowser : UdonSharpBehaviour
    {
        [Header("System")]
        [SerializeField] private MVSLibrary library;
        [SerializeField] private MVSDiscManager discs;
        [SerializeField] private MVSPlayer[] players = new MVSPlayer[0];

        [Header("Genres")]
        [Tooltip("Inactive template with an MVSGenreTab; cloned per genre under its parent.")]
        [SerializeField] private GameObject genreTabTemplate;
        [SerializeField] private string allGenresLabel = "All";
        [Tooltip("Extra tab for albums without a genre (only shown when there are some).")]
        [SerializeField] private string unsortedLabel = "Unsorted";

        [Header("Views")]
        [SerializeField] private GameObject gridRoot;
        [SerializeField] private MVSAlbumCell[] gridCells = new MVSAlbumCell[0];
        [SerializeField] private GameObject listRoot;
        [SerializeField] private MVSAlbumCell[] listCells = new MVSAlbumCell[0];
        [Tooltip("Icon or label of the Grid view button (tinted Text when active, TextDim otherwise).")]
        [SerializeField] private Graphic gridTabLabel;
        [SerializeField] private Graphic listTabLabel;
        [SerializeField] private bool startInList;

        [Header("Drag to change page")]
        [Tooltip("Horizontal elastic ScrollRect whose content holds the grid and list (content same size as the viewport). " +
                 "Its onValueChanged → _OnPageDrag. Dragging past the threshold flips the page.")]
        [SerializeField] private ScrollRect pageScroller;
        [Tooltip("How far the content must be pulled (canvas units) to flip a page.")]
        [SerializeField, Min(5)] private float dragThreshold = 60f;
        [Tooltip("Optional: the page holder (with a CanvasGroup) that slides out/in on a page flip. " +
                 "Put it under a RectMask2D (soft sides) so it never overlaps its neighbours.")]
        [SerializeField] private RectTransform pageContent;
        [SerializeField, Min(0)] private float slideDistance = 160f;
        [SerializeField, Min(0.01f)] private float slideOutTime = 0.12f;
        [SerializeField, Min(0.01f)] private float slideInTime = 0.22f;

        [Header("Search")]
        [SerializeField] private TMP_InputField searchField;
        [SerializeField] private GameObject clearSearchButton;

        [Header("Footer")]
        [SerializeField] private TextMeshProUGUI pageText;
        [SerializeField] private Button prevPageButton;
        [SerializeField] private Button nextPageButton;
        [SerializeField] private TextMeshProUGUI resultText;
        [SerializeField] private TextMeshProUGUI recordsOutText;
        [SerializeField] private TextMeshProUGUI noticeText;
        [SerializeField] private GameObject noticeIcon;
        [SerializeField] private GameObject emptyState;

        [Header("Colors")]
        [SerializeField] private Color textColor = new Color(0.941f, 0.949f, 0.965f, 1f);
        [SerializeField] private Color textDimColor = new Color(0.588f, 0.612f, 0.659f, 1f);
        [SerializeField] private Color warningColor = new Color(1f, 0.588f, 0.275f, 1f);
        [SerializeField] private Color playingColor = new Color(0.275f, 0.871f, 0.471f, 1f);
        [SerializeField] private Color outColor = new Color(0.471f, 0.784f, 1f, 1f);

        [Header("Texts")]
        [SerializeField] private string spawnedText = "Your record is on the table";
        [SerializeField] private string alreadyOutText = "Already out, moved to the table";
        [SerializeField] private string inUseText = "That record is playing";
        [SerializeField] private string limitText = "Too many records out. Press Reset All";
        [SerializeField] private string resetText = "Records put away";
        [SerializeField] private string playingLabel = "Playing";
        [SerializeField] private string outLabel = "Out";
        [Tooltip("Status of an album playing in a listening booth: {0} = listener name.")]
        [SerializeField] private string boothLabelFormat = "{0}";
        [SerializeField] private Color boothColor = new Color(0.706f, 0.549f, 1f, 1f);
        [SerializeField] private string resultsFormat = "{0} albums";
        [SerializeField] private string recordsOutFormat = "{0} of {1} records out";
        [SerializeField, Min(1)] private float noticeSeconds = 4f;

        private MVSGenreTab[] tabs = new MVSGenreTab[0];
        private int[] filtered = new int[0];
        private int filteredCount;
        /// <summary>Genre filter values besides library genre indices.</summary>
        public const int GENRE_ALL = -1, GENRE_UNSORTED = -2;

        private int genre = GENRE_ALL;
        private string query = "";
        private int page;
        private bool listView;
        private bool started;
        private float noticeExpire;

        private void Start()
        {
            listView = startInList;
            BuildTabs();
            if (Utilities.IsValid(discs)) discs._AddListener(this);
            for (int i = 0; i < players.Length; i++) if (Utilities.IsValid(players[i])) players[i]._AddListener(this);
            for (int i = 0; i < gridCells.Length; i++) if (Utilities.IsValid(gridCells[i])) { gridCells[i].browser = this; gridCells[i].slot = i; }
            for (int i = 0; i < listCells.Length; i++) if (Utilities.IsValid(listCells[i])) { listCells[i].browser = this; listCells[i].slot = i; }
            if (Utilities.IsValid(pageContent))
            {
                pageHome = pageContent.localPosition;
                pageGroup = pageContent.GetComponent<CanvasGroup>();
            }
            started = true;
            SetNotice("", false);
            Refilter();
            ShowPage(false);
            RefreshFooter();
        }

        // =========================================================================================
        // Genres
        // =========================================================================================

        private void BuildTabs()
        {
            if (!Utilities.IsValid(genreTabTemplate) || !Utilities.IsValid(library)) return;
            int n = library.GetGenreCount();
            int[] counts = new int[n];
            int albums = library.GetAlbumCount();
            int unsorted = 0;
            for (int a = 0; a < albums; a++)
            {
                int g = library.GetGenre(a);
                if (g >= 0 && g < n) counts[g]++;
                else unsorted++;
            }
            int total = n + 1 + (unsorted > 0 ? 1 : 0);
            tabs = new MVSGenreTab[total];
            Transform parent = genreTabTemplate.transform.parent;
            for (int i = 0; i < total; i++)
            {
                GameObject go = Instantiate(genreTabTemplate);
                go.transform.SetParent(parent, false);
                go.SetActive(true);
                MVSGenreTab tab = go.GetComponent<MVSGenreTab>();
                if (i == 0) { go.name = "Genre All"; tab.Setup(this, GENRE_ALL, allGenresLabel, albums); }
                else if (i <= n) { go.name = "Genre " + library.GetGenreName(i - 1); tab.Setup(this, i - 1, library.GetGenreName(i - 1), counts[i - 1]); }
                else { go.name = "Genre Unsorted"; tab.Setup(this, GENRE_UNSORTED, unsortedLabel, unsorted); }
                tabs[i] = tab;
            }
            genreTabTemplate.SetActive(false);
            UpdateTabs();
        }

        public void SelectGenre(int genreIndex)
        {
            if (genre == genreIndex) return;
            genre = genreIndex;
            page = 0;
            UpdateTabs();
            Refilter();
            ShowPage(true);
        }

        private void UpdateTabs()
        {
            for (int i = 0; i < tabs.Length; i++)
                if (Utilities.IsValid(tabs[i])) tabs[i].SetSelected(tabs[i].genre == genre);
        }

        // =========================================================================================
        // Search / view / pages
        // =========================================================================================

        public void _OnSearchChanged()
        {
            if (!Utilities.IsValid(searchField)) return;
            string q = searchField.text;
            q = q == null ? "" : q.Trim().ToLower();
            if (q == query) return;
            query = q;
            page = 0;
            Refilter();
            ShowPage(false);
        }

        public void _ClearSearch()
        {
            if (Utilities.IsValid(searchField)) searchField.text = "";
            _OnSearchChanged();
        }

        public void _ShowGrid() { SetListView(false); }
        public void _ShowList() { SetListView(true); }

        private void SetListView(bool value)
        {
            if (listView == value) return;
            // keep the first visible album in view
            int first = page * PageSize();
            listView = value;
            page = first / PageSize();
            ShowPage(true);
        }

        private bool dragLatched;

        /// <summary>ScrollRect onValueChanged: pull left = next page, pull right = previous page.</summary>
        public void _OnPageDrag()
        {
            if (!Utilities.IsValid(pageScroller) || !Utilities.IsValid(pageScroller.content)) return;
            float x = pageScroller.content.anchoredPosition.x;
            if (!dragLatched)
            {
                if (x < -dragThreshold && page < PageCount() - 1) { dragLatched = true; _NextPage(); }
                else if (x > dragThreshold && page > 0) { dragLatched = true; _PrevPage(); }
            }
            else if (Mathf.Abs(x) < dragThreshold * 0.3f) dragLatched = false;
        }

        public void _PrevPage() { if (page > 0) { page--; FlipTo(-1); } }
        public void _NextPage() { if (page < PageCount() - 1) { page++; FlipTo(1); } }

        // ---- page slide (rigid page: slides away and fades at the soft mask edge, the next one slides in) ----
        private Vector3 pageHome;
        private CanvasGroup pageGroup;
        private int slideDir;
        private VRCTweenHandle slideMove, slideFade, slideTimer;

        private void FlipTo(int dir)
        {
            if (!Utilities.IsValid(pageContent)) { ShowPage(true); return; }
            slideDir = dir;
            slideMove.Kill(); slideFade.Kill(); slideTimer.Kill();
            slideMove = pageContent.TweenLocalPosition(pageHome - Vector3.right * (dir * slideDistance), slideOutTime, VRCTweenEase.InCubic);
            if (Utilities.IsValid(pageGroup)) slideFade = pageGroup.TweenFade(0f, slideOutTime, VRCTweenEase.InCubic);
            slideTimer = VRCTween.DelayedCall(this, nameof(_SlideIn), slideOutTime);
        }

        public void _SlideIn()
        {
            ShowPage(false);
            pageContent.localPosition = pageHome + Vector3.right * (slideDir * slideDistance);
            if (Utilities.IsValid(pageGroup)) { pageGroup.alpha = 0f; slideFade = pageGroup.TweenFade(1f, slideInTime, VRCTweenEase.OutCubic); }
            slideMove = pageContent.TweenLocalPosition(pageHome, slideInTime, VRCTweenEase.OutCubic);
        }

        private int PageSize()
        {
            int n = listView ? listCells.Length : gridCells.Length;
            return n < 1 ? 1 : n;
        }

        private int PageCount()
        {
            int size = PageSize();
            int pages = (filteredCount + size - 1) / size;
            return pages < 1 ? 1 : pages;
        }

        private void Refilter()
        {
            if (!Utilities.IsValid(library)) { filteredCount = 0; return; }
            int n = library.GetAlbumCount();
            if (filtered.Length != n) filtered = new int[n];
            filteredCount = 0;
            for (int a = 0; a < n; a++)
            {
                if (genre == GENRE_UNSORTED)
                {
                    int g = library.GetGenre(a);
                    if (g >= 0 && g < library.GetGenreCount()) continue;
                    if (library.Matches(a, query, GENRE_ALL)) filtered[filteredCount++] = a;
                }
                else if (library.Matches(a, query, genre)) filtered[filteredCount++] = a;
            }
            if (Utilities.IsValid(resultText)) resultText.text = string.Format(resultsFormat, filteredCount);
            if (Utilities.IsValid(emptyState)) emptyState.SetActive(filteredCount == 0);
            if (Utilities.IsValid(clearSearchButton)) clearSearchButton.SetActive(query.Length > 0);
        }

        private void ShowPage(bool animate)
        {
            if (!started) return;
            if (page >= PageCount()) page = PageCount() - 1;
            if (page < 0) page = 0;
            if (Utilities.IsValid(gridRoot)) gridRoot.SetActive(!listView);
            if (Utilities.IsValid(listRoot)) listRoot.SetActive(listView);
            if (Utilities.IsValid(gridTabLabel)) gridTabLabel.color = listView ? textDimColor : textColor;
            if (Utilities.IsValid(listTabLabel)) listTabLabel.color = listView ? textColor : textDimColor;

            MVSAlbumCell[] cells = listView ? listCells : gridCells;
            int start = page * cells.Length;
            for (int i = 0; i < cells.Length; i++)
            {
                MVSAlbumCell cell = cells[i];
                if (!Utilities.IsValid(cell)) continue;
                int f = start + i;
                if (f < filteredCount)
                {
                    cell.SetAlbum(filtered[f], library);
                    ApplyStatus(cell);
                    if (animate) cell.Appear(i * 0.015f);
                }
                else cell.Clear();
            }
            if (Utilities.IsValid(pageText)) pageText.text = (page + 1) + " / " + PageCount();
            if (Utilities.IsValid(prevPageButton)) prevPageButton.interactable = page > 0;
            if (Utilities.IsValid(nextPageButton)) nextPageButton.interactable = page < PageCount() - 1;
        }

        // =========================================================================================
        // Status
        // =========================================================================================

        private void ApplyStatus(MVSAlbumCell cell)
        {
            int a = cell.GetAlbum();
            if (a < 0) return;
            MVSPlayer p = PlayerWith(a);
            if (Utilities.IsValid(p) && p.IsBooth()) cell.SetStatus(string.Format(boothLabelFormat, p.GetListenerName()), boothColor, true);
            else if (Utilities.IsValid(p)) cell.SetStatus(playingLabel, playingColor, true);
            else if (Utilities.IsValid(discs) && discs.FindPoolDiscWithAlbum(a) >= 0) cell.SetStatus(outLabel, outColor, true);
            else cell.SetStatus("", textDimColor, false);
        }

        private MVSPlayer PlayerWith(int album)
        {
            for (int i = 0; i < players.Length; i++)
            {
                MVSPlayer p = players[i];
                if (Utilities.IsValid(p) && p.HasRecord() && p.GetAlbum() == album) return p;
            }
            return null;
        }

        private void RefreshStatuses()
        {
            MVSAlbumCell[] cells = listView ? listCells : gridCells;
            for (int i = 0; i < cells.Length; i++)
                if (Utilities.IsValid(cells[i]) && cells[i].GetAlbum() >= 0) ApplyStatus(cells[i]);
        }

        private void RefreshFooter()
        {
            if (!Utilities.IsValid(discs)) return;
            if (Utilities.IsValid(recordsOutText))
                recordsOutText.text = string.Format(recordsOutFormat, discs.GetActiveCount(), discs.GetPoolSize());
            if (discs.IsAtLimit()) SetNotice(limitText, true);
        }

        // =========================================================================================
        // Actions
        // =========================================================================================

        public void OnCellClicked(int slot)
        {
            MVSAlbumCell[] cells = listView ? listCells : gridCells;
            if (slot < 0 || slot >= cells.Length || !Utilities.IsValid(discs)) return;
            int album = cells[slot].GetAlbum();
            if (album < 0) return;
            int existing = discs.FindPoolDiscWithAlbum(album);
            if (existing >= 0 && discs.IsDiscInUse(existing)) { SetNotice(inUseText, false); return; }
            int result = discs.Spawn(album);
            if (result == MVSDiscManager.SPAWN_OK) SetNotice(spawnedText, false);
            else if (result == MVSDiscManager.SPAWN_ALREADY_OUT) SetNotice(alreadyOutText, false);
            else if (result == MVSDiscManager.SPAWN_LIMIT) SetNotice(limitText, true);
            RefreshStatuses();
            RefreshFooter();
        }

        public void _ResetAll()
        {
            if (!Utilities.IsValid(discs)) return;
            discs.ResetAll();
            SetNotice(resetText, false);
            RefreshStatuses();
            RefreshFooter();
        }

        private void SetNotice(string text, bool warning)
        {
            noticeExpire = Time.time + noticeSeconds;
            if (Utilities.IsValid(noticeText))
            {
                noticeText.text = text;
                noticeText.color = warning ? warningColor : textDimColor;
            }
            if (Utilities.IsValid(noticeIcon)) noticeIcon.SetActive(warning && text.Length > 0);
            if (text.Length > 0 && !warning)
                SendCustomEventDelayedSeconds(nameof(_NoticeTimeout), noticeSeconds);
        }

        public void _NoticeTimeout()
        {
            // a newer notice extended the time: its own timeout will clear it
            if (Time.time < noticeExpire - 0.05f) return;
            SetNotice("", false);
            RefreshFooter();
        }

        // =========================================================================================
        // Events
        // =========================================================================================

        public void _MVSDiscsChanged() { if (started) { RefreshStatuses(); RefreshFooter(); } }
        public void _MVSRecordChanged() { if (started) RefreshStatuses(); }
        public void _MVSTrackChanged() { }
        public void _MVSStateChanged() { }
        public void _MVSLoopChanged() { }
        public void _MVSPlaybackChanged() { }
    }
}
