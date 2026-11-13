using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Server: each player's running buffs as one list on its ghost (M4.5i, owner pick A), rewritten whenever its server body's
    /// <see cref="CharacterBuffManager"/> changes (a buff added, refreshed, run out, broken, dispelled, spent, interrupted, cleared).
    /// Clocks go as end ticks, so an unchanged list is never sent again.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class RunningBuffsServerSystem : SystemBase
    {
        readonly HashSet<CharacterBuffManager> m_Heard = new HashSet<CharacterBuffManager>();
        readonly HashSet<CharacterBuffManager> m_Dirty = new HashSet<CharacterBuffManager>();
        readonly List<RunningBuffEntry> m_Running = new List<RunningBuffEntry>();

        /// <summary>Lists written (tests).</summary>
        public int Written { get; private set; }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton(out NetworkTime time) || !time.ServerTick.IsValid) return;
            int rate = SystemAPI.TryGetSingleton(out ClientServerTickRate r) && r.SimulationTickRate > 0 ? (int)r.SimulationTickRate : 30;
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out CharacterBuffManager buffs)) continue;
                if (m_Heard.Add(buffs))
                {
                    buffs.BuffsChanged += () => m_Dirty.Add(buffs);
                    buffs.BuffRemoved += _ => m_Dirty.Add(buffs);
                    m_Dirty.Add(buffs);
                }
                if (!m_Dirty.Remove(buffs) || !em.Exists(entry.Ghost) || !em.HasBuffer<RunningBuff>(entry.Ghost)) continue;
                BuffMirror.Capture(buffs, m_Running);
                DynamicBuffer<RunningBuff> list = em.GetBuffer<RunningBuff>(entry.Ghost);
                list.Clear();
                foreach (RunningBuffEntry b in m_Running)
                    list.Add(new RunningBuff
                    {
                        SkillId = new FixedString64Bytes(b.SkillId), Rank = (short)Mathf.Clamp(b.Rank, 0, short.MaxValue),
                        EndTick = RunningBuffs.EndTick(time.ServerTick, b.Remaining, rate), FromSquad = (byte)(b.FromSquad ? 1 : 0),
                        Giver = Fit(b.Giver), Shield = b.Shield,
                    });
                Written++;
            }
            m_Heard.RemoveWhere(b => b == null);
            m_Dirty.RemoveWhere(b => b == null);
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
    /// Client: every view of a player, its own included, keeps its buff copy equal to the server's running list (M4.5i; NGO's buff
    /// messages: cast, Squad, removed, one-hit spent, late-joiner sync). A list is worked again only when it changed; a view whose
    /// skills are not known yet (its faction arrives with the look) tries again next frame.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class RunningBuffsPresentSystem : SystemBase
    {
        readonly Dictionary<Entity, int> m_Seen = new Dictionary<Entity, int>();
        readonly List<RunningBuffEntry> m_Running = new List<RunningBuffEntry>();

        /// <summary>Lists worked (tests).</summary>
        public int Reconciled { get; private set; }

        protected override void OnUpdate()
        {
            if (!SystemAPI.TryGetSingleton(out NetworkTime time) || !time.ServerTick.IsValid) return;
            int rate = SystemAPI.TryGetSingleton(out ClientServerTickRate r) && r.SimulationTickRate > 0 ? (int)r.SimulationTickRate : 30;
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry player in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(player.World, World) || player.Body == null || !em.Exists(player.Ghost) || !em.HasBuffer<RunningBuff>(player.Ghost)
                    || !player.Body.TryGetComponent(out CharacterBuffManager copy) || !player.Body.TryGetComponent(out PlayerCombatSkills skills)) continue;
                DynamicBuffer<RunningBuff> list = em.GetBuffer<RunningBuff>(player.Ghost, true);
                int signature = Signature(list);
                if (m_Seen.TryGetValue(player.Ghost, out int seen) && seen == signature) continue;

                m_Running.Clear();
                bool known = true;
                foreach (RunningBuff b in list)
                {
                    string skillId = b.SkillId.ToString();
                    known &= skills.GetSkill(skillId) != null;
                    m_Running.Add(new RunningBuffEntry
                    {
                        SkillId = skillId, Rank = b.Rank, Remaining = RunningBuffs.SecondsLeft(time.ServerTick, b.EndTick, rate),
                        FromSquad = b.FromSquad != 0, Giver = b.Giver.ToString(), Shield = b.Shield,
                    });
                }
                BuffMirror.Reconcile(copy, m_Running, skills.GetSkill);
                Reconciled++;
                if (known) m_Seen[player.Ghost] = signature;
            }
        }

        static int Signature(DynamicBuffer<RunningBuff> list)
        {
            unchecked
            {
                int hash = 17 + list.Length;
                foreach (RunningBuff b in list)
                    hash = hash * 31 + b.SkillId.GetHashCode() * 7 + b.Rank * 13 + (int)b.EndTick * 3 + b.FromSquad + b.Giver.GetHashCode() + b.Shield * 17;
                return hash;
            }
        }
    }
}
