using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Player;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Persistence
{
    /// <summary>
    /// The roster on the server (online phase 4, milestone 5): which characters an account has is the server's word, not
    /// the player's PC's. A game lists, creates and deletes its characters by asking (<see cref="RosterRequest"/>, carried
    /// by <see cref="RosterLink"/>); the server checks every name and look, and names are unique across the whole game,
    /// capitals ignored (owner, 19 Oct 2026). A character the server does not have cannot play on it
    /// (<see cref="PlayerSaveAgent"/>).
    /// <para>The names in use are one world document of entries (<see cref="NamesDocument"/>), one entry a name. It is
    /// made once from every saved account the first time it is needed. Where two characters already shared a name, one
    /// keeps it: a character of a GM's account first (the GM Tolan stays Tolan, owner), then the higher level, then the
    /// one played least recently; the others are marked to be renamed before they play again.</para>
    /// </summary>
    public sealed class ServerRoster : IServerRoster
    {
        public const string NamesDocument = "names";

        /// <summary>The rules a look is checked by; from the world's player prefab unless set (tests).</summary>
        public static CharacterCreationDatabase Database { get; set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Database = null;

        public string Answer(string account, string oldAccount, string request) =>
            Handle(account, oldAccount, RosterRequest.FromJson(request)).ToJson();

        /// <summary>The answer to one request from <paramref name="account"/> (proven by the caller).</summary>
        public static RosterReply Handle(string account, string oldAccount, RosterRequest request)
        {
            if (!ServerSave.HasAuthority) return RosterReply.No("This game does not keep characters.");
            if (!SaveKeys.IsId(account)) return RosterReply.No("The server could not tell who you are. Join again.");
            if (request == null) return RosterReply.No("The server could not read that request.");
            // The characters this game played before Unity sign-in move to its proven account once, as on joining.
            if (SaveKeys.IsId(oldAccount) && oldAccount != account) ServerSave.CarryOverOnce(oldAccount, account);
            Names(); // made once, before any list, so a character that must be renamed shows it

            switch (request.op)
            {
                case RosterRequest.List: return List(account, request, CarryLeftBehind(account, request.carry));
                case RosterRequest.Create: return Create(account, request);
                case RosterRequest.Delete: return Delete(account, request);
                case RosterRequest.Rename: return Rename(account, request);
                default: return RosterReply.No("The server does not know that request.");
            }
        }

        // ------------------------------------------------------------------ the requests

        private static RosterReply List(string account, RosterRequest request, string note)
        {
            AccountSave saved = ServerSave.LiveAccount(account) ?? ServerSave.ReadAccountFrom(ServerSave.Store, account);
            if (saved != null && request.looks != null && request.looks.Count > 0) saved = TakeMissingLooks(account, saved, request.looks) ?? saved;
            return Listing(saved, true, note);
        }

        // ------------------------------------------------------------------ moving characters between accounts

        /// <summary>
        /// Who a sign-in proves (its Unity player id), or null. A dedicated server checks it against Unity's keys; tests set
        /// their own.
        /// </summary>
        public static Func<string, string> ProvePlayer = token =>
            UnityKeys.Ready && UnityToken.Verify(token, UnityKeys.Current, Application.cloudProjectId, DateTime.UtcNow, out string player, out _) ? player : null;

        /// <summary>
        /// Every character of one account to another, with the names they hold, as one change (see
        /// <see cref="ServerSave.MoveAccount"/>). Onto an account with characters of its own they join them while the
        /// slots allow; of the documents both accounts share (the Deep Vault, the Holoweave looks, the trade allowance),
        /// <paramref name="keepOld"/> says whose are kept, and the ones put aside are kept as backup copies. Null when they
        /// moved; otherwise why not.
        /// </summary>
        public static string MoveCharacters(string fromAccount, string toAccount, bool keepOld = true)
        {
            NameIndex names = Names();
            string why = ServerSave.MoveAccount(fromAccount, toAccount, (from, to) => Merge(from, to, keepOld),
                () => names.Reassign(fromAccount, toAccount), NamesDocument);
            if (why == null) ServerRecords.Write(RecordKind.Access, $"Characters of account {fromAccount} moved to account {toAccount}");
            return why;
        }

        /// <summary>Where a shared document put aside by a move is kept.</summary>
        public static string SetAsideKey(string account, string part, long ticks)
        {
            var clean = new System.Text.StringBuilder();
            foreach (char c in (part ?? string.Empty).ToLowerInvariant())
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_') clean.Append(c);
            return "accounts-set-aside/" + account + "/" + clean + "-" + ticks;
        }

        private static string Merge(AccountSave from, AccountSave to, bool keepOld)
        {
            int total = to.characters.Count + from.characters.Count;
            if (total > CharacterRoster.SlotCount)
                return $"Account {to.accountId} has {to.characters.Count} character(s); {from.characters.Count} more would not fit in its " +
                       $"{CharacterRoster.SlotCount} slots. Delete one there first.";
            to.characters.AddRange(from.characters); // theirs keep their slots; the moved ones take the free ones
            AssignSlots(to);
            long now = DateTime.UtcNow.Ticks;
            foreach (SavePart part in from.parts ?? new List<SavePart>())
            {
                if (part == null || string.IsNullOrEmpty(part.key)) continue;
                string theirs = to.Get(part.key);
                if (theirs == null) { to.Set(part.key, part.json); continue; }
                if (theirs == part.json) continue;
                string setAside = keepOld ? theirs : part.json;
                ServerSave.Store.Write(SetAsideKey(to.accountId, part.key, now), setAside);
                if (keepOld) to.Set(part.key, part.json);
            }
            return null;
        }

        /// <summary>
        /// A game whose sign-in opened a player other than the one this PC played (the account was linked to another player
        /// first) sends the old player's sign-in along once: its characters come to the account it plays now, when that
        /// account has none (owner, 22 Oct 2026: "so that we can fix this issue for others"). What to tell the player, or null.
        /// </summary>
        private static string CarryLeftBehind(string account, string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            string player = ProvePlayer(token);
            if (string.IsNullOrEmpty(player)) return null; // an old or foreign sign-in: nothing to carry
            string old = AccountIds.FromUnityPlayer(player);
            if (old == account || !ServerSave.Store.TryRead(ServerSave.AccountsPrefix + old, out _)) return null;
            AccountSave before = ServerSave.ReadAccountFrom(ServerSave.Store, old);
            int count = before?.characters?.Count ?? 0;
            if (count == 0) return null;
            string why = MoveCharacters(old, account);
            if (why == null) return count == 1 ? "Your character from this PC is on your account now." : $"Your {count} characters from this PC are on your account now.";
            ServerRecords.Write(RecordKind.Access, $"Characters of account {old} were not moved to account {account}: {why}");
            return "The characters this PC played before you signed in could not be brought to this account (its character slots are full, or one is in the world). A GM can move them for you.";
        }

        private static RosterReply Create(string account, RosterRequest request)
        {
            if (!CharacterNameRules.Validate(request.name, out string name, out string reason)) return RosterReply.No(reason);
            CharacterCreationDatabase rules = LookRules();
            if (rules == null) return RosterReply.No("The server cannot check looks right now. Try again in a moment.");
            CharacterRecipe recipe = request.recipe.Clone();
            if (!CharacterRecipeValidator.Validate(recipe, rules, out reason)) return RosterReply.No("The server did not take that look: " + reason);
            NameIndex names = Names();
            if (names.Holder(name) != null) return RosterReply.No(CharacterNameRules.ReasonTakenInGame);
            if (!ServerSave.TryHoldOffline(account, out string refusal)) return RosterReply.No(refusal);
            try
            {
                AccountSave saved = ServerSave.LoadAccount(account);
                AssignSlots(saved);
                int slot = FreeSlot(saved, request.slot);
                if (slot < 0) return Listing(saved, false, $"All {CharacterRoster.SlotCount} character slots are used. Delete one first.");
                long now = DateTime.UtcNow.Ticks;
                var character = new CharacterSave
                {
                    characterId = Guid.NewGuid().ToString("N"), name = name, faction = (int)recipe.faction, level = 1,
                    slot = slot, created = now, savedUtc = now,
                };
                character.Set(PlayerIdentity.LookSaveKey, PlayerIdentity.LookJson(recipe));
                saved.characters.Add(character);
                names.Put(name, account, character.characterId);
                ServerSave.WriteTogether(new[] { saved }, NamesDocument);
                ServerRecords.Write(RecordKind.Access, $"Character {name} ({character.characterId}) created (account {account})");
                RosterReply reply = Listing(saved, true);
                reply.characterId = character.characterId;
                return reply;
            }
            finally
            {
                ServerSave.ReleaseOffline(account);
            }
        }

        private static RosterReply Delete(string account, RosterRequest request)
        {
            if (!SaveKeys.IsId(request.characterId)) return RosterReply.No("Choose a character to delete.");
            if (!ServerSave.TryHoldOffline(account, out string refusal)) return RosterReply.No(refusal);
            try
            {
                AccountSave saved = ServerSave.LoadAccount(account);
                CharacterSave character = saved.Find(request.characterId);
                if (character == null) return Listing(saved, false, "That character is not on this server.");
                saved.characters.Remove(character);
                Names().Drop(character.name, account, character.characterId);
                // Everything it carried goes with it: one line in the kept ledger, saved with the change.
                ServerSave.Ledger(LedgerEntry.For(LedgerKinds.CharacterDeleted, account, character.characterId, character.name));
                ServerSave.WriteTogether(new[] { saved }, NamesDocument);
                ServerRecords.Write(RecordKind.Access, $"Character {character.name} ({character.characterId}, level {character.level}) deleted (account {account})");
                return Listing(saved, true);
            }
            finally
            {
                ServerSave.ReleaseOffline(account);
            }
        }

        private static RosterReply Rename(string account, RosterRequest request)
        {
            if (!SaveKeys.IsId(request.characterId)) return RosterReply.No("Choose a character to rename.");
            if (!CharacterNameRules.Validate(request.name, out string name, out string reason)) return RosterReply.No(reason);
            NameIndex names = Names();
            if (!ServerSave.TryHoldOffline(account, out string refusal)) return RosterReply.No(refusal);
            try
            {
                AccountSave saved = ServerSave.LoadAccount(account);
                CharacterSave character = saved.Find(request.characterId);
                if (character == null) return Listing(saved, false, "That character is not on this server.");
                // Only a character that lost its name when names became unique is renamed here.
                if (!character.rename) return Listing(saved, false, "Only a character asked to choose a new name can be renamed.");
                NameIndex.Entry holder = names.Holder(name);
                if (holder != null && holder.character != character.characterId) return Listing(saved, false, CharacterNameRules.ReasonTakenInGame);
                string old = character.name;
                character.name = name;
                character.rename = false;
                names.Put(name, account, character.characterId);
                ServerSave.WriteTogether(new[] { saved }, NamesDocument);
                ServerRecords.Write(RecordKind.Access, $"Character {old} ({character.characterId}) renamed {name} (account {account})");
                return Listing(saved, true);
            }
            finally
            {
                ServerSave.ReleaseOffline(account);
            }
        }

        // A character made before looks were saved has none on the server: the look this PC kept of it is taken, checked
        // like a new one's. Only while the account is not in the world. The account as written, or null when nothing changed.
        private static AccountSave TakeMissingLooks(string account, AccountSave saved, List<RosterLook> looks)
        {
            CharacterCreationDatabase rules = LookRules();
            if (rules == null) return null;
            var wanted = new Dictionary<string, CharacterRecipe>(StringComparer.Ordinal);
            foreach (CharacterSave character in saved.characters)
            {
                if (character == null || character.Get(PlayerIdentity.LookSaveKey) != null) continue;
                foreach (RosterLook look in looks)
                    if (look != null && look.id == character.characterId && CharacterRecipeValidator.Validate(look.recipe, rules, out _)
                        && (int)look.recipe.faction == character.faction)
                        wanted[character.characterId] = look.recipe;
            }
            if (wanted.Count == 0 || !ServerSave.TryHoldOffline(account, out _)) return null;
            try
            {
                AccountSave live = ServerSave.LoadAccount(account);
                foreach (KeyValuePair<string, CharacterRecipe> look in wanted)
                {
                    CharacterSave character = live.Find(look.Key);
                    if (character != null && character.Get(PlayerIdentity.LookSaveKey) == null)
                        character.Set(PlayerIdentity.LookSaveKey, PlayerIdentity.LookJson(look.Value));
                }
                ServerSave.WriteAccount(live);
                return live;
            }
            finally
            {
                ServerSave.ReleaseOffline(account);
            }
        }

        // ------------------------------------------------------------------ the list the game shows

        private static RosterReply Listing(AccountSave saved, bool ok, string message = null)
        {
            var reply = new RosterReply { ok = ok, message = message ?? string.Empty };
            if (saved == null) return reply;
            int unslotted = AssignSlots(saved);
            foreach (CharacterSave character in saved.characters)
            {
                if (character == null || character.slot < 0) continue;
                PlayerIdentity.TryReadLook(character.Get(PlayerIdentity.LookSaveKey), out CharacterRecipe recipe);
                reply.characters.Add(new RosterCharacter
                {
                    id = character.characterId, slot = character.slot, name = character.name, level = Mathf.Max(1, character.level),
                    recipe = recipe, created = character.created, lastPlayed = character.savedUtc, mustRename = character.rename,
                });
            }
            if (ok && unslotted > 0)
                reply.message = $"{unslotted} more character(s) of yours are kept on the server but have no slot. Delete one to see them.";
            return reply;
        }

        /// <summary>
        /// Gives every character a slot of the select screen: the one it has, else the first free one, the most recently
        /// played first. Returns how many found no free slot (an account that had more characters than slots).
        /// </summary>
        public static int AssignSlots(AccountSave saved)
        {
            var taken = new bool[CharacterRoster.SlotCount];
            var waiting = new List<CharacterSave>();
            foreach (CharacterSave character in saved.characters)
            {
                if (character == null) continue;
                if (character.slot >= 0 && character.slot < taken.Length && !taken[character.slot]) taken[character.slot] = true;
                else waiting.Add(character);
            }
            waiting.Sort((a, b) => b.savedUtc.CompareTo(a.savedUtc));
            int left = 0;
            foreach (CharacterSave character in waiting)
            {
                character.slot = Array.IndexOf(taken, false);
                if (character.slot < 0) { left++; continue; }
                taken[character.slot] = true;
            }
            return left;
        }

        private static int FreeSlot(AccountSave saved, int wanted)
        {
            var taken = new bool[CharacterRoster.SlotCount];
            foreach (CharacterSave character in saved.characters)
                if (character != null && character.slot >= 0 && character.slot < taken.Length) taken[character.slot] = true;
            if (wanted >= 0 && wanted < taken.Length && !taken[wanted]) return wanted;
            return Array.IndexOf(taken, false);
        }

        private static CharacterCreationDatabase LookRules()
        {
            if (Database != null) return Database;
            GameObject player = NetworkManager.Singleton != null ? NetworkManager.Singleton.NetworkConfig?.PlayerPrefab : null;
            PlayerIdentity identity = player != null ? player.GetComponent<PlayerIdentity>() : null;
            return identity != null ? identity.Database : null;
        }

        // ------------------------------------------------------------------ the names in use

        /// <summary>The names in use, made from every saved account the first time (see the class notes).</summary>
        public static NameIndex Names()
        {
            bool kept = ServerSave.Store.TryRead(ServerSave.WorldPrefix + NamesDocument, out _);
            NameIndex names = ServerSave.World<NameIndex>(NamesDocument);
            if (kept || names.Made) return names;
            names.Made = true;
            var renamed = MakeNames(names);
            ServerSave.WriteTogether(renamed, NamesDocument);
            Debug.Log($"[Roster] The names in use were gathered: {names.Count} name(s); {renamed.Count} account(s) have a character to rename.");
            return names;
        }

        // Every character of every account, one per name; the others are marked to be renamed. The accounts changed.
        private static List<AccountSave> MakeNames(NameIndex names)
        {
            var byName = new Dictionary<string, List<(AccountSave account, CharacterSave character)>>(StringComparer.Ordinal);
            foreach (AccountSave header in ServerSave.AccountHeaders())
                foreach (CharacterSave character in header.characters)
                {
                    if (character == null || !SaveKeys.IsId(character.characterId) || string.IsNullOrWhiteSpace(character.name)) continue;
                    string key = NameIndex.KeyOf(character.name);
                    if (!byName.TryGetValue(key, out var holders)) byName[key] = holders = new List<(AccountSave, CharacterSave)>();
                    holders.Add((header, character));
                }

            var losers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var holders in byName.Values)
            {
                holders.Sort((a, b) => KeepsName(a.account.accountId, a.character, b.account.accountId, b.character));
                (AccountSave account, CharacterSave character) keeper = holders[0];
                if (!keeper.character.rename) names.Put(keeper.character.name, keeper.account.accountId, keeper.character.characterId);
                for (int i = 1; i < holders.Count; i++)
                {
                    if (!losers.TryGetValue(holders[i].account.accountId, out var ids)) losers[holders[i].account.accountId] = ids = new List<string>();
                    ids.Add(holders[i].character.characterId);
                }
            }

            var changed = new List<AccountSave>();
            foreach (KeyValuePair<string, List<string>> loser in losers)
            {
                AccountSave saved = ServerSave.LiveAccount(loser.Key);
                if (saved == null)
                {
                    if (!ServerSave.TryHoldOffline(loser.Key, out _)) continue;
                    try { saved = ServerSave.LoadAccount(loser.Key); }
                    finally { ServerSave.ReleaseOffline(loser.Key); }
                }
                foreach (string id in loser.Value)
                {
                    CharacterSave character = saved.Find(id);
                    if (character == null) continue;
                    character.rename = true;
                    Debug.Log($"[Roster] {character.name} ({id}) of account {loser.Key} shares its name with a character that keeps it: it is renamed before it plays again.");
                }
                changed.Add(saved);
            }
            return changed;
        }

        /// <summary>Which of two characters of one name keeps it (negative: the first): a GM's, the higher level, the one played least recently.</summary>
        public static int KeepsName(string accountA, CharacterSave a, string accountB, CharacterSave b)
        {
            bool gmA = GmRoster.FindByAccount(accountA) != null, gmB = GmRoster.FindByAccount(accountB) != null;
            if (gmA != gmB) return gmA ? -1 : 1;
            if (a.level != b.level) return b.level.CompareTo(a.level);
            if (a.savedUtc != b.savedUtc) return a.savedUtc.CompareTo(b.savedUtc);
            return string.CompareOrdinal(a.characterId, b.characterId);
        }
    }

    /// <summary>
    /// The character names in use on the server, one entry a name (capitals ignored), each with the account and character
    /// holding it. A world document of entries: a new name writes that name alone.
    /// </summary>
    [Serializable]
    public sealed class NameIndex : IWorldEntries
    {
        [Serializable]
        public sealed class Entry
        {
            public string name;
            public string account;
            public string character;
        }

        private readonly Dictionary<string, Entry> byKey = new Dictionary<string, Entry>(StringComparer.Ordinal);

        /// <summary>True once gathered (or read back): it is never gathered twice.</summary>
        public bool Made { get; set; }
        public int Count => byKey.Count;

        /// <summary>How a name is looked up: capitals ignored.</summary>
        public static string KeyOf(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();

        /// <summary>Who holds the name, or null when it is free.</summary>
        public Entry Holder(string name) => byKey.TryGetValue(KeyOf(name), out Entry entry) ? entry : null;

        public void Put(string name, string account, string character) =>
            byKey[KeyOf(name)] = new Entry { name = name, account = account, character = character };

        /// <summary>Every name an account holds goes to another account (its characters moved there). How many.</summary>
        public int Reassign(string fromAccount, string toAccount)
        {
            int moved = 0;
            foreach (Entry entry in byKey.Values)
            {
                if (entry.account != fromAccount) continue;
                entry.account = toAccount;
                moved++;
            }
            return moved;
        }

        /// <summary>Frees the name when this character holds it.</summary>
        public void Drop(string name, string account, string character)
        {
            string key = KeyOf(name);
            if (byKey.TryGetValue(key, out Entry entry) && entry.account == account && entry.character == character) byKey.Remove(key);
        }

        public void CollectEntries(List<KeyValuePair<string, string>> into)
        {
            var keys = new List<string>(byKey.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys) into.Add(new KeyValuePair<string, string>(key, JsonUtility.ToJson(byKey[key])));
        }

        public void RestoreEntries(IReadOnlyList<string> texts)
        {
            byKey.Clear();
            Made = true;
            foreach (string text in texts)
            {
                Entry entry;
                try { entry = JsonUtility.FromJson<Entry>(text); }
                catch (Exception) { continue; }
                if (entry != null && !string.IsNullOrEmpty(entry.name)) byKey[KeyOf(entry.name)] = entry;
            }
        }
    }
}
