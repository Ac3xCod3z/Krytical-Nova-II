using KryticalNova.GameFeel;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Walls as a second ground: what shows at the feet while a player stands on a wall (owner, 18 Oct 2026: "need some
    /// vfx on the feet as we're standing on the side of the wall and added to the dust while running or walking"). One
    /// loop under each foot (the effects "directly under each foot"), following the foot and turned with the body so it
    /// lies on the wall: small lightning crackling round the sole, and dust puffs left by the distance covered, so walking
    /// and running both leave a trail and standing still leaves none.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        private const string WallLoop = "PFX_Wall_Loop";
        private const string WallLightning = "PFX_Wall_Lightning";

        private const float FeetLightningScale = 0.2f;

        /// <summary>
        /// Polygon Particle FX's Electricity 02 (owner, 18 Oct 2026: "use the lightning from polygonvfx"), made small for
        /// one foot: its crackling noise-trail arcs, a fifth the size, started on a ring round the sole and kept in the
        /// foot's own space so the arcs step with it rather than trailing behind.
        /// </summary>
        private static void FeetLightning(GameObject root)
        {
            root.transform.localScale = Vector3.one * FeetLightningScale;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.duration = 0.3f; // three arcs every 0.3 s: a steady crackle, never a sheet of light
                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
                var trails = ps.trails;
                trails.worldSpace = false;
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Circle;
                shape.radius = 0.12f / FeetLightningScale; // 12 cm round the sole
                shape.radiusThickness = 0.3f;
                shape.rotation = new Vector3(90f, 0f, 0f); // laid flat under the foot, so on the wall
            }
        }

        [MenuItem("Krytical Nova/GameFeel/Rebuild Wallstride Feel")]
        public static void RebuildWallstrideFeel()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);
            BuildWallLoop();
            AssetDatabase.SaveAssets();
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                var presenter = scope.prefabContentsRoot.GetComponent<GameFeelPresenter>();
                if (presenter == null) { Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first."); return; }
                var so = new SerializedObject(presenter);
                so.FindProperty("wallLoopVfx").objectReferenceValue = Load(Vfx(WallLoop));
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Wallstride feel built: {Vfx(WallLoop)}.");
        }

        private static void BuildWallLoop()
        {
            // The additive square specks the de-rez uses (the dust's stand-in), and the run's own dust for the puffs.
            Material sparks = AssetDatabase.LoadAssetAtPath<Material>(DerezVoxelMaterialPath);
            if (sparks == null) { BuildDerezVoxels(); sparks = AssetDatabase.LoadAssetAtPath<Material>(DerezVoxelMaterialPath); }
            // The run's own comic puffs: its material and the sphere-puff mesh it draws them with.
            ParticleSystemRenderer runRenderer = null;
            var runLoop = AssetDatabase.LoadAssetAtPath<GameObject>(Vfx("PFX_Run_Loop"));
            if (runLoop != null) runRenderer = runLoop.GetComponentInChildren<ParticleSystemRenderer>();

            var root = new GameObject(WallLoop);

            // Grip: small lightning crackling round the sole (owner, 18 Oct 2026: "small lightning particles to represent
            // the wallstride just around the feet", from Polygon Particle FX), in the foot's own space so it stays on the
            // wall with it.
            WeatherVfxBuilder.Build(ParticleFx + "FX_Electricity_02.prefab", Vfx(WallLightning), FeetLightning);
            var lightning = AssetDatabase.LoadAssetAtPath<GameObject>(Vfx(WallLightning));
            if (lightning != null)
            {
                var grip = Object.Instantiate(lightning, root.transform, false);
                grip.name = "Grip";
            }

            // Dust: puffs left by the distance covered on the wall (walking or running), drifting off it.
            var puffs = new GameObject("Dust");
            puffs.transform.SetParent(root.transform, false);
            var d = puffs.AddComponent<ParticleSystem>();
            d.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var dm = d.main;
            dm.loop = true;
            dm.playOnAwake = true;
            dm.duration = 1f;
            dm.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            dm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
            dm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            dm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            dm.startColor = new Color(0.9f, 0.88f, 0.84f, 1f);
            dm.simulationSpace = ParticleSystemSimulationSpace.World; // left behind where the feet were
            dm.maxParticles = 80;
            var de = d.emission;
            de.rateOverTime = 0f;
            de.rateOverDistance = 1.4f; // per foot: nothing standing still, a trail walking, more running
            var ds = d.shape;
            ds.shapeType = ParticleSystemShapeType.Hemisphere;
            ds.radius = 0.08f;
            ds.rotation = new Vector3(-90f, 0f, 0f); // opening off the wall
            var dsize = d.sizeOverLifetime;
            dsize.enabled = true;
            // Comic puffs pop up and shrink away, as the run's do.
            dsize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f)));
            var dc = d.colorOverLifetime;
            dc.enabled = true;
            var dfade = new Gradient();
            dfade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
            dc.color = dfade;
            var dr = puffs.GetComponent<ParticleSystemRenderer>();
            if (runRenderer != null && runRenderer.renderMode == ParticleSystemRenderMode.Mesh && runRenderer.mesh != null)
            {
                dr.renderMode = ParticleSystemRenderMode.Mesh;
                dr.mesh = runRenderer.mesh;
            }
            else dr.renderMode = ParticleSystemRenderMode.Billboard;
            dr.sharedMaterial = runRenderer != null ? runRenderer.sharedMaterial : sparks;
            dr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dr.receiveShadows = false;

            PrefabUtility.SaveAsPrefabAsset(root, Vfx(WallLoop));
            Object.DestroyImmediate(root);
        }
    }
}
