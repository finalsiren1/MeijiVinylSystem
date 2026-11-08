using System.Collections.Generic;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace MeijiVinylSystem.Editor
{
    [CustomEditor(typeof(MVSDiscManager))]
    public class MVSDiscManagerEditor : UnityEditor.Editor
    {
        private int createCount = 20;
        private GameObject recordPrefab;
        private Transform displayRoot;

        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;
            DrawDefaultInspector();

            var manager = (MVSDiscManager)target;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Setup", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (recordPrefab == null) recordPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MVSAssetBuilder.SleevePrefabPath);
                recordPrefab = (GameObject)EditorGUILayout.ObjectField("Record Prefab", recordPrefab, typeof(GameObject), false);
                createCount = EditorGUILayout.IntSlider("Pool Size", createCount, 1, 64);
                using (new EditorGUI.DisabledScope(recordPrefab == null))
                    if (GUILayout.Button("Create Pool Records"))
                        CreatePool(manager, recordPrefab, createCount);
                EditorGUILayout.HelpBox("Replaces the Pool Discs list with new records under a 'Pool' child (old pool children are deleted). Pool records start inactive.", MessageType.None);
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                displayRoot = (Transform)EditorGUILayout.ObjectField("Display Records Root", displayRoot, typeof(Transform), true);
                using (new EditorGUI.DisabledScope(displayRoot == null))
                    if (GUILayout.Button("Collect Display Records"))
                        CollectDisplay(manager, displayRoot);
                EditorGUILayout.HelpBox("Fills Display Discs with every MVS Record under the root that has a Fixed Album Id.", MessageType.None);
            }
        }

        public static void CreatePool(MVSDiscManager manager, GameObject prefab, int count)
        {
            Transform pool = manager.transform.Find("Pool");
            if (pool != null) Undo.DestroyObjectImmediate(pool.gameObject);
            var poolGo = new GameObject("Pool");
            Undo.RegisterCreatedObjectUndo(poolGo, "Create Pool Records");
            poolGo.transform.SetParent(manager.transform, false);

            var discs = new List<MVSDisc>();
            for (int i = 0; i < count; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, poolGo.transform);
                go.name = $"Pool Record {i:00}";
                go.transform.localPosition = new Vector3(0, i * 0.01f, 0);
                go.SetActive(false);
                Undo.RegisterCreatedObjectUndo(go, "Create Pool Records");
                var disc = go.GetComponent<MVSDisc>();
                if (disc != null) discs.Add(disc);
            }
            SetArray(manager, "poolDiscs", discs);
            Debug.Log($"[MVS] Created {discs.Count} pool records.", manager);
        }

        public static void CollectDisplay(MVSDiscManager manager, Transform root)
        {
            var discs = new List<MVSDisc>();
            foreach (var d in root.GetComponentsInChildren<MVSDisc>(true))
                if (!string.IsNullOrEmpty(d.fixedAlbumId)) discs.Add(d);
            SetArray(manager, "displayDiscs", discs);
            Debug.Log($"[MVS] Collected {discs.Count} display records.", manager);
        }

        private static void SetArray(MVSDiscManager manager, string field, List<MVSDisc> discs)
        {
            using (var so = new SerializedObject(manager))
            {
                var prop = so.FindProperty(field);
                prop.arraySize = discs.Count;
                for (int i = 0; i < discs.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = discs[i];
                so.ApplyModifiedProperties();
            }
            UdonSharpEditorUtility.CopyProxyToUdon(manager);
            EditorUtility.SetDirty(manager);
        }
    }
}
