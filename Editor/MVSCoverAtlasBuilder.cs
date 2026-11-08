using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace MeijiVinylSystem.Editor
{
    /// <summary>
    /// Packs album artwork into square-cell atlas pages (PNG, 4096 wide) next to the library asset in
    /// "&lt;LibraryName&gt;_Covers/", in three sets:
    /// <list type="bullet">
    /// <item>Front covers (menu, sleeve fronts) — from the sleeve texture's top-left quarter, else from Cover.</item>
    /// <item>Back covers (sleeve backs) — sleeve top-right quarter; none without a sleeve texture.</item>
    /// <item>Record labels — sleeve bottom-left quarter; none without a sleeve texture.</item>
    /// </list>
    /// Sources are only read through the GPU (no import settings change). Rebuilds only when something changed.
    /// </summary>
    public static class MVSCoverAtlasBuilder
    {
        public const int PageWidth = 4096;
        private const int Inset = 2; // pixels cropped per cell edge so mipmaps don't bleed neighbours

        // Quarter rects (UV, origin bottom-left) of a 2x2 sleeve texture.
        private static readonly Rect FrontQ = new Rect(0f, 0.5f, 0.5f, 0.5f);
        private static readonly Rect BackQ = new Rect(0.5f, 0.5f, 0.5f, 0.5f);
        private static readonly Rect LabelQ = new Rect(0f, 0f, 0.5f, 0.5f);
        private static readonly Rect LabelBQ = new Rect(0.5f, 0f, 0.5f, 0.5f);

        public static string PagesFolder(MVSLibraryData data)
        {
            string path = AssetDatabase.GetAssetPath(data);
            return Path.GetDirectoryName(path).Replace('\\', '/') + "/" + data.name + "_Covers";
        }

        private static string TexKey(Texture2D t)
        {
            if (t == null) return "-";
            string p = AssetDatabase.GetAssetPath(t);
            return AssetDatabase.AssetPathToGUID(p) + ":" + AssetDatabase.GetAssetDependencyHash(p);
        }

        public static string ComputeHash(MVSLibraryData data)
        {
            var sb = new StringBuilder("v2|");
            sb.Append(data.coverSize).Append('|').Append(data.backSize).Append('|').Append(data.labelSize).Append('|').Append(PageWidth).Append('|');
            foreach (MVSAlbum a in data.albums) sb.Append(TexKey(a.cover)).Append(',').Append(TexKey(a.sleeve)).Append(',').Append(a.hasSideB).Append(';');
            using (var md5 = MD5.Create())
            {
                var hex = new StringBuilder();
                foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }

        public static bool IsUpToDate(MVSLibraryData data)
        {
            if (data.coverAtlasHash != ComputeHash(data)) return false;
            foreach (var list in new[] { data.coverPages, data.backPages, data.labelPages, data.labelBPages })
                foreach (Texture2D page in list) if (page == null) return false;
            return true;
        }

        private struct Entry { public MVSAlbum album; public Texture2D tex; public Rect src; }

        /// <summary>Rebuilds the atlases if needed (or always with force). Returns true if pages were written.</summary>
        public static bool Build(MVSLibraryData data, bool force = false)
        {
            if (data == null) return false;
            if (!force && IsUpToDate(data)) return false;

            var fronts = new List<Entry>();
            var backs = new List<Entry>();
            var labels = new List<Entry>();
            var labelsB = new List<Entry>();
            foreach (MVSAlbum a in data.albums)
            {
                a.coverPage = a.backPage = a.labelPage = a.labelBPage = -1;
                a.coverRect = a.backRect = a.labelRect = a.labelBRect = Vector4.zero;
                if (a.sleeve != null)
                {
                    fronts.Add(new Entry { album = a, tex = a.sleeve, src = FrontQ });
                    backs.Add(new Entry { album = a, tex = a.sleeve, src = BackQ });
                    labels.Add(new Entry { album = a, tex = a.sleeve, src = LabelQ });
                    if (a.hasSideB) labelsB.Add(new Entry { album = a, tex = a.sleeve, src = LabelBQ });
                }
                else if (a.cover != null)
                    fronts.Add(new Entry { album = a, tex = a.cover, src = CenterSquare(a.cover) });
            }

            string folder = PagesFolder(data);
            MVSAssetBuilder.EnsureFolder(folder);
            Undo.RecordObject(data, "Build Cover Atlas");
            try
            {
                data.coverSize = Cell(data.coverSize);
                data.backSize = Cell(data.backSize);
                data.labelSize = Cell(data.labelSize);
                data.coverPages = BuildSet(data, fronts, data.coverSize, folder, "Front", 0);
                data.backPages = BuildSet(data, backs, data.backSize, folder, "Back", 1);
                data.labelPages = BuildSet(data, labels, data.labelSize, folder, "Label", 2);
                data.labelBPages = BuildSet(data, labelsB, data.labelSize, folder, "LabelB", 3);
            }
            finally { EditorUtility.ClearProgressBar(); }

            data.coverAtlasHash = ComputeHash(data);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[MVS] Atlases for '{data.name}': {fronts.Count} fronts ({data.coverPages.Count} pages), " +
                      $"{backs.Count} backs ({data.backPages.Count}), {labels.Count} labels ({data.labelPages.Count}) → {folder}", data);
            return true;
        }

        private static int Cell(int size) => Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.Max(64, size)), 64, 1024);

        private static Rect CenterSquare(Texture2D t)
        {
            float aspect = t.width / (float)t.height;
            return aspect > 1f
                ? new Rect((1f - 1f / aspect) * 0.5f, 0f, 1f / aspect, 1f)
                : new Rect(0f, (1f - aspect) * 0.5f, 1f, aspect);
        }

        /// <param name="kind">0 = front, 1 = back, 2 = label (which album fields receive the rects)</param>
        private static List<Texture2D> BuildSet(MVSLibraryData data, List<Entry> entries, int cell, string folder, string tag, int kind)
        {
            int perRow = PageWidth / cell;
            int perPage = perRow * perRow;
            int pageCount = Mathf.CeilToInt(entries.Count / (float)perPage);
            var pages = new List<Texture2D>();
            for (int p = 0; p < pageCount; p++)
            {
                EditorUtility.DisplayProgressBar("MVS Atlas", $"{tag} page {p + 1}/{pageCount}", p / (float)Mathf.Max(1, pageCount));
                int first = p * perPage;
                int count = Mathf.Min(perPage, entries.Count - first);
                int rows = Mathf.CeilToInt(count / (float)perRow);
                int height = Mathf.NextPowerOfTwo(rows * cell);
                string file = $"{folder}/{data.name}_{tag}_{p}.png";
                WritePage(entries, first, count, perRow, cell, height, file);
                ConfigureImporter(file);
                pages.Add(AssetDatabase.LoadAssetAtPath<Texture2D>(file));
                for (int i = 0; i < count; i++)
                {
                    int col = i % perRow, row = i / perRow;
                    var rect = new Vector4(
                        (col * cell + Inset) / (float)PageWidth,
                        (height - (row + 1) * cell + Inset) / (float)height,
                        (cell - Inset * 2) / (float)PageWidth,
                        (cell - Inset * 2) / (float)height);
                    MVSAlbum a = entries[first + i].album;
                    if (kind == 0) { a.coverPage = p; a.coverRect = rect; }
                    else if (kind == 1) { a.backPage = p; a.backRect = rect; }
                    else if (kind == 2) { a.labelPage = p; a.labelRect = rect; }
                    else { a.labelBPage = p; a.labelBRect = rect; }
                }
            }
            // stale pages (and v1 "_Covers_n" pages) of this set
            for (int p = pageCount; ; p++)
            {
                string stale = $"{folder}/{data.name}_{tag}_{p}.png";
                if (!File.Exists(stale)) break;
                AssetDatabase.DeleteAsset(stale);
            }
            if (kind == 0)
                for (int p = 0; ; p++)
                {
                    string old = $"{folder}/{data.name}_Covers_{p}.png";
                    if (!File.Exists(old)) break;
                    AssetDatabase.DeleteAsset(old);
                }
            return pages;
        }

        private static void WritePage(List<Entry> entries, int first, int count, int perRow, int cell, int height, string file)
        {
            var rt = RenderTexture.GetTemporary(PageWidth, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, PageWidth, height, 0); // y = 0 at the top
            for (int i = 0; i < count; i++)
            {
                Entry e = entries[first + i];
                int col = i % perRow, row = i / perRow;
                Graphics.DrawTexture(new Rect(col * cell, row * cell, cell, cell), e.tex, e.src, 0, 0, 0, 0);
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
