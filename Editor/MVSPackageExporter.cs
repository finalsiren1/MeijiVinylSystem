using System.IO;
using UnityEditor;
using UnityEngine;

namespace MeijiVinylSystem.Editor
{
    /// <summary>Exports Assets/MeijiVinylSystem as a .unitypackage into &lt;ProjectRoot&gt;/Exports.</summary>
    public static class MVSPackageExporter
    {
        private const string MenuRoot = "Tools/Meiji Vinyl System/";

        [MenuItem(MenuRoot + "Export .unitypackage", priority = 100)]
        public static void Export()
        {
            if (!AssetDatabase.IsValidFolder(MVSInfo.PackageRoot))
            {
                Debug.LogError($"[MVS] Package folder not found: {MVSInfo.PackageRoot}");
                return;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string exportDir = Path.Combine(projectRoot, "Exports");
            Directory.CreateDirectory(exportDir);

            string fileName = $"{MVSInfo.PackageName}_v{MVSInfo.Version}.unitypackage";
            string outPath = Path.Combine(exportDir, fileName);

            if (File.Exists(outPath) &&
                !EditorUtility.DisplayDialog(MVSInfo.DisplayName,
                    $"{fileName} already exists.\nOverwrite it?", "Overwrite", "Cancel"))
            {
                return;
            }

            // Only the package folder; dependencies (SDK, UdonSharp, VizVid) are installed via VCC.
            AssetDatabase.ExportPackage(MVSInfo.PackageRoot, outPath, ExportPackageOptions.Recurse);
            Debug.Log($"[MVS] Exported {fileName} → {outPath}");
            EditorUtility.RevealInFinder(outPath);
        }

        [MenuItem(MenuRoot + "Open Repository", priority = 200)]
        public static void OpenRepository() => Application.OpenURL(MVSInfo.RepositoryUrl);
    }
}
