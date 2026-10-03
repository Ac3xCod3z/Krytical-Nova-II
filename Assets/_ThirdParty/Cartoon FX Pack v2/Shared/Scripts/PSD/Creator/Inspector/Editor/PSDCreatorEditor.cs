using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEditor.SceneManagement;
using PSD.Creator.Tool;
using PSD.Creator.Data;
using PSD.Creator.Inspector;
using PSD.Creator.Inspector.Utility;
using PSD.Preferences.Theming;

namespace PSD.Creator.Editor.Inspector.Editors
{
    public abstract class PSDCreatorEditor<T, TBasic, TRandomization> : UnityEditor.Editor where T : PSDCreator<T, TBasic> where TBasic : Basic where TRandomization : RandomizationPage
    {
        enum State
        {
            Uninitialized,
            MaterialCheck,
            Idle,
            Editing
        }
        private State state = State.Uninitialized;

        protected abstract BasicSettingsPage BasicSettingsPage { get; }
        protected abstract RandomizationPage RandomizationPage { get; }
        protected abstract string Title { get; }
        protected abstract string Reference { get; }

        private PageManager pageManager;
        private int targetInstanceId;

        private Texture2D btnBg, btnBgHover, btnBgActive;

        private string trackedName;

        private void OnEnable()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                AssetDatabase.ForceReserializeAssets(
                    new[] { AssetDatabase.GetAssetPath(target) },
                    ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata
                );

            InitializePages();
            Undo.undoRedoPerformed += OnUndoRedoPerformed;

            CheckForRename();
            EditorApplication.hierarchyChanged += OnHierarchyChanged;

            btnBg = InspectorUtility.MakeGradientTex(128, 24, Theme.BtnGradientLeftNormal, Theme.BtnGradientRightNormal);
            btnBgHover = InspectorUtility.MakeGradientTex(128, 24, Theme.BtnGradientLeftHover, Theme.BtnGradientRightHover);
            btnBgActive = InspectorUtility.MakeGradientTex(128, 24, Theme.BtnGradientLeftActive, Theme.BtnGradientRightActive);
        }

        private void OnDisable()
        {
            if (pageManager != null && targetInstanceId != 0)
            {
                string key = $"PageState_{targetInstanceId}";

                SessionState.SetInt(key + "_cat", pageManager.CurrentCategoryIndex);
                SessionState.SetInt(key + "_page", pageManager.CurrentPageIndex);
            }

            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;

            DestroyImmediate(btnBg); DestroyImmediate(btnBgHover); DestroyImmediate(btnBgActive);
        }

        private void OnHierarchyChanged() => CheckForRename();

        private void CheckForRename()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            if (target == null) return;
            var creator = target as T;
            if (creator == null) return;

            if (string.IsNullOrEmpty(creator.TrackedName))
            {
                creator.TrackedName = creator.name;
                EditorUtility.SetDirty(creator);
                return;
            }

            if (creator.TrackedName == creator.name) return;

            string oldName = creator.TrackedName;
            string newName = creator.name;

            creator.TrackedName = newName;
            EditorUtility.SetDirty(creator);

