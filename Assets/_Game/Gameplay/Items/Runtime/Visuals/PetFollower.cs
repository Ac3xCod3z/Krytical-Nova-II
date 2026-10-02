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

        private GameObject pet;
        private ItemDefinition current;
        private Vector3 velocity;
        private float bobPhase;

        public void SetPet(ItemDefinition item)
        {
            if (current == item && (item == null || pet != null)) return;
            current = item;
            if (pet != null) Destroy(pet);
            pet = null;
            if (item == null || item.petPrefab == null) return;

            pet = Instantiate(item.petPrefab, TargetPosition(), transform.rotation);
            pet.name = $"Pet ({item.displayName})";
            VisualUtility.PrepareCosmetic(pet, gameObject.layer);
            // Prop art comes at any scale (Synty drones are metres across); normalise to a companion size.
            pet.transform.localScale = Vector3.one;
            float extent = MaxDimension(pet);
            float scale = extent > 0.001f ? normalizedSize / extent : 1f;
            pet.transform.localScale = Vector3.one * scale * Mathf.Max(0.05f, item.petScale);
            pet.SetActive(isActiveAndEnabled);
            velocity = Vector3.zero;
            bobPhase = Random.value * 10f;
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
