using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.World.Portals;
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
    /// It also answers the Transit Row port while a war is called (owner, 16 Nov 2026; <see cref="CrossfirePort"/>): a called player of a
    /// bracket with a zone is put on their own faction's Rally Point, on this server; any other use of the portal is the portal's as before.
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
        /// <summary>Players the Transit Row port put on their Rally Point (tests).</summary>
        public int Ported { get; private set; }
        /// <summary>Players last seen outside the war area; whether the yards took players in at the last look (tests, diagnostics).</summary>
        public int SeenOutside => m_Entries.OutsideCount;
        public bool LastJoinOpen { get; private set; }
        public int Looks { get; private set; }
        /// <summary>What the port answered last (tests, diagnostics).</summary>
        public CrossfirePortAnswer LastPortAnswer { get; private set; }

        PortHandler m_Port;

        /// <summary>Stands in front of the portals' handler: Crossfire's port first, everything else as before (<see cref="Next"/>).</summary>
        sealed class PortHandler : IZoneTransferHandler
        {
            public CrossfireEntryServerSystem System;
            public IZoneTransferHandler Next;

            public ZoneTransferResult TryTransfer(ZonePortal portal, GameObject player)
            {
                if (System != null && portal != null && System.TryPort(portal.PortalId, player, out ZoneTransferResult result)) return result;
                return Next != null ? Next.TryTransfer(portal, player) : new ZoneTransferResult(false, portal != null ? portal.SealedMessage : null);
            }
        }

        protected override void OnCreate()
        {
            m_Port = new PortHandler { System = this, Next = ZonePortal.TransferHandler };
            ZonePortal.TransferHandler = m_Port;
            CreateWar();
        }

        protected override void OnDestroy()
        {
            DestroyWar();
            if (ReferenceEquals(ZonePortal.TransferHandler, m_Port)) ZonePortal.TransferHandler = m_Port.Next;
            if (m_Port != null) m_Port.System = null;   // a handler another world put on top of this one now passes straight through
        }

        /// <summary>
        /// The Transit Row port for a server body: true when Crossfire answered (moved the player, or told them why not), false when the
        /// portal should do what it always does. The player's level is their <see cref="PlayerLevelTag"/>, their faction their body's.
        /// </summary>
        public bool TryPort(string portalId, GameObject body, out ZoneTransferResult result)
        {
            result = default;
            if (body == null) return false;
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, World) || !ReferenceEquals(e.Body, body) || !em.Exists(e.Ghost) || !em.HasComponent<PlayerLevelTag>(e.Ghost)) continue;
                return Port(e.Ghost, em.GetComponentData<PlayerLevelTag>(e.Ghost).Level, HomeOf(body), portalId, out result);
            }
            return false;
        }

        /// <summary>The Transit Row port for a player ghost of <paramref name="level"/> and <paramref name="faction"/> (see <see cref="TryPort"/>).</summary>
        public bool Port(Entity ghost, int level, FactionId faction, string portalId, out ZoneTransferResult result)
        {
            result = default;
            m_Clock ??= World.GetExistingSystemManaged<CrossfireClockServerSystem>();
            if (m_Clock == null) return false;
            CrossfireSettings settings = m_Clock.ActiveSettings;
            CrossfirePortAnswer answer = CrossfirePort.Decide(settings, m_Clock.Current, portalId, level, faction, out CrossfireArea yard);
            LastPortAnswer = answer;
            if (answer == CrossfirePortAnswer.NotThisPort || answer == CrossfirePortAnswer.NotCalled) return false;
            string message = CrossfirePort.Message(settings, answer, level);
            EntityManager em = EntityManager;
            if (answer == CrossfirePortAnswer.Land && !m_Sides.TryGetSide(ghost, out _) && SideFull(faction))
            {
                Refused++;
                bool offered = TryOfferMerc(ghost, faction, settings);
                result = new ZoneTransferResult(false, offered ? null : Fill(settings.sideFullText, settings, m_Sides.Real(faction)));
                FlushTells();
                return true;
            }
            if (answer != CrossfirePortAnswer.Land || !em.Exists(ghost) || !em.HasComponent<TeleportRequest>(ghost))
            {
                result = new ZoneTransferResult(false, answer == CrossfirePortAnswer.Land ? settings.portNoRallyText : message);
                return true;
            }
            yard.Landing(out Vector3 at, out float yaw);
            var mm = new int3((int)math.round(at.x * 1000f), (int)math.round(at.y * 1000f), (int)math.round(at.z * 1000f));
            int yawCdeg = (int)math.round(Mathf.Repeat(yaw, 360f) * 100f) % 36000;
            PlayerEntitiesSetup.Teleport(em, ghost, mm, yawCdeg);
            m_Arriving.Add(ghost);
            Ported++;
            result = new ZoneTransferResult(true, message);
            return true;
        }

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
                LastJoinOpen = joinOpen;
                Looks++;
                foreach (var (pose, entity) in SystemAPI.Query<RefRO<PlayerPose>>().WithAll<PlayerGhostTag>().WithEntityAccess())
                {
                    Vector3 at = Meters(pose.ValueRO);
                    var spot = new CrossfireSpot { Mm = new Vector3Int(pose.ValueRO.XMm, pose.ValueRO.YMm, pose.ValueRO.ZMm), YawCdeg = pose.ValueRO.YawCdeg };
                    bool inArea = CrossfireArea.AnyContains(at, false), inYard = inArea && CrossfireArea.AnyContains(at, true);
                    // a newcomer to a Rally Point while it takes players in: their side takes them, or (full) puts them back
                    if (IsBot(entity)) continue;   // War Bots are never admitted, paid or sent back as real players (review 1, item 4)
                    if (!inArea && m_Sides.TryGetSide(entity, out _) && !m_Arriving.Contains(entity)) m_Leaving.Add(entity);   // left the war (item 2)
                    if (inArea) m_Arriving.Remove(entity);
                    if (inYard && joinOpen && !m_Entries.TryEntry(entity, out _) && m_Entries.TryOutside(entity, out CrossfireSpot from) && !Admit(entity, from))
                        continue;
                    m_Entries.Observe(entity, spot, inArea, inYard, joinOpen);
                }
                foreach (Entity who in m_Leaving) LeaveWar(who, true);
                m_Leaving.Clear();
                SendRefusedBack();
                m_Entries.ForgetWhere(e => !em.Exists(e) || !em.HasComponent<PlayerPose>(e));
            }
            // the war is over: back to the Gap (the End ran out, or a GM called it off), or out of the End into another war a GM started
            RunWar(state, m_Clock.Now);
            CrossfirePhase phase = state.Now.Phase;
            bool over = phase != m_LastPhase && m_LastPhase != CrossfirePhase.Gap
                && (phase == CrossfirePhase.Gap || m_LastPhase == CrossfirePhase.End);
            m_LastPhase = phase;
            if (!over) return;
            EndWar();
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
