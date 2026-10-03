using System;
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
    /// Builds the GameFeel system (see GameFeelPresenter): URP copies of the POLYGON Particle FX it spawns, the
    /// PFX_GameFeel prefab with one FEEL MMF_Player per moment, and the scene wiring those players need — a Cinemachine
    /// impulse listener + FOV shaker on the orbit camera, the Beautify vignette pulse + URP chromatic-aberration shaker on the global volume.
    /// Also swaps the player's hit VFX for the URP copy (the Synty original uses a legacy particle shader).
    /// Re-runnable: the prefab is rebuilt, scene components are added only when missing. Synty and FEEL folders are
    /// never modified. Sound slots (MMF_Sound) are left empty for audio to be assigned on the prefab.
    /// </summary>
    public static class GameFeelBuilder
    {
        public const string PrefabFolder = "Assets/_Game/GameFeel/Prefabs";
        public const string PrefabPath = PrefabFolder + "/PFX_GameFeel.prefab";
        public const string VfxFolder = "Assets/_Game/VFX/GameFeel";
        private const string ParticleFx = "Assets/Synty/PolygonParticleFX/Prefabs/";
        private const string PlayerPrefabPath = "Assets/_Game/Gameplay/Player/Prefabs/Player.prefab";
        private const string VolumeProfilePath = "Assets/_Game/Rendering/Settings/SampleSceneProfile.asset";
        private const string OrbitCameraName = "Player Orbit Camera";
        private const string SystemsHeader = "--- Systems ---";

        private static string Vfx(string name) => $"{VfxFolder}/{name}.prefab";

        [MenuItem("Krytical Nova/GameFeel/Build Game Feel")]
        public static void BuildAll()
        {
            WeatherVfxBuilder.EnsureFolder(PrefabFolder);
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            BuildVfx();
            BuildPrefab();
            WireScene();
            UseUrpHitVfxOnPlayer();
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Built {PrefabPath}, VFX in {VfxFolder}, and wired the open scene. Assign sounds on the prefab's MMF_Sound slots.");
        }

        // ---------------------------------------------------------------- VFX

        private static void BuildVfx()
        {
            Make("FX_Sparks_01", "PFX_Hit_Sparks", 0.6f);
            Make("FX_Impact_Large_01", "PFX_Hit_Heavy", 0.45f);
            Make("FX_SwordStab_01", "PFX_Hit_SwordStab", 1f);
            Make("FX_Cartoony_Rings_01", "PFX_Click_Ring", 0.35f, new Color(0.55f, 0.95f, 1f));
            Make("FX_Cartoony_Rings_01", "PFX_Select_Hostile", 0.55f, new Color(1f, 0.35f, 0.3f));
            Make("FX_Sparkles_Small_01", "PFX_Select_Friendly", 0.6f, new Color(1f, 0.85f, 0.45f));
            Make("FX_Dust_Small_01", "PFX_Dust_Run", 0.55f);
            Make("FX_Dust_Small_01", "PFX_Dust_Jump", 0.75f);
            Make("FX_Dust_Big_01", "PFX_Dust_Land", 0.45f);
        }

        /// <summary>URP copy of a POLYGON effect, one-shot, scaled, optionally re-tinted.</summary>
        private static void Make(string source, string name, float scale, Color? tint = null)
        {
            WeatherVfxBuilder.Build(ParticleFx + source + ".prefab", Vfx(name), root =>
            {
                root.transform.localScale = Vector3.one * scale;
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = false;
                    main.playOnAwake = true;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Min(main.maxParticles, 120);
                    main.stopAction = ParticleSystemStopAction.None; // the VFX pool recycles it
                    if (tint.HasValue)
                    {
                        var c = main.startColor;
                        c.colorMin *= tint.Value;
                        c.colorMax *= tint.Value;
                        c.color *= tint.Value;
                        main.startColor = c;
                    }
                }
            });
        }

        // ---------------------------------------------------------------- prefab

        private static void BuildPrefab()
        {
            var root = new GameObject("PFX_GameFeel");
            try
            {
                var presenter = root.AddComponent<GameFeelPresenter>();
                var so = new SerializedObject(presenter);

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
                    var ca = p.AddFeedback(typeof(MMF_ChromaticAberration_URP)) as MMF_ChromaticAberration_URP;
                    ca.Label = "Chromatic Punch";
                    ca.Duration = 0.22f;
                    ca.RemapIntensityOne = 0.7f;
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
                so.FindProperty("hitSparkVfx").objectReferenceValue = Load(Vfx("PFX_Hit_Sparks"));
                so.FindProperty("heavyHitVfx").objectReferenceValue = Load(Vfx("PFX_Hit_Heavy"));
                so.FindProperty("clickVfx").objectReferenceValue = Load(Vfx("PFX_Click_Ring"));
                so.FindProperty("selectHostileVfx").objectReferenceValue = Load(Vfx("PFX_Select_Hostile"));
                so.FindProperty("selectFriendlyVfx").objectReferenceValue = Load(Vfx("PFX_Select_Friendly"));
                so.FindProperty("runDustVfx").objectReferenceValue = Load(Vfx("PFX_Dust_Run"));
                so.FindProperty("jumpDustVfx").objectReferenceValue = Load(Vfx("PFX_Dust_Jump"));
                so.FindProperty("landDustVfx").objectReferenceValue = Load(Vfx("PFX_Dust_Land"));
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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

        private static GameObject Load(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path);

        // ---------------------------------------------------------------- scene wiring

        private static void WireScene()
        {
            var scene = EditorSceneManager.GetActiveScene();

            var cam = Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(c => c.name == OrbitCameraName) ?? Object.FindAnyObjectByType<CinemachineCamera>();
            if (cam != null)
            {
                if (cam.GetComponent<CinemachineImpulseListener>() == null) Undo.AddComponent<CinemachineImpulseListener>(cam.gameObject);
                if (cam.GetComponent<MMCinemachineFieldOfViewShaker>() == null) Undo.AddComponent<MMCinemachineFieldOfViewShaker>(cam.gameObject);
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

        private static void UseUrpHitVfxOnPlayer()
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
