using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Client: another player's casts, each once, as NGO's TriggerSkillCastClientRpc played them on every other client (M4.5c), through
    /// <see cref="PlayerCombatSkills.ShowCastSeen"/>. The player's own casts are shown by the server's reply (ShowServerCast). A ring first
    /// met is taken as seen.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class CastEventPresentSystem : SystemBase
    {
        readonly Dictionary<Entity, uint> m_Seen = new Dictionary<Entity, uint>();

        /// <summary>Casts shown here (tests).</summary>
        public int Shown { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry caster in GhostBodyRegistry.Entries)
            {
                if (caster.IsLocal || !ReferenceEquals(caster.World, World) || caster.Body == null || !em.Exists(caster.Ghost)
                    || !em.HasBuffer<CastEvent>(caster.Ghost)) continue;
                DynamicBuffer<CastEvent> ring = em.GetBuffer<CastEvent>(caster.Ghost, true);
                if (!m_Seen.TryGetValue(caster.Ghost, out uint seen))
                {
                    m_Seen[caster.Ghost] = CastEventRing.Newest(ring);
                    continue;
                }
                for (int i = 0; i < ring.Length; i++)
                {
                    CastEvent cast = ring[i];
                    if (cast.Seq <= seen) continue;
                    seen = cast.Seq;
                    Shown++;
                    if (caster.Body.TryGetComponent(out PlayerCombatSkills skills))
                        skills.ShowCastSeen(cast.SkillId.ToString(), cast.Rank, FindBody(em, cast.TargetGhostId));
                }
                m_Seen[caster.Ghost] = seen;
            }
        }

        Transform FindBody(EntityManager em, int ghostId)
        {
            if (ghostId == 0) return null;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                    return e.Body.transform;
            return null;
        }
    }
}
