using System;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Camera-following precipitation rigs (rain / snow / ground mist). Particles simulate in world space
    /// inside a small radius around the camera, with no collision, shadows or lights. A rig at zero
    /// intensity stops emitting and is deactivated once its particles die, so clear weather costs nothing.
    /// Wind is applied through one global <see cref="WindZone"/> (External Forces module), which future
    /// foliage can share.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(60)]
    public sealed class PrecipitationController : MonoBehaviour
    {
        [Serializable]
        private sealed class Rig
        {
            public ParticleSystem system;
            [Tooltip("Rig height above the camera (or above the ground when Ground Anchored).")]
            public float heightOffset = 12f;
            [Tooltip("Place at ground height under the camera instead of relative to the camera (splashes, mist).")]
            public bool groundAnchored;
            [Tooltip("Metres the rig is pushed along the camera's flattened forward, so particles fill what the camera sees.")]
            public float lookAhead = 6f;
            [Tooltip("WindZone influence on these particles.")]
            [Range(0f, 4f)] public float windInfluence = 1f;
            [Tooltip("Fade out under roofs (rain/snow yes, mist no).")]
            public bool useShelter = true;

            [NonSerialized] public ParticleSystem[] all;
            [NonSerialized] public float[] baseRates;
            [NonSerialized] public float appliedIntensity = -1f;
            [NonSerialized] public float nextAliveCheck;
        }

        private static readonly int WindVectorId = Shader.PropertyToID("_KN_WindVector");

        [SerializeField] private Rig rain = new Rig();
        [SerializeField] private Rig rainSplash = new Rig { heightOffset = 0.05f, windInfluence = 0f, groundAnchored = true };
        [SerializeField] private Rig snow = new Rig();
        [Tooltip("Extra heavy snow layered on top once snow intensity passes Blizzard Threshold.")]
        [SerializeField] private Rig blizzard = new Rig();
        [SerializeField] private Rig mist = new Rig { heightOffset = 0f, windInfluence = 0.3f, useShelter = false, groundAnchored = true, lookAhead = 0f };
        [Range(0f, 1f)] [SerializeField] private float blizzardThreshold = 0.7f;

        [Header("Wind")]
        [SerializeField] private WindZone windZone;
        [Tooltip("WindZone.windMain per m/s of weather wind.")]
        [SerializeField] private float windZoneScale = 0.06f;

        [Header("Following")]
        [SerializeField] private float snapSize = 1f;

        [Header("Shelter + ground (terrain sample / one raycast every interval)")]
        [SerializeField] private LayerMask shelterMask = ~0;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] private float shelterCheckInterval = 0.25f;
        [SerializeField] private float shelterRayLength = 40f;
        [SerializeField] private float shelterFadeSpeed = 3f;

        private Transform cam;
        private float shelter = 1f;
        private float shelterTarget = 1f;
        private float nextShelterCheck;
        private float groundY;
        private Vector3 camForwardXZ;

        private void Awake()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            if (windZone == null)
            {
                windZone = new GameObject("Weather WindZone").AddComponent<WindZone>();
                windZone.transform.SetParent(transform, false);
            }
            windZone.mode = WindZoneMode.Directional;

            Init(rain);
            Init(rainSplash);
            Init(snow);
            Init(blizzard);
            Init(mist);
        }

        private static void Init(Rig rig)
        {
            if (rig.system == null) return;
            rig.all = rig.system.GetComponentsInChildren<ParticleSystem>(true);
            rig.baseRates = new float[rig.all.Length];
            for (int i = 0; i < rig.all.Length; i++)
            {
                var ps = rig.all[i];
                rig.baseRates[i] = ps.emission.rateOverTimeMultiplier;
                var forces = ps.externalForces;
                forces.enabled = rig.windInfluence > 0f;
                forces.multiplier = rig.windInfluence;
            }
            rig.system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            rig.system.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null) return;

            if (cam == null)
            {
                var main = Camera.main;
                if (main == null) return;
                cam = main.transform;
            }

            var w = env.Weather;
            Vector3 camPos = cam.position;
            Vector3 forward = cam.forward;
            forward.y = 0f;
            camForwardXZ = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.zero;

            // Wind (one zone for particles; global vector for future foliage shaders).
            float speed = w.wind.magnitude;
            if (speed > 0.01f) windZone.transform.rotation = Quaternion.LookRotation(new Vector3(w.wind.x, 0f, w.wind.y));
            windZone.windMain = speed * windZoneScale;
            windZone.windTurbulence = speed * windZoneScale * 0.3f;
            Shader.SetGlobalVector(WindVectorId, new Vector4(w.wind.x, w.wind.y, speed, 0f));

            if ((w.rain > 0f || w.snow > 0f || w.mist > 0f) && Time.time >= nextShelterCheck)
            {
                nextShelterCheck = Time.time + shelterCheckInterval;
                shelterTarget = Physics.Raycast(camPos + Vector3.up * 0.5f, Vector3.up, shelterRayLength, shelterMask, QueryTriggerInteraction.Ignore) ? 0f : 1f;
                groundY = SampleGround(camPos);
            }
            shelter = Mathf.MoveTowards(shelter, shelterTarget, shelterFadeSpeed * Time.deltaTime);

            float snowLayer = blizzardThreshold > 0f ? Mathf.Clamp01(w.snow / blizzardThreshold) : w.snow;
            float blizzardLayer = Mathf.Clamp01((w.snow - blizzardThreshold) / Mathf.Max(1f - blizzardThreshold, 0.01f));

            UpdateRig(rain, w.rain, camPos);
            UpdateRig(rainSplash, w.rain, camPos);
            UpdateRig(snow, snowLayer, camPos);
            UpdateRig(blizzard, blizzardLayer, camPos);
            UpdateRig(mist, w.mist, camPos);
        }

        private float SampleGround(Vector3 camPos)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null) return terrain.SampleHeight(camPos) + terrain.transform.position.y;
            return Physics.Raycast(camPos, Vector3.down, out var hit, 200f, groundMask, QueryTriggerInteraction.Ignore) ? hit.point.y : camPos.y - 2f;
        }

        private void UpdateRig(Rig rig, float intensity, Vector3 camPos)
        {
            if (rig.system == null || rig.all == null) return;
            if (rig.useShelter) intensity *= shelter;

            var go = rig.system.gameObject;
            bool want = intensity > 0.001f;

            if (want && !go.activeSelf)
            {
                go.SetActive(true);
                rig.appliedIntensity = -1f;
                rig.system.Play(true);
            }
            if (!go.activeSelf) return;

            float s = snapSize;
            Vector3 centre = camPos + camForwardXZ * rig.lookAhead;
            float y = (rig.groundAnchored ? groundY : camPos.y) + rig.heightOffset;
            rig.system.transform.position = new Vector3(Mathf.Round(centre.x / s) * s, y, Mathf.Round(centre.z / s) * s);

            if (Mathf.Abs(intensity - rig.appliedIntensity) > 0.005f)
            {
                rig.appliedIntensity = intensity;
                for (int i = 0; i < rig.all.Length; i++)
                {
                    var emission = rig.all[i].emission;
                    emission.rateOverTimeMultiplier = rig.baseRates[i] * intensity;
                }
            }

            if (!want && Time.time >= rig.nextAliveCheck)
            {
                rig.nextAliveCheck = Time.time + 0.5f;
                if (!rig.system.IsAlive(true)) go.SetActive(false);
            }
        }
    }
}
