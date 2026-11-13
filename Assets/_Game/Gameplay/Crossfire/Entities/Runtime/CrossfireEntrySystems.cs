using System.Collections.Generic;
using KryticalNova.Player.Entities;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Crossfire.Entities
{
    /// <summary>
    /// Server: who joined the war and where they came from, and sending them back when it is over (owner's DAY list item 17). Reads the
    /// clock's state (<see cref="CrossfireClockServerSystem.Current"/>) and the map's <see cref="CrossfireArea"/> boxes; a player in a
    /// Muster Yard while the yards take players in has joined (<see cref="CrossfireEntries{T}"/>). When the clock goes back to the Gap
    /// (the End ran out, or a GM called the war off), everyone who joined and is still in the war area is put back where they came from
    /// (<see cref="PlayerEntitiesSetup.Teleport"/>). A map with no areas does nothing.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(CrossfireClockServerSystem))]
    public partial class CrossfireEntryServerSystem : SystemBase
    {
        readonly CrossfireEntries<Entity> m_Entries = new CrossfireEntries<Entity>();
        CrossfireClockServerSystem m_Clock;
        CrossfirePhase m_LastPhase = CrossfirePhase.Gap;

        /// <summary>Players who joined the war under way; players sent back so far (tests).</summary>
        public int Joined => m_Entries.Count;
        public int SentBack { get; private set; }

        static Vector3 Meters(in PlayerPose p) => new Vector3(p.XMm, p.YMm, p.ZMm) * 0.001f;

        protected override void OnUpdate()
        {
            m_Clock ??= World.GetExistingSystemManaged<CrossfireClockServerSystem>();
            if (m_Clock == null) return;
            CrossfireState state = m_Clock.Current;
            if (!state.Known) return;
            EntityManager em = EntityManager;
            if (CrossfireArea.All.Count > 0)
            {
                bool joinOpen = state.JoinOpen(m_Clock.Now);
                foreach (var (pose, entity) in SystemAPI.Query<RefRO<PlayerPose>>().WithAll<PlayerGhostTag>().WithEntityAccess())
                {
                    Vector3 at = Meters(pose.ValueRO);
                    var spot = new CrossfireSpot { Mm = new Vector3Int(pose.ValueRO.XMm, pose.ValueRO.YMm, pose.ValueRO.ZMm), YawCdeg = pose.ValueRO.YawCdeg };
                    m_Entries.Observe(entity, spot, CrossfireArea.AnyContains(at, false), CrossfireArea.AnyContains(at, true), joinOpen);
                }
                m_Entries.ForgetWhere(e => !em.Exists(e) || !em.HasComponent<PlayerPose>(e));
            }
            // the war is over: back to the Gap (the End ran out, or a GM called it off), or out of the End into another war a GM started
            CrossfirePhase phase = state.Now.Phase;
            bool over = phase != m_LastPhase && m_LastPhase != CrossfirePhase.Gap
                && (phase == CrossfirePhase.Gap || m_LastPhase == CrossfirePhase.End);
            m_LastPhase = phase;
            if (!over) return;
            List<(Entity who, CrossfireSpot to)> back = m_Entries.TakeSendBack(e =>
                em.Exists(e) && em.HasComponent<PlayerPose>(e) && CrossfireArea.AnyContains(Meters(em.GetComponentData<PlayerPose>(e)), false));
            foreach ((Entity who, CrossfireSpot to) in back)
            {
                if (!em.HasComponent<TeleportRequest>(who)) continue;
                PlayerEntitiesSetup.Teleport(em, who, new int3(to.Mm.x, to.Mm.y, to.Mm.z), to.YawCdeg);
                SentBack++;
            }
        }
    }
}
