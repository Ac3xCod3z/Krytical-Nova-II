using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Server: every hit a player's server body deals, a basic strike or a skill (both go through NGO's ResolveHit), goes on the
    /// attacker's HitEvent ring for the clients (M4.4h, M4.5b): the one place the ring is written. Each body is listened to once.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HitRecordServerSystem : SystemBase
    {
        readonly HashSet<PlayerMeleeCombat> m_Heard = new HashSet<PlayerMeleeCombat>();

        /// <summary>Hits written (tests).</summary>
        public int Recorded { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out PlayerMeleeCombat melee)
                    || !m_Heard.Add(melee)) continue;
                Entity ghost = entry.Ghost;
                melee.HitDealt += (target, result, step, fromSkill, label) => Record(ghost, melee, target, result, step, fromSkill, label);
            }
            m_Heard.RemoveWhere(m => m == null);
        }

        void Record(Entity attackerGhost, PlayerMeleeCombat attacker, IDamageable target, DamageResult result, int step, bool fromSkill, string label)
        {
            if (World == null || !World.IsCreated) return;
            EntityManager em = World.EntityManager;
            if (!em.Exists(attackerGhost) || !em.HasBuffer<HitEvent>(attackerGhost) || !em.HasComponent<HitEventSeq>(attackerGhost)) return;
            int targetGhost = GhostIdOf(em, target != null ? target.Transform : null);
            if (targetGhost == 0) return;
            attacker.TryGetComponent(out CharacterCombatStats stats);
            HitEventSeq seq = em.GetComponentData<HitEventSeq>(attackerGhost);
            HitEventRing.Append(em.GetBuffer<HitEvent>(attackerGhost), ref seq, new HitEvent
            {
                TargetGhostId = targetGhost,
                DamageMilli = Mathf.RoundToInt(result.DamageDealt * 1000f),
                ReflectedMilli = Mathf.RoundToInt(result.ReflectedDamage * 1000f),
                Flags = (byte)(MeleeStrike.FlagsOf(result, stats) | (fromSkill ? HitEventRing.Skill : 0)),
                Step = (byte)math.clamp(step, 0, 3),
                Label = fromSkill && !string.IsNullOrEmpty(label) ? Fit(label) : default,
            });
            em.SetComponentData(attackerGhost, seq);
            Recorded++;
        }

        // a label longer than the field keeps its start (UTF-8 bytes)
        static Unity.Collections.FixedString64Bytes Fit(string text)
        {
            var fixedText = new Unity.Collections.FixedString64Bytes();
            foreach (char c in text) if (fixedText.Append(c) != Unity.Collections.FormatError.None) break;
            return fixedText;
        }

        int GhostIdOf(EntityManager em, Transform body)
        {
            if (body == null) return 0;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && (body == e.Body.transform || body.IsChildOf(e.Body.transform))
                    && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost))
                    return em.GetComponentData<GhostInstance>(e.Ghost).ghostId;
            return 0;
        }
    }
}
