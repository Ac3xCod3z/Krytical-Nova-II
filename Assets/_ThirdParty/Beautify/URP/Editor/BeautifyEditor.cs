using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Unity", "UAL0010", Scope = "namespaceanddescendants", Target = "~N:Beautify.Universal")]
[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage("Unity", "UAL0013", Scope = "namespaceanddescendants", Target = "~N:Beautify.Universal")]

namespace Beautify.Universal {
#if UNITY_2022_2_OR_NEWER
    [CustomEditor(typeof(Beautify))]
#else
    [VolumeComponentEditor(typeof(Beautify))]
#endif
    public class BeautifyEditor : VolumeComponentEditor {

        Beautify beautify;
        GUIStyle sectionGroupStyle, foldoutStyle, blackBack;
        bool stylesProSkin;
        Texture2D headerTex, blackTex;
        bool pixelateExpanded;

        // Cached BeautifySettings instances to avoid frequent FindObjectsOfType calls
        BeautifySettings[] cachedBeautifySettingsInstances;

        // per-type layout, built once per domain reload
        class FieldMeta {
            public FieldInfo field;
            public Beautify.DisplayConditionEnum displayConditionEnum;
            public Beautify.DisplayConditionBool displayConditionBool;
            public GUIContent label;
            public bool hasGlobalOverride;
            public bool showStrippedLabel;
            public bool hasToggleAllFields;
            public bool isDepthOfFieldFocusMode;
        }

        class GroupLayout {
            public Beautify.SettingsGroup settingsGroup;
            public string name;
            public List<FieldMeta> fields = new List<FieldMeta>();
        }

        class SectionLayout {
            public string name;
            public List<FieldMeta> singleFields = new List<FieldMeta>();
            public List<GroupLayout> groups = new List<GroupLayout>();
        }

        // per-instance serialized state
        class FieldState {
            public SerializedDataParameter parameter;
            public SerializedProperty enumCondition, boolCondition, boolCondition2;
        }

        static List<SectionLayout> layout;
        static readonly GUIContent clearEffectsContent = new GUIContent("Clear Effects", "Clears all effect overrides");
        static readonly GUIContent quickSettingsContent = new GUIContent("Quick Settings", "Applies a default set of effects including color improvement, sharpening, vignette and bloom.");
        static readonly GUIContent onlineResourcesContent = new GUIContent("Online Resources & Support");
        static readonly GUIContent configureBuildContent = new GUIContent("Configure Build Settings >");
        static readonly GUIContent clickToConfigureContent = new GUIContent("(click to configure)");
        const string NOT_AVAILABLE_LABEL = "(Not available - Check General Options)";
        const string STRIPPED_WARNING = "This feature is stripped in the build.";

        readonly Dictionary<FieldMeta, FieldState> fieldStates = new Dictionary<FieldMeta, FieldState>();
        readonly Dictionary<string, bool> sectionFoldStates = new Dictionary<string, bool>();
        string strippedKeywords;
        const string SECTION_FOLD_PREF_PREFIX = "Beautify_SectionFold_";
#if !UNITY_2021_2_OR_NEWER
        public override bool hasAdvancedMode => false;
#endif

        public override void OnEnable () {
            if (target == null) return;

            base.OnEnable();

            headerTex = Resources.Load<Texture2D>("beautifyHeader");
            blackTex = MakeTex(4, 4, Color.black);
            blackBack = new GUIStyle();
            blackBack.normal.background = blackTex;
            blackBack.alignment = TextAnchor.MiddleCenter;

            beautify = (Beautify)target;

            // Cache BeautifySettings instances on enable
            cachedBeautifySettingsInstances = Misc.FindObjectsOfType<BeautifySettings>(true);

            if (layout == null) BuildLayout();

            fieldStates.Clear();
            foreach (var section in layout) {
                foreach (var meta in section.singleFields) BindField(meta);
                foreach (var group in section.groups) {
                    foreach (var meta in group.fields) BindField(meta);
                }
            }
        }

