using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GI
{
    [CustomEditor(typeof(SDFAtlas))]
    public class SDFAtlasEditor : Editor
    {
        private struct ShowSettings
        {
            public bool Resolution;
            public bool Thresholds;
        }

        private struct MeshMaterial
        {
            public Mesh Mesh;
            public Material Material;
        }

        private ShowSettings _auto_show;
        private ShowSettings _trees_show;

        private int _prebuilt_index = -1;
        private bool _show_prebuilt = false;

        private GUIContent withTooltip(Type type, string field_name, string text)
        {
            var field = type.GetField(field_name);

            if (field == null)
            {
                Debug.Log("Invalid tooltip field: " + field_name);
                return new GUIContent(text);
            }

            var attributes = field.GetCustomAttributes(typeof(TooltipAttribute), false) as TooltipAttribute[];

            if (attributes.Length > 0)
                return new GUIContent(text, attributes[0].tooltip);
            else
                return new GUIContent(text);
        }

        private GUIContent withTooltip(string field_name, string text)
        {
            var field = typeof(SDFAtlas).GetField(field_name);
            var attributes = field.GetCustomAttributes(typeof(TooltipAttribute), false) as TooltipAttribute[];

            if (attributes.Length > 0)
                return new GUIContent(text, attributes[0].tooltip);
            else
                return new GUIContent(text);
        }

        public override void OnInspectorGUI()
        {
            var atlas = (SDFAtlas)target;

            Undo.RecordObject(atlas, "SDF Atlas");

            GUILayout.BeginVertical("Debug", "window");
            {
                atlas.Debug = (DebugChoice)EditorGUILayout.EnumPopup(withTooltip("Debug", "Show:"), atlas.Debug);
                atlas.DebugMesh = (Mesh)EditorGUILayout.ObjectField(withTooltip("DebugMesh", "Sphere:"), atlas.DebugMesh, typeof(Mesh), true);

                if (GUILayout.Button(new GUIContent("Reset Built", "Cleans up automatically built SDFs. Resets auto instances.")))
                    atlas.ResetBuilt();

                GUILayout.EndVertical();
            }

            EditorGUILayout.Separator();

            GUILayout.BeginVertical("Auto", "window");
            {
                atlas.Auto.Type = (SearchType)EditorGUILayout.EnumPopup(withTooltip(atlas.Auto.GetType(), "Type", "Type:"), atlas.Auto.Type);

                if (atlas.Auto.Type != SearchType.None)
                {
                    atlas.Auto.MaxDistance = EditorGUILayout.FloatField(withTooltip(atlas.Auto.GetType(), "MaxDistance", "Max Distance:"), atlas.Auto.MaxDistance);
                    atlas.Auto.SizeFilter = EditorGUILayout.FloatField(withTooltip(atlas.Auto.GetType(), "SizeFilter", "Size Filter:"), atlas.Auto.SizeFilter);

                    editBuild(ref atlas.Auto.Build, ref _auto_show);

                    atlas.Auto.CheckPerFrame = Mathf.Max(EditorGUILayout.IntField(withTooltip(atlas.Auto.GetType(), "CheckPerFrame", "Check Per Frame"), atlas.Auto.CheckPerFrame), 0);

                    if (GUILayout.Button(new GUIContent("Debug Reset", "Destroys all automatic SDF component instances for tracked meshes")))
                        atlas.ResetAuto();
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Separator();

            GUILayout.BeginVertical("Trees", "window");
            {
                atlas.Trees.Type = (SearchType)EditorGUILayout.EnumPopup(withTooltip(atlas.Trees.GetType(), "Type", "Type:"), atlas.Trees.Type);

                if (atlas.Trees.Type != SearchType.None)
                {
                    atlas.Trees.MaxDistance = EditorGUILayout.FloatField(withTooltip(atlas.Trees.GetType(), "MaxDistance", "Max Distance:"), atlas.Trees.MaxDistance);

                    editBuild(ref atlas.Trees.Build, ref _trees_show);

                    atlas.Trees.CheckPerFrame = Mathf.Max(EditorGUILayout.IntField(withTooltip(atlas.Trees.GetType(), "CheckPerFrame", "Check Per Frame"), atlas.Trees.CheckPerFrame), 0);

                    if (GUILayout.Button(new GUIContent("Debug Reset", "Destroys all automatic SDF component instances for tracked trees")))
                        atlas.ResetTrees();
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Separator();

            GUILayout.BeginVertical("Terrain", "window");
            {
                atlas.Terrain.Terrain = (GameObject)EditorGUILayout.ObjectField(withTooltip(atlas.Terrain.GetType(), "Terrain", "Terrain:"), atlas.Terrain.Terrain, typeof(GameObject), true);

                if (atlas.Terrain.Terrain == null)
                {
                    atlas.Terrain.AutoFind = EditorGUILayout.Toggle(withTooltip(atlas.Terrain.GetType(), "AutoFind", "Auto Find:"), atlas.Terrain.AutoFind);

                    if (GUILayout.Button(new GUIContent("Find Now", "Manually find a Terrain instance in the scene")))
                    {
                        Undo.RecordObject(atlas, "Force Find Terrain");

                        if (atlas.Terrain == null)
                            atlas.Terrain = new SDFTerrain();

                        atlas.Terrain.Terrain = atlas.Terrain.Find(Vector3.zero);

                        if (atlas.Terrain.Terrain != null)
                        {
                            var terrain = atlas.Terrain.Terrain.GetComponent<Terrain>();

                            atlas.Terrain.Copy(terrain);
                        }
                    }
                }

                atlas.Terrain.ColorMap = (Texture)EditorGUILayout.ObjectField(withTooltip(atlas.Terrain.GetType(), "ColorMap", "Color Map:"), atlas.Terrain.ColorMap, typeof(Texture), true);

                if (atlas.Terrain.ColorMap == null)
                    atlas.Terrain.Color = EditorGUILayout.ColorField(withTooltip(atlas.Terrain.GetType(), "Color", "Color:"), atlas.Terrain.Color);

                if (atlas.Terrain.Terrain == null)
                {
                    EditorGUILayout.Separator();

                    GUILayout.BeginVertical("Manual", "window");
                    {
                        atlas.Terrain.HeightMap = (Texture)EditorGUILayout.ObjectField(withTooltip(atlas.Terrain.GetType(), "HeightMap", "Height Map:"), atlas.Terrain.HeightMap, typeof(Texture), true);
                        atlas.Terrain.NormalMap = (Texture)EditorGUILayout.ObjectField(withTooltip(atlas.Terrain.GetType(), "NormalMap", "Normal Map:"), atlas.Terrain.NormalMap, typeof(Texture), true);

                        atlas.Terrain.Position = EditorGUILayout.Vector3Field(withTooltip(atlas.Terrain.GetType(), "Position", "Position:"), atlas.Terrain.Position);
                        atlas.Terrain.Scale = EditorGUILayout.Vector3Field(withTooltip(atlas.Terrain.GetType(), "Scale", "Scale:"), atlas.Terrain.Scale);

                        atlas.Terrain.Bounds.min = EditorGUILayout.Vector3Field(withTooltip(atlas.Terrain.GetType(), "Bounds", "Bounds Min:"), atlas.Terrain.Bounds.min);
                        atlas.Terrain.Bounds.max = EditorGUILayout.Vector3Field(withTooltip(atlas.Terrain.GetType(), "Bounds", "Bounds Max:"), atlas.Terrain.Bounds.max);

                        GUILayout.EndVertical();
                    }
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Separator();

            GUILayout.BeginVertical("SDFs In Scene", "window");
            {
                if (GUILayout.Button(new GUIContent("Find And Create New", "Create an SDF component for every found mesh in the scene")))
                {
                    foreach (var mesh in FindObjectsOfType<MeshRenderer>())
                    {
                        var sdf = mesh.gameObject.GetComponent<SDF>();

                        if (sdf == null)
                            sdf = mesh.gameObject.GetComponentInParent<SDF>();

                        if (sdf == null)
                        {
                            Undo.RegisterCompleteObjectUndo(mesh.gameObject, "Create SDF");

                            sdf = mesh.gameObject.AddComponent<SDF>();
                            SDFEditor.Bake(sdf);
                        }
                        else if (sdf.IsAuto)
                        {
                            Undo.RecordObject(sdf, "Remove Auto");
                            sdf.RemoveAuto();
                        }
                    }

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                if (GUILayout.Button(new GUIContent("Rebuild All", "Find every SDF component in the scene and rebuild it")))
                {
                    foreach (var sdf in FindObjectsOfType<SDF>())
                    {
                        if (sdf.IsAuto)
                        {
                            Undo.RecordObject(sdf, "Remove Auto");
                            sdf.RemoveAuto();
                        }

                        SDFEditor.Bake(sdf);
                    }

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }

                GUILayout.EndVertical();
            }

            EditorGUILayout.Separator();

            GUILayout.BeginVertical("Prebuilt", "window");
            {
                GUILayout.BeginHorizontal();
                EditorGUILayout.Space(12, false);
                _show_prebuilt = EditorGUILayout.Foldout(_show_prebuilt, "Expand (" + atlas.Prebuilt.Count + ")", true);
                GUILayout.EndHorizontal();

                if (_show_prebuilt)
                {
                    var to_remove = -1;
                    var background_color = GUI.backgroundColor;

                    for (int i = 0; i < atlas.Prebuilt.Count; i++)
                    {
                        var prebuilt = atlas.Prebuilt[i];

                        GUILayout.BeginVertical(EditorStyles.helpBox);
                        {
                            GUILayout.BeginHorizontal();
                            {
                                prebuilt.Mesh = (Mesh)EditorGUILayout.ObjectField(withTooltip(prebuilt.GetType(), "Mesh", "Mesh:"), prebuilt.Mesh, typeof(Mesh), true);

                                GUI.backgroundColor = Color.red;

                                if (GUILayout.Button("X", GUILayout.ExpandWidth(false)))
                                    to_remove = i;

                                GUI.backgroundColor = background_color;

                                GUILayout.EndHorizontal();
                            }

                            prebuilt.Material = (Material)EditorGUILayout.ObjectField(withTooltip(prebuilt.GetType(), "Material", "Material:"), prebuilt.Material, typeof(Material), true);
                            prebuilt.Entry = (SDFEntry)EditorGUILayout.ObjectField(withTooltip(prebuilt.GetType(), "Entry", "SDF:"), prebuilt.Entry, typeof(SDFEntry), true);

                            GUILayout.BeginHorizontal();
                            EditorGUILayout.Space(12, false);
                            var show = EditorGUILayout.BeginFoldoutHeaderGroup(_prebuilt_index == i, "Build");
                            GUILayout.EndHorizontal();

                            if (show)
                            {
                                _prebuilt_index = i;

                                prebuilt.Resolution = Mathf.Clamp(EditorGUILayout.IntField(withTooltip(prebuilt.GetType(), "Resolution", "Resolution:"), prebuilt.Resolution), 8, 64);
                                prebuilt.Bias = Mathf.Clamp(EditorGUILayout.FloatField(withTooltip(prebuilt.GetType(), "Bias", "Bias:"), prebuilt.Bias), -1, 1);
                                prebuilt.ForceDoubleSided = EditorGUILayout.Toggle(withTooltip(prebuilt.GetType(), "ForceDoubleSided", "Force Double Sided:"), prebuilt.ForceDoubleSided);

                                if (prebuilt.Mesh != null && prebuilt.Material != null)
                                {
                                    if (GUILayout.Button(new GUIContent("Bake", "Bake the SDF")))
                                        prebuilt.Entry = Baker.Instance.Bake(prebuilt.Mesh, prebuilt.Material, prebuilt.Resolution, prebuilt.Bias, prebuilt.ForceDoubleSided);
                                }
                            }
                            else if (_prebuilt_index == i)
                                _prebuilt_index = -1;

                            EditorGUILayout.EndFoldoutHeaderGroup();

                            GUILayout.EndVertical();
                        }

                        atlas.Prebuilt[i] = prebuilt;
                    }

                    if (to_remove >= 0)
                        atlas.Prebuilt.RemoveAt(to_remove);

                    EditorGUILayout.Separator();
                }

                if (atlas.Prebuilt.Count > 0 && _show_prebuilt)
                    GUILayout.BeginHorizontal();

                if (GUILayout.Button(new GUIContent("Add", "Add new prebuilt SDF")))
                {
                    var prebuilt = new SDFAtlas.PrebuiltSDF();
                    prebuilt.Resolution = 32;
                    prebuilt.Bias = 0;
                    prebuilt.ForceDoubleSided = false;

                    atlas.Prebuilt.Add(prebuilt);

                    _show_prebuilt = true;
                }

                if (atlas.Prebuilt.Count > 0 && _show_prebuilt)
                {
                    if (GUILayout.Button(new GUIContent("Copy Last", "Add new prebuilt SDF as a copy of the last one in the list")))
                    {
                        atlas.Prebuilt.Add(atlas.Prebuilt[atlas.Prebuilt.Count - 1]);

                        _show_prebuilt = true;
                    }

                    GUILayout.EndHorizontal();
                }

                if (atlas.Prebuilt.Count == 0 || _show_prebuilt)
                    GUILayout.Label("Prebuilt:");

                if (atlas.Prebuilt.Count > 0 && _show_prebuilt)
                {
                    if (GUILayout.Button(new GUIContent("Copy To Matching In Scene", "Copy prebuilt SDF to every matching SDF component found in the scene")))
                    {
                        var mesh_map = new Dictionary<MeshMaterial, SDFEntry>();

                        for (int i = 0; i < atlas.Prebuilt.Count; i++)
                        {
                            var prebuilt = atlas.Prebuilt[i];

                            if (prebuilt.Entry == null)
                                continue;

                            if (prebuilt.Mesh != null && prebuilt.Material != null)
                            {
                                var key = new MeshMaterial();
                                key.Mesh = prebuilt.Mesh;
                                key.Material = prebuilt.Material;

                                mesh_map[key] = prebuilt.Entry;
                            }
                        }

                        foreach (var renderer in FindObjectsOfType<MeshRenderer>())
                        {
                            if (renderer.sharedMaterials.Length == 0)
                                continue;

                            var filter = renderer.GetComponent<MeshFilter>();

                            if (filter == null)
                                continue;

                            if (filter.sharedMesh == null)
                                continue;

                            var sdf = renderer.GetComponent<SDF>();

                            if (sdf == null)
                                continue;

                            var key = new MeshMaterial();
                            key.Mesh = filter.sharedMesh;
                            key.Material = renderer.sharedMaterial;

                            if (mesh_map.ContainsKey(key))
                            {
                                Undo.RecordObject(sdf, "Assign SDF");
                                sdf.Entry = mesh_map[key];
                            }
                        }
                    }
                }

                if (atlas.Prebuilt.Count == 0 || _show_prebuilt)
                {
                    if (GUILayout.Button(new GUIContent("Find In Scene", "Generate a prebuilt entry for every mesh found in the scene.")))
                    {
                        var mesh_map = new Dictionary<MeshMaterial, int>();

                        for (int i = 0; i < atlas.Prebuilt.Count; i++)
                        {
                            var prebuilt = atlas.Prebuilt[i];

                            if (prebuilt.Mesh != null && prebuilt.Material != null)
                            {
                                var key = new MeshMaterial();
                                key.Mesh = prebuilt.Mesh;
                                key.Material = prebuilt.Material;

                                mesh_map[key] = i;
                            }
                        }

                        foreach (var renderer in FindObjectsOfType<MeshRenderer>())
                        {
                            if (renderer.sharedMaterials.Length == 0)
                                continue;

                            var filter = renderer.GetComponent<MeshFilter>();

                            if (filter == null)
                                continue;

                            if (filter.sharedMesh == null)
                                continue;

                            var sdf = renderer.GetComponent<SDF>();

                            var key = new MeshMaterial();
                            key.Mesh = filter.sharedMesh;
                            key.Material = renderer.sharedMaterial;

                            if (mesh_map.ContainsKey(key))
                            {
                                if (sdf != null && sdf.Entry != null && atlas.Prebuilt[mesh_map[key]].Entry == null)
                                {
                                    var prebuilt = atlas.Prebuilt[mesh_map[key]];
                                    prebuilt.Entry = sdf.Entry;
                                    atlas.Prebuilt[mesh_map[key]] = prebuilt;
                                }
                            }
                            else
                            {
                                var prebuilt = new SDFAtlas.PrebuiltSDF();

                                prebuilt.Mesh = key.Mesh;
                                prebuilt.Material = key.Material;

                                if (sdf != null)
                                {
                                    prebuilt.Entry = sdf.Entry;

                                    prebuilt.Bias = prebuilt.Entry.Fit.Bias;

                                    if (prebuilt.Entry.HasBuiltInfo)
                                    {
                                        prebuilt.Resolution = prebuilt.Entry.BuiltResolution;
                                        prebuilt.ForceDoubleSided = prebuilt.Entry.BuiltForceDoubleSided;
                                    }
                                    else
                                    {
                                        prebuilt.Resolution = 32;
                                        prebuilt.ForceDoubleSided = false;
                                    }
                                }
                                else
                                {
                                    prebuilt.Bias = 0;
                                    prebuilt.ForceDoubleSided = false;
                                    prebuilt.Resolution = 32;
                                }

                                mesh_map[key] = atlas.Prebuilt.Count;
                                atlas.Prebuilt.Add(prebuilt);
                            }
                        }
                    }
                }

                if (atlas.Prebuilt.Count > 0)
                {
                    if (GUILayout.Button(new GUIContent("Rebuild All", "Rebuild all of the prebuilt entries")))
                    {
                        for (int i = 0; i < atlas.Prebuilt.Count; i++)
                        {
                            var prebuilt = atlas.Prebuilt[i];

                            if (prebuilt.Mesh != null && prebuilt.Material != null)
                                prebuilt.Entry = Baker.Instance.Bake(prebuilt.Mesh, prebuilt.Material, prebuilt.Resolution, prebuilt.Bias, prebuilt.ForceDoubleSided);

                            atlas.Prebuilt[i] = prebuilt;
                        }
                    }

                    if (GUILayout.Button(new GUIContent("Remove Unused", "Remove prebuilt entries that have meshes not found in the scene")))
                    {
                        var mesh_map = new Dictionary<MeshMaterial, bool>();

                        for (int i = 0; i < atlas.Prebuilt.Count; i++)
                        {
                            var prebuilt = atlas.Prebuilt[i];

                            var key = new MeshMaterial();
                            key.Mesh = prebuilt.Mesh;
                            key.Material = prebuilt.Material;

                            mesh_map[key] = false;
                        }

                        foreach (var renderer in FindObjectsOfType<MeshRenderer>())
                        {
                            if (renderer.sharedMaterials.Length == 0)
                                continue;

                            var filter = renderer.GetComponent<MeshFilter>();

                            if (filter == null)
                                continue;

                            if (filter.sharedMesh == null)
                                continue;

                            var key = new MeshMaterial();
                            key.Mesh = filter.sharedMesh;
                            key.Material = renderer.sharedMaterial;

                            if (mesh_map.ContainsKey(key))
                                mesh_map[key] = true;
                        }

                        for (int i = atlas.Prebuilt.Count - 1; i >= 0; i--)
                        {
                            var prebuilt = atlas.Prebuilt[i];

                            var key = new MeshMaterial();
                            key.Mesh = prebuilt.Mesh;
                            key.Material = prebuilt.Material;

                            if (!mesh_map[key])
                                atlas.Prebuilt.RemoveAt(i);
                        }
                    }

                    if (GUILayout.Button(new GUIContent("Clear", "Remove all prebuilt entries")))
                        atlas.Prebuilt.Clear();
                }

                GUILayout.EndVertical();
            }
        }

        private void editBuild(ref SDFAtlas.BuildSettings settings, ref ShowSettings show)
        {
            settings.Bias = Mathf.Clamp(EditorGUILayout.FloatField(withTooltip(settings.GetType(), "Bias", "Bias:"), settings.Bias), -1, 1);
            settings.ForceDoubleSided = EditorGUILayout.Toggle(withTooltip(settings.GetType(), "ForceDoubleSided", "Force Double Sided:"), settings.ForceDoubleSided);

            GUILayout.BeginHorizontal();
            EditorGUILayout.Space(12, false);
            show.Thresholds = EditorGUILayout.Foldout(show.Thresholds, "Thresholds", true);
            GUILayout.EndHorizontal();

            if (show.Thresholds)
            {
                GUILayout.BeginVertical();
                {
                    settings.MediumSize = Mathf.Clamp(EditorGUILayout.FloatField(withTooltip(settings.GetType(), "MediumSize", "Medium:"), settings.MediumSize), 0, settings.LargeSize);
                    settings.LargeSize = Mathf.Clamp(EditorGUILayout.FloatField(withTooltip(settings.GetType(), "LargeSize", "Large:"), settings.LargeSize), settings.MediumSize, settings.HugeSize);
                    settings.HugeSize = Mathf.Max(EditorGUILayout.FloatField(withTooltip(settings.GetType(), "HugeSize", "Huge:"), settings.HugeSize), settings.LargeSize);

                    GUILayout.EndVertical();
                }
            }

            GUILayout.BeginHorizontal();
            EditorGUILayout.Space(12, false);
            show.Resolution = EditorGUILayout.Foldout(show.Resolution, "Resolution", true);
            GUILayout.EndHorizontal();

            if (show.Resolution)
            {
                GUILayout.BeginVertical();
                {
                    settings.SmallResolution = Mathf.Clamp(EditorGUILayout.IntField(withTooltip(settings.GetType(), "SmallResolution", "Small:"), settings.SmallResolution), 8, settings.MediumResolution);
                    settings.MediumResolution = Mathf.Clamp(EditorGUILayout.IntField(withTooltip(settings.GetType(), "MediumResolution", "Medium:"), settings.MediumResolution), settings.SmallResolution, settings.LargeResolution);
                    settings.LargeResolution = Mathf.Clamp(EditorGUILayout.IntField(withTooltip(settings.GetType(), "LargeResolution", "Large:"), settings.LargeResolution), settings.MediumResolution, settings.HugeResolution);
                    settings.HugeResolution = Mathf.Clamp(EditorGUILayout.IntField(withTooltip(settings.GetType(), "HugeResolution", "Huge:"), settings.HugeResolution), settings.LargeResolution, 64);

                    GUILayout.EndVertical();
                }
            }
        }
    }
}