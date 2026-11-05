using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Server: a Squad member's Stance that reached a player's server body (NGO's ServerReceiveSquadBuff) goes on that player's
    /// SquadBuffEvent ring for the clients (M4.5g): the one place the ring is written. Each body is listened to once.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SquadBuffRecordServerSystem : SystemBase
    {
        readonly HashSet<PlayerCombatSkills> m_Heard = new HashSet<PlayerCombatSkills>();

        /// <summary>Squad buffs written (tests).</summary>
        public int Recorded { get; private set; }

        protected override void OnUpdate()
        {
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out PlayerCombatSkills skills)
                    || !m_Heard.Add(skills)) continue;
                Entity ghost = entry.Ghost;
                skills.SquadBuffGiven += (skillId, rank, giver) => Record(ghost, skillId, rank, giver);
            }
            m_Heard.RemoveWhere(s => s == null);
        }

        void Record(Entity ghost, string skillId, int rank, string giver)
        {
            if (World == null || !World.IsCreated) return;
            EntityManager em = World.EntityManager;
            if (!em.Exists(ghost) || !em.HasBuffer<SquadBuffEvent>(ghost) || !em.HasComponent<SquadBuffSeq>(ghost)) return;
            SquadBuffSeq seq = em.GetComponentData<SquadBuffSeq>(ghost);
            SquadBuffRing.Append(em.GetBuffer<SquadBuffEvent>(ghost), ref seq, new SquadBuffEvent
            {
                SkillId = new FixedString64Bytes(skillId),
                Rank = (short)Mathf.Clamp(rank, 0, short.MaxValue),
                Giver = Fit(giver),
            });
            em.SetComponentData(ghost, seq);
            Recorded++;
        }

        // a name longer than the field keeps its start (UTF-8 bytes)
        static FixedString32Bytes Fit(string text)
        {
            var fixedText = new FixedString32Bytes();
            if (text != null)
                foreach (char c in text) if (fixedText.Append(c) != FormatError.None) break;
            return fixedText;
        }
    }

    /// <summary>
    /// Client: each Squad buff a player was given, once, on every view of that player, the player's own included, as NGO's
    /// SquadBuffClientRpc (Everyone) showed it, through <see cref="PlayerCombatSkills.ShowSquadBuff"/>. A ring first met is taken as seen.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class SquadBuffPresentSystem : SystemBase
    {
        readonly Dictionary<Entity, uint> m_Seen = new Dictionary<Entity, uint>();

        /// <summary>Squad buffs shown here (tests).</summary>
        public int Shown { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry player in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(player.World, World) || player.Body == null || !em.Exists(player.Ghost)
                    || !em.HasBuffer<SquadBuffEvent>(player.Ghost)) continue;
                DynamicBuffer<SquadBuffEvent> ring = em.GetBuffer<SquadBuffEvent>(player.Ghost, true);
                if (!m_Seen.TryGetValue(player.Ghost, out uint seen))
                {
                    m_Seen[player.Ghost] = SquadBuffRing.Newest(ring);
                    continue;
                }
                for (int i = 0; i < ring.Length; i++)
                {
                    SquadBuffEvent given = ring[i];
                    if (given.Seq <= seen) continue;
                    seen = given.Seq;
                    Shown++;
                    if (player.Body.TryGetComponent(out PlayerCombatSkills skills))
                        skills.ShowSquadBuff(given.SkillId.ToString(), given.Rank, given.Giver.ToString());
                }
                m_Seen[player.Ghost] = seen;
            }
        }
    }
}
