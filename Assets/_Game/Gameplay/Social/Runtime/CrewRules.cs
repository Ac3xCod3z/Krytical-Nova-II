using System;
using System.Collections.Generic;
using System.Text;

namespace KryticalNova.Social
{
    /// <summary>
    /// The Crew rules, free of networking so they can be tested: what a Crew may be called, who may do what by
    /// rank, how a Crew fills, and what happens when people leave. A Crew has one Leader, a few Officers and
    /// Members; it belongs to one faction; a character is in one Crew at most.
    /// </summary>
    public static class CrewRules
    {
        public const string ReasonNotInCrew = "You are not in a Crew.";
        public const string ReasonAlreadyInCrew = "You are already in a Crew.";
        public const string ReasonLeaderOnly = "Only the Crew Leader can do that.";
        public const string ReasonOfficerOnly = "Only the Crew Leader and Officers can do that.";
        public const string ReasonNoSuchMember = "They are not in your Crew.";
        public const string ReasonLeaderMustHandOver = "Hand the Crew over to another member first, or disband it.";

        // ------------------------------------------------------------------ text

        /// <summary>One line with single spaces and no markup.</summary>
        private static string Tidy(string raw)
        {
            var text = new StringBuilder((raw ?? string.Empty).Length);
            bool space = false;
            foreach (char c in (raw ?? string.Empty).Trim())
            {
                if (c == '<' || c == '>' || char.IsControl(c) || char.IsWhiteSpace(c))
                {
                    space = text.Length > 0;
                    continue;
                }
                if (space) text.Append(' ');
                space = false;
                text.Append(c);
            }
            return text.ToString();
        }

        private static string Shorten(string text, int max) => text.Length > max ? text.Substring(0, Math.Max(0, max)).Trim() : text;

        /// <summary>Why a Crew cannot have this name (shown to the founder), or null. <paramref name="clean"/> is the name as it would be kept.</summary>
        public static string NameBlockedReason(string raw, int minLength, int maxLength, IEnumerable<string> taken, out string clean)
        {
            clean = Tidy(raw);
            if (clean.Length < minLength) return $"A Crew name needs at least {minLength} characters.";
            if (clean.Length > maxLength) return $"A Crew name has at most {maxLength} characters.";
            foreach (char c in clean)
                if (!char.IsLetterOrDigit(c) && c != ' ' && c != '\'' && c != '-') return "A Crew name uses letters, digits, spaces, ' and - only.";
            if (taken != null)
                foreach (string other in taken)
                    if (string.Equals(other, clean, StringComparison.OrdinalIgnoreCase)) return $"There is already a Crew called {other}.";
            return null;
        }

        public static string CleanTitle(string raw, int maxLength) => Shorten(Tidy(raw), maxLength);

        public static string CleanAnnouncement(string raw, int maxLength) => Shorten(Tidy(raw), maxLength);

        // ------------------------------------------------------------------ founding and size

        /// <summary>Why a character cannot found a Crew (shown to them), or null.</summary>
        public static string FoundBlockedReason(bool inCrew, int level, int founderLevel, long creds, long cost)
        {
            if (inCrew) return ReasonAlreadyInCrew;
            if (level < founderLevel) return $"Founding a Crew takes level {founderLevel}. You are level {level}.";
            if (creds < cost) return $"Founding a Crew costs {cost:N0} Creds.";
            return null;
        }

        /// <summary>How many members a Crew of a level may have.</summary>
        public static int MemberCap(int[] capByLevel, int crewLevel)
        {
            if (capByLevel == null || capByLevel.Length == 0) return 20;
            return Math.Max(1, capByLevel[Math.Min(capByLevel.Length - 1, Math.Max(0, crewLevel - 1))]);
        }

        public static CrewMember Find(Crew crew, string characterId)
        {
            if (crew == null || string.IsNullOrEmpty(characterId)) return null;
            foreach (CrewMember member in crew.members)
                if (member.characterId == characterId) return member;
            return null;
        }

        public static CrewMember FindByName(Crew crew, string name)
        {
            if (crew == null || string.IsNullOrEmpty(name)) return null;
            foreach (CrewMember member in crew.members)
                if (string.Equals(member.name, name, StringComparison.OrdinalIgnoreCase)) return member;
            return null;
        }

        public static int Count(Crew crew, CrewRank rank)
        {
            int count = 0;
            foreach (CrewMember member in crew.members)
                if (member.Rank == rank) count++;
            return count;
        }

        // ------------------------------------------------------------------ who may do what

