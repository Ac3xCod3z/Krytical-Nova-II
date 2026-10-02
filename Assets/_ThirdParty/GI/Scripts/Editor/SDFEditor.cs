using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace GI
{
    [InitializeOnLoad]
    public static class SDFEditorSelectionListener
    {
        static SDFEditorSelectionListener()
        {
            Selection.selectionChanged += OnSelectionChanged;
        }

        public static void OnSelectionChanged()
        {
            var atlas = GameObject.FindObjectOfType<SDFAtlas>();

            if (atlas == null)
                return;

            atlas.ClearSelection();

            foreach (var obj in Selection.gameObjects)
            {
                var sdf = obj.GetComponent<SDF>();

                if (sdf != null && sdf.PreviewWhenSelected)
                    atlas.AddSelection(sdf);
            }
        }
    }

    [CustomEditor(typeof(SDF))]
    [CanEditMultipleObjects]
    public class SDFEditor : Editor
    {
        public SDFEditor()
        {
        }

        public static void Bake(SDF sdf)
        {
            var entry = Baker.Instance.Bake(sdf);
            EditorUtility.SetDirty(entry);

            Undo.RecordObject(sdf, "Bake");
            sdf.Entry = entry;

            var asset_path = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;

            if (asset_path == null || asset_path.Length == 0)
            {
                var current = sdf.gameObject;

                while (current != null)
                {
                    asset_path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(current);

                    if (asset_path != null && asset_path.Length > 0)
                        break;

                    asset_path = AssetDatabase.GetAssetPath(current);

                    if (asset_path != null && asset_path.Length > 0)
                        break;

                    if (current.transform.parent != null)
                        current = current.transform.parent.gameObject;
                    else
                        current = null;
                }

                if (asset_path == null || asset_path.Length == 0)
                    asset_path = sdf.gameObject.scene.path;
            }

            if (asset_path == null || asset_path.Length == 0)
            {
                EditorUtility.DisplayDialog("Path Error", "No valid path to save the SDF was found", "OK");
                return;
            }

            var directory_path = System.IO.Path.GetDirectoryName(asset_path);
            var sub_folder_path = System.IO.Path.Combine(directory_path, "SDF").Replace("\\", "/");

            if (!AssetDatabase.IsValidFolder(sub_folder_path))
                AssetDatabase.CreateFolder(directory_path, "SDF");

            if (sdf.UniqueID == null || sdf.UniqueID.Trim().Length == 0)
                sdf.UniqueID = System.Guid.NewGuid().ToString();

            var sdf_path = System.IO.Path.ChangeExtension(sdf.UniqueID, ".asset");
            sdf_path = System.IO.Path.Combine(sub_folder_path, sdf_path).Replace("\\", "/");

            var asset = AssetDatabase.LoadAssetAtPath<SDFEntry>(sdf_path);

            if (asset != null)
            {
                asset.Overwrite(entry);
                sdf.Entry = asset;

                EditorUtility.SetDirty(asset);
            }
            else
                AssetDatabase.CreateAsset(entry, sdf_path);
        }

        public override void OnInspectorGUI()
        {
            var any_auto = false;

            foreach (var obj in Selection.gameObjects)
            {
                var sdf = obj.GetComponent<SDF>();

                if (sdf != null && sdf.IsAuto)
                {
                    any_auto = true;
                    break;
                }
            }

            if (any_auto)
            {
                if (Selection.gameObjects.Length == 1)
                {
                    var sdf = Selection.gameObjects[0].GetComponent<SDF>();

                    if (sdf != null)
                    {
                        GUILayout.Label("Resolution: " + sdf.Resolution);
                        GUILayout.Label("Bias: " + sdf.Bias);
                        GUILayout.Label("Force Double Sided: " + sdf.ForceDoubleSided);
                    }
                }

                if (GUILayout.Button("Make Permanent (Remove auto marker)"))
                {
                    foreach (var obj in Selection.gameObjects)
                    {
                        var sdf = obj.GetComponent<SDF>();

                        if (sdf != null && sdf.IsAuto)
                        {
                            Undo.RecordObject(sdf, "Remove Auto");
                            sdf.RemoveAuto();
                        }
                    }
                }
            }
            else
            {
                base.OnInspectorGUI();

                SDFEditorSelectionListener.OnSelectionChanged();

                if (Selection.gameObjects.Length == 1)
                {
                    var sdf = Selection.gameObjects[0].GetComponent<SDF>();

                    if (sdf != null)
                    {
                        Undo.RecordObject(sdf, "ID");

                        sdf.UniqueID = EditorGUILayout.TextField("Bake ID", sdf.UniqueID);

                        if (GUILayout.Button("New Bake ID"))
                        {
                            sdf.RemoveAuto();
                            sdf.UniqueID = System.Guid.NewGuid().ToString();
                        }
                    }
                }
                else if (Selection.gameObjects.Length > 1)
                {
                    if (GUILayout.Button("New Bake IDs"))
                    {
                        foreach (var obj in Selection.gameObjects)
                        {
                            var sdf = obj.GetComponent<SDF>();

                            if (sdf != null)
                            {
                                Undo.RecordObject(sdf, "ID");

                                sdf.RemoveAuto();
                                sdf.UniqueID = System.Guid.NewGuid().ToString();
                            }
                        }
                    }
                }

                if (GUILayout.Button("Bake"))
                {
                    var baker = Baker.Instance;

                    foreach (var obj in Selection.gameObjects)
                    {
                        var sdf = obj.GetComponent<SDF>();

                        if (sdf != null)
                        {
                            Bake(sdf);
                            sdf.RemoveAuto();

                            AssetDatabase.SaveAssets();
                            AssetDatabase.Refresh();
                        }
                    }
                }
            }
        }
    }
}