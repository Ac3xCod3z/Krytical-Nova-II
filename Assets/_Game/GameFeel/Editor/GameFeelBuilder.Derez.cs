using KryticalNova.GameFeel;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The de-rez of a player killed off the ground (owner, 17 Oct 2026): the material the body and pet are swept with
    /// (KryticalNova/Player/Derez, on the All In 1 Shader Nodes library), the Cartoon FX Remaster flash, crackle and
    /// burst with a rise of voxels at the body, and a FEEL kick for the local player. Built onto the game feel prefab
    /// without regenerating the rest of it.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string DerezShaderPath = "Assets/_Game/VFX/GameFeel/Shaders/Derez.shader";
        public const string DerezMaterialPath = MaterialFolder + "/M_Derez.mat";
        private const string DerezVoxelMaterialPath = MaterialFolder + "/M_Derez_Voxel.mat";
        private const string Cfxr = "Assets/_ThirdParty/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";
        private const string DerezPlayer = "Derez";
        private const string DerezVoxels = "PFX_Derez_Voxels";
        private static readonly string[] DerezPlayers = { DerezPlayer };

        [MenuItem("Krytical Nova/GameFeel/Rebuild Derez Feel")]
        public static void RebuildDerezFeel()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);
            Material derez = EnsureDerezMaterial();
            BuildDerezVfx();
            AssetDatabase.SaveAssets();

            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                GameObject root = scope.prefabContentsRoot;
                var presenter = root.GetComponent<GameFeelPresenter>();
                Transform world = root.transform.Find("World");
                if (presenter == null || world == null)
                {
                    Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first.");
                    return;
                }
                Remove(root.transform, DerezPlayers);
                Remove(world, DerezPlayers);

                var so = new SerializedObject(presenter);
                so.FindProperty("derezMaterial").objectReferenceValue = derez;
                so.FindProperty("derezGlow").floatValue = 1.4f;
                // ---- the local player was de-rezzed, or de-rezzed someone: a hard digital kick
                so.FindProperty("derezFeel").objectReferenceValue = Player(root, DerezPlayer, p =>
                {
                    Sound(p, "Critical Death"); // its own de-rez sound is still to be chosen (sound scout)
                    Impulse(p, new Vector3(0.25f, 0.45f, 0f), 0.3f);
                    Chromatic(p, "Chromatic Glitch", 0.5f, 0.9f);
                });
                // ---- at the body, for everyone: the flash, the crackle over it, the burst, and the voxels rising off it
                so.FindProperty("derezWorldFeel").objectReferenceValue = World(world.gameObject, DerezPlayer,
                    new Fx("PFX_Derez_Flash", tint: true),
                    new Fx("PFX_Derez_Crackle", follow: true, tint: true),
                    new Fx("PFX_Derez_Burst", tint: true),
                    new Fx(DerezVoxels, follow: true, tint: true));
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] De-rez feel built on {PrefabPath}.");
        }

        private static Material EnsureDerezMaterial()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DerezShaderPath);
            if (shader == null) { Debug.LogError($"[GameFeel] No shader at {DerezShaderPath}."); return null; }
            var material = AssetDatabase.LoadAssetAtPath<Material>(DerezMaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "M_Derez" };
                AssetDatabase.CreateAsset(material, DerezMaterialPath);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void BuildDerezVfx()
        {
            WeatherVfxBuilder.Build(Cfxr + "Misc/CFXR Flash.prefab", Vfx("PFX_Derez_Flash"), root => OneShot(root, 0.9f, null));
            WeatherVfxBuilder.Build(Cfxr + "Electric/CFXR Electrified 3.prefab", Vfx("PFX_Derez_Crackle"), root =>
            {
                OneShot(root, 0.8f, null);
                // Over the body while it glitches and the sweep runs, then gone with it.
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.duration = 1.4f;
                    main.loop = false;
                }
            });
            WeatherVfxBuilder.Build(Cfxr + "Electric/CFXR3 Hit Electric C (Air).prefab", Vfx("PFX_Derez_Burst"), root => OneShot(root, 0.9f, null));
            BuildDerezVoxels();
        }

        /// <summary>Square, unlit specks of the body rising off it as the sweep goes down: the voxels it breaks into.</summary>
        private static void BuildDerezVoxels()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(DerezVoxelMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "M_Derez_Voxel" };
                AssetDatabase.CreateAsset(material, DerezVoxelMaterialPath);
            }
            // No texture: each particle is a crisp square. Additive, so the faction's colour glows.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetInt("_ZWrite", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_BLENDMODE_ADD");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);

            var go = new GameObject(DerezVoxels);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1.5f;
            main.loop = false;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.25f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startColor = new Color(1f, 1f, 1f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 260;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = ps.emission;
            emission.rateOverTime = new ParticleSystem.MinMaxCurve(150f, new AnimationCurve(
                new Keyframe(0f, 0.15f), new Keyframe(0.28f, 1f), new Keyframe(0.85f, 0.7f), new Keyframe(1f, 0f)));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(0.7f, 1.8f, 0.5f);
            var velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            velocity.y = new ParticleSystem.MinMaxCurve(1.1f, 2.6f);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 1.2f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.7f, 0.8f), new Keyframe(1f, 0f)));
            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            PrefabUtility.SaveAsPrefabAsset(go, Vfx(DerezVoxels));
            Object.DestroyImmediate(go);
        }
    }
}
