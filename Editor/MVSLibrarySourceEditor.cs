using UnityEditor;
using UnityEngine;

namespace MeijiVinylSystem.Editor
{
    [CustomEditor(typeof(MVSLibrarySource))]
    public class MVSLibrarySourceEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var source = (MVSLibrarySource)target;
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("data"), new GUIContent("Library Asset"));
            serializedObject.ApplyModifiedProperties();

            if (source.GetComponent<MVSLibrary>() == null)
                EditorGUILayout.HelpBox("Add an MVS Library (UdonSharp) component to this GameObject.", MessageType.Error);
            else if (source.data == null)
                EditorGUILayout.HelpBox("Assign a library asset (Create > Meiji Vinyl System > Library).", MessageType.Warning);
            else
            {
                var library = source.GetComponent<MVSLibrary>();
                EditorGUILayout.HelpBox(
                    $"Asset: {source.data.albums.Count} albums · Baked: {library.albumIds.Length} albums, {library.trackUrls.Length} tracks.\n" +
                    "Bakes automatically before Play Mode and VRChat builds.",
                    library.albumIds.Length == source.data.albums.Count ? MessageType.Info : MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(source.data == null))
                {
                    if (GUILayout.Button("Open Library Editor")) MVSLibraryWindow.Open(source.data);
                    if (GUILayout.Button("Bake Now")) MVSLibraryBaker.Bake(source);
                }
            }
        }
    }
}
