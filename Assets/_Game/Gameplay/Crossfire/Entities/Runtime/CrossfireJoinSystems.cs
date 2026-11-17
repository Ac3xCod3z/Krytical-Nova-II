using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Player.Entities;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Crossfire.Entities
{
    /// <summary>How a player came into a war (owner's war flow, 16 Nov 2026: "the server tracks how and from where each player joins").</summary>
    public enum CrossfireJoinWay : byte
    {
        /// <summary>Walked into a Rally Point while it took players.</summary>
        Walked = 0,
        /// <summary>Said yes to the join prompt every bracket player gets when the join window opens.</summary>
        Prompt = 1,
        /// <summary>Asked the Marshal (JOIN CROSSFIRE).</summary>
        Marshal = 2,
        /// <summary>Went through the Transit Row port.</summary>
        Port = 3,
        /// <summary>Took a Merc place on a short side.</summary>
        Merc = 4,
    }

    /// <summary>Server to every player of a bracket with a zone when the join window opens: join this war? (owner's war flow).</summary>
    public struct CrossfireJoinPromptRpc : IRpcCommand
    {
        public FixedString32Bytes Eyebrow;
        public FixedString512Bytes Text;
        public float Seconds;
        public double WarStart;
    }

    /// <summary>A player to the server: yes or no to the join prompt, or the Marshal's JOIN CROSSFIRE (<see cref="Way"/>).</summary>
    public struct CrossfireJoinRpc : IRpcCommand
    {
        /// <summary><see cref="CrossfireJoinRpcKind"/>.</summary>
        public byte Kind;
        public double WarStart;
    }

    public static class CrossfireJoinRpcKind
    {
        public const byte PromptYes = 1, PromptNo = 2, Marshal = 3;
    }

    /// <summary>Server to each real player when the war ends: the results screen (owner: 30 s, then back where they joined from).</summary>
    public struct CrossfireResultsRpc : IRpcCommand
    {
        /// <summary><see cref="CrossfireResultKind"/>.</summary>
        public byte Result;
        public byte Side;
        public int KillsHegemony, KillsNull, KillsCovenant;
        public FixedString32Bytes Title;
        public FixedString128Bytes Pay;
        public float Seconds;
        public double WarStart;
    }

    /// <summary>
    /// Crossfire's way in and out (owner's war flow and his answers, 16 Nov 2026), on the entry system:
    /// <list type="bullet">
    /// <item>When the join window opens, every player of a bracket with a zone gets a join prompt wherever they are; yes puts them straight
    /// on their own Rally Point (gates shut). No: they can still join at the Marshal while the war takes players, or (side full) be offered a
    /// Merc place. A player who got in is never a Merc.</item>
    /// <item>A running War still takes players until its last <see cref="CrossfireSettings.lateJoinCutoffMinutes"/>; a late joiner earns the
    /// share of the pay of the War they were in (<see cref="CrossfireRules.PayShare"/>).</item>
    /// <item>At the War's end every real player gets the results screen for the End; then everyone goes back to exactly where they joined from.</item>
    /// </list>
    /// Moving the player is in one place each way (<see cref="SendToRally"/>, the send-back): on this same server today, a server hop later.
    /// </summary>
    public partial class CrossfireEntryServerSystem
    {
        readonly Dictionary<Entity, CrossfireJoinWay> m_JoinWay = new Dictionary<Entity, CrossfireJoinWay>();
        readonly Dictionary<Entity, double> m_JoinedAt = new Dictionary<Entity, double>();
        readonly HashSet<Entity> m_Prompted = new HashSet<Entity>();
        readonly HashSet<Entity> m_PromptDeclined = new HashSet<Entity>();
        double m_PromptFor = double.NaN, m_FilledFor = double.NaN, m_RallyPromptFor = double.NaN;
        EntityQuery m_JoinRequests, m_PlayerGhosts;

        /// <summary>Join prompts sent; players a prompt, the Marshal or the port sent to their Rally Point; late joiners (tests).</summary>
        public int PromptsSent { get; private set; }
        public int SentToRally { get; private set; }
        public int LateJoins { get; private set; }
        /// <summary>Results screens sent at the last war's end (tests).</summary>
        public int ResultsSent { get; private set; }

        /// <summary>How a player came into this war; false when they are not in it (tests, diagnostics).</summary>
        public bool TryJoinWay(Entity who, out CrossfireJoinWay way) => m_JoinWay.TryGetValue(who, out way);

        /// <summary>The share of this war's pay <paramref name="who"/> earns (1: in before the gates opened).</summary>
        public float ShareOf(Entity who)
        {
            CrossfireSettings s = Settings;
            if (s == null || !s.latePayByShare || m_Clock == null || !m_JoinedAt.TryGetValue(who, out double at)) return 1f;
            double gatesOpen = m_Clock.Current.Now.WarStart + s.musterSeconds;
            return CrossfireRules.PayShare(at, gatesOpen, gatesOpen + s.warMinutes * 60.0);
        }

        double LateCutoffSeconds => Settings != null ? Settings.lateJoinCutoffMinutes * 60.0 : 0.0;

        /// <summary>The war takes players now: the join window, the Rally, or a running War until its late-join cutoff.</summary>
        bool TakesPlayers => m_Clock != null && m_Clock.Current.TakesPlayers(m_Clock.Now, LateCutoffSeconds);

        void CreateJoins()
        {
            m_JoinRequests = GetEntityQuery(ComponentType.ReadOnly<CrossfireJoinRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            m_PlayerGhosts = GetEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<GhostOwner>());
        }

        /// <summary>
        /// A real player asks to join (prompt yes, the Marshal, a test): put on their own faction's Rally Point while the war takes
        /// players; their side full: refused, and offered a Merc place if one is open. True when they were sent.
        /// </summary>
        public bool AskToJoin(Entity who, CrossfireJoinWay way)
        {
            m_Clock ??= World.GetExistingSystemManaged<CrossfireClockServerSystem>();
            CrossfireSettings s = Settings;
            if (s == null || IsBot(who)) return false;
            if (m_Sides.TryGetSide(who, out _)) return true;   // already in this war
            if (!TakesPlayers) { Tell(who, s.joinClosedText); FlushTells(); return false; }
            int level = LevelOf != null ? LevelOf(who) : LevelOfReal(who);
            int bracket = s.BracketOf(level);
            if (bracket <= 0 || !s.HasZone(bracket)) { Tell(who, CrossfirePort.Message(s, CrossfirePortAnswer.NoZone, level)); FlushTells(); return false; }
            FactionId faction = SideOfReal(who);
            if (!CrossfireArea.TryRallyPoint(faction, out CrossfireArea yard)) { Tell(who, s.portNoRallyText); FlushTells(); return false; }
            if (SideFull(faction))
            {
                Refused++;
                if (!TryOfferMerc(who, faction, s)) Tell(who, Fill(s.sideFullText, s, m_Sides.Real(faction)));
                FlushTells();
                return false;
            }
            if (!SendToRally(who, yard)) return false;
            m_JoinWay[who] = way;
            if (!string.IsNullOrEmpty(s.portArrivedText)) Tell(who, s.portArrivedText);
            FlushTells();
            return true;
        }

        /// <summary>
        /// Puts a player on a Rally Point's landing: the one place a player is moved INTO the war (a teleport on this server; the Crucible's own
        /// server will make this a server hop with a ticket). They count as arriving until the next look sees them in the yard.
        /// </summary>
        bool SendToRally(Entity who, CrossfireArea yard)
        {
            EntityManager em = EntityManager;
            if (yard == null || !em.Exists(who) || !em.HasComponent<TeleportRequest>(who)) return false;
            yard.Landing(out Vector3 at, out float yaw);
            PlayerEntitiesSetup.Teleport(em, who, new Unity.Mathematics.int3((int)Math.Round(at.x * 1000f), (int)Math.Round(at.y * 1000f), (int)Math.Round(at.z * 1000f)),
                (int)Math.Round(Mathf.Repeat(yaw, 360f) * 100f) % 36000);
            m_Arriving.Add(who);
            SentToRally++;
            return true;
        }

        /// <summary>A player's place in the war began now: their join time (for a late joiner's share), and the late-join line.</summary>
        void MarkJoined(Entity who, CrossfireJoinWay fallbackWay)
        {
            if (!m_JoinWay.ContainsKey(who)) m_JoinWay[who] = fallbackWay;
            if (m_JoinedAt.ContainsKey(who) || m_Clock == null) return;
            m_JoinedAt[who] = m_Clock.Now;
            CrossfireSettings s = Settings;
            if (s == null || m_Clock.Current.Now.Phase != CrossfirePhase.War) return;
            LateJoins++;
            if (s.latePayByShare && !string.IsNullOrEmpty(s.lateJoinText))
                Tell(who, s.lateJoinText.Replace("{percent}", Mathf.RoundToInt(ShareOf(who) * 100f).ToString()));
        }

        /// <summary>One look a tick: the join prompt for every bracket player when the window opens, and the join answers.</summary>
        void RunJoins(in CrossfireState state, double now)
        {
            ReadJoinRequests();
            CrossfireSettings s = Settings;
            if (s == null || !state.Known) return;
            double war = state.Now.WarStart;
            bool window = state.Now.Phase == CrossfirePhase.Call && now >= state.JoinOpensAt;
            // A war that skipped its join window (a GM's gm.crossfire start goes straight to the Rally) is still offered to the bracket:
            // the prompt goes out in the Rally instead, until the gates open (crossfire-c's call for the Master, 17 Nov 2026).
            bool rally = state.Now.Phase == CrossfirePhase.Muster && (m_RallyPromptFor.Equals(war) || !m_PromptFor.Equals(war));
            if (!window && !rally) return;
            if (rally) m_RallyPromptFor = war;
            if (!m_PromptFor.Equals(war))
            {
                m_PromptFor = war;
                m_Prompted.Clear();
                m_PromptDeclined.Clear();
            }
            double closes = rally ? war + s.musterSeconds : war;   // the settle (or, for a GM's war, the gates)
            // every player of a bracket with a zone, wherever they are and whatever they do, once a war (a player who logs in during the window too)
            EntityManager em = EntityManager;
            using NativeArray<Entity> ghosts = m_PlayerGhosts.ToEntityArray(Allocator.Temp);
            foreach (Entity g in ghosts)
            {
                if (m_Prompted.Contains(g) || IsBot(g) || m_Sides.TryGetSide(g, out _)) continue;
                int level = LevelOf != null ? LevelOf(g) : LevelOfReal(g);
                int bracket = s.BracketOf(level);
                if (bracket <= 0 || !s.HasZone(bracket)) continue;
                m_Prompted.Add(g);
                var rpc = new CrossfireJoinPromptRpc
                {
                    Eyebrow = s.joinPromptEyebrow ?? string.Empty,
                    Text = CrossfireCaller.Fill(s.joinPromptText, s, bracket),
                    Seconds = (float)Math.Max(1.0, closes - now),   // until the settle (a GM's war: the gates): later they join at the Marshal
                    WarStart = war,
                };
                SendTo(g, rpc);
                PromptsSent++;
            }
        }

        /// <summary>A player's answer to the join prompt (or a test): yes joins, no is remembered for this war.</summary>
        public bool AnswerPrompt(Entity who, bool yes)
        {
            CrossfireSettings s = Settings;
            if (yes) return AskToJoin(who, CrossfireJoinWay.Prompt);
            if (m_PromptDeclined.Add(who) && s != null) { Tell(who, s.joinDeclinedText); FlushTells(); }
            return false;
        }

        void ReadJoinRequests()
        {
            if (m_JoinRequests.IsEmpty || m_Clock == null) return;
            EntityManager em = EntityManager;
            using NativeArray<Entity> requests = m_JoinRequests.ToEntityArray(Allocator.Temp);
            var asked = new List<(Entity who, byte kind, double war)>(requests.Length);
            foreach (Entity r in requests)
            {
                CrossfireJoinRpc rpc = em.GetComponentData<CrossfireJoinRpc>(r);
                Entity from = em.GetComponentData<ReceiveRpcCommandRequest>(r).SourceConnection;
                em.DestroyEntity(r);
                if (!em.Exists(from) || !em.HasComponent<NetworkId>(from)) continue;
                Entity who = GhostOfConnection(em.GetComponentData<NetworkId>(from).Value);
                if (who != Entity.Null) asked.Add((who, rpc.Kind, rpc.WarStart));
            }
            double war = m_Clock.Current.Now.WarStart;
            foreach ((Entity who, byte kind, double rpcWar) in asked)
            {
                if (kind == CrossfireJoinRpcKind.Marshal) AskToJoin(who, CrossfireJoinWay.Marshal);
                else if (rpcWar.Equals(war) && m_Prompted.Contains(who)) AnswerPrompt(who, kind == CrossfireJoinRpcKind.PromptYes);   // an answer to an old war's prompt is dropped
            }
        }

        /// <summary>The player ghost of a connection (the sender of an RPC: never what the RPC says about itself).</summary>
        Entity GhostOfConnection(int networkId)
        {
            EntityManager em = EntityManager;
            using NativeArray<Entity> ghosts = m_PlayerGhosts.ToEntityArray(Allocator.Temp);
            foreach (Entity g in ghosts)
                if (em.GetComponentData<GhostOwner>(g).NetworkId == networkId) return g;
            return Entity.Null;
        }

        /// <summary>A side's War Bot vote offered to a player who came in while the vote is open (owner: they vote in the yard).</summary>
        void OfferVoteTo(Entity who, FactionId side, CrossfireSettings s)
        {
            if (!m_VoteOpen || s == null || m_Voted.Contains(who)) return;
            if (!m_Votes.ContainsKey(side))
            {
                if (!CrossfireVote.Offered(m_Sides.Real(side), BotsOn(side), s.capPerFaction, Bots != null)) return;
                m_Votes[side] = (0, 0);
            }
            var rpc = new CrossfireVoteRpc
            {
                Eyebrow = s.voteEyebrow ?? string.Empty, Text = Fill(s.voteText, s, m_Sides.Real(side)),
                Seconds = (float)Math.Max(1.0, m_VoteCloses - m_Clock.Now), WarStart = m_VoteFor,
            };
            SendTo(who, rpc);
            VotesSent++;
        }

        /// <summary>
        /// The settle (the Rally): every side with no real player in a war that has one is filled with War Bots, once. A war whose first
        /// real player comes in during the Rally (a GM's war that skips the join window, a Marshal join) is settled then, not skipped:
        /// the settle counts as done only once there is a real player to fight for and War Bots to fill with (crucible-unity-2's test war,
        /// 17 Nov 2026: the empty sides got no bots).
        /// </summary>
        void SettleSides(in CrossfireState state, CrossfireSettings s)
        {
            double war = state.Now.WarStart;
            if (state.Now.Phase != CrossfirePhase.Muster || m_FilledFor.Equals(war)) return;
            if (m_Sides.Count == 0 || Bots == null) return;
            m_FilledFor = war;
            foreach (FactionId side in CrossfireSides<Entity>.Factions)
                if (m_Sides.Real(side) == 0) AutoFill(side, s);
        }

        /// <summary>The results screen for one real player (sent with the war's pay, shown for the End).</summary>
        void SendResults(Entity who, CrossfireSettings s, byte result, FactionId side, in CrossfirePay given)
        {
            string title = result == CrossfireResultKind.Won ? s.resultsWonTitle : result == CrossfireResultKind.Tied ? s.resultsTiedTitle
                : result == CrossfireResultKind.Lost ? s.resultsLostTitle : s.resultsNoPayTitle;
            string pay = Words(given);
            var rpc = new CrossfireResultsRpc
            {
                Result = result, Side = (byte)side,
                KillsHegemony = m_Sides.Kills(FactionId.Hegemony), KillsNull = m_Sides.Kills(FactionId.Null), KillsCovenant = m_Sides.Kills(FactionId.Covenant),
                Title = new FixedString32Bytes(title ?? string.Empty),
                Pay = new FixedString128Bytes(pay.Length > 120 ? pay.Substring(0, 120) : pay),
                Seconds = Math.Max(1f, s.endSeconds), WarStart = m_Clock != null ? m_Clock.Current.Now.WarStart : 0,
            };
            SendTo(who, rpc);
            ResultsSent++;
        }

        void ClearJoins()
        {
            m_JoinWay.Clear();
            m_JoinedAt.Clear();
            m_Prompted.Clear();
            m_PromptDeclined.Clear();
        }
    }

    /// <summary>Client: the join prompt on the HUD's accept / decline panel, the Marshal's JOIN CROSSFIRE, and the results screen's numbers.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrossfireJoinClientSystem : SystemBase
    {
        public const string PromptId = "crossfire-join";
        EntityQuery m_Prompts, m_Results;
        Action m_Requester;

        /// <summary>Join prompts and results screens shown (tests).</summary>
        public int PromptsShown { get; private set; }
        public int ResultsShown { get; private set; }

        protected override void OnCreate()
        {
            m_Prompts = GetEntityQuery(ComponentType.ReadOnly<CrossfireJoinPromptRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            m_Results = GetEntityQuery(ComponentType.ReadOnly<CrossfireResultsRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            Unity.Entities.World world = World;
            m_Requester = () => Send(world, CrossfireJoinRpcKind.Marshal, 0);
            CrossfireJoinLink.Requester = m_Requester;
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(CrossfireJoinLink.Requester, m_Requester)) CrossfireJoinLink.Requester = null;
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Prompts.IsEmpty)
            {
                using NativeArray<Entity> prompts = m_Prompts.ToEntityArray(Allocator.Temp);
                foreach (Entity p in prompts)
                {
                    CrossfireJoinPromptRpc prompt = em.GetComponentData<CrossfireJoinPromptRpc>(p);
                    em.DestroyEntity(p);
                    PromptsShown++;
                    double war = prompt.WarStart;
                    Unity.Entities.World w = World;
                    ClientPresentationRegistry.Prompts?.Show(PromptId, prompt.Eyebrow.ToString(), prompt.Text.ToString(), prompt.Seconds,
                        yes => Send(w, yes ? CrossfireJoinRpcKind.PromptYes : CrossfireJoinRpcKind.PromptNo, war));
                }
            }
            if (m_Results.IsEmpty) return;
            using NativeArray<Entity> results = m_Results.ToEntityArray(Allocator.Temp);
            foreach (Entity r in results)
            {
                CrossfireResultsRpc rpc = em.GetComponentData<CrossfireResultsRpc>(r);
                em.DestroyEntity(r);
                ResultsShown++;
                CrossfireView.Results = new CrossfireResults
                {
                    Result = rpc.Result, Side = (FactionId)rpc.Side,
                    KillsHegemony = rpc.KillsHegemony, KillsNull = rpc.KillsNull, KillsCovenant = rpc.KillsCovenant,
                    Title = rpc.Title.ToString(), Pay = rpc.Pay.ToString(),
                    ShownUntil = UnityEngine.Time.realtimeSinceStartupAsDouble + rpc.Seconds,
                };
            }
        }

        /// <summary>Sends a join answer or the Marshal's request to the server.</summary>
        public static void Send(Unity.Entities.World world, byte kind, double warStart)
        {
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, new CrossfireJoinRpc { Kind = kind, WarStart = warStart });
            em.AddComponentData(e, new SendRpcCommandRequest());
        }
    }
}
