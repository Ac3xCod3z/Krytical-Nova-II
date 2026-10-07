using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The places on the player model a weapon can sit besides the right hand and the back: the left hand, for the
    /// second copy of a paired weapon, and the two hips, where sidearms and claws ride while they are not drawn.
    /// The left sockets are made as mirror images of the right ones, so a paired weapon is symmetrical without any
    /// tuning of its own. Run by Krytical Nova > Setup > Player Prefab. A socket that already exists keeps its pose:
    /// tune it in the prefab, and delete it there to have it made again.
    /// </summary>
    public static class WeaponSocketsSetup
    {
        public const string ModelPrefabPath = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";

        private const string HandSocketName = "KatanaHandSocket";
        private const string OffHandSocketName = "OffHandSocket";
        private const string HipRightName = "HipSocket_R";
        private const string HipLeftName = "HipSocket_L";
        // A holster hangs a little below the hip joint and just clear of the thigh.
        private const float HolsterDrop = 0.1f;
        private const float HolsterOut = 0.05f;

        public static void Run()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ModelPrefabPath);
            try
            {
                var mount = root.GetComponentInChildren<WeaponVisualMount>(true);
                Transform hand = Find(root, HandSocketName);
                Transform leftHand = Find(root, "hand_l");
                Transform hipRight = Find(root, "hipAttach_r");
                Transform hipLeft = Find(root, "hipAttach_l");
                if (mount == null || hand == null || leftHand == null)
                {
                    Debug.LogWarning($"[Setup] {ModelPrefabPath} has no weapon mount, '{HandSocketName}' or 'hand_l': weapon sockets skipped.");
                    return;
                }

                Transform offHand = Ensure(leftHand, OffHandSocketName, out bool madeOffHand);
                if (madeOffHand) MirrorOnto(offHand, hand, root.transform);

                Transform holsterRight = null, holsterLeft = null;
                if (hipRight != null && hipLeft != null)
                {
                    holsterRight = Ensure(hipRight, HipRightName, out bool madeRight);
                    if (madeRight)
                    {
                        // Which way is "out" is read off the rig, not assumed.
                        float side = Mathf.Sign(root.transform.InverseTransformPoint(hipRight.position).x);
                        Vector3 outward = root.transform.right * side;
                        holsterRight.position = hipRight.position - root.transform.up * HolsterDrop + outward * HolsterOut;
                        // A weapon model's long axis is +Z and its top +Y: muzzle or tip down, top to the front (so a
                        // pistol's grip points back, ready for the hand) and its flat side against the leg.
                        holsterRight.rotation = Quaternion.LookRotation(-root.transform.up, root.transform.forward);
                    }
                    holsterLeft = Ensure(hipLeft, HipLeftName, out bool madeLeft);
                    if (madeLeft) MirrorOnto(holsterLeft, holsterRight, root.transform);
                }
                else Debug.LogWarning($"[Setup] {ModelPrefabPath} has no hipAttach_l / hipAttach_r joints: hip-carried weapons stay on the back.");

                mount.EditorSetSockets(hand, offHand, holsterRight, holsterLeft);
                EditorUtility.SetDirty(mount);
                PrefabUtility.SaveAsPrefabAsset(root, ModelPrefabPath);
                Debug.Log("[Setup] Weapon sockets wired (off hand and hips).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Transform Ensure(Transform parent, string name, out bool made)
        {
            Transform existing = parent.Find(name);
            made = existing == null;
            if (!made) return existing;
            var socket = new GameObject(name).transform;
            socket.SetParent(parent, false);
            return socket;
        }

        /// <summary>
        /// Places <paramref name="socket"/> as the reflection of <paramref name="original"/> in the character's
        /// left-right plane. A reflected frame is not a rotation, so the socket takes the reflected forward and up
        /// and lets its sideways axis come out flipped: the model on it is flipped back by its own scale.
        /// </summary>
        private static void MirrorOnto(Transform socket, Transform original, Transform character)
        {
            Vector3 Reflect(Vector3 local) => new Vector3(-local.x, local.y, local.z);
            Vector3 position = Reflect(character.InverseTransformPoint(original.position));
            Vector3 forward = Reflect(character.InverseTransformDirection(original.forward));
            Vector3 up = Reflect(character.InverseTransformDirection(original.up));
            socket.position = character.TransformPoint(position);
            socket.rotation = Quaternion.LookRotation(character.TransformDirection(forward), character.TransformDirection(up));
        }
    }
}
