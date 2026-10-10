using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The Tidecaller's effects, which belong to its animations (PetFollower plays each at the moments or over the
    /// stretch its PetEffect names): dust where it hops off and lands, dust that follows it while it rolls, a bubble
    /// of Zs while it dozes, and a wave that rises behind it, runs past it, curls over and shatters in front when it
    /// calls the tide. Solid, flat and inked like the fox's puffs. Sizes are in footprints: PetFollower sizes an effect so
    /// that 1 is as far as the pet is wide (the egg: about 0.23 m).
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string PetHopDustPath = VfxFolder + "/PFX_Pet_HopDust.prefab";
        public const string PetRollDustPath = VfxFolder + "/PFX_Pet_RollDust.prefab";
        public const string PetZzzPath = VfxFolder + "/PFX_Pet_Zzz.prefab";
        public const string PetTideWavePath = VfxFolder + "/PFX_Pet_TideWave.prefab";
        private const string PetMeshFolder = VfxFolder + "/Meshes";
        private const string WaveMeshPath = PetMeshFolder + "/SM_FX_Wave.asset";
        private const string ShardMeshPath = PetMeshFolder + "/SM_FX_WaterShard.asset";
        private const string BubbleMeshPath = PetMeshFolder + "/SM_FX_SleepBubble.asset";
        private const string ZedMeshPath = PetMeshFolder + "/SM_FX_Zed.asset";

        private static readonly Color DustColour = new Color(0.96f, 0.93f, 0.87f);
        private static readonly Color WaterColour = new Color(0.25f, 0.72f, 0.84f);
        private static readonly Color WaterDeep = new Color(0.16f, 0.5f, 0.72f);
        private static readonly Color FoamColour = new Color(0.97f, 0.99f, 1f);
        private static readonly Color InkColour = new Color(0.08f, 0.07f, 0.17f);

        /// <summary>Builds only the Tidecaller's effects and the three meshes they are made of.</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Tidecaller Effects")]
        public static void BuildTidecallerEffects()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            WeatherVfxBuilder.EnsureFolder(PetMeshFolder);
            Make("FX_Steam_01", "PFX_Pet_HopDust", 1f, loop: false, then: ShapePetHopDust);
            Make("FX_Steam_01", "PFX_Pet_RollDust", 1f, loop: true, then: ShapePetRollDust);
            BuildPetZzz();
            BuildPetTideWave();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ dust

        /// <summary>
        /// A hop's dust: a ring of small puffs thrown out along the floor from under the pet, which slow at once
        /// and shrink away. One burst each time it is played. Left in the world where they were raised.
        /// </summary>
        private static void ShapePetHopDust(GameObject root)
        {
            ParticleSystem keep = OneSolidSystem(root);
            if (keep == null) return;
            var main = keep.main;
            main.loop = false;
            main.duration = 0.3f;
            main.playOnAwake = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.2f);
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(1f, 1.7f);
            main.startColor = DustColour;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 24;

            var emission = keep.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 7, 8) });

            // Outwards along the floor, from a ring as wide as the pet.
            var shape = keep.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.position = Vector3.zero;
            shape.rotation = new Vector3(90f, 0f, 0f);
            shape.scale = Vector3.one;
            shape.radius = 0.4f;
            shape.radiusThickness = 0f;
            shape.arc = 360f;

            // They stop almost where they started: the burst is a puff, not a throw.
            var limit = keep.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.2f;
            limit.limit = 0.15f;
            var velocity = keep.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = keep.noise;
            noise.enabled = false;
            var rotation = keep.rotationOverLifetime;
            rotation.enabled = false;
        }

        /// <summary>
        /// The dust of a pet that rolls along: puffs raised under it by the distance it covers, which keep some of
        /// its speed and so run after it for a moment before they tire and shrink away. Played only while its
        /// movement animation does.
        /// </summary>
        private static void ShapePetRollDust(GameObject root)
        {
            ParticleSystem keep = OneSolidSystem(root);
            if (keep == null) return;
            var main = keep.main;
            main.loop = true;
            main.duration = 2f;
            main.playOnAwake = true;
            main.prewarm = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            main.startSpeed = 0f;
            main.startSize3D = false;
            main.startSize = new ParticleSystem.MinMaxCurve(1.3f, 2.1f);
            main.startColor = DustColour;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Transform;
            main.maxParticles = 60;

            var emission = keep.emission;
            emission.enabled = true;
            emission.rateOverTime = 4f;
            emission.rateOverDistance = 7f;
            emission.SetBursts(Array.Empty<ParticleSystem.Burst>());

            var shape = keep.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(1f, 0.3f, 1f);
            shape.radius = 0.3f;
            shape.radiusThickness = 1f;

            // They chase it: each keeps about half of the pet's speed when it is let go, and loses it as it goes.
            var inherit = keep.inheritVelocity;
            inherit.enabled = true;
            inherit.mode = ParticleSystemInheritVelocityMode.Initial;
            inherit.curve = new ParticleSystem.MinMaxCurve(0.55f);
            var limit = keep.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.dampen = 0.06f;
            limit.limit = 0.1f;
            var velocity = keep.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var noise = keep.noise;
            noise.enabled = false;
            var rotation = keep.rotationOverLifetime;
            rotation.enabled = false;
        }

        // ------------------------------------------------------------------ sleep

        /// <summary>
        /// A thought bubble with Zs in it: a white bubble that swells up beside the pet's top with two little
        /// bubbles leading to it, and three Zs, each bigger than the last, that pop up in it one after the other,
        /// again and again for as long as it is played. The bubble and the letters are flat shapes that face the
        /// camera; the letters stand a little in front of the bubble.
        /// </summary>
        private const float BubbleSize = 0.7f; // the bubble's width in footprints: a little narrower than the pet

        private static void BuildPetZzz()
        {
            Mesh bubble = SaveMesh(BubbleMeshPath, BubbleMesh());
            // Three Zs, small to large, up and to the right. Where each stands and how big it is are in its mesh,
            // not in its transform: the meshes face the camera, so the row of letters does too from any side.
            var places = new[] { new Vector2(-0.2f, -0.12f) * BubbleSize, new Vector2(0.02f, 0.02f) * BubbleSize, new Vector2(0.24f, 0.2f) * BubbleSize };
            var sizes = new[] { 0.17f * BubbleSize, 0.23f * BubbleSize, 0.31f * BubbleSize };
            var zeds = new Mesh[3];
            for (int i = 0; i < 3; i++) zeds[i] = SaveMesh(ZedMeshPath.Replace(".asset", "_" + (i + 1) + ".asset"), ZedMesh(places[i], sizes[i], i + 1));
            Material flat = WeatherVfxBuilder.OpaqueParticleMaterial("M_ComicPuff", Color.white);
            var root = new GameObject("PFX_Pet_Zzz");
            try
            {
                // The bubble: one, for as long as the effect plays. It breathes a little.
                ParticleSystem body = FlatSystem(root, "Bubble", bubble, flat, root.transform);
                var main = body.main;
                main.loop = true;
                main.duration = 2.4f;
                main.startLifetime = 2.4f;
                main.startSize = BubbleSize;
                main.startColor = Color.white;
                main.maxParticles = 2;
                var emission = body.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
                var size = body.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.96f), new Keyframe(0.25f, 1.04f), new Keyframe(0.5f, 0.97f), new Keyframe(0.75f, 1.04f), new Keyframe(1f, 0.96f)));

                // Each Z comes a little after the last.
                for (int i = 0; i < 3; i++)
                {
                    ParticleSystem letter = FlatSystem(root, "Z " + (i + 1), zeds[i], flat, root.transform);
                    var m = letter.main;
                    m.loop = true;
                    m.duration = 2.4f;
                    m.startDelay = 0f;
                    m.startLifetime = 1.5f;
                    m.startSize = 1f;
                    m.startColor = InkColour;
                    m.maxParticles = 2;
                    var e = letter.emission;
                    e.rateOverTime = 0f;
                    e.SetBursts(new[] { new ParticleSystem.Burst(0.25f + i * 0.45f, 1) });
                    // Pops in, holds, drifts up a little and goes.
                    var s = letter.sizeOverLifetime;
                    s.enabled = true;
                    s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                        new Keyframe(0f, 0f), new Keyframe(0.12f, 1.2f), new Keyframe(0.22f, 1f), new Keyframe(0.8f, 1f), new Keyframe(1f, 0f)));
                    var v = letter.velocityOverLifetime;
                    v.enabled = true;
                    v.space = ParticleSystemSimulationSpace.Local;
                    v.x = new ParticleSystem.MinMaxCurve(0.012f);
                    v.y = new ParticleSystem.MinMaxCurve(0.035f);
                    v.z = new ParticleSystem.MinMaxCurve(0f);
                }
                SavePrefab(root, PetZzzPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>A particle system that draws one flat mesh facing the camera, in the effect's own space.</summary>
        private static ParticleSystem FlatSystem(GameObject root, string name, Mesh mesh, Material material, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = true;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.startRotation = 0f;
            var shape = system.shape;
            shape.enabled = false;
            var emission = system.emission;
            emission.enabled = true;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        // ------------------------------------------------------------------ the tide

        // The wave's life, in seconds from the moment it is called. It gathers behind the pet, runs past it, its lip
        // curls over and comes down, and what is left falls in on itself.
        private const float WaveGathered = 0.55f, WaveCurls = 1.05f, WaveBreaks = 1.42f, WaveGone = 1.6f;
        // Where its middle is at those moments, in footprints ahead of the pet (the lip reaches 1.2 further).
        private const float WaveFrom = -1.4f, WaveAtGathered = -1.2f, WaveAtCurl = -0.25f, WaveAtBreak = 0.1f, WaveAtGone = 0.2f;
        // Where the lip comes down: the middle of everything the break throws up.
        private const float WaveLands = 1.25f;
        private static readonly Vector3 WaveSize = new Vector3(2.3f, 1.2f, 1.3f);

        /// <summary>
        /// The wave the Tidecaller calls. It swells up behind the pet while the pet leans back, runs forward past it
        /// steepening as it goes, throws its lip forward, curls over and comes down in front of the pet, and falls
        /// in on itself into a heap of foam that sinks away; where the lip lands the water shatters. One wave each
        /// time it is played, 1.6 s in all, in
        /// the pet's own space (z is ahead of the pet), so it travels with the pet.
        /// <para>The wave is a run of meshes shown one after the other (the swell, then seventeen shapes of the
        /// break), each carried forward at the wave's speed for the moment it shows: a flip book, about fifteen
        /// shapes a second, which suits the picture's cut-out look. The shapes are made in <see cref="WaveMeshes"/>.</para>
        /// </summary>
        private static void BuildPetTideWave()
        {
            Material flat = WeatherVfxBuilder.OpaqueParticleMaterial("M_ComicPuff", Color.white);
            var puffSource = AssetDatabase.LoadAssetAtPath<GameObject>(PetCloudPath);
            Mesh puff = puffSource != null ? puffSource.GetComponentInChildren<ParticleSystemRenderer>(true).mesh : null;

            // When each shape of the break shows, and how far through the break it is (0 the swell, 2 the lip thrown,
            // 4 the lip down, 5 a heap of foam).
            var moments = new List<(float Time, float Shape)>();
            for (int i = 0; i < 7; i++) moments.Add((Mathf.Lerp(WaveGathered, WaveCurls, i / 7f), 2f * i / 7f));
            for (int i = 0; i < 6; i++) moments.Add((Mathf.Lerp(WaveCurls, WaveBreaks, i / 6f), 2f + 2f * i / 6f));
            for (int i = 0; i < 4; i++) moments.Add((Mathf.Lerp(WaveBreaks, WaveBreaks + 0.1f, i / 3f), 4f + i / 3f));
            var shapes = new float[moments.Count];
            for (int i = 0; i < shapes.Length; i++) shapes[i] = moments[i].Shape;
            Mesh[] meshes = SaveMeshes(WaveMeshPath, WaveMeshes(shapes));
            Mesh shard = SaveMesh(ShardMeshPath, ShardMesh());

            var root = new GameObject("PFX_Pet_TideWave");
            try
            {
                // The swell: the first shape, growing where it stands and creeping forward.
                ParticleSystem swell = WaveStage(root, "Wave Swell", meshes[0], flat, 0f, WaveGathered, WaveFrom, WaveAtGathered);
                var grow = swell.sizeOverLifetime;
                grow.enabled = true;
                grow.separateAxes = true;
                grow.x = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1f)));
                grow.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 1f)));
                grow.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1f)));

                // The break, shape by shape.
                for (int i = 0; i < moments.Count; i++)
                {
                    float from = moments[i].Time, to = i + 1 < moments.Count ? moments[i + 1].Time : WaveGone;
                    ParticleSystem stage = WaveStage(root, "Wave " + (i + 1).ToString("00"), meshes[i], flat, from, to, WaveAt(from), WaveAt(to));
                    if (i + 1 < moments.Count) continue;
                    // The last shape is the heap of foam: it sinks away under the foam the break throws up, and
                    // never lies there as a sheet.
                    var sink = stage.sizeOverLifetime;
                    sink.enabled = true;
                    sink.separateAxes = true;
                    sink.x = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.8f)));
                    sink.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0.55f), new Keyframe(1f, 0f)));
                    sink.z = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.75f)));
                }

                // Where the lip comes down, the water shatters. From the bottom up:
                // foam that rolls out along the floor in a half ring ahead of the wave;
                ParticleSystem roll = PuffSystem(root, "Break Foam Roll", puff, flat, new Vector3(0f, 0.08f, WaveLands));
                var rollMain = roll.main;
                rollMain.startDelay = WaveBreaks;
                rollMain.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
                rollMain.startSpeed = new ParticleSystem.MinMaxCurve(2.2f, 3.4f);
                rollMain.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
                rollMain.startColor = FoamColour;
                rollMain.maxParticles = 30;
                var rollEmission = roll.emission;
                rollEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 11, 13), new ParticleSystem.Burst(0.07f, 5, 6) });
                var rollShape = roll.shape;
                rollShape.enabled = true;
                rollShape.shapeType = ParticleSystemShapeType.Circle;
                rollShape.rotation = new Vector3(90f, 0f, 0f); // flat on the floor
                rollShape.arc = 180f;                           // the half ahead
                rollShape.arcMode = ParticleSystemShapeMultiModeValue.BurstSpread;
                rollShape.radius = 0.5f;
                rollShape.radiusThickness = 0f;
                rollShape.scale = new Vector3(2f, 1f, 1f);
                Slowed(roll, 0.09f, 0.25f);

                // a heap of foam thrown up over the spot;
                ParticleSystem foam = PuffSystem(root, "Break Foam", puff, flat, new Vector3(0f, 0.15f, WaveLands - 0.1f));
                var foamMain = foam.main;
                foamMain.startDelay = WaveBreaks;
                foamMain.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.8f);
                foamMain.startSpeed = new ParticleSystem.MinMaxCurve(0.7f, 1.7f);
                foamMain.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.1f);
                foamMain.startColor = FoamColour;
                foamMain.gravityModifier = 0.2f;
                foamMain.maxParticles = 40;
                var foamEmission = foam.emission;
                foamEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 12, 14), new ParticleSystem.Burst(0.08f, 6, 7) });
                var foamShape = foam.shape;
                foamShape.enabled = true;
                foamShape.shapeType = ParticleSystemShapeType.Hemisphere;
                foamShape.rotation = new Vector3(-90f, 0f, 0f);
                foamShape.scale = new Vector3(2.1f, 0.7f, 0.6f);
                foamShape.radius = 0.5f;
                Slowed(foam, 0.12f, 0.2f);

                // shards of water, flat and angular, flung up and forward and tumbling as they fall: the shatter;
                ParticleSystem shards = PuffSystem(root, "Break Shards", shard, flat, new Vector3(0f, 0.2f, WaveLands - 0.15f));
                var shardMain = shards.main;
                shardMain.startDelay = WaveBreaks;
                shardMain.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.65f);
                shardMain.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 4.6f);
                shardMain.startSize = new ParticleSystem.MinMaxCurve(0.42f, 0.8f);
                shardMain.startColor = new ParticleSystem.MinMaxGradient(WaterDeep, FoamColour);
                shardMain.gravityModifier = 0.95f;
                shardMain.maxParticles = 40;
                var shardEmission = shards.emission;
                shardEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14, 17), new ParticleSystem.Burst(0.06f, 6, 8) });
                var shardShape = shards.shape;
                shardShape.enabled = true;
                shardShape.shapeType = ParticleSystemShapeType.Cone;
                shardShape.rotation = new Vector3(-58f, 0f, 0f);
                shardShape.angle = 42f;
                shardShape.radius = 0.5f;
                shardShape.scale = new Vector3(2f, 1f, 0.6f);
                var shardRenderer = shards.GetComponent<ParticleSystemRenderer>();
                shardRenderer.alignment = ParticleSystemRenderSpace.Local;
                var tumble = shards.rotationOverLifetime;
                tumble.enabled = true;
                tumble.separateAxes = true;
                tumble.x = new ParticleSystem.MinMaxCurve(-9f, 9f);
                tumble.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
                tumble.z = new ParticleSystem.MinMaxCurve(-9f, 9f);

                // drops, small ones high and a few fat ones low;
                ParticleSystem drops = PuffSystem(root, "Break Drops", puff, flat, new Vector3(0f, 0.3f, WaveLands - 0.1f));
                var dropMain = drops.main;
                dropMain.startDelay = WaveBreaks + 0.02f;
                dropMain.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
                dropMain.startSpeed = new ParticleSystem.MinMaxCurve(2.8f, 5f);
                dropMain.startSize = new ParticleSystem.MinMaxCurve(0.22f, 0.42f);
                dropMain.startColor = new ParticleSystem.MinMaxGradient(WaterColour, FoamColour);
                dropMain.gravityModifier = 1f;
                dropMain.maxParticles = 40;
                var dropEmission = drops.emission;
                dropEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 16, 20) });
                var dropShape = drops.shape;
                dropShape.enabled = true;
                dropShape.shapeType = ParticleSystemShapeType.Cone;
                dropShape.rotation = new Vector3(-68f, 0f, 0f);
                dropShape.angle = 30f;
                dropShape.radius = 0.5f;
                dropShape.scale = new Vector3(1.9f, 1f, 0.6f);

                ParticleSystem blobs = PuffSystem(root, "Break Blobs", puff, flat, new Vector3(0f, 0.2f, WaveLands));
                var blobMain = blobs.main;
                blobMain.startDelay = WaveBreaks + 0.04f;
                blobMain.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f);
                blobMain.startSpeed = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
                blobMain.startSize = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
                blobMain.startColor = WaterColour;
                blobMain.gravityModifier = 1.3f;
                blobMain.maxParticles = 12;
                var blobEmission = blobs.emission;
                blobEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 5, 6) });
                var blobShape = blobs.shape;
                blobShape.enabled = true;
                blobShape.shapeType = ParticleSystemShapeType.Cone;
                blobShape.rotation = new Vector3(-50f, 0f, 0f);
                blobShape.angle = 38f;
                blobShape.radius = 0.4f;
                blobShape.scale = new Vector3(1.8f, 1f, 0.6f);

                // and a mist of small foam left hanging over it for a moment after.
                ParticleSystem mist = PuffSystem(root, "Break Mist", puff, flat, new Vector3(0f, 0.55f, WaveLands));
                var mistMain = mist.main;
                mistMain.startDelay = WaveBreaks + 0.12f;
                mistMain.duration = 0.3f;
                mistMain.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.95f);
                mistMain.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
                mistMain.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.75f);
                mistMain.startColor = FoamColour;
                mistMain.gravityModifier = -0.05f;
                mistMain.maxParticles = 24;
                var mistEmission = mist.emission;
                mistEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 10), new ParticleSystem.Burst(0.15f, 5, 6) });
                var mistShape = mist.shape;
                mistShape.enabled = true;
                mistShape.shapeType = ParticleSystemShapeType.Box;
                mistShape.scale = new Vector3(2.2f, 0.7f, 1f);

                SavePrefab(root, PetTideWavePath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>Where the wave's middle is, in footprints ahead of the pet, <paramref name="seconds"/> after it was called.</summary>
        private static float WaveAt(float seconds)
        {
            if (seconds <= WaveGathered) return Mathf.Lerp(WaveFrom, WaveAtGathered, Mathf.InverseLerp(0f, WaveGathered, seconds));
            if (seconds <= WaveCurls) return Mathf.Lerp(WaveAtGathered, WaveAtCurl, Mathf.InverseLerp(WaveGathered, WaveCurls, seconds));
            if (seconds <= WaveBreaks) return Mathf.Lerp(WaveAtCurl, WaveAtBreak, Mathf.InverseLerp(WaveCurls, WaveBreaks, seconds));
            return Mathf.Lerp(WaveAtBreak, WaveAtGone, Mathf.InverseLerp(WaveBreaks, WaveGone, seconds));
        }

        /// <summary>
        /// One shape of the wave: a single mesh that shows from <paramref name="from"/> to <paramref name="to"/>
        /// seconds after the effect is played, and is carried from <paramref name="zFrom"/> to <paramref name="zTo"/>
        /// ahead of the pet in that time.
        /// </summary>
        private static ParticleSystem WaveStage(GameObject root, string name, Mesh mesh, Material material, float from, float to, float zFrom, float zTo)
        {
            float seconds = Mathf.Max(0.02f, to - from);
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, zFrom);
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = seconds;
            main.startDelay = from;
            main.startLifetime = seconds;
            main.startSpeed = 0f;
            main.startSize3D = true;
            main.startSizeX = WaveSize.x;
            main.startSizeY = WaveSize.y;
            main.startSizeZ = WaveSize.z;
            main.startColor = Color.white; // the mesh carries the water and the foam
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 1;
            var shape = system.shape;
            shape.enabled = false;
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f);
            velocity.z = new ParticleSystem.MinMaxCurve((zTo - zFrom) / seconds);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return system;
        }

        /// <summary>What is thrown slows at once: it is a burst of water, not a throw.</summary>
        private static void Slowed(ParticleSystem system, float dampen, float limit)
        {
            var slow = system.limitVelocityOverLifetime;
            slow.enabled = true;
            slow.dampen = dampen;
            slow.limit = limit;
        }

        /// <summary>A system of solid comic puffs in the effect's own space: one burst, no loop, shrinking away.</summary>
        private static ParticleSystem PuffSystem(GameObject root, string name, Mesh puff, Material material, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = at;
            var system = go.AddComponent<ParticleSystem>();
            var main = system.main;
            main.playOnAwake = true;
            main.loop = false;
            main.duration = 0.4f;
            main.startSpeed = 0f;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f), new Keyframe(0.55f, 0.8f), new Keyframe(1f, 0f)));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = puff;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return system;
        }

        // ------------------------------------------------------------------ meshes

        private static Mesh SaveMesh(string path, Mesh mesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            // The same asset is filled again, so effects that already point at it keep pointing at it.
            existing.Clear();
            existing.vertices = mesh.vertices;
            existing.colors = mesh.colors;
            existing.triangles = mesh.triangles;
            existing.RecalculateNormals();
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }

        /// <summary>
        /// Several meshes kept in one asset file: the first is the file's own, the others ride in it. The file is
        /// made afresh each time, so whatever is built from these meshes is built after this, not before.
        /// </summary>
        private static Mesh[] SaveMeshes(string path, Mesh[] meshes)
        {
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(meshes[0], path);
            for (int i = 1; i < meshes.Length; i++) AssetDatabase.AddObjectToAsset(meshes[i], path);
            AssetDatabase.SaveAssets();
            return meshes;
        }

        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }

        /// <summary>Flat-shaded triangles from points and colours: every corner its own vertex, both faces drawn.</summary>
        private sealed class Faces
        {
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<Color> Colours = new List<Color>();
            public readonly List<int> Corners = new List<int>();

            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Color colour, bool bothSides = false)
            {
                Add(a, b, c, colour);
                if (bothSides) Add(a, c, b, colour);
            }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color colour, bool bothSides = false)
            {
                Triangle(a, b, c, colour, bothSides);
                Triangle(a, c, d, colour, bothSides);
            }

            private void Add(Vector3 a, Vector3 b, Vector3 c, Color colour)
            {
                int first = Points.Count;
                Points.Add(a); Points.Add(b); Points.Add(c);
                Colours.Add(colour); Colours.Add(colour); Colours.Add(colour);
                Corners.Add(first); Corners.Add(first + 1); Corners.Add(first + 2);
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Points);
                mesh.SetColors(Colours);
                mesh.SetTriangles(Corners, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                return mesh;
            }
        }

        // The outline of a slice through the wave, back to front, at six moments of its break: (z ahead, y up, how
        // much of that point is foam). Twelve points each, the same points throughout, so one moment can be blended
        // into the next: 0 the back's foot, 1 to 3 up the back, 4 the crest, 5 the top of the lip, 6 its tip, 7 under
        // the tip, 8 to 10 down the face, 11 the front's foot.
        private static readonly (float z, float y, float foam)[][] WaveOutlines =
        {
            // 0: a swell. A low hump with no lip.
            new[] { (-0.50f, 0f, 0f), (-0.36f, 0.16f, 0f), (-0.20f, 0.36f, 0f), (-0.04f, 0.52f, 0f), (0.10f, 0.60f, 0.2f), (0.18f, 0.59f, 0.2f),
                    (0.26f, 0.54f, 0.1f), (0.30f, 0.48f, 0f), (0.35f, 0.38f, 0f), (0.41f, 0.24f, 0f), (0.46f, 0.10f, 0f), (0.50f, 0f, 0f) },
            // 1: it steepens. Taller, the crest forward and white, the face nearly upright.
            new[] { (-0.50f, 0f, 0f), (-0.34f, 0.20f, 0f), (-0.16f, 0.46f, 0f), (0.04f, 0.74f, 0f), (0.22f, 0.95f, 0.8f), (0.33f, 1.00f, 1f),
                    (0.42f, 0.95f, 1f), (0.42f, 0.84f, 0.6f), (0.42f, 0.62f, 0f), (0.43f, 0.38f, 0f), (0.46f, 0.16f, 0f), (0.50f, 0f, 0f) },
            // 2: the lip is thrown forward over a face that hollows.
            new[] { (-0.50f, 0f, 0f), (-0.32f, 0.22f, 0f), (-0.12f, 0.52f, 0f), (0.10f, 0.82f, 0f), (0.32f, 1.00f, 0.9f), (0.50f, 1.00f, 1f),
                    (0.66f, 0.88f, 1f), (0.56f, 0.78f, 0.8f), (0.42f, 0.68f, 0.1f), (0.36f, 0.44f, 0f), (0.40f, 0.18f, 0f), (0.48f, 0f, 0f) },
            // 3: it curls. The tip turns down over the barrel.
            new[] { (-0.50f, 0f, 0f), (-0.30f, 0.22f, 0f), (-0.08f, 0.52f, 0f), (0.16f, 0.80f, 0.1f), (0.40f, 0.96f, 0.9f), (0.64f, 0.90f, 1f),
                    (0.82f, 0.60f, 1f), (0.70f, 0.58f, 0.9f), (0.54f, 0.70f, 0.2f), (0.40f, 0.50f, 0f), (0.40f, 0.20f, 0f), (0.48f, 0f, 0f) },
            // 4: the tip comes down on the floor and the top sags after it.
            new[] { (-0.50f, 0f, 0f), (-0.28f, 0.20f, 0f), (-0.04f, 0.46f, 0.1f), (0.22f, 0.68f, 0.4f), (0.48f, 0.78f, 1f), (0.74f, 0.62f, 1f),
                    (0.92f, 0.14f, 1f), (0.82f, 0.16f, 1f), (0.64f, 0.44f, 0.5f), (0.48f, 0.36f, 0.2f), (0.44f, 0.16f, 0.1f), (0.50f, 0f, 0f) },
            // 5: it has fallen in: a lumpy heap of foam where the lip came down. (It sinks away from here: see BuildPetTideWave.)
            new[] { (-0.30f, 0f, 0.6f), (-0.16f, 0.16f, 0.8f), (0.02f, 0.30f, 1f), (0.20f, 0.40f, 1f), (0.40f, 0.44f, 1f), (0.58f, 0.36f, 1f),
                    (0.72f, 0.42f, 1f), (0.84f, 0.30f, 1f), (0.92f, 0.20f, 1f), (0.98f, 0.12f, 1f), (1.02f, 0.05f, 1f), (1.05f, 0f, 1f) },
        };

        /// <summary>
        /// The wave at each of <paramref name="shapes"/>: how far through its break it is, 0 (a swell) to 5 (a heap
        /// of foam), a whole number being one of <see cref="WaveOutlines"/> and anything between two of them a blend.
        /// Each is one unit wide (x) and about one high and one long (the way it travels, z): the outline swept
        /// across, falling away to nothing at its two ends, which hang back, so it reads as a swell and not a wall.
        /// Water below, foam where the outline says.
        /// </summary>
        private static Mesh[] WaveMeshes(float[] shapes)
        {
            const int Slices = 10;
            var meshes = new Mesh[shapes.Length];
            for (int m = 0; m < shapes.Length; m++)
            {
                float shape = Mathf.Clamp(shapes[m], 0f, WaveOutlines.Length - 1);
                int low = Mathf.Min(Mathf.FloorToInt(shape), WaveOutlines.Length - 2);
                float blend = shape - low;
                var a = WaveOutlines[low];
                var b = WaveOutlines[low + 1];
                var outline = new (float z, float y, float foam)[a.Length];
                for (int i = 0; i < a.Length; i++)
                    outline[i] = (Mathf.Lerp(a[i].z, b[i].z, blend), Mathf.Lerp(a[i].y, b[i].y, blend), Mathf.Lerp(a[i].foam, b[i].foam, blend));

                var faces = new Faces();
                Vector3 At(int s, int i)
                {
                    float across = s / (float)Slices - 0.5f;                               // -0.5 .. 0.5
                    float tall = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(across * Mathf.PI)), 0.7f); // nothing at the two ends
                    float lean = (1f - tall) * 0.3f;                                       // the ends hang back
                    // The lip reaches less far toward the ends, as the height does.
                    float z = Mathf.Lerp(Mathf.Min(outline[i].z, 0.5f), outline[i].z, tall);
                    return new Vector3(across, outline[i].y * tall, z - lean);
                }
                for (int s = 0; s < Slices; s++)
                    for (int i = 0; i < outline.Length - 1; i++)
                    {
                        float foam = (outline[i].foam + outline[i + 1].foam) * 0.5f;
                        // Under the lip and down the face the water is in the wave's own shade.
                        Color water = i >= 7 ? WaterDeep : WaterColour;
                        Color colour = foam >= 0.5f ? FoamColour : Color.Lerp(water, FoamColour, foam * 0.6f);
                        faces.Quad(At(s, i), At(s + 1, i), At(s + 1, i + 1), At(s, i + 1), colour, true);
                    }
                meshes[m] = faces.ToMesh("SM_FX_Wave_" + m.ToString("00"));
            }
            return meshes;
        }

        /// <summary>A shard of water: a thin kite, a little bent, one unit long. White: the particles colour it.</summary>
        private static Mesh ShardMesh()
        {
            var faces = new Faces();
            Vector3 top = new Vector3(0f, 0.5f, 0f), right = new Vector3(0.24f, 0.08f, 0.07f), foot = new Vector3(0.04f, -0.5f, 0f), left = new Vector3(-0.2f, -0.02f, -0.07f);
            faces.Triangle(top, right, left, Color.white, true);
            faces.Triangle(right, foot, left, new Color(0.86f, 0.9f, 0.94f), true);
            return faces.ToMesh("SM_FX_WaterShard");
        }

        /// <summary>A round bubble one unit across, facing -z, with two smaller ones leading straight down to whoever is thinking it.</summary>
        private static Mesh BubbleMesh()
        {
            var faces = new Faces();
            void Disc(Vector2 centre, float radius, int sides)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
                    faces.Triangle(centre, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, centre + new Vector2(Mathf.Cos(b), Mathf.Sin(b)) * radius, Color.white, true);
                }
            }
            Disc(Vector2.zero, 0.5f, 18);
            // Straight down: the bubble stands over the pet's top, so they lead to the pet from whichever side it is seen.
            Disc(new Vector2(-0.07f, -0.63f), 0.1f, 10);
            Disc(new Vector2(0.01f, -0.81f), 0.055f, 8);
            return faces.ToMesh("SM_FX_SleepBubble");
        }

        /// <summary>
        /// The letter Z, <paramref name="size"/> tall and standing at <paramref name="at"/> in the bubble's own
        /// plane, a little in front of that plane (toward the camera when the mesh faces it), so that it is drawn
        /// in front of the bubble it sits in.
        /// </summary>
        private static Mesh ZedMesh(Vector2 at, float size, int number)
        {
            const float Front = -0.3f, Half = 0.5f, Wide = 0.4f, Thick = 0.2f;
            var faces = new Faces();
            Vector3 P(float x, float y) => new Vector3(at.x + x * size, at.y + y * size, Front * 0.5f);
            faces.Quad(P(-Wide, Half), P(Wide, Half), P(Wide, Half - Thick), P(-Wide, Half - Thick), Color.white, true);          // top bar
            faces.Quad(P(-Wide, -Half + Thick), P(Wide, -Half + Thick), P(Wide, -Half), P(-Wide, -Half), Color.white, true);      // bottom bar
            faces.Quad(P(Wide - 0.3f, Half - Thick), P(Wide, Half - Thick), P(-Wide + 0.3f, -Half + Thick), P(-Wide, -Half + Thick), Color.white, true); // the stroke
            return faces.ToMesh("SM_FX_Zed_" + number);
        }
    }
}
