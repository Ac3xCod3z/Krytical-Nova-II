using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The effects of the grown Drone Cores (25 % to 100 %), which belong to their animations: PetFollower plays each
    /// at the moments or over the stretch its PetEffect names, and the ones on the gun look down the cannon bone.
    /// Two kinds are mixed. The shots, smoke, shells, sparks, arcs and the body of the beam are POLYGON Particle FX
    /// effects, made over for URP like the rest of the game feel. What says "energy" (the blur round a spinning
    /// barrel, the flash and shock at a muzzle, the core of the beam, the scan lines) is drawn with the Pet Drone
    /// Energy shader, which is built on the All In 1 Shader Nodes library, on simple meshes carried by particles.
    /// Sizes are in footprints: PetFollower sizes an effect so that 1 is as far as the pet is wide.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string DroneShotPath = VfxFolder + "/PFX_Pet_DroneShot.prefab";
        public const string DroneBurstPath = VfxFolder + "/PFX_Pet_DroneBurst.prefab";
        public const string DroneShellsPath = VfxFolder + "/PFX_Pet_DroneShells.prefab";
        public const string DroneRocketPath = VfxFolder + "/PFX_Pet_DroneRocket.prefab";
        public const string DroneBoomPath = VfxFolder + "/PFX_Pet_DroneBoom.prefab";
        /// <summary>How long a pod rocket flies before it bursts, and how fast, in the effect's own metres a second.</summary>
        public const float DroneRocketSeconds = 0.34f, DroneRocketSpeed = 20f;
        public const string DronePopPath = VfxFolder + "/PFX_Pet_DronePop.prefab";
        public const string DroneSleevePath = VfxFolder + "/PFX_Pet_DroneSleeve.prefab";
        public const string DroneChargePath = VfxFolder + "/PFX_Pet_DroneCharge.prefab";
        public const string DroneFizzlePath = VfxFolder + "/PFX_Pet_DroneFizzle.prefab";
        public const string DroneBeamPath = VfxFolder + "/PFX_Pet_DroneBeam.prefab";
        public const string DroneFlashPath = VfxFolder + "/PFX_Pet_DroneFlash.prefab";
        public const string DroneScanPath = VfxFolder + "/PFX_Pet_DroneScan.prefab";
        public const string DroneWhirlPath = VfxFolder + "/PFX_Pet_DroneWhirl.prefab";
        public const string DroneSteamPath = VfxFolder + "/PFX_Pet_DroneSteam.prefab";
        public const string DroneGlitchPath = VfxFolder + "/PFX_Pet_DroneGlitch.prefab";
        public const string DroneArcsPath = VfxFolder + "/PFX_Pet_DroneArcs.prefab";
        public const string DroneLockPath = VfxFolder + "/PFX_Pet_DroneLock.prefab";

        private const string DroneEnergyShaderPath = VfxFolder + "/Shaders/FX_PetDroneEnergy.shader";
        private const string DroneTubeMeshPath = PetMeshFolder + "/SM_FX_DroneTube.asset";
        private const string DroneConeMeshPath = PetMeshFolder + "/SM_FX_DroneCone.asset";
        private const string DroneDiscMeshPath = PetMeshFolder + "/SM_FX_DroneDisc.asset";

        // The Drone Core's own light: the blue of its lines, the yellow of its lamps, and the heat of a gun.
        private static readonly Color DroneBlue = new Color(0.31f, 0.82f, 1f);
        private static readonly Color DroneYellow = new Color(1f, 0.79f, 0.24f);
        private static readonly Color DroneHeat = new Color(1f, 0.45f, 0.12f);

        /// <summary>Builds only the grown Drone Cores' effects, their meshes and their materials.</summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Drone Core Effects")]
        public static void BuildDroneCoreEffects()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            WeatherVfxBuilder.EnsureFolder(PetMeshFolder);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(DroneEnergyShaderPath);
            if (shader == null)
            {
                Debug.LogError($"[GameFeel] No energy shader at {DroneEnergyShaderPath}.");
                return;
            }
            Mesh tube = SaveUvMesh(DroneTubeMeshPath, TubeMesh("SM_FX_DroneTube", 1f, 1f));
            Mesh cone = SaveUvMesh(DroneConeMeshPath, TubeMesh("SM_FX_DroneCone", 0.12f, 1f));
            Mesh disc = SaveUvMesh(DroneDiscMeshPath, DiscMesh("SM_FX_DroneDisc"));
            Mesh ball = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");

            Material flash = Energy(shader, "M_FX_DroneFlash", "RADIAL", m =>
            {
                m.SetColor("_ColorA", DroneYellow * 1.6f);
                m.SetColor("_ColorB", Color.white * 2.2f);
                m.SetColor("_GlowColor", DroneYellow);
                m.SetFloat("_Segments", 10f);
                m.SetFloat("_Spin", 0.2f);
            });
            Material shock = Energy(shader, "M_FX_DroneShock", "RING", m =>
            {
                m.SetColor("_ColorA", DroneBlue * 1.4f);
                m.SetColor("_ColorB", Color.white * 2f);
                m.SetColor("_GlowColor", DroneBlue);
            });
            Material cameraFlash = Energy(shader, "M_FX_DroneCameraFlash", "RADIAL", m =>
            {
                m.SetColor("_ColorA", new Color(0.8f, 0.93f, 1f) * 1.6f);
                m.SetColor("_ColorB", Color.white * 2.6f);
                m.SetColor("_GlowColor", Color.white);
                m.SetFloat("_Segments", 6f);
                m.SetFloat("_Spin", 0f);
                m.SetFloat("_Spacing", 0.55f);
                m.SetFloat("_Hot", 0.45f);
            });
            Material sleeve = Energy(shader, "M_FX_DroneSleeve", "STREAKS", m =>
            {
                m.SetColor("_ColorA", DroneHeat * 1.5f);
                m.SetColor("_ColorB", new Color(1f, 0.95f, 0.7f) * 2.2f);
                m.SetColor("_GlowColor", DroneHeat);
                m.SetFloat("_Glow", 2.5f);
                m.SetFloat("_Bands", 7f);
                m.SetFloat("_Sharp", 2f);
                m.SetFloat("_Break", 0.35f);
                m.SetFloat("_ScrollX", 5f);
                m.SetFloat("_Twist", 0.6f);
                m.SetFloat("_EndFade", 0.2f);
            });
            Material beamCore = Energy(shader, "M_FX_DroneBeamCore", "STREAKS", m =>
            {
                m.SetColor("_ColorA", DroneBlue);
                m.SetColor("_ColorB", Color.white * 1.5f);
                m.SetColor("_GlowColor", DroneBlue);
                m.SetFloat("_Glow", 0.8f);
                m.SetFloat("_Core", 0.3f);
                m.SetFloat("_Bands", 6f);
                m.SetFloat("_Sharp", 1.2f);
                m.SetFloat("_Break", 0.8f);
                m.SetFloat("_AlongFreq", 5f);
                m.SetFloat("_ScrollX", 1.5f);
                m.SetFloat("_ScrollY", -4f);
                m.SetFloat("_Twist", 0.9f);
                m.SetFloat("_EndFade", 0.06f);
                m.SetFloat("_EdgeSoft", 0.7f);
            });
            Material whirl = Energy(shader, "M_FX_DroneWhirl", "STREAKS", m =>
            {
                m.SetColor("_ColorA", DroneBlue * 1.3f);
                m.SetColor("_ColorB", Color.white * 2f);
                m.SetColor("_GlowColor", DroneBlue);
                m.SetFloat("_Bands", 5f);
                m.SetFloat("_Sharp", 3f);
                m.SetFloat("_Break", 0.9f);
                m.SetFloat("_AlongFreq", 1f);
                m.SetFloat("_ScrollX", 2.5f);
                m.SetFloat("_EndFade", 0.3f);
            });
            Material holo = Energy(shader, "M_FX_DroneHolo", "HOLO", m =>
            {
                m.SetColor("_ColorA", new Color(DroneBlue.r, DroneBlue.g, DroneBlue.b, 0.55f));
                m.SetColor("_ColorB", Color.white * 1.4f);
                m.SetColor("_GlowColor", DroneBlue);
                m.SetFloat("_Glow", 0.8f);
                m.SetFloat("_HoloFrequency", 16f);
                m.SetFloat("_HoloBase", 0.03f);
                m.SetFloat("_EndFade", 0f);
            });
            Material scan = Energy(shader, "M_FX_DroneScan", "HOLO", m =>
            {
                m.SetColor("_ColorA", DroneBlue * 1.2f);
                m.SetColor("_ColorB", Color.white * 1.6f);
                m.SetColor("_GlowColor", DroneBlue);
                m.SetVector("_HoloDirection", new Vector4(0f, 0f, 1f, 0f));
                m.SetFloat("_HoloFrequency", 14f);
                m.SetFloat("_HoloBase", 0.05f);
                m.SetFloat("_EndFade", 0.12f);
            });

            // ---- the guns: POLYGON's shots, with a flash and a shock of the drone's own at the muzzle
            Make("FX_Gunshot_Heavy_Single_Tracers_01", "PFX_Pet_DroneShot", 1f, then: root =>
            {
                Pop(root, "Flash", disc, flash, 0.16f, 1.5f, billboard: true);
                Pop(root, "Shock", disc, shock, 0.3f, 1.6f, billboard: false);
            });
            Make("FX_Gunshot_Heavy_Repeating_Tracers_01", "PFX_Pet_DroneBurst", 1f, loop: true, then: root =>
            {
                ParticleSystem flicker = Pop(root, "Flash", disc, flash, 0.07f, 0.7f, billboard: true);
                Steady(flicker, 12f); // one for each shot of the burst: twelve a second
            });
            // The spent rounds of the minigun: POLYGON's large brass, which reads at a pet's size where the small does not.
            Make("FX_Shell_Ejection_Repeating_Large_01", "PFX_Pet_DroneShells", 1f, loop: true, then: root =>
            {
                // One round for each shot: twelve a second.
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var emission = system.emission;
                    emission.rateOverTime = 12f;
                }
            });
            // The rockets of the 75 % form's pod arms: a rocket leaves the pod with the cannon's flash and shock and flies
            // straight for a third of a second (some two metres at a pet's size). It is POLYGON's artillery shell
            // without its strike on the ground: the burst at the end of its flight is an effect of its own
            // (PFX_Pet_DroneBoom), which the pet plays where the rocket has got to.
            Make("FX_ArtilleryShell_01", "PFX_Pet_DroneRocket", 1f, then: root =>
            {
                var rocket = root.GetComponent<ParticleSystem>();
                var flight = rocket.main;
                flight.startLifetime = DroneRocketSeconds;
                flight.gravityModifier = 0f;
                var hits = rocket.collision;
                hits.enabled = false;
                var strike = rocket.subEmitters;
                strike.enabled = false;
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                    if (system != null && system != rocket) UnityEngine.Object.DestroyImmediate(system.gameObject);
                Pop(root, "Flash", disc, flash, 0.18f, 1.3f, billboard: true);
                Pop(root, "Shock", disc, shock, 0.3f, 1.4f, billboard: false);
            });
            Make("FX_Gunshot_01", "PFX_Pet_DronePop", 1f, then: root =>
            {
                Pop(root, "Flash", disc, flash, 0.12f, 0.7f, billboard: true);
                Pop(root, "Shock", disc, shock, 0.22f, 1f, billboard: false);
            });
            Make("FX_Explosion_01", "PFX_Pet_DroneBoom", 1f, then: root => Pop(root, "Shock", disc, shock, 0.35f, 4f, billboard: true));
            BuildDroneSleeve(tube, sleeve);

            // ---- the fake-out: it gathers power, lets out a puff, then the real beam
            Make("FX_PowerDraw_01", "PFX_Pet_DroneCharge", 1f, loop: true);
            Make("FX_Steam_01", "PFX_Pet_DroneFizzle", 1f);
            Make("FX_PowerBeam_01", "PFX_Pet_DroneBeam", 1f, loop: true, then: root =>
            {
                // POLYGON's beam is made to cross a battlefield: what it throws lives a fifth as long here, so the
                // beam is some ten of its metres long and nothing of it is still flying when the beam is off.
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var own = system.main;
                    if (own.startSpeed.constantMax > 10f) own.startLifetime = 0.2f;
                }
                // The core down the middle of it, and a ball of light where it leaves the barrel.
                ParticleSystem core = Pop(root, "Core", tube, beamCore, 0.3f, 1f, billboard: false);
                var main = core.main;
                main.startSizeX = 0.55f;
                main.startSizeY = 0.55f;
                main.startSizeZ = 10f;
                Steady(core, 8f);
                ParticleSystem flare = Pop(root, "Flare", disc, flash, 0.12f, 1.6f, billboard: true);
                Steady(flare, 14f);
            });

            // ---- what it does with its lens and its body
            var flashRoot = new GameObject("PFX_Pet_DroneFlash");
            try
            {
                Pop(flashRoot, "Flash", disc, cameraFlash, 0.22f, 2.4f, billboard: true);
                Pop(flashRoot, "Shock", disc, shock, 0.35f, 2f, billboard: true);
                SavePrefab(flashRoot, DroneFlashPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(flashRoot); }

            var scanRoot = new GameObject("PFX_Pet_DroneScan");
            try
            {
                ParticleSystem beam = Pop(scanRoot, "Cone", cone, scan, 0.8f, 1f, billboard: false);
                var main = beam.main;
                main.startSizeX = 0.42f;
                main.startSizeY = 0.42f;
                main.startSizeZ = 1.3f;
                Steady(beam, 2.5f);
                SavePrefab(scanRoot, DroneScanPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(scanRoot); }

            var whirlRoot = new GameObject("PFX_Pet_DroneWhirl");
            try
            {
                // Two hoops of streaks round the body, one upright and one lying flat, so the blur shows whichever axis it spins on.
                ParticleSystem upright = Pop(whirlRoot, "Hoop", tube, whirl, 0.35f, 1f, billboard: false);
                Hoop(upright, 0.78f, 0.5f, Vector3.zero);
                ParticleSystem flat = Pop(whirlRoot, "HoopFlat", tube, whirl, 0.35f, 1f, billboard: false);
                Hoop(flat, 0.84f, 0.42f, new Vector3(90f, 0f, 0f));
                SavePrefab(whirlRoot, DroneWhirlPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(whirlRoot); }

            Make("FX_Steam_01", "PFX_Pet_DroneSteam", 1f, DroneHeat, loop: true, then: root =>
            {
                // Out of its top, not out of its face.
                foreach (var system in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = system.shape;
                    shape.rotation = new Vector3(-90f, 0f, 0f);
                }
            });

            // ---- the swarm: scan lines as it comes apart and locks together, arcs between the blocks while it hovers
            var glitchRoot = new GameObject("PFX_Pet_DroneGlitch");
            try
            {
                ParticleSystem shell = Pop(glitchRoot, "Shell", cube, holo, 0.45f, 1.5f, billboard: false);
                var size = shell.sizeOverLifetime;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.15f)));
                Pop(glitchRoot, "Shock", disc, shock, 0.4f, 2.6f, billboard: true);
                SavePrefab(glitchRoot, DroneGlitchPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(glitchRoot); }
            Make("FX_Electricity_01", "PFX_Pet_DroneArcs", 1f, DroneBlue, loop: true);
            Make("FX_Sparks_01", "PFX_Pet_DroneLock", 1f, then: root => Pop(root, "Shock", disc, shock, 0.3f, 5f, billboard: true)); // wider than the hull it locks round
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// The blur round a barrel that is spinning: a tube of hot streaks that whirl round it, a little wider than
        /// the barrel and as long. It looks down the cannon bone from the bone's root. For as long as it plays.
        /// </summary>
        private static void BuildDroneSleeve(Mesh tube, Material sleeve)
        {
            var root = new GameObject("PFX_Pet_DroneSleeve");
            try
            {
                ParticleSystem blur = Pop(root, "Blur", tube, sleeve, 0.25f, 1f, billboard: false);
                var main = blur.main;
                main.startSizeX = 0.085f;
                main.startSizeY = 0.085f;
                main.startSizeZ = 0.3f;
                Steady(blur, 10f);
                SavePrefab(root, DroneSleevePath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        /// <summary>
        /// One piece of energy: a mesh carried by a single particle that swells in, holds and fades, once each time
        /// the effect is played. <paramref name="billboard"/> = it faces the camera; otherwise it keeps the effect's
        /// own frame (z = ahead, or down the barrel).
        /// </summary>
        private static ParticleSystem Pop(GameObject root, string name, Mesh mesh, Material material, float seconds, float size, bool billboard)
        {
            ParticleSystem system = FlatSystem(root, name, mesh, material, root.transform);
            var main = system.main;
            main.loop = false;
            main.duration = Mathf.Max(0.1f, seconds);
            main.startLifetime = seconds;
            main.startSize3D = true;
            main.startSizeX = size;
            main.startSizeY = size;
            main.startSizeZ = size;
            main.startColor = Color.white;
            main.maxParticles = 4;
            var emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var grow = system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.25f, 1f), new Keyframe(1f, 1.1f)));
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;
            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.alignment = billboard ? ParticleSystemRenderSpace.View : ParticleSystemRenderSpace.Local;
            return system;
        }

        /// <summary>Makes a piece of energy last for as long as the effect plays: it is let go again and again, each fading into the next.</summary>
        private static void Steady(ParticleSystem system, float aSecond)
        {
            var main = system.main;
            main.loop = true;
            main.duration = 1f;
            main.maxParticles = Mathf.CeilToInt(aSecond * main.startLifetime.constant) + 3;
            var emission = system.emission;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            emission.rateOverTime = aSecond;
            var grow = system.sizeOverLifetime;
            grow.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.85f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.05f)));
        }

        /// <summary>A tube turned into a hoop round the pet's middle: this far across and this deep, turned by these angles.</summary>
        private static void Hoop(ParticleSystem system, float radius, float depth, Vector3 turned)
        {
            var main = system.main;
            main.startSizeX = radius;
            main.startSizeY = radius;
            main.startSizeZ = depth;
            Steady(system, 6f);
            // The tube runs from 0 to 1 down its length: the hoop is set back by half its depth to sit round the middle.
            system.transform.localRotation = Quaternion.Euler(turned);
            system.transform.localPosition = system.transform.localRotation * new Vector3(0f, 0f, -depth * 0.5f);
        }

        /// <summary>A material of the energy shader in one of its patterns. Kept if it is there, so tuning survives.</summary>
        private static Material Energy(Shader shader, string name, string pattern, Action<Material> set)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                if (material.shader != shader) material.shader = shader;
                return material;
            }
            material = new Material(shader) { name = name };
            string[] patterns = { "STREAKS", "RADIAL", "RING", "HOLO" };
            for (int i = 0; i < patterns.Length; i++)
            {
                if (patterns[i] == pattern)
                {
                    material.SetFloat("_Pattern", i);
                    material.EnableKeyword("_PATTERN_" + patterns[i]);
                }
                else material.DisableKeyword("_PATTERN_" + patterns[i]);
            }
            set?.Invoke(material);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// An open tube down z, from 0 to 1, as wide at its far end as 1 and at its near end as <paramref name="nearRadius"/>
        /// (so 1 = a pipe, less = a cone). u runs once round it, v from the near end to the far.
        /// </summary>
        private static Mesh TubeMesh(string name, float nearRadius, float farRadius)
        {
            const int sides = 20;
            var points = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var corners = new List<int>();
            for (int i = 0; i <= sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                var out_ = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                points.Add(out_ * nearRadius);
                points.Add(out_ * farRadius + Vector3.forward);
                normals.Add(out_);
                normals.Add(out_);
                uvs.Add(new Vector2(i / (float)sides, 0f));
                uvs.Add(new Vector2(i / (float)sides, 1f));
            }
            for (int i = 0; i < sides; i++)
            {
                int a = i * 2;
                corners.AddRange(new[] { a, a + 1, a + 2, a + 2, a + 1, a + 3 });
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(points);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(corners, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A flat square one unit across in the xy plane, its picture the whole of it.</summary>
        private static Mesh DiscMesh(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(new List<Vector3> { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) });
            mesh.SetNormals(new List<Vector3> { Vector3.back, Vector3.back, Vector3.back, Vector3.back });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Saves a mesh that carries a picture (uv), filling the same asset again when it is already there.</summary>
        private static Mesh SaveUvMesh(string path, Mesh mesh)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }
            existing.Clear();
            existing.SetVertices(mesh.vertices);
            existing.SetNormals(mesh.normals);
            existing.SetUVs(0, mesh.uv);
            existing.SetTriangles(mesh.triangles, 0);
            existing.RecalculateBounds();
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            return existing;
        }
    }
}
