using System.Collections.Generic;
using KryticalNova.World.Ambience;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Splines;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the city's ambient flying traffic: closed spline lanes at several heights (low over the boulevards,
    /// a ring around the wall, a high skyline loop, the north island) with Sci-Fi City hover vehicles driven by
    /// <see cref="FlyingTrafficLane"/>. The root is rebuilt on every run. Vehicles carry no colliders, so they never
    /// touch the NavMesh, clicks or physics. Each car gets URP copies of the Synty exhaust trail and booster flame
    /// (the originals use legacy Built-in particle shaders, which render magenta in URP).
    /// </summary>
    public static class CityTrafficBuilder
    {
        public const string RootName = "--- Traffic ---";
        private const string VehicleFolder = "Assets/Synty/PolygonSciFiCity/Prefabs/Vehicles/";
        private const string CityFx = "Assets/Synty/PolygonSciFiCity/Prefabs/FX/";
        public const string VfxFolder = "Assets/_Game/VFX/Environment/Vehicles";
        public const string ExhaustPath = VfxFolder + "/PFX_Vehicle_Exhaust.prefab";
        public const string BoosterPath = VfxFolder + "/PFX_Vehicle_Booster.prefab";
        private static readonly Color ExhaustHot = new Color(0.75f, 0.95f, 1f, 1f);
        private static readonly Color ExhaustCool = new Color(0.55f, 0.6f, 0.75f, 1f);

        private static readonly string[] Cars =
        {
            "SM_Veh_Future_01", "SM_Veh_Future_01", "SM_Veh_Future_Taxi_01", "SM_Veh_Future_Taxi_01", "SM_Veh_Future_Cop_01",
            "SM_Veh_Classic_01_Hover", "SM_Veh_Retro_01_Hover", "SM_Veh_Hover_Bike_01"
        };
        private static readonly string[] Heavy = { "SM_Veh_Armored_Truck_01_Hover", "SM_Veh_Armored_Truck_02_Hover", "SM_Veh_Garbage_01_Hover" };

        private struct Lane
        {
            public string Name;
            public Vector3[] Knots;
            public int Count;
            public float Speed;
            public bool Reverse;
            public bool TwoWay;     // half the cars run on an offset lane
            public bool AllowHeavy;
        }

        [MenuItem("Krytical Nova/Zone/Build Vehicle Exhaust VFX")]
        public static void BuildExhaustVfx()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);

            // Exhaust: world-space puffs emitted per metre travelled, so faster lanes leave longer trails, not denser ones.
            WeatherVfxBuilder.Build(CityFx + "FX_Exhaust_Trail_01.prefab", ExhaustPath, root =>
            {
                var ps = root.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.55f, 0.8f);
                main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 2.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
                main.startColor = Color.white;
                main.maxParticles = 90;
                main.playOnAwake = true;
                main.loop = true;
                var emission = ps.emission;
                emission.rateOverTime = 4f;
                emission.rateOverDistance = 2.2f;
                var shape = ps.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 6f;
                shape.radius = 0.12f;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(ExhaustHot, 0f), new GradientColorKey(ExhaustCool, 1f) },
                    new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.45f, 0.4f), new GradientAlphaKey(0f, 1f) });
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(gradient);
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            });

            // Booster: the small hot glow at the nozzle (local space, rides with the car).
            WeatherVfxBuilder.Build(CityFx + "FX_Flame_Booster_01.prefab", BoosterPath, root =>
            {
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.maxParticles = Mathf.Min(main.maxParticles, 24);
                    main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.9f, 1f, 1f), new Color(1f, 0.85f, 0.55f, 1f));
                }
            });

            AssetDatabase.SaveAssets();
            Debug.Log($"[CityTraffic] Built URP exhaust and booster VFX into {VfxFolder}.");
        }

        [MenuItem("Krytical Nova/Zone/Build City Traffic")]
        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ExhaustPath) == null || AssetDatabase.LoadAssetAtPath<GameObject>(BoosterPath) == null)
                BuildExhaustVfx();

            var scene = EditorSceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == RootName) Object.DestroyImmediate(root);
            var rootGo = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(rootGo, "Create traffic");

            var rng = new System.Random(4242);
            int total = 0;
            foreach (var lane in Lanes())
                total += BuildLane(rootGo.transform, lane, rng);

            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[CityTraffic] Built {total} flying vehicles.");
        }

        private static IEnumerable<Lane> Lanes()
        {
            // Low lanes ride over the boulevards (heights differ where they cross) and turn round outside the walls.
            yield return new Lane
            {
                Name = "Boulevard N-S (18 m)", Count = 7, Speed = 20f, TwoWay = true,
                Knots = Stadium(new Vector2(0f, 0f), 150f, 2.5f, true, 18f),
            };
            yield return new Lane
            {
                Name = "Boulevard E-W (24 m)", Count = 7, Speed = 19f, TwoWay = true,
                Knots = Stadium(new Vector2(0f, 0f), 150f, 2.5f, false, 24f),
            };
            // Over the mid ring road, counter-clockwise.
            yield return new Lane
            {
                Name = "Mid Ring (58 m)", Count = 6, Speed = 17f, Reverse = true,
                Knots = RoundedSquare(65f, 14f, 58f, 0f),
            };
            // Just outside the wall, with heavy haulers.
            yield return new Lane
            {
                Name = "Wall Ring (46 m)", Count = 9, Speed = 24f, AllowHeavy = true, TwoWay = true,
                Knots = RoundedSquare(128f, 26f, 46f, 3f),
            };
            // High loop out over the ruins toward the skyline.
            yield return new Lane
            {
                Name = "Skyline Loop (62 m)", Count = 10, Speed = 32f, AllowHeavy = true,
                Knots = Wobbly(new Vector2(0f, 20f), 255f, 14, 62f, 8f, 31),
            };
            // The north island, circling the portal side.
            yield return new Lane
            {
                Name = "Island Loop (48 m)", Count = 4, Speed = 18f, Reverse = true,
                Knots = Wobbly(new Vector2(150f, 320f), 85f, 9, 48f, 4f, 7),
            };
        }

        private static int BuildLane(Transform parent, Lane lane, System.Random rng)
        {
            var go = new GameObject(lane.Name);
            go.transform.SetParent(parent, false);
            var container = go.AddComponent<SplineContainer>();
            var spline = container.Spline;
            spline.Clear();
            foreach (var k in lane.Knots) spline.Add(new BezierKnot(new float3(k.x, k.y, k.z)), TangentMode.AutoSmooth);
            spline.Closed = true;

            var cars = new List<Transform>(lane.Count);
            var starts = new List<float>(lane.Count);
            var lanes = new List<float>(lane.Count);
            for (int i = 0; i < lane.Count; i++)
            {
                string name = lane.AllowHeavy && rng.NextDouble() < 0.25 ? Heavy[rng.Next(Heavy.Length)] : Cars[rng.Next(Cars.Length)];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehicleFolder + name + ".prefab");
                if (prefab == null) { Debug.LogWarning($"[CityTraffic] Missing vehicle {name}"); continue; }
                var car = (GameObject)PrefabUtility.InstantiatePrefab(prefab, go.transform);
                foreach (var c in car.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
                GameObjectUtility.SetStaticEditorFlags(car, 0);
                AddExhaust(car, name);
                cars.Add(car.transform);
                starts.Add((i + (float)rng.NextDouble() * 0.5f) / lane.Count);
                lanes.Add(lane.TwoWay ? (i % 2 == 0 ? 1.2f : -1.2f) : (float)(rng.NextDouble() * 1.6 - 0.8));
            }

            var driver = go.AddComponent<FlyingTrafficLane>();
            driver.Configure(container, cars.ToArray(), starts.ToArray(), lanes.ToArray(), lane.Speed, lane.Reverse);

            // Park each car on its start so the edit-mode scene already shows traffic in place.
            float3 pos, tan, up;
            for (int i = 0; i < cars.Count; i++)
            {
                container.Evaluate(starts[i], out pos, out tan, out up);
                Vector3 forward = math.normalizesafe(lane.Reverse ? -tan : tan, new float3(0f, 0f, 1f));
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                cars[i].SetPositionAndRotation((Vector3)pos + right * lanes[i], Quaternion.LookRotation(forward, Vector3.up));
            }
            return cars.Count;
        }

        /// <summary>Mounts the exhaust trail(s) and booster glow at the car's tail, sized to the vehicle.</summary>
        private static void AddExhaust(GameObject car, string vehicleName)
        {
            var exhaust = AssetDatabase.LoadAssetAtPath<GameObject>(ExhaustPath);
            var booster = AssetDatabase.LoadAssetAtPath<GameObject>(BoosterPath);
            if (exhaust == null) return;

            car.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Bounds b = default;
            bool found = false;
            foreach (var r in car.GetComponentsInChildren<Renderer>())
            {
                if (!found) { b = r.bounds; found = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!found) return;

            float scale = Mathf.Clamp(b.size.x / 2.7f, 0.45f, 1.3f);
            bool twin = b.size.x > 2.2f && !vehicleName.Contains("Bike");
            float y = b.min.y + b.size.y * 0.35f;
            float z = b.min.z + 0.15f;
            for (int i = 0; i < (twin ? 2 : 1); i++)
            {
                float x = twin ? (i == 0 ? -1f : 1f) * b.size.x * 0.28f : b.center.x;
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(exhaust, car.transform);
                fx.transform.localPosition = new Vector3(x, y, z);
                fx.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // emit backwards
                fx.transform.localScale = Vector3.one * scale;
                if (booster != null)
                {
                    var glow = (GameObject)PrefabUtility.InstantiatePrefab(booster, car.transform);
                    glow.transform.localPosition = new Vector3(x, y, z + 0.05f);
                    glow.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    glow.transform.localScale = Vector3.one * (0.35f * scale);
                }
            }
        }

        // ---------------------------------------------------------------- lane shapes

        /// <summary>A long thin loop along one axis (two parallel runs joined by round ends).</summary>
        private static Vector3[] Stadium(Vector2 center, float halfLength, float halfWidth, bool alongZ, float height)
        {
            var pts = new List<Vector3>();
            void Add(float a, float b) => pts.Add(alongZ ? new Vector3(center.x + b, height, center.y + a) : new Vector3(center.x + a, height, center.y + b));
            Add(-halfLength, halfWidth); Add(0f, halfWidth); Add(halfLength, halfWidth);
            Add(halfLength + halfWidth * 2.2f, 0f);
            Add(halfLength, -halfWidth); Add(0f, -halfWidth); Add(-halfLength, -halfWidth);
            Add(-halfLength - halfWidth * 2.2f, 0f);
            return pts.ToArray();
        }

        private static Vector3[] RoundedSquare(float half, float corner, float height, float heightWave)
        {
            var pts = new List<Vector3>();
            float s = half - corner;
            Vector2[] path =
            {
                new Vector2(s, half), new Vector2(half, s), new Vector2(half, -s), new Vector2(s, -half),
                new Vector2(-s, -half), new Vector2(-half, -s), new Vector2(-half, s), new Vector2(-s, half)
            };
            for (int i = 0; i < path.Length; i++)
                pts.Add(new Vector3(path[i].x, height + Mathf.Sin(i * 1.3f) * heightWave, path[i].y));
            return pts.ToArray();
        }

        private static Vector3[] Wobbly(Vector2 center, float radius, int knots, float height, float heightWave, int seed)
        {
            var rng = new System.Random(seed);
            var pts = new Vector3[knots];
            for (int i = 0; i < knots; i++)
            {
                float a = i * Mathf.PI * 2f / knots;
                float r = radius * (0.85f + 0.3f * (float)rng.NextDouble());
                pts[i] = new Vector3(center.x + Mathf.Cos(a) * r, height + ((float)rng.NextDouble() * 2f - 1f) * heightWave, center.y + Mathf.Sin(a) * r);
            }
            return pts;
        }
    }
}
