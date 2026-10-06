using System;
using System.Collections.Generic;

namespace KryticalNova.Social
{
    /// <summary>
    /// The Contacts rules, free of networking so they can be tested. Contacts are mutual: one asks, the other
    /// accepts, and then each is in the other's book; removing takes both out. Blocking is one-sided and silent: it
    /// ends the contact, drops any request between the two, and (through <c>SocialGate</c>) stops whispers,
    /// invitations, trades and requests in both directions.
    /// </summary>
    public static class ContactRules
    {
        public const int MaxContacts = 100;
        public const int MaxBlocked = 100;
        public const int MaxPending = 30;
        public const int MaxNoteLength = 60;

        public const string ReasonSelf = "That is you.";
        public const string ReasonUnavailable = "That player isn't accepting requests from you.";
        public const string ReasonOtherFaction = "You can only add a member of another faction while you are both on neutral ground.";
        public const string ReasonListFull = "Your contact list is full.";
        public const string ReasonTheirListFull = "Their contact list is full.";
        public const string ReasonTooManyRequests = "They have too many requests waiting. Try again later.";
        public const string ReasonTooManySent = "You have too many requests waiting for an answer.";
        public const string ReasonBlockListFull = "Your block list is full.";
        public const string ReasonNoRequest = "There is no such request.";