            RenameAssociatedAssets(creator, oldName, newName);
        }

        private void RenameAssociatedAssets(T creator, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || oldName == newName) return;

            string newSafe = newName.Replace(" ", "_");

            var components = creator.GetComponents();
            if (components.Length == 0) return;

            var materialGuids = new List<string>();
            string folder = null;

            foreach (var comp in components)
            {
                var material = comp.Material;
                if (material == null) continue;

                string matPath = AssetDatabase.GetAssetPath(material);
                if (string.IsNullOrEmpty(matPath)) continue;

                string guid = AssetDatabase.AssetPathToGUID(matPath);
                if (!string.IsNullOrEmpty(guid))
                    materialGuids.Add(guid);

                if (folder == null)
                    folder = System.IO.Path.GetDirectoryName(matPath)?.Replace("\\", "/");
            }

            if (materialGuids.Count == 0) return;

            if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
            {
                string folderError = AssetDatabase.RenameAsset(folder, newName);
                if (!string.IsNullOrEmpty(folderError))
                    Debug.LogWarning($"Failed to rename folder '{folder}': {folderError}");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var comp in components)
                {
                    var material = comp.Material;
                    if (material == null) continue;

                    string matPath = AssetDatabase.GetAssetPath(material);
                    string guid = AssetDatabase.AssetPathToGUID(matPath);
                    string currentPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(currentPath)) continue;

                    var refMaterial = comp.Reference != null ? comp.Reference.material : null;
                    if (refMaterial == null)
                    {
                        Debug.LogWarning($"Component '{creator.GetTypeName(comp)}' has no reference material — cannot safely rename '{currentPath}'.");
                        continue;
                    }

                    string typeName = creator.GetTypeName(comp)?.Replace(" ", "_");
                    if (string.IsNullOrEmpty(typeName))
                    {
                        Debug.LogWarning($"GetTypeName returned empty for component on '{currentPath}' — skipping rename.");
                        continue;
                    }

                    string refMatName = refMaterial.name.Replace(" ", "_");
                    string newFileName = $"{typeName}_{newSafe}_{refMatName}";
                    string currentFileName = System.IO.Path.GetFileNameWithoutExtension(currentPath);

                    if (newFileName == currentFileName) continue;

                    string matError = AssetDatabase.RenameAsset(currentPath, newFileName);
                    if (!string.IsNullOrEmpty(matError))
                        Debug.LogWarning($"Failed to rename material '{currentPath}': {matError}");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        protected abstract void AddPages(PageManager manager);

        private void InitializePages()
        {
            // Krytical Nova patch: Object.GetInstanceID() is a compile error in Unity 6.6.
            targetInstanceId = target.GetEntityId().GetHashCode();

            pageManager ??= new PageManager();

            pageManager.ClearPages();

            var dataPage = new ReferencePage();
            pageManager.AddPage(dataPage);
            pageManager.AddPage(BasicSettingsPage);
            AddPages(pageManager);
            pageManager.AddPage(RandomizationPage);

            string key = $"PageState_{targetInstanceId}";

            int cat = SessionState.GetInt(key + "_cat", 0);
            int page = SessionState.GetInt(key + "_page", 0);

            if (dataPage.Validate(serializedObject))
            {
                if (!SessionState.GetBool(key + "_has", false))
                    pageManager.SetCategory(0, 1);
                else
                    pageManager.SetCategory(cat, page);
            }
            else
                pageManager.SetCategory(cat, page);

            state = State.Idle;
        }

        public override void OnInspectorGUI()
        {
            if (target == null) return;
            var creator = (T)target;

            CheckForRename();

            if (state == State.Uninitialized) InitializePages();

            serializedObject.Update();

            int previousPageIndex = pageManager?.CurrentPageIndex ?? 0;

            DrawCustomHeader();

            if (PrefabStageUtility.GetCurrentPrefabStage() == null)
            {
                string path = AssetDatabase.GetAssetPath(target);
                if (!string.IsNullOrEmpty(path))
                {
                    EditorGUILayout.HelpBox("Editing is not available project selection mode. Please open the scene containing the Cartoon FX or enter prefab edit mode (double-click prefab) to edit it.", MessageType.Info);
                    return;
                }
            }
            // Probably only EditorApplication is needed.
            bool isPlaying =
#if UNITY_EDITOR
                EditorApplication.isPlaying;
#else
                Application.isPlaying;
#endif
            if (isPlaying)
            {
                EditorGUILayout.HelpBox("Editing is not available when playing.", MessageType.Info);
                return;
            }

            EditorGUI.BeginChangeCheck();

            var missing = creator.FindMissingMaterials();
            if (missing.Count > 0) state = State.MaterialCheck;
            else if (state == State.MaterialCheck) state = State.Idle;

            switch (state)
            {
                case State.MaterialCheck:
                    DrawMaterialCheck(missing);
                    break;
                case State.Idle:
                    DrawIdle();
                    break;
                case State.Editing:
                    DrawEditing(previousPageIndex);
                    break;
            }

            bool guiChanged = EditorGUI.EndChangeCheck();
            bool hasModifiedProperties = serializedObject.hasModifiedProperties;
            serializedObject.ApplyModifiedProperties();

            if (guiChanged || hasModifiedProperties)
            {
                creator.ResetSystem();
                EditorUtility.SetDirty(target);
            }
        }

        private void DrawCustomHeader()
        {
            var creator = (T)target;

            var accentRect = GUILayoutUtility.GetRect(0, 3, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(accentRect, btnBg, ScaleMode.StretchToFill);

            EditorGUILayout.Space(10);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(12);

                var iconBoxStyle = new GUIStyle(GUI.skin.box)
                {
                    fixedWidth = 40,
                    fixedHeight = 40,
                    margin = new RectOffset(0, 12, 0, 0),
                    padding = new RectOffset(4, 4, 4, 4),
                };
                GUILayout.Box(EditorGUIUtility.IconContent("ParticleSystem Icon"), iconBoxStyle, GUILayout.Width(40), GUILayout.Height(40));

                using (new EditorGUILayout.VerticalScope())
                {
                    GUILayout.Space(4);

                    EditorGUILayout.LabelField(Title, new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 14,
                        fontStyle = FontStyle.Bold,
                    });
                    EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(creator.name), new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 10,
                        fontStyle = FontStyle.BoldAndItalic,
                        normal = { textColor = Theme.SubtleText }
                    });
                }

                GUILayout.FlexibleSpace();

                var badgeStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    fontSize = 10,
                    padding = new RectOffset(8, 8, 3, 3),
                    normal = { textColor = Theme.BadgeText }
                };
                var badgeRect = GUILayoutUtility.GetRect(new GUIContent("v2.0"), badgeStyle, GUILayout.Width(40), GUILayout.Height(18));
                EditorGUI.DrawRect(badgeRect, Theme.BadgeBackground);
                EditorGUI.LabelField(badgeRect, "v2.0", badgeStyle);

                GUILayout.Space(12);
            }

            EditorGUILayout.Space(8);

            var dividerRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(dividerRect, btnBg, ScaleMode.StretchToFill);

            EditorGUILayout.Space(6);
        }

        private void DrawIdle()
        {
            EditorGUILayout.Space(4);

            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 24,
                fixedWidth = 120,
                normal = { background = btnBg, textColor = Color.white },
                hover = { background = btnBgHover, textColor = Color.white },
                active = { background = btnBgActive, textColor = Color.white },
            };

            var rect = GUILayoutUtility.GetRect(120, 24, GUILayout.Width(120), GUILayout.Height(24));

            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 4, rect.width, rect.height), Theme.ButtonShadowHeavy);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 3, rect.width, rect.height), Theme.ButtonShadowMid);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 2, rect.width, rect.height), Theme.ButtonShadowLight);

            if (GUI.Button(rect, $"Edit {Reference}", style))
                state = State.Editing;

            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);

            EditorGUILayout.Space(8);
        }

        private void DrawEditing(int previousPageIndex)
        {
            pageManager.DrawPageSystem(serializedObject);

            if (pageManager != null && pageManager.CurrentPageIndex != previousPageIndex && targetInstanceId != 0)
            {
                string key = $"PageState_{targetInstanceId}";

                SessionState.SetInt(key + "_cat", pageManager.CurrentCategoryIndex);
                SessionState.SetInt(key + "_page", pageManager.CurrentPageIndex);
                SessionState.SetBool(key + "_has", true);
            }
        }

        private void DrawMaterialCheck(List<IComponent> missingComponents)
        {
            var creator = (T)target;

            EditorGUILayout.HelpBox("Some materials are missing!", MessageType.Error);
            EditorGUILayout.Space(10);

            var style = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 24,
                fixedWidth = 160,
                normal = { background = btnBg, textColor = Color.white },
                hover = { background = btnBgHover, textColor = Color.white },
                active = { background = btnBgActive, textColor = Color.white },
            };

            var rect = GUILayoutUtility.GetRect(160, 24, GUILayout.Width(160), GUILayout.Height(24));

            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 4, rect.width, rect.height), Theme.ButtonShadowHeavy);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 3, rect.width, rect.height), Theme.ButtonShadowMid);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 2, rect.width, rect.height), Theme.ButtonShadowLight);

            if (GUI.Button(rect, "Add Missing Materials", style))
            {
                string folder = EditorUtility.OpenFolderPanel("Select folder to save new materials", "Assets", "");
                if (string.IsNullOrEmpty(folder)) return;

                string projectPath;
                if (folder.StartsWith(Application.dataPath))
                    projectPath = "Assets" + folder.Substring(Application.dataPath.Length);
                else
                {
                    EditorUtility.DisplayDialog("Invalid Folder", "Please select a folder inside this Unity project's Assets folder.", "OK");
                    return;
                }

                serializedObject.Update();

                foreach (var comp in missingComponents)
                {
                    var material = comp.Reference.material;
                    if (material == null)
                    {
                        Debug.LogWarning($"Component {creator.GetTypeName(comp)} has no reference material assigned, please assign one in the reference object.", creator.reference.references);
                        continue;
                    }

                    string subFolder;
                    if (System.IO.Path.GetFileName(projectPath) == target.name)
                        subFolder = projectPath;
                    else
                    {
                        subFolder = $"{projectPath}/{target.name}";
                        if (!AssetDatabase.IsValidFolder(subFolder))
                            AssetDatabase.CreateFolder(projectPath, target.name);
                    }

                    var safeName = (creator.GetTypeName(comp) + " " + target.name + " " + material.name).Replace(" ", "_");
                    string assetPath = $"{subFolder}/{safeName}.mat";

                    var mat = new Material(material);
                    AssetDatabase.CreateAsset(mat, assetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.ImportAsset(assetPath);

                    comp.Material = mat;
                }

                serializedObject.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();

                serializedObject.Update();

                // Refresh editor state
                creator.ResetSystem();
                EditorUtility.SetDirty(target);
            }
        }

        private void OnUndoRedoPerformed()
        {
            if (target != null)
            {
                T creator = target as T;
                creator.ResetSystem();
                creator.ResetPlay();
                Repaint();
            }
        }
    }
}