using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Server (M4 step 7): every skill hit a player's server body lands (<see cref="PlayerSkillAttacks.SkillHitShown"/>) goes on the
    /// attacker's SkillHitEvent ring for the views. Each body is listened to once.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SkillHitRecordServerSystem : SystemBase
    {
        readonly HashSet<PlayerSkillAttacks> m_Heard = new HashSet<PlayerSkillAttacks>();

        protected override void OnUpdate()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out PlayerSkillAttacks attacks)
                    || !m_Heard.Add(attacks)) continue;
                Entity ghost = entry.Ghost;
                attacks.SkillHitShown += (skillId, position) => Record(ghost, skillId, position);
            }
            m_Heard.RemoveWhere(a => a == null);
        }

        void Record(Entity ghost, string skillId, Vector3 position)
        {
            if (World == null || !World.IsCreated) return;
            EntityManager em = World.EntityManager;
            if (!em.Exists(ghost) || !em.HasBuffer<SkillHitEvent>(ghost) || !em.HasComponent<SkillHitSeq>(ghost)) return;
            SkillHitSeq seq = em.GetComponentData<SkillHitSeq>(ghost);
            SkillHitRing.Append(em.GetBuffer<SkillHitEvent>(ghost), ref seq, new SkillHitEvent
            {
                SkillId = new FixedString64Bytes(skillId),
                XMm = (int)math.round(position.x * 1000f), YMm = (int)math.round(position.y * 1000f), ZMm = (int)math.round(position.z * 1000f),
            });
            em.SetComponentData(ghost, seq);
        }
    }

    /// <summary>
    /// Client (M4 step 7): each skill hit once on every view of the attacker, its own included, as NGO's SkillHitVfxClientRpc (Everyone)
    /// played it, through <see cref="PlayerSkillAttacks.ShowSkillHitSeen"/>. A ring first met is taken as seen.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SkillHitPresentSystem : SystemBase
    {
        readonly Dictionary<Entity, uint> m_Seen = new Dictionary<Entity, uint>();

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry attacker in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(attacker.World, World) || attacker.Body == null || !em.Exists(attacker.Ghost)
                    || !em.HasBuffer<SkillHitEvent>(attacker.Ghost)) continue;
                DynamicBuffer<SkillHitEvent> ring = em.GetBuffer<SkillHitEvent>(attacker.Ghost, true);
                if (!m_Seen.TryGetValue(attacker.Ghost, out uint seen))
                {
                    m_Seen[attacker.Ghost] = SkillHitRing.Newest(ring);
                    continue;
                }
                for (int i = 0; i < ring.Length; i++)
                {
                    SkillHitEvent hit = ring[i];
                    if (hit.Seq <= seen) continue;
                    seen = hit.Seq;
                    if (attacker.Body.TryGetComponent(out PlayerSkillAttacks attacks))
                        attacks.ShowSkillHitSeen(hit.SkillId.ToString(), new Vector3(hit.XMm, hit.YMm, hit.ZMm) * 0.001f);
                }
                m_Seen[attacker.Ghost] = seen;
            }
        }
    }
}
