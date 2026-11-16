using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using KryticalNova.Wallet;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Crossfire.Entities
{
    /// <summary>
    /// Crossfire's war itself (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part A and the owner's corrections), on the entry system that
    /// already sees who comes into a Rally Point:
    /// <list type="bullet">
    /// <item>The sides: each real player who joins fights for their faction; a side holds <see cref="CrossfireSettings.capPerFaction"/>
    /// (33) real players and War Bots together; the next real player is refused and put back where they came from, and a real player
    /// always takes a bot's place (the bot leaves).</item>
    /// <item>The War Bot vote: when the Rally starts, every side short of the cap (with the server running War Bots) is asked once; yes
    /// fills it to the cap with bots before the gates open (<see cref="IWarBotFill"/>), no or no answer leaves it short.</item>
    /// <item>The team deathmatch: kills in the War count for the killer's side (a side never scores on its own); the HUD shows them; at the
    /// War's end the most kills wins, a tie is possible, and every real player is paid the war EXP of their side's outcome.</item>
    /// <item>A kill of a real player by a real player drops an item, extremely rarely; War Bots never drop and earn nothing here.</item>
    /// </list>
    /// </summary>
    public partial class CrossfireEntryServerSystem
    {
        readonly CrossfireSides<Entity> m_Sides = new CrossfireSides<Entity>();
        readonly List<(Entity who, CrossfireSpot to)> m_Refused = new List<(Entity, CrossfireSpot)>();
        readonly List<FactionId> m_BotsOut = new List<FactionId>();
        readonly List<Entity> m_Leaving = new List<Entity>();
        /// <summary>Players put into the war area who have not been seen there yet (a teleport lands a tick later): not leavers.</summary>
        readonly HashSet<Entity> m_Arriving = new HashSet<Entity>();

        /// <summary>Players who left the war before its end (tests).</summary>
        public int Left { get; private set; }
        /// <summary>Kill drops rolled (an item was picked), whether or not it fitted in the killer's Pack (tests).</summary>
        public int DropsRolled { get; private set; }

        bool IsBot(Entity ghost) => Bots != null && Bots.IsWarBot(ghost);

        /// <summary>
        /// A real player leaves the war before its end (walked or ported out of the war area, or left the game): their place goes, they are
        /// not paid and not sent back (review 1, item 2).
        /// </summary>
        void LeaveWar(Entity who, bool tell)
        {
            if (!m_Sides.TryGetSide(who, out _)) return;
            m_Sides.Leave(who);
            m_Entries.Forget(who);
            m_Arriving.Remove(who);
            if (m_Mercs.Remove(who)) ApplyWarSide(who, FactionId.None);   // a Merc who leaves is their home faction again at once
            m_MercSide.Remove(who);
            m_MercInside.Remove(who);
            Left++;
            if (tell && Settings != null) Tell(who, Settings.leftWarText);
        }
        readonly Dictionary<FactionId, (int yes, int no)> m_Votes = new Dictionary<FactionId, (int, int)>();
        readonly HashSet<Entity> m_Voted = new HashSet<Entity>();
        readonly List<Entity> m_RealPlayers = new List<Entity>();
        System.Random m_Random = new System.Random();
        double m_VoteFor = double.NaN, m_VoteCloses, m_PaidFor = double.NaN;
        bool m_VoteOpen;
        EntityQuery m_VoteAnswers, m_Connections, m_MercAnswers;
        readonly Dictionary<Entity, (FactionId side, double expires, double war)> m_Offers = new Dictionary<Entity, (FactionId, double, double)>();
        readonly Dictionary<Entity, FactionId> m_Home = new Dictionary<Entity, FactionId>();
        readonly HashSet<Entity> m_Mercs = new HashSet<Entity>();
        readonly List<Entity> m_Lapsed = new List<Entity>();
        /// <summary>
        /// Owner, 16 Nov 2026: "they only become a Merc once they agree to be one AND once they enter the Crossfire map". The side a Merc
        /// agreed to, and the Mercs who are inside the war area now (only they fight for it); leaving or the war's end takes it away.
        /// </summary>
        readonly Dictionary<Entity, FactionId> m_MercSide = new Dictionary<Entity, FactionId>();
        readonly HashSet<Entity> m_MercInside = new HashSet<Entity>();
        readonly List<Entity> m_ToSide = new List<Entity>();

        /// <summary>A Merc who agreed is now inside the war area: from now they fight for their adopted side.</summary>
        void EnterAsMerc(Entity who)
        {
            if (!m_MercSide.TryGetValue(who, out FactionId side) || !m_MercInside.Add(who)) return;
            ApplyWarSide(who, side);
        }

        /// <summary>Players who declined (or let run out) a Merc offer this war: never offered again in it (review 1, item 3).</summary>
        readonly HashSet<Entity> m_Declined = new HashSet<Entity>();
        public bool Declined(Entity who) => m_Declined.Contains(who);

        /// <summary>Merc offers made, taken and declined (tests); players fighting as Mercs now.</summary>
        public int MercOffers { get; private set; }
        public int MercsTaken { get; private set; }
        public int MercsDeclined { get; private set; }
        public IReadOnlyCollection<Entity> Mercs => m_Mercs;
        public bool HasOffer(Entity who) => m_Offers.ContainsKey(who);

        /// <summary>The faction a real player fights for. Unset: their server body's (<see cref="IFactionMember"/>). Tests put their own.</summary>
        public Func<Entity, FactionId> FactionOf;
        /// <summary>The level a real player has. Unset: their <see cref="PlayerLevelTag"/>. Tests put their own.</summary>
        public Func<Entity, int> LevelOf;
        /// <summary>The dice of the kill drop (tests put their own seed).</summary>
        public System.Random Random { get => m_Random; set => m_Random = value ?? new System.Random(); }

        /// <summary>The war's sides (tests).</summary>
        public CrossfireSides<Entity> Sides => m_Sides;
        /// <summary>Real players refused at a full side; votes sent; sides the bots filled; kill drops; EXP paid per player at the last war's end (tests).</summary>
        public int Refused { get; private set; }
        public int VotesSent { get; private set; }
        public int SidesFilled { get; private set; }
        public int Drops { get; private set; }
        public readonly Dictionary<Entity, CrossfirePay> LastPay = new Dictionary<Entity, CrossfirePay>();
        /// <summary>What each real player was really given at the last war's end (a capped wallet takes less; no body, nothing) (tests).</summary>
        public readonly Dictionary<Entity, CrossfirePay> LastGiven = new Dictionary<Entity, CrossfirePay>();
        /// <summary>The item the last drop gave (tests).</summary>
        public ushort LastDrop { get; private set; }

        /// <summary>
        /// What a real player has to be paid on: (character at the level cap, EXP of their level step, pet, mount), pet and mount -1 none,
        /// 0 growing, 1 fully grown. Unset: their server body's progression and growth shares (<see cref="IGrowthShare"/>). Tests put their own.
        /// </summary>
        public Func<Entity, (bool maxed, int levelStep, int pet, int mount)> GrowthOf;
        /// <summary>The item a kill drops for a killer of a level: the mobs' gear rule on the killer's catalog (<see cref="HostileLoot.PickGear"/>). Tests put their own.</summary>
        public Func<Entity, int, float, ItemDefinition> PickDrop;

        void CreateWar()
        {
            m_VoteAnswers = GetEntityQuery(ComponentType.ReadOnly<CrossfireVoteAnswerRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            m_Connections = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            m_MercAnswers = GetEntityQuery(ComponentType.ReadOnly<CrossfireMercAnswerRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            CombatEntity.AnyKilled += OnAnyKilled;
        }

        void DestroyWar() => CombatEntity.AnyKilled -= OnAnyKilled;

        CrossfireSettings Settings => m_Clock != null ? m_Clock.ActiveSettings : null;
        static IWarBotFill Bots => WarBots.Fill;
        static int BotsOn(FactionId side) => Bots != null ? Bots.Count(side) : 0;

        bool TryBodyEntry(Entity ghost, out GhostBodyEntry entry)
        {
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Ghost == ghost) { entry = e; return true; }
            entry = null;
            return false;
        }

        bool TryGhostOf(GameObject body, out Entity ghost)
        {
            ghost = Entity.Null;
            if (body == null) return false;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && (body == e.Body || body.transform.IsChildOf(e.Body.transform))) { ghost = e.Ghost; return true; }
            return false;
        }

        /// <summary>A real player's HOME faction (never the side they fight for as a Merc).</summary>
        FactionId SideOfReal(Entity ghost)
        {
            if (FactionOf != null) return FactionOf(ghost);
            return TryBodyEntry(ghost, out GhostBodyEntry e) ? HomeOf(e.Body) : FactionId.None;
        }

        static FactionId HomeOf(GameObject body)
        {
            if (body == null) return FactionId.None;
            PlayerIdentity identity = body.GetComponentInParent<PlayerIdentity>();
            if (identity != null) return identity.HomeFaction;
            IFactionMember member = body.GetComponentInParent<IFactionMember>();
            return member != null ? member.Faction : FactionId.None;
        }

        int LevelOfReal(Entity ghost)
        {
            if (LevelOf != null) return LevelOf(ghost);
            EntityManager em = EntityManager;
            return em.Exists(ghost) && em.HasComponent<PlayerLevelTag>(ghost) ? em.GetComponentData<PlayerLevelTag>(ghost).Level : 0;
        }

        /// <summary>Tells one real player a line through the announcement system (<see cref="ServerChat.AnnounceTo"/>).</summary>
        void Tell(Entity ghost, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Told.Add((ghost, text));
            m_ToTell.Add((ghost, text));   // said by FlushTells, never inside a look over the players (a chat line may change the world)
        }

        readonly List<(Entity who, string text)> m_ToTell = new List<(Entity, string)>();

        void FlushTells()
        {
            if (m_ToTell.Count == 0) return;
            var tell = ServerChat.AnnounceTo;
            foreach ((Entity who, string text) in m_ToTell)
                if (tell != null && TryBodyEntry(who, out GhostBodyEntry e) && e.Body != null) tell(e.Body, AnnouncementKind.Crossfire, text, null, 0);
            m_ToTell.Clear();
        }

        /// <summary>Every line told to a real player this run (tests).</summary>
        public readonly List<(Entity who, string text)> Told = new List<(Entity, string)>();

        static string Fill(string text, CrossfireSettings s, int real) =>
            (text ?? string.Empty).Replace("{cap}", s.capPerFaction.ToString()).Replace("{real}", real.ToString());

        /// <summary>
        /// A real player stands in a Rally Point while it takes players in and has not joined: their side takes them (in a bot's place if
        /// it must) or, full of real players, refuses them. False when refused (they are put back where they came from).
        /// </summary>
        bool Admit(Entity who, CrossfireSpot from)
        {
            CrossfireSettings s = Settings;
            if (s == null) return true;
            if (IsBot(who)) return false;   // never a real player's place (review 1, item 4)
            if (m_Sides.TryGetSide(who, out _)) return true;   // already fights (a Merc put on their adopted yard): their entry is kept for the send-back
            FactionId side = SideOfReal(who);
            m_Home[who] = side;
            CrossfireJoin join = m_Sides.TryJoin(who, side, s.capPerFaction, BotsOn(side));
            switch (join)
            {
                case CrossfireJoin.Full:
                    m_Refused.Add((who, from));
                    Refused++;
                    if (!TryOfferMerc(who, side, s)) Tell(who, Fill(s.sideFullText, s, m_Sides.Real(side)));
                    return false;
                case CrossfireJoin.TookBotsPlace:
                    m_BotsOut.Add(side);   // after the look over every player: a bot leaving changes the world
                    Tell(who, s.tookBotsPlaceText);
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>The port's cap check: the player's side is full of real players.</summary>
        bool SideFull(FactionId side)
        {
            CrossfireSettings s = Settings;
            return s != null && m_Sides.Real(side) >= s.capPerFaction;
        }

        /// <summary>Puts refused players back where they came from, and takes off the bots real players replaced (after the look over every player).</summary>
        void SendRefusedBack()
        {
            foreach (FactionId side in m_BotsOut) Bots?.Release(side, 1);
            m_BotsOut.Clear();
            FlushTells();
            EntityManager em = EntityManager;
            foreach ((Entity who, CrossfireSpot to) in m_Refused)
                if (em.Exists(who) && em.HasComponent<TeleportRequest>(who))
                    PlayerEntitiesSetup.Teleport(em, who, new Unity.Mathematics.int3(to.Mm.x, to.Mm.y, to.Mm.z), to.YawCdeg);
            m_Refused.Clear();
        }

        // ------------------------------------------------------------------ Mercs (owner, 16 Nov 2026)

        /// <summary>
        /// The short side a refused player of <paramref name="home"/> may fight for as a Merc: another faction's side with room (real
        /// players and bots under the cap), the shortest first; None when every other side is full.
        /// </summary>
        FactionId ShortSideFor(FactionId home, CrossfireSettings s)
        {
            FactionId best = FactionId.None;
            int fewest = int.MaxValue;
            foreach (FactionId f in CrossfireSides<Entity>.Factions)
            {
                if (f == home || !CrossfireArea.TryRallyPoint(f, out _)) continue;
                int n = m_Sides.Real(f);   // real players only: a Merc takes a bot's place like anyone (review 1, item 1)
                if (n < s.capPerFaction && n < fewest) { fewest = n; best = f; }
            }
            return best;
        }

        /// <summary>Offers a refused player a Merc place on the shortest side (once a war). False when Mercs are off or no side has room.</summary>
        bool TryOfferMerc(Entity who, FactionId home, CrossfireSettings s)
        {
            if (!s.offerMercs || m_Offers.ContainsKey(who)) return m_Offers.ContainsKey(who);
            if (m_Declined.Contains(who))
            {
                Tell(who, s.mercDeclinedText);   // their answer for this war stands: they miss it
                return true;
            }
            FactionId side = ShortSideFor(home, s);
            if (side == FactionId.None) return false;
            double war = m_Clock.Current.Now.WarStart;
            m_Offers[who] = (side, m_Clock.Now + s.mercOfferSeconds, war);
            m_Home[who] = home;
            MercOffers++;
            var rpc = new CrossfireMercOfferRpc
            {
                Eyebrow = s.mercEyebrow ?? string.Empty, Text = Fill(s.mercOfferText, s, 0).Replace("{side}", side.ToString()),
                Seconds = s.mercOfferSeconds, WarStart = war,
            };
            SendTo(who, rpc);
            return true;
        }

        void SendTo<T>(Entity who, T rpc) where T : unmanaged, IRpcCommand
        {
            EntityManager em = EntityManager;
            if (!em.Exists(who) || !em.HasComponent<GhostOwner>(who)) return;
            int id = em.GetComponentData<GhostOwner>(who).NetworkId;
            using NativeArray<Entity> connections = m_Connections.ToEntityArray(Allocator.Temp);
            foreach (Entity c in connections)
            {
                if (em.GetComponentData<NetworkId>(c).Value != id) continue;
                Entity e = em.CreateEntity();
                em.AddComponentData(e, rpc);
                em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = c });
            }
        }

        /// <summary>
        /// A refused player's answer to their Merc offer (from the panel, or a test). Yes, while the yards still take players and the side
        /// still has room: they fight for it (in a bot's place if they must) and are put on its Rally Point. No, or too late: they miss this war.
        /// </summary>
        public bool AnswerMerc(Entity who, bool yes)
        {
            if (!m_Offers.TryGetValue(who, out (FactionId side, double expires, double war) offer)) return false;
            m_Offers.Remove(who);
            CrossfireSettings s = Settings;
            bool open = s != null && m_Clock.Current.JoinOpen(m_Clock.Now) && m_Clock.Current.Now.WarStart.Equals(offer.war);
            CrossfireJoin join = yes && open ? m_Sides.TryJoin(who, offer.side, s.capPerFaction, BotsOn(offer.side)) : CrossfireJoin.Full;
            if (join == CrossfireJoin.Full)
            {
                m_Declined.Add(who);
                MercsDeclined++;
                if (s != null) Tell(who, s.mercDeclinedText);
                FlushTells();
                return false;
            }
            if (join == CrossfireJoin.TookBotsPlace) m_BotsOut.Add(offer.side);
            m_Mercs.Add(who);
            m_MercSide[who] = offer.side;
            MercsTaken++;   // the war side itself waits until they are inside the war area (owner, 16 Nov 2026: EnterAsMerc)
            EntityManager em = EntityManager;
            if (CrossfireArea.TryRallyPoint(offer.side, out CrossfireArea yard) && em.Exists(who) && em.HasComponent<TeleportRequest>(who))
            {
                yard.Landing(out Vector3 at, out float yaw);
                PlayerEntitiesSetup.Teleport(em, who, new Unity.Mathematics.int3((int)Math.Round(at.x * 1000f), (int)Math.Round(at.y * 1000f), (int)Math.Round(at.z * 1000f)),
                    (int)Math.Round(Mathf.Repeat(yaw, 360f) * 100f) % 36000);
                m_Arriving.Add(who);
            }
            Tell(who, (s.mercJoinedText ?? string.Empty).Replace("{side}", offer.side.ToString()));
            SendRefusedBack();
            return true;
        }

        /// <summary>The side a Merc fights for, on their ghost (every client applies it to their views) and their server body (hostility).</summary>
        void ApplyWarSide(Entity who, FactionId side)
        {
            EntityManager em = EntityManager;
            if (em.Exists(who) && em.HasComponent<PvpSwitch>(who))
            {
                PvpSwitch pvp = em.GetComponentData<PvpSwitch>(who);
                pvp.WarSide = (byte)side;
                em.SetComponentData(who, pvp);
            }
            if (TryBodyEntry(who, out GhostBodyEntry e) && e.Body != null && e.Body.TryGetComponent(out PlayerIdentity identity)) identity.SetWarSide(side);
        }

        void ReadMercAnswers()
        {
            EntityManager em = EntityManager;
            if (!m_MercAnswers.IsEmpty)
            {
                using NativeArray<Entity> answers = m_MercAnswers.ToEntityArray(Allocator.Temp);
                foreach (Entity a in answers)
                {
                    CrossfireMercAnswerRpc answer = em.GetComponentData<CrossfireMercAnswerRpc>(a);
                    Entity from = em.GetComponentData<ReceiveRpcCommandRequest>(a).SourceConnection;
                    em.DestroyEntity(a);
                    if (!em.Exists(from) || !em.HasComponent<NetworkId>(from)) continue;
                    int id = em.GetComponentData<NetworkId>(from).Value;
                    Entity who = Entity.Null;
                    foreach (KeyValuePair<Entity, (FactionId, double, double)> o in m_Offers)
                        if (em.Exists(o.Key) && em.HasComponent<GhostOwner>(o.Key) && em.GetComponentData<GhostOwner>(o.Key).NetworkId == id) { who = o.Key; break; }
                    if (who != Entity.Null && m_Offers[who].war.Equals(answer.WarStart)) AnswerMerc(who, answer.Yes != 0);
                }
            }
            // offers nobody answered: they miss this war
            m_Lapsed.Clear();
            foreach (KeyValuePair<Entity, (FactionId side, double expires, double war)> o in m_Offers)
                if (m_Clock.Now >= o.Value.expires || !em.Exists(o.Key)) m_Lapsed.Add(o.Key);
            foreach (Entity who in m_Lapsed) AnswerMerc(who, false);
        }

        // ------------------------------------------------------------------ the kills

        void OnAnyKilled(CombatEntity victim, KillingBlow blow)
        {
            if (victim == null || blow.Attacker == null || !World.IsCreated) return;
            if (TryGhostOf(victim.gameObject, out Entity dead) && TryGhostOf(blow.Attacker.gameObject, out Entity killer)) RecordKill(killer, dead);
        }

        /// <summary>The side a ghost fights for in this war: a real player's, a War Bot's, else None.</summary>
        FactionId SideIn(Entity ghost, out bool bot)
        {
            bot = false;
            if (m_Sides.TryGetSide(ghost, out FactionId side)) return side;
            FactionId botSide = Bots != null ? Bots.SideOf(ghost) : FactionId.None;
            bot = botSide != FactionId.None;
            return botSide;
        }

        /// <summary>
        /// A kill between two ghosts (the combat's kill, or a test): during the War it counts for the killer's side, and a real player's
        /// kill of a real player may drop an item. True when it counted.
        /// </summary>
        public bool RecordKill(Entity killer, Entity victim)
        {
            m_Clock ??= World.GetExistingSystemManaged<CrossfireClockServerSystem>();
            if (m_Clock == null || !m_Clock.Current.Known || m_Clock.Current.Now.Phase != CrossfirePhase.War) return false;
            FactionId killerSide = SideIn(killer, out bool killerBot), victimSide = SideIn(victim, out bool victimBot);
            if (!m_Sides.AddKill(killerSide, victimSide)) return false;
            m_Clock.SetScore(m_Sides.Kills(FactionId.Hegemony), m_Sides.Kills(FactionId.Null), m_Sides.Kills(FactionId.Covenant));
            CrossfireSettings s = Settings;
            if (CrossfireRewards.KillDrops(s, killerBot, victimBot, m_Random.NextDouble())) GiveDrop(killer, s);
            FlushTells();
            return true;
        }

        /// <summary>
        /// A kill's very rare drop: a piece of gear a Hostile of the killer's level can drop, into their Pack, with its ledger line
        /// (<see cref="LedgerKinds.Crossfire"/>), as every item an economy action moves.
        /// </summary>
        void GiveDrop(Entity killer, CrossfireSettings s)
        {
            PlayerInventory pack = TryBodyEntry(killer, out GhostBodyEntry e) && e.Body != null ? e.Body.GetComponent<PlayerInventory>() : null;
            int level = LevelOfReal(killer);
            float pick = (float)m_Random.NextDouble();
            ItemDefinition item = PickDrop != null ? PickDrop(killer, level, pick)
                : pack != null ? HostileLoot.PickGear(pack.Catalog, level, s.killDropLevelsBelow, s.killDropBestRarity, pick) : null;
            if (item == null) return;
            DropsRolled++;
            LastDrop = item.itemId;
            // only what was given is told (review 1, item 5): a full Pack takes nothing, and nothing is said
            if (pack == null || pack.ServerGive(item.itemId, 1, earned: true) <= 0) return;
            Drops++;
            EconomyLedger.Keep(EconomyLedger.Line(pack, LedgerKinds.Crossfire, note: "kill drop").Item(pack.Catalog, new ItemStack(item.itemId, 1), 1));
            Tell(killer, (s.killDropText ?? string.Empty).Replace("{item}", item.displayName));
        }

        // ------------------------------------------------------------------ the vote

        void SendVote(FactionId side, CrossfireSettings s, double warStart)
        {
            EntityManager em = EntityManager;
            int real = m_Sides.Real(side);
            var rpc = new CrossfireVoteRpc
            {
                Eyebrow = s.voteEyebrow ?? string.Empty, Text = Fill(s.voteText, s, real),
                Seconds = (float)Math.Max(1.0, m_VoteCloses - m_Clock.Now), WarStart = warStart,
            };
            using NativeArray<Entity> connections = m_Connections.ToEntityArray(Allocator.Temp);
            foreach (KeyValuePair<Entity, FactionId> p in m_Sides.Players)
            {
                if (p.Value != side || !em.Exists(p.Key) || !em.HasComponent<GhostOwner>(p.Key)) continue;
                int id = em.GetComponentData<GhostOwner>(p.Key).NetworkId;
                foreach (Entity c in connections)
                {
                    if (em.GetComponentData<NetworkId>(c).Value != id) continue;
                    Entity e = em.CreateEntity();
                    em.AddComponentData(e, rpc);
                    em.AddComponentData(e, new SendRpcCommandRequest { TargetConnection = c });
                    VotesSent++;
                }
            }
        }

        /// <summary>A real player's vote (from their answer, or a test): counts once, for their own side, while the vote is open.</summary>
        public bool Vote(Entity who, bool yes)
        {
            if (!m_VoteOpen || !m_Sides.TryGetSide(who, out FactionId side) || !m_Votes.TryGetValue(side, out (int yes, int no) v) || !m_Voted.Add(who)) return false;
            m_Votes[side] = yes ? (v.yes + 1, v.no) : (v.yes, v.no + 1);
            return true;
        }

        void ReadVoteAnswers()
        {
            if (m_VoteAnswers.IsEmpty) return;
            EntityManager em = EntityManager;
            using NativeArray<Entity> answers = m_VoteAnswers.ToEntityArray(Allocator.Temp);
            foreach (Entity a in answers)
            {
                CrossfireVoteAnswerRpc answer = em.GetComponentData<CrossfireVoteAnswerRpc>(a);
                Entity from = em.GetComponentData<ReceiveRpcCommandRequest>(a).SourceConnection;
                if (answer.WarStart.Equals(m_VoteFor) && em.Exists(from) && em.HasComponent<NetworkId>(from))
                {
                    int id = em.GetComponentData<NetworkId>(from).Value;
                    foreach (KeyValuePair<Entity, FactionId> p in m_Sides.Players)
                        if (em.Exists(p.Key) && em.HasComponent<GhostOwner>(p.Key) && em.GetComponentData<GhostOwner>(p.Key).NetworkId == id) { Vote(p.Key, answer.Yes != 0); break; }
                }
                em.DestroyEntity(a);
            }
        }

        void CloseVote(CrossfireSettings s)
        {
            m_VoteOpen = false;
            foreach (KeyValuePair<FactionId, (int yes, int no)> v in m_Votes)
            {
                FactionId side = v.Key;
                int real = m_Sides.Real(side);
                bool passes = CrossfireVote.Passes(v.Value.yes, v.Value.no) && Bots != null;
                if (passes)
                {
                    m_RealPlayers.Clear();
                    foreach (KeyValuePair<Entity, FactionId> p in m_Sides.Players) m_RealPlayers.Add(p.Key);
                    var request = new WarBotRequest { Faction = side, Bracket = BracketOfWar(s), Count = s.capPerFaction - real - BotsOn(side) };
                    if (CrossfireArea.TryRallyPoint(side, out CrossfireArea yard)) yard.Landing(out request.RallyAt, out request.RallyYaw);
                    if (request.Count > 0 && Bots.Fill(request, m_RealPlayers) > 0) SidesFilled++;
                }
                foreach (KeyValuePair<Entity, FactionId> p in m_Sides.Players)
                    if (p.Value == side) Tell(p.Key, Fill(passes ? s.votePassedText : s.voteFailedText, s, real));
            }
            m_Votes.Clear();
        }

        /// <summary>The war's bracket, from its first real player: right while only one bracket (X) has a zone (one war at a time).</summary>
        int BracketOfWar(CrossfireSettings s)
        {
            foreach (KeyValuePair<Entity, FactionId> p in m_Sides.Players)
            {
                int b = s.BracketOf(LevelOfReal(p.Key));
                if (b > 0) return b;
            }
            return s.bracketCount;
        }

        // ------------------------------------------------------------------ the war's course

        /// <summary>One look a tick: the vote, the pay at the War's end, and the war's sides cleared when it is over.</summary>
        void RunWar(in CrossfireState state, double now)
        {
            CrossfireSettings s = Settings;
            if (s == null) return;
            EntityManager em = EntityManager;
            m_Sides.ForgetWhere(e => !em.Exists(e));   // left the game: no place, no pay
            m_Mercs.RemoveWhere(e => !em.Exists(e));
            m_MercInside.RemoveWhere(e => !em.Exists(e));
            m_Arriving.RemoveWhere(e => !em.Exists(e));
            ReadVoteAnswers();
            ReadMercAnswers();
            CrossfirePhase phase = state.Now.Phase;
            double war = state.Now.WarStart;

            // the vote opens with the Rally, for every short side
            if (phase == CrossfirePhase.Muster && !m_VoteFor.Equals(war))
            {
                m_VoteFor = war;
                m_Votes.Clear();
                m_Voted.Clear();
                double gatesOpen = war + s.musterSeconds;
                m_VoteCloses = Math.Min(now + s.voteSeconds, gatesOpen - s.voteClosesBeforeGatesSeconds);
                m_VoteOpen = true;
                foreach (FactionId side in CrossfireSides<Entity>.Factions)
                {
                    if (!CrossfireVote.Offered(m_Sides.Real(side), BotsOn(side), s.capPerFaction, Bots != null)) continue;
                    m_Votes[side] = (0, 0);
                    SendVote(side, s, war);
                }
            }
            if (m_VoteOpen && (now >= m_VoteCloses || phase != CrossfirePhase.Muster || !m_VoteFor.Equals(war))) CloseVote(s);

            // the War's end: the most kills wins; every real player is paid by their side's outcome
            if (phase == CrossfirePhase.End && !m_PaidFor.Equals(war))
            {
                m_PaidFor = war;
                Pay(s);
            }
            FlushTells();
        }

        /// <summary>
        /// The war's pay, to every real player (War Bots are never on the sides): EXP for the character, the active pet and the selected
        /// mount, and Creds (a new source through the wallet's own tally, <see cref="CredsSource.Crossfire"/>, which writes the ledger
        /// line). Each payout changes only that player's own save, so no <c>ServerSave.SaveTogether</c> is needed.
        /// </summary>
        void Pay(CrossfireSettings s)
        {
            LastPay.Clear();
            LastGiven.Clear();
            // owner, 16 Nov 2026: a war with no kill on any side pays nothing to anyone (no farming by standing in a yard)
            int kills = 0;
            foreach (FactionId f in CrossfireSides<Entity>.Factions) kills += m_Sides.Kills(f);
            if (kills == 0)
            {
                foreach (KeyValuePair<Entity, FactionId> p in m_Sides.Players) Tell(p.Key, s.noKillsText);
                return;
            }
            int bracket = BracketOfWar(s);
            string score = $"Hegemony {m_Sides.Kills(FactionId.Hegemony)}, Null {m_Sides.Kills(FactionId.Null)}, Covenant {m_Sides.Kills(FactionId.Covenant)}";
            foreach (KeyValuePair<Entity, FactionId> p in m_Sides.Players)
            {
                CrossfireOutcome outcome = m_Sides.Outcome(p.Value);
                GameObject body = TryBodyEntry(p.Key, out GhostBodyEntry e) ? e.Body : null;
                PlayerCharacterProgression progression = body != null ? body.GetComponent<PlayerCharacterProgression>() : null;
                IGrowthShare pet = Share(body, GrowthShareKind.Pet), mount = Share(body, GrowthShareKind.Mount);
                (bool maxed, int step, int petState, int mountState) growth = GrowthOf != null ? GrowthOf(p.Key)
                    : (progression != null && progression.IsAtLevelCap, progression != null ? progression.ExperienceRequired : 0, StateOf(pet), StateOf(mount));
                CrossfirePay pay = CrossfireRewards.Payout(s, outcome, bracket, growth.maxed, growth.step, growth.petState, growth.mountState);
                LastPay[p.Key] = pay;
                // what was really given, and only that is told (review 1, item 5: a wallet at its cap takes less)
                var given = new CrossfirePay();
                if (pay.CharacterExp > 0 && progression != null) { progression.GrantExperience(pay.CharacterExp); given.CharacterExp = pay.CharacterExp; }
                if (pay.PetPercent > 0f && pet != null) { pet.ServerGrantPercentOfFull(pay.PetPercent); given.PetPercent = pay.PetPercent; }
                if (pay.MountPercent > 0f && mount != null) { mount.ServerGrantPercentOfFull(pay.MountPercent); given.MountPercent = pay.MountPercent; }
                if (pay.Creds > 0 && body != null && body.TryGetComponent(out PlayerWallet wallet)) given.Creds = wallet.ServerEarn(pay.Creds, CredsSource.Crossfire);
                LastGiven[p.Key] = given;
                string text = outcome == CrossfireOutcome.Won ? s.wonText : outcome == CrossfireOutcome.Tied ? s.tiedText : s.lostText;
                Tell(p.Key, (text ?? string.Empty).Replace("{kills}", m_Sides.Kills(p.Value).ToString()).Replace("{score}", score).Replace("{pay}", Words(given)));
            }
        }

        static IGrowthShare Share(GameObject body, GrowthShareKind kind)
        {
            if (body == null) return null;
            foreach (IGrowthShare share in body.GetComponents<IGrowthShare>()) if (share.Kind == kind) return share;
            return null;
        }

        static int StateOf(IGrowthShare share) => share == null || !share.HasActive ? -1 : share.IsFullyGrown ? 1 : 0;

        /// <summary>"+4,210 EXP, pet +0.5 %, mount +0.5 %, +50,000 Creds".</summary>
        public static string Words(in CrossfirePay pay)
        {
            var parts = new List<string>(4);
            if (pay.CharacterExp > 0) parts.Add($"+{pay.CharacterExp:N0} EXP");
            if (pay.PetPercent > 0f) parts.Add($"pet +{pay.PetPercent:0.##} %");
            if (pay.MountPercent > 0f) parts.Add($"mount +{pay.MountPercent:0.##} %");
            if (pay.Creds > 0) parts.Add($"+{pay.Creds:N0} Creds");
            return string.Join(", ", parts) + (parts.Count > 0 ? "." : string.Empty);
        }

        /// <summary>The war is over (its End ran out, or a GM called it off): its sides, score and bots go.</summary>
        void EndWar()
        {
            foreach (Entity merc in m_Mercs) ApplyWarSide(merc, FactionId.None);   // every Merc is their home faction again
            m_Mercs.Clear();
            m_MercSide.Clear();
            m_MercInside.Clear();
            m_Offers.Clear();
            m_Declined.Clear();
            m_Home.Clear();
            m_Arriving.Clear();
            m_Sides.Clear();
            m_Votes.Clear();
            m_VoteOpen = false;
            m_Clock?.SetScore(0, 0, 0);
            Bots?.EndWar();
        }
    }

    /// <summary>Client: the War Bot vote on the HUD's accept / decline panel (<see cref="IRequestPromptSink"/>), and the answer back to the server.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrossfireVoteClientSystem : SystemBase
    {
        public const string PromptId = "crossfire-vote";
        public const string MercPromptId = "crossfire-merc";
        EntityQuery m_Votes, m_Offers;

        /// <summary>Votes shown (tests).</summary>
        public int Shown { get; private set; }

        protected override void OnCreate()
        {
            m_Votes = GetEntityQuery(ComponentType.ReadOnly<CrossfireVoteRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
            m_Offers = GetEntityQuery(ComponentType.ReadOnly<CrossfireMercOfferRpc>(), ComponentType.ReadOnly<ReceiveRpcCommandRequest>());
        }

        /// <summary>Merc offers shown (tests).</summary>
        public int OffersShown { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Offers.IsEmpty)
            {
                using NativeArray<Entity> offers = m_Offers.ToEntityArray(Allocator.Temp);
                foreach (Entity o in offers)
                {
                    CrossfireMercOfferRpc offer = em.GetComponentData<CrossfireMercOfferRpc>(o);
                    em.DestroyEntity(o);
                    OffersShown++;
                    double offerWar = offer.WarStart;
                    Unity.Entities.World w = World;
                    ClientPresentationRegistry.Prompts?.Show(MercPromptId, offer.Eyebrow.ToString(), offer.Text.ToString(), offer.Seconds, yes => AnswerMerc(w, offerWar, yes));
                }
            }
            if (m_Votes.IsEmpty) return;
            using NativeArray<Entity> votes = m_Votes.ToEntityArray(Allocator.Temp);
            foreach (Entity v in votes)
            {
                CrossfireVoteRpc vote = em.GetComponentData<CrossfireVoteRpc>(v);
                em.DestroyEntity(v);
                Shown++;
                double war = vote.WarStart;
                Unity.Entities.World world = World;
                ClientPresentationRegistry.Prompts?.Show(PromptId, vote.Eyebrow.ToString(), vote.Text.ToString(), vote.Seconds, yes => Answer(world, war, yes));
            }
        }

        /// <summary>Sends the player's answer to a Merc offer to the server.</summary>
        public static void AnswerMerc(Unity.Entities.World world, double warStart, bool yes)
        {
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, new CrossfireMercAnswerRpc { Yes = (byte)(yes ? 1 : 0), WarStart = warStart });
            em.AddComponentData(e, new SendRpcCommandRequest());
        }

        /// <summary>Sends the player's answer to the server.</summary>
        public static void Answer(Unity.Entities.World world, double warStart, bool yes)
        {
            if (world == null || !world.IsCreated) return;
            EntityManager em = world.EntityManager;
            Entity e = em.CreateEntity();
            em.AddComponentData(e, new CrossfireVoteAnswerRpc { Yes = (byte)(yes ? 1 : 0), WarStart = warStart });
            em.AddComponentData(e, new SendRpcCommandRequest());
        }
    }

    /// <summary>
    /// Client: every player view fights for the side the server put on its ghost (<see cref="PvpSwitch.WarSide"/>): a Crossfire Merc is
    /// hostile to their home faction and friendly to the side they fight for, on namecards, targets and the Callout strip (owner, 16 Nov 2026).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CrossfireWarSideClientSystem : SystemBase
    {
        /// <summary>Views whose side changed (tests).</summary>
        public int Applied { get; private set; }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, World) || e.Body == null || !em.Exists(e.Ghost) || !em.HasComponent<PvpSwitch>(e.Ghost)) continue;
                if (!e.Body.TryGetComponent(out PlayerIdentity identity)) continue;
                var side = (FactionId)em.GetComponentData<PvpSwitch>(e.Ghost).WarSide;
                if (identity.WarSide == side || (side == identity.HomeFaction && identity.WarSide == FactionId.None)) continue;
                identity.SetWarSide(side);
                Applied++;
            }
        }
    }
}
