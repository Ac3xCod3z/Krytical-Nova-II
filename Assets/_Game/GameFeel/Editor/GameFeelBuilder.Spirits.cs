using System;
using KryticalNova.GameFeel;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The spirit that leaves a body when it dies (<see cref="SpiritReleaser"/>), made from the Cartoon FX Remaster
    /// pack's Souls Escape: ghosts that rise out of a point and fade. Three effects in three colours, each a cell of
    /// the Synty swatch texture (Characters/Shared/Swatch/T_KN_Swatch_Synty.png):
    ///   a creature             fewer ghosts than the pack lets go, with its rays and its haze, light green
    ///   a person of flesh      one small ghost and nothing else, light blue
    ///   a robot or a skeleton  the same one ghost, reddish
    /// Also shared here: what every effect taken from that pack has taken off it (its own script and light).
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string SpiritCreaturePath = VfxFolder + "/PFX_Spirit_Creature.prefab";
        public const string SpiritLivingPath = VfxFolder + "/PFX_Spirit_Living.prefab";
        public const string SpiritUnlivingPath = VfxFolder + "/PFX_Spirit_Unliving.prefab";
        public const string SpiritSettingsPath = "Assets/_Game/GameFeel/Data/Assets/SpiritSettings.asset";
        /// <summary>The Cartoon FX Remaster pack's effects (Jean Moreno). A different pack from Cartoon FX Pack v2, which the comic marks and words come from.</summary>
        private const string CartoonRemaster = "Assets/_ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";

        // Cells of the Synty swatch (T_KN_Swatch_Synty.png, 64 x 64; the pixel each was read at).
        private static readonly Color SwatchLightGreen = Hex("75B692"); // (32, 44)
        private static readonly Color SwatchLightBlue = Hex("A3F5FF");  // (20, 52)
        private static readonly Color SwatchReddish = Hex("EA7668");    // (32, 24)

        /// <summary>Builds the three spirit effects and their settings, and puts the releaser on the game feel prefab.</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Spirit Effects")]
        public static void BuildSpiritEffects()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            const string source = CartoonRemaster + "Eerie/CFXR2 Souls Escape.prefab";

            // A creature: the pack's burst, a little over half as many ghosts, at a size for a body of a person's
            // height (SpiritReleaser sizes it up for a taller one).
            WeatherVfxBuilder.Build(source, SpiritCreaturePath, root =>
            {
                StripCartoonRemaster(root);
                root.transform.localScale = Vector3.one * 0.42f;
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    OneColour(system, SwatchLightGreen);
                    var main = system.main;
                    main.loop = false;
                    main.playOnAwake = true;
                    main.stopAction = ParticleSystemStopAction.None;
                    if (system.gameObject != root) continue;
                    main.maxParticles = 8;
                    var emission = system.emission;
                    emission.rateOverTimeMultiplier *= 0.55f;
                }
            });

            // A person: one ghost, small enough to leave by the mouth, straight up, with nothing round it.
            foreach ((string path, Color colour) in new[] { (SpiritLivingPath, SwatchLightBlue), (SpiritUnlivingPath, SwatchReddish) })
                WeatherVfxBuilder.Build(source, path, root =>
                {
                    StripCartoonRemaster(root);
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                        if (child != null && child != root.transform) UnityEngine.Object.DestroyImmediate(child.gameObject);
                    root.transform.localScale = Vector3.one * 0.2f; // a ghost a hand wide: it leaves by the mouth and can still be seen
                    var system = root.GetComponent<ParticleSystem>();
                    OneColour(system, colour);
                    var main = system.main;
                    main.loop = false;
                    main.playOnAwake = true;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.maxParticles = 1;
                    var emission = system.emission;
                    emission.rateOverTime = 0f;
                    emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                    var shape = system.shape;
                    shape.angle = 0f; // straight up out of the mouth
                });

            var settings = AssetDatabase.LoadAssetAtPath<SpiritSettings>(SpiritSettingsPath);
            if (settings == null)
            {
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(SpiritSettingsPath).Replace('\\', '/'));
                settings = ScriptableObject.CreateInstance<SpiritSettings>();
                AssetDatabase.CreateAsset(settings, SpiritSettingsPath);
            }
            settings.creature = Load(SpiritCreaturePath);
            settings.living = Load(SpiritLivingPath);
            settings.unliving = Load(SpiritUnlivingPath);
            EditorUtility.SetDirty(settings);

            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath)) AddSpiritReleaser(scope.prefabContentsRoot);
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Spirit effects rebuilt; the releaser is on {PrefabPath}. Timing and sizes: {SpiritSettingsPath}.");
        }

        /// <summary>The spirit releaser on the game feel prefab, with its settings.</summary>
        private static void AddSpiritReleaser(GameObject root)
        {
            var settings = AssetDatabase.LoadAssetAtPath<SpiritSettings>(SpiritSettingsPath);
            if (settings == null) return; // nothing built yet: Rebuild Spirit Effects makes it
            if (!root.TryGetComponent(out SpiritReleaser releaser)) releaser = root.AddComponent<SpiritReleaser>();
            var so = new SerializedObject(releaser);
            so.FindProperty("settings").objectReferenceValue = settings;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Takes off an effect of the Cartoon FX Remaster pack what the game does not want of it: the pack's own
        /// script on it (which shakes the camera, drives a light and destroys the effect when it is done) and its
        /// lights. The particle systems and their materials are left as the pack made them.
        /// </summary>
        private static void StripCartoonRemaster(GameObject root)
        {
            foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null && (behaviour.GetType().Namespace ?? string.Empty) == "CartoonFX") UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                if (light != null) UnityEngine.Object.DestroyImmediate(light.gameObject == root ? (UnityEngine.Object)light : light.gameObject);
        }

        /// <summary>
        /// Gives a particle system one colour: its particles start in it, and whatever changed their colour over
        /// their life or along their trails keeps only how see-through it made them.
        /// </summary>
        private static void OneColour(ParticleSystem system, Color colour)
        {
            var main = system.main;
            main.startColor = colour;
            var life = system.colorOverLifetime;
            if (life.enabled) life.color = OnlyAlpha(life.color);
            var trails = system.trails;
            if (trails.enabled)
            {
                trails.inheritParticleColor = true;
                trails.colorOverLifetime = OnlyAlpha(trails.colorOverLifetime);
                trails.colorOverTrail = OnlyAlpha(trails.colorOverTrail);
            }
        }

        private static ParticleSystem.MinMaxGradient OnlyAlpha(ParticleSystem.MinMaxGradient source)
        {
            Gradient White(Gradient gradient)
            {
                if (gradient == null) return null;
                var copy = new Gradient { mode = gradient.mode };
                copy.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, gradient.alphaKeys);
                return copy;
            }
            switch (source.mode)
            {
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(White(source.gradient));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(White(source.gradientMin), White(source.gradientMax));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, source.colorMin.a), new Color(1f, 1f, 1f, source.colorMax.a));
                default:
                    return new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, source.color.a));
            }
        }
    }
}
