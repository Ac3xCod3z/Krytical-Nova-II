using System.Text;
using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.DemoEditor
{
    /// <summary>
    /// Replaces Click Push Energy's open-ended number field with a slider over the range where
    /// the value still changes anything.
    ///
    /// A launch impulse divided by mass IS a speed, so the profile's Max Fragment Launch Speed
    /// trims every push above <c>mass x cap / Break Push</c> back to the same result. That point
    /// is entirely material-dependent — around 1 N·s for a 0.15 kg barrel stave and several
    /// hundred for a stone block — so no fixed slider range can suit both. The bound is
    /// therefore measured from the destructibles actually present in the scene.
    /// </summary>
    // Fully qualified: this namespace sits under RipVertices.QuickDestruction, where the
    // package's own Editor namespace would otherwise win over UnityEditor.Editor.
    [CustomEditor(typeof(DestructibleDemoController))]
    public class DestructibleDemoControllerEditor : UnityEditor.Editor
    {
        const string PushEnergyField = "ClickPushEnergy";
        const string FallbackDistanceField = "ExplosionFallbackDistance";
        // The inspector repaints on every mouse move; a scene-wide search per repaint is not
        // worth it for a figure that only changes when a profile or a bake does.
        const double SaturationCacheSeconds = 0.5;
        double m_saturationCachedAt = double.NegativeInfinity;
        float m_cachedLightest;
        float m_cachedHeaviest;
        string m_cachedHeaviestName = string.Empty;
        int m_cachedMeasured;
        bool m_cachedImpactSpread;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty property = serializedObject.GetIterator();
            bool enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (property.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(property);
                    continue;
                }
                if (property.propertyPath == PushEnergyField)
                {
                    DrawClickPushEnergy(property);
                    continue;
                }
                if (property.propertyPath == FallbackDistanceField)
                {
                    EditorGUILayout.PropertyField(property,
                        new GUIContent("No-Hit Aim Distance", property.tooltip), true);
                    continue;
                }
                EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        void DrawClickPushEnergy(SerializedProperty property)
        {
            RefreshSaturationCache();
            float lightest = m_cachedLightest;
            float heaviest = m_cachedHeaviest;
            string heaviestName = m_cachedHeaviestName;
            int measured = m_cachedMeasured;

            // A quarter of headroom past the last thing in the scene that still reacts, so the
            // top of the slider is visibly "past everything" rather than exactly at the edge.
            float useful = measured > 0
                ? Mathf.Max(1f, Mathf.Ceil(heaviest * 1.25f))
                : 50f;

            if (m_cachedImpactSpread)
            {
                // Keep authored impulse values reachable; spread radius is fixed.
                float spreadRange = Mathf.Max(400f, useful, property.floatValue);
                property.floatValue = EditorGUILayout.Slider("Click Push Energy",
                    property.floatValue, 0f, spreadRange);
                GUILayout.Label(
                    "Impulse in N s. A target has impact spread enabled: this controls collateral push. " +
                    "Higher force does not increase the radius. " +
                    "Use Impact Spread Radius on that target's profile to adjust the range. " +
                    "Click Damage independently controls the damage carried by the wave.",
                    EditorStyles.wordWrappedMiniLabel);
                return;
            }

            if (property.floatValue > useful)
            {
                // Never clamp silently: selecting the object must not quietly rewrite a value
                // the user typed. Show it, explain it, and offer the correction as a button.
                EditorGUILayout.PropertyField(property,
                    new GUIContent("Click Push Energy"));
                EditorGUILayout.HelpBox(
                    $"{property.floatValue:0.#} is past the point where this does anything. " +
                    $"Everything in the scene already reaches its launch-speed cap by " +
                    $"{useful:0.#}, so every value above that produces an identical hit. " +
                    "Raise Max Fragment Launch Speed on the profile to hit harder.",
                    MessageType.Warning);
                if (GUILayout.Button($"Bring into range ({useful:0.#})"))
                    property.floatValue = useful;
            }
            else
            {
                property.floatValue = EditorGUILayout.Slider("Click Push Energy",
                    property.floatValue, 0f, useful);
            }

            GUILayout.Label(BuildSaturationHelp(lightest, heaviest, heaviestName,
                measured, useful), EditorStyles.wordWrappedMiniLabel);
        }

        static string BuildSaturationHelp(float lightest, float heaviest,
            string heaviestName, int measured, float useful)
        {
            if (measured == 0)
            {
                return "No baked destructible is in the scene, so the useful range cannot be " +
                       "measured; the slider falls back to 0-50. The real limit is the " +
                       "profile's Max Fragment Launch Speed: any push above a piece's mass " +
                       "times that speed is trimmed back to it.";
            }

            var text = new StringBuilder();
            text.Append("Slider ends at ").Append(useful.ToString("0.#"))
                .Append(" because nothing in this scene reacts to more. ");
            if (heaviest > lightest * 1.05f)
            {
                text.Append("Lightest debris here stops gaining speed at ")
                    .Append(lightest.ToString("0.##"))
                    .Append(", the heaviest (").Append(heaviestName).Append(") at ")
                    .Append(heaviest.ToString("0.#")).Append(". ");
            }
            else
            {
                text.Append("Debris here stops gaining speed at about ")
                    .Append(heaviest.ToString("0.##")).Append(". ");
            }
            text.Append("Past a piece's own figure the launch is identical, so the way to hit " +
                        "harder is Max Fragment Launch Speed on the profile.");
            return text.ToString();
        }

        void RefreshSaturationCache()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - m_saturationCachedAt < SaturationCacheSeconds) return;
            m_saturationCachedAt = now;
            ResolveSceneSaturation(out m_cachedLightest, out m_cachedHeaviest,
                out m_cachedHeaviestName, out m_cachedMeasured, out m_cachedImpactSpread);
        }

        /// <summary>
        /// Push energy at which the lightest and heaviest baked pieces in the scene reach their
        /// profile's launch-speed cap. Break Push is included because it multiplies this value
        /// before the cap is applied, so a profile with a 3x multiplier saturates three times
        /// sooner.
        /// </summary>
        static void ResolveSceneSaturation(out float lightest, out float heaviest,
            out string heaviestName, out int measured, out bool impactSpread)
        {
            lightest = float.MaxValue;
            heaviest = 0f;
            heaviestName = string.Empty;
            measured = 0;
            impactSpread = false;

            DestructibleRoot[] roots = FindRoots();
            for (int r = 0; r < roots.Length; r++)
            {
                DestructibleRoot root = roots[r];
                if (root == null || root.Profile == null) continue;
                DestructionProfile profile = root.Profile;
                impactSpread |= profile.SpreadImpactToNearbyObjects && profile.ImpactSpreadRadius > 0f;
                if (root.Fragments == null) continue;
                float multiplier = Mathf.Max(0f, profile.FragmentPushMultiplier);
                if (multiplier <= 0f) continue;
                float cap = Mathf.Max(0.01f, profile.MaxFragmentLaunchSpeed);

                for (int i = 0; i < root.Fragments.Count; i++)
                {
                    FragmentRecord fragment = root.Fragments[i];
                    if (fragment == null) continue;
                    float mass = fragment.Body != null ? fragment.Body.mass : fragment.Mass;
                    if (mass <= 0f) continue;

                    float saturation = mass * cap / multiplier;
                    measured++;
                    if (saturation < lightest) lightest = saturation;
                    if (saturation > heaviest)
                    {
                        heaviest = saturation;
                        heaviestName = root.name;
                    }
                }
            }

            if (measured == 0) lightest = 0f;
        }

        static DestructibleRoot[] FindRoots()
        {
#if UNITY_6000_4_OR_NEWER
            return Object.FindObjectsByType<DestructibleRoot>();
#elif UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<DestructibleRoot>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<DestructibleRoot>();
#endif
        }
    }
}
