using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Client: another player's hold-to-charge, each start and end once (M4.5f), as NGO's ChargeStartedRpc / ChargeEndedRpc (SendTo.NotOwner)
    /// showed it, through <see cref="PlayerSkillAttacks.ShowChargeSeen"/>. The player's own charge is shown by its own hotbar.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class ChargePresentSystem : SystemBase
    {
        readonly Dictionary<Entity, byte> m_Seen = new Dictionary<Entity, byte>();

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry caster in GhostBodyRegistry.Entries)
            {
                if (caster.IsLocal || !ReferenceEquals(caster.World, World) || caster.Body == null || !em.Exists(caster.Ghost)
                    || !em.HasComponent<ChargeState>(caster.Ghost)) continue;
                ChargeState state = em.GetComponentData<ChargeState>(caster.Ghost);
                bool met = m_Seen.TryGetValue(caster.Ghost, out byte seen);
                m_Seen[caster.Ghost] = state.Seq;
                if (!met || seen == state.Seq || state.Phase == 0) continue;   // first met: nothing old is replayed
                if (caster.Body.TryGetComponent(out PlayerSkillAttacks attacks))
                    attacks.ShowChargeSeen(state.SkillId.ToString(), FindBody(em, state.AimGhostId), state.Phase == 1, state.Phase == 2);
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