        public override void OnDisable () {
            EditorApplication.delayCall -= RepaintAllViews;
            cachedBeautifySettingsInstances = null;
            if (blackTex != null) {
                DestroyImmediate(blackTex);
                blackTex = null;
            }
        }

        static void BuildLayout () {
            layout = new List<SectionLayout>();
            var sectionByType = new Dictionary<Type, SectionLayout>();
            var groupByType = new Dictionary<Type, GroupLayout>();

            var fields = typeof(Beautify).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (var field in fields) {
                if (!field.FieldType.IsSubclassOf(typeof(VolumeParameter))) continue;
                bool serialized = (field.IsPublic && !field.IsDefined(typeof(NonSerializedAttribute), false)) || field.IsDefined(typeof(SerializeField), false);
                if (!serialized) continue;
                if (field.IsDefined(typeof(HideInInspector), false)) continue;
                if (!field.IsDefined(typeof(Beautify.SectionGroup), false)) continue;

                var meta = new FieldMeta {
                    field = field,
                    displayConditionEnum = field.GetCustomAttribute<Beautify.DisplayConditionEnum>(),
                    displayConditionBool = field.GetCustomAttribute<Beautify.DisplayConditionBool>(),
                    hasGlobalOverride = field.IsDefined(typeof(Beautify.GlobalOverride), false),
                    showStrippedLabel = field.IsDefined(typeof(Beautify.ShowStrippedLabel), false),
                    hasToggleAllFields = field.IsDefined(typeof(Beautify.ToggleAllFields), false),
                    isDepthOfFieldFocusMode = field.Name.Equals("depthOfFieldFocusMode")
                };
                var displayName = field.GetCustomAttribute<Beautify.DisplayName>();
                var tooltip = field.GetCustomAttribute<TooltipAttribute>();
                meta.label = new GUIContent(displayName != null ? displayName.name : ObjectNames.NicifyVariableName(field.Name), tooltip != null ? tooltip.tooltip : "");

                foreach (var section in field.GetCustomAttributes<Beautify.SectionGroup>()) {
                    Type sectionType = section.GetType();
                    if (!sectionByType.TryGetValue(sectionType, out var sectionLayout)) {
                        sectionLayout = new SectionLayout { name = ObjectNames.NicifyVariableName(sectionType.Name) };
                        sectionByType[sectionType] = sectionLayout;
                        layout.Add(sectionLayout);
                    }

                    bool isGrouped = false;
                    foreach (var settingsGroup in field.GetCustomAttributes<Beautify.SettingsGroup>()) {
                        Type groupType = settingsGroup.GetType();
                        if (!groupByType.TryGetValue(groupType, out var groupLayout)) {
                            groupLayout = new GroupLayout { settingsGroup = settingsGroup, name = ObjectNames.NicifyVariableName(groupType.Name) };
                            groupByType[groupType] = groupLayout;
                            sectionLayout.groups.Add(groupLayout);
                        }
                        groupLayout.fields.Add(meta);
                        isGrouped = true;
                    }

                    if (!isGrouped) {
                        sectionLayout.singleFields.Add(meta);
                    }
                }
            }
        }

        void BindField (FieldMeta meta) {
            if (fieldStates.ContainsKey(meta)) return;
            var property = serializedObject.FindProperty(meta.field.Name);
            if (property == null) return;
            var state = new FieldState { parameter = Unpack(property) };
            if (meta.displayConditionEnum != null) {
                state.enumCondition = FindValueProperty(meta.displayConditionEnum.field);
            }
            if (meta.displayConditionBool != null) {
                state.boolCondition = FindValueProperty(meta.displayConditionBool.field);
                state.boolCondition2 = FindValueProperty(meta.displayConditionBool.field2);
            }
            fieldStates[meta] = state;
        }

        SerializedProperty FindValueProperty (string fieldName) {
            if (string.IsNullOrEmpty(fieldName)) return null;
            var prop = serializedObject.FindProperty(fieldName);
            return prop != null ? prop.FindPropertyRelative("m_Value") : null;
        }

