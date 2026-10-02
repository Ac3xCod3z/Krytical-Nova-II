using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// A few drifting leaves near the camera on windy days. Deliberately sparse: a small pool of
    /// leaf emitters with hard particle caps, scaled by wind strength, silent below
    /// <see cref="minWindSpeed"/>. Leaves ride the weather WindZone via External Forces.
    /// </summary>
    public sealed class WindLeavesController : MonoBehaviour
    {
        [SerializeField] private ParticleSystem leavesPrefab;
        [Range(1, 4)] [SerializeField] private int emitterCount = 2;
        [Tooltip("Below this wind speed (m/s) no leaves are emitted.")]
        [SerializeField] private float minWindSpeed = 6f;
        [Tooltip("Wind speed (m/s) at which emission reaches its maximum.")]
        [SerializeField] private float fullWindSpeed = 18f;
        [Tooltip("Particles per second per emitter at full wind.")]
        [Range(0.5f, 10f)] [SerializeField] private float maxRatePerEmitter = 4f;
        [Range(10, 200)] [SerializeField] private int maxParticlesPerEmitter = 40;
        [Tooltip("Emitters sit this far upwind of the camera focus.")]
        [SerializeField] private float upwindDistance = 12f;
        [SerializeField] private float spread = 18f;
        [SerializeField] private float heightAboveGround = 4f;
        [SerializeField] private float relocateSeconds = 6f;
        [SerializeField] private LayerMask groundMask = ~0;

        private ParticleSystem[] emitters;
        private float[] nextMove;
        private Transform cam;

        private void Start()
        {
            if (leavesPrefab == null) { enabled = false; return; }
            emitters = new ParticleSystem[emitterCount];
            nextMove = new float[emitterCount];
            for (int i = 0; i < emitterCount; i++)
            {
                var ps = Instantiate(leavesPrefab, transform);
                ps.name = $"Wind Leaves {i}";
                var main = ps.main;
                main.maxParticles = maxParticlesPerEmitter;
                main.prewarm = false;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var forces = ps.externalForces;
                forces.enabled = true;
                forces.multiplier = 1f;
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = 0f;
                ps.Play(true);
                emitters[i] = ps;
                nextMove[i] = i * relocateSeconds / emitterCount;
            }
        }

        private void LateUpdate()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null || emitters == null) return;
            if (cam == null)
            {
                var main = Camera.main;
                if (main == null) return;
                cam = main.transform;
            }

            var w = env.Weather;
            float speed = w.wind.magnitude;
            float strength = Mathf.Clamp01(Mathf.InverseLerp(minWindSpeed, fullWindSpeed, speed));
            // Gusts kick a few extra leaves loose; rain/snow wash them out.
            float rate = maxRatePerEmitter * strength * (0.5f + w.gust) * (1f - Mathf.Clamp01(w.rain + w.snow) * 0.7f);
            Vector2 dir = speed > 0.01f ? w.wind / speed : Vector2.up;

            for (int i = 0; i < emitters.Length; i++)
            {
                var ps = emitters[i];
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = rate;
                if (rate <= 0f || Time.time < nextMove[i]) continue;

                nextMove[i] = Time.time + relocateSeconds;
                Vector3 focus = cam.position + cam.forward * 10f;
                Vector3 p = focus - new Vector3(dir.x, 0f, dir.y) * upwindDistance
                            + new Vector3(Random.Range(-spread, spread) * 0.5f, 0f, Random.Range(-spread, spread) * 0.5f);
                if (Physics.Raycast(new Vector3(p.x, p.y + 100f, p.z), Vector3.down, out var hit, 300f, groundMask, QueryTriggerInteraction.Ignore))
                    p.y = hit.point.y;
                ps.transform.position = p + Vector3.up * heightAboveGround;
            }
        }
    }
}
