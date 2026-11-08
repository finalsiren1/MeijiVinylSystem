using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

namespace MeijiVinylSystem.Editor
{
    /// <summary>
    /// Copies an <see cref="MVSLibraryData"/> asset into the Udon-readable arrays of <see cref="MVSLibrary"/>.
    /// Runs automatically before entering Play Mode and before a VRChat build/upload.
    /// </summary>
    [InitializeOnLoad]
    public static class MVSLibraryBaker
    {
        static MVSLibraryBaker()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) BakeAllInOpenScenes(false);
        }

        /// <summary>Bakes every MVSLibrarySource in all loaded scenes. Returns the number baked.</summary>
        public static int BakeAllInOpenScenes(bool log = true)
        {
            int count = 0;
            foreach (var source in FindSourcesInOpenScenes())
                if (Bake(source, log)) count++;
            return count;
        }

        /// <summary>Bakes the sources in open scenes that use the given data asset.</summary>
        public static int BakeAllUsing(MVSLibraryData data, bool log = true)
        {
            int count = 0;
            foreach (var source in FindSourcesInOpenScenes())
                if (source.data == data && Bake(source, log)) count++;
            return count;
        }

        public static List<MVSLibrarySource> FindSourcesInOpenScenes()
        {
            var result = new List<MVSLibrarySource>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    result.AddRange(root.GetComponentsInChildren<MVSLibrarySource>(true));
            }
            return result;
        }

        public static bool Bake(MVSLibrarySource source, bool log = true)
        {
            if (source == null) return false;
            MVSLibraryData data = source.data;
            MVSLibrary library = source.GetComponent<MVSLibrary>();
            if (data == null)
            {
                Debug.LogWarning($"[MVS] {source.name}: MVS Library Source has no library asset.", source);
                return false;
            }
            if (library == null)
            {
                Debug.LogWarning($"[MVS] {source.name}: needs an MVS Library component on the same GameObject.", source);
                return false;
            }

            int albumCount = data.albums.Count;
            int trackTotal = 0;
            foreach (MVSAlbum album in data.albums) trackTotal += album.tracks.Count;

            var genreNames = new string[data.genres.Count];
            for (int g = 0; g < genreNames.Length; g++) genreNames[g] = data.genres[g].name ?? "";

            var ids = new string[albumCount];
            var titles = new string[albumCount];
            var artists = new string[albumCount];
            var years = new int[albumCount];
            var genres = new int[albumCount];
            var covers = new Texture2D[albumCount];
            var trackStart = new int[albumCount];
            var trackCount = new int[albumCount];
            var trackTitles = new string[trackTotal];
            var trackUrls = new VRCUrl[trackTotal];

            int t = 0;
            for (int a = 0; a < albumCount; a++)
            {
                MVSAlbum album = data.albums[a];
                ids[a] = album.id ?? "";
                titles[a] = album.title ?? "";
                artists[a] = album.artist ?? "";
                years[a] = album.year;
                genres[a] = data.GenreIndex(album.genreId);
                covers[a] = album.cover;
                trackStart[a] = t;
                trackCount[a] = album.tracks.Count;
                foreach (MVSTrack track in album.tracks)
                {
                    trackTitles[t] = track.title ?? "";
                    // Each element gets its own VRCUrl instance (never share VRCUrl.Empty).
                    trackUrls[t] = new VRCUrl(track.url ?? "");
                    t++;
                }
            }

            Undo.RecordObject(library, "Bake MVS Library");
            library.genreNames = genreNames;
            library.albumIds = ids;
            library.albumTitles = titles;
            library.albumArtists = artists;
            library.albumYears = years;
            library.albumGenres = genres;
            library.albumCovers = covers;
            library.albumTrackStart = trackStart;
            library.albumTrackCount = trackCount;
            library.trackTitles = trackTitles;
            library.trackUrls = trackUrls;
            UdonSharpEditorUtility.CopyProxyToUdon(library);
            EditorUtility.SetDirty(library);
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(library.gameObject.scene);

            if (log)
                Debug.Log($"[MVS] Baked '{data.name}' into {library.name}: {albumCount} albums, {trackTotal} tracks, {genreNames.Length} genres.", library);
            return true;
        }
    }

    /// <summary>Bakes all libraries right before the VRChat SDK builds the scene.</summary>
    public class MVSLibraryBuildCallback : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder => -100;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType == VRCSDKRequestedBuildType.Scene)
                MVSLibraryBaker.BakeAllInOpenScenes(true);
            return true;
        }
    }
}
