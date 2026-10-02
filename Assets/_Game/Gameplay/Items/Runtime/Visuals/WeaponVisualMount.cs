using KryticalNova.Combat;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Sits on the object PlayerAnimancerMotor moves between the sheath and hand sockets (the old katana blade, which
    /// also carries the WeaponHitbox). Swaps the visible weapon model under it and resizes the hitbox; the draw/sheathe
    /// reparenting keeps working untouched because it moves this mount, not the model.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponVisualMount : MonoBehaviour
    {
        [Tooltip("Renderer baked into the mount itself (the original katana). Hidden once item visuals take over.")]
        [SerializeField] private Renderer builtInRenderer;
        [SerializeField] private WeaponHitbox hitbox;

        private GameObject spawned;
        private ItemDefinition current;

        private void Awake()
        {
            if (builtInRenderer == null) builtInRenderer = GetComponent<Renderer>();
            if (hitbox == null) hitbox = GetComponent<WeaponHitbox>();
        }

        public void SetWeapon(ItemDefinition item)
        {
            if (current == item && (item == null || spawned != null)) return;
            current = item;
            if (spawned != null) Destroy(spawned);
            spawned = null;

            if (builtInRenderer != null) builtInRenderer.enabled = false;
            if (item == null || item.weaponPrefab == null) return; // unarmed: mount stays, nothing visible

            spawned = Instantiate(item.weaponPrefab, transform, false);
            spawned.name = item.weaponPrefab.name;
            spawned.transform.localPosition = item.weaponLocalPosition;
            spawned.transform.localRotation = Quaternion.Euler(item.weaponLocalEuler);
            spawned.transform.localScale = item.weaponLocalScale;
            VisualUtility.PrepareCosmetic(spawned, gameObject.layer);
            if (hitbox != null) hitbox.Configure(item.hitboxCenter, item.hitboxSize);
        }
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
