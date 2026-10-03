using System;
using System.Collections.Generic;
using System.Linq;
using KryticalNova.GameFeel;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using MoreMountains.Tools;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the GameFeel system (see GameFeelPresenter): first-party copies of the effects it spawns (URP copies of
    /// POLYGON Particle FX, stripped copies of Cartoon FX impacts and comic text, the blade trail on an All In 1
    /// Shader Nodes slash material), the PFX_GameFeel
    /// prefab with one FEEL MMF_Player per moment plus the per-skill catalog, and the scene wiring those players need
    /// — a Cinemachine impulse listener + FOV shaker on the orbit camera, the Beautify vignette pulse + URP
    /// chromatic-aberration shaker on the global volume.
    /// Also swaps the player's hit VFX for the URP copy (the Synty original uses a legacy particle shader).
    /// Re-runnable: assets are rebuilt from the tables below, scene components are added only when missing. Vendor
    /// folders are never modified. Sound slots (MMF_Sound) are left empty for audio to be assigned on the prefab.
    /// </summary>
    public static class GameFeelBuilder
    {
        public const string PrefabFolder = "Assets/_Game/GameFeel/Prefabs";
        public const string PrefabPath = PrefabFolder + "/PFX_GameFeel.prefab";
        public const string VfxFolder = "Assets/_Game/VFX/GameFeel";
        private const string GameplayVfxFolder = "Assets/_Game/VFX/Gameplay";
        private const string MaterialFolder = "Assets/_Game/VFX/Materials";
        private const string ParticleFx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        private const string SwipeTexture = "Assets/Synty/PolygonParticleFX/Textures/PolygonParticles_Swipe_01.png";
        private const string AllIn1SlashMaterial = "Assets/Plugins/AllIn1ShaderNodes/Demo/Materials/DemoMaterials/MAT_Demo_VfxSlash.mat";
        private const string CartoonFx = "Assets/_ThirdParty/Cartoon FX Pack v2/Assets/Prefabs/";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string VolumeProfilePath = "Assets/_Game/Rendering/Settings/SampleSceneProfile.asset";
        private const string OrbitCameraName = "Player Orbit Camera";
        private const string SystemsHeader = "--- Systems ---";
        private const string BladeTrailName = "PFX_Blade_Trail";

        // Colour Bible (Rendering-Bible.md §6b), shared with the skill icons.
        private static readonly Color Cyan = new Color32(0x4F, 0xE3, 0xF0, 0xFF);
        private static readonly Color Yellow = new Color32(0xFF, 0xC9, 0x3C, 0xFF);
        private static readonly Color Magenta = new Color32(0xE0, 0x4F, 0xB8, 0xFF);
        private static readonly Color Coral = new Color32(0xFF, 0x9E, 0x66, 0xFF);
        private static readonly Color Violet = new Color(0.62f, 0.4f, 1f);
        private static readonly Color Teal = new Color(0.35f, 0.95f, 0.78f);
        private static readonly Color HealGreen = new Color(0.55f, 1f, 0.6f);

        private static readonly Vector3 Chest = new Vector3(0f, 1.1f, 0f);
        private static readonly Vector3 Feet = new Vector3(0f, 0.05f, 0f);
        private static readonly Vector3 SlashOrigin = new Vector3(0f, 1f, 0.6f);

        private static string Vfx(string name) => $"{VfxFolder}/{name}.prefab";

        [MenuItem("Krytical Nova/GameFeel/Build Game Feel")]
        public static void BuildAll()
        {
            WeatherVfxBuilder.EnsureFolder(PrefabFolder);
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            BuildVfx();
            BuildPrefab();
            WireScene();
            UseUrpVfxOnPlayer();
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Built {PrefabPath}, VFX in {VfxFolder}, and wired the open scene. Assign sounds on the prefab's MMF_Sound slots.");
        }

        // ---------------------------------------------------------------- VFX

        private static void BuildVfx()
        {
            // Hits, clicks and selection.
            Make("FX_Sparks_01", "PFX_Hit_Sparks", 0.6f);
            Make("FX_Impact_Large_01", "PFX_Hit_Heavy", 0.45f);
            Make("FX_SwordStab_01", "PFX_Hit_SwordStab", 1f);
            Make("FX_Cartoony_Rings_01", "PFX_Click_Ring", 0.35f, new Color(0.55f, 0.95f, 1f));
            Make("FX_Cartoony_Rings_01", "PFX_Select_Hostile", 0.55f, new Color(1f, 0.35f, 0.3f));
            Make("FX_Sparkles_Small_01", "PFX_Select_Friendly", 0.6f, new Color(1f, 0.85f, 0.45f));
            CartoonImpact("Impact 024", "PFX_Comic_Hit", 1.6f);
            CartoonImpact("Impact 028", "PFX_Comic_HitHeavy", 1.8f);
            CartoonImpact("Impact 023", "PFX_Comic_Slash", 2.2f);
            CartoonText("Text 020", "PFX_Comic_Pow", 2.6f);
            CartoonText("Text 022", "PFX_Comic_Wham", 2.4f);
            CartoonText("Text 021", "PFX_Comic_Zap", 2.8f);
            CartoonText("Text 012", "PFX_Comic_SlashText", 2.8f);

            // Locomotion.
            Make("FX_Dust_Small_01", "PFX_Dust_Run", 0.55f);
            Make("FX_Dust_Small_01", "PFX_Dust_Jump", 0.75f);
            Make("FX_Dust_Big_01", "PFX_Dust_Land", 0.45f);
            Make("FX_Cartoony_Jump_Up_01", "PFX_Jump_Burst", 0.8f);
            Make("FX_Cartoony_Rings_01", "PFX_Land_Ring", 0.9f);
            Make("FX_Cartoony_Sprint_01", "PFX_Run_Loop", 0.7f, loop: true);
            Make("FX_Trail_Dust_01", "PFX_Air_Loop", 0.6f, loop: true);

            // Skill casts.
            Make("FX_SwordSlash_01", "PFX_Cast_Slash", 1f);
            Make("FX_Slash_Large_01", "PFX_Cast_SlashLarge", 1f);
            Make("FX_MagicBlast_02", "PFX_Cast_Buff", 0.8f, Yellow);
            Make("FX_LevelUp_01", "PFX_Cast_PowerUp", 0.7f);
            Make("FX_LightningStrike_01", "PFX_Cast_Thunder", 0.8f);
            Make("FX_GroundCrack_Blast_01", "PFX_Cast_GroundCrack", 0.7f);
            Make("FX_Fire_Explosion_01", "PFX_Cast_Fire", 0.5f);
            Make("FX_Sparkle_Orbit_01", "PFX_Buff_End", 0.6f);
            Make("FX_Heal_01", "PFX_Heal", 0.8f, HealGreen);
            Make("FX_Heal_01", "PFX_Chi", 0.8f, Cyan);

            // Buff auras: loop for as long as the buff lasts.
            Make("FX_GlowSpot_01", "PFX_Aura_Default", 0.8f, Yellow, loop: true);
            Make("FX_Healing_Cirle_02", "PFX_Aura_AzureWall", 0.8f, Cyan, loop: true);        // ring + column: the wall
            Make("FX_Electricity_01", "PFX_Aura_LightningFlash", 0.3f, Cyan, loop: true);
            Make("FX_Embers_01", "PFX_Aura_SpiritualRage", 0.5f, Magenta, loop: true);
            Make("FX_Heal_02", "PFX_Aura_Serenity", 0.7f, Yellow, loop: true);                // pillar of light: meditation
            Make("FX_Healing_Cirle_02", "PFX_Aura_Shield", 1.05f, Yellow, loop: true);         // a wider golden barrier
            Make("FX_Sparkle_Orbit_02", "PFX_Aura_Blow", 0.8f, Coral, loop: true);
            Make("FX_Sparkles_Large_01", "PFX_Aura_Prayer", 0.8f, Yellow, loop: true);
            Make("FX_Swirl_01", "PFX_Aura_Repulsion", 0.8f, Yellow, loop: true);              // turning wind: the hit turned back
            Make("FX_Embers_01", "PFX_Aura_Destruction", 0.5f, Violet, loop: true);
            Make("FX_Swirl_03", "PFX_Aura_ReverseFlow", 0.8f, Teal, loop: true);

            BuildBladeTrail();
            UseUrpMaterials($"{GameplayVfxFolder}/FX_ComicSlash.prefab");
        }

        /// <summary>
        /// Swaps a prefab's legacy particle materials for URP copies in place, keeping everything else about it (the
        /// basic combo's slash is a hand-tuned variant of the Synty prefab and still pointed at the legacy shaders).
        /// </summary>
        private static void UseUrpMaterials(string prefabPath)
        {
            if (Load(prefabPath) == null) return;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath))
            {
                foreach (var renderer in scope.prefabContentsRoot.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    renderer.sharedMaterial = WeatherVfxBuilder.ToUrp(renderer.sharedMaterial);
                    if (renderer.trailMaterial != null) renderer.trailMaterial = WeatherVfxBuilder.ToUrp(renderer.trailMaterial);
                }
            }
        }

        /// <summary>URP copy of a POLYGON effect, scaled, optionally re-tinted; one-shot unless <paramref name="loop"/>.</summary>
        private static void Make(string source, string name, float scale, Color? tint = null, bool loop = false)
        {
            WeatherVfxBuilder.Build(ParticleFx + source + ".prefab", Vfx(name), root =>
            {
                if (loop) Loop(root, scale, tint);
                else OneShot(root, scale, tint);
            });
        }

        private static void OneShot(GameObject root, float scale, Color? tint) => Configure(root, scale, tint, false);

        /// <summary>Loops ride on a character until released, so they stay light and never stop by themselves.</summary>
        private static void Loop(GameObject root, float scale, Color? tint) => Configure(root, scale, tint, true);

        private static void Configure(GameObject root, float scale, Color? tint, bool loop)
        {
            root.transform.localScale = Vector3.one * scale;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = loop;
                main.playOnAwake = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = Mathf.Min(main.maxParticles, loop ? 60 : 120);
                main.stopAction = ParticleSystemStopAction.None; // the VFX pool recycles it
            }
            if (tint.HasValue) Recolor(root, tint.Value);
        }

        /// <summary>
        /// Gives the effect one hue. Most POLYGON effects carry their colour in the material, so multiplying the
        /// particles' start colour only darkens them (a green swirl tinted yellow stays green). The renderer gets a
        /// tinted copy of its URP material instead, and the particles go white so the material colour shows as is.
        /// </summary>
        private static void Recolor(GameObject root, Color tint)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var renderer = ps.GetComponent<ParticleSystemRenderer>();
                if (renderer == null || renderer.sharedMaterial == null) continue;
                Material urp = WeatherVfxBuilder.ToUrp(renderer.sharedMaterial);
                var main = ps.main;
                var start = main.startColor;
                if (!urp.HasProperty("_BaseColor"))
                {
                    // Shader Graph materials (no base colour to replace): fall back to tinting the particles.
                    start.colorMin *= tint;
                    start.colorMax *= tint;
                    start.color *= tint;
                    main.startColor = start;
                    continue;
                }
                renderer.sharedMaterial = Tinted(urp, tint);
                float alpha = Mathf.Max(start.color.a, start.colorMax.a);
                main.startColor = new Color(1f, 1f, 1f, alpha > 0f ? alpha : 1f);
            }
        }

        private static Material Tinted(Material source, Color tint)
        {
            string path = $"{MaterialFolder}/{source.name}_{ColorUtility.ToHtmlStringRGB(tint)}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool isNew = mat == null;
            if (isNew) mat = new Material(source);
            else mat.CopyPropertiesFromMaterial(source);
            tint.a = source.GetColor("_BaseColor").a;
            mat.SetColor("_BaseColor", tint);
            if (isNew) AssetDatabase.CreateAsset(mat, path);
            else EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void CartoonImpact(string source, string name, float scale) => Cartoon("Impacts/" + source, name, scale);
        private static void CartoonText(string source, string name, float scale) => Cartoon("Texts/" + source, name, scale);

        /// <summary>
        /// Copy of a Cartoon FX effect with the pack's authoring components removed: they rebuild the effect in the
        /// editor and turn it to the camera once in Start, neither of which suits a pooled instance. The particle
        /// systems and their materials are all the effect needs; MMF_PooledVfx faces it to the camera on every play.
        /// </summary>
        private static void Cartoon(string source, string name, float scale)
        {
            WeatherVfxBuilder.Build(CartoonFx + source + ".prefab", Vfx(name), root =>
            {
                StripAuthoring(root);
                OneShot(root, scale, null);
            });
        }

        private static void StripAuthoring(GameObject root)
        {
            // Components can require each other, so keep removing until a pass removes nothing.
            for (int pass = 0; pass < 8; pass++)
            {
                bool removed = false;
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null) continue;
                    string ns = behaviour.GetType().Namespace ?? string.Empty;
                    if (!ns.StartsWith("CTFX2", StringComparison.Ordinal) && !ns.StartsWith("PSD", StringComparison.Ordinal)) continue;
                    if (ComponentUtility.IsRequiredByOthers(behaviour)) continue;
                    Object.DestroyImmediate(behaviour);
                    removed = true;
                }
                if (!removed) break;
            }
        }

        private static class ComponentUtility
        {
            /// <summary>True while another component on the same object still lists this one's type in RequireComponent.</summary>
            public static bool IsRequiredByOthers(Component component)
            {
                Type type = component.GetType();
                foreach (var other in component.GetComponents<Component>())
                {
                    if (other == null || other == component) continue;
                    foreach (RequireComponent require in other.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                        if (Requires(require.m_Type0, type) || Requires(require.m_Type1, type) || Requires(require.m_Type2, type)) return true;
                }
                return false;
            }

            private static bool Requires(Type required, Type type) => required != null && required.IsAssignableFrom(type);
        }

        /// <summary>
        /// The blade trail's material: a first-party copy of All In 1 Shader Nodes' slash material (scrolling energy
        /// ribbon with a colour ramp), which reads far better on a trail than a plain swipe texture. Falls back to
        /// the POLYGON swipe on a URP particle material when that pack is not in the project.
        /// </summary>
        private static Material BladeTrailMaterial()
        {
            var source = AssetDatabase.LoadAssetAtPath<Material>(AllIn1SlashMaterial);
            if (source == null) return WeatherVfxBuilder.ParticleMaterial("M_BladeTrail_Swipe", SwipeTexture, additive: true);

            string path = $"{MaterialFolder}/M_BladeTrail.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(source);
                AssetDatabase.CreateAsset(mat, path);
            }
            else
            {
                mat.shader = source.shader;
                mat.CopyPropertiesFromMaterial(source);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        /// <summary>The ribbon the blade draws while it cuts: a view-facing trail that fades from white to the blade's cyan.</summary>
        private static void BuildBladeTrail()
        {
            var go = new GameObject(BladeTrailName);
            try
            {
                var trail = go.AddComponent<TrailRenderer>();
                trail.sharedMaterial = BladeTrailMaterial();
                trail.time = 0.22f;
                trail.minVertexDistance = 0.04f;
                trail.alignment = LineAlignment.View;
                trail.textureMode = LineTextureMode.Stretch;
                trail.numCapVertices = 2;
                trail.widthMultiplier = 0.6f;
                trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Cyan, 0.35f), new GradientColorKey(Cyan, 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.55f, 0.4f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = gradient;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.lightProbeUsage = LightProbeUsage.Off;
                trail.emitting = false;
                trail.autodestruct = false;
                PrefabUtility.SaveAsPrefabAsset(go, Vfx(BladeTrailName));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- per-skill looks

        /// <summary>One pooled effect inside a world player.</summary>
        private readonly struct Fx
        {
            public readonly string Prefab;
            public readonly Vector3 Offset;
            public readonly bool Ground, Follow, FaceCamera;
            public Fx(string prefab, Vector3 offset = default, bool ground = false, bool follow = false, bool faceCamera = false)
            {
                Prefab = prefab; Offset = offset; Ground = ground; Follow = follow; FaceCamera = faceCamera;
            }
        }

        private readonly struct SkillLook
        {
            public readonly string Id, Aura;
            public readonly AuraAnchor Anchor;
            public readonly Fx[] Cast, Hit;
            public SkillLook(string id, Fx[] cast = null, Fx[] hit = null, string aura = null, AuraAnchor anchor = AuraAnchor.Feet)
            {
                Id = id; Cast = cast; Hit = hit; Aura = aura; Anchor = anchor;
            }
        }

        private static Fx Slash(string prefab = "PFX_Cast_Slash") => new Fx(prefab, SlashOrigin);
        private static Fx Comic(string prefab, float up = 0f) => new Fx(prefab, new Vector3(0f, up, 0f), faceCamera: true);
        private static Fx OnCaster(string prefab) => new Fx(prefab, Chest, follow: true);
        /// <summary>Rising effects (pillars, columns) start at the feet so they wrap the body instead of the head.</summary>
        private static Fx AtFeet(string prefab) => new Fx(prefab, Feet, follow: true);
        private static Fx OnGround(string prefab) => new Fx(prefab, ground: true);

        /// <summary>
        /// What each skill looks like. Attacks: a cast burst and a hit burst. Buffs: a cast burst and an aura that
        /// lasts for the buff's duration. Skills missing here fall back to the presenter's default players.
        /// </summary>
        private static readonly SkillLook[] Skills =
        {
            new SkillLook("FuryStrike", cast: new[] { Slash("PFX_Cast_SlashLarge") }),
            new SkillLook("HaeseoStroke", cast: new[] { Slash() }),
            new SkillLook("GentlemansPen", cast: new[] { Slash() }, hit: new[] { Comic("PFX_Comic_Slash"), new Fx("PFX_Hit_Sparks") }),
            new SkillLook("DevilsStrike", cast: new[] { Slash() }, hit: new[] { Comic("PFX_Comic_HitHeavy"), new Fx("PFX_Hit_Heavy"), Comic("PFX_Comic_Wham", 0.9f) }),
            new SkillLook("FlashOfRage", cast: new[] { Slash(), OnCaster("PFX_Cast_Fire") }, hit: new[] { Comic("PFX_Comic_Slash"), new Fx("PFX_Hit_Sparks") }),
            new SkillLook("SunraysStroke", cast: new[] { Slash(), AtFeet("PFX_Cast_PowerUp") }, hit: new[] { Comic("PFX_Comic_HitHeavy"), new Fx("PFX_Hit_Heavy") }),
            new SkillLook("PhantomThunder", cast: new[] { Slash() }, hit: new[] { Comic("PFX_Comic_Slash"), OnGround("PFX_Cast_Thunder"), Comic("PFX_Comic_Zap", 0.9f) }),
            new SkillLook("ThunderousDragon", cast: new[] { Slash("PFX_Cast_SlashLarge"), OnGround("PFX_Cast_GroundCrack"), OnGround("PFX_Cast_Thunder") },
                hit: new[] { Comic("PFX_Comic_HitHeavy"), OnGround("PFX_Cast_Thunder"), Comic("PFX_Comic_Zap", 0.9f) }),

            new SkillLook("AzureWall", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_AzureWall"),
            new SkillLook("LightningFlash", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_LightningFlash", anchor: AuraAnchor.Chest),
            new SkillLook("SpiritualRage", cast: new[] { OnCaster("PFX_Cast_Fire") }, aura: "PFX_Aura_SpiritualRage", anchor: AuraAnchor.Chest),
            new SkillLook("HeavenlySerenity", cast: new[] { AtFeet("PFX_Heal") }, aura: "PFX_Aura_Serenity"),
            new SkillLook("HeavenlyShield", cast: new[] { AtFeet("PFX_Cast_PowerUp") }, aura: "PFX_Aura_Shield"),
            new SkillLook("HeavenlyBlow", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_Blow", anchor: AuraAnchor.Chest),
            new SkillLook("GarudasPrayer", cast: new[] { AtFeet("PFX_Cast_PowerUp") }, aura: "PFX_Aura_Prayer", anchor: AuraAnchor.Chest),
            new SkillLook("HeavenlyRepulsion", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_Repulsion"),
            new SkillLook("HeavenlyDestruction", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_Destruction", anchor: AuraAnchor.Chest),
            new SkillLook("ReverseFlow", cast: new[] { AtFeet("PFX_Cast_Buff") }, aura: "PFX_Aura_ReverseFlow"),
        };

        // ---------------------------------------------------------------- prefab

        private static void BuildPrefab()
        {
            var root = new GameObject("PFX_GameFeel");
            try
            {
                var presenter = root.AddComponent<GameFeelPresenter>();
                var catalog = root.AddComponent<SkillFeelCatalog>();
                var so = new SerializedObject(presenter);

                // ---- camera / screen / sound: the local player's own moments only
                so.FindProperty("swingFeel").objectReferenceValue = Player(root, "Swing", p =>
                {
                    Sound(p, "Swing Whoosh");
                    Impulse(p, new Vector3(0.04f, 0.03f, 0f), 0.15f);
                });
                so.FindProperty("hitFeel").objectReferenceValue = Player(root, "Hit", p =>
                {
                    Sound(p, "Hit");
                    Impulse(p, new Vector3(0.18f, 0.12f, 0f), 0.18f);
                });
                so.FindProperty("heavyHitFeel").objectReferenceValue = Player(root, "Heavy Hit", p =>
                {
                    Sound(p, "Heavy Hit");
                    Impulse(p, new Vector3(0.45f, 0.3f, 0f), 0.28f);
                    Chromatic(p, "Chromatic Punch", 0.22f, 0.7f);
                });
                so.FindProperty("damagedFeel").objectReferenceValue = Player(root, "Damaged", p =>
                {
                    Sound(p, "Hurt");
                    Impulse(p, new Vector3(0.3f, 0.22f, 0f), 0.25f);
                    var v = p.AddFeedback(typeof(MMF_BeautifyVignette)) as MMF_BeautifyVignette;
                    v.Label = "Hurt Vignette (Beautify)";
                    v.Duration = 0.45f;
                    v.RingBoost = 0.35f;
                    v.PulseColor = new Color(0.75f, 0.05f, 0.08f);
                });
                so.FindProperty("clickFeel").objectReferenceValue = Player(root, "Click", p => Sound(p, "Click Tick"));
                so.FindProperty("selectFeel").objectReferenceValue = Player(root, "Select", p => Sound(p, "Select"));
                so.FindProperty("runFeel").objectReferenceValue = Player(root, "Run Start", p =>
                {
                    Sound(p, "Footstep Burst");
                    var fov = p.AddFeedback(typeof(MMF_CameraFieldOfView)) as MMF_CameraFieldOfView;
                    fov.Label = "FOV Kick";
                    fov.Duration = 0.4f;
                    fov.RelativeFieldOfView = true;
                    fov.RemapFieldOfViewZero = 0f;
                    fov.RemapFieldOfViewOne = 4f;
                    fov.ShakeFieldOfView = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0f));
                });
                so.FindProperty("jumpFeel").objectReferenceValue = Player(root, "Jump", p => Sound(p, "Jump"));
                so.FindProperty("landFeel").objectReferenceValue = Player(root, "Land", p =>
                {
                    Sound(p, "Land Thud");
                    Impulse(p, new Vector3(0f, -0.22f, 0f), 0.16f);
                });
                so.FindProperty("attackCastFeel").objectReferenceValue = Player(root, "Attack Skill", p =>
                {
                    Sound(p, "Skill Slash");
                    Impulse(p, new Vector3(0.1f, 0.08f, 0f), 0.2f);
                });
                so.FindProperty("buffCastFeel").objectReferenceValue = Player(root, "Buff Skill", p =>
                {
                    Sound(p, "Buff Cast");
                    Chromatic(p, "Chromatic Shimmer", 0.3f, 0.25f);
                });

                // ---- world particles: every client
                var world = Group(root, "World");
                so.FindProperty("hitWorldFeel").objectReferenceValue = World(world, "Hit", new Fx("PFX_Hit_Sparks"), Comic("PFX_Comic_Hit"));
                so.FindProperty("heavyHitWorldFeel").objectReferenceValue = World(world, "Heavy Hit", new Fx("PFX_Hit_Heavy"), Comic("PFX_Comic_HitHeavy"));
                so.FindProperty("critWorldFeel").objectReferenceValue = World(world, "Critical", Comic("PFX_Comic_Pow", 0.9f));
                so.FindProperty("runWorldFeel").objectReferenceValue = World(world, "Run Start", OnGround("PFX_Dust_Run"));
                so.FindProperty("jumpWorldFeel").objectReferenceValue = World(world, "Jump", OnGround("PFX_Dust_Jump"), OnGround("PFX_Jump_Burst"));
                so.FindProperty("landWorldFeel").objectReferenceValue = World(world, "Land", OnGround("PFX_Dust_Land"), OnGround("PFX_Land_Ring"));
                so.FindProperty("attackCastWorldFeel").objectReferenceValue = World(world, "Attack Skill", Slash());
                so.FindProperty("buffCastWorldFeel").objectReferenceValue = World(world, "Buff Skill", AtFeet("PFX_Cast_Buff"));
                so.FindProperty("skillHitWorldFeel").objectReferenceValue = World(world, "Skill Hit", Comic("PFX_Comic_Slash"), new Fx("PFX_Hit_Sparks"));
                so.FindProperty("buffEndWorldFeel").objectReferenceValue = World(world, "Buff End", OnCaster("PFX_Buff_End"));
                so.FindProperty("healWorldFeel").objectReferenceValue = World(world, "Heal", AtFeet("PFX_Heal"));
                so.FindProperty("chiWorldFeel").objectReferenceValue = World(world, "Chi", AtFeet("PFX_Chi"));

                so.FindProperty("clickVfx").objectReferenceValue = Load(Vfx("PFX_Click_Ring"));
                so.FindProperty("selectHostileVfx").objectReferenceValue = Load(Vfx("PFX_Select_Hostile"));
                so.FindProperty("selectFriendlyVfx").objectReferenceValue = Load(Vfx("PFX_Select_Friendly"));

                so.FindProperty("skills").objectReferenceValue = catalog;
                so.FindProperty("defaultAuraVfx").objectReferenceValue = Load(Vfx("PFX_Aura_Default"));
                so.FindProperty("runLoopVfx").objectReferenceValue = Load(Vfx("PFX_Run_Loop"));
                so.FindProperty("airborneLoopVfx").objectReferenceValue = Load(Vfx("PFX_Air_Loop"));
                so.FindProperty("bladeTrail").objectReferenceValue = Load(Vfx(BladeTrailName)).GetComponent<TrailRenderer>();
                so.ApplyModifiedPropertiesWithoutUndo();

                BuildCatalog(root, catalog);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void BuildCatalog(GameObject root, SkillFeelCatalog catalog)
        {
            var group = Group(root, "Skills");
            var so = new SerializedObject(catalog);
            var list = so.FindProperty("skills");
            list.arraySize = Skills.Length;
            for (int i = 0; i < Skills.Length; i++)
            {
                SkillLook look = Skills[i];
                var entry = list.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("skillId").stringValue = look.Id;
                entry.FindPropertyRelative("cast").objectReferenceValue = look.Cast != null ? World(group, look.Id + " Cast", look.Cast) : null;
                entry.FindPropertyRelative("hit").objectReferenceValue = look.Hit != null ? World(group, look.Id + " Hit", look.Hit) : null;
                entry.FindPropertyRelative("aura").objectReferenceValue = look.Aura != null ? Load(Vfx(look.Aura)) : null;
                entry.FindPropertyRelative("auraAnchor").enumValueIndex = (int)look.Anchor;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static GameObject Group(GameObject root, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            return go;
        }

        private static MMF_Player Player(GameObject root, string name, Action<MMF_Player> build)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var player = go.AddComponent<MMF_Player>();
            player.InitializationMode = MMFeedbacks.InitializationModes.Awake;
            player.CanPlayWhileAlreadyPlaying = true;
            build(player);
            return player;
        }

        /// <summary>A world player: nothing but pooled particles, so it is safe to play on every client.</summary>
        private static MMF_Player World(GameObject group, string name, params Fx[] effects)
        {
            return Player(group, name, p =>
            {
                foreach (Fx fx in effects)
                {
                    GameObject prefab = Load(Vfx(fx.Prefab));
                    if (prefab == null)
                    {
                        Debug.LogWarning($"[GameFeel] Missing VFX '{fx.Prefab}' for '{name}'.");
                        continue;
                    }
                    var vfx = p.AddFeedback(typeof(MMF_PooledVfx)) as MMF_PooledVfx;
                    vfx.Label = fx.Prefab;
                    vfx.Prefab = prefab;
                    vfx.Offset = fx.Offset;
                    vfx.SnapToGround = fx.Ground;
                    vfx.FollowTarget = fx.Follow;
                    vfx.FaceCamera = fx.FaceCamera;
                    vfx.Upright = fx.Ground || fx.Follow;
                }
            });
        }

        private static void Sound(MMF_Player player, string label)
        {
            var sound = player.AddFeedback(typeof(MMF_Sound)) as MMF_Sound;
            sound.Label = label + " (assign clip)";
        }

        private static void Impulse(MMF_Player player, Vector3 velocity, float duration)
        {
            var impulse = player.AddFeedback(typeof(MMF_CinemachineImpulse)) as MMF_CinemachineImpulse;
            impulse.Label = "Camera Impulse";
            impulse.Velocity = velocity;
            impulse.m_ImpulseDefinition.ImpulseDuration = duration;
        }

        private static void Chromatic(MMF_Player player, string label, float duration, float peak)
        {
            var ca = player.AddFeedback(typeof(MMF_ChromaticAberration_URP)) as MMF_ChromaticAberration_URP;
            ca.Label = label;
            ca.Duration = duration;
            ca.RemapIntensityOne = peak;
        }

        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        // ---------------------------------------------------------------- scene wiring

        /// <summary>Scene half of <see cref="BuildAll"/> on its own, for wiring a second scene without rebuilding assets.</summary>
        [MenuItem("Krytical Nova/GameFeel/Wire Open Scene")]
        public static void WireScene()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var cam = Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == OrbitCameraName) ?? Object.FindAnyObjectByType<CinemachineCamera>();
            if (cam != null)
            {
                if (cam.GetComponent<CinemachineImpulseListener>() == null) Undo.AddComponent<CinemachineImpulseListener>(cam.gameObject);
                if (cam.GetComponent<MMCinemachineFieldOfViewShaker>() == null) Undo.AddComponent<MMCinemachineFieldOfViewShaker>(cam.gameObject);
                var fov = cam.GetComponent<MMCinemachineFieldOfViewShaker>();
                Undo.RecordObject(fov, "Park FOV Shaker");
                fov.RelativeFieldOfView = true;
                fov.RemapFieldOfViewZero = 0f;
                fov.RemapFieldOfViewOne = 0f;
                Park(fov);
            }
            else Debug.LogWarning("[GameFeel] No CinemachineCamera in the scene: camera impulses / FOV kicks will not show.");

            var volume = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(v => v.isGlobal && v.sharedProfile != null && AssetDatabase.GetAssetPath(v.sharedProfile) == VolumeProfilePath);
            if (volume != null)
            {
                var urpVignette = volume.GetComponent<MMVignetteShaker_URP>();
                if (urpVignette != null) Undo.DestroyObjectImmediate(urpVignette); // Beautify owns the vignette now
                if (volume.GetComponent<BeautifyVignettePulse>() == null) Undo.AddComponent<BeautifyVignettePulse>(volume.gameObject);
                if (volume.GetComponent<MMChromaticAberrationShaker_URP>() == null) Undo.AddComponent<MMChromaticAberrationShaker_URP>(volume.gameObject);
                var ca = volume.GetComponent<MMChromaticAberrationShaker_URP>();
                Undo.RecordObject(ca, "Park Chromatic Aberration Shaker");
                ca.RelativeIntensity = true;
                ca.RemapIntensityZero = 0f;
                ca.RemapIntensityOne = 0f;
                Park(ca);
                EnsureChromaticAberration(volume.sharedProfile);
            }
            else Debug.LogWarning("[GameFeel] Global volume using SampleSceneProfile not found: screen pulses will not show.");

            var existing = Object.FindAnyObjectByType<GameFeelPresenter>(FindObjectsInactive.Include);
            if (existing != null && !PrefabUtility.IsPartOfPrefabInstance(existing)) Undo.DestroyObjectImmediate(existing.gameObject);
            if (Object.FindAnyObjectByType<GameFeelPresenter>(FindObjectsInactive.Include) == null)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load(PrefabPath), scene);
                Undo.RegisterCreatedObjectUndo(instance, "Game Feel");
                var header = scene.GetRootGameObjects().FirstOrDefault(r => r.name == SystemsHeader);
                if (header != null) instance.transform.SetParent(header.transform, false);
            }
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Saves a shaker disabled, with resting values that change nothing. FEEL parks shakers in Awake, but with Scene
        /// Reload off (Enter Play Mode options) an [ExecuteAlways] component never gets that Awake, so an enabled shaker
        /// plays its own inspector values on entering Play: the FOV shaker's 60 → 120 default was a zoom out and back in.
        /// A feedback re-enables the shaker and supplies its own values when it plays.
        /// </summary>
        private static void Park(MMShaker shaker)
        {
            shaker.PlayOnAwake = false;
            shaker.Shaking = false;
            shaker.enabled = false;
            EditorUtility.SetDirty(shaker);
        }

        /// <summary>The CA shaker needs an override to drive; it rests at 0 so nothing changes until a heavy hit.</summary>
        private static void EnsureChromaticAberration(VolumeProfile profile)
        {
            if (!profile.TryGet(out ChromaticAberration ca))
            {
                ca = profile.Add<ChromaticAberration>(false);
                AssetDatabase.AddObjectToAsset(ca, profile);
            }
            ca.active = true;
            ca.intensity.Override(0f);
            EditorUtility.SetDirty(profile);
        }

        /// <summary>Points the player's basic-combo hit and slash VFX at the URP copies.</summary>
        private static void UseUrpVfxOnPlayer()
        {
            var stab = Load(Vfx("PFX_Hit_SwordStab"));
            if (stab == null) return;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PlayerPrefabPath))
            {
                var melee = scope.prefabContentsRoot.GetComponent<Combat.PlayerMeleeCombat>();
                if (melee == null) return;
                var so = new SerializedObject(melee);
                var prop = so.FindProperty("hitVfxPrefab");
                if (prop != null && prop.objectReferenceValue != stab)
                {
                    prop.objectReferenceValue = stab;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }
        }
    }
}
