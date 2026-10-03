using System.Collections.Generic;
using BoingKit;
using KryticalNova.Progression;
using Unity.Netcode.Components;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Client-side "I got hit" reaction for anything with a <see cref="CombatEntity"/> (strike dummy, NPCs, players):
    /// a Boing Kit spring squash with overshoot, a lean away from the blow, and a short warm emissive flash.
    /// Triggered by the hit ClientRpc (with direction) and, as a fallback, by a health drop seen on this client, so
    /// every client reacts even to hits it never got an RPC for (GM hits, remote attackers). Calls within
    /// <see cref="DedupeSeconds"/> merge into one reaction. Added on demand by <see cref="GameFeelPresenter"/>.
    /// The flash uses the material's own emission (URP Lit / ComicToonLit), on per-renderer material instances,
    /// because GPU Resident Drawer ignores property blocks.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HitReactor : MonoBehaviour
    {
        private const float DedupeSeconds = 0.1f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private float squash = 0.28f;
        [SerializeField] private float heavySquash = 0.42f;
        [SerializeField] private float leanDegrees = 9f;
        [SerializeField] private float springFrequency = 5.5f;
        [SerializeField] private float springHalfLife = 0.09f;
        [SerializeField] private float flashSeconds = 0.14f;
        [SerializeField] private Color flashColor = new Color(1f, 0.93f, 0.8f, 1f);
        [SerializeField] private float flashIntensity = 0.85f;

        private Transform visual;
        private Vector3 baseScale;
        private Quaternion baseRotation;
        private Vector3Spring scaleSpring;
        private Vector3Spring leanSpring;   // lean as euler-ish (x, z) degrees
        private Renderer[] renderers;
        private readonly List<(Material Mat, Color Emission, bool HadKeyword)> flashMats = new List<(Material, Color, bool)>();
        private float flashT = -1f;
        private float lastReact = -10f;
        private CombatEntity entity;
        private float lastHealth = -1f;

        public static HitReactor For(Transform target)
        {
            if (target == null) return null;
            var reactor = target.GetComponent<HitReactor>();
            return reactor != null ? reactor : target.gameObject.AddComponent<HitReactor>();
        }

        private void Awake()
        {
            visual = VisualRoot(transform);
            baseScale = visual.localScale;
            baseRotation = visual.localRotation;
            scaleSpring.Reset(Vector3.one);
            leanSpring.Reset(Vector3.zero);
            renderers = visual.GetComponentsInChildren<Renderer>();
            entity = GetComponent<CombatEntity>();
        }

        private void OnEnable()
        {
            if (entity != null)
            {
                lastHealth = entity.CurrentHealth;
                entity.HealthChanged += OnHealthChanged;
            }
        }

        private void OnDisable()
        {
            if (entity != null) entity.HealthChanged -= OnHealthChanged;
            EndFlash();
            if (visual != null)
            {
                visual.localScale = baseScale;
                visual.localRotation = baseRotation;
            }
        }

        private void OnHealthChanged()
        {
            float hp = entity.CurrentHealth;
            if (lastHealth >= 0f && hp < lastHealth - 0.01f) React(Vector3.zero, false);
            lastHealth = hp;
        }

        /// <summary>Plays the reaction. <paramref name="towardAttacker"/> may be zero (unknown direction).</summary>
        public void React(Vector3 towardAttacker, bool heavy)
        {
            if (Time.unscaledTime - lastReact < DedupeSeconds)
            {
                // A health-drop echo of a hit we already showed: only upgrade the lean if we now know the direction.
                if (towardAttacker.sqrMagnitude > 0.001f) Lean(towardAttacker, heavy);
                return;
            }
            lastReact = Time.unscaledTime;

            float s = heavy ? heavySquash : squash;
            // Squashed wide and short, released with outward velocity so the spring overshoots into a wobble.
            scaleSpring.Reset(new Vector3(1f + s * 0.6f, 1f - s, 1f + s * 0.6f), new Vector3(-s * 6f, s * 9f, -s * 6f));
            if (towardAttacker.sqrMagnitude > 0.001f) Lean(towardAttacker, heavy);
            StartFlash(heavy);
        }

        private void Lean(Vector3 towardAttacker, bool heavy)
        {
            // Tip away from the attacker: rotate about the axis perpendicular to the blow.
            Vector3 away = -towardAttacker;
            away.y = 0f;
            away.Normalize();
            Vector3 local = visual.parent != null ? visual.parent.InverseTransformDirection(away) : away;
            float deg = leanDegrees * (heavy ? 1.5f : 1f);
            leanSpring.Reset(Vector3.zero, new Vector3(local.z, 0f, -local.x) * deg * 14f);
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 scale = scaleSpring.TrackHalfLife(Vector3.one, springFrequency, springHalfLife, dt);
            Vector3 lean = leanSpring.TrackHalfLife(Vector3.zero, springFrequency * 0.8f, springHalfLife * 1.4f, dt);
            visual.localScale = Vector3.Scale(baseScale, scale);
            visual.localRotation = baseRotation * Quaternion.Euler(lean.x, 0f, lean.z);

            if (flashT >= 0f)
            {
                flashT += Time.unscaledDeltaTime;
                float k = 1f - Mathf.Clamp01(flashT / flashSeconds);
                k *= k; // fast ease-out: a pop, not a strobe
                foreach (var (mat, emission, _) in flashMats)
                    if (mat != null) mat.SetColor(EmissionColorId, Color.Lerp(emission, flashColor * flashIntensity, k));
                if (flashT >= flashSeconds) EndFlash();
            }
        }

        private void StartFlash(bool heavy)
        {
            if (flashT < 0f)
            {
                flashMats.Clear();
                foreach (var r in renderers)
                {
                    if (r == null || r is ParticleSystemRenderer) continue;
                    foreach (var mat in r.materials) // per-renderer instances; OccluderFader swaps are re-read next hit
                    {
                        if (mat == null || !mat.HasProperty(EmissionColorId)) continue;
                        bool had = mat.IsKeywordEnabled("_EMISSION");
                        flashMats.Add((mat, mat.GetColor(EmissionColorId), had));
                        if (!had) mat.EnableKeyword("_EMISSION");
                    }
                }
            }
            flashT = heavy ? -0.03f : 0f; // heavy hits hold full brightness a touch longer
        }

        private void EndFlash()
        {
            foreach (var (mat, emission, had) in flashMats)
            {
                if (mat == null) continue;
                mat.SetColor(EmissionColorId, emission);
                if (!had) mat.DisableKeyword("_EMISSION");
            }
            flashMats.Clear();
            flashT = -1f;
        }

        /// <summary>Root, unless a NetworkTransform syncs it (scale would be fought over); then the visual child.</summary>
        private static Transform VisualRoot(Transform target)
        {
            if (target.GetComponent<NetworkTransform>() == null) return target;
            foreach (Transform child in target)
                if (child.GetComponentInChildren<SkinnedMeshRenderer>() != null || child.GetComponentInChildren<MeshRenderer>() != null)
                    return child;
            return target;
        }
    }
}
