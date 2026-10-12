using System.Collections;
using KryticalNova.Core;
using KryticalNova.Hostiles;
using KryticalNova.Player;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Lets a spirit go from every body that dies, on this machine only (nothing is sent: every machine hears of the
    /// death and shows its own). A creature bursts with several; a person, player or Hostile, lies a moment on the
    /// ground and then lets one small spirit rise from the mouth, light blue for the living and reddish for robots
    /// and skeletons. Which, when, where and how big: <see cref="SpiritSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpiritReleaser : MonoBehaviour
    {
        [SerializeField] private SpiritSettings settings;

        public static SpiritReleaser Instance { get; private set; }

        private void OnEnable()
        {
            Instance = this;
            SpiritMoments.BodyDied += Released; // Hostiles tell of their deaths this way; players through the game feel
        }

        private void OnDisable()
        {
            SpiritMoments.BodyDied -= Released;
            if (Instance == this) Instance = null;
        }

        /// <summary>This body has just died.</summary>
        public void Released(Transform body)
        {
            if (body == null || settings == null || !isActiveAndEnabled) return;
            StartCoroutine(Release(body));
        }

        /// <summary>What kind of spirit a body lets go: a player's by its lineage, a Hostile's by its species.</summary>
        public static SpiritKind KindOf(Transform body, SpiritSettings settings)
        {
            var identity = body.GetComponentInParent<PlayerIdentity>();
            if (identity != null)
            {
                string species = identity.Lineage != null ? identity.Lineage.speciesCode : null;
                if (species != null && settings != null)
                    foreach (string unliving in settings.unlivingSpecies)
                        if (string.Equals(unliving, species, System.StringComparison.OrdinalIgnoreCase)) return SpiritKind.Unliving;
                return SpiritKind.Living;
            }
            var hostile = body.GetComponentInParent<HostileNet>();
            if (hostile != null && hostile.Definition != null) return hostile.Definition.spirit;
            return SpiritKind.Creature;
        }

        private IEnumerator Release(Transform body)
        {
            SpiritKind kind = KindOf(body, settings);
            GameObject prefab = kind == SpiritKind.Creature ? settings.creature : kind == SpiritKind.Living ? settings.living : settings.unliving;
            if (prefab == null) yield break;
            yield return new WaitForSeconds(kind == SpiritKind.Creature ? settings.creatureDelay : settings.bodyDownDelay);
            // Gone in the meantime (its corpse was cleared away), or up again (revived): nothing leaves it.
            if (body == null || !body.gameObject.activeInHierarchy) yield break;
            var entity = body.GetComponentInParent<CombatEntity>();
            if (entity != null && entity.IsAlive) yield break;

            Vector3 at;
            float size;
            if (kind == SpiritKind.Creature)
            {
                // Out of the middle of the body, sized to it.
                float height = BodyHeight(body);
                at = body.position + Vector3.up * (height * 0.5f);
                size = settings.creatureSize * (height / 1.8f);
            }
            else
            {
                at = Mouth(body);
                size = settings.mouthSize;
            }
            // As the effect was made, which is pointing up: a spirit rises, however the body lies.
            GameObject spirit = Instantiate(prefab, at, prefab.transform.rotation);
            spirit.transform.localScale = prefab.transform.localScale * size;
            Destroy(spirit, settings.lifetime);
        }

        /// <summary>Where a person's mouth is: a little ahead of and below the head bone, wherever the body lies.</summary>
        private Vector3 Mouth(Transform body)
        {
            foreach (Animator animator in body.GetComponentsInChildren<Animator>())
            {
                if (animator.avatar == null || !animator.isHuman) continue;
                Transform jaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
                if (jaw != null) return jaw.position;
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                if (head == null) continue;
                // The head bone sits where the skull meets the neck. The mouth is a little way up the head from
                // there and out in front of the face. Which way the face points is read off the body, not off the
                // bone's own axes (rigs differ): it is square to the line up the head and to the line across the
                // shoulders, and this holds however the body lies.
                Vector3 up = neck != null ? (head.position - neck.position).normalized : head.up;
                Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                Transform right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                Vector3 across = left != null && right != null ? (right.position - left.position).normalized : head.right;
                Vector3 face = Vector3.Cross(across, up).normalized;
                return head.position + up * settings.mouthUp + face * settings.mouthAhead;
            }
            return body.position + Vector3.up * 0.2f;
        }

        private static float BodyHeight(Transform body)
        {
            var capsule = body.GetComponentInParent<CapsuleCollider>();
            if (capsule != null) return capsule.height * capsule.transform.lossyScale.y;
            var controller = body.GetComponentInParent<CharacterController>();
            return controller != null ? controller.height * controller.transform.lossyScale.y : 1.8f;
        }
    }
}
