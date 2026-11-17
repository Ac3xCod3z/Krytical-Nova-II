using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.World;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Crossfire.Entities
{
    /// <summary>Crossfire's state as the server sends it (owner's DAY list item 17): the phase, its times and who is called. ~60 bytes.</summary>
    public struct CrossfireStateRpc : IRpcCommand
    {
        public byte Phase;
        public double PhaseStart, PhaseEnd, WarStart, JoinOpensAt;
        public float CallRepeatSeconds;
        public short MinLevel, BracketSize, BracketCount;
        public ushort KillsHegemony, KillsNull, KillsCovenant;

        public static CrossfireStateRpc Of(in CrossfireState s) => new CrossfireStateRpc
        {
            Phase = (byte)s.Now.Phase, PhaseStart = s.Now.PhaseStart, PhaseEnd = s.Now.PhaseEnd, WarStart = s.Now.WarStart,
            JoinOpensAt = s.JoinOpensAt, CallRepeatSeconds = s.CallRepeatSeconds,
            MinLevel = (short)s.MinLevel, BracketSize = (short)s.BracketSize, BracketCount = (short)s.BracketCount,
            KillsHegemony = (ushort)System.Math.Clamp(s.KillsHegemony, 0, ushort.MaxValue), KillsNull = (ushort)System.Math.Clamp(s.KillsNull, 0, ushort.MaxValue), KillsCovenant = (ushort)System.Math.Clamp(s.KillsCovenant, 0, ushort.MaxValue),
        };

        public CrossfireState State => new CrossfireState
        {
            Now = new CrossfireNow { Phase = (CrossfirePhase)Phase, PhaseStart = PhaseStart, PhaseEnd = PhaseEnd, WarStart = WarStart },
            JoinOpensAt = JoinOpensAt, CallRepeatSeconds = CallRepeatSeconds,
            MinLevel = MinLevel, BracketSize = BracketSize, BracketCount = BracketCount, Known = true,
            KillsHegemony = KillsHegemony, KillsNull = KillsNull, KillsCovenant = KillsCovenant,
        };

        public bool SameAs(in CrossfireStateRpc o) => Phase == o.Phase && PhaseStart.Equals(o.PhaseStart) && PhaseEnd.Equals(o.PhaseEnd)
            && WarStart.Equals(o.WarStart) && JoinOpensAt.Equals(o.JoinOpensAt) && CallRepeatSeconds == o.CallRepeatSeconds
            && MinLevel == o.MinLevel && BracketSize == o.BracketSize && BracketCount == o.BracketCount
            && KillsHegemony == o.KillsHegemony && KillsNull == o.KillsNull && KillsCovenant == o.KillsCovenant;
    }

    /// <summary>Server to one player of a short side: the War Bot vote (owner, 16 Nov 2026), shown on the HUD's accept / decline panel.</summary>
    public struct CrossfireVoteRpc : IRpcCommand
    {
        public FixedString32Bytes Eyebrow;
        public FixedString512Bytes Text;
        public float Seconds;
        public double WarStart;   // which war's vote (an answer to an old one is dropped)
    }

    /// <summary>Server to one real player refused by their full side: a Merc place on a short side (owner, 16 Nov 2026).</summary>
    public struct CrossfireMercOfferRpc : IRpcCommand
    {
        public FixedString32Bytes Eyebrow;
        public FixedString512Bytes Text;
        public float Seconds;
        public double WarStart;
    }

    /// <summary>A player's answer to <see cref="CrossfireMercOfferRpc"/>.</summary>
    public struct CrossfireMercAnswerRpc : IRpcCommand
    {
        public byte Yes;
        public double WarStart;
    }

    /// <summary>A player's answer to <see cref="CrossfireVoteRpc"/>.</summary>
    public struct CrossfireVoteAnswerRpc : IRpcCommand
    {
        public byte Yes;
        public double WarStart;
    }

    /// <summary>A GM's hand on Crossfire's clock (gm.crossfire, the GM window, the control page).</summary>
    public enum CrossfireCommand : byte { StartNow = 1, Skip = 2, End = 3 }

    /// <summary>
    /// Server: Crossfire's clock (owner's DAY list item 17). Works out the phase from <see cref="CrossfireSettings"/> and the world clock's
    /// nightfall (<see cref="CrossfireRules"/>), keeps a GM's start / skip / end, and sends the state to every player in the game when it
    /// changes and to each player as they come in (as <c>WorldStateServerSystem</c> does the world clock). Clients work the timers out
    /// with <see cref="NetClock"/>. Looked at four times a second.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrossfireClockServerSystem : SystemBase
    {
        /// <summary>Where the world clock is read (the scene's <c>WorldEnvironmentNetwork</c>; tests put their own).</summary>
        public static Func<(bool ok, WorldEnvironmentState state)> ClockSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ClockSource = null;

        /// <summary>The settings (tests put their own); the Resources asset otherwise. A change starts the fixed cycle anew.</summary>
        public CrossfireSettings Settings { get => m_Settings; set { m_Settings = value; m_Origin = double.NaN; m_Caller = default; m_Next = 0; } }
        /// <summary>The clock (tests put their own); <see cref="NetClock.Now"/> otherwise. A change starts the fixed cycle anew, so a
        /// cycle is never measured from a time on another clock.</summary>
        public Func<double> Clock { get => m_Clock; set { m_Clock = value; m_Origin = double.NaN; m_Caller = default; m_Next = 0; } }
        CrossfireSettings m_Settings;
        Func<double> m_Clock;
        public float Interval = 0.25f;

        CrossfireOverride m_Override = CrossfireOverride.None;
        double m_Origin = double.NaN, m_Next;
        readonly Dictionary<Entity, CrossfireStateRpc> m_Sent = new Dictionary<Entity, CrossfireStateRpc>();
        readonly List<Entity> m_Gone = new List<Entity>();
        EntityQuery m_InGame;

        /// <summary>The state now (tests, the GM window, the gates).</summary>
        public CrossfireState Current { get; private set; }
        /// <summary>The war runs on a fixed cycle because the nightfall anchor is off or this map has no world clock (GM status).</summary>
        public bool UsingFixedCycle { get; private set; }
        /// <summary>States sent (tests).</summary>
        public int Sends { get; private set; }
        /// <summary>Announcements made (tests): one per moment, however many players it went to.</summary>
        public int Announced { get; private set; }
        /// <summary>Lines told to players (tests): a moment told to three players is three.</summary>
        public int Told { get; private set; }

        /// <summary>
        /// Who hears Crossfire's call: each player on this server (their server body) and their level. Unset: the server's player bodies
        /// (<see cref="GhostBodyRegistry"/>) and their <see cref="PlayerLevelTag"/>. Tests put their own.
        /// </summary>
        public Func<IReadOnlyList<(GameObject body, int level)>> Listeners;
        readonly List<(GameObject body, int level)> m_Listeners = new List<(GameObject, int)>();

        CrossfireCaller m_Caller;

        /// <summary>The server world's clock system, null when this process runs no server.</summary>
        public static CrossfireClockServerSystem Find()
        {
            foreach (Unity.Entities.World w in Unity.Entities.World.All)
                if (w.IsCreated && w.IsServer() && w.GetExistingSystemManaged<CrossfireClockServerSystem>() is { } system) return system;
            return null;
        }

        protected override void OnCreate()
        {
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            CrossfireView.HasServer = true;
        }

        protected override void OnDestroy()
        {
            CrossfireView.HasServer = false;
            CrossfireView.Server = default;
        }

        double NowSeconds => Clock != null ? Clock() : NetClock.Now;

        /// <summary>The clock's time now (the same one its phases are in).</summary>
        public double Now => NowSeconds;

        CrossfireSettings Config => Settings != null ? Settings : CrossfireSettings.Instance;

        int m_KillsH, m_KillsN, m_KillsC;

        /// <summary>The war's kills per side (the entry system counts them); they go out with the state, for the HUD's counter.</summary>
        public void SetScore(int hegemony, int nul, int covenant)
        {
            m_KillsH = hegemony; m_KillsN = nul; m_KillsC = covenant;
        }

        /// <summary>The settings the clock runs on (tests' own, else the Resources asset).</summary>
        public CrossfireSettings ActiveSettings => Config;

        /// <summary>When the wars fall: nightfall on the world clock, or a fixed cycle from this server's start.</summary>
        bool TryCycle(CrossfireSettings s, double now, out CrossfireCycle cycle)
        {
            cycle = default;
            if (s.anchorToNightfall)
            {
                (bool ok, WorldEnvironmentState w) = ClockSource != null ? ClockSource() : (WorldEnvironmentNetwork.TryGetState(out var state), state);
                if (ok && w.DayLengthSeconds > 0f && w.TimeScale > 0f)
                {
                    cycle = CrossfireRules.Nightfalls(w.CycleEpoch, w.TimeOffset01, w.TimeScale, w.DayLengthSeconds, s.nightfallHour);
                    UsingFixedCycle = false;
                    return true;
                }
            }
            UsingFixedCycle = true;   // the anchor is off, or this map has no world clock (a test world): every Cycle Minutes from the server's start
            if (double.IsNaN(m_Origin)) m_Origin = now;
            cycle = CrossfireRules.Fixed(m_Origin, s.Times.CallSeconds, s.cycleMinutes * 60.0);
            return true;
        }

        /// <summary>The state at <paramref name="now"/> (no settings: never Known, no war).</summary>
        CrossfireState Work(double now)
        {
            CrossfireSettings s = Config;
            if (s == null || !TryCycle(s, now, out CrossfireCycle cycle)) return default;
            CrossfireTimes t = s.Times;
            CrossfireNow at = CrossfireRules.At(cycle, t, m_Override, now);
            return new CrossfireState
            {
                Now = at, JoinOpensAt = at.WarStart - t.JoinWindowSeconds, CallRepeatSeconds = s.callRepeatSeconds,
                MinLevel = s.minLevel, BracketSize = s.bracketSize, BracketCount = s.bracketCount, Known = true,
                KillsHegemony = m_KillsH, KillsNull = m_KillsN, KillsCovenant = m_KillsC,
            };
        }

        /// <summary>A GM's command, at once (main thread: QC, the GM window, the control page's command line). False with no settings.</summary>
        public bool Run(CrossfireCommand command)
        {
            CrossfireSettings s = Config;
            double now = NowSeconds;
            if (s == null || !TryCycle(s, now, out CrossfireCycle cycle)) return false;
            CrossfireTimes t = s.Times;
            switch (command)
            {
                case CrossfireCommand.StartNow: CrossfireRules.StartNow(cycle, t, ref m_Override, now); break;
                case CrossfireCommand.Skip: CrossfireRules.Skip(cycle, t, ref m_Override, now); break;
                case CrossfireCommand.End: CrossfireRules.End(cycle, t, ref m_Override, now); break;
                default: return false;
            }
            m_Next = 0;   // the change goes out on the next update
            Current = Work(now);
            CrossfireView.Server = Current;
            return true;
        }

        IReadOnlyList<(GameObject body, int level)> Hearers()
        {
            if (Listeners != null) return Listeners();
            m_Listeners.Clear();
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<PlayerLevelTag>(e.Ghost))
                    m_Listeners.Add((e.Body, em.GetComponentData<PlayerLevelTag>(e.Ghost).Level));
            return m_Listeners;
        }

        /// <summary>
        /// The call, the countdown and the gates-closed line, through the game's announcement system, to each player in the words of THEIR
        /// OWN bracket (owner, 16 Nov 2026: a level 112 is told bracket X, a level 10 bracket I; <see cref="CrossfireCaller.Text"/>), one
        /// player at a time (<see cref="ServerChat.AnnounceTo"/>). The moment is the server's one clock, so every player's minutes agree.
        /// </summary>
        void Announce(double now)
        {
            CrossfireSettings s = Config;
            CrossfireCaller.Line line = m_Caller.NextLine(Current, now, Current.Now.WarStart + (s != null ? s.musterSeconds : 0), s);
            if (line.Kind == CrossfireCaller.LineKind.None) return;
            Announced++;
            Action<GameObject, AnnouncementKind, string, string, byte> tell = ServerChat.AnnounceTo;
            AnnouncementKind kind = line.IsCountdown ? AnnouncementKind.CrossfireCount : AnnouncementKind.Crossfire;
            IReadOnlyList<(GameObject body, int level)> hearers = Hearers();
            for (int i = 0; i < hearers.Count; i++)
            {
                string text = CrossfireCaller.Text(line, s, s.BracketOf(hearers[i].level), Current);
                if (string.IsNullOrEmpty(text) || tell == null) continue;
                tell(hearers[i].body, kind, text, null, 0);
                Told++;
            }
        }

        protected override void OnUpdate()
        {
            double tick = SystemAPI.Time.ElapsedTime;
            if (tick < m_Next) return;
            m_Next = tick + Interval;
            Current = Work(NowSeconds);
            CrossfireView.Server = Current;
            if (!Current.Known) return;
            Announce(NowSeconds);
            CrossfireStateRpc message = CrossfireStateRpc.Of(Current);
            EntityManager em = EntityManager;
            m_Gone.Clear();
            foreach (Entity c in m_Sent.Keys) if (!em.Exists(c)) m_Gone.Add(c);
            foreach (Entity c in m_Gone) m_Sent.Remove(c);
            using NativeArray<Entity> connections = m_InGame.ToEntityArray(Allocator.Temp);
            foreach (Entity connection in connections)
            {
                if (m_Sent.TryGetValue(connection, out CrossfireStateRpc had) && had.SameAs(message)) continue;
                m_Sent[connection] = message;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, message);
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                Sends++;
            }
        }
    }

    /// <summary>Client: keeps the server's Crossfire state for the HUD and the gates (<see cref="CrossfireView.Client"/>).</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrossfireClientSystem : SystemBase
    {
        EntityQuery m_States;

        /// <summary>States taken, and the last one (tests).</summary>
        public int Applied { get; private set; }
        public CrossfireState Last { get; private set; }

        protected override void OnCreate() =>
            m_States = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<CrossfireStateRpc>());

        protected override void OnDestroy() => CrossfireView.Client = default;

        protected override void OnUpdate()
        {
            if (m_States.IsEmpty) return;
            using NativeArray<CrossfireStateRpc> states = m_States.ToComponentDataArray<CrossfireStateRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_States);
            foreach (CrossfireStateRpc rpc in states)
            {
                Last = rpc.State;
                Applied++;
                CrossfireView.Client = Last;
            }
        }
    }
}
