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

    /// <summary>What one real player is paid at a war's end (<see cref="CrossfireRewards.Payout"/>).</summary>
    public struct CrossfirePay
    {
        /// <summary>Character EXP (0 at the level cap: their share is in <see cref="Creds"/>).</summary>
        public int CharacterExp;
        /// <summary>% of the growth meter for the active pet and the selected mount (0 when there is none, or it is fully grown).</summary>
        public float PetPercent, MountPercent;
        /// <summary>Creds: the result's, plus the maxed shares'.</summary>
        public long Creds;
    }

    /// <summary>
    /// The war's pay and its very rare kill drop (owner, 16 Nov 2026; every number in <see cref="CrossfireSettings"/>). EXP for the
    /// character (a % of their level step), the active pet and the selected mount (a % of their growth meter); Creds by result and bracket
    /// (bracket n pays n / 10 of X's); anything already maxed pays a fixed share of that result's Creds instead of its EXP. War Bots are
    /// never paid. Pure.
    /// </summary>
    public static class CrossfireRewards
    {
        static int Index(CrossfireOutcome o) => o == CrossfireOutcome.Won ? 0 : o == CrossfireOutcome.Tied ? 1 : 2;

        static float Pick(UnityEngine.Vector3 v, CrossfireOutcome o) => Math.Max(0f, v[Index(o)]);

        /// <summary>The result's Creds in <paramref name="bracket"/> (before the maxed shares).</summary>
        public static long ResultCreds(CrossfireSettings s, CrossfireOutcome outcome, int bracket)
        {
            if (s == null || bracket <= 0) return 0;
            double share = outcome == CrossfireOutcome.Won ? 1.0 : outcome == CrossfireOutcome.Tied ? s.tieAndLossCredsShare.x : s.tieAndLossCredsShare.y;
            double scale = Math.Min(bracket, s.bracketCount) / (double)Math.Max(1, s.bracketCount);
            return (long)Math.Round(Math.Max(0L, s.topWinCreds) * Math.Max(0.0, share) * scale);
        }

        /// <param name="levelStepExp">The EXP of the character's current level step.</param>
        /// <param name="pet">The pet: -1 none out, 0 growing, 1 fully grown. <paramref name="mount"/> the same for the selected mount.</param>
        public static CrossfirePay Payout(CrossfireSettings s, CrossfireOutcome outcome, int bracket, bool characterMaxed, int levelStepExp, int pet, int mount)
        {
            var pay = new CrossfirePay();
            if (s == null) return pay;
            long creds = ResultCreds(s, outcome, bracket);
            pay.Creds = creds;
            if (characterMaxed) pay.Creds += (long)Math.Round(creds * Math.Max(0f, s.maxedCredsShare.x));
            else pay.CharacterExp = (int)Math.Round(Math.Max(0, levelStepExp) * Pick(s.characterExpPercent, outcome) / 100.0);
            float companion = Pick(s.companionExpPercent, outcome);
            if (pet == 1) pay.Creds += (long)Math.Round(creds * Math.Max(0f, s.maxedCredsShare.y));
            else if (pet == 0) pay.PetPercent = companion;
            if (mount == 1) pay.Creds += (long)Math.Round(creds * Math.Max(0f, s.maxedCredsShare.z));
            else if (mount == 0) pay.MountPercent = companion;
            return pay;
        }

        /// <summary>A late joiner's pay (owner: diminished; <see cref="CrossfireRules.PayShare"/>): every part times <paramref name="share"/> (0 to 1).</summary>
        public static CrossfirePay Scaled(in CrossfirePay pay, float share)
        {
            float k = Math.Max(0f, Math.Min(1f, share));
            if (k >= 1f) return pay;
            return new CrossfirePay
            {
                CharacterExp = (int)Math.Round(pay.CharacterExp * (double)k),
                PetPercent = pay.PetPercent * k,
                MountPercent = pay.MountPercent * k,
                Creds = (long)Math.Round(pay.Creds * (double)k),
            };
        }

        /// <summary>A kill drops: only a real player killing a real player (never with a War Bot on either side), with <see cref="CrossfireSettings.killDropChance"/>; <paramref name="roll"/> in [0, 1).</summary>
        public static bool KillDrops(CrossfireSettings s, bool killerIsBot, bool victimIsBot, double roll) =>
            KillDrops(s, killerIsBot, victimIsBot, roll, 1f);

        /// <summary><see cref="KillDrops(CrossfireSettings, bool, bool, double)"/> for a killer who earns <paramref name="share"/> of the war's pay (a late joiner's drop chance is diminished too).</summary>
        public static bool KillDrops(CrossfireSettings s, bool killerIsBot, bool victimIsBot, double roll, float share) =>
            s != null && !killerIsBot && !victimIsBot && roll < s.killDropChance * Math.Max(0f, Math.Min(1f, share));
    }
}
