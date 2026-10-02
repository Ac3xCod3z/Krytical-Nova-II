using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Editor
{
    [CustomEditor(typeof(DestructibleRoot))]
    public class DestructibleRootEditor : UnityEditor.Editor
    {
        bool m_showBaked;
        bool m_showBreakEffects = true;
        DestructibleRoot m_pendingRebuildRoot;
        double m_rebuildAt;
        bool m_rebuildQueued;
        bool m_isRebuilding;

        public override bool RequiresConstantRepaint() => Application.isPlaying || m_rebuildQueued;

        void OnDisable()
        {
            EditorApplication.update -= ProcessQueuedRebuild;
            if (target is DestructibleRoot root)
                SetPlaySelectionWireHidden(root, false);
        }

        public override void OnInspectorGUI()
        {
            QDUI.EnsureStyles();
            var root = (DestructibleRoot)target;
            SetPlaySelectionWireHidden(root, Application.isPlaying);

            // Branded strip.
            Rect r = GUILayoutUtility.GetRect(1f, 34f);
            GUI.DrawTexture(r, QDUI.Tex(QDUI.FromHex("#0C0C0C")), ScaleMode.StretchToFill);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1f, r.width, 1f), QDUI.Accent);
            GUI.Label(new Rect(r.x + 8f, r.y + 3f, r.width, 20f), "Quick ", QDUI.TitleAccent);
            float w = QDUI.TitleAccent.CalcSize(new GUIContent("Quick ")).x;
            GUI.Label(new Rect(r.x + 8f + w, r.y + 3f, r.width, 20f), "Destruction", QDUI.TitleWhite);
            GUILayout.Space(6f);

            // Live state.
            EditorGUILayout.BeginHorizontal();
            QDUI.StatusPill(root.State.ToString().ToUpperInvariant(), StateColor(root.State), 96f);
            GUILayout.Space(8f);
            GUILayout.Label(
                root.BakedSecondaryFragmentCount > 0
                    ? $"{root.Fragments.Count} primary + " +
                      $"{root.BakedSecondaryFragmentCount} level-two baked"
                    : $"{root.Fragments.Count} fragments baked",
                QDUI.BodyDim);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(4f);

            if (root.Profile != null)
                DrawQuickTuning(root);

            if (Application.isPlaying)
            {
                QDUI.Meter("Health", root.HealthNormalized, QDUI.Green,
                    $"{root.Health:0}");
                if (root.StructuralNode != null &&
                    root.StructuralNode.HasBaseline)
                {
                    float support = root.StructuralNode.CurrentSupportRatio;
                    float threshold = root.Profile != null
                        ? root.Profile.RequiredSupportRatio : 0f;
                    QDUI.Meter("Foundation Support", support,
                        support + 0.0001f >= threshold ? QDUI.Green : QDUI.Amber,
                        $"{support * 100f:0.#}%  " +
                        $"({root.StructuralNode.CurrentSupportUnits}/" +
                        $"{root.StructuralNode.InitialSupportUnits})");
                    GUILayout.Label(
                        $"Collapse threshold: {threshold * 100f:0.#}%. " +
                        "This combines only probes that originally touched world support; " +
                        "carried floors and roofs keep their own local support state.",
                        QDUI.HelpText);
                }
                QDUI.Meter("Active Fragments",
                    root.Profile != null && root.Profile.EnforceActiveBodyBudget &&
                    root.Profile.MaxActiveFragments > 0
                        ? (float)root.ActiveFragmentCount / root.Profile.MaxActiveFragments
                        : (root.Fragments.Count > 0
                            ? (float)root.ActiveFragmentCount / root.Fragments.Count
                            : 0f),
                    QDUI.Accent, root.ActiveFragmentCount.ToString());
                if (root.BakedSecondaryFragmentCount > 0)
                {
                    QDUI.Meter("Active Level-Two",
                        root.Profile != null && root.Profile.EnforceActiveBodyBudget &&
                        root.Profile.MaxActiveSecondaryBodies > 0
                            ? (float)root.ActiveSecondaryFragmentCount /
                              root.Profile.MaxActiveSecondaryBodies
                            : (root.BakedSecondaryFragmentCount > 0
                                ? (float)root.ActiveSecondaryFragmentCount /
                                  root.BakedSecondaryFragmentCount
                                : 0f),
                        QDUI.Amber,
                        root.ActiveSecondaryFragmentCount.ToString());
                }
                if (root.StructuralNode != null)
                {
                    var structure = root.StructuralNode;
                    QDUI.Meter("Structural Support",
                        structure.CurrentSupportRatio, QDUI.Amber,
                        structure.HasBaseline
                            ? $"{structure.CurrentSupportUnits}/{structure.InitialSupportUnits}"
                            : "NO BASELINE");
                    GUILayout.Label(
                        structure.HasBaseline
                            ? $"Requires {structure.RequiredSupportUnits} support units to remain."
                            : "No initial support was detected. This body will release after Collapse Delay; " +
                              "select Structural Support to inspect its probe distance and layers.",
                        QDUI.HelpText);
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Impact", QDUI.GhostBtn))
                {
                    float amount = root.Profile != null ? root.Profile.Health * 0.55f : 60f;
                    Vector3 impactPoint =
                        root.transform.position + Random.onUnitSphere * 0.25f;
                    root.ApplyDamage(new DamageContext(amount,
                        impactPoint, Vector3.up, 0.8f, 0, 0, -1f,
                        Vector3.zero, -1f,
                        root.ResolveStructuralGroup(impactPoint)));
                }
                if (GUILayout.Button("Explosion (Profile Radius)", QDUI.GhostBtn))
                    root.Explode(root.transform.position);
                if (GUILayout.Button("Break All", QDUI.GhostBtn))
                {
                    float energy = root.Profile != null
                        ? root.Profile.DefaultExplosionForce
                        : 30f;
                    root.BreakNow(new BreakContext(
                        root.transform.position, Vector3.up, 0f, energy));
                }
                if (GUILayout.Button("Reset", QDUI.GhostBtn))
                    root.ResetDestructible(ResetMode.Instant);
                EditorGUILayout.EndHorizontal();
                if (root.BakedSecondaryFragmentCount > 0)
                {
                    if (GUILayout.Button(
                            "Break First Active Level-Two Parent", QDUI.GhostBtn))
                    {
                        BreakFirstSecondary(root);
                    }
                }
                GUILayout.Space(6f);
            }
            else if (root.IntactRoot != null && root.FracturedRoot != null)
            {
                QDUI.BeginCard();
                QDUI.Header(root.FracturedRoot.activeSelf
                    ? "Edit Baked Pieces — FRAGMENTS SHOWN"
                    : "Edit Baked Pieces — INTACT MESH SHOWN");
                GUILayout.Label(
                    "Intact Mesh shows the original unbroken model. Baked Pieces shows the real generated " +
                    "fragment hierarchy, where every piece has its own Renderer and Collider. " +
                    "Large Whole Building bakes add a Rigidbody only when a piece detaches; " +
                    "other bakes expose the complete body pool for direct editing.",
                    QDUI.HelpText);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("SHOW INTACT MESH", QDUI.GhostBtn))
                    SetEditView(root, false);
                if (GUILayout.Button("SHOW BAKED PIECES", QDUI.GhostBtn))
                    SetEditView(root, true);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Capture Current Fragment Layout", QDUI.GhostBtn))
                    CaptureFragmentLayout(root);
                if (GUILayout.Button("Restore Captured Layout", QDUI.GhostBtn))
                    RestoreFragmentLayout(root);
                EditorGUILayout.EndHorizontal();
                GUILayout.Label(
                    "Baked Pieces deliberately stay in their original positions, so the silhouette can look " +
                    "the same as Intact Mesh. Expand Fractured in the Hierarchy to select or edit individual " +
                    "pieces; this is an edit view, not an explosion preview. After edits, capture the layout " +
                    "so Reset returns to your arrangement.",
                    QDUI.HelpText);
                QDUI.EndCard();
            }

            serializedObject.Update();
            SerializedProperty profileProperty = serializedObject.FindProperty("Profile");
#if UNITY_6000_4_OR_NEWER
            bool hasMissingProfile = profileProperty.objectReferenceValue == null &&
                                     profileProperty.objectReferenceEntityIdValue.GetHashCode() != 0;
#else
            bool hasMissingProfile = profileProperty.objectReferenceValue == null &&
                                     profileProperty.objectReferenceInstanceIDValue != 0;
#endif
            EditorGUILayout.PropertyField(profileProperty);
            if (hasMissingProfile)
            {
                EditorGUILayout.HelpBox(
                    "The baked Quick Profile asset is missing. Moving an asset with its .meta " +
                    "preserves the reference; this means the profile file or its .meta was removed.",
                    MessageType.Error);
                if (GUILayout.Button("REPAIR MISSING QUICK PROFILE", QDUI.AccentBtn))
                {
                    if (TryRepairMissingProfile(root, out string repairMessage))
                    {
                        serializedObject.Update();
                        Debug.Log("[Quick Destruction] " + repairMessage, root);
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Quick Destruction",
                            repairMessage, "OK");
                    }
                }
            }
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("FirstBreakActivation"),
                new GUIContent("First Break Scope"));
            GUILayout.Label(
                "Use Profile follows the shared asset. Local or All Pieces overrides only this destructible.",
                QDUI.HelpText);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ImpactSpreadParent"));
            if (root.ImpactSpreadParent != null && !root.transform.IsChildOf(root.ImpactSpreadParent))
                EditorGUILayout.HelpBox("Impact Spread Parent must contain this destructible. Using this root instead.", MessageType.Warning);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("StableId"));

            DrawBreakEffects(root);

            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnStructureReleasedEvent"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnSecondaryBreakEvent"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnBrokenEvent"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("OnResetEvent"));

            m_showBaked = EditorGUILayout.Foldout(m_showBaked, "Baked References", true);
            if (m_showBaked)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("IntactRoot"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("FracturedRoot"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("IntactBody"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("StructuralNode"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Fragments"), true);
                }
            }
            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUILayout.Button(
                        "EDIT THIS OBJECT IN QUICK DESTRUCTION STUDIO", QDUI.AccentBtn))
                    QuickDestructionWindow.OpenFor(root);
            }
            GUILayout.Label(
                Application.isPlaying
                    ? "Exit Play Mode to edit and preview this object's baked geometry."
                    : "Opens Fracture with this object's Source and Profile already assigned, full controls " +
                      "visible and an assembled wireframe preview scheduled automatically.",
                QDUI.HelpText);
        }

        void DrawBreakEffects(DestructibleRoot root)
        {
            GUILayout.Space(4f);
            m_showBreakEffects = EditorGUILayout.Foldout(
                m_showBreakEffects, "Optional Break Particles & Sound", true);
            if (!m_showBreakEffects) return;

            DestructionBreakEffects effects = root.GetComponent<DestructionBreakEffects>();
            if (effects == null)
            {
                GUILayout.Label(
                    "Adds ready-made preset particles and sound for every hit. Custom ParticleSystem " +
                    "and AudioClip overrides remain optional.", QDUI.HelpText);
                if (GUILayout.Button("ADD BREAK EFFECTS", QDUI.GhostBtn))
                {
                    effects = Undo.AddComponent<DestructionBreakEffects>(root.gameObject);
                    EditorUtility.SetDirty(root.gameObject);
                }
                return;
            }

            var effectObject = new SerializedObject(effects);
            effectObject.Update();
            FracturePattern effectPattern = root.Profile != null
                ? root.Profile.Pattern
                : FracturePattern.UniformConcrete;
            EditorGUILayout.LabelField("READY-TO-USE PRESET",
                effectPattern.ToString().ToUpperInvariant(), EditorStyles.miniBoldLabel);
            if (root.Profile != null)
            {
                // Nothing is auto-assigned into the override here. Drawing this inspector
                // used to fill an empty Profile Particle Override with the generic template
                // of whichever render pipeline happened to be active, and write it into the
                // profile asset. That is exactly what the schema 38 migration exists to undo:
                // the stored reference then named a pipeline rather than an effect, so the
                // same profile opened under another one drew a magenta burst, and every
                // material lost its procedural preset configuration on the way. An empty
                // field already means "use the preset burst", which the runtime resolves per
                // pipeline on its own.
                var profileObject = new SerializedObject(root.Profile);
                profileObject.Update();
                EditorGUILayout.ObjectField("Effects Profile", root.Profile,
                    typeof(DestructionProfile), false);
                EditorGUILayout.PropertyField(
                    profileObject.FindProperty("PlayHitEffectsOnEveryHit"),
                    new GUIContent("Play On Every Hit"));
                EditorGUILayout.PropertyField(
                    profileObject.FindProperty("UseDefaultHitParticles"),
                    new GUIContent("Preset Particles"));
                EditorGUILayout.PropertyField(
                    profileObject.FindProperty("HitParticlePrefab"),
                    new GUIContent("Profile Particle Override"));
                EditorGUILayout.PropertyField(
                    profileObject.FindProperty("UseDefaultHitSound"),
                    new GUIContent("Preset Sound"));
                EditorGUILayout.PropertyField(
                    profileObject.FindProperty("HitSound"),
                    new GUIContent("Profile Sound Override"));
                EditorGUILayout.PropertyField(
                    profileObject.FindProperty("HitSoundVariants"),
                    new GUIContent("Profile Sound Variants"), true);
                profileObject.ApplyModifiedProperties();
                EditorGUILayout.HelpBox(
                    "CONNECTED TO PROFILE: empty override fields use the selected preset's " +
                    "procedural particle burst and synthesized hit sound. Filling a variant " +
                    "list makes every break draw one entry at random from the override plus " +
                    "that list.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.PropertyField(effectObject.FindProperty("UsePresetParticles"),
                    new GUIContent("Preset Particles"));
                EditorGUILayout.PropertyField(effectObject.FindProperty("UsePresetSound"),
                    new GUIContent("Preset Sound"));
                EditorGUILayout.PropertyField(effectObject.FindProperty("PlayOnEveryHit"),
                    new GUIContent("Play On Every Hit"));
            }
            GUILayout.Label(
                "Enabled by default. The current destruction preset supplies a ready-made " +
                "particle and sound for every hit, including later hits after fracture.",
                QDUI.HelpText);

            EditorGUILayout.PropertyField(effectObject.FindProperty("ParticlePrefab"),
                new GUIContent("Custom Particle Override"));
            EditorGUILayout.PropertyField(effectObject.FindProperty("BreakSound"),
                new GUIContent("Custom Sound Override"));
            EditorGUILayout.PropertyField(effectObject.FindProperty("SoundVariants"),
                new GUIContent("Custom Sound Variants"), true);
            EditorGUILayout.PropertyField(
                effectObject.FindProperty("GroundDustSoundVariants"),
                new GUIContent("Landing Sound Variants"), true);
            EditorGUILayout.PropertyField(effectObject.FindProperty("AlignParticlesToBreakNormal"),
                new GUIContent("Align To Break Normal"));
            EditorGUILayout.PropertyField(effectObject.FindProperty("ParticleScale"));
            EditorGUILayout.PropertyField(effectObject.FindProperty("SoundVolume"));
            EditorGUILayout.PropertyField(effectObject.FindProperty("PitchVariation"));
            EditorGUILayout.PropertyField(effectObject.FindProperty("SpatialBlend"));
            if (effects.SpatialBlend > 0f)
            {
                EditorGUILayout.PropertyField(effectObject.FindProperty("MinDistance"));
                EditorGUILayout.PropertyField(effectObject.FindProperty("MaxDistance"));
            }
            effectObject.ApplyModifiedProperties();

            ParticleSystem effectiveParticle = effects.ParticlePrefab != null
                ? effects.ParticlePrefab
                : root.Profile != null ? root.Profile.HitParticlePrefab : null;
            if (effectiveParticle != null && effectiveParticle.main.loop)
            {
                EditorGUILayout.HelpBox(
                    "Use a non-looping ParticleSystem burst for automatic pool return. " +
                    "A looping effect remains active until this destructible is reset.",
                    MessageType.Warning);
            }

            GUILayout.Label(
                "Custom references are optional and replace their matching preset effect. Playback " +
                "is pooled, follows the exact hit point, and is stopped automatically by Reset.",
                QDUI.HelpText);
            if (Application.isPlaying)
            {
                using (new EditorGUI.DisabledScope(!effects.HasAnyEffect))
                    if (GUILayout.Button("TEST FX AT OBJECT CENTER", QDUI.GhostBtn))
                        effects.PlayAt(root.transform.position, Vector3.up);
            }
        }

        static void SetPlaySelectionWireHidden(DestructibleRoot root, bool hidden)
        {
            if (root == null) return;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            EditorSelectedRenderState state = hidden
                ? EditorSelectedRenderState.Hidden
                : EditorSelectedRenderState.Wireframe |
                  EditorSelectedRenderState.Highlight;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null)
                    EditorUtility.SetSelectedRenderState(renderers[i], state);
        }

        static void DrawDirectionalBiasRows(DestructionProfile profile,
            string axisLabel = "Preferred Length Axis")
        {
            profile.DirectionalCutStrength = QDUI.SliderRow(
                "Directional Influence", profile.DirectionalCutStrength, 0f, 1f);
            using (new EditorGUI.DisabledScope(profile.DirectionalCutStrength <= 0.0001f))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(axisLabel, QDUI.FieldLabel, GUILayout.Width(QDUI.LabelWidth));
                profile.PreferredCutAxis = (AxisMode)EditorGUILayout.EnumPopup(
                    profile.PreferredCutAxis);
                EditorGUILayout.EndHorizontal();
                if (profile.Pattern == FracturePattern.DirectionalWood ||
                    profile.Pattern == FracturePattern.LayeredSlabs)
                {
                    profile.LengthwiseSplitBias = QDUI.SliderRow(
                        "Lengthwise Split Bias", profile.LengthwiseSplitBias, 0f, 1f);
                }
            }
        }

        /// <summary>
        /// Says out loud where the push sliders stop doing anything. A launch impulse divided
        /// by mass IS a speed, so every push value above mass × Max Fragment Launch Speed is
        /// trimmed back to the same result. On light materials that point arrives almost
        /// immediately, which is why raising Break Push or a weapon's push energy can feel
        /// like it changes nothing at all. Reported against this object's own baked masses
        /// rather than in the abstract.
        /// </summary>
        static void DrawLaunchSaturation(DestructibleRoot root,
            DestructionProfile profile)
        {
            if (root == null || profile == null || root.Fragments == null) return;

            float lightest = float.MaxValue;
            float total = 0f;
            int counted = 0;
            for (int i = 0; i < root.Fragments.Count; i++)
            {
                FragmentRecord fragment = root.Fragments[i];
                float mass = fragment != null && fragment.Body != null
                    ? fragment.Body.mass
                    : fragment != null ? fragment.Mass : 0f;
                if (mass <= 0f) continue;
                lightest = Mathf.Min(lightest, mass);
                total += mass;
                counted++;
            }
            if (counted == 0) return;

            float typical = total / counted;
            float cap = Mathf.Max(0.01f, profile.MaxFragmentLaunchSpeed);
            float typicalSaturation = typical * cap;
            float lightestSaturation = lightest * cap;
            float authored = Mathf.Max(0f, profile.DefaultExplosionForce) *
                             Mathf.Max(0f, profile.FragmentPushMultiplier);
            string unit = "N·s";

            var text = new System.Text.StringBuilder();
            text.Append("Where push stops mattering: a typical ")
                .Append(typical.ToString("0.###")).Append(" kg piece here reaches the ")
                .Append(cap.ToString("0.0")).Append(" m/s cap at ")
                .Append(typicalSaturation.ToString("0.##")).Append(' ').Append(unit)
                .Append(" of push");
            if (lightestSaturation < typicalSaturation * 0.95f)
            {
                text.Append(", the lightest at ")
                    .Append(lightestSaturation.ToString("0.##")).Append(' ').Append(unit);
            }
            text.Append(". ");

            if (authored > 0f)
            {
                float ratio = authored / Mathf.Max(0.0001f, typicalSaturation);
                text.Append("Default Explosion Force ")
                    .Append(profile.DefaultExplosionForce.ToString("0.#"))
                    .Append(" x Break Push ")
                    .Append(profile.FragmentPushMultiplier.ToString("0.00"))
                    .Append(" = ").Append(authored.ToString("0.#")).Append(' ')
                    .Append(unit).Append(", ");
                text.Append(ratio >= 1.05f
                    ? ratio.ToString("0.#") +
                      "x past it: raising either changes nothing, and Max Fragment " +
                      "Launch Speed is the knob that still does."
                    : ratio <= 0.95f
                        ? "which is only " + (ratio * 100f).ToString("0") +
                          "% of it, so there is room for more force before the cap bites."
                        : "right at it.");
            }
            else
            {
                text.Append("Break Push is 0x, so breaks add no outward kick at all.");
            }

            GUILayout.Label(text.ToString(), QDUI.HelpText);
        }

        void DrawQuickTuning(DestructibleRoot root)
        {
            DestructionProfile profile = root.Profile;
            if (profile == null) return;

            QDUI.BeginCard();
            QDUI.Header("Quick Destruction Settings");
            GUILayout.Label(
                "The most common controls are available directly on Destructible Root. Baked " +
                "settings automatically rebuild this object's prefab shortly after you stop " +
                "dragging. Break Push and Directional Impact Force update the runtime response " +
                "immediately.", QDUI.HelpText);

            int previousCount = profile.FragmentCount;
            int previousSpokes = profile.RadialSpokes;
            int previousRings = profile.RadialRings;
            Vector3 previousCellSize = profile.BrickCellSize;
            float previousDirectionalStrength = profile.DirectionalCutStrength;
            AxisMode previousDirectionalAxis = profile.PreferredCutAxis;
            AxisMode previousPillarAxis = profile.PillarAxis;
            float previousImpactFocus = profile.ImpactFocus;
            float previousFragmentShape = profile.FragmentShapeIrregularity;
            float previousConcreteScatter = profile.ConcreteIrregularity;
            float previousWoodFiber = profile.FiberCutShare;
            float previousWoodIrregularity = profile.WoodSplitIrregularity;
            float previousWoodSharpness = profile.WoodEndSharpness;
            float previousPush = profile.FragmentPushMultiplier;
            float previousDirectionalImpact = profile.DirectionalImpactMultiplier;
            float previousMaxLaunchSpeed = profile.MaxFragmentLaunchSpeed;
            float previousMaxWholeBodySpeed = profile.MaxWholeBodyLaunchSpeed;

            EditorGUI.BeginChangeCheck();
            Undo.RecordObject(profile, "Edit Quick Destruction Settings");
            switch (profile.Pattern)
            {
                case FracturePattern.UniformConcrete:
                    profile.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", profile.FragmentCount, 2, 250);
                    profile.FragmentShapeIrregularity = QDUI.SliderRow(
                        "Fragment Shape Irregularity",
                        profile.FragmentShapeIrregularity, 0f, 1f);
                    profile.ConcreteIrregularity = QDUI.SliderRow(
                        "Interior Stone Scatter", profile.ConcreteIrregularity, 0f, 1f);
                    GUILayout.Label(
                        "Fragment Shape Irregularity changes the real cut layout and baked pieces. " +
                        "Interior Stone Scatter adds sparse render-only chips without changing " +
                        "the seed, cut layout or collider.",
                        QDUI.HelpText);
                    break;

                case FracturePattern.RadialGlass:
                    profile.RadialSpokes = QDUI.IntSliderRow(
                        "Glass Spokes", profile.RadialSpokes, 3, 24);
                    profile.RadialRings = QDUI.IntSliderRow(
                        "Glass Rings", profile.RadialRings, 1, 8);
                    GUILayout.Label(
                        "Spokes control divisions around the impact center; Rings add divisions " +
                        "toward the pane edge.", QDUI.HelpText);
                    break;

                case FracturePattern.BrickCells when profile.BrickUseCellSize:
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Brick Cell Size", QDUI.FieldLabel,
                        GUILayout.Width(QDUI.LabelWidth));
                    profile.BrickCellSize = EditorGUILayout.Vector3Field(
                        GUIContent.none, profile.BrickCellSize);
                    EditorGUILayout.EndHorizontal();
                    profile.BrickCellSize = new Vector3(
                        Mathf.Max(0.01f, profile.BrickCellSize.x),
                        Mathf.Max(0.01f, profile.BrickCellSize.y),
                        Mathf.Max(0.01f, profile.BrickCellSize.z));
                    GUILayout.Label(
                        "Smaller cells create more brick pieces; larger cells create fewer pieces.",
                        QDUI.HelpText);
                    break;

                case FracturePattern.DirectionalWood:
                    profile.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", profile.FragmentCount, 2, 250);
                    DrawDirectionalBiasRows(profile, "Grain / Length Axis");
                    profile.WoodEndSharpness = QDUI.SliderRow(
                        "Splinter End Sharpness", profile.WoodEndSharpness, 0f, 1f);
                    profile.WoodSplitIrregularity = QDUI.SliderRow(
                        "Interior Splinter Scatter", profile.WoodSplitIrregularity, 0f, 1f);
                    GUILayout.Label(
                        "0 cuts mostly across this element's local width; 1 splits mostly along its " +
                        "local length. Auto evaluates every disconnected plank independently. End " +
                        "Sharpness creates wedge-like tips; Interior Splinter Scatter adds a few " +
                        "render-only splinters without changing the seed, cut layout or collider.",
                        QDUI.HelpText);
                    break;

                case FracturePattern.LayeredSlabs:
                    profile.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", profile.FragmentCount, 2, 250);
                    DrawDirectionalBiasRows(profile, "Plate Length Axis");
                    GUILayout.Label(
                        "Directional Influence steers the cross-cuts inside each slab layer.",
                        QDUI.HelpText);
                    break;

                case FracturePattern.ColumnarPillars:
                    profile.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", profile.FragmentCount, 2, 250);
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Label("Pillar Length Axis", QDUI.FieldLabel,
                        GUILayout.Width(QDUI.LabelWidth));
                    profile.PillarAxis = (AxisMode)EditorGUILayout.EnumPopup(profile.PillarAxis);
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Label(
                        "Auto evaluates every disconnected pillar, post or beam independently. " +
                        "Choose X, Y or Z only to force one shared length direction.",
                        QDUI.HelpText);
                    break;

                case FracturePattern.ImpactShatter:
                    profile.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", profile.FragmentCount, 2, 250);
                    profile.ImpactFocus = QDUI.SliderRow(
                        "Impact Focus", profile.ImpactFocus, 0f, 1f);
                    GUILayout.Label(
                        "Impact is radial. Focus controls fragment concentration around the hit; " +
                        "this pattern intentionally ignores preferred length directions.",
                        QDUI.HelpText);
                    break;

                default:
                    profile.FragmentCount = QDUI.IntSliderRow(
                        "Max Parts", profile.FragmentCount, 2, 250);
                    GUILayout.Label(
                        "Maximum primary-piece count. The solver may return fewer pieces when a " +
                        "cut would create invalid or extremely small geometry.", QDUI.HelpText);
                    break;
            }

            profile.FragmentPushMultiplier = QDUI.SliderRow(
                "Break Push (Launch Force)", profile.FragmentPushMultiplier, 0f, 5f, "0.00x");
            profile.DirectionalImpactMultiplier = QDUI.SliderRow(
                "Directional Impact Force", profile.DirectionalImpactMultiplier,
                0f, 5f, "0.00x");
            profile.MaxFragmentLaunchSpeed = QDUI.SliderRow(
                "Max Fragment Launch Speed", profile.MaxFragmentLaunchSpeed,
                1f, 50f, "0.0 m/s");
            profile.MaxWholeBodyLaunchSpeed = QDUI.SliderRow(
                "Max Whole Body Push Speed", profile.MaxWholeBodyLaunchSpeed,
                1f, 50f, "0.0 m/s");
            GUILayout.Label(
                "How strongly pieces are kicked outward when breaking: 0× adds no kick, 1× is the " +
                "normal physical response, and above 1× makes collision, scripted and explosion " +
                "breaks throw debris harder. Directional Impact Force adds a mass-dependent impulse " +
                "in the incoming projectile or ray direction, so shotgun debris continues away " +
                "from the attacker even when pieces are heavy; 0× keeps the legacy radial-only " +
                "response. It defaults to 0; high values create a strong shotgun push. " +
                "Fragment speed caps loose debris; Whole Body Push separately caps an intact " +
                "Dynamic Rigidbody and its connected remainder.",
                QDUI.HelpText);
            DrawLaunchSaturation(root, profile);

            bool changed = EditorGUI.EndChangeCheck();
            bool scatterChanged = !Mathf.Approximately(
                                      previousConcreteScatter, profile.ConcreteIrregularity) ||
                                  !Mathf.Approximately(
                                      previousWoodIrregularity, profile.WoodSplitIrregularity);
            bool layoutChanged = previousCount != profile.FragmentCount ||
                                   !Mathf.Approximately(previousFragmentShape,
                                       profile.FragmentShapeIrregularity) ||
                                   previousSpokes != profile.RadialSpokes ||
                                   previousRings != profile.RadialRings ||
                                   previousCellSize != profile.BrickCellSize ||
                                   !Mathf.Approximately(previousDirectionalStrength, profile.DirectionalCutStrength) ||
                                   previousDirectionalAxis != profile.PreferredCutAxis ||
                                   previousPillarAxis != profile.PillarAxis ||
                                   !Mathf.Approximately(previousImpactFocus, profile.ImpactFocus) ||
                                   !Mathf.Approximately(previousWoodFiber, profile.FiberCutShare) ||
                                   !Mathf.Approximately(previousWoodSharpness, profile.WoodEndSharpness);
            bool pushChanged = !Mathf.Approximately(
                                   previousPush, profile.FragmentPushMultiplier) ||
                               !Mathf.Approximately(previousDirectionalImpact,
                                   profile.DirectionalImpactMultiplier) ||
                               !Mathf.Approximately(previousMaxLaunchSpeed,
                                   profile.MaxFragmentLaunchSpeed) ||
                               !Mathf.Approximately(previousMaxWholeBodySpeed,
                                   profile.MaxWholeBodyLaunchSpeed);
            if (changed)
            {
                EditorUtility.SetDirty(profile);
                if (layoutChanged || scatterChanged)
                {
                    // Scatter is baked separately from the closed fragment geometry.
                    // Only layout controls should flash the cut-plan overlay.
                    if (layoutChanged)
                        PatternHintOverlay.Show(root.gameObject, profile, profile.Pattern);
                    QueueRebuild(root);
                    SceneView.lastActiveSceneView?.ShowNotification(
                        new GUIContent("Geometry changed — rebuilding baked pieces after adjustment."));
                }
                else if (pushChanged)
                    SceneView.RepaintAll();
            }

            GUILayout.Label(
                m_rebuildQueued && m_pendingRebuildRoot == root
                    ? "Baked result update queued: this object's prefab will rebuild when you stop adjusting the control."
                    : $"Current prefab: {root.Fragments.Count} baked primary parts. " +
                      "The profile can be shared by multiple destructibles.", QDUI.HelpText);
            QDUI.EndCard();
        }

        void QueueRebuild(DestructibleRoot root)
        {
            if (root == null || Application.isPlaying) return;
            m_pendingRebuildRoot = root;
            m_rebuildQueued = true;
            m_rebuildAt = EditorApplication.timeSinceStartup + 0.55d;
            EditorApplication.update -= ProcessQueuedRebuild;
            EditorApplication.update += ProcessQueuedRebuild;
            Repaint();
        }

        void ProcessQueuedRebuild()
        {
            if (!m_rebuildQueued || m_isRebuilding) return;
            if (EditorApplication.timeSinceStartup < m_rebuildAt) return;

            DestructibleRoot root = m_pendingRebuildRoot;
            m_rebuildQueued = false;
            m_pendingRebuildRoot = null;
            EditorApplication.update -= ProcessQueuedRebuild;
            if (root == null || root.Profile == null || !root.gameObject.scene.IsValid())
            {
                Repaint();
                return;
            }

            m_isRebuilding = true;
            try
            {
                BakeOutcome outcome = BakePipeline.Bake(root.gameObject, root.Profile, true);
                if (outcome.Success)
                {
                    SceneView.lastActiveSceneView?.ShowNotification(
                        new GUIContent($"Rebuilt {outcome.FragmentCount} baked pieces."));
                }
                else
                {
                    Debug.LogWarning("[Quick Destruction] Automatic rebuild failed: " +
                                     outcome.Error, root);
                }
            }
            finally
            {
                m_isRebuilding = false;
                Repaint();
            }
        }

        static void SetEditView(DestructibleRoot root, bool fragments)
        {
            Undo.RecordObject(root.IntactRoot, "Change Destruction Edit View");
            Undo.RecordObject(root.FracturedRoot, "Change Destruction Edit View");
            root.IntactRoot.SetActive(!fragments);
            root.FracturedRoot.SetActive(fragments);
            EditorUtility.SetDirty(root.IntactRoot);
            EditorUtility.SetDirty(root.FracturedRoot);
            SceneView.RepaintAll();
        }

        static void CaptureFragmentLayout(DestructibleRoot root)
        {
            Undo.RecordObject(root, "Capture Destruction Fragment Layout");
            foreach (var fragment in root.Fragments)
            {
                if (fragment == null) continue;
                if (fragment.Transform != null)
                {
                    fragment.InitialLocalPosition = fragment.Transform.localPosition;
                    fragment.InitialLocalRotation = fragment.Transform.localRotation;
                    fragment.InitialLocalScale = fragment.Transform.localScale;
                }

                // Level-two children reset through their SecondaryFragmentNode, so
                // their edited layout is captured there as well.
                var node = fragment.SecondaryNode;
                if (node == null) continue;
                Undo.RecordObject(node, "Capture Destruction Fragment Layout");
                foreach (var child in node.Children)
                {
                    if (child == null || child.Transform == null) continue;
                    child.InitialLocalPosition = child.Transform.localPosition;
                    child.InitialLocalRotation = child.Transform.localRotation;
                    child.InitialLocalScale = child.Transform.localScale;
                }
                EditorUtility.SetDirty(node);
            }
            EditorUtility.SetDirty(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root);
            SceneView.lastActiveSceneView?.ShowNotification(
                new GUIContent("Fragment reset layout captured."));
        }

        static void RestoreFragmentLayout(DestructibleRoot root)
        {
            foreach (var fragment in root.Fragments)
            {
                if (fragment == null) continue;
                if (fragment.Transform != null)
                {
                    Undo.RecordObject(fragment.Transform, "Restore Destruction Fragment Layout");
                    fragment.Transform.localPosition = fragment.InitialLocalPosition;
                    fragment.Transform.localRotation = fragment.InitialLocalRotation;
                    fragment.Transform.localScale = fragment.InitialLocalScale;
                    EditorUtility.SetDirty(fragment.Transform);
                }

                var node = fragment.SecondaryNode;
                if (node == null) continue;
                foreach (var child in node.Children)
                {
                    if (child == null || child.Transform == null) continue;
                    Undo.RecordObject(child.Transform, "Restore Destruction Fragment Layout");
                    child.Transform.localPosition = child.InitialLocalPosition;
                    child.Transform.localRotation = child.InitialLocalRotation;
                    child.Transform.localScale = child.InitialLocalScale;
                    EditorUtility.SetDirty(child.Transform);
                }
            }
            SceneView.RepaintAll();
        }

        static void BreakFirstSecondary(DestructibleRoot root)
        {
            for (int i = 0; i < root.Fragments.Count; i++)
            {
                FragmentRecord fragment = root.Fragments[i];
                if (fragment?.SecondaryNode == null ||
                    !root.CanSecondaryBreak(i))
                    continue;

                fragment.SecondaryNode.BreakSecondary(
                    fragment.Transform != null
                        ? fragment.Transform.position : root.transform.position,
                    Vector3.up);
                return;
            }
        }

        static bool TryRepairMissingProfile(DestructibleRoot root, out string message)
        {
            string profileName = FindBakedProfileName(root);
            if (string.IsNullOrEmpty(profileName))
            {
                message = "Could not determine the baked profile name. Open this object in " +
                          "Quick Destruction Studio, choose a profile and Bake & Setup again.";
                return false;
            }

            BakePipeline.EnsureQuickProfileRoot();
            string profilePath = BakePipeline.QuickProfileFolder + "/" +
                                 BakePipeline.MakeSafeFileName(profileName) + ".asset";
            DestructionProfile profile =
                AssetDatabase.LoadAssetAtPath<DestructionProfile>(profilePath);
            if (profile == null)
            {
                if (!TryPatternFromProfileName(profileName, out FracturePattern pattern))
                {
                    message = $"The manifest names '{profileName}', but no matching profile " +
                              "asset exists and its preset cannot be reconstructed safely.";
                    return false;
                }

                profile = CreateInstance<DestructionProfile>();
                profile.name = profileName;
                profile.ApplyPreset(pattern);
                AssetDatabase.CreateAsset(profile, profilePath);
                AssetDatabase.SaveAssets();
            }

            AssignProfile(root, profile);
            DestructibleRoot prefabRoot = GetSourcePrefabRoot(root);
            if (prefabRoot != null && prefabRoot != root)
                AssignProfile(prefabRoot, profile);
            AssetDatabase.SaveAssets();

            message = $"Repaired missing profile '{profileName}' using the protected QuickProfiles folder.";
            return true;
        }

        static void AssignProfile(DestructibleRoot root, DestructionProfile profile)
        {
            if (root == null || root.Profile == profile) return;
            Undo.RecordObject(root, "Repair Quick Destruction Profile");
            root.Profile = profile;
            EditorUtility.SetDirty(root);
            if (PrefabUtility.IsPartOfPrefabInstance(root))
                PrefabUtility.RecordPrefabInstancePropertyModifications(root);
        }

        static DestructibleRoot GetSourcePrefabRoot(DestructibleRoot root)
        {
            if (root == null || !PrefabUtility.IsPartOfPrefabInstance(root)) return null;
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(root.gameObject);
            return source != null ? source.GetComponent<DestructibleRoot>() : null;
        }

        static string FindBakedProfileName(DestructibleRoot root)
        {
            string prefabPath = root != null && EditorUtility.IsPersistent(root)
                ? AssetDatabase.GetAssetPath(root.gameObject)
                : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    root != null ? root.gameObject : null);
            if (string.IsNullOrEmpty(prefabPath)) return null;

            string folder = System.IO.Path.GetDirectoryName(prefabPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder)) return null;
            foreach (string guid in AssetDatabase.FindAssets(
                         "t:GeneratedManifest", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/'),
                        folder, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                GeneratedManifest manifest =
                    AssetDatabase.LoadAssetAtPath<GeneratedManifest>(path);
                if (manifest != null && !string.IsNullOrEmpty(manifest.ProfileName))
                    return manifest.ProfileName;
            }
            return null;
        }

        static bool TryPatternFromProfileName(string profileName,
            out FracturePattern pattern)
        {
            foreach (FracturePattern candidate in
                     System.Enum.GetValues(typeof(FracturePattern)))
            {
                if (profileName.IndexOf(candidate.ToString(),
                        System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                pattern = candidate;
                return true;
            }
            pattern = default;
            return false;
        }

        static Color StateColor(DestructibleState state)
        {
            switch (state)
            {
                case DestructibleState.Intact: return QDUI.Green;
                case DestructibleState.Damaged:
                case DestructibleState.Cracked: return QDUI.Amber;
                case DestructibleState.Released: return QDUI.Red;
                case DestructibleState.Fractured: return QDUI.Accent;
                default: return QDUI.Blue;
            }
        }
    }

    [CustomEditor(typeof(SecondaryFragmentNode))]
    public class SecondaryFragmentNodeEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            QDUI.EnsureStyles();
            var node = (SecondaryFragmentNode)target;

            QDUI.BeginCard();
            QDUI.Header("Secondary Breakup");
            GUILayout.Label(
                "This primary fragment contains a pre-baked second level. Strong impacts reduce " +
                "Secondary Health and replace the parent with these child pieces.",
                QDUI.HelpText);
            QDUI.StatusPill(node.IsBroken ? "BROKEN" : "READY",
                node.IsBroken ? QDUI.Accent : QDUI.Green, 86f);
            if (Application.isPlaying)
            {
                QDUI.Meter("Secondary Health", node.HealthNormalized, QDUI.Amber,
                    $"{node.Health:0}");
                QDUI.Meter("Active Children",
                    node.Children.Count > 0
                        ? (float)node.ActiveChildCount / node.Children.Count : 0f,
                    QDUI.Blue, node.ActiveChildCount.ToString());
                using (new EditorGUI.DisabledScope(node.IsBroken))
                {
                    if (GUILayout.Button("Break This Fragment", QDUI.GhostBtn))
                        node.BreakSecondary(node.transform.position, Vector3.up);
                }
            }
            QDUI.EndCard();

            serializedObject.Update();
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("ParentFragmentIndex"));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("SecondaryRoot"));
                EditorGUILayout.PropertyField(
                    serializedObject.FindProperty("Children"), true);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }

    [CustomEditor(typeof(StructuralSupportNode))]
    public class StructuralSupportNodeEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            QDUI.EnsureStyles();
            var node = (StructuralSupportNode)target;
            var profile = node.Root != null ? node.Root.Profile : null;

            QDUI.BeginCard();
            QDUI.Header("Structural Support");
            GUILayout.Label(
                "This component holds the intact Rigidbody, then monitors the remaining kinematic " +
                "fragment shell after a local break. It releases either stage when the configured " +
                "share of support is lost. Orange Scene gizmos show the downward probes.",
                QDUI.HelpText);

            if (profile != null)
            {
                QDUI.InfoBox(
                    "Mode: Automatic Below\n" +
                    $"Required: {profile.RequiredSupportRatio * 100f:0}%  •  " +
                    $"Distance: {profile.SupportProbeDistance:0.##} m  •  " +
                    $"Delay: {profile.SupportGraceTime:0.##} s");
            }
            else
            {
                QDUI.WarnBox("Assign a Destruction Profile on the DestructibleRoot.");
            }

            if (Application.isPlaying)
            {
                QDUI.Meter("Remaining Support", node.CurrentSupportRatio, QDUI.Amber,
                    node.HasBaseline
                        ? $"{node.CurrentSupportUnits}/{node.InitialSupportUnits}"
                        : "NO BASELINE");
                GUILayout.Label(
                    node.HasBaseline
                        ? $"Release threshold: {node.RequiredSupportUnits} remaining support units."
                        : "No support was captured. The object will release after Collapse Delay. " +
                          "Check layers and probe distance if that is unexpected.",
                    QDUI.HelpText);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Rebuild Baseline", QDUI.GhostBtn))
                    node.RebuildSupportBaseline();
                using (new EditorGUI.DisabledScope(node.Released))
                {
                    if (GUILayout.Button("Release Now", QDUI.GhostBtn))
                        node.ReleaseNow();
                }
                EditorGUILayout.EndHorizontal();
            }
            QDUI.EndCard();

        }
    }

    internal static class QuickDestructionMenu
    {
        [MenuItem("GameObject/Quick Destruction/Make Destructible", false, 49)]
        static void MakeDestructible()
        {
            QuickDestructionWindow.OpenWith(Selection.activeGameObject);
        }

        [MenuItem("GameObject/Quick Destruction/Make Destructible", true)]
        static bool ValidateMakeDestructible() => Selection.activeGameObject != null;
    }
}
