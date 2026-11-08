using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Components;

namespace MeijiVinylSystem.Editor
{
    /// <summary>Builds the package's own record mesh, material and prefab (no third-party art).</summary>
    public static class MVSAssetBuilder
    {
        public const string ArtRoot = MVSInfo.PackageRoot + "/Runtime/Art";
        public const string MeshPath = ArtRoot + "/Meshes/MVS_VinylRecord.asset";
        public const string MaterialPath = ArtRoot + "/Materials/MVS_VinylRecord.mat";
        public const string ShaderPath = ArtRoot + "/Shaders/MVSVinylRecord.shader";
        public const string RecordPrefabPath = MVSInfo.PackageRoot + "/Prefabs/MVS Record.prefab";

        // 12" record
        private const float Radius = 0.1524f;
        private const float Thickness = 0.002f;
        private const int Segments = 96;

        [MenuItem("Tools/Meiji Vinyl System/Dev/Rebuild Record Assets", priority = 300)]
        public static void BuildAll()
        {
            Mesh mesh = BuildMesh();
            Material mat = BuildMaterial();
            BuildRecordPrefab(mesh, mat);
            AssetDatabase.SaveAssets();
            Debug.Log("[MVS] Record mesh, material and prefab rebuilt.");
        }

        public static Mesh BuildMesh()
        {
            EnsureFolder(ArtRoot + "/Meshes");
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            float h = Thickness * 0.5f;

            // Caps (top +Y, bottom -Y). Planar UVs: center 0.5, rim at 0/1. Bottom mirrored so the label reads correctly.
            for (int cap = 0; cap < 2; cap++)
            {
                float y = cap == 0 ? h : -h;
                Vector3 n = cap == 0 ? Vector3.up : Vector3.down;
                int center = verts.Count;
                verts.Add(new Vector3(0, y, 0)); normals.Add(n); uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i <= Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    float x = Mathf.Cos(a), z = Mathf.Sin(a);
                    verts.Add(new Vector3(x * Radius, y, z * Radius));
                    normals.Add(n);
                    uvs.Add(new Vector2(0.5f + (cap == 0 ? x : -x) * 0.5f, 0.5f + z * 0.5f));
                }
                for (int i = 0; i < Segments; i++)
                {
                    int a = center + 1 + i, b = center + 2 + i;
                    if (cap == 0) { tris.Add(center); tris.Add(b); tris.Add(a); }
                    else { tris.Add(center); tris.Add(a); tris.Add(b); }
                }
            }
            // Edge: UVs at the very rim (plain vinyl color).
            int edge = verts.Count;
            for (int i = 0; i <= Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts.Add(dir * Radius + Vector3.up * h); normals.Add(dir); uvs.Add(new Vector2(0.5f + dir.x * 0.499f, 0.5f + dir.z * 0.499f));
                verts.Add(dir * Radius - Vector3.up * h); normals.Add(dir); uvs.Add(new Vector2(0.5f + dir.x * 0.499f, 0.5f + dir.z * 0.499f));
            }
            for (int i = 0; i < Segments; i++)
            {
                int t0 = edge + i * 2, b0 = t0 + 1, t1 = t0 + 2, b1 = t0 + 3;
                tris.Add(t0); tris.Add(t1); tris.Add(b0);
                tris.Add(b0); tris.Add(t1); tris.Add(b1);
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "MVS_VinylRecord";
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            if (isNew) AssetDatabase.CreateAsset(mesh, MeshPath);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }

        public static Material BuildMaterial()
        {
            EnsureFolder(ArtRoot + "/Materials");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (mat == null)
            {
                mat = new Material(shader) { name = "MVS_VinylRecord", enableInstancing = true };
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            else if (mat.shader != shader) mat.shader = shader;
            return mat;
        }

        public static GameObject BuildRecordPrefab(Mesh mesh, Material mat)
        {
            EnsureFolder(MVSInfo.PackageRoot + "/Prefabs");
            var go = new GameObject("MVS Record");
            try
            {
                int pickupLayer = LayerMask.NameToLayer("Pickup");
                if (pickupLayer >= 0) go.layer = pickupLayer;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(Radius * 2f, 0.01f, Radius * 2f);
                box.isTrigger = true;
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
                var pickup = go.AddComponent<VRCPickup>();
                pickup.orientation = VRC.SDKBase.VRC_Pickup.PickupOrientation.Any;
                pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
                pickup.InteractionText = "Record";
                pickup.proximity = 1f;
                var sync = go.AddComponent<VRCObjectSync>();
                sync.AllowCollisionOwnershipTransfer = false;
                var disc = UdonSharpUndo.AddComponent<MVSDisc>(go);
                using (var so = new SerializedObject(disc))
                {
                    so.FindProperty("coverRenderer").objectReferenceValue = mr;
                    so.FindProperty("pickup").objectReferenceValue = pickup;
                    so.FindProperty("objectSync").objectReferenceValue = sync;
                    so.FindProperty("discCollider").objectReferenceValue = box;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                UdonSharpEditorUtility.CopyProxyToUdon(disc);
                return PrefabUtility.SaveAsPrefabAsset(go, RecordPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
