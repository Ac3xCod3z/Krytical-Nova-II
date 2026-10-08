using KryticalNova.Rendering;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Client-side cosmetic pet. Spawned from the replicated loadout on every peer (no NetworkObject, no bandwidth),
    /// floats beside its owner's head with a gentle bob. Lives unparented and is damped towards its place, so it
    /// trails a little at a run. Where it hovers follows its size (<see cref="PetPlacement"/>): its nearest edge keeps
    /// the same clear air from the owner at every growth stage, it goes round its owner on a turn instead of through
    /// them, and it shrinks out of the way of a camera that comes too close.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PetFollower : MonoBehaviour
    {
        private static readonly Vector3 DefaultOffset = new Vector3(0.45f, 0.75f, -0.15f);
        // A resting pet is this much inside its own clear distance before it counts as too near: rounding, not a nudge.
        private const float ClearSlack = 0.002f;
        // Shrinking away from the camera and coming back: quick, and it returns a little further off than it left.
        private const float ShrinkSeconds = 0.12f;
        private const float CameraReturnMargin = 0.25f;

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

        // Where it keeps itself. The model's box is measured once, in its own space, so neither its size nor its
        // place depends on which way the owner faced when it came out.
        private PetFollowLook follow;
        private Bounds unitBounds;
        private bool hasBounds;
        private float fullScale = 1f;      // base x stage, before the camera shrinks it
        private Vector3 hoverLocal = DefaultOffset;
        private float clearDistance;       // from the owner's centre line to its nearest edge at rest; 0 = not kept
        private float shown = 1f;          // 1 = full size, 0 = shrunk away from the camera
        private bool hiddenByCamera;

        private static Camera viewCamera;
        private static int viewCameraFrame = -1;

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
        /// size step, its glow (strength 0 = none) and how it keeps its place beside its owner.
        /// </summary>
        public void SetStageLook(GameObject model, float scale, in PetGlowLook stageGlow, in PetFollowLook spacing)
        {
            bool modelChanged = stageModel != model;
            stageModel = model;
            stageScale = Mathf.Max(0.1f, scale);
            glow = stageGlow;
            glow.strength = Mathf.Clamp01(glow.strength);
            follow = spacing;
            if (modelChanged) Rebuild();
            else ApplyStage();
        }

        /// <summary>Back to the plain item look (no pet system on this object, or the pet was put away).</summary>
        public void ClearStageLook() => SetStageLook(null, 1f, default, default);

        private void Rebuild()
        {
            if (pet != null) Destroy(pet);
            pet = null;
            sparkles = null; // children of the old pet
            rim?.Detach();   // its halo was the old pet's outline
            rimOn = false;
            hasBounds = false;
            GameObject model = stageModel != null ? stageModel : current != null ? current.petPrefab : null;
            if (current == null || model == null) return;

            pet = Instantiate(model, transform.position, transform.rotation);
            pet.name = $"Pet ({current.displayName})";
            VisualUtility.PrepareCosmetic(pet, gameObject.layer);
            // Prop art comes at any scale (Synty drones are metres across); normalise to a companion size.
            pet.transform.localScale = Vector3.one;
            hasBounds = PetPlacement.TryMeasure(pet, out unitBounds);
            float extent = hasBounds ? Mathf.Max(unitBounds.size.x, Mathf.Max(unitBounds.size.y, unitBounds.size.z)) : 0f;
            float scale = extent > 0.001f ? normalizedSize / extent : 1f;
            baseScale = scale * Mathf.Max(0.05f, current.petScale);
            velocity = Vector3.zero;
            bobPhase = Random.value * 10f;
            shown = 1f;
            hiddenByCamera = false;
            ApplyStage();
            pet.transform.position = TargetPosition();
        }

        private void ApplyStage()
        {
            if (pet == null) return;
            fullScale = baseScale * stageScale;
            ApplyScale();
            PlaceHover();
            ApplyRim();
            ApplySparkles();
        }

        /// <summary>The pet at its stage's size, less whatever the camera has shrunk away; gone altogether at nothing.</summary>
        private void ApplyScale()
        {
            if (pet == null) return;
            bool visible = shown > 0f;
            if (visible) pet.transform.localScale = Vector3.one * (fullScale * shown);
            bool active = visible && isActiveAndEnabled;
            if (pet.activeSelf != active) pet.SetActive(active);
        }

        /// <summary>
        /// Where the pet hovers at this size: its nearest edge the same clear air from the owner as at any other
        /// size, its underside where it is at base size. Without spacing it is the item's authored point.
        /// </summary>
        private void PlaceHover()
        {
            Vector3 authored = current != null ? current.petOffset : DefaultOffset;
            if (!follow.spaced || !hasBounds)
            {
                hoverLocal = authored;
                clearDistance = 0f;
                return;
            }
            hoverLocal = PetPlacement.HoverPoint(authored, unitBounds, baseScale, fullScale, follow.ClearDistanceAt(stageScale));
            clearDistance = PetPlacement.NearestDistance(hoverLocal, unitBounds, fullScale);
        }

        /// <summary>
        /// A halo of light round the pet's outline, breathing: wider, brighter and livelier the further it has grown.
        /// The pet's own colours are left alone.
        /// </summary>
        private void ApplyRim()
        {
            if (glow.strength <= 0f)
            {
                if (rimOn) rim.Detach();
                rimOn = false;
                return;
            }
            if (rim == null) rim = new GlowRim(); // no twinkle: the pet's sparkles are an effect of their own
            float s = glow.strength;
            rim.Set(new GlowHaloLook
            {
                color = glow.color,
                intensity = Mathf.Lerp(glow.rimIntensity.x, glow.rimIntensity.y, s), width = Mathf.Lerp(glow.rimWidth.x, glow.rimWidth.y, s),
                pulse = Mathf.Lerp(glow.rimPulse.x, glow.rimPulse.y, s), pulseSpeed = Mathf.Lerp(glow.rimPulseSpeed.x, glow.rimPulseSpeed.y, s),
                pulseWidth = Mathf.Lerp(glow.rimPulseWidth.x, glow.rimPulseWidth.y, s),
            });
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
            float inherited = Mathf.Max(0.0001f, fullScale);
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

        private Vector3 TargetOffset() => transform.rotation * hoverLocal;

        private Vector3 TargetPosition() => transform.position + TargetOffset();

        private void LateUpdate()
        {
            if (pet == null) return;
            var t = pet.transform;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(forward), 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));

            Vector3 targetOffset = TargetOffset();
            targetOffset.y += Mathf.Sin((Time.time + bobPhase) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;

            // The pet hangs in the world and is drawn to its place beside the owner: it trails a running owner by
            // about their speed x the smooth time, and catches up when they stop.
            Vector3 offset = t.position - transform.position;
            if ((offset - targetOffset).sqrMagnitude > snapDistance * snapDistance)
            {
                offset = targetOffset;
                velocity = Vector3.zero;
            }
            else
            {
                offset = Vector3.SmoothDamp(offset, targetOffset, ref velocity, followSmoothTime);
                // On its way round a turning owner it goes round them, never through.
                if (clearDistance > ClearSlack)
                    offset = PetPlacement.KeepClear(offset, ref velocity, targetOffset, t.rotation, unitBounds, fullScale,
                        clearDistance - ClearSlack, -forward);
            }
            t.position = transform.position + offset;

            StayClearOfCamera(t);
        }

        /// <summary>
        /// A pet the camera has come too close to (a wall behind the camera pushes it in) would fill the view or
        /// cut through the lens: it shrinks away, and comes back when the camera has room again.
        /// </summary>
        private void StayClearOfCamera(Transform t)
        {
            bool tooNear = false;
            if (follow.cameraClearance > 0f && hasBounds)
            {
                Camera view = ViewCamera();
                if (view != null)
                {
                    Vector3 centre = t.position + t.rotation * (unitBounds.center * fullScale);
                    float room = Vector3.Distance(view.transform.position, centre) - unitBounds.extents.magnitude * fullScale;
                    float limit = hiddenByCamera ? follow.cameraClearance * (1f + CameraReturnMargin) : follow.cameraClearance;
                    tooNear = room < limit;
                }
            }
            hiddenByCamera = tooNear;
            float wanted = tooNear ? 0f : 1f;
            if (Mathf.Approximately(shown, wanted)) return;
            shown = Mathf.MoveTowards(shown, wanted, Time.deltaTime / ShrinkSeconds);
            ApplyScale();
        }

        // One look-up a frame for every pet on screen.
        private static Camera ViewCamera()
        {
            if (viewCameraFrame != Time.frameCount)
            {
                viewCameraFrame = Time.frameCount;
                viewCamera = Camera.main;
            }
            return viewCamera;
        }

        private void OnEnable()
        {
            if (pet == null) return;
            ApplyScale();
            pet.transform.position = TargetPosition();
        }

        private void OnDisable()
        {
            if (pet != null) pet.SetActive(false);
        }

        private void OnDestroy()
        {
            if (pet != null) Destroy(pet);
            rim?.Detach();
        }

#if UNITY_EDITOR
        // For tuning the follow gap in the Scene view: the owner's width where the pet hovers, and the pet's box.
        private void OnDrawGizmosSelected()
        {
            if (pet == null || !hasBounds) return;
            Vector3 centre = pet.transform.position + pet.transform.rotation * (unitBounds.center * fullScale);
            Gizmos.color = new Color(1f, 0.79f, 0.24f);
            Gizmos.matrix = Matrix4x4.TRS(centre, pet.transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, unitBounds.size * fullScale);
            if (!follow.spaced) return;
            Gizmos.color = new Color(0.31f, 0.89f, 0.94f);
            Gizmos.matrix = Matrix4x4.TRS(new Vector3(transform.position.x, centre.y, transform.position.z), Quaternion.identity, new Vector3(1f, 0f, 1f));
            Gizmos.DrawWireSphere(Vector3.zero, follow.ownerHalfWidth);
            Gizmos.DrawWireSphere(Vector3.zero, follow.ClearDistanceAt(stageScale));
        }
#endif
    }
}