        public override void OnInspectorGUI () {

            serializedObject.Update();

            SetStyles();
            strippedKeywords = PlayerPrefs.GetString(BeautifyRendererFeature.PLAYER_PREF_KEYNAME, "");

            Beautify.TonemapOperator prevTonemap = beautify.tonemap.value;
            bool prevDirectWrite = beautify.directWrite.value;
            int prevBloomExclusionLayerMask = beautify.bloomExclusionLayerMask.overrideState ? (int)beautify.bloomExclusionLayerMask.value : 0;
            int prevAnamorphicFlaresExclusionLayerMask = beautify.anamorphicFlaresExclusionLayerMask.overrideState ? (int)beautify.anamorphicFlaresExclusionLayerMask.value : 0;

            EditorGUILayout.BeginVertical();
            {
                GUILayout.BeginHorizontal(blackBack);
                GUILayout.Label(headerTex, blackBack, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(clearEffectsContent, EditorStyles.miniButton)) {
                    if (EditorUtility.DisplayDialog("Clear Effects", "Do you want to clear all effects?", "Yes", "Cancel")) {
                        beautify.sharpenIntensity.overrideState = false;
                        beautify.antialiasStrength.overrideState = false;
                        beautify.ditherIntensity.overrideState = false;
                        beautify.tonemap.overrideState = false;
                        beautify.saturate.Override(0);
                        beautify.brightness.overrideState = false;
                        beautify.contrast.overrideState = false;
                        beautify.daltonize.overrideState = false;
                        beautify.sepia.overrideState = false;
                        beautify.tintColor.overrideState = false;
                        beautify.colorTemp.overrideState = false;
                        beautify.colorTempBlend.overrideState = false;
                        beautify.lut.overrideState = false;
                        beautify.bloomIntensity.overrideState = false;
                        beautify.anamorphicFlaresIntensity.overrideState = false;
                        beautify.sunFlaresIntensity.overrideState = false;
                        beautify.lensDirtIntensity.overrideState = false;
                        beautify.chromaticAberrationIntensity.overrideState = false;
                        beautify.depthOfField.overrideState = false;
                        beautify.eyeAdaptation.overrideState = false;
                        beautify.purkinje.overrideState = false;
                        beautify.vignettingOuterRing.overrideState = false;
                        beautify.vignettingInnerRing.overrideState = false;
                        beautify.vignettingFade.overrideState = false;
                        beautify.vignettingBlink.overrideState = false;
                        beautify.outline.overrideState = false;
                        beautify.nightVision.overrideState = false;
                        beautify.thermalVision.overrideState = false;
                        beautify.frame.overrideState = false;
                        beautify.blurIntensity.overrideState = false;
                        EditorUtility.SetDirty(beautify);
                    }
                }
                if (GUILayout.Button(quickSettingsContent, EditorStyles.miniButton)) {
                    if (EditorUtility.DisplayDialog("Quick Settings", "Do you want to apply a collection of example effect settings (including color improvements, dithering, sharpening, vignette and bloom)?\nYou can adjust them later as you wish.", "Yes", "No")) {
                        beautify.sharpenIntensity.Override(4f);
                        beautify.ditherIntensity.Override(0.005f);
                        beautify.brightness.Override(1.05f);
                        beautify.saturate.Override(1f);
                        beautify.contrast.Override(1.02f);
                        beautify.bloomIntensity.Override(0.25f);
                        beautify.bloomThreshold.Override(0.75f);
                        beautify.vignettingOuterRing.Override(0.325f);
                        beautify.vignettingInnerRing.Override(0.925f);
                        EditorUtility.SetDirty(beautify);
                    }
                }
                if (GUILayout.Button(onlineResourcesContent)) {
                    ContactUsWindow.ShowScreen();
                }
                EditorGUILayout.EndHorizontal();

                if (string.IsNullOrEmpty(strippedKeywords)) {
                    EditorGUILayout.HelpBox("All features are currently included in the build, which may significantly increase compilation time. Click the button below to configure and exclude unnecessary features from the build.", MessageType.Warning);
                }
                if (GUILayout.Button(configureBuildContent)) {
                    SelectActiveURPRendererAsset();
                }

                // Check for multiple BeautifySettings instances
                BeautifySettings[] beautifySettingsInstances = cachedBeautifySettingsInstances;
                if (beautifySettingsInstances != null && beautifySettingsInstances.Length > 1) {
                    EditorGUILayout.HelpBox($"Multiple BeautifySettings instances found in the scene ({beautifySettingsInstances.Length}). It's recommended to keep only one instance.", MessageType.Warning);

                    EditorGUILayout.LabelField("Found instances:", EditorStyles.boldLabel);
                    foreach (BeautifySettings settingsInstance in beautifySettingsInstances) {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(settingsInstance.gameObject.name, GUILayout.ExpandWidth(true));
                        if (GUILayout.Button("Select", GUILayout.Width(60))) {
                            Selection.activeObject = settingsInstance.gameObject;
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                    EditorGUILayout.Separator();
                }

                UniversalRenderPipelineAsset pipe = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

                Camera cam = Camera.main;
                if (cam != null) {
                    UniversalAdditionalCameraData data = cam.GetComponent<UniversalAdditionalCameraData>();
                    if (data != null && !data.renderPostProcessing && !BeautifyRendererFeature.ignoringPostProcessingOption) {
                        EditorGUILayout.HelpBox("Post Processing option is disabled in the camera. Either enable it or enable the option 'Ignore Post Processing Option' in the Beautify Render Feature.", MessageType.Warning);
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("Go to Camera")) {
                            Selection.activeObject = cam;
                        }
                        if (GUILayout.Button("Go to Universal Rendering Pipeline Asset")) {
                            Selection.activeObject = pipe;
                        }
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.Separator();
                    }
                }

                if (pipe == null) {
                    EditorGUILayout.HelpBox("Universal Rendering Pipeline asset is not set in 'Project Settings / Graphics' !", MessageType.Error);
                    EditorGUILayout.Separator();
                    GUI.enabled = false;
                }
                else if (!BeautifyRendererFeature.installed) {
                    EditorGUILayout.HelpBox("Beautify Render Feature must be added to the rendering pipeline renderer.", MessageType.Error);
                    if (GUILayout.Button("Go to Universal Rendering Pipeline Asset")) {
                        Selection.activeObject = pipe;
                    }
                    EditorGUILayout.Separator();
                    GUI.enabled = false;
                }
                else if (beautify.RequiresDepthTexture()) {
#if !UNITY_2021_3_OR_NEWER
                    if (!pipe.supportsCameraDepthTexture) {
                        EditorGUILayout.HelpBox("Depth Texture option may be required for certain effects. Check Universal Rendering Pipeline asset!", MessageType.Warning);
                        if (GUILayout.Button("Go to Universal Rendering Pipeline Asset")) {
                            Selection.activeObject = pipe;
                        }
                        EditorGUILayout.Separator();
                    }
#endif
                }

                bool usesHDREffect = beautify.tonemap.value != Beautify.TonemapOperator.Linear;
                if (usesHDREffect && (QualitySettings.activeColorSpace != ColorSpace.Linear || (cam != null && !cam.allowHDR))) {
                    EditorGUILayout.HelpBox("Some effects, like bloom or tonemapping, works better with Linear Color Space and HDR enabled. Enable Linear color space in Player Settings and check your camera and pipeline HDR setting.", MessageType.Warning);
                }

                if ((bool)beautify.directWrite) {
#if !UNITY_2022_3_OR_NEWER
                    if (UnityEngine.XR.XRSettings.enabled) {
                        EditorGUILayout.HelpBox("Direct Write To Camera option is not compatible with VR.", MessageType.Warning);
                    }
#endif
                }

                // sections
                foreach (var section in layout) {
                    string sectionName = section.name;
                    bool sectionExpanded = GetSectionFoldState(sectionName);

                    GUILayout.Space(6.0f);
                    Rect rect = GUILayoutUtility.GetRect(16f, 22f, sectionGroupStyle);

                    // Draw foldout arrow
                    Rect foldoutRect = new Rect(rect.x + 4f, rect.y + 3f, 13f, 13f);
                    if (Event.current.type == EventType.Repaint) {
                        EditorStyles.foldout.Draw(foldoutRect, false, false, sectionExpanded, false);
                    }

                    GUI.Box(rect, sectionName, sectionGroupStyle);

                    // Handle click on header
                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition)) {
                        sectionExpanded = !sectionExpanded;
                        SetSectionFoldState(sectionName, sectionExpanded);
                        Event.current.Use();
                    }

                    EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

                    if (!sectionExpanded) continue;

                    // individual properties
                    EditorGUI.indentLevel++;
                    foreach (var meta in section.singleFields) {
                        if (!fieldStates.TryGetValue(meta, out var state)) continue;

                        bool indent;
                        if (!IsVisible(meta, state, out indent)) {
                            if (meta.showStrippedLabel) {
                                EditorGUILayout.BeginHorizontal();
                                GUI.enabled = false;
                                DrawPropertyField(meta, state, indent);
                                GUILayout.Label(NOT_AVAILABLE_LABEL);
                                GUI.enabled = true;
                                EditorGUILayout.EndHorizontal();
                            }
                            continue;
                        }

                        DrawPropertyField(meta, state, indent);

                        if (beautify.disabled.value) GUI.enabled = false;
                    }
                    EditorGUI.indentLevel--;
                    GUILayout.Space(6.0f);

                    // grouped properties
                    foreach (var group in section.groups) {
                        Beautify.SettingsGroup settingsGroup = group.settingsGroup;
                        string groupName = group.name;
                        bool printGroupFoldout = true;
                        bool firstField = true;
                        bool groupHasContent = false;

                        foreach (var meta in group.fields) {
                            if (!fieldStates.TryGetValue(meta, out var state)) continue;

                            bool indent;
                            if (!IsVisible(meta, state, out indent)) {
                                if (firstField) {
                                    if (meta.showStrippedLabel) {
                                        EditorGUILayout.BeginHorizontal();
                                        GUI.enabled = false;
                                        EditorGUILayout.Foldout(false, groupName, true, foldoutStyle);
                                        GUILayout.FlexibleSpace();
                                        GUILayout.Label(NOT_AVAILABLE_LABEL);
                                        GUI.enabled = true;
                                        EditorGUILayout.EndHorizontal();
                                        GUILayout.Space(6.0f);
                                    }
                                    break;
                                }
                                continue;
                            }

                            firstField = false;

                            if (printGroupFoldout) {
                                printGroupFoldout = false;
                                settingsGroup.IsExpanded = EditorGUILayout.Foldout(settingsGroup.IsExpanded, groupName, true, foldoutStyle);
                                if (!settingsGroup.IsExpanded)
                                    break;
                                EditorGUI.indentLevel++;
                            }

                            DrawPropertyField(meta, state, indent);
                            groupHasContent = true;

                            var parameter = state.parameter;
                            if (parameter.value.propertyType == SerializedPropertyType.Boolean) {
                                if (!parameter.value.boolValue && meta.hasToggleAllFields) break;
                            }
                            else if (meta.isDepthOfFieldFocusMode) {
                                if (BeautifySettings.instance != null && BeautifySettings.instance.depthOfFieldTarget == null && parameter.value.enumValueIndex == (int)Beautify.DoFFocusMode.FollowTarget) {
                                    EditorGUILayout.HelpBox("Assign target in the Beautify Settings component.", MessageType.Info);
                                }
                            }
                        }
                        if (groupHasContent) {
                            EditorGUI.indentLevel--;
                            GUILayout.Space(6.0f);
                        }
                    }
                }
            }
            EditorGUILayout.EndVertical();

