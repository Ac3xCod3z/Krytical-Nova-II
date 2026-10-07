using System.Collections;
using System.Collections.Generic;
using KryticalNova.Combat;
using UnityEngine;

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
    /// A tempered weapon also wears a glow: a second, additive material laid over the model's own (see
    /// <see cref="SetGlow"/>), so it works on any weapon whatever shader that uses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponVisualMount : MonoBehaviour
    {
        [Tooltip("Renderer baked into the mount itself (the original katana). Hidden once item visuals take over.")]
        [SerializeField] private Renderer builtInRenderer;
        [SerializeField] private WeaponHitbox hitbox;

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

        private GameObject spawned;     // the weapon, or the right-hand one of a pair
        private GameObject spawnedOff;  // the left-hand one of a pair
        private ItemDefinition current;
        private bool drawn;
        private bool capeWorn;
        private Coroutine settleRoutine;

        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int PulseSpeedId = Shader.PropertyToID("_PulseSpeed");
        private static readonly int SparkleId = Shader.PropertyToID("_Sparkle");
        private Material glow;          // this mount's own copy: every weapon glows in its own colour
        private bool glowOn;

        /// <summary>True while the weapon is in the hand rather than carried.</summary>
        public bool IsDrawn => drawn;

        private void Awake()
        {
            if (builtInRenderer == null) builtInRenderer = GetComponent<Renderer>();
            if (hitbox == null) hitbox = GetComponent<WeaponHitbox>();
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

        public void SetWeapon(ItemDefinition item)
        {
            if (current == item && (item == null || spawned != null)) return;
            current = item;
            ClearModels();

            if (builtInRenderer != null) builtInRenderer.enabled = false;
            if (item == null || item.weaponPrefab == null) return; // unarmed: mount stays, nothing visible

            spawned = Spawn(item);
            if (item.paired && offHandSocket != null) spawnedOff = Spawn(item);
            Place(settle: false);
            if (hitbox != null) hitbox.Configure(item.hitboxCenter, item.hitboxSize);
            if (glowOn) AttachGlow(); // a new model for the same glow (the look changed, not the Temper)
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
        /// Turns the glow on in a Temper band's colour at a rarity's strength, or off when <paramref name="template"/>
        /// is null or <paramref name="on"/> is false. The template is the shared glow material; each mount works on a copy.
        /// </summary>
        public void SetGlow(Material template, bool on, Color color, TemperGlowLook look)
        {
            if (!on || template == null)
            {
                if (glowOn) DetachGlow();
                glowOn = false;
                return;
            }
            if (glow == null) glow = new Material(template) { name = template.name + " (weapon)" };
            glow.SetColor(GlowColorId, color);
            glow.SetFloat(IntensityId, look.intensity);
            glow.SetFloat(RimPowerId, look.rimPower);
            glow.SetFloat(PulseId, look.pulse);
            glow.SetFloat(PulseSpeedId, look.pulseSpeed);
            glow.SetFloat(SparkleId, look.sparkles ? 1.5f : 0f);
            if (!glowOn) AttachGlow();
            glowOn = true;
        }

        private void AttachGlow()
        {
            if (glow == null) return;
            if (spawned != null) GlowShell.Attach(spawned, glow);
            if (spawnedOff != null) GlowShell.Attach(spawnedOff, glow);
        }

        private void DetachGlow()
        {
            if (spawned != null) GlowShell.Detach(spawned);
            if (spawnedOff != null) GlowShell.Detach(spawnedOff);
        }

        private void OnDestroy()
        {
            if (glow != null) Destroy(glow);
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
