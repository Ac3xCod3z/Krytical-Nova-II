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
        readonly Dictionary<Entity, Queue<HitEvent>> m_Waiting = new Dictionary<Entity, Queue<HitEvent>>();

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
            // every hit is applied when it lands; the ring shows a few a tick, the rest wait their turn (none is dropped)
            List<Entity> gone = null;
            foreach (var pair in m_Waiting)
            {
                if (!em.Exists(pair.Key) || !em.HasBuffer<HitEvent>(pair.Key) || !em.HasComponent<HitEventSeq>(pair.Key)) { (gone ??= new List<Entity>()).Add(pair.Key); continue; }
                if (pair.Value.Count == 0) continue;
                HitEventSeq seq = em.GetComponentData<HitEventSeq>(pair.Key);
                HitEventRing.Drain(pair.Value, em.GetBuffer<HitEvent>(pair.Key), ref seq);
                em.SetComponentData(pair.Key, seq);
            }
            if (gone != null) foreach (Entity e in gone) m_Waiting.Remove(e);
        }

        void Record(Entity attackerGhost, PlayerMeleeCombat attacker, IDamageable target, DamageResult result, int step, bool fromSkill, string label)
        {
            attacker.TryGetComponent(out CharacterCombatStats stats);
            RecordFor(attackerGhost, stats, target, result, step, fromSkill, label);
        }

        /// <summary>
        /// Server: a hit dealt in a player's name by something other than their own strike (day list item 19, phase 3: their Mount
        /// Companion's DPS strike) goes on that player's ring like their own, so every view shows its impact and number, labelled.
        /// </summary>
        public bool RecordFor(Entity attackerGhost, CharacterCombatStats stats, IDamageable target, DamageResult result, int step, bool fromSkill, string label)
        {
            if (World == null || !World.IsCreated) return false;
            EntityManager em = World.EntityManager;
            if (!em.Exists(attackerGhost) || !em.HasBuffer<HitEvent>(attackerGhost) || !em.HasComponent<HitEventSeq>(attackerGhost)) return false;
            int targetGhost = GhostIdOf(em, target != null ? target.Transform : null);
            if (targetGhost == 0) return false;
            if (!m_Waiting.TryGetValue(attackerGhost, out Queue<HitEvent> waiting)) m_Waiting[attackerGhost] = waiting = new Queue<HitEvent>();
            waiting.Enqueue(new HitEvent
            {
                TargetGhostId = targetGhost,
                DamageMilli = Mathf.RoundToInt(result.DamageDealt * 1000f),
                ReflectedMilli = Mathf.RoundToInt(result.ReflectedDamage * 1000f),
                Flags = (byte)(MeleeStrike.FlagsOf(result, stats) | (fromSkill ? HitEventRing.Skill : 0)),
                Step = (byte)math.clamp(step, 0, 3),
                Label = fromSkill && !string.IsNullOrEmpty(label) ? HitEventRing.FitLabel(label) : default,
            });
            Recorded++;
            return true;
        }

        int GhostIdOf(EntityManager em, Transform body)
        {
            if (body == null) return 0;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && (body == e.Body.transform || body.IsChildOf(e.Body.transform))
                    && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost))
                    return em.GetComponentData<GhostInstance>(e.Ghost).ghostId;
            return CombatTargetRegistry.GhostIdOf(body);   // the Strike Dummy (M4.2b): its impact shows on every view
        }
    }
}