            if (GetSectionFoldState("Artistic Choices")) {
                pixelateExpanded = EditorGUILayout.Foldout(pixelateExpanded, "Pixelate", true, foldoutStyle);
                if (pixelateExpanded) {
                    EditorGUILayout.HelpBox("Use the Downsampling option in General Settings to apply a pixelate effect.", MessageType.Info);
                }
            }


            if (serializedObject.ApplyModifiedProperties()) {
                if (beautify.directWrite.value != prevDirectWrite || (beautify.bloomExclusionLayerMask.overrideState ? (int)beautify.bloomExclusionLayerMask.value : 0) != prevBloomExclusionLayerMask || (beautify.anamorphicFlaresExclusionLayerMask.overrideState ? (int)beautify.anamorphicFlaresExclusionLayerMask.value : 0) != prevAnamorphicFlaresExclusionLayerMask || beautify.outline.value) {
                    EditorApplication.delayCall += RepaintAllViews;
                }
            }

            if (prevTonemap != beautify.tonemap.value && beautify.tonemap.value != Beautify.TonemapOperator.Linear) {
                beautify.saturate.value = 0;
                beautify.saturate.overrideState = true;
                beautify.contrast.value = 1f;
                beautify.contrast.overrideState = true;
            }

        }

        static void RepaintAllViews () {
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }

