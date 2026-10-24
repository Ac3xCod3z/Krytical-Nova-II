using System.Collections;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Rendering;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Sits on the object PlayerAnimancerMotor moves between the sheath and hand sockets (the old katana blade, which
    /// also carries the WeaponHitbox). Swaps the visible weapon model under it and resizes the hitbox; the draw/sheathe
    /// reparenting keeps working untouched because it moves this mount, not the model.
    ///
    /// The mount also decides where the model rides while the weapon is not drawn: across the back with the mount, or
    /// at the hip (<see cref="ItemDefinition.carry"/>). A paired weapon (<see cref="ItemDefinition.paired"/>) gets a
    /// second, mirrored copy in the left hand and on the left hip. It learns that the weapon was drawn or put away
    /// from its own parent changing, so nothing else has to tell it.
    ///
    /// A tempered weapon also wears a glow: a halo of light round its outline (see <see cref="SetGlow"/> and
    /// <see cref="GlowRim"/>), drawn on the screen so it works on any weapon whatever its shape or shader.
    ///
    /// A long weapon is kept out of the ground: where its far end would sink in, it is tipped up about the grip.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponVisualMount : MonoBehaviour
    {
        [Tooltip("Renderer baked into the mount itself (the original katana). Hidden once item visuals take over.")]
        [SerializeField] private Renderer builtInRenderer;
        [Tooltip("The hitbox the mount sizes to the weapon held (a WeaponHitbox).")]
        [SerializeField] private MonoBehaviour hitbox;

        [Header("Sockets (filled by Krytical Nova > Setup > Player Prefab)")]
        [Tooltip("The right-hand socket: the weapon is drawn while the mount sits under it.")]
        [SerializeField] private Transform handSocket;
        [Tooltip("Left hand: where the second copy of a paired weapon sits while drawn.")]
        [SerializeField] private Transform offHandSocket;
        [Tooltip("Right hip: where a hip-carried weapon rides while it is not drawn.")]
        [SerializeField] private Transform hipSocketRight;
        [Tooltip("Left hip: the second copy of a paired weapon.")]
        [SerializeField] private Transform hipSocketLeft;
        [Tooltip("Seconds a model takes to settle on its new spot when the weapon is drawn or put away.")]
        [SerializeField, Min(0f)] private float settleSeconds = 0.1f;
        [Tooltip("The back sheath stands off far enough to clear a cape. With no cape worn, a back-carried weapon moves in by this much (the mount's X points at the spine).")]
        [SerializeField] private Vector3 bareBackOffset = new Vector3(0.08f, 0f, 0f);
        [Tooltip("A weapon's lowest point is kept at least this far (metres) above the ground: a long axe, scythe or glaive is tipped up about the grip instead of sinking in. 0 = never.")]
        [SerializeField, Min(0f)] private float groundClearance = 0.03f;

        // Keeping long weapons out of the ground: how near the ground a weapon must be before the ground under its
        // tip is looked up, how far above or below the owner's feet that ground may be and still count (a kerb, a
        // slope; not a table the tip is under), and the least reach from the grip worth tipping.
        private const float NearGround = 0.5f;
        private const float StepLimit = 0.6f;
        private const float LeastReach = 0.1f;
        private const int GroundPasses = 2;

        private GameObject spawned;     // the weapon, or the right-hand one of a pair
        private GameObject spawnedOff;  // the left-hand one of a pair
        private Vector3[] spawnedReach, spawnedOffReach;    // each model's outermost points in its own space (null = unknown)
        private float spawnedRadius, spawnedOffRadius;      // and how far the furthest of them is from its grip
        private NavMeshAgent owner;     // whose feet say where the ground is
        private bool ownerSought;
        private ItemDefinition current;
        private ItemDefinition strikesAs;   // the weapon whose reach the hitbox has: the one really held
        private bool drawn;
        private bool capeWorn;
        private Coroutine settleRoutine;

        private GlowRim glow;           // this mount's own: every weapon glows in its own colour
        private bool glowOn;

        /// <summary>True while the weapon is in the hand rather than carried.</summary>
        public bool IsDrawn => drawn;
        /// <summary>The hand the weapon is held in. Whoever moves the mount there has drawn the weapon.</summary>
        public Transform HandSocket => handSocket;
        /// <summary>Where the weapon in the main hand is held (a two-handed weapon's second hand), or null.</summary>
        public WeaponGrip Grip { get; private set; }

        private void Awake()
        {
            if (builtInRenderer == null) builtInRenderer = GetComponent<Renderer>();
            if (hitbox == null) hitbox = GetComponent<IWeaponReach>() as MonoBehaviour;
            drawn = handSocket != null && transform.parent == handSocket;
        }

        /// <summary>The motor moved the mount to the other socket: the weapon was drawn or put away.</summary>
        private void OnTransformParentChanged()
        {
            bool now = handSocket != null && transform.parent == handSocket;
            if (now == drawn) return;
            drawn = now;
            Place(settle: true);
        }

        /// <summary>Whether something is worn on the back: a weapon slung there rides outside it, or close in when there is nothing.</summary>
        public void SetCapeWorn(bool worn)
        {
            if (capeWorn == worn) return;
            capeWorn = worn;
            Place(settle: false);
        }

        /// <summary>
        /// Shows <paramref name="item"/>'s model. <paramref name="worn"/> is the weapon really held when the model
        /// is only a look over it (a Weave): the hitbox keeps the held weapon's reach.
        /// </summary>
        public void SetWeapon(ItemDefinition item, ItemDefinition worn = null)
        {
            ItemDefinition strikes = worn != null ? worn : item;
            if (current == item && (item == null || spawned != null))
            {
                if (strikes != strikesAs && spawned != null) ConfigureHitbox(strikes);
                return;
            }
            current = item;
            ClearModels();

            if (builtInRenderer != null) builtInRenderer.enabled = false;
            if (item == null || item.weaponPrefab == null) return; // unarmed: mount stays, nothing visible

            spawned = Spawn(item);
            Grip = spawned.GetComponentInChildren<WeaponGrip>(true);
            spawnedReach = ReachOf(spawned, out spawnedRadius);
            if (item.paired && offHandSocket != null)
            {
                spawnedOff = Spawn(item);
                spawnedOffReach = ReachOf(spawnedOff, out spawnedOffRadius);
            }
            Place(settle: false);
            ConfigureHitbox(strikes);
            ApplyDye(); // a new model wears the weapon's dye
            if (glowOn) AttachGlow(); // a new model for the same glow (the look changed, not the Temper)
        }

        /// <summary>
        /// The colourway the weapon is dyed in (0 = its own colours). It is the model shown that is repainted, so a
        /// Weave over a dyed weapon is drawn in that dye, like an outfit piece.
        /// </summary>
        public void SetDye(GearThemeLibrary colourways, ushort colourway)
        {
            GearThemeLibrary.Theme theme = default;
            if (colourway != 0 && (colourways == null || !colourways.TryGet(colourway, out theme))) theme = default;
            if (theme.id == dye.id) return;
            dye = theme;
            ApplyDye();
        }

        private void ApplyDye()
        {
            Paint(spawned);
            Paint(spawnedOff);
        }

        private void Paint(GameObject model)
        {
            if (model == null) return;
            var paint = model.GetComponentInChildren<WeaponPaint>(true);
            if (paint != null) paint.Apply(dye);
        }

        private GearThemeLibrary.Theme dye;

        private void ConfigureHitbox(ItemDefinition strikes)
        {
            strikesAs = strikes;
            if (hitbox is IWeaponReach reach && strikes != null) reach.Configure(strikes.hitboxCenter, strikes.hitboxSize);
        }

        private GameObject Spawn(ItemDefinition item)
        {
            var model = Instantiate(item.weaponPrefab, transform, false);
            model.name = item.weaponPrefab.name;
            VisualUtility.PrepareCosmetic(model, gameObject.layer);
            return model;
        }

        private void ClearModels()
        {
            if (settleRoutine != null)
            {
                StopCoroutine(settleRoutine);
                settleRoutine = null;
            }
            if (spawned != null) Destroy(spawned);
            if (spawnedOff != null) Destroy(spawnedOff);
            spawned = null;
            spawnedOff = null;
            Grip = null;
            spawnedReach = null;
            spawnedOffReach = null;
        }

        /// <summary>
        /// On a fresh copy of a character (the equipment preview doll): removes the weapon models that came along
        /// from the original, which this copy's mount knows nothing about.
        /// </summary>
        public void ClearInherited()
        {
            ClearModels();
            current = null;
            DestroyChildren(transform);
            DestroyChildren(offHandSocket);
            DestroyChildren(hipSocketRight);
            DestroyChildren(hipSocketLeft);
        }

        private static void DestroyChildren(Transform holder)
        {
            if (holder == null) return;
            for (int i = holder.childCount - 1; i >= 0; i--) DestroyImmediate(holder.GetChild(i).gameObject);
        }

        // ---------------------------------------------------------------- where the models sit

        private struct Pose
        {
            public Transform parent;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        /// <summary>Where the weapon belongs right now: in the hand, or on its carry spot.</summary>
        private Pose MainPose()
        {
            var item = current;
            if (drawn)
                return new Pose
                {
                    // A bow is held in the left hand: the right one draws the string.
                    parent = item.leftHanded && offHandSocket != null ? offHandSocket : transform,
                    position = item.weaponLocalPosition, rotation = Quaternion.Euler(item.weaponLocalEuler), scale = item.weaponLocalScale
                };
            bool onHip = item.carry == WeaponCarry.Hips && hipSocketRight != null;
            return new Pose
            {
                // On the back the mount itself is on the sheath socket.
                parent = onHip ? hipSocketRight : transform,
                position = item.carryLocalPosition + (onHip || capeWorn ? Vector3.zero : bareBackOffset),
                rotation = Quaternion.Euler(item.carryLocalEuler), scale = item.weaponLocalScale
            };
        }

        /// <summary>
        /// The second copy of a pair: the mirror image of the first. The left sockets are the right ones mirrored, so
        /// the same pose with its sideways axis flipped (and the model itself flipped) is an exact reflection.
        /// </summary>
        private Pose OffPose()
        {
            var item = current;
            if (drawn)
            {
                bool own = item.offHandLocalPosition != Vector3.zero || item.offHandLocalEuler != Vector3.zero;
                if (own)
                    return new Pose
                    {
                        parent = offHandSocket, position = item.offHandLocalPosition,
                        rotation = Quaternion.Euler(item.offHandLocalEuler), scale = Flip(item.weaponLocalScale)
                    };
                return Mirror(offHandSocket, item.weaponLocalPosition, Quaternion.Euler(item.weaponLocalEuler), item.weaponLocalScale);
            }
            if (item.carry == WeaponCarry.Hips && hipSocketLeft != null)
                return Mirror(hipSocketLeft, item.carryLocalPosition, Quaternion.Euler(item.carryLocalEuler), item.weaponLocalScale);
            // On the back the two are crossed: the second is the first mirrored across the mount.
            Pose first = MainPose();
            return Mirror(transform, first.position, first.rotation, item.weaponLocalScale);
        }

        private static Pose Mirror(Transform parent, Vector3 position, Quaternion rotation, Vector3 scale) => new Pose
        {
            parent = parent,
            position = new Vector3(-position.x, position.y, position.z),
            rotation = new Quaternion(rotation.x, -rotation.y, -rotation.z, rotation.w),
            scale = Flip(scale)
        };

        private static Vector3 Flip(Vector3 scale) => new Vector3(-scale.x, scale.y, scale.z);

        private void Place(bool settle)
        {
            if (current == null || spawned == null) return;
            if (settleRoutine != null)
            {
                StopCoroutine(settleRoutine);
                settleRoutine = null;
            }
            Pose main = MainPose();
            Pose off = spawnedOff != null ? OffPose() : default;
            if (settle && settleSeconds > 0f && Application.isPlaying && isActiveAndEnabled)
            {
                settleRoutine = StartCoroutine(Settle(main, off));
                return;
            }
            Apply(spawned, main);
            if (spawnedOff != null) Apply(spawnedOff, off);
        }

        private static void Apply(GameObject model, Pose pose)
        {
            var t = model.transform;
            t.SetParent(pose.parent, false);
            t.localPosition = pose.position;
            t.localRotation = pose.rotation;
            t.localScale = pose.scale;
        }

        /// <summary>Eases each model from where it is to its new spot, so nothing pops between the hand and the hip.</summary>
        private IEnumerator Settle(Pose main, Pose off)
        {
            var models = new List<(Transform model, Pose to, Vector3 fromPosition, Quaternion fromRotation)>(2);
            void Begin(GameObject model, Pose pose)
            {
                // Where the model is now, said in the new parent's terms. Worked out by hand rather than with
                // SetParent(worldPositionStays): Unity re-derives the scale of a mirrored model there and can move
                // the flip to another axis, which would make the copy collapse through zero on its way.
                var t = model.transform;
                Vector3 fromPosition = pose.parent.InverseTransformPoint(t.position);
                Quaternion fromRotation = Quaternion.Inverse(pose.parent.rotation) * t.rotation;
                t.SetParent(pose.parent, false);
                t.localPosition = fromPosition;
                t.localRotation = fromRotation;
                t.localScale = pose.scale;
                models.Add((t, pose, fromPosition, fromRotation));
            }
            Begin(spawned, main);
            if (spawnedOff != null) Begin(spawnedOff, off);

            float elapsed = 0f;
            while (elapsed < settleSeconds)
            {
                elapsed += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, elapsed / settleSeconds);
                foreach (var m in models)
                {
                    if (m.model == null) continue;
                    m.model.localPosition = Vector3.Lerp(m.fromPosition, m.to.position, k);
                    m.model.localRotation = Quaternion.Slerp(m.fromRotation, m.to.rotation, k);
                }
                yield return null;
            }
            foreach (var m in models)
                if (m.model != null) Apply(m.model.gameObject, m.to);
            settleRoutine = null;
        }

        // ---------------------------------------------------------------- out of the ground

        /// <summary>
        /// After the animation has moved the hand: a weapon whose far end would sink into the ground is tipped up
        /// about its grip just far enough to clear it. Looks only: the hitbox is the mount's and does not move.
        /// </summary>
        private void LateUpdate()
        {
            if (groundClearance <= 0f || current == null || spawned == null) return;
            if (!ownerSought)
            {
                owner = GetComponentInParent<NavMeshAgent>();
                ownerSought = true;
            }
            if (owner == null) return; // a preview doll stands on nothing
            float feet = owner.transform.position.y - owner.baseOffset;
            // While a model eases from one socket to the other the easing has just placed it; otherwise it goes back
            // to where the item puts it first. Either way it is tipped as far as the ground asks this frame and no
            // further, so nothing builds up.
            bool easing = settleRoutine != null;
            if (spawnedReach != null) KeepOffGround(spawned.transform, MainPose(), spawnedReach, spawnedRadius, feet, easing);
            if (spawnedOff != null && spawnedOffReach != null)
                KeepOffGround(spawnedOff.transform, OffPose(), spawnedOffReach, spawnedOffRadius, feet, easing);
        }

        private void KeepOffGround(Transform model, Pose pose, Vector3[] points, float radius, float feet, bool placed)
        {
            if (!placed)
            {
                model.localPosition = pose.position;
                model.localRotation = pose.rotation;
            }
            // Out of reach of any ground that counts, whichever way it points: most weapons, most of the time.
            Vector3 size = model.lossyScale;
            float furthest = radius * Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z)));
            if (model.position.y - furthest >= feet + StepLimit + groundClearance) return;

            for (int pass = 0; pass < GroundPasses; pass++)
            {
                Vector3 lowest = LowestPoint(model, points);
                float floor = feet + groundClearance;
                if (lowest.y >= floor + NearGround) return;
                // The ground the eye sees under the tip, where there is some near the level of the owner's feet (a
                // slope ahead, a kerb; the feet themselves ride a little above it).
                if (GroundSnap.TryGetGround(lowest, out RaycastHit ground, owner.transform) && Mathf.Abs(ground.point.y - feet) <= StepLimit)
                    floor = ground.point.y + groundClearance;
                if (lowest.y >= floor) return;

                Vector3 grip = model.position;
                Vector3 reach = lowest - grip;
                float length = reach.magnitude;
                Vector3 axis = Vector3.Cross(reach, Vector3.up);
                if (length < LeastReach || axis.sqrMagnitude < 1e-8f) return; // hanging straight down: tipping gains nothing
                float now = Mathf.Asin(Mathf.Clamp(reach.y / length, -1f, 1f));
                float wanted = Mathf.Asin(Mathf.Clamp((floor - grip.y) / length, -1f, 1f));
                if (wanted <= now) return;
                model.rotation = Quaternion.AngleAxis((wanted - now) * Mathf.Rad2Deg, axis.normalized) * model.rotation;
            }
        }

        /// <summary>
        /// A model's outermost points in its own space, and how far the furthest is from its origin: the ones baked
        /// on its prefab (<see cref="WeaponReach"/>), or failing that the corners of its box.
        /// </summary>
        private static Vector3[] ReachOf(GameObject model, out float radius)
        {
            radius = 0f;
            Vector3[] points = model.TryGetComponent(out WeaponReach reach) && reach.points != null && reach.points.Length > 0 ? reach.points : null;
            if (points == null)
            {
                if (!PetPlacement.TryMeasure(model, out Bounds box)) return null;
                points = new Vector3[8];
                for (int corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    points[corner] = box.center + Vector3.Scale(box.extents, sign);
                }
            }
            foreach (Vector3 point in points) radius = Mathf.Max(radius, point.magnitude);
            return points;
        }

        private static Vector3 LowestPoint(Transform model, Vector3[] reach)
        {
            Matrix4x4 toWorld = model.localToWorldMatrix;
            int lowest = 0;
            float least = float.MaxValue;
            for (int i = 0; i < reach.Length; i++)
            {
                Vector3 p = reach[i];
                float height = toWorld.m10 * p.x + toWorld.m11 * p.y + toWorld.m12 * p.z + toWorld.m13;
                if (height >= least) continue;
                least = height;
                lowest = i;
            }
            return toWorld.MultiplyPoint3x4(reach[lowest]);
        }

        private void OnDisable()
        {
            // A coroutine does not survive a disable: finish the move it was making.
            if (settleRoutine == null) return;
            settleRoutine = null;
            if (current == null || spawned == null) return;
            Apply(spawned, MainPose());
            if (spawnedOff != null) Apply(spawnedOff, OffPose());
        }

        // ---------------------------------------------------------------- Temper glow

        /// <summary>
        /// Turns the glow on in a Temper band's colour at a rarity's strength, or off when <paramref name="on"/> is
        /// false.
        /// </summary>
        public void SetGlow(bool on, Color color, TemperGlowLook look)
        {
            if (!on)
            {
                if (glowOn) DetachGlow();
                glowOn = false;
                return;
            }
            if (glow == null) glow = new GlowRim();
            glow.Set(new GlowHaloLook
            {
                color = color, intensity = look.intensity, width = look.width,
                pulse = look.pulse, pulseSpeed = look.pulseSpeed, pulseWidth = look.pulseWidth,
                twinkle = look.twinkle, lightning = look.lightning, flame = look.flame, ember = look.ember,
            });
            if (!glowOn) AttachGlow();
            glowOn = true;
        }

        private void AttachGlow()
        {
            if (glow == null) return;
            glow.Detach(); // the models it was on may be gone: a new look spawns new ones
            if (spawned != null) glow.Attach(spawned);
            if (spawnedOff != null) glow.Attach(spawnedOff);
        }

        private void DetachGlow() => glow?.Detach();

        private void OnDestroy()
        {
            glow?.Detach();
        }

#if UNITY_EDITOR
        /// <summary>Editor setup entry point: the sockets on the character this mount belongs to.</summary>
        public void EditorSetSockets(Transform hand, Transform offHand, Transform hipRight, Transform hipLeft)
        {
            handSocket = hand;
            offHandSocket = offHand;
            hipSocketRight = hipRight;
            hipSocketLeft = hipLeft;
        }
#endif
    }

    public static class VisualUtility
    {
        /// <summary>Cosmetic copies must never collide, receive raycasts, or run gameplay physics.</summary>
        public static void PrepareCosmetic(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
