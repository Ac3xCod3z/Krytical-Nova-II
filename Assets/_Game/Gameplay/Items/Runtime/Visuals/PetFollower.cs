using System.Collections.Generic;
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

        // The model's own animations, when it has an Animator; null for one that has none.
        private PetAnimations animations;
        private float animationCycleSeconds;

        // The pet's gait follows how fast its owner is going over the ground (0 = it has no gait).
        private const float OwnerSpeedSmoothTime = 0.12f;
        private float walkFromSpeed;
        private float runFromSpeed;
        private float runToWalkSpeed;
        private const float RestHysteresis = 0.5f;
        private Vector3 lastOwnerPosition;
        private Vector3 lastPetPosition;
        private float ownerSpeed;
        private float petSpeed;
        private bool running;
        private bool moving;
        // For a model with one movement loop instead of a walk and a run.
        private string moveLoop;
        private float moveLoopWalkSpeed = 1f;
        private float moveLoopRunSpeed = 1f;
        private string restLoop;

        // A pet that stands on something rather than flying: it does not bob, and wears an effect under each foot.
        private bool steady;
        private GameObject footEffectPrefab;
        private IReadOnlyList<string> footBones;
        private readonly List<(Transform effect, Transform foot)> footEffects = new List<(Transform, Transform)>();

        /// <summary>An effect a pet wears as its own mark: the prefab, the bones it goes on, and whether it takes the stage's colour.</summary>
        public readonly struct WornEffect
        {
            public readonly GameObject Prefab;
            public readonly IReadOnlyList<string> Bones;
            public readonly bool StageColour;

            public WornEffect(GameObject prefab, IReadOnlyList<string> bones, bool stageColour)
            {
                Prefab = prefab;
                Bones = bones;
                StageColour = stageColour;
            }
        }

        // The pet's own effects, as asked for and as worn: each copy, the bone it follows, and whether it is tinted.
        private object wornSource;
        private IReadOnlyList<WornEffect> wornLooks;
        private readonly List<(Transform effect, Transform bone, bool tinted)> worn = new List<(Transform, Transform, bool)>();

        private static Camera viewCamera;
        private static int viewCameraFrame = -1;

        /// <summary>The pet in the world, or null. For effects that play on it.</summary>
        public Transform PetTransform => pet != null ? pet.transform : null;

        /// <summary>
        /// The pet's own box in the space of <see cref="PetTransform"/>: its meshes, without its glow or sparkles.
        /// False when no pet is out. For what is placed beside it on the screen.
        /// </summary>
        public bool TryGetBodyBox(out Bounds box)
        {
            box = unitBounds;
            return pet != null && hasBounds;
        }

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

        /// <summary>The names of the animations the pet that is out can play; empty for a pet with none.</summary>
        public IReadOnlyList<string> AnimationNames => pet != null && animations != null ? animations.Names : System.Array.Empty<string>();

        /// <summary>The pet plays its animations in turn, one every this many seconds. 0 = it only rests.</summary>
        public void SetAnimationCycle(float seconds) => animationCycleSeconds = Mathf.Max(0f, seconds);

        /// <summary>
        /// When the pet walks and runs, by its owner's speed over the ground in metres a second: it walks from
        /// <paramref name="walkFrom"/>, runs from <paramref name="runFrom"/> and drops back to a walk below
        /// <paramref name="runToWalk"/>. It does this on the spot: the follower is what moves it. 0 for walkFrom = it
        /// never does either.
        /// </summary>
        public void SetGaitSpeeds(float walkFrom, float runFrom, float runToWalk)
        {
            walkFromSpeed = Mathf.Max(0f, walkFrom);
            runFromSpeed = Mathf.Max(walkFromSpeed, runFrom);
            runToWalkSpeed = Mathf.Clamp(runToWalk, walkFromSpeed, runFromSpeed);
        }

        /// <summary>
        /// For a pet whose model moves in a single loop rather than a walk and a run: the true/false parameter of
        /// its Animator that holds the loop (empty = it has none), and how fast it is played while the owner walks
        /// and while the owner runs.
        /// </summary>
        public void SetMoveLoop(string parameter, float walkSpeed, float runSpeed)
        {
            moveLoop = parameter;
            moveLoopWalkSpeed = walkSpeed;
            moveLoopRunSpeed = runSpeed;
            animations?.SetMoveLoop(moveLoop, moveLoopWalkSpeed, moveLoopRunSpeed);
        }

        /// <summary>
        /// For a pet whose model should not wait in its default state (a bird that would be perched in mid-air): the
        /// true/false parameter of its Animator that holds the loop it waits in instead. Empty = none.
        /// </summary>
        public void SetRestLoop(string parameter)
        {
            restLoop = parameter;
            animations?.SetRestLoop(restLoop);
        }

        /// <summary>Plays one of the pet's animations now, by the hash of its name. False when this pet has no such animation.</summary>
        public bool PlayAnimation(int nameHash) => pet != null && animations != null && animations.Play(nameHash);

        /// <summary>
        /// How the pet carries itself: <paramref name="standsSteady"/> = it keeps its height instead of bobbing like a
        /// drone; <paramref name="underFeet"/> = an effect worn under its feet (null = none), one for each bone of the
        /// model named in <paramref name="feet"/>, or a single one under the middle of the body when none is named.
        /// </summary>
        public void SetStance(bool standsSteady, GameObject underFeet, IReadOnlyList<string> feet = null)
        {
            steady = standsSteady;
            if (footEffectPrefab == underFeet && footBones == feet) return;
            footEffectPrefab = underFeet;
            footBones = feet;
            ClearFootEffects();
            ApplyFootEffects();
        }

        /// <summary>
        /// The effects that mark this pet out (its definition's), besides what it stands on.
        /// <paramref name="source"/> is whatever the list was made from: asked again with the same one, nothing is rebuilt.
        /// </summary>
        public void SetEffects(IReadOnlyList<WornEffect> effects, object source)
        {
            if (ReferenceEquals(wornSource, source) && (source != null || wornLooks == null)) return;
            wornSource = source;
            wornLooks = effects;
            ClearWornEffects();
            ApplyWornEffects();
        }

        /// <summary>Back to the plain item look (no pet system on this object, or the pet was put away).</summary>
        public void ClearStageLook() => SetStageLook(null, 1f, default, default);

        private void Rebuild()
        {
            if (pet != null) Destroy(pet);
            pet = null;
            animations = null;
            sparkles = null; // children of the old pet
            footEffects.Clear();
            worn.Clear();
            rim?.Detach();   // its halo was the old pet's outline
            rimOn = false;
            hasBounds = false;
            GameObject model = stageModel != null ? stageModel : current != null ? current.petPrefab : null;
            if (current == null || model == null) return;

            pet = Instantiate(model, transform.position, transform.rotation);
            pet.name = $"Pet ({current.displayName})";
            VisualUtility.PrepareCosmetic(pet, gameObject.layer);
            ActorDrawOrder.On(pet); // a Nova hides the pets behind it
            animations = PetAnimations.For(pet);
            animations?.SetMoveLoop(moveLoop, moveLoopWalkSpeed, moveLoopRunSpeed);
            animations?.SetRestLoop(restLoop);
            // Prop art comes at any scale (Synty drones are metres across); normalise to a companion size.
            pet.transform.localScale = Vector3.one;
            hasBounds = PetPlacement.TryMeasure(pet, out unitBounds);
            float extent = hasBounds ? Mathf.Max(unitBounds.size.x, Mathf.Max(unitBounds.size.y, unitBounds.size.z)) : 0f;
            float scale = extent > 0.001f ? normalizedSize / extent : 1f;
            baseScale = scale * Mathf.Max(0.05f, current.petScale);
            velocity = Vector3.zero;
            bobPhase = Random.value * 10f;
            lastOwnerPosition = transform.position;
            ownerSpeed = 0f;
            running = false;
            shown = 1f;
            hiddenByCamera = false;
            ApplyStage();
            pet.transform.position = TargetPosition();
            lastPetPosition = pet.transform.position;
            petSpeed = 0f;
            moving = false;
        }

        private void ApplyStage()
        {
            if (pet == null) return;
            fullScale = baseScale * stageScale;
            ApplyScale();
            PlaceHover();
            ApplyRim();
            ApplySparkles();
            ApplyFootEffects();
            ApplyWornEffects();
        }

        private void ClearWornEffects()
        {
            foreach ((Transform effect, _, _) in worn)
                if (effect != null) Destroy(effect.gameObject);
            worn.Clear();
        }

        /// <summary>
        /// The pet's own effects: one copy on each named bone (or one on the middle of the body), a child of the pet
        /// so it grows and hides with it, sized to the pet's footprint like the clouds. Unlike the clouds its
        /// particles are left as the effect made them, so one that trails keeps its particles in the world.
        /// Asked again (a new growth stage), only the colour is set again.
        /// </summary>
        private void ApplyWornEffects()
        {
            if (pet == null || wornLooks == null || !hasBounds) return;
            if (worn.Count == 0)
            {
                Transform[] bones = null;
                foreach (WornEffect look in wornLooks)
                {
                    if (look.Prefab == null) continue;
                    int before = worn.Count;
                    if (look.Bones != null && look.Bones.Count > 0)
                    {
                        if (bones == null) bones = pet.GetComponentsInChildren<Transform>(true);
                        foreach (Transform bone in bones)
                            for (int i = 0; i < look.Bones.Count; i++)
                                if (bone.name == look.Bones[i]) AddWornEffect(look, bone);
                    }
                    if (worn.Count == before) AddWornEffect(look, null);
                }
                PlaceFootEffects();
            }
            Color tint = glow.strength > 0f ? glow.color : Color.white;
            tint.a = 1f;
            foreach ((Transform effect, _, bool tinted) in worn)
            {
                if (effect == null || !tinted || glow.strength <= 0f) continue;
                foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.startColor = tint;
                }
            }
        }

        private void AddWornEffect(in WornEffect look, Transform bone)
        {
            GameObject effect = Instantiate(look.Prefab, pet.transform);
            effect.name = look.Prefab.name;
            effect.transform.localPosition = unitBounds.center;
            effect.transform.localRotation = Quaternion.identity;
            effect.transform.localScale = Vector3.one * Mathf.Max(unitBounds.size.x, unitBounds.size.z);
            VisualUtility.PrepareCosmetic(effect, gameObject.layer);
            worn.Add((effect.transform, bone, look.StageColour));
        }

        private void ClearFootEffects()
        {
            foreach ((Transform effect, _) in footEffects)
                if (effect != null) Destroy(effect.gameObject);
            footEffects.Clear();
        }

        /// <summary>
        /// What carries a pet that stands (the fox's clouds): one effect under each named foot, sized to the pet's
        /// footprint. Each is a child of the pet, facing the way the pet faces, so it grows and hides with it and
        /// "backwards" for the effect is behind the pet; its particles live in the pet's space, so a puff stays
        /// where it was let go when the foot above it lifts.
        /// </summary>
        private void ApplyFootEffects()
        {
            if (pet == null || footEffectPrefab == null || !hasBounds || footEffects.Count > 0) return;
            if (footBones != null && footBones.Count > 0)
            {
                foreach (Transform bone in pet.GetComponentsInChildren<Transform>(true))
                    for (int i = 0; i < footBones.Count; i++)
                        if (bone.name == footBones[i]) AddFootEffect(bone);
            }
            if (footEffects.Count == 0) AddFootEffect(null);
            PlaceFootEffects();
        }

        private void AddFootEffect(Transform foot)
        {
            GameObject effect = Instantiate(footEffectPrefab, pet.transform);
            effect.name = footEffectPrefab.name;
            effect.transform.localPosition = new Vector3(unitBounds.center.x, unitBounds.min.y, unitBounds.center.z);
            effect.transform.localRotation = Quaternion.identity;
            effect.transform.localScale = Vector3.one * Mathf.Max(unitBounds.size.x, unitBounds.size.z);
            VisualUtility.PrepareCosmetic(effect, gameObject.layer);
            foreach (var particles in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom;
                main.customSimulationSpace = pet.transform;
            }
            footEffects.Add((effect.transform, foot));
        }

        /// <summary>Each foot's effect goes where the animation has put that foot this frame.</summary>
        private void PlaceFootEffects()
        {
            foreach ((Transform effect, Transform foot) in footEffects)
                if (effect != null && foot != null) effect.position = foot.position;
            foreach ((Transform effect, Transform bone, _) in worn)
                if (effect != null && bone != null) effect.position = bone.position;
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

        /// <summary>
        /// The gait that goes with how fast the owner is covering ground right now, measured from where the owner
        /// is, so it is the same on every machine. A jump in place (a portal) is not speed. A pet still catching
        /// up after its owner has stopped goes by its own speed, so it runs in rather than sliding in at rest.
        /// </summary>
        private PetGait OwnerGait(Transform t)
        {
            Vector3 step = transform.position - lastOwnerPosition;
            lastOwnerPosition = transform.position;
            step.y = 0f;
            Vector3 petStep = t.position - lastPetPosition;
            lastPetPosition = t.position;
            petStep.y = 0f;
            float dt = Time.deltaTime;
            if (dt > 0f)
            {
                float blend = 1f - Mathf.Exp(-dt / OwnerSpeedSmoothTime);
                float now = step.sqrMagnitude > snapDistance * snapDistance ? 0f : step.magnitude / dt;
                ownerSpeed = Mathf.Lerp(ownerSpeed, now, blend);
                float petNow = petStep.sqrMagnitude > snapDistance * snapDistance ? 0f : petStep.magnitude / dt;
                petSpeed = Mathf.Lerp(petSpeed, petNow, blend);
            }
            float speed = Mathf.Max(ownerSpeed, petSpeed);
            // Once moving it rests only well below the speed that set it off, so it does not flicker at the edge.
            float restBelow = moving ? walkFromSpeed * RestHysteresis : walkFromSpeed;
            if (walkFromSpeed <= 0f || speed < restBelow)
            {
                running = false;
                moving = false;
                return PetGait.Rest;
            }
            moving = true;
            running = running ? speed >= runToWalkSpeed : speed >= runFromSpeed;
            return running ? PetGait.Run : PetGait.Walk;
        }

        private Vector3 TargetOffset() => transform.rotation * hoverLocal;

        private Vector3 TargetPosition() => transform.position + TargetOffset();

        private void LateUpdate()
        {
            if (pet == null) return;
            var t = pet.transform;
            if (animations != null)
            {
                animations.SetGait(OwnerGait(t));
                animations.Tick(animationCycleSeconds);
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(forward), 1f - Mathf.Exp(-turnSpeed * Time.deltaTime));

            Vector3 targetOffset = TargetOffset();
            if (!steady) targetOffset.y += Mathf.Sin((Time.time + bobPhase) * bobFrequency * Mathf.PI * 2f) * bobAmplitude;

            // The pet hangs in the world and is drawn to its place beside the owner: it trails a running owner by
            // about their speed x the smooth time, never by more than its longest trail, and catches up when they stop.
            Vector3 offset = t.position - transform.position;
            if ((offset - targetOffset).sqrMagnitude > snapDistance * snapDistance)
            {
                offset = targetOffset;
                velocity = Vector3.zero;
                lastPetPosition = transform.position + offset; // put there, not travelled there
            }
            else
            {
                offset = Vector3.SmoothDamp(offset, targetOffset, ref velocity, followSmoothTime);
                offset = PetPlacement.LimitTrail(offset, targetOffset, follow.maxTrail);
                // On its way round a turning owner it goes round them, never through.
                if (clearDistance > ClearSlack)
                    offset = PetPlacement.KeepClear(offset, ref velocity, targetOffset, t.rotation, unitBounds, fullScale,
                        clearDistance - ClearSlack, -forward);
            }
            t.position = transform.position + offset;
            PlaceFootEffects();

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
            lastOwnerPosition = transform.position;
            lastPetPosition = pet.transform.position;
            ownerSpeed = 0f;
            petSpeed = 0f;
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