        bool IsVisible (FieldMeta meta, FieldState state, out bool indent) {
            bool visible = true;
            indent = false;

            Beautify.DisplayConditionEnum enumCondition = meta.displayConditionEnum;
            Beautify.DisplayConditionBool boolCondition = meta.displayConditionBool;
            bool isEnumCondition = enumCondition != null;
            bool isBoolCondition = boolCondition != null;
            bool canIndent = isEnumCondition ^ isBoolCondition;

            if (isEnumCondition) {
                var value = state.enumCondition;
                if (value != null) {
                    visible = false;
                    if (enumCondition.isEqual) {
                        if (value.enumValueIndex == enumCondition.enumValueIndex) {
                            indent = canIndent;
                            return true;
                        }
                    }
                    else if (value.enumValueIndex != enumCondition.enumValueIndex) {
                        indent = canIndent;
                        return true;
                    }
                }
            }
            /* OR */
            if (isBoolCondition) {
                var value = state.boolCondition;
                if (value != null) {
                    if (value.boolValue != boolCondition.value) {
                        return false;
                    }
                    indent = value.boolValue;
                }
                /* AND */
                var value2 = state.boolCondition2;
                if (value2 != null) {
                    if (value2.boolValue != boolCondition.value2) {
                        return false;
                    }
                    indent = indent || value2.boolValue;
                }
                indent &= canIndent;
                visible = true;
            }

            return visible;
        }

