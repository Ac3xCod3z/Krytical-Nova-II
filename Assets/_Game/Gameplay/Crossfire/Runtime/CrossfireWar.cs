using System;
using System.Collections.Generic;
using KryticalNova.Factions;

namespace KryticalNova.Crossfire
{
    /// <summary>How a war ended for one side (owner, 16 Nov 2026: a team deathmatch, the most kills wins, a tie is possible).</summary>
    public enum CrossfireOutcome : byte { Lost = 0, Tied = 1, Won = 2 }

    /// <summary>What a side does with a player who comes to its Rally Point (<see cref="CrossfireSides{T}.TryJoin"/>).</summary>
    public enum CrossfireJoin : byte
    {
        /// <summary>In: there was room.</summary>
        Joined = 0,
        /// <summary>In, in a War Bot's place: the side was full with bots in it, and one bot leaves (backfill).</summary>
        TookBotsPlace = 1,
        /// <summary>Was already on this side.</summary>
        Already = 2,
        /// <summary>Refused: the side has its cap of real players (the 34th of 33).</summary>
        Full = 3,
    }

    /// <summary>
    /// The sides of one Crossfire war (owner, 16 Nov 2026; CROSSFIRE-WARBOTS-BRIEF Part A): who fights for which faction, the cap per side
    /// (<see cref="CrossfireSettings.capPerFaction"/>, 33), War Bots in a side counting against the cap, a real player taking a bot's place,
    /// and the team deathmatch: kills per side, most kills wins, ties allowed. Server only; pure (no world), so it is tested as it is.
    /// </summary>
    public sealed class CrossfireSides<T>
    {
        /// <summary>The factions that fight (the fourth yard stays sealed until the Ronin).</summary>
        public static readonly FactionId[] Factions = { FactionId.Hegemony, FactionId.Null, FactionId.Covenant };

        readonly Dictionary<T, FactionId> m_Side = new Dictionary<T, FactionId>();
        readonly int[] m_Kills = new int[4];

        /// <summary>Real players in the war, all sides.</summary>
        public int Count => m_Side.Count;
        public IEnumerable<KeyValuePair<T, FactionId>> Players => m_Side;

        public bool TryGetSide(T who, out FactionId side) => m_Side.TryGetValue(who, out side);

        /// <summary>Real players fighting for <paramref name="side"/>.</summary>
        public int Real(FactionId side)
        {
            int n = 0;
            foreach (FactionId f in m_Side.Values) if (f == side) n++;
            return n;
        }

        /// <summary>
        /// A real player comes to <paramref name="side"/>'s Rally Point. <paramref name="bots"/> War Bots fight for it now; real players and
        /// bots together never pass <paramref name="cap"/>, and a real player always takes a bot's place rather than be refused.
        /// </summary>
        public CrossfireJoin TryJoin(T who, FactionId side, int cap, int bots)
        {
            if (m_Side.TryGetValue(who, out FactionId had) && had == side) return CrossfireJoin.Already;
            int real = Real(side);
            if (real >= cap) return CrossfireJoin.Full;
            bool takesBot = real + Math.Max(0, bots) >= cap && bots > 0;
            m_Side[who] = side;
            return takesBot ? CrossfireJoin.TookBotsPlace : CrossfireJoin.Joined;
        }

        /// <summary>A player left the war (left the game, or was sent back).</summary>
        public void Leave(T who) => m_Side.Remove(who);

        readonly List<T> m_Gone = new List<T>();

        /// <summary>Takes out every player <paramref name="gone"/> says has left the game.</summary>
        public void ForgetWhere(Func<T, bool> gone)
        {
            m_Gone.Clear();
            foreach (T who in m_Side.Keys) if (gone(who)) m_Gone.Add(who);
            foreach (T who in m_Gone) m_Side.Remove(who);
        }

        /// <summary>
        /// A kill in the war: counts for <paramref name="killerSide"/> when it is a fighting side and not the victim's own. False when it
        /// does not count (a side killing its own, a kill by or of someone outside the war).
        /// </summary>
        public bool AddKill(FactionId killerSide, FactionId victimSide)
        {
            if (!Fights(killerSide) || !Fights(victimSide) || killerSide == victimSide) return false;
            m_Kills[(int)killerSide]++;
            return true;
        }

        public int Kills(FactionId side) => Fights(side) ? m_Kills[(int)side] : 0;

        static bool Fights(FactionId side) => side == FactionId.Hegemony || side == FactionId.Null || side == FactionId.Covenant;

        /// <summary>How the war ended for <paramref name="side"/>: the most kills wins; two or three sides level at the top tie (no kills at all: everyone ties).</summary>
        public CrossfireOutcome Outcome(FactionId side)
        {
            int best = 0, atBest = 0;
            foreach (FactionId f in Factions) best = Math.Max(best, Kills(f));
            foreach (FactionId f in Factions) if (Kills(f) == best) atBest++;
            if (Kills(side) < best) return CrossfireOutcome.Lost;
            return atBest > 1 ? CrossfireOutcome.Tied : CrossfireOutcome.Won;
        }

        /// <summary>The next war starts empty.</summary>
        public void Clear()
        {
            m_Side.Clear();
            Array.Clear(m_Kills, 0, m_Kills.Length);
        }
    }

    /// <summary>
    /// The War Bot vote of a short side (owner, 16 Nov 2026): the side's real players are asked once, before the gates open; it passes when
    /// more of those who answered said yes than no. Nobody answering (the time ran out) is a no.
    /// </summary>
    public static class CrossfireVote
    {
        public static bool Passes(int yes, int no) => yes > 0 && yes > no;

        /// <summary>A side is offered the vote: it has real players, fewer than the cap with its bots, and the server runs War Bots.</summary>
        public static bool Offered(int real, int bots, int cap, bool botsRun) => botsRun && real > 0 && real + bots < cap;
    }

    /// <summary>The war's pay and its very rare kill drop (owner, 16 Nov 2026; the numbers are in <see cref="CrossfireSettings"/>).</summary>
    public static class CrossfireRewards
    {
        /// <summary>The war EXP a real player of a side with <paramref name="outcome"/> is paid (the winning side more than the others).</summary>
        public static int Experience(CrossfireSettings s, CrossfireOutcome outcome)
        {
            if (s == null) return 0;
            switch (outcome)
            {
                case CrossfireOutcome.Won: return Math.Max(0, s.winExperience);
                case CrossfireOutcome.Tied: return Math.Max(0, s.tieExperience);
                default: return Math.Max(0, s.loseExperience);
            }
        }

        /// <summary>
        /// The item a kill drops, or 0 for none: only a real player killing a real player (never with a War Bot on either side), with
        /// <see cref="CrossfireSettings.killDropChance"/>; <paramref name="roll"/> and <paramref name="pick"/> are uniform in [0, 1).
        /// </summary>
        public static ushort KillDrop(CrossfireSettings s, bool killerIsBot, bool victimIsBot, double roll, double pick)
        {
            if (s == null || killerIsBot || victimIsBot || s.killDropItemIds == null || s.killDropItemIds.Length == 0) return 0;
            if (!(roll < s.killDropChance)) return 0;
            int i = Math.Min(s.killDropItemIds.Length - 1, (int)(pick * s.killDropItemIds.Length));
            return s.killDropItemIds[i];
        }
    }
}
