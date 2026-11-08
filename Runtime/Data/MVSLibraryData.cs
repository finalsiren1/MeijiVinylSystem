using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeijiVinylSystem
{
    /// <summary>
    /// Editor-side source of truth for a vinyl library (albums, genres, tracks, covers).
    /// Edited with <c>Tools &gt; Meiji Vinyl System &gt; Library Editor</c> and baked into an
    /// <see cref="MVSLibrary"/> component (which Udon can read) through <see cref="MVSLibrarySource"/>.
    /// The asset itself is never referenced at runtime, so it can be shared by several scenes
    /// (e.g. a PC scene and its Quest copy).
    /// </summary>
    [CreateAssetMenu(menuName = "Meiji Vinyl System/Library", fileName = "VinylLibrary", order = 0)]
    public class MVSLibraryData : ScriptableObject
    {
        public List<MVSGenre> genres = new List<MVSGenre>();
        public List<MVSAlbum> albums = new List<MVSAlbum>();

        [Header("Cover Atlas (generated)")]
        [Tooltip("Pixel size of one front cover in the atlas (menu thumbnails, sleeve fronts).")]
        public int coverSize = 256;
        [Tooltip("Pixel size of one back cover (sleeve backs).")]
        public int backSize = 256;
        [Tooltip("Pixel size of one record label (center of the vinyl on the platter).")]
        public int labelSize = 256;
        [Tooltip("Generated front cover atlas pages. Rebuilt automatically when covers change (before Play / Build).")]
        public List<Texture2D> coverPages = new List<Texture2D>();
        public List<Texture2D> backPages = new List<Texture2D>();
        public List<Texture2D> labelPages = new List<Texture2D>();
        [HideInInspector] public string coverAtlasHash = "";

        public MVSGenre FindGenre(string genreId)
        {
            if (string.IsNullOrEmpty(genreId)) return null;
            for (int i = 0; i < genres.Count; i++)
                if (genres[i].id == genreId) return genres[i];
            return null;
        }

        public int GenreIndex(string genreId)
        {
            if (string.IsNullOrEmpty(genreId)) return -1;
            for (int i = 0; i < genres.Count; i++)
                if (genres[i].id == genreId) return i;
            return -1;
        }

        public MVSAlbum FindAlbum(string albumId)
        {
            if (string.IsNullOrEmpty(albumId)) return null;
            for (int i = 0; i < albums.Count; i++)
                if (albums[i].id == albumId) return albums[i];
            return null;
        }

        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);
    }

    [Serializable]
    public class MVSGenre
    {
        [Tooltip("Stable id. Albums reference genres by id, so renaming/reordering is safe.")]
        public string id;
        public string name;
    }

    [Serializable]
    public class MVSAlbum
    {
        [Tooltip("Stable id used by records placed in the scene. Do not change after records reference it.")]
        public string id;
        public string title;
        public string artist;
        [Tooltip("0 = unknown")]
        public int year;
        public string genreId;
        [Tooltip("Front cover image (used when there is no Sleeve Texture).")]
        public Texture2D cover;
        [Tooltip("Optional full sleeve texture laid out 2x2: front cover (top-left), back cover (top-right), " +
                 "record label (bottom-left), vinyl (bottom-right, unused). Overrides Cover when set.")]
        public Texture2D sleeve;
        [HideInInspector] public int coverPage = -1;   // generated: atlas page, -1 = no cover
        [HideInInspector] public Vector4 coverRect;    // generated: atlas UV rect (x, y, width, height)
        [HideInInspector] public int backPage = -1;
        [HideInInspector] public Vector4 backRect;
        [HideInInspector] public int labelPage = -1;
        [HideInInspector] public Vector4 labelRect;
        public List<MVSTrack> tracks = new List<MVSTrack>();
        [Tooltip("Editor-only note (e.g. import warnings). Not baked.")]
        public string note;

        public string DisplayName
        {
            get
            {
                string t = string.IsNullOrEmpty(title) ? "(untitled)" : title;
                return string.IsNullOrEmpty(artist) ? t : artist + " - " + t;
            }
        }
    }

    [Serializable]
    public class MVSTrack
    {
        [Tooltip("Shown on the Now Playing UI for multi-track records. Optional for single-track records.")]
        public string title;
        public string url;
    }
}