        void DrawPropertyField (FieldMeta meta, FieldState state, bool indent) {

            if (indent) {
                EditorGUI.indentLevel++;
            }

            SerializedDataParameter property = state.parameter;

            if (property.value.propertyType == SerializedPropertyType.Boolean && meta.hasGlobalOverride) {

                BoolParameter pr = property.GetObjectRef<BoolParameter>();
                bool prev = pr.value;

                using (new EditorGUILayout.HorizontalScope()) {
                    float w = 17f;
                    if (EditorGUI.indentLevel > 0) w += 8f;
                    var overrideRect = GUILayoutUtility.GetRect(w, 20f, GUILayout.ExpandWidth(false));
                    overrideRect.yMin += 4f;
                    if (EditorGUI.indentLevel > 0) {
                        overrideRect.xMin += 12f;
                    }
                    bool value = GUI.Toggle(overrideRect, prev, GUIContent.none);

                    using (new EditorGUI.DisabledScope(!prev)) {
                        EditorGUILayout.LabelField(meta.label);
                    }

                    if (value != prev) {
                        pr.value = value;
                        property.value.boolValue = value;
                        if (value) {
                            property.overrideState.boolValue = true;
                        }
                    }
                }

            }
            else {
                PropertyField(property, meta.label);
            }

            if (indent) {
                EditorGUI.indentLevel--;
            }

            // Add warning messages for stripped features
            if (meta.showStrippedLabel && IsParameterEnabled(property) && IsFieldStripped(meta.field.Name, property)) {
                var linkSize = EditorStyles.linkLabel.CalcSize(clickToConfigureContent);
                var height = EditorGUIUtility.singleLineHeight + 8;

                var warningRect = EditorGUILayout.GetControlRect(false, height);
                warningRect.xMin += 8;
                warningRect.xMax -= 8;
                EditorGUI.HelpBox(warningRect, "", MessageType.Warning);

                var textRect = warningRect;
                textRect.xMin += 24f;
                textRect.xMax -= linkSize.x + 8;
                EditorGUI.LabelField(textRect, STRIPPED_WARNING);

                var linkRect = warningRect;
                linkRect.xMin = textRect.xMax;
                linkRect.y = textRect.y + 3;

                if (GUI.Button(linkRect, clickToConfigureContent, EditorStyles.linkLabel)) {
                    SelectActiveURPRendererAsset();
                }
            }

        }

