using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Client: each new hit in an attacker's ring is shown once, as NGO's ResolveHit showed it (M4.4h, M4-combat.md section 15): the damage
    /// number (and the miss of an evaded blow) to the ATTACKER's own game only (NGO DamageFeedbackRpc to the owner), the comic impact on
    /// the struck body to everyone who sees the fight (NGO SpawnImpactVfxClientRpc, through the same PlayerMeleeCombat.PreviewImpact call).
    /// A ring first met is taken as seen: nothing old is replayed.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HitEventPresentSystem : SystemBase
    {
        readonly Dictionary<Entity, uint> m_Seen = new Dictionary<Entity, uint>();

        /// <summary>Hits shown here (tests).</summary>
        public int Shown { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry attacker in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(attacker.World, World) || attacker.Body == null || !em.Exists(attacker.Ghost) || !em.HasBuffer<HitEvent>(attacker.Ghost)) continue;
                DynamicBuffer<HitEvent> ring = em.GetBuffer<HitEvent>(attacker.Ghost, true);
                if (!m_Seen.TryGetValue(attacker.Ghost, out uint seen))
                {
                    m_Seen[attacker.Ghost] = HitEventRing.Newest(ring);
                    continue;
                }
                for (int i = 0; i < ring.Length; i++)
                {
                    HitEvent hit = ring[i];
                    if (hit.Seq <= seen) continue;
                    Show(em, attacker, hit);
                    seen = hit.Seq;
                    Shown++;
                }
                m_Seen[attacker.Ghost] = seen;
            }
        }

        void Show(EntityManager em, GhostBodyEntry attacker, in HitEvent hit)
        {
            Transform target = FindBody(em, hit.TargetGhostId);
            bool evaded = (hit.Flags & HitEventRing.Evaded) != 0;
            if (attacker.IsLocal && target != null)
            {
                var result = new DamageResult
                {
                    DamageDealt = hit.DamageMilli / 1000f,
                    ReflectedDamage = hit.ReflectedMilli / 1000f,
                    IsCritical = (hit.Flags & HitEventRing.Critical) != 0,
                    IsEvaded = evaded,
                    IsReflected = (hit.Flags & HitEventRing.Reflected) != 0,
                    ShieldShattered = (hit.Flags & HitEventRing.Shattered) != 0,
                };
                ClientPresentationRegistry.Feedback?.ShowCombatFeedback(target.position, result);
                if (evaded) ClientPresentationRegistry.Feel?.Missed(attacker.Body.transform, target.position, (hit.Flags & HitEventRing.Skill) != 0, true);
                // the attacker's combat line (NGO TellAttacker / CombatMessageRpc), the same words through PlayerMeleeCombat.HitLine
                if (attacker.Body.TryGetComponent(out PlayerMeleeCombat own) && target.TryGetComponent(out IDamageable struck))
                    own.ShowCombatLine(PlayerMeleeCombat.HitLine(hit.Step == 3 ? "Combo Finisher" : $"Strike {hit.Step}", struck.EntityName, result,
                        struck.CurrentHealth, struck.MaxHealth));
            }
            if (!evaded && target != null && attacker.Body.TryGetComponent(out PlayerMeleeCombat melee))
                melee.PreviewImpact(target, hit.Step, (hit.Flags & HitEventRing.Critical) != 0, (hit.Flags & HitEventRing.Reflected) != 0,
                    (hit.Flags & HitEventRing.Shattered) != 0, (hit.Flags & HitEventRing.Elemental) != 0);
        }

        Transform FindBody(EntityManager em, int ghostId)
        {
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                    return e.Body.transform;
            return null;
        }
    }
}
