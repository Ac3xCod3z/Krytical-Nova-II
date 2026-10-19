using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Persistence;
using Unity.Collections;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// Every character's contact book on the server. Books are a world document (<see cref="ServerSave"/>), so a
    /// request reaches a character who is offline and both sides of a contact stay in step. Runs only where the
    /// game is decided; players reach it through <see cref="PlayerContacts"/>. Also answers <see cref="SocialGate"/>:
    /// a block here stops every other kind of request between the two.
    /// </summary>
    public static class ContactsService
    {
        public const string WorldName = "contacts";

        private static readonly Dictionary<string, PlayerContacts> OnlinePlayers = new Dictionary<string, PlayerContacts>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ContactBook> Index = new Dictionary<string, ContactBook>(StringComparer.Ordinal);
        private static ContactsWorld indexed;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            OnlinePlayers.Clear();
            Index.Clear();
            indexed = null;
        }

        // SocialGate forgets its check at SubsystemRegistration; this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => SocialGate.Check = GateCheck;

        private static ContactsWorld World
        {
            get
            {
                ContactsWorld world = ServerSave.World<ContactsWorld>(WorldName);
                if (ReferenceEquals(world, indexed)) return world;
                // Another store (a wipe, a test): the books are different objects now.
                indexed = world;
                Index.Clear();
                foreach (ContactBook book in world.books)
                    if (!string.IsNullOrEmpty(book.characterId)) Index[book.characterId] = book;
                return world;
            }
        }

        private static long Now => DateTime.UtcNow.Ticks;
        private static void Save() => ServerSave.SaveWorld(WorldName);

        /// <summary>The book of a character, or null when the server has never seen them.</summary>
        public static ContactBook Book(string characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            _ = World;
            return Index.TryGetValue(characterId, out ContactBook book) ? book : null;
        }

        public static bool IsOnline(string characterId) =>
            !string.IsNullOrEmpty(characterId) && OnlinePlayers.TryGetValue(characterId, out PlayerContacts player) && player != null;

        public static PlayerContacts Online(string characterId) =>
            !string.IsNullOrEmpty(characterId) && OnlinePlayers.TryGetValue(characterId, out PlayerContacts player) ? player : null;

        private static string GateCheck(Component from, Component to)
        {
            ContactBook a = BookOf(from), b = BookOf(to);
            return ContactRules.Blocks(a, b) ? ContactRules.ReasonUnavailable : null;
        }

        private static ContactBook BookOf(Component player)
        {
            var contacts = player != null ? player.GetComponent<PlayerContacts>() : null;
            return contacts != null ? Book(contacts.CharacterId) : null;
        }

        // ------------------------------------------------------------------ coming and going

        /// <summary>Server: a character came into the world. Its book is made or brought up to date, and its contacts hear of it.</summary>
        public static void ServerArrived(PlayerContacts player)
        {
            string id = player.CharacterId;
            if (string.IsNullOrEmpty(id)) return;
            ContactBook book = Book(id);
            if (book == null)
            {
                book = new ContactBook { characterId = id };
                World.books.Add(book);
                Index[id] = book;
            }
            Describe(book, player);
            OnlinePlayers[id] = player;
            Save();

            foreach (ContactEntry contact in book.contacts)
            {
                PlayerContacts other = Online(contact.characterId);
                if (other == null) continue;
                other.Tell($"{book.name} is online.");
                other.ServerPushView();
            }
            if (book.incoming.Count > 0)
                player.Tell(book.incoming.Count == 1
                    ? $"{book.incoming[0].name} wants to add you as a contact. Open Contacts [F] to answer."
                    : $"{book.incoming.Count} contact requests are waiting. Open Contacts [F] to answer.");
            player.ServerPushView();
        }

        /// <summary>Server: a character left the world.</summary>
        public static void ServerLeft(PlayerContacts player)
        {
            string id = player.CharacterId;
            if (string.IsNullOrEmpty(id) || !OnlinePlayers.TryGetValue(id, out PlayerContacts known) || known != player) return;
            OnlinePlayers.Remove(id);
            ContactBook book = Book(id);
            if (book == null) return;
            Describe(book, player);
            Save();
            foreach (ContactEntry contact in book.contacts)
            {
                PlayerContacts other = Online(contact.characterId);
                if (other == null) continue;
                other.Tell($"{book.name} went offline.");
                other.ServerPushView();
            }
        }

        private static void Describe(ContactBook book, PlayerContacts player)
        {
            book.name = player.ContactName;
            book.faction = (int)player.Faction;
            book.level = player.Level;
            book.weaponClass = player.WeaponClass;
            book.lastSeenUtc = Now;
        }

        // ------------------------------------------------------------------ finding people

        /// <summary>The books of characters with this name (any case), the asker's own left out.</summary>
        private static void FindByName(string name, string exceptId, List<ContactBook> found)
        {
            found.Clear();
            name = ChatRouting.Clean(name);
            if (name.Length == 0) return;
            foreach (ContactBook book in World.books)
                if (book.characterId != exceptId && string.Equals(book.name, name, StringComparison.OrdinalIgnoreCase)) found.Add(book);
        }

        private static readonly List<ContactBook> Found = new List<ContactBook>(4);

        /// <summary>One character by name: the online one when several share it. Tells the asker when there is none or no telling.</summary>
        private static ContactBook Resolve(PlayerContacts asker, string name)
        {
            FindByName(name, asker.CharacterId, Found);
            if (Found.Count == 0) { asker.Notify($"No character named '{ChatRouting.Clean(name)}' is known here."); return null; }
            if (Found.Count == 1) return Found[0];
            ContactBook online = null;
            int onlineCount = 0;
            foreach (ContactBook book in Found)
                if (IsOnline(book.characterId)) { online = book; onlineCount++; }
            if (onlineCount == 1) return online;
            asker.Notify("Several characters share that name. Use the player menu on the one you mean (Shift+click them).");
            return null;
        }

        private static bool TryMine(PlayerContacts player, out ContactBook mine)
        {
            mine = Book(player.CharacterId);
            if (mine != null) return true;
            player.Notify("Contacts need a saved character.");
            return false;
        }

        // ------------------------------------------------------------------ requests

        public static void ServerRequestByName(PlayerContacts from, string name)
        {
            if (!TryMine(from, out _)) return;
            ContactBook to = Resolve(from, name);
            if (to != null) ServerRequest(from, to);
        }

        public static void ServerRequest(PlayerContacts from, ContactBook to)
        {
            if (to == null || !TryMine(from, out ContactBook mine)) return;
            // Members of different factions add each other face to face on neutral ground, nowhere else: so the
            // other one has to be in the world, and both have to be standing on it.
            PlayerContacts met = Online(to.characterId);
            bool neutralGround = met != null && KryticalNova.World.TradeZone.CrossFactionAt(from.transform.position)
                                 && KryticalNova.World.TradeZone.CrossFactionAt(met.transform.position);
            if (!ContactRules.TryRequest(mine, to, Now, neutralGround, out string reason, out bool becameContacts))
            {
                from.Notify(reason);
                return;
            }
            Save();
            PlayerContacts other = Online(to.characterId);
            if (becameContacts)
            {
                from.Tell($"{to.name} is now a contact.");
                if (other != null) other.Tell($"{mine.name} is now a contact.");
            }
            else
            {
                from.Tell($"You asked {to.name} to be a contact.");
                if (other != null)
                {
                    other.Tell($"{mine.name} wants to add you as a contact. Answer in Contacts [F].");
                    other.ServerPrompt(mine.characterId, mine.name);
                }
            }
            from.ServerPushView();
            if (other != null) other.ServerPushView();
        }

        public static void ServerRespond(PlayerContacts player, string fromId, bool accept)
        {
            if (!TryMine(player, out ContactBook mine)) return;
            ContactBook from = Book(fromId);
            if (from == null || ContactRules.Find(mine.incoming, fromId) == null) { player.Notify(ContactRules.ReasonNoRequest); return; }
            PlayerContacts other = Online(fromId);
            if (accept)
            {
                if (!ContactRules.TryAccept(mine, from, Now, out string reason)) { player.Notify(reason); Save(); player.ServerPushView(); return; }
                player.Tell($"{from.name} is now a contact.");
                if (other != null) other.Tell($"{mine.name} accepted your contact request.");
            }
            else
            {
                ContactRules.DropRequests(mine, from);
                player.Tell($"You declined {from.name}'s contact request.");
            }
            Save();
            player.ServerPushView();
            if (other != null) other.ServerPushView();
        }

        /// <summary>Takes back a request the player sent.</summary>
        public static void ServerCancel(PlayerContacts player, string toId)
        {
            if (!TryMine(player, out ContactBook mine)) return;
            ContactBook to = Book(toId);
            if (to == null || ContactRules.Find(mine.outgoing, toId) == null) { player.Notify(ContactRules.ReasonNoRequest); return; }
            ContactRules.DropRequests(mine, to);
            Save();
            player.ServerPushView();
            PlayerContacts other = Online(toId);
            if (other == null) return;
            other.ServerClosePrompt(mine.characterId);
            other.ServerPushView();
        }

        // ------------------------------------------------------------------ the list

        public static void ServerRemove(PlayerContacts player, string otherId)
        {
            if (!TryMine(player, out ContactBook mine)) return;
            ContactBook other = Book(otherId);
            if (other == null || !ContactRules.RemoveContact(mine, other)) { player.Notify("They are not a contact."); return; }
            Save();
            player.Tell($"{other.name} is no longer a contact.");
            player.ServerPushView();
            PlayerContacts theirs = Online(otherId);
            if (theirs != null) theirs.ServerPushView();
        }

        public static void ServerBlockByName(PlayerContacts player, string name)
        {
            if (!TryMine(player, out _)) return;
            ContactBook other = Resolve(player, name);
            if (other != null) ServerBlock(player, other.characterId);
        }

        public static void ServerBlock(PlayerContacts player, string otherId)
        {
            if (!TryMine(player, out ContactBook mine)) return;
            ContactBook other = Book(otherId);
            if (other == null) { player.Notify("That character is not known here."); return; }
            if (!ContactRules.TryBlock(mine, other, Now, out string reason)) { player.Notify(reason); return; }
            Save();
            player.Tell($"{other.name} is blocked: no whispers, invitations, trades or requests from them will reach you.");
            player.ServerPushView();
            PlayerContacts theirs = Online(otherId);
            if (theirs == null) return;
            theirs.ServerClosePrompt(mine.characterId);
            theirs.ServerPushView(); // they only see the contact and requests gone: a block is never announced
        }

        public static void ServerUnblock(PlayerContacts player, string otherId)
        {
            if (!TryMine(player, out ContactBook mine)) return;
            ContactEntry entry = ContactRules.Find(mine.blocked, otherId);
            if (entry == null || !ContactRules.Unblock(mine, otherId)) { player.Notify("They are not blocked."); return; }
            Save();
            player.Tell($"{entry.name} is no longer blocked.");
            player.ServerPushView();
        }

        public static void ServerUnblockByName(PlayerContacts player, string name)
        {
            if (!TryMine(player, out ContactBook mine)) return;
            name = ChatRouting.Clean(name);
            foreach (ContactEntry entry in mine.blocked)
            {
                if (!string.Equals(entry.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                ServerUnblock(player, entry.characterId);
                return;
            }
            player.Notify($"Nobody named '{name}' is blocked.");
        }

        public static void ServerRemoveByName(PlayerContacts player, string name)
        {
            if (!TryMine(player, out ContactBook mine)) return;
            name = ChatRouting.Clean(name);
            foreach (ContactEntry entry in mine.contacts)
            {
                if (!string.Equals(entry.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                ServerRemove(player, entry.characterId);
                return;
            }
            player.Notify($"No contact is named '{name}'.");
        }

        public static void ServerSetFavourite(PlayerContacts player, string otherId, bool favourite)
        {
            if (!TryMine(player, out ContactBook mine) || !ContactRules.SetFavourite(mine, otherId, favourite)) return;
            Save();
            player.ServerPushView();
        }

        public static void ServerSetNote(PlayerContacts player, string otherId, string note)
        {
            if (!TryMine(player, out ContactBook mine) || !ContactRules.SetNote(mine, otherId, note)) return;
            Save();
            player.ServerPushView();
        }

        // ------------------------------------------------------------------ what the owner sees

        /// <summary>Server: every line of a character's Contacts window, as things stand now.</summary>
        public static ContactView[] ServerView(string characterId)
        {
            ContactBook mine = Book(characterId);
            if (mine == null) return Array.Empty<ContactView>();
            var lines = new List<ContactView>(mine.contacts.Count + mine.incoming.Count + mine.outgoing.Count + mine.blocked.Count);
            Add(lines, mine.contacts, ContactKind.Contact);
            Add(lines, mine.incoming, ContactKind.Incoming);
            Add(lines, mine.outgoing, ContactKind.Outgoing);
            Add(lines, mine.blocked, ContactKind.Blocked);
            return lines.ToArray();
        }

        private static void Add(List<ContactView> lines, List<ContactEntry> entries, ContactKind kind)
        {
            foreach (ContactEntry entry in entries)
            {
                ContactBook theirs = Book(entry.characterId);
                // A blocked character's whereabouts are nobody's business; neither are a stranger's.
                bool known = kind == ContactKind.Contact && theirs != null;
                if (theirs != null && !string.IsNullOrEmpty(theirs.name)) entry.name = theirs.name;
                var line = new ContactView
                {
                    kind = kind,
                    online = known && IsOnline(entry.characterId),
                    level = (ushort)Mathf.Clamp(theirs != null ? theirs.level : 1, 1, ushort.MaxValue),
                    faction = (byte)(theirs != null ? theirs.faction : 0),
                    weaponClass = (byte)(theirs != null ? theirs.weaponClass : 0),
                    favourite = entry.favourite,
                    lastSeenUtc = known ? theirs.lastSeenUtc : 0,
                };
                line.id.CopyFromTruncated(entry.characterId ?? string.Empty);
                line.name.CopyFromTruncated(entry.name ?? string.Empty);
                line.note.CopyFromTruncated(entry.note ?? string.Empty);
                lines.Add(line);
            }
        }
    }
}