        static bool IsParameterEnabled (SerializedDataParameter property) {
            if (!property.overrideState.boolValue) return false;
            switch (property.value.propertyType) {
                case SerializedPropertyType.Boolean: return property.value.boolValue;
                case SerializedPropertyType.Float: return property.value.floatValue > 0;
                case SerializedPropertyType.Enum: return property.value.enumValueIndex > 0;
                case SerializedPropertyType.LayerMask: return property.value.intValue != 0;
                case SerializedPropertyType.ObjectReference: return property.value.objectReferenceValue != null;
            }
            return false;
        }

        bool IsFieldStripped (string fieldName, SerializedDataParameter property) {
            switch (fieldName) {
                case "sharpenIntensity": return strippedKeywords.Contains(ShaderParams.SKW_SHARPEN);
                case "sharpenExclusionLayerMask": return strippedKeywords.Contains(ShaderParams.SKW_SHARPEN_EXCLUSION_MASK);
                case "ditherIntensity": return strippedKeywords.Contains(ShaderParams.SKW_DITHER);
                case "tonemap":
                    return (property.value.enumValueIndex == (int)Beautify.TonemapOperator.ACES && strippedKeywords.Contains(ShaderParams.SKW_TONEMAP_ACES)) ||
                           (property.value.enumValueIndex == (int)Beautify.TonemapOperator.ACESFitted && strippedKeywords.Contains(ShaderParams.SKW_TONEMAP_ACES_FITTED)) ||
                           (property.value.enumValueIndex == (int)Beautify.TonemapOperator.AGX && strippedKeywords.Contains(ShaderParams.SKW_TONEMAP_AGX)) ||
                           (property.value.enumValueIndex == (int)Beautify.TonemapOperator.PBRNeutral && strippedKeywords.Contains(ShaderParams.SKW_TONEMAP_ACES));
                case "lut": return strippedKeywords.Contains(ShaderParams.SKW_LUT3D) && strippedKeywords.Contains(ShaderParams.SKW_LUT);
                case "bloomIntensity":
                case "anamorphicFlaresIntensity":
                case "sunFlaresIntensity": return strippedKeywords.Contains(ShaderParams.SKW_BLOOM);
                case "outline": return strippedKeywords.Contains(ShaderParams.SKW_OUTLINE);
                case "nightVision": return strippedKeywords.Contains(ShaderParams.SKW_NIGHT_VISION);
                case "thermalVision": return strippedKeywords.Contains(ShaderParams.SKW_THERMAL_VISION);
                case "chromaticAberrationIntensity": return strippedKeywords.Contains(ShaderParams.SKW_CHROMATIC_ABERRATION);
                case "depthOfField": return strippedKeywords.Contains(ShaderParams.SKW_DEPTH_OF_FIELD);
                case "depthOfFieldTransparentSupport":
                case "depthOfFieldAlphaTestSupport": return strippedKeywords.Contains(ShaderParams.SKW_DEPTH_OF_FIELD_TRANSPARENT);
                case "eyeAdaptation": return strippedKeywords.Contains(ShaderParams.SKW_EYE_ADAPTATION);
                case "purkinje": return strippedKeywords.Contains(ShaderParams.SKW_PURKINJE);
                case "vignettingOuterRing": return strippedKeywords.Contains(ShaderParams.SKW_VIGNETTING);
                case "vignettingMask": return strippedKeywords.Contains(ShaderParams.SKW_VIGNETTING_MASK);
                case "filmGrainIntensity":
                case "filmGrainEnabled": return strippedKeywords.Contains(ShaderParams.SKW_FILM_GRAIN);
                case "frame": return strippedKeywords.Contains(ShaderParams.SKW_FRAME);
                case "lensDirtIntensity": return strippedKeywords.Contains(ShaderParams.SKW_DIRT);
                case "antialiasStrength": return strippedKeywords.Contains(ShaderParams.SKW_EDGE_ANTIALIASING);
                case "sepia":
                case "daltonize":
                case "colorTemp": return strippedKeywords.Contains(ShaderParams.SKW_COLOR_TWEAKS);
            }
            return false;
        }

