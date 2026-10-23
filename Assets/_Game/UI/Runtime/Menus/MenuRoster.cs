using System;
using System.Collections;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Networking;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Whose characters the menus show (online phase 4, milestone 5: the roster on the server). A server keeps its
    /// players' characters: the select screen shows this PC's copy of the chosen server's roster at once, asks the server
    /// for the real one, and creates, deletes and renames by asking it (<see cref="RosterLink"/>). "Play here" (and no
    /// server chosen yet in the Editor) keeps this PC's own characters, as before. The copy keeps what the server does
    /// not send: what each character last wore, for the doll.
    /// </summary>
    public static class MenuRoster
    {
        private static readonly Dictionary<string, ICharacterStore> Stores = new Dictionary<string, ICharacterStore>(StringComparer.Ordinal);
        private static ICharacterStore local;

        /// <summary>The server whose roster is shown ("here" or empty: this PC's own characters).</summary>
        public static string Key { get; private set; } = string.Empty;

        /// <summary>True while the shown roster is a server's.</summary>
        public static bool OnServer => IsServerKey(Key);

        /// <summary>True once the shown server's roster was fetched since it was chosen.</summary>
        public static bool Fetched { get; private set; }

        public static bool IsServerKey(string key) => !string.IsNullOrEmpty(key) && key != ServerDirectory.HereKey;

        /// <summary>This PC's own characters (Play here, the Editor).</summary>
        public static ICharacterStore Local => local ??= new PlayerPrefsCharacterStore(KryticalNova.Core.LocalProfile.Name());

        /// <summary>Another account signed in: the server's list is asked for again.</summary>
        public static void Forget() => Fetched = false;

        /// <summary>Shows the roster of <paramref name="key"/> (a server's, or this PC's own) and plays from it.</summary>
        public static void Use(string key)
        {
            key ??= string.Empty;
            if (key != Key) Fetched = false;
            Key = key;
            CharacterSession.UseStore(StoreFor(key));
        }

        private static ICharacterStore StoreFor(string key)
        {
            if (!IsServerKey(key)) return Local;
            if (!Stores.TryGetValue(key, out ICharacterStore store))
            {
                string profile = KryticalNova.Core.LocalProfile.Name();
                Stores[key] = store = new PlayerPrefsCharacterStore((string.IsNullOrEmpty(profile) ? "" : profile + ".") + "server." + key);
            }
            return store;
        }

        /// <summary>The listing of the shown server, or null (none chosen, or not in the list yet).</summary>
        public static ServerListing Listing
        {
            get
            {
                foreach (ServerEntry entry in ServerDirectory.Entries)
                    if (entry.Key == Key) return entry.Listing;
                return null;
            }
        }

        /// <summary>The server's name, for messages.</summary>
        public static string ServerName
        {
            get
            {
                foreach (ServerEntry entry in ServerDirectory.Entries)
                    if (entry.Key == Key) return entry.Name;
                return "the server";
            }
        }

        // ------------------------------------------------------------------ asking

        /// <summary>
        /// Fetches the shown server's roster into this PC's copy; <paramref name="done"/> gets the problem (null when it
        /// came) and a note from the server (characters without a slot), or null.
        /// </summary>
        public static IEnumerator Fetch(Action<string, string> done)
        {
            // A sign-in that opened another player sends the one it left behind, once: its characters come along.
            var request = new RosterRequest { op = RosterRequest.List, carry = UnityAccount.LeftBehind };
            // The looks this PC keeps of its own characters, for ones made before the server kept looks; as many as fit.
            AddLooks(Local.Load(), request.looks);
            while (request.looks.Count > 0 && RosterWire.Pack(request.ToJson()).Length > RosterWire.MaxRequestChars)
                request.looks.RemoveAt(request.looks.Count - 1);
            if (request.carry != null && RosterWire.Pack(request.ToJson()).Length > RosterWire.MaxRequestChars)
            {
                UnityEngine.Debug.LogWarning("[Roster] The sign-in left behind is too long to send along: its characters stay where they are.");
                request.carry = null;
                UnityAccount.ForgetLeftBehind();
            }
            yield return Ask(request, (reply, problem) =>
            {
                if (reply != null && reply.ok)
                {
                    Fetched = true;
                    if (request.carry != null) UnityAccount.ForgetLeftBehind();
                }
                done?.Invoke(problem, reply != null && reply.ok && !string.IsNullOrEmpty(reply.message) ? reply.message : null);
            });
        }

        /// <summary>
        /// A character was renamed in the world (a Name Change Pass): this PC's copies take the new name at once, and the
        /// server's list is asked for again when character select opens (owner, 23 Oct 2026: the old name stayed there
        /// until the game was restarted).
        /// </summary>
        public static void NoteRenamed(string characterId, string name)
        {
            if (!string.IsNullOrEmpty(characterId) && !string.IsNullOrEmpty(name))
            {
                if (IsServerKey(Key)) RenameIn(StoreFor(Key), characterId, name);
                RenameIn(Local, characterId, name);
            }
            Forget();
        }

        private static void RenameIn(ICharacterStore store, string characterId, string name)
        {
            CharacterRoster roster = store.Load();
            CharacterProfile profile = Find(roster, characterId);
            if (profile == null) return;
            profile.name = name;
            profile.mustRename = false;
            store.Save(roster);
        }

        public static IEnumerator Create(string name, CharacterRecipe recipe, int slot, Action<RosterReply, string> done) =>
            Ask(new RosterRequest { op = RosterRequest.Create, name = name, recipe = recipe, slot = slot }, done);

        public static IEnumerator Delete(string characterId, Action<RosterReply, string> done) =>
            Ask(new RosterRequest { op = RosterRequest.Delete, characterId = characterId }, done);

        public static IEnumerator Rename(string characterId, string name, Action<RosterReply, string> done) =>
            Ask(new RosterRequest { op = RosterRequest.Rename, characterId = characterId, name = name }, done);

        // Asks the shown server. Whatever it answers, its list replaces this PC's copy; a refusal comes back as the problem.
        private static IEnumerator Ask(RosterRequest request, Action<RosterReply, string> done)
        {
            ServerListing listing = Listing;
            string key = Key;
            if (!IsServerKey(key) || listing == null)
            {
                done(null, "Choose a server first.");
                yield break;
            }
            string text = null, problem = null;
            yield return RosterLink.Ask(listing, request.ToJson(), (answer, why) => { text = answer; problem = why; });
            RosterReply reply = text != null ? RosterReply.FromJson(text) : null;
            if (reply == null)
            {
                done(null, problem ?? $"{ServerNameOf(key)} answered something this game cannot read.");
                yield break;
            }
            if (reply.ok || reply.characters.Count > 0 || request.op != RosterRequest.List) Keep(key, reply);
            done(reply, reply.ok ? null : reply.message);
        }

        private static string ServerNameOf(string key)
        {
            foreach (ServerEntry entry in ServerDirectory.Entries)
                if (entry.Key == key) return entry.Name;
            return "The server";
        }

        // A refusal from a request that changes nothing still carries the list as the server has it.
        private static void Keep(string key, RosterReply reply)
        {
            if (!reply.ok && reply.characters.Count == 0) return;
            ICharacterStore store = StoreFor(key);
            CharacterRoster before = store.Load();
            CharacterRoster mine = Local.Load();
            var roster = new CharacterRoster();
            foreach (RosterCharacter character in reply.characters)
            {
                if (character == null || character.slot < 0 || character.slot >= CharacterRoster.SlotCount || string.IsNullOrEmpty(character.id)) continue;
                CharacterProfile kept = Find(before, character.id) ?? Find(mine, character.id);
                roster.Set(character.slot, new CharacterProfile
                {
                    id = character.id,
                    name = character.name,
                    // A character with no look on the server yet keeps the one this PC has.
                    recipe = !character.recipe.IsEmpty ? character.recipe.Clone() : kept != null ? kept.recipe.Clone() : default,
                    level = character.level,
                    created = character.created,
                    lastPlayed = Math.Max(character.lastPlayed, kept != null ? kept.lastPlayed : 0),
                    worn = kept != null && kept.worn != null ? kept.worn : Array.Empty<WornPiece>(),
                    mustRename = character.mustRename,
                });
            }
            CharacterProfile last = before.Get(before.lastPlayedSlot);
            if (last != null)
                for (int slot = 0; slot < CharacterRoster.SlotCount; slot++)
                    if (roster.IsUsed(slot) && roster.Get(slot).id == last.id) roster.lastPlayedSlot = slot;
            store.Save(roster);
        }

        private static CharacterProfile Find(CharacterRoster roster, string id)
        {
            for (int slot = 0; slot < CharacterRoster.SlotCount; slot++)
            {
                CharacterProfile profile = roster.Get(slot);
                if (profile != null && profile.id == id) return profile;
            }
            return null;
        }

        private static void AddLooks(CharacterRoster roster, List<RosterLook> into)
        {
            for (int slot = 0; slot < CharacterRoster.SlotCount; slot++)
            {
                CharacterProfile profile = roster.Get(slot);
                if (profile == null || profile.recipe.IsEmpty || into.Exists(l => l.id == profile.id)) continue;
                into.Add(new RosterLook { id = profile.id, recipe = profile.recipe });
            }
        }
    }
}