        public static bool CanInvite(CrewRank actor) => actor >= CrewRank.Officer;
        public static bool CanAnnounce(CrewRank actor) => actor >= CrewRank.Officer;

        /// <summary>The Leader expels anyone but themselves; an Officer expels Members.</summary>
        public static bool CanExpel(CrewRank actor, CrewRank target) =>
            actor == CrewRank.Leader ? target != CrewRank.Leader : actor == CrewRank.Officer && target == CrewRank.Member;

        /// <summary>Why <paramref name="crew"/> cannot take another member of a faction (shown to the inviter), or null.</summary>
        public static string InviteBlockedReason(Crew crew, CrewRank actor, int memberCap, int targetFaction, bool targetInCrew, string targetName)
        {
            if (!CanInvite(actor)) return ReasonOfficerOnly;
            if (targetInCrew) return $"{targetName} is already in a Crew.";
            if (targetFaction != crew.faction) return OtherFaction(targetName);
            if (crew.members.Count >= memberCap) return $"Your Crew is full ({memberCap} members).";
            return null;
        }

        /// <summary>A Crew is one faction. No zone, rank or tool changes that.</summary>
        public static string OtherFaction(string targetName) => $"{targetName} is of another faction. A Crew only takes members of its own.";

        /// <summary>Why the Leader cannot make (or unmake) a member an Officer, or null.</summary>
        public static string OfficerBlockedReason(Crew crew, CrewRank actor, CrewMember target, bool makeOfficer, int maxOfficers)
        {
            if (actor != CrewRank.Leader) return ReasonLeaderOnly;
            if (target == null) return ReasonNoSuchMember;
            if (target.Rank == CrewRank.Leader) return "The Leader is not an Officer.";
            if (makeOfficer && target.Rank == CrewRank.Officer) return $"{target.name} is already an Officer.";
            if (!makeOfficer && target.Rank != CrewRank.Officer) return $"{target.name} is not an Officer.";
            if (makeOfficer && Count(crew, CrewRank.Officer) >= maxOfficers) return $"A Crew has at most {maxOfficers} Officers.";
            return null;
        }

        /// <summary>Why a member cannot simply leave, or null. The last one out closes the Crew; a Leader with others hands over first.</summary>
        public static string LeaveBlockedReason(CrewRank rank, int memberCount) =>
            rank == CrewRank.Leader && memberCount > 1 ? ReasonLeaderMustHandOver : null;

        // ------------------------------------------------------------------ changes

        public static Crew Found(string id, string name, int faction, int symbol, CrewMember founder, long now)
        {
            founder.rank = (int)CrewRank.Leader;
            founder.joinedUtc = now;
            founder.title = string.Empty;
            var crew = new Crew { id = id, name = name, faction = faction, symbol = symbol, foundedUtc = now };
            crew.members.Add(founder);
            return crew;
        }

        public static void Join(Crew crew, CrewMember member, long now)
        {
            member.rank = (int)CrewRank.Member;
            member.title = string.Empty;
            member.joinedUtc = now;
            crew.members.Add(member);
        }

        public static bool Remove(Crew crew, string characterId)
        {
            for (int i = crew.members.Count - 1; i >= 0; i--)
                if (crew.members[i].characterId == characterId) { crew.members.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>The Leader hands the Crew to another member and becomes an Officer (or a Member when there is no room).</summary>
        public static bool TryHandOver(Crew crew, string leaderId, string toId, int maxOfficers, out string reason)
        {
            reason = null;
            CrewMember leader = Find(crew, leaderId), heir = Find(crew, toId);
            if (leader == null || leader.Rank != CrewRank.Leader) { reason = ReasonLeaderOnly; return false; }
            if (heir == null || heir == leader) { reason = ReasonNoSuchMember; return false; }
            heir.rank = (int)CrewRank.Leader;
            leader.rank = (int)(Count(crew, CrewRank.Officer) < maxOfficers ? CrewRank.Officer : CrewRank.Member);
            return true;
        }

        /// <summary>Leader first, then Officers, then Members; within a rank whoever is online, then by name.</summary>
        public static int Compare(CrewRank rankA, bool onlineA, string nameA, CrewRank rankB, bool onlineB, string nameB)
        {
            int c = rankB.CompareTo(rankA);
            if (c != 0) return c;
            c = onlineB.CompareTo(onlineA);
            return c != 0 ? c : string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
        }

        public static string RankName(CrewRank rank) => rank == CrewRank.Leader ? "Leader" : rank == CrewRank.Officer ? "Officer" : "Member";
    }
}