        void SetStyles () {
            bool proSkin = EditorGUIUtility.isProSkin;
            if (sectionGroupStyle != null && stylesProSkin == proSkin) return;
            stylesProSkin = proSkin;

            // section header style
            Color titleColor = proSkin ? new Color(0.52f, 0.66f, 0.9f) : new Color(0.12f, 0.16f, 0.4f);
            GUIStyle skurikenModuleTitleStyle = "ShurikenModuleTitle";
            sectionGroupStyle = new GUIStyle(skurikenModuleTitleStyle);
            sectionGroupStyle.contentOffset = new Vector2(12f, -2f);
            sectionGroupStyle.normal.textColor = titleColor;
            sectionGroupStyle.fixedHeight = 22;
            sectionGroupStyle.fontStyle = FontStyle.Bold;

            // foldout style
            foldoutStyle = new GUIStyle(EditorStyles.foldout);
            foldoutStyle.margin = new RectOffset(0, 0, 0, 0);
        }

        bool GetSectionFoldState (string sectionName) {
            if (!sectionFoldStates.TryGetValue(sectionName, out bool state)) {
                state = EditorPrefs.GetBool(SECTION_FOLD_PREF_PREFIX + sectionName, true);
                sectionFoldStates[sectionName] = state;
            }
            return state;
        }

        void SetSectionFoldState (string sectionName, bool expanded) {
            sectionFoldStates[sectionName] = expanded;
            EditorPrefs.SetBool(SECTION_FOLD_PREF_PREFIX + sectionName, expanded);
        }

        [VolumeParameterDrawer(typeof(Beautify.MinMaxFloatParameter))]
        public class MinMaxFloatParameterDrawer : VolumeParameterDrawer {
            public override bool OnGUI (SerializedDataParameter parameter, GUIContent title) {
                if (parameter.value.propertyType == SerializedPropertyType.Vector2) {
                    var o = parameter.GetObjectRef<Beautify.MinMaxFloatParameter>();
                    var range = o.value;
                    float x = range.x;
                    float y = range.y;

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.MinMaxSlider(title, ref x, ref y, o.min, o.max);
                    x = EditorGUILayout.FloatField(x, GUILayout.Width(40));
                    y = EditorGUILayout.FloatField(y, GUILayout.Width(40));
                    if (EditorGUI.EndChangeCheck()) {
                        range.x = x;
                        range.y = y;
                        o.SetValue(new Beautify.MinMaxFloatParameter(range, o.min, o.max));
                    }
                    return true;
                }
                else {
                    EditorGUILayout.PropertyField(parameter.value);
                    return false;
                }
            }
        }

        static Texture2D MakeTex (int width, int height, Color col) {
            Color[] pix = new Color[width * height];

            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;

            TextureFormat tf = SystemInfo.SupportsTextureFormat(TextureFormat.RGBAFloat) ? TextureFormat.RGBAFloat : TextureFormat.RGBA32;
            Texture2D result = new Texture2D(width, height, tf, false);
            result.hideFlags = HideFlags.DontSave;
            result.SetPixels(pix);
            result.Apply();

            return result;
        }

        void SelectActiveURPRendererAsset () {
            var urpAsset = UniversalRenderPipeline.asset;
            if (urpAsset != null && urpAsset.scriptableRenderer is UniversalRenderer urpRenderer) {
                var rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                var rendererDataList = rendererDataField?.GetValue(urpAsset) as ScriptableRendererData[];
                if (rendererDataList?.Length > 0) {
                    Selection.activeObject = rendererDataList[0];
                }
            }
        }

    }
}
