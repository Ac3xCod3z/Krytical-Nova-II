using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// The Crucible's lava and fire, client presentation only (owner's list, 16 Nov 2026, sections 6-7; Fable's LAVA_022 / FX_022):
    /// the lava floors' emission breathes, the few realtime fire lights flicker (+-15 % at 6-10 Hz with noise), and the heat haze
    /// shimmers over the main cracks only (the 32 of Fable's 16 m crack cells nearest the camera, into the one shared half-res haze
    /// pass). All of it only while the camera is inside the map, and the haze scaled by the World Effects tier (off on Low).
    /// </summary>
    public sealed class CrucibleHeat : MonoBehaviour
    {
        [Header("Lava")]
        [Tooltip("Renderers whose emission breathes (arena floor quarters, dais, outer ground).")]
        public Renderer[] lavaRenderers = new Renderer[0];
        [Range(0f, 0.5f)] public float pulseDepth = 0.18f;
        [Tooltip("Breaths per second.")]
        [Range(0.02f, 1f)] public float pulseRate = 0.22f;

        [Header("Fire")]
        [Tooltip("The realtime fire lights (gate mouths, Null gate foot, Covenant shrine, sealed stair).")]
        public Light[] flickerLights = new Light[0];
        [Range(0f, 0.4f)] public float flickerDepth = 0.15f;
        public Vector2 flickerHz = new Vector2(6f, 10f);

        [Header("Heat haze")]
        [Tooltip("Fable's crack cells, map space: x, z centre, y ground height, w half size.")]
        public Vector4[] hazeBoxes = new Vector4[0];
        [Tooltip("Haze strength on Low, Medium, High, Ultra.")]
        public Vector4 hazeByTier = new Vector4(0f, 0.6f, 1f, 1f);
        [Tooltip("Inside this distance from the heart the map's effects run (the dome radius).")]
        public float radius = 500f;
        [Tooltip("Cells farther than this from the camera get no haze.")]
        public float hazeRange = 140f;

        [Header("Embers and smoke")]
        public ParticleSystem embers;
        public ParticleSystem smoke;
        [Tooltip("Fable's ember points (every 3 m on the arena and dais cracks), map space.")]
        public Vector3[] emberPoints = new Vector3[0];
        [Tooltip("Fable's smoke points (every 14 m on the outer flows), map space.")]
        public Vector3[] smokePoints = new Vector3[0];
        [Tooltip("Embers a second on Low, Medium, High, Ultra (spread over the points near the camera).")]
        public Vector4 embersByTier = new Vector4(0f, 20f, 40f, 60f);
        [Tooltip("Smoke puffs a second on Low, Medium, High, Ultra.")]
        public Vector4 smokeByTier = new Vector4(0f, 2f, 4f, 6f);
        public float emberRange = 70f, smokeRange = 260f;

        static readonly int EmissionId = Shader.PropertyToID("_Emission_Color_Tint");
        Color[] m_Emission;
        float[] m_LightBase;
        MaterialPropertyBlock m_Block;
        bool m_Inside;
        readonly int[] m_Nearest = new int[HeatHazeRuntime.MaxLocalBoxes];
        readonly float[] m_NearestD = new float[HeatHazeRuntime.MaxLocalBoxes];

        void Awake()
        {
            m_Block = new MaterialPropertyBlock();
            m_Emission = new Color[lavaRenderers.Length];
            for (int i = 0; i < lavaRenderers.Length; i++)
                if (lavaRenderers[i] != null && lavaRenderers[i].sharedMaterial != null)
                    m_Emission[i] = lavaRenderers[i].sharedMaterial.GetColor(EmissionId);
            m_LightBase = new float[flickerLights.Length];
            for (int i = 0; i < flickerLights.Length; i++) if (flickerLights[i] != null) m_LightBase[i] = flickerLights[i].intensity;
        }

        void OnDisable() => Off();

        void Off()
        {
            if (!m_Inside) return;
            m_Inside = false;
            HeatHazeRuntime.LocalStrength = 0f;
            HeatHazeRuntime.LocalCount = 0;
        }

        void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null) { Off(); return; }
            Vector3 local = transform.InverseTransformPoint(cam.transform.position);
            if (local.x * local.x + local.z * local.z > radius * radius) { Off(); return; }
            m_Inside = true;
            float t = Time.time;

            float breath = 1f + pulseDepth * (Mathf.Sin(t * pulseRate * Mathf.PI * 2f) * 0.7f + (Mathf.PerlinNoise(t * 0.35f, 3.1f) - 0.5f) * 0.6f);
            for (int i = 0; i < lavaRenderers.Length; i++)
            {
                Renderer r = lavaRenderers[i];
                if (r == null) continue;
                r.GetPropertyBlock(m_Block);
                m_Block.SetColor(EmissionId, m_Emission[i] * breath);
                r.SetPropertyBlock(m_Block);
            }

            for (int i = 0; i < flickerLights.Length; i++)
            {
                Light l = flickerLights[i];
                if (l == null) continue;
                float hz = Mathf.Lerp(flickerHz.x, flickerHz.y, (i * 0.618f) % 1f);
                float n = Mathf.PerlinNoise(t * hz, i * 7.3f) * 2f - 1f;
                l.intensity = m_LightBase[i] * (1f + flickerDepth * n);
            }

            int q = Mathf.Clamp((int)WorldEffectsRuntime.Quality, 0, 3);
            Emit(embers, emberPoints, embersByTier[q], emberRange, local, ref m_EmberDebt);
            Emit(smoke, smokePoints, smokeByTier[q], smokeRange, local, ref m_SmokeDebt);

            float tier = hazeByTier[Mathf.Clamp((int)WorldEffectsRuntime.Quality, 0, 3)];
            HeatHazeRuntime.LocalStrength = tier;
            HeatHazeRuntime.LocalCount = tier > 0f ? Nearest(local, transform.position) : 0;
        }

        float m_EmberDebt, m_SmokeDebt;

        /// <summary>Emits a tier's rate at random points within range of the camera (points are map space, the system sits at the heart).</summary>
        void Emit(ParticleSystem ps, Vector3[] points, float rate, float range, Vector3 cam, ref float debt)
        {
            if (ps == null || points.Length == 0 || rate <= 0f) return;
            debt += rate * Time.deltaTime;
            float range2 = range * range;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int tries = 0; debt >= 1f && tries < 64; tries++)
            {
                Vector3 at = points[Random.Range(0, points.Length)];
                float dx = at.x - cam.x, dz = at.z - cam.z;
                if (dx * dx + dz * dz > range2) continue;
                p.position = at + new Vector3(Random.Range(-0.6f, 0.6f), 0.05f, Random.Range(-0.6f, 0.6f));
                ps.Emit(p, 1);
                debt -= 1f;
            }
            if (debt > 4f) debt = 4f;
        }

        /// <summary>The cells nearest the camera (insertion into a small sorted list), written to the haze in world space.</summary>
        int Nearest(Vector3 cam, Vector3 origin)
        {
            int max = HeatHazeRuntime.MaxLocalBoxes, count = 0;
            float range2 = hazeRange * hazeRange;
            for (int i = 0; i < hazeBoxes.Length; i++)
            {
                Vector4 b = hazeBoxes[i];
                float dx = b.x - cam.x, dz = b.z - cam.z, d = dx * dx + dz * dz;
                if (d > range2 || (count == max && d >= m_NearestD[max - 1])) continue;
                int at = count < max ? count++ : max - 1;
                while (at > 0 && m_NearestD[at - 1] > d) { m_NearestD[at] = m_NearestD[at - 1]; m_Nearest[at] = m_Nearest[at - 1]; at--; }
                m_NearestD[at] = d; m_Nearest[at] = i;
            }
            for (int k = 0; k < count; k++)
            {
                Vector4 b = hazeBoxes[m_Nearest[k]];
                HeatHazeRuntime.LocalBoxes[k] = new Vector4(b.x + origin.x, b.y + origin.y, b.z + origin.z, b.w);
            }
            return count;
        }
    }
}
