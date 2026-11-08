using System.Collections.Generic;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// Client: each strike a ghost's combo starts is played once on its body (M4.4k), as NGO's PlayComboStrikeClientRpc played it on every
    /// client: the owner's from its own predicted combo (no round trip: NGO showed strike 1 on the click), everyone else's from the
    /// server's. A strike is known by its step and start tick, so a re-prediction of the same strike plays nothing new.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class StrikePresentSystem : SystemBase
    {
        struct Last
        {
            public byte Step;
            public NetworkTick Start;
        }

        readonly Dictionary<Entity, Last> m_Last = new Dictionary<Entity, Last>();

        /// <summary>Strikes played here (tests).</summary>
        public int Shown { get; private set; }

        /// <summary>A "[KnStrike]" line with the frame for each strike drawn (the Play DOTS here probe: which VFX is whose, 8 Nov 2026).</summary>
        public static bool Trace;

        /// <summary>True when (<paramref name="step"/>, <paramref name="start"/>) is a strike not yet played (pure).</summary>
        public static bool IsNewStrike(byte lastStep, NetworkTick lastStart, byte step, NetworkTick start) =>
            step > 0 && start.IsValid && (step != lastStep || !lastStart.IsValid || start != lastStart);

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<CombatState>(entry.Ghost)) continue;
                CombatState s = em.GetComponentData<CombatState>(entry.Ghost);
                m_Last.TryGetValue(entry.Ghost, out Last last);
                if (!IsNewStrike(last.Step, last.Start, s.Step, s.StepStartTick))
                {
                    if (s.Step == 0) m_Last[entry.Ghost] = default;
                    continue;
                }
                m_Last[entry.Ghost] = new Last { Step = s.Step, Start = s.StepStartTick };
                if (entry.Body.TryGetComponent(out PlayerMeleeCombat melee)) melee.ShowStrikeFromGhost(s.Step);
                Shown++;
                if (Trace) UnityEngine.Debug.Log($"[KnStrike] frame {UnityEngine.Time.frameCount}: strike {s.Step} drawn on '{entry.Body.name}' (start tick {s.StepStartTick.ToFixedString()})");
            }
        }
    }
}
