using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace MeijiVinylSystem.Editor
{
    /// <summary>One-page editor for an <see cref="MVSLibraryData"/>: albums, tracks, covers, genres, checks.</summary>
    public class MVSLibraryWindow : EditorWindow
    {
        private const string PrefKey = "MeijiVinylSystem.LibraryWindow.Asset";
        private static readonly string[] Tabs = { "Albums", "Genres", "Check" };

        [SerializeField] private MVSLibraryData data;
        [SerializeField] private int tab;
        [SerializeField] private string search = "";
        [SerializeField] private int genreFilter = -2; // -2 = all, -1 = unsorted, >=0 genre index
        [SerializeField] private bool problemsOnly;
        [SerializeField] private string selectedId;

        private Vector2 listScroll, detailScroll, genreScroll, checkScroll;
        private ReorderableList trackList, genreList;
        private string trackListAlbumId;
        private readonly List<int> filtered = new List<int>();

        private static GUIStyle rowStyle;
        private static GUIStyle RowStyle
        {
            get
            {
                if (rowStyle == null)
                {
                    rowStyle = new GUIStyle(EditorStyles.label) { padding = new RectOffset(6, 4, 2, 2) };
                    rowStyle.onNormal.textColor = Color.white; // text on the selection highlight
                }
                return rowStyle;
            }
        }

        [MenuItem("Tools/Meiji Vinyl System/Library Editor", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<MVSLibraryWindow>();
            window.titleContent = new GUIContent("MVS Library");
            window.minSize = new Vector2(720, 420);
            window.Show();
        }

        public static void Open(MVSLibraryData asset)
        {
            Open();
            GetWindow<MVSLibraryWindow>().SetData(asset);
        }

        private void OnEnable()
        {
            if (data == null)
            {
                string guid = EditorPrefs.GetString(PrefKey, "");
                if (!string.IsNullOrEmpty(guid))
                    data = AssetDatabase.LoadAssetAtPath<MVSLibraryData>(AssetDatabase.GUIDToAssetPath(guid));
            }
            Undo.undoRedoPerformed += Repaint;
            wantsMouseMove = true; // hover highlight in the album list
        }

        private void OnDisable() => Undo.undoRedoPerformed -= Repaint;

        private void SetData(MVSLibraryData asset)
        {
            data = asset;
            trackList = null;
            genreList = null;
            selectedId = null;
            if (asset != null)
                EditorPrefs.SetString(PrefKey, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)));
        }

        // =========================================================================================

        private void OnGUI()
        {
            DrawHeader();
            if (data == null)
            {
                EditorGUILayout.HelpBox("Select a library asset, or create one.", MessageType.Info);
                return;
            }

            tab = GUILayout.Toolbar(tab, Tabs);
            EditorGUILayout.Space(2);
            switch (tab)
            {
                case 0: DrawAlbumsTab(); break;
                case 1: DrawGenresTab(); break;
                case 2: DrawCheckTab(); break;
            }
            DrawFooter();
        }

        private void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                var picked = (MVSLibraryData)EditorGUILayout.ObjectField(data, typeof(MVSLibraryData), false, GUILayout.MinWidth(200));
                if (EditorGUI.EndChangeCheck()) SetData(picked);

                if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(50))) CreateNewLibrary();
                GUILayout.FlexibleSpace();
                if (data != null)
                    GUILayout.Label($"{data.albums.Count} albums · {data.genres.Count} genres", EditorStyles.miniLabel);
            }
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                var sources = MVSLibraryBaker.FindSourcesInOpenScenes().FindAll(s => s.data == data);
                GUILayout.Label(sources.Count == 0
                        ? "Not used in the open scene(s)."
                        : $"Used by {sources.Count} MVS Library in the open scene(s). Auto-bakes before Play / Build.",
                    EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add Library to Scene", GUILayout.Width(150))) AddLibraryToScene();
                using (new EditorGUI.DisabledScope(sources.Count == 0))
                    if (GUILayout.Button("Bake Now", GUILayout.Width(90)))
                        MVSLibraryBaker.BakeAllUsing(data);
            }
        }

        // ---- Albums ----------------------------------------------------------------------------

        private void DrawAlbumsTab()
        {
            using (new EditorGUILayout.HorizontalScope(GUILayout.ExpandHeight(true)))
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(260, position.width * 0.38f))))
                    DrawAlbumList();
                using (new EditorGUILayout.VerticalScope())
                    DrawAlbumDetail();
            }
        }

        private void DrawAlbumList()
        {
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            using (new EditorGUILayout.HorizontalScope())
            {
                genreFilter = EditorGUILayout.IntPopup(genreFilter, GenreFilterNames(), GenreFilterValues());
                problemsOnly = GUILayout.Toggle(problemsOnly, "Problems", EditorStyles.miniButton, GUILayout.Width(70));
            }

            RebuildFilter();
            listScroll = EditorGUILayout.BeginScrollView(listScroll, EditorStyles.helpBox);
            foreach (int i in filtered)
            {
                MVSAlbum album = data.albums[i];
                bool selected = album.id == selectedId;
                string label = album.DisplayName + (album.year > 0 ? $" ({album.year})" : "");
                if (HasProblem(album)) label = "⚠ " + label;

                Rect row = GUILayoutUtility.GetRect(GUIContent.none, RowStyle, GUILayout.ExpandWidth(true));
                Event e = Event.current;
                if (e.type == EventType.Repaint)
                {
                    if (selected)
                        EditorGUI.DrawRect(row, EditorGUIUtility.isProSkin ? new Color(0.17f, 0.36f, 0.53f) : new Color(0.23f, 0.45f, 0.69f));
                    else if (row.Contains(e.mousePosition))
                        EditorGUI.DrawRect(row, new Color(0.5f, 0.5f, 0.5f, 0.12f));
                    RowStyle.Draw(row, label, false, false, selected, false);
                }
                else if (e.type == EventType.MouseDown && e.button == 0 && row.Contains(e.mousePosition))
                {
                    selectedId = album.id;
                    GUI.FocusControl(null);
                    e.Use();
                }
            }
            if (Event.current.type == EventType.MouseMove) Repaint();
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{filtered.Count} shown", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+", GUILayout.Width(28))) AddAlbum(null);
                using (new EditorGUI.DisabledScope(SelectedAlbum() == null))
                {
                    if (GUILayout.Button("Duplicate", GUILayout.Width(72))) AddAlbum(SelectedAlbum());
                    if (GUILayout.Button("−", GUILayout.Width(28))) DeleteSelectedAlbum();
                }
            }
        }

        private void DrawAlbumDetail()
        {
            MVSAlbum album = SelectedAlbum();
            if (album == null)
            {
                EditorGUILayout.HelpBox("Select an album on the left.", MessageType.None);
                return;
            }

            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string title = EditorGUILayout.TextField("Title", album.title);
                    string artist = EditorGUILayout.TextField("Artist", album.artist);
                    int year = EditorGUILayout.IntField(new GUIContent("Year", "0 = unknown"), album.year);
                    int genre = EditorGUILayout.IntPopup("Genre", data.GenreIndex(album.genreId), GenreNamesWithUnsorted(), GenreValuesWithUnsorted());
                    var cover = (Texture2D)EditorGUILayout.ObjectField("Cover", album.cover, typeof(Texture2D), false);
                    var sleeve = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("Sleeve Texture (2x2)", "Optional: front TL, back TR, label BL, vinyl BR. Overrides Cover."), album.sleeve, typeof(Texture2D), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Record("Edit Album");
                        album.title = title;
                        album.artist = artist;
                        album.year = Mathf.Max(0, year);
                        album.genreId = genre >= 0 ? data.genres[genre].id : "";
                        album.cover = cover;
                        album.sleeve = sleeve;
                    }
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.TextField(new GUIContent("Id", "Stable id used by records in the scene."), album.id);
                }
                Rect preview = GUILayoutUtility.GetRect(110, 110, GUILayout.Width(110), GUILayout.Height(110));
                if (album.sleeve != null) GUI.DrawTextureWithTexCoords(preview, album.sleeve, new Rect(0f, 0.5f, 0.5f, 0.5f));
                else if (album.cover != null) GUI.DrawTexture(preview, album.cover, ScaleMode.ScaleToFit);
                else GUI.Box(preview, "No cover");
            }

            EditorGUILayout.Space(6);
            EnsureTrackList(album);
            trackList.DoLayoutList();

            EditorGUILayout.LabelField("Note (editor only)");
            EditorGUI.BeginChangeCheck();
            string note = EditorGUILayout.TextArea(album.note ?? "", GUILayout.MinHeight(36));
            if (EditorGUI.EndChangeCheck()) { Record("Edit Note"); album.note = note; }

            foreach (string problem in Problems(album))
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            EditorGUILayout.EndScrollView();
        }

        private void EnsureTrackList(MVSAlbum album)
        {
            if (trackList != null && trackListAlbumId == album.id && trackList.list == album.tracks) return;
            trackListAlbumId = album.id;
            trackList = new ReorderableList(album.tracks, typeof(MVSTrack), true, true, true, true)
            {
                drawHeaderCallback = r => EditorGUI.LabelField(r, "Tracks (1 = single record, 2+ = Next / Previous)"),
                elementHeight = EditorGUIUtility.singleLineHeight * 2 + 6,
                drawElementCallback = (r, i, active, focused) =>
                {
                    MVSTrack track = album.tracks[i];
                    float h = EditorGUIUtility.singleLineHeight;
                    var r1 = new Rect(r.x, r.y + 2, r.width, h);
                    var r2 = new Rect(r.x, r.y + h + 4, r.width, h);
                    float lw = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 40;
                    EditorGUI.BeginChangeCheck();
                    string t = EditorGUI.TextField(r1, "Title", track.title);
                    string u = EditorGUI.TextField(r2, "URL", track.url);
                    EditorGUIUtility.labelWidth = lw;
                    if (EditorGUI.EndChangeCheck()) { Record("Edit Track"); track.title = t; track.url = u; }
                },
                onAddCallback = l => { Record("Add Track"); album.tracks.Add(new MVSTrack { title = "", url = "" }); },
                onRemoveCallback = l => { Record("Remove Track"); album.tracks.RemoveAt(l.index); },
                onReorderCallback = l => EditorUtility.SetDirty(data),
            };
        }

        // ---- Genres ----------------------------------------------------------------------------

        private void DrawGenresTab()
        {
            if (genreList == null || genreList.list != data.genres)
            {
                genreList = new ReorderableList(data.genres, typeof(MVSGenre), true, true, true, true)
                {
                    drawHeaderCallback = r => EditorGUI.LabelField(r, "Genres (order = tab order in the UI)"),
                    drawElementCallback = (r, i, active, focused) =>
                    {
                        MVSGenre g = data.genres[i];
                        var nameRect = new Rect(r.x, r.y + 2, r.width - 90, EditorGUIUtility.singleLineHeight);
                        var countRect = new Rect(r.xMax - 85, r.y + 2, 85, EditorGUIUtility.singleLineHeight);
                        EditorGUI.BeginChangeCheck();
                        string n = EditorGUI.TextField(nameRect, g.name);
                        if (EditorGUI.EndChangeCheck()) { Record("Rename Genre"); g.name = n; }
                        EditorGUI.LabelField(countRect, $"{CountInGenre(g.id)} albums", EditorStyles.miniLabel);
                    },
                    onAddCallback = l =>
                    {
                        Record("Add Genre");
                        data.genres.Add(new MVSGenre { id = MVSLibraryData.NewId(), name = "New Genre" });
                    },
                    onRemoveCallback = l =>
                    {
                        MVSGenre g = data.genres[l.index];
                        int count = CountInGenre(g.id);
                        if (count > 0 && !EditorUtility.DisplayDialog("Delete genre",
                                $"'{g.name}' has {count} albums. They will become Unsorted.", "Delete", "Cancel"))
                            return;
                        Record("Delete Genre");
                        foreach (MVSAlbum a in data.albums) if (a.genreId == g.id) a.genreId = "";
                        data.genres.RemoveAt(l.index);
                    },
                    onReorderCallback = l => EditorUtility.SetDirty(data),
                };
            }
            genreScroll = EditorGUILayout.BeginScrollView(genreScroll, GUILayout.ExpandHeight(true));
            genreList.DoLayoutList();
            EditorGUILayout.LabelField($"Unsorted: {CountInGenre("")} albums", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }

        // ---- Check -----------------------------------------------------------------------------

        private void DrawCheckTab()
        {
            var urlOwners = new Dictionary<string, string>();
            var nameOwners = new Dictionary<string, string>();
            var rows = new List<(MVSAlbum album, string text)>();
            foreach (MVSAlbum album in data.albums)
            {
                foreach (string p in Problems(album)) rows.Add((album, p));
                foreach (MVSTrack t in album.tracks)
                {
                    if (string.IsNullOrEmpty(t.url)) continue;
                    if (urlOwners.TryGetValue(t.url, out string other) && other != album.id)
                        rows.Add((album, $"Same URL as '{data.FindAlbum(other)?.DisplayName}'"));
                    else urlOwners[t.url] = album.id;
                }
                string key = album.DisplayName.ToLowerInvariant();
                if (nameOwners.TryGetValue(key, out string dup))
                    rows.Add((album, $"Same name as another album (id {dup})"));
                else nameOwners[key] = album.id;
            }

            EditorGUILayout.LabelField(rows.Count == 0 ? "No problems found." : $"{rows.Count} issue(s). Click to open.", EditorStyles.boldLabel);
            checkScroll = EditorGUILayout.BeginScrollView(checkScroll, GUILayout.ExpandHeight(true));
            foreach (var row in rows)
            {
                if (GUILayout.Button($"{row.album.DisplayName}  —  {row.text}", EditorStyles.label))
                {
                    selectedId = row.album.id;
                    tab = 0;
                    GUI.FocusControl(null);
                }
            }
            EditorGUILayout.EndScrollView();
            DrawCoverAtlas();
        }

        private static readonly int[] CoverSizes = { 128, 256, 512, 1024 };
        private static readonly string[] CoverSizeNames = { "128 px", "256 px", "512 px", "1024 px" };

        private void DrawCoverAtlas()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Cover Atlas", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                int size = EditorGUILayout.IntPopup("Front Cover Size", data.coverSize, CoverSizeNames, CoverSizes);
                int back = EditorGUILayout.IntPopup("Back Cover Size", data.backSize, CoverSizeNames, CoverSizes);
                int label = EditorGUILayout.IntPopup("Label Size", data.labelSize, CoverSizeNames, CoverSizes);
                if (EditorGUI.EndChangeCheck()) { Record("Atlas Sizes"); data.coverSize = size; data.backSize = back; data.labelSize = label; }

                bool upToDate = MVSCoverAtlasBuilder.IsUpToDate(data);
                int pages = data.coverPages.Count;
                EditorGUILayout.LabelField(upToDate
                        ? $"Up to date · front {pages}, back {data.backPages.Count}, label {data.labelPages.Count} page(s) in {MVSCoverAtlasBuilder.PagesFolder(data)}"
                        : "Out of date (covers changed). Rebuilds automatically before Play / Build.",
                    EditorStyles.miniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = 0; i < pages; i++)
                        if (data.coverPages[i] != null && GUILayout.Button($"Page {i}", EditorStyles.miniButton, GUILayout.Width(60)))
                            EditorGUIUtility.PingObject(data.coverPages[i]);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(upToDate ? "Rebuild" : "Build Now", GUILayout.Width(90)))
                    {
                        MVSCoverAtlasBuilder.Build(data, true);
                        MVSLibraryBaker.BakeAllUsing(data);
                    }
                }
            }
        }

        // ---- Helpers ---------------------------------------------------------------------------

        private static bool HasProblem(MVSAlbum album) => Problems(album).Count > 0;

        private static List<string> Problems(MVSAlbum album)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(album.title)) list.Add("Title is empty.");
            if (string.IsNullOrWhiteSpace(album.artist)) list.Add("Artist is empty.");
            if (album.year <= 0) list.Add("Year is unknown.");
            if (string.IsNullOrEmpty(album.genreId)) list.Add("No genre (Unsorted).");
            if (album.cover == null && album.sleeve == null) list.Add("No cover.");
            if (album.tracks.Count == 0) list.Add("No tracks.");
            for (int i = 0; i < album.tracks.Count; i++)
            {
                string url = album.tracks[i].url;
                if (string.IsNullOrWhiteSpace(url)) list.Add($"Track {i + 1}: URL is empty.");
                else if (!url.StartsWith("http://") && !url.StartsWith("https://")) list.Add($"Track {i + 1}: URL must start with http(s)://");
                if (album.tracks.Count > 1 && string.IsNullOrWhiteSpace(album.tracks[i].title))
                    list.Add($"Track {i + 1}: multi-track records need track titles.");
            }
            if (!string.IsNullOrWhiteSpace(album.note)) list.Add("Note: " + album.note);
            return list;
        }

        private void RebuildFilter()
        {
            filtered.Clear();
            string q = (search ?? "").Trim().ToLowerInvariant();
            for (int i = 0; i < data.albums.Count; i++)
            {
                MVSAlbum a = data.albums[i];
                if (genreFilter != -2)
                {
                    int g = data.GenreIndex(a.genreId);
                    if (g != genreFilter) continue;
                }
                if (problemsOnly && !HasProblem(a)) continue;
                if (q.Length > 0)
                {
                    string hay = $"{a.title} {a.artist} {a.year} {a.id}".ToLowerInvariant();
                    if (!hay.Contains(q)) continue;
                }
                filtered.Add(i);
            }
        }

        private int CountInGenre(string genreId)
        {
            int n = 0;
            foreach (MVSAlbum a in data.albums)
            {
                bool unsorted = string.IsNullOrEmpty(a.genreId) || data.FindGenre(a.genreId) == null;
                if (string.IsNullOrEmpty(genreId) ? unsorted : a.genreId == genreId) n++;
            }
            return n;
        }

        private string[] GenreFilterNames()
        {
            var names = new List<string> { "All genres", "Unsorted" };
            foreach (MVSGenre g in data.genres) names.Add(g.name);
            return names.ToArray();
        }

        private int[] GenreFilterValues()
        {
            var values = new List<int> { -2, -1 };
            for (int i = 0; i < data.genres.Count; i++) values.Add(i);
            return values.ToArray();
        }

        private string[] GenreNamesWithUnsorted()
        {
            var names = new List<string> { "Unsorted" };
            foreach (MVSGenre g in data.genres) names.Add(g.name);
            return names.ToArray();
        }

        private int[] GenreValuesWithUnsorted()
        {
            var values = new List<int> { -1 };
            for (int i = 0; i < data.genres.Count; i++) values.Add(i);
            return values.ToArray();
        }

        private MVSAlbum SelectedAlbum() => data == null ? null : data.FindAlbum(selectedId);

        private void Record(string what)
        {
            Undo.RecordObject(data, what);
            EditorUtility.SetDirty(data);
        }

        private void AddAlbum(MVSAlbum copyFrom)
        {
            Record(copyFrom == null ? "Add Album" : "Duplicate Album");
            var album = new MVSAlbum
            {
                id = MVSLibraryData.NewId(),
                title = copyFrom != null ? copyFrom.title + " (copy)" : "New Album",
                artist = copyFrom?.artist ?? "",
                year = copyFrom?.year ?? 0,
                genreId = copyFrom != null ? copyFrom.genreId : (genreFilter >= 0 ? data.genres[genreFilter].id : ""),
                cover = copyFrom?.cover,
            };
            if (copyFrom != null)
                foreach (MVSTrack t in copyFrom.tracks) album.tracks.Add(new MVSTrack { title = t.title, url = t.url });
            else
                album.tracks.Add(new MVSTrack { title = "", url = "" });
            data.albums.Add(album);
            selectedId = album.id;
            search = "";
        }

        private void DeleteSelectedAlbum()
        {
            MVSAlbum album = SelectedAlbum();
            if (album == null) return;
            if (!EditorUtility.DisplayDialog("Delete album", $"Delete '{album.DisplayName}'?\nRecords in the scene that use it will become empty.", "Delete", "Cancel"))
                return;
            Record("Delete Album");
            data.albums.Remove(album);
            selectedId = null;
        }

        private void CreateNewLibrary()
        {
            string path = EditorUtility.SaveFilePanelInProject("New Vinyl Library", "VinylLibrary", "asset", "Where to save the library asset?");
            if (string.IsNullOrEmpty(path)) return;
            var asset = CreateInstance<MVSLibraryData>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            SetData(asset);
        }

        private void AddLibraryToScene()
        {
            var go = new GameObject("MVS Library");
            Undo.RegisterCreatedObjectUndo(go, "Add MVS Library");
            UdonSharpUndo.AddComponent<MVSLibrary>(go);
            var source = Undo.AddComponent<MVSLibrarySource>(go);
            source.data = data;
            MVSLibraryBaker.Bake(source);
            Selection.activeGameObject = go;
        }
    }

    /// <summary>Double-click a library asset to open it in the Library Editor.</summary>
    internal static class MVSLibraryAssetOpener
    {
        [UnityEditor.Callbacks.OnOpenAsset]
        private static bool OnOpen(int instanceId, int line)
        {
            if (EditorUtility.InstanceIDToObject(instanceId) is MVSLibraryData asset)
            {
                MVSLibraryWindow.Open(asset);
                return true;
            }
            return false;
        }
    }
}
