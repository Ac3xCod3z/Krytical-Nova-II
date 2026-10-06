using System.Collections.Generic;
using KryticalNova.Core;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.NPC
{
    /// <summary>
    /// A clickable town NPC. Plain scene object (not networked): it is static and identical on every peer.
    /// Interaction is client-side only for now: it turns toward the player and opens the dialogue panel.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcInteractable : MonoBehaviour, IInteractable, INpcDialogueSource
    {
        private const float StandOffDistance = 1.6f;
        private const float NameplateHeight = 2.25f;
        private const float TurnSpeedDegrees = 360f;

        private static readonly List<NpcInteractable> ActiveNpcs = new List<NpcInteractable>(16);

        /// <summary>Every enabled NPC; read by the nameplate overlay. Do not modify.</summary>
        public static IReadOnlyList<NpcInteractable> Active => ActiveNpcs;

        [SerializeField] private NpcDefinition definition;
        [Tooltip("Optional animator to start at a random point so idles are not in sync.")]
        [SerializeField] private Animator animator;

        private Quaternion homeRotation;
        private Quaternion targetRotation;
        private Vector3 interactPoint;
        private bool hasInteractPoint;

        public NpcDefinition Definition => definition;
        public string DisplayName => definition != null ? definition.displayName : name;
        public string Title => definition != null ? definition.title : string.Empty;
        public string Greeting => definition != null ? definition.greeting : string.Empty;
        public string Services => definition != null ? definition.services : string.Empty;
        public NpcOffer Offers => definition != null ? definition.offers : NpcOffer.None;
        public float InteractRange => definition != null ? definition.interactRange : 3f;
        public Vector3 Position => transform.position;
        public Vector3 InteractPoint
        {
            get
            {
                // Resolved lazily: the baked NavMesh may register after this NPC's Awake.
                if (!hasInteractPoint) hasInteractPoint = TryResolveInteractPoint(out interactPoint);
                return hasInteractPoint ? interactPoint : transform.position + transform.forward * StandOffDistance;
            }
        }
        public Vector3 NameplatePosition => transform.position + Vector3.up * NameplateHeight;

        /// <summary>
        /// True when an NPC offering <paramref name="offer"/> stands within its range (plus <paramref name="padding"/>)
        /// of a point. NPCs are scene objects, so the server and every client get the same answer.
        /// </summary>
        public static bool IsOffered(NpcOffer offer, Vector3 position, float padding = 0f)
        {
            for (int i = 0; i < ActiveNpcs.Count; i++)
            {
                NpcInteractable npc = ActiveNpcs[i];
                if (npc == null || (npc.Offers & offer) == 0) continue;
                Vector3 offset = npc.transform.position - position;
                offset.y = 0f;
                float reach = npc.InteractRange + padding;
                if (offset.sqrMagnitude <= reach * reach) return true;
            }
            return false;
        }

        private void Awake()
        {
            homeRotation = transform.rotation;
            targetRotation = homeRotation;
        }

        private void OnEnable() => ActiveNpcs.Add(this);
        private void OnDisable() => ActiveNpcs.Remove(this);

        private void Start()
        {
            if (animator != null && animator.isActiveAndEnabled)
                animator.Play(0, 0, Random.value);
        }

        private void Update()
        {
            if (transform.rotation != targetRotation)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, TurnSpeedDegrees * Time.deltaTime);
        }

        public void Interact(GameObject player)
        {
            if (player == null) return;
            Vector3 toPlayer = player.transform.position - transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.01f)
                targetRotation = Quaternion.LookRotation(toPlayer, Vector3.up);

            // TODO(server): shops/quests will go through a server-validated RequestInteractServerRpc.
            ClientPresentationRegistry.Dialogue?.ShowDialogue(this, player.transform);
        }

        /// <summary>Called by the dialogue panel when it closes, so the NPC turns back to its post.</summary>
        public void EndInteraction() => targetRotation = homeRotation;

        // Uses the home facing, so the stand-off point does not drift while the NPC turns.
        private bool TryResolveInteractPoint(out Vector3 point)
        {
            Vector3 front = transform.position + homeRotation * Vector3.forward * StandOffDistance;
            bool found = NavMesh.SamplePosition(front, out NavMeshHit hit, 2f, NavMesh.AllAreas);
            point = found ? hit.position : front;
            return found;
        }
    }
}
