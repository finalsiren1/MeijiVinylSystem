using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MeijiVinylSystem.Editor
{
    /// <summary>
    /// Packs every album cover of a library into a few square-cell atlas pages (PNG, 4096 wide), so the world
    /// loads a handful of compressed textures instead of hundreds of originals. Source textures are only read
    /// through the GPU (no import settings are changed). Pages are written next to the library asset in
    /// "&lt;LibraryName&gt;_Covers/". Rebuilds only when a cover, the order or the cell size changed.
    /// </summary>
    public static class MVSCoverAtlasBuilder
    {
        public const int PageWidth = 4096;
        /// <summary>Pixels cropped from each cover edge inside its cell, so mipmaps don't bleed neighbours.</summary>
        private const int Inset = 2;

        public static string PagesFolder(MVSLibraryData data)
        {
            string path = AssetDatabase.GetAssetPath(data);
            return Path.GetDirectoryName(path).Replace('\\', '/') + "/" + data.name + "_Covers";
        }

        public static string ComputeHash(MVSLibraryData data)
        {
            var sb = new StringBuilder();
            sb.Append(data.coverSize).Append('|').Append(PageWidth).Append('|').Append(Inset).Append('|');
            foreach (MVSAlbum a in data.albums)
            {
                if (a.cover == null) { sb.Append("-;"); continue; }
                string p = AssetDatabase.GetAssetPath(a.cover);
                sb.Append(AssetDatabase.AssetPathToGUID(p)).Append(':').Append(AssetDatabase.GetAssetDependencyHash(p)).Append(';');
            }
            using (var md5 = MD5.Create())
            {
                byte[] h = md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                var hex = new StringBuilder();
                foreach (byte b in h) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        public static bool IsUpToDate(MVSLibraryData data)
        {
            if (data.coverAtlasHash != ComputeHash(data)) return false;
            foreach (Texture2D page in data.coverPages) if (page == null) return false;
            return true;
        }

        /// <summary>Rebuilds the atlas if needed (or always with force). Returns true if pages were written.</summary>
        public static bool Build(MVSLibraryData data, bool force = false)
        {
            if (data == null) return false;
            if (!force && IsUpToDate(data)) return false;

            int cell = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(64, data.coverSize)), 64, 1024);
            int perRow = PageWidth / cell;
            int perPage = perRow * perRow;

            var withCover = new List<MVSAlbum>();
            foreach (MVSAlbum a in data.albums)
            {
                if (a.cover != null) withCover.Add(a);
                else { a.coverPage = -1; a.coverRect = Vector4.zero; }
            }

            string folder = PagesFolder(data);
            MVSAssetBuilder.EnsureFolder(folder);
            int pageCount = Mathf.CeilToInt(withCover.Count / (float)perPage);
            var pages = new List<Texture2D>();

            try
            {
                for (int p = 0; p < pageCount; p++)
                {
                    EditorUtility.DisplayProgressBar("MVS Cover Atlas", $"Page {p + 1}/{pageCount}", p / (float)Mathf.Max(1, pageCount));
                    int first = p * perPage;
                    int count = Mathf.Min(perPage, withCover.Count - first);
                    int rows = Mathf.CeilToInt(count / (float)perRow);
                    int height = Mathf.NextPowerOfTwo(rows * cell);

                    string file = $"{folder}/{data.name}_Covers_{p}.png";
                    WritePage(withCover, first, count, perRow, cell, height, file);
                    ConfigureImporter(file);
                    pages.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(file));

                    for (int i = 0; i < count; i++)
                    {
                        int col = i % perRow, row = i / perRow;
                        // Row 0 is at the TOP of the image; texture UV origin is bottom-left.
                        float x = (col * cell + Inset) / (float)PageWidth;
                        float y = (height - (row + 1) * cell + Inset) / (float)height;
                        float w = (cell - Inset * 2) / (float)PageWidth;
                        float h = (cell - Inset * 2) / (float)height;
                        MVSAlbum a = withCover[first + i];
                        a.coverPage = p;
                        a.coverRect = new Vector4(x, y, w, h);
                    }
                }
                // Remove stale pages from a previous, larger build.
                for (int p = pageCount; ; p++)
                {
                    string stale = $"{folder}/{data.name}_Covers_{p}.png";
                    if (!File.Exists(stale)) break;
                    AssetDatabase.DeleteAsset(stale);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Undo.RecordObject(data, "Build Cover Atlas");
            data.coverSize = cell;
            data.coverPages = pages;
            data.coverAtlasHash = ComputeHash(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MVS] Cover atlas for '{data.name}': {withCover.Count} covers, {pageCount} page(s), {cell}px cells → {folder}", data);
            return true;
        }

        private static void WritePage(List<MVSAlbum> albums, int first, int count, int perRow, int cell, int height, string file)
        {
            var rt = RenderTexture.GetTemporary(PageWidth, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, PageWidth, height, 0); // y = 0 at the top
            for (int i = 0; i < count; i++)
            {
                Texture2D src = albums[first + i].cover;
                int col = i % perRow, row = i / perRow;
                // Center-crop non-square covers.
                float aspect = src.width / (float)src.height;
                Rect srcRect = aspect > 1f
                    ? new Rect((1f - 1f / aspect) * 0.5f, 0f, 1f / aspect, 1f)
                    : new Rect(0f, (1f - aspect) * 0.5f, 1f, aspect);
                Graphics.DrawTexture(new Rect(col * cell, row * cell, cell, cell), src, srcRect, 0, 0, 0, 0);
            }
            GL.PopMatrix();

            var tex = new Texture2D(PageWidth, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, PageWidth, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            File.WriteAllBytes(file, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceUpdate);
        }

        private static void ConfigureImporter(string file)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(file);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.alphaSource = TextureImporterAlphaSource.None;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 2;
            imp.maxTextureSize = PageWidth;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.crunchedCompression = false;
            foreach (string platform in new[] { "Android", "iPhone" })
            {
                var s = imp.GetPlatformTextureSettings(platform);
                s.overridden = true;
                s.maxTextureSize = PageWidth;
                s.format = TextureImporterFormat.ASTC_6x6;
                imp.SetPlatformTextureSettings(s);
            }
            imp.SaveAndReimport();
        }
    }
}
