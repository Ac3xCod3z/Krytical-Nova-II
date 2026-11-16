using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Player.Entities.Presentation
{
    /// <summary>
    /// What the rest of the game (World, travel, the rain, nameplates) sees of a player ghost: a read-only <see cref="IPlayerBody"/>.
    /// It reads the entity; it changes nothing. No Netcode type crosses into Core: the interface is Core's, this class is the adapter.
    /// <see cref="Transform"/> is the presentation body (where the player is SEEN); <see cref="GhostPosition"/> is where the ghost is.
    /// </summary>
    public sealed class GhostPlayerBody : IPlayerBody
    {
        readonly GhostBodyEntry m_Entry;

        internal GhostPlayerBody(GhostBodyEntry entry) => m_Entry = entry;

        public Transform Transform => m_Entry.Body != null ? m_Entry.Body.transform : null;

        /// <summary>The server's key; none on a client.</summary>
        public PlayerKey Owner => m_Entry.Key;

        public bool IsLocal => m_Entry.IsLocal;

        /// <summary>Jumping is not in the ghost yet (M3b); a body that is never mid-jump lets travel and rain treat it as grounded.</summary>
        public bool IsJumping => false;

        /// <summary>The map does not change under a ghost (a map change rebuilds the client world), so there is nothing to rebind.</summary>
        public void RebindToScene() { }

        public int NetworkId => m_Entry.NetworkId;

        public Entity Ghost => m_Entry.Ghost;

        /// <summary>The ghost still exists in a live world and is still a player ghost.</summary>
        public bool IsAlive
        {
            get
            {
                var w = m_Entry.World;
                if (w == null || !w.IsCreated) return false;
                var em = w.EntityManager;
                return em.Exists(m_Entry.Ghost) && em.HasComponent<PlayerGhostTag>(m_Entry.Ghost);
            }
        }

        /// <summary>The ghost's own position (the simulation, not the eased body), or the body's when the ghost is gone.</summary>
        public Vector3 GhostPosition
        {
            get
            {
                if (IsAlive)
                {
                    float3 p = m_Entry.World.EntityManager.GetComponentData<LocalTransform>(m_Entry.Ghost).Position;
                    return new Vector3(p.x, p.y, p.z);
                }
                return Transform != null ? Transform.position : default;
            }
        }
    }
}
