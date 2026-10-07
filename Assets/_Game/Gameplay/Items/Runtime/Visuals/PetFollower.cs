using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Client-side cosmetic pet. Spawned from the replicated loadout on every peer (no NetworkObject, no bandwidth),
    /// floats at a shoulder offset with a gentle bob. Lives unparented; the damping is applied to the offset relative to
    /// the owner, so the pet stays on the shoulder at any run speed and only lags on turns.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetFollower : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] private float followSmoothTime = 0.2f;
        [SerializeField] private float bobAmplitude = 0.12f;
        [SerializeField] private float bobFrequency = 1.4f;
        [SerializeField] private float turnSpeed = 6f;
        [Tooltip("Snap instead of flying when the owner teleports (portals, spawn).")]
        [SerializeField] private float snapDistance = 8f;
        [Tooltip("Pets are normalised so their largest dimension is this many metres (x the item's petScale).")]
        [SerializeField] private float normalizedSize = 0.55f;
        [Tooltip("Size of the sparkle effect relative to how it was authored: small enough to come off the pet, not surround the owner.")]
        [SerializeField, Min(0.05f)] private float glowSize = 0.42f;

        private GameObject pet;
        private ItemDefinition current;
        private Vector3 velocity;
        private float bobPhase;

        // The look of the pet's growth stage, set by whatever owns pet growth; defaults are the plain item.
        private GameObject stageModel;
        private float stageScale = 1f;
        private PetGlowLook glow;
        private GameObject sparkles;
        private GlowRim rim;       // this pet's own: every pet glows in its own colour
        private bool rimOn;
        private float baseScale = 1f;

        /// <summary>The pet in the world, or null. For effects that play on it.</summary>
        public Transform PetTransform => pet != null ? pet.transform : null;

        public void SetPet(ItemDefinition item)
        {
            if (current == item && (item == null || pet != null)) return;
            current = item;
            Rebuild();
        }

        /// <summary>
        /// How the pet looks at its current growth stage: its own model for the stage (null = the item's model), a
        /// size step, and its glow (strength 0 = none).
        /// </summary>
        public void SetStageLook(GameObject model, float scale, in PetGlowLook stageGlow)
        {
            bool modelChanged = stageModel != model;
            stageModel = model;
            stageScale = Mathf.Max(0.1f, scale);
            glow = stageGlow;
            glow.strength = Mathf.Clamp01(glow.strength);
            if (modelChanged) Rebuild();
            else ApplyStage();
        }

        /// <summary>Back to the plain item look (no pet system on this object, or the pet was put away).</summary>
        public void ClearStageLook() => SetStageLook(null, 1f, default);

        private void Rebuild()
        {
            if (pet != null) Destroy(pet);
            pet = null;
            sparkles = null; // children of the old pet, like its rim shell
            rimOn = false;
            GameObject model = stageModel != null ? stageModel : current != null ? current.petPrefab : null;
            if (current == null || model == null) return;

            pet = Instantiate(model, TargetPosition(), transform.rotation);
            pet.name = $"Pet ({current.displayName})";
            VisualUtility.PrepareCosmetic(pet, gameObject.layer);
            // Prop art comes at any scale (Synty drones are metres across); normalise to a companion size.
            pet.transform.localScale = Vector3.one;
            float extent = MaxDimension(pet);
            float scale = extent > 0.001f ? normalizedSize / extent : 1f;
            baseScale = scale * Mathf.Max(0.05f, current.petScale);
            pet.SetActive(isActiveAndEnabled);
            velocity = Vector3.zero;
            bobPhase = Random.value * 10f;
            ApplyStage();
        }

        private void ApplyStage()
        {
            if (pet == null) return;
            pet.transform.localScale = Vector3.one * baseScale * stageScale;
            ApplyRim();
            ApplySparkles();
        }

        /// <summary>
        /// A rim of light around the pet's outline, breathing: wider, brighter and livelier the further it has grown.
        /// The pet's own colours are left alone.
        /// </summary>
        private void ApplyRim()
        {
            if (glow.rim == null || glow.strength <= 0f)
            {
                if (rimOn) GlowRim.Detach(pet);
                rimOn = false;
                return;
            }
            if (rim == null) rim = new GlowRim(glow.rim, "pet");
            float s = glow.strength;
            rim.Set(glow.color, Mathf.Lerp(glow.rimIntensity.x, glow.rimIntensity.y, s), Mathf.Lerp(3.4f, 2f, s),
                Mathf.Lerp(0.15f, 0.4f, s), Mathf.Lerp(0.6f, 1.1f, s), 0f); // no surface sparkles: the pet's are an effect of their own
            if (!rimOn) rim.Attach(pet);
            rimOn = true;
        }

        /// <summary>Soft sparkles coming off the body: more of them, larger and more solid, with every stage.</summary>
        private void ApplySparkles()
        {
            bool wanted = glow.sparkles != null && glow.strength > 0f;
            if (sparkles != null && (!wanted || sparkles.name != glow.sparkles.name))
            {
                Destroy(sparkles);
                sparkles = null;
            }
            if (!wanted) return;
            if (sparkles == null)
            {
                sparkles = Instantiate(glow.sparkles, pet.transform);
                sparkles.name = glow.sparkles.name;
                sparkles.transform.localPosition = Vector3.zero;
                sparkles.transform.localRotation = Quaternion.identity;
                VisualUtility.PrepareCosmetic(sparkles, gameObject.layer);
            }
            // The pet is scaled down from prop size; undo that for the effect so it keeps its own size whatever the model.
            float inherited = Mathf.Max(0.0001f, pet.transform.lossyScale.x);
            sparkles.transform.localScale = Vector3.one * (glowSize * stageScale / inherited);
            float s = glow.strength;
            Color tint = glow.color;
            tint.a = Mathf.Lerp(0.55f, 1f, s);
            foreach (var particles in sparkles.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startColor = tint;
                main.startSizeMultiplier = Mathf.Lerp(0.8f, 1.3f, s);
                var emission = particles.emission;
                emission.rateOverTime = Mathf.Lerp(glow.sparkleRate.x, glow.sparkleRate.y, s);
            }
        }

        private static float MaxDimension(GameObject go)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) : 0f;
        }

        private Vector3 TargetOffset()
        {
            Vector3 offset = current != null ? current.petOffset : new Vector3(0.45f, 0.75f, -0.15f);
            return transform.rotation * offset;
        }

        private Vector3 TargetPosition() => transform.position + TargetOffset();

        private void LateUpdate()
        {
            if (pet == null) return;
            var t = pet.transform;
            Vector3 targetOffset = TargetOffset();
            targetOffset.y += Mathf.Sin((Time.time + bobPhase) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;

            // Damp in the owner's frame: travel speed never builds lag, only rotation/offset changes do.
            Vector3 offset = t.position - transform.position;
            if ((offset - targetOffset).sqrMagnitude > snapDistance * snapDistance)
            {
                offset = targetOffset;
                velocity = Vector3.zero;
            }
            else offset = Vector3.SmoothDamp(offset, targetOffset, ref velocity, followSmoothTime);
            t.position = transform.position + offset;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(forward), 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));
        }

        private void OnEnable()
        {
            if (pet == null) return;
            pet.SetActive(true);
            pet.transform.position = TargetPosition();
        }

        private void OnDisable()
        {
            if (pet != null) pet.SetActive(false);
        }

        private void OnDestroy()
        {
            if (pet != null) Destroy(pet);
            rim?.Destroy();
        }
    }
}
