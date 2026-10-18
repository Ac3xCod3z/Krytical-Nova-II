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
    /// them, and it shrinks out of the way of a camera that comes too close. While its owner fights it is kept calm
    /// (<see cref="SetCombatCalm"/>): idle, walk and run only.
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
        [Tooltip("Size of the sparkle effect against the pet's own size. At 1 its sparkles are born on a ball as wide as the pet is long, so they come off its surface and stay close to it, and they are small or large as the pet is.")]
        [SerializeField, Min(0.05f)] private float sparkleReach = 1f;

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
        private bool combatCalm;

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
        // The animation whose first frame is the pose the model rests in, when its file holds another.
        private string restPose;
        private IReadOnlyList<PetAnimations.FollowOn> followOns;

        // A charge: an animation in which the pet turns on its owner and drives at them. The animation carries one
        // bone forward a little way; the follower moves the whole pet so that the lunge ends at the owner.
        private const float FallbackOwnerHalfWidth = 0.18f;
        private const float LongestCharge = 1.5f;
        private const float ChargeReturnSpeed = 2f;
        private const float ChargeSampleStep = 1f / 24f;
        private string chargeAnimation;
        private string chargeBoneName;
        private int chargeState;
        private Transform chargeBone;
        private float chargeTravel;    // how far the animation itself carries the bone at its furthest, in the pet's own units
        private float chargeFrom;      // where the bone was when this charge began
        private bool charging;
        private Vector3 chargeOffset;  // what the follower has added to the pet's place this frame

        // Materials of the model that glow from an emission map: they breathe.
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int EmissionMapId = Shader.PropertyToID("_EmissionMap");
        private readonly List<(Renderer renderer, int index, Color colour)> glowing = new List<(Renderer, int, Color)>();
        private MaterialPropertyBlock glowBlock;
        private Vector2 emissionBreath = Vector2.one;
        private float emissionBreathsPerSecond;

        // A pet that stands on something rather than flying: it does not bob, and wears an effect under each foot.
        private bool steady;
        private GameObject footEffectPrefab;
        private IReadOnlyList<string> footBones;
        private readonly List<(Transform effect, Transform foot)> footEffects = new List<(Transform, Transform)>();

        /// <summary>Where an effect goes on a pet when it names no bone: the middle of the body, under it, or on top of it.</summary>
        public enum EffectPlace { Body, Feet, Top }

        /// <summary>
        /// An effect a pet wears as its own mark: the prefab, the bones it goes on (or a place on the body), whether
        /// it takes the stage's colour, and the animation it belongs to if it is not worn all the time: the state's
        /// name, and either the moments of it at which it plays once (shares of the animation, 0 to 1) or the stretch
        /// of it during which it plays (0 to 0 = all of it).
        /// </summary>
        public readonly struct WornEffect
        {
            public readonly GameObject Prefab;
            public readonly IReadOnlyList<string> Bones;
            public readonly bool StageColour;
            public readonly EffectPlace Place;
            public readonly Vector3 Offset;
            public readonly string Animation;
            public readonly IReadOnlyList<float> At;
            public readonly Vector2 During;
            /// <summary>Its size against the pet's footprint; 0 counts as 1.</summary>
            public readonly float Size;
            /// <summary>Worn on a bone, it turns with the bone and looks down it; its offset is in the bone's frame, in metres of the model (z = down the bone).</summary>
            public readonly bool AlongBone;
            /// <summary>What is out of it is taken away at once when its animation is cut short.</summary>
            public readonly bool EndsWithAnimation;
            /// <summary>With <see cref="AlongBone"/>: placed in the bone's frame, but facing the way the pet faces.</summary>
            public readonly bool FacesAhead;

            public WornEffect(GameObject prefab, IReadOnlyList<string> bones, bool stageColour, EffectPlace place = EffectPlace.Body,
                Vector3 offset = default, string animation = null, IReadOnlyList<float> at = null, Vector2 during = default, float size = 1f,
                bool alongBone = false, bool endsWithAnimation = false, bool facesAhead = false)
            {
                FacesAhead = facesAhead;
                AlongBone = alongBone;
                EndsWithAnimation = endsWithAnimation;
                Size = size;
                Prefab = prefab;
                Bones = bones;
                StageColour = stageColour;
                Place = place;
                Offset = offset;
                Animation = animation;
                At = at;
                During = during;
            }
        }

        // One copy of an effect as the pet wears it.
        private sealed class Worn
        {
            public Transform Effect;
            public Transform Bone;          // followed each frame, or null: it stays at Home
            public Vector3 Home;            // its place in the pet's own space when it follows no bone
            public Vector3 Offset;          // in footprints, in the pet's own space
            public bool Tinted;
            public int State;               // the animation it belongs to (0 = worn all the time)
            public IReadOnlyList<float> At;
            public Vector2 During;
            public ParticleSystem[] Systems;
            public bool Playing;
            public bool Along;              // it looks down its bone, and Offset is in the bone's frame
            public bool Ahead;              // ... but it faces the way the pet faces
            public bool Ends;               // cleared when its animation is left
            public bool Out;                // it has played in the animation that is running
        }

        /// <summary>Something the pet says at a moment of one of its animations: the state's name, the share of it (0 to 1) and the words.</summary>
        public readonly struct SpokenLine
        {
            public readonly int State;
            public readonly float At;
            public readonly string Text;

            public SpokenLine(string animation, float at, string text)
            {
                State = Animator.StringToHash(animation ?? "");
                At = at;
                Text = text;
            }
        }

        private IReadOnlyList<SpokenLine> lines;

        /// <summary>A pet has reached a line of one of its animations. Whoever shows speech bubbles listens.</summary>
        public static event System.Action<PetFollower, string> Spoke;

        /// <summary>What the pet says in its animations (null = nothing).</summary>
        public void SetLines(IReadOnlyList<SpokenLine> spoken) => lines = spoken;

        /// <summary>
        /// The animation whose first frame is the pose the model rests in (empty = it rests as its file has it).
        /// Told before the stage look, since it is used when the model is brought out.
        /// </summary>
        public void SetRestPose(string animation) => restPose = animation;

        /// <summary>The animations that are followed by another instead of going back to rest (null = none).</summary>
        public void SetFollowOns(IReadOnlyList<PetAnimations.FollowOn> list)
        {
            followOns = list;
            animations?.SetFollowOns(list);
        }

        // The pet's own effects, as asked for and as worn.
        private object wornSource;
        private IReadOnlyList<WornEffect> wornLooks;
        private readonly List<Worn> worn = new List<Worn>();
        // What the pet's Animator was in last frame, for the effects that belong to an animation.
        private int lastState;
        private float lastShare = -1f;

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

        /// <summary>
        /// The pet and the look of its stage in one step, for whatever decides both (<see cref="IPetPresenter"/>):
        /// the model is built once when either changes, so a Morph of another pet is never built on the wrong one.
        /// </summary>
        public void SetPetLook(ItemDefinition item, GameObject model, float scale, in PetGlowLook stageGlow, in PetFollowLook spacing)
        {
            bool changed = current != item || stageModel != model || (item != null && pet == null);
            current = item;
            stageModel = model;
            stageScale = Mathf.Max(0.1f, scale);
            glow = stageGlow;
            glow.strength = Mathf.Clamp01(glow.strength);
            follow = spacing;
            if (changed) Rebuild();
            else ApplyStage();
        }

        /// <summary>The names of the animations the pet that is out can play; empty for a pet with none.</summary>
        public IReadOnlyList<string> AnimationNames => pet != null && animations != null ? animations.Names : System.Array.Empty<string>();

        /// <summary>The pet plays its animations in turn, one every this many seconds. 0 = it only rests.</summary>
        public void SetAnimationCycle(float seconds) => animationCycleSeconds = Mathf.Max(0f, seconds);

        /// <summary>
        /// Whether this game takes the pet's turns itself (its own player's game, or play alone). Elsewhere the turns
        /// come from that game (<see cref="PlayAnimation"/>), so every screen shows the same trick at the same time.
        /// </summary>
        public bool LeadsTricks { get; set; } = true;

        /// <summary>Raised when this game started one of the pet's turns by itself (the animation's name hash).</summary>
        public event System.Action<int> TrickPlayed;

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

        /// <summary>
        /// The animation in which the pet charges its owner (the name of a state of its Animator; empty = none) and
        /// the bone that animation carries forward. While it plays the pet faces its owner instead of the way they
        /// face, and is moved so that its nose just reaches them at the furthest point of the lunge.
        /// </summary>
        /// <summary>
        /// An animation in which the pet is not there for a moment: the name of a state of its Animator (empty =
        /// none) and the stretch of it, as shares, during which the pet's own body is not drawn. Its effects are.
        /// </summary>
        public void SetVanish(string animation, Vector2 during)
        {
            vanishState = string.IsNullOrEmpty(animation) ? 0 : Animator.StringToHash(animation);
            vanishDuring = during;
        }

        private int vanishState;
        private Vector2 vanishDuring;
        private bool vanished;
        private Renderer[] bodyRenderers;

        /// <summary>Once a frame: the pet's own body is hidden while its vanish animation is inside its stretch.</summary>
        private void ApplyVanish()
        {
            bool gone = false;
            if (vanishState != 0 && animations != null && animations.TryGetPlaying(out int state, out float share))
                gone = state == vanishState && share >= vanishDuring.x && share <= vanishDuring.y;
            if (gone == vanished || bodyRenderers == null) return;
            vanished = gone;
            foreach (Renderer body in bodyRenderers)
                if (body != null) body.enabled = !gone;
        }

        public void SetCharge(string animation, string bone)
        {
            if (chargeAnimation == animation && chargeBoneName == bone) return;
            chargeAnimation = animation;
            chargeBoneName = bone;
            ResolveCharge();
        }

        /// <summary>
        /// How the glow of a model's emission maps breathes: between these two shares of each material's own emission
        /// colour, this many times a second. 1 and 1, or no breaths, leaves the materials as they are.
        /// </summary>
        public void SetEmissionBreath(Vector2 strength, float breathsPerSecond)
        {
            emissionBreath = strength;
            emissionBreathsPerSecond = Mathf.Max(0f, breathsPerSecond);
        }

        /// <summary>
        /// Plays one of the pet's animations now, by the hash of its name. False when this pet has no such animation,
        /// or is being kept calm.
        /// </summary>
        public bool PlayAnimation(int nameHash) => pet != null && animations != null && animations.Play(nameHash);

        /// <summary>
        /// The owner is in a fight (true) or out of one: in a fight the pet rests, walks and runs and does nothing
        /// else. No animation is played once, by turn or on demand, so there is no charge and none of the effects
        /// that belong to those animations; one under way is cut short.
        /// </summary>
        public void SetCombatCalm(bool calm)
        {
            combatCalm = calm;
            animations?.SetCalm(calm);
        }

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
            glowing.Clear();
            chargeBone = null;
            charging = false;
            chargeOffset = Vector3.zero;
            rim?.Detach();   // its halo was the old pet's outline
            rimOn = false;
            hasBounds = false;
            GameObject model = stageModel != null ? stageModel : current != null ? current.petPrefab : null;
            if (current == null || model == null) return;

            pet = Instantiate(model, transform.position, transform.rotation);
            pet.name = $"Pet ({current.displayName})";
            VisualUtility.PrepareCosmetic(pet, gameObject.layer);
            ActorDrawOrder.On(pet); // a Nova hides the pets behind it
            CharacterLod.Ensure(pet); // lighter with distance
            bodyRenderers = pet.GetComponentsInChildren<Renderer>(true); // before its effects are added: the body only
            vanished = false;
            bool posed = PetAnimations.Pose(pet, restPose); // before its Animator starts: this is the pose it goes back to
            animations = PetAnimations.For(pet);
            animations?.SetFollowOns(followOns);
            animations?.SetMoveLoop(moveLoop, moveLoopWalkSpeed, moveLoopRunSpeed);
            animations?.SetRestLoop(restLoop);
            animations?.SetCalm(combatCalm);
            FindGlowing();
            // Prop art comes at any scale (Synty drones are metres across); normalise to a companion size.
            pet.transform.localScale = Vector3.one;
            hasBounds = posed ? PetPlacement.TryMeasurePosed(pet, out unitBounds) : PetPlacement.TryMeasure(pet, out unitBounds);
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
            ResolveCharge();
        }

        /// <summary>The materials of the model that have an emission map switched on, with the colour each was given.</summary>
        private void FindGlowing()
        {
            glowing.Clear();
            foreach (Renderer renderer in pet.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer) continue;
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null || !material.IsKeywordEnabled("_EMISSION") || !material.HasProperty(EmissionMapId) || material.GetTexture(EmissionMapId) == null) continue;
                    glowing.Add((renderer, i, material.GetColor(EmissionColorId)));
                }
            }
        }

        /// <summary>The glow rises and falls like a slow breath. Each pet breathes in its own time.</summary>
        private void Breathe()
        {
            if (glowing.Count == 0 || emissionBreathsPerSecond <= 0f || Mathf.Approximately(emissionBreath.x, emissionBreath.y)) return;
            if (glowBlock == null) glowBlock = new MaterialPropertyBlock();
            float breath = 0.5f - 0.5f * Mathf.Cos((Time.time + bobPhase) * emissionBreathsPerSecond * Mathf.PI * 2f);
            float strength = Mathf.Lerp(emissionBreath.x, emissionBreath.y, breath);
            foreach ((Renderer renderer, int index, Color colour) in glowing)
            {
                if (renderer == null) continue;
                renderer.GetPropertyBlock(glowBlock, index);
                glowBlock.SetColor(EmissionColorId, colour * strength);
                renderer.SetPropertyBlock(glowBlock, index);
            }
        }

        /// <summary>
        /// Finds the charge on the model that is out: the bone, and how far the animation carries it forward by
        /// itself, read off the clip once (the model is put through it and left on its first frame, which is where
        /// its Animator starts from anyway). A model without that animation or bone has no charge.
        /// </summary>
        private void ResolveCharge()
        {
            chargeState = 0;
            chargeBone = null;
            chargeTravel = 0f;
            charging = false;
            if (pet == null || string.IsNullOrEmpty(chargeAnimation) || string.IsNullOrEmpty(chargeBoneName)) return;
            var animator = pet.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null) return;
            AnimationClip clip = null;
            foreach (AnimationClip candidate in animator.runtimeAnimatorController.animationClips)
                if (candidate != null && candidate.name == chargeAnimation) clip = candidate;
            if (clip == null) return;
            foreach (Transform bone in pet.GetComponentsInChildren<Transform>(true))
                if (bone.name == chargeBoneName) chargeBone = bone;
            if (chargeBone == null) return;

            Transform root = pet.transform;
            float from = 0f, furthest = 0f;
            for (float time = 0f; time <= clip.length; time += ChargeSampleStep)
            {
                clip.SampleAnimation(animator.gameObject, time);
                float ahead = root.InverseTransformPoint(chargeBone.position).z;
                if (time <= 0f) from = ahead;
                furthest = Mathf.Max(furthest, ahead - from);
            }
            clip.SampleAnimation(animator.gameObject, 0f);
            chargeTravel = furthest;
            if (chargeTravel > 0.0001f) chargeState = Animator.StringToHash(chargeAnimation);
        }

        /// <summary>
        /// What the charge adds to the pet's place this frame. The animation says how far through its lunge the pet
        /// is (how far it has carried the bone, as a share of the furthest it carries it: below nothing while it
        /// draws back); the follower moves the pet by that share of whatever the animation's own reach leaves
        /// between its nose and its owner. After the charge it comes back the short way.
        /// </summary>
        private Vector3 ChargeOffset(Transform t)
        {
            bool on = chargeState != 0 && chargeBone != null && animations != null
                      && animations.TryGetPlaying(out int state, out _) && state == chargeState;
            if (on && !charging) chargeFrom = t.InverseTransformPoint(chargeBone.position).z;
            charging = on;
            if (!on)
            {
                chargeOffset = Vector3.MoveTowards(chargeOffset, Vector3.zero, ChargeReturnSpeed * Time.deltaTime);
                return chargeOffset;
            }
            Vector3 toOwner = transform.position - t.position;
            toOwner.y = 0f;
            float distance = toOwner.magnitude;
            if (distance < 0.001f) return chargeOffset;
            float through = Mathf.Clamp((t.InverseTransformPoint(chargeBone.position).z - chargeFrom) / chargeTravel, -1f, 1f);
            float ownerHalfWidth = follow.spaced ? follow.ownerHalfWidth : FallbackOwnerHalfWidth;
            float nose = hasBounds ? unitBounds.max.z * fullScale : 0f;
            float needed = Mathf.Min(distance - ownerHalfWidth - nose, LongestCharge);
            float carried = chargeTravel * fullScale;
            chargeOffset = toOwner / distance * ((needed - carried) * through);
            return chargeOffset;
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
            foreach (Worn copy in worn)
                if (copy.Effect != null) Destroy(copy.Effect.gameObject);
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
            foreach (Worn copy in worn)
            {
                if (copy.Effect == null || !copy.Tinted || glow.strength <= 0f) continue;
                foreach (ParticleSystem particles in copy.Systems)
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
            Vector3 home = unitBounds.center;
            if (look.Place == EffectPlace.Feet) home.y = unitBounds.min.y;
            else if (look.Place == EffectPlace.Top) home.y = unitBounds.max.y;
            float footprint = Mathf.Max(unitBounds.size.x, unitBounds.size.z);
            effect.transform.localPosition = home + look.Offset * footprint;
            effect.transform.localRotation = Quaternion.identity;
            // The effect's own size times the pet's: one made at a size of its own (a comic mark or word) keeps it.
            effect.transform.localScale = look.Prefab.transform.localScale * (footprint * (look.Size > 0f ? look.Size : 1f));
            VisualUtility.PrepareCosmetic(effect, gameObject.layer);
            var copy = new Worn
            {
                Effect = effect.transform,
                Bone = bone,
                Home = home,
                Offset = look.AlongBone ? look.Offset : look.Offset * footprint,
                Along = look.AlongBone && bone != null,
                Ahead = look.FacesAhead,
                Ends = look.EndsWithAnimation,
                Tinted = look.StageColour,
                State = string.IsNullOrEmpty(look.Animation) ? 0 : Animator.StringToHash(look.Animation),
                At = look.At,
                During = look.During,
                Systems = effect.GetComponentsInChildren<ParticleSystem>(true),
            };
            // One that belongs to an animation waits for it: nothing of it shows until then.
            if (copy.State != 0)
                foreach (ParticleSystem particles in copy.Systems) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            worn.Add(copy);
        }

        /// <summary>
        /// The effects that belong to an animation: one with moments plays once as the animation passes each of
        /// them; one with a stretch plays while the animation is inside it and stops letting particles go when it
        /// leaves (those already out live their time).
        /// </summary>
        private void PlayAnimationEffects()
        {
            if (worn.Count == 0 && (lines == null || lines.Count == 0)) return;
            int state = 0;
            float share = 0f;
            bool running = animations != null && animations.TryGetPlaying(out state, out share);
            // A new state, or a loop come round again, starts from before its beginning.
            float before = running && state == lastState && share >= lastShare ? lastShare : -1f;
            if (running && lines != null)
                foreach (SpokenLine line in lines)
                    if (line.State == state && line.At > before && line.At <= share) Spoke?.Invoke(this, line.Text);
            foreach (Worn copy in worn)
            {
                if (copy.State == 0 || copy.Effect == null) continue;
                bool mine = running && copy.State == state;
                if (!mine && copy.Ends && (copy.Out || copy.Playing))
                {
                    // Its animation was left: nothing of it stays behind.
                    copy.Out = false;
                    copy.Playing = false;
                    foreach (ParticleSystem particles in copy.Systems) particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    continue;
                }
                if (copy.At != null && copy.At.Count > 0)
                {
                    if (!mine) continue;
                    for (int i = 0; i < copy.At.Count; i++)
                    {
                        if (copy.At[i] <= before || copy.At[i] > share) continue;
                        copy.Out = true;
                        foreach (ParticleSystem particles in copy.Systems)
                        {
                            particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                            particles.Play(false);
                        }
                        break;
                    }
                    continue;
                }
                bool whole = copy.During.x <= 0f && copy.During.y <= 0f;
                bool inside = mine && (whole || (share >= copy.During.x && share <= copy.During.y));
                if (inside == copy.Playing) continue;
                copy.Playing = inside;
                foreach (ParticleSystem particles in copy.Systems)
                {
                    if (inside) particles.Play(false);
                    else particles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                }
            }
            lastState = running ? state : 0;
            lastShare = running ? share : -1f;
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
            // The effect's own size times the pet's footprint (the cloud is made at size 1; the fire is made small).
            effect.transform.localScale = footEffectPrefab.transform.localScale * Mathf.Max(unitBounds.size.x, unitBounds.size.z);
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
            foreach (Worn copy in worn)
            {
                if (copy.Effect == null || copy.Bone == null) continue;
                if (copy.Along)
                {
                    // A bone runs along its own y: the effect looks down it, and its offset is measured in that frame.
                    Quaternion down = Quaternion.LookRotation(copy.Bone.up, -copy.Bone.forward);
                    copy.Effect.SetPositionAndRotation(copy.Bone.position + down * (copy.Offset * pet.transform.lossyScale.x), copy.Ahead ? pet.transform.rotation : down);
                }
                else copy.Effect.position = copy.Bone.position + pet.transform.TransformVector(copy.Offset);
            }
            PlayAnimationEffects();
            ApplyVanish();
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
            if (glow.strength <= 0f || glow.noRim)
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
                sparkles.transform.localRotation = Quaternion.identity;
                VisualUtility.PrepareCosmetic(sparkles, gameObject.layer);
            }
            // Round the middle of the body and as big as the pet (it is the pet's child, so it grows with it): the
            // sparkles of a small pet are small and close, whatever size its model was made at.
            sparkles.transform.localPosition = hasBounds ? unitBounds.center : Vector3.zero;
            float longest = hasBounds ? Mathf.Max(unitBounds.size.x, Mathf.Max(unitBounds.size.y, unitBounds.size.z)) : 1f;
            sparkles.transform.localScale = Vector3.one * (sparkleReach * longest);
            float s = glow.strength;
            Color tint = glow.color;
            tint.a = Mathf.Lerp(0.55f, 1f, s);
            foreach (var particles in sparkles.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.startColor = tint;
                // Their size is the effect's own. (It used to be multiplied here, which on a size given as a range
                // sets the top of the range: every sparkle effect came out up to a whole pet-length across.)
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
            // In a charge only its owner's going counts: the lunge is the pet's own, and is not a reason to swim off.
            float speed = charging ? ownerSpeed : Mathf.Max(ownerSpeed, petSpeed);
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
            t.position -= chargeOffset; // back to where it keeps itself: a charge is added on top of that, below
            if (animations != null)
            {
                animations.SetGait(OwnerGait(t));
                // Only the pet's own player's game takes its turns; everyone else plays what that game sends (online
                // phase 3, owner: a pet's tricks were different on each screen).
                int trick = animations.Tick(LeadsTricks ? animationCycleSeconds : 0f);
                if (trick != 0) TrickPlayed?.Invoke(trick);
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            // It looks the way its owner does, except in a charge: then it turns on them, twice as fast.
            Vector3 facing = forward;
            float turn = turnSpeed;
            if (charging)
            {
                Vector3 toOwner = transform.position - t.position;
                toOwner.y = 0f;
                if (toOwner.sqrMagnitude > 0.0001f)
                {
                    facing = toOwner;
                    turn = turnSpeed * 2f;
                }
            }
            if (facing.sqrMagnitude > 0.0001f)
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(facing), 1f - Mathf.Exp(-turn * Time.deltaTime));

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
            t.position += ChargeOffset(t);
            PlaceFootEffects();
            Breathe();

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
