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
        [Tooltip("Size of the stage glow relative to how its effect was authored: small enough to ride on the pet, not around the owner.")]
        [SerializeField, Min(0.05f)] private float glowSize = 0.42f;

        private GameObject pet;
        private ItemDefinition current;
        private Vector3 velocity;
        private float bobPhase;

        // The look of the pet's growth stage, set by whatever owns pet growth; defaults are the plain item.
        private GameObject stageModel;
        private float stageScale = 1f;
        private GameObject auraPrefab;
        private Color auraColor = Color.white;
        private float auraStrength;
        private GameObject aura;
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
        /// size step, and a glow effect riding on it in the stage's colour (strength 0 = none).
        /// </summary>
        public void SetStageLook(GameObject model, float scale, GameObject glowPrefab, Color glowColor, float glowStrength)
        {
            bool modelChanged = stageModel != model;
            stageModel = model;
            stageScale = Mathf.Max(0.1f, scale);
            auraPrefab = glowPrefab;
            auraColor = glowColor;
            auraStrength = Mathf.Clamp01(glowStrength);
            if (modelChanged) Rebuild();
            else ApplyStage();
        }

        /// <summary>Back to the plain item look (no pet system on this object, or the pet was put away).</summary>
        public void ClearStageLook() => SetStageLook(null, 1f, null, Color.white, 0f);

        private void Rebuild()
        {
            if (pet != null) Destroy(pet);
            pet = null;
            aura = null; // a child of the old pet
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

            bool wanted = auraPrefab != null && auraStrength > 0f;
            if (aura != null && (!wanted || aura.name != auraPrefab.name))
            {
                Destroy(aura);
                aura = null;
            }
            if (!wanted) return;
            if (aura == null)
            {
                aura = Instantiate(auraPrefab, pet.transform);
                aura.name = auraPrefab.name;
                aura.transform.localPosition = Vector3.zero;
                aura.transform.localRotation = Quaternion.identity;
                VisualUtility.PrepareCosmetic(aura, gameObject.layer);
            }
            // The pet is scaled down from prop size; undo that for the glow so it keeps its own size whatever the model.
            float inherited = Mathf.Max(0.0001f, pet.transform.lossyScale.x);
            aura.transform.localScale = Vector3.one * (glowSize * stageScale / inherited);
            Color tint = auraColor;
            tint.a = Mathf.Lerp(0.35f, 1f, auraStrength);
            foreach (var particles in aura.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startColor = tint;
                var emission = particles.emission;
                emission.rateOverTimeMultiplier = Mathf.Lerp(0.3f, 1f, auraStrength);
                // Ribbons keep the colour they were authored with unless they take it from their particle.
                var trails = particles.trails;
                if (!trails.enabled) continue;
                trails.inheritParticleColor = true;
                trails.colorOverLifetime = Color.white;
                trails.colorOverTrail = Color.white;
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
        }
    }
}