        public static ContactEntry Find(List<ContactEntry> list, string characterId)
        {
            if (list == null || string.IsNullOrEmpty(characterId)) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i].characterId == characterId) return list[i];
            return null;
        }

        private static bool Remove(List<ContactEntry> list, string characterId)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].characterId == characterId) { list.RemoveAt(i); return true; }
            return false;
        }

        private static ContactEntry Entry(ContactBook of, long now) =>
            new ContactEntry { characterId = of.characterId, name = of.name, sinceUtc = now };

        public static bool AreContacts(ContactBook a, ContactBook b) => Find(a.contacts, b.characterId) != null;

        /// <summary>True when either has blocked the other.</summary>
        public static bool Blocks(ContactBook a, ContactBook b) =>
            a != null && b != null && (Find(a.blocked, b.characterId) != null || Find(b.blocked, a.characterId) != null);

        /// <summary>Why <paramref name="from"/> may not ask <paramref name="to"/> (shown to the asker), or null.</summary>
        /// <param name="crossFaction">True when the two stand on neutral ground together: only there may factions befriend each other.</param>
        public static string RequestBlockedReason(ContactBook from, ContactBook to, bool crossFaction)
        {
            if (from.characterId == to.characterId) return ReasonSelf;
            if (AreContacts(from, to)) return $"{to.name} is already a contact.";
            if (Blocks(from, to)) return Find(from.blocked, to.characterId) != null ? $"You have blocked {to.name}. Unblock them first." : ReasonUnavailable;
            if (!crossFaction && from.faction != to.faction) return ReasonOtherFaction;
            if (from.contacts.Count >= MaxContacts) return ReasonListFull;
            if (Find(from.outgoing, to.characterId) != null) return $"You already asked {to.name}.";
            if (Find(from.incoming, to.characterId) != null) return null; // they asked first: asking back accepts
            if (to.contacts.Count >= MaxContacts) return ReasonTheirListFull;
            if (from.outgoing.Count >= MaxPending) return ReasonTooManySent;
            if (to.incoming.Count >= MaxPending) return ReasonTooManyRequests;
            return null;
        }

        /// <summary>
        /// <paramref name="from"/> asks <paramref name="to"/>. When <paramref name="to"/> had already asked, the two
        /// become contacts at once (<paramref name="becameContacts"/>).
        /// </summary>
        public static bool TryRequest(ContactBook from, ContactBook to, long now, bool crossFaction, out string reason, out bool becameContacts)
        {
            becameContacts = false;
            reason = RequestBlockedReason(from, to, crossFaction);
            if (reason != null) return false;
            if (Find(from.incoming, to.characterId) != null)
            {
                becameContacts = TryAccept(from, to, now, out reason);
                return becameContacts;
            }
            from.outgoing.Add(Entry(to, now));
            to.incoming.Add(Entry(from, now));
            return true;
        }

        /// <summary><paramref name="me"/> accepts the request <paramref name="from"/> sent: each joins the other's contacts.</summary>
        public static bool TryAccept(ContactBook me, ContactBook from, long now, out string reason)
        {
            reason = null;
            if (Find(me.incoming, from.characterId) == null) { reason = ReasonNoRequest; return false; }
            if (Blocks(me, from)) { DropRequests(me, from); reason = ReasonUnavailable; return false; }
            if (me.contacts.Count >= MaxContacts) { reason = ReasonListFull; return false; }
            if (from.contacts.Count >= MaxContacts) { reason = ReasonTheirListFull; return false; }
            DropRequests(me, from);
            if (Find(me.contacts, from.characterId) == null) me.contacts.Add(Entry(from, now));
            if (Find(from.contacts, me.characterId) == null) from.contacts.Add(Entry(me, now));
            return true;
        }

        /// <summary>Takes any request between the two away, whichever way it went. True when there was one.</summary>
        public static bool DropRequests(ContactBook a, ContactBook b)
        {
            bool any = Remove(a.incoming, b.characterId);
            any |= Remove(a.outgoing, b.characterId);
            any |= Remove(b.incoming, a.characterId);
            any |= Remove(b.outgoing, a.characterId);
            return any;
        }

        /// <summary>Ends a contact, on both sides. True when they were contacts.</summary>
        public static bool RemoveContact(ContactBook a, ContactBook b)
        {
            bool any = Remove(a.contacts, b.characterId);
            any |= Remove(b.contacts, a.characterId);
            return any;
        }

        /// <summary><paramref name="me"/> blocks <paramref name="other"/>: the contact and any request between them end too.</summary>
        public static bool TryBlock(ContactBook me, ContactBook other, long now, out string reason)
        {
            reason = null;
            if (me.characterId == other.characterId) { reason = ReasonSelf; return false; }
            if (Find(me.blocked, other.characterId) != null) { reason = $"{other.name} is already blocked."; return false; }
            if (me.blocked.Count >= MaxBlocked) { reason = ReasonBlockListFull; return false; }
            RemoveContact(me, other);
            DropRequests(me, other);
            me.blocked.Add(Entry(other, now));
            return true;
        }

        public static bool Unblock(ContactBook me, string characterId) => Remove(me.blocked, characterId);

        public static bool SetFavourite(ContactBook me, string characterId, bool favourite)
        {
            ContactEntry entry = Find(me.contacts, characterId);
            if (entry == null || entry.favourite == favourite) return false;
            entry.favourite = favourite;
            return true;
        }

        public static bool SetNote(ContactBook me, string characterId, string note)
        {
            ContactEntry entry = Find(me.contacts, characterId);
            if (entry == null) return false;
            string clean = CleanNote(note);
            if (entry.note == clean) return false;
            entry.note = clean;
            return true;
        }

        /// <summary>A note is one short line of plain text.</summary>
        public static string CleanNote(string note)
        {
            string clean = (note ?? string.Empty).Replace('\n', ' ').Replace('\r', ' ').Replace('<', ' ').Replace('>', ' ').Trim();
            return clean.Length > MaxNoteLength ? clean.Substring(0, MaxNoteLength).Trim() : clean;
        }

        /// <summary>"Online", "5m ago", "3h ago", "2d ago".</summary>
        public static string LastSeen(bool online, long nowTicks, long lastSeenTicks)
        {
            if (online) return "Online";
            if (lastSeenTicks <= 0) return "Offline";
            TimeSpan ago = TimeSpan.FromTicks(Math.Max(0, nowTicks - lastSeenTicks));
            if (ago.TotalDays >= 1) return $"{(int)ago.TotalDays}d ago";
            if (ago.TotalHours >= 1) return $"{(int)ago.TotalHours}h ago";
            return ago.TotalMinutes >= 1 ? $"{(int)ago.TotalMinutes}m ago" : "Just now";
        }
    }
}
