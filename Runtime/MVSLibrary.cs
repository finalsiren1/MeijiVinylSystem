using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Read-only album library for Udon. All arrays are baked in the editor from an
    /// <see cref="MVSLibraryData"/> asset (via <see cref="MVSLibrarySource"/>); nothing here is synced.
    /// Albums are addressed by index (0..AlbumCount-1); use <see cref="FindAlbum"/> to resolve a stable id.
    /// Tracks are stored flattened: album <c>a</c> owns tracks
    /// <c>albumTrackStart[a] .. albumTrackStart[a] + albumTrackCount[a] - 1</c>.
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class MVSLibrary : UdonSharpBehaviour
    {
        // The fields below are written by the editor baker and must persist in the scene,
        // so they are public + [HideInInspector] (hidden to prevent hand-editing, still serialized).

        [HideInInspector] public string[] genreNames = new string[0];

        [HideInInspector] public string[] albumIds = new string[0];
        [HideInInspector] public string[] albumTitles = new string[0];
        [HideInInspector] public string[] albumArtists = new string[0];
        [HideInInspector] public int[] albumYears = new int[0];
        [HideInInspector] public int[] albumGenres = new int[0];
        [HideInInspector] public Texture2D[] albumCovers = new Texture2D[0];
        [HideInInspector] public int[] albumTrackStart = new int[0];
        [HideInInspector] public int[] albumTrackCount = new int[0];

        [HideInInspector] public string[] trackTitles = new string[0];
        [HideInInspector] public VRCUrl[] trackUrls = new VRCUrl[0];

        // ---- Counts ---------------------------------------------------------------------------

        public int GetAlbumCount() { return albumIds.Length; }

        public int GetGenreCount() { return genreNames.Length; }

        public bool IsValidAlbum(int album) { return album >= 0 && album < albumIds.Length; }

        // ---- Lookup ---------------------------------------------------------------------------

        /// <summary>Index of the album with this stable id, or -1.</summary>
        public int FindAlbum(string albumId)
        {
            if (string.IsNullOrEmpty(albumId)) return -1;
            for (int i = 0; i < albumIds.Length; i++)
                if (albumIds[i] == albumId) return i;
            return -1;
        }

        public string GetAlbumId(int album) { return IsValidAlbum(album) ? albumIds[album] : ""; }

        public string GetTitle(int album) { return IsValidAlbum(album) ? albumTitles[album] : ""; }

        public string GetArtist(int album) { return IsValidAlbum(album) ? albumArtists[album] : ""; }

        /// <summary>Release year, 0 when unknown.</summary>
        public int GetYear(int album) { return IsValidAlbum(album) ? albumYears[album] : 0; }

        /// <summary>Genre index, -1 when unsorted.</summary>
        public int GetGenre(int album) { return IsValidAlbum(album) ? albumGenres[album] : -1; }

        public string GetGenreName(int genre)
        {
            return genre >= 0 && genre < genreNames.Length ? genreNames[genre] : "";
        }

        public Texture2D GetCover(int album) { return IsValidAlbum(album) ? albumCovers[album] : null; }

        /// <summary>"Artist - Title", or just the title when the artist is empty.</summary>
        public string GetDisplayName(int album)
        {
            if (!IsValidAlbum(album)) return "";
            string artist = albumArtists[album];
            if (string.IsNullOrEmpty(artist)) return albumTitles[album];
            return artist + " - " + albumTitles[album];
        }

        // ---- Tracks ---------------------------------------------------------------------------

        public int GetTrackCount(int album) { return IsValidAlbum(album) ? albumTrackCount[album] : 0; }

        public bool IsMultiTrack(int album) { return GetTrackCount(album) > 1; }

        public string GetTrackTitle(int album, int track)
        {
            int index = TrackIndex(album, track);
            return index < 0 ? "" : trackTitles[index];
        }

        public VRCUrl GetTrackUrl(int album, int track)
        {
            int index = TrackIndex(album, track);
            return index < 0 ? null : trackUrls[index];
        }

        private int TrackIndex(int album, int track)
        {
            if (!IsValidAlbum(album)) return -1;
            if (track < 0 || track >= albumTrackCount[album]) return -1;
            return albumTrackStart[album] + track;
        }

        // ---- Search ---------------------------------------------------------------------------

        /// <summary>
        /// True when the album matches the genre filter (-1 = any) and the query
        /// (case-insensitive substring of title, artist or year). Pass the query already lower-cased.
        /// </summary>
        public bool Matches(int album, string lowerQuery, int genreFilter)
        {
            if (!IsValidAlbum(album)) return false;
            if (genreFilter >= 0 && albumGenres[album] != genreFilter) return false;
            if (string.IsNullOrEmpty(lowerQuery)) return true;
            if (albumTitles[album].ToLower().Contains(lowerQuery)) return true;
            if (albumArtists[album].ToLower().Contains(lowerQuery)) return true;
            return albumYears[album] > 0 && albumYears[album].ToString().Contains(lowerQuery);
        }
    }
}
