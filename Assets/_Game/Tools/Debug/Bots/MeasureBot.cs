#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using KryticalNova.Autopilot;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Social;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// A test player for measuring a server (online phase 1, chunk 7; owner: bots that play like players). A development
    /// build started with <c>-bot &lt;n&gt; -botServer &lt;address:port&gt; -profile bot&lt;n&gt;</c> (and <c>-batchmode
    /// -nographics</c>, no window) signs in as a player of its own (the profile), asks the server's roster for its
    /// character and makes one there the first time (a random look, one of the three factions in turn, a name like
    /// "Botkari"; online phase 4, milestone 5: the server keeps the characters), skips the menus, joins that server
    /// through the same join as a player and then plays: two in three
    /// walk to a Hostile Zone and hunt with Autopilot (the measuring tool gives them gear and Autopilot time with GM
    /// commands, so nothing here bends the game's rules), the rest wander the town, and all of them chat now and then.
    /// Never in a player's build: only development builds carry it.
    /// </summary>
    public static class MeasureBot
    {
        public const string BotArgument = "-bot";
        public const string ServerArgument = "-botServer";
        /// <summary>Names asked for before giving up: the bot's own, then with a digit when another account holds it.</summary>
        internal const int NameTries = 4;

        private static readonly string[] Syllables = { "ka", "ri", "mo", "te", "lu", "sa", "no", "vi", "ze", "po" };
        private static readonly FactionId[] Factions = { FactionId.Hegemony, FactionId.Null, FactionId.Covenant };

        /// <summary>This copy's number, or -1 when it is not a bot.</summary>
        public static int Index { get; private set; } = -1;

        public static bool Active => Index >= 0;

        /// <summary>The server this bot joins.</summary>
        internal static ServerListing Server { get; private set; }

        /// <summary>This bot's copy of the server's roster (kept per profile and server, as the menus keep theirs).</summary>
        internal static ICharacterStore Store { get; private set; }

        /// <summary>The character the server's roster gave this bot.</summary>
        internal static string CharacterId { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            Index = -1;
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            string server = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == BotArgument && int.TryParse(args[i + 1], out int n)) Index = Mathf.Clamp(n, 0, 99);
                if (args[i] == ServerArgument) server = args[i + 1];
            }
            if (!Active) return;
            if (!TryAddress(server, out string address, out int port))
            {
                Debug.LogError("[Bot] Start with -botServer <address:port>.");
                Application.Quit(2);
                return;
            }
            // Without a profile of its own a bot would sign in, and play, as this PC's own player.
            string profile = LocalProfile.Name();
            if (string.IsNullOrEmpty(profile))
            {
                Debug.LogError($"[Bot] Start with -profile bot{Index:00}, so the bot signs in as a player of its own.");
                Application.Quit(2);
                return;
            }
            Debug.Log($"[Bot] {NameFor(Index)} (bot {Index}, profile {profile}) joins {address}:{port}.");
            Application.runInBackground = true;
            Server = new ServerListing { name = "Measure server", address = address, port = port };
            Store = new PlayerPrefsCharacterStore(profile + ".bot.server." + address + ":" + port);
            CharacterId = null;
            JoinFlow.Failed += reason =>
            {
                Debug.LogWarning("[Bot] The join failed: " + reason);
                Application.Quit(3);
            };
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static bool TryAddress(string text, out string address, out int port)
        {
            address = null;
            port = 0;
            if (string.IsNullOrEmpty(text)) return false;
            int colon = text.LastIndexOf(':');
            if (colon <= 0 || !int.TryParse(text.Substring(colon + 1), out port)) return false;
            address = text.Substring(0, colon);
            return true;
        }

        public static string NameFor(int index) =>
            "Bot" + Syllables[(index / Syllables.Length) % Syllables.Length] + Syllables[index % Syllables.Length];

        // The first scene is the title. Character select comes next only because it hands the network prefab to the
        // roster link (MeasureBotRoster asks the server there); then the world, where the join runs (NetworkConnectionManager).
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == BuildSceneNames.Title) { SceneManager.LoadScene(BuildSceneNames.CharacterSelect); return; }
            if (scene.name == BuildSceneNames.CharacterSelect) { new GameObject("Measure Bot Roster").AddComponent<MeasureBotRoster>(); return; }
            if (scene.name != BuildSceneNames.World) return;
            // Before the world's Start: the character and the server must be chosen before the player is asked for.
            // Chosen here, not in character select, whose screen puts back a store of its own while it is open.
            if (!EnterCharacter()) { Application.Quit(4); return; }
            JoinFlow.Begin(Server);
            new GameObject("Measure Bot").AddComponent<MeasureBotBrain>();
        }

        private static bool EnterCharacter()
        {
            CharacterSession.UseStore(Store);
            CharacterRoster roster = Store.Load();
            for (int slot = 0; slot < CharacterRoster.SlotCount; slot++)
            {
                if (!roster.IsUsed(slot) || roster.Get(slot).id != CharacterId) continue;
                CharacterSession.Enter(roster, slot);
                return true;
            }
            Debug.LogError("[Bot] The character the server gave is missing from the bot's copy of the roster.");
            return false;
        }

        /// <summary>A random look for the bot's faction from the rules the server checks it by; empty when there is none.</summary>
        internal static CharacterRecipe RandomLook(CharacterCreationDatabase database)
        {
            var lineages = new List<LineageDefinition>();
            database.GetLineages(Factions[Index % Factions.Length], lineages);
            if (lineages.Count == 0) { Debug.LogError("[Bot] No lineage for the bot's faction."); return default; }
            var random = new System.Random(Index * 7919 + 17);
            LineageDefinition lineage = lineages[random.Next(lineages.Count)];
            BodyType body = random.Next(2) == 0 ? BodyType.Masculine : BodyType.Feminine;
            Debug.Log($"[Bot] Look: {lineage.name}, {body}.");
            return RecipeEdit.Randomize(lineage, body, database, random);
        }

        /// <summary>The name asked for on try <paramref name="attempt"/>: the bot's own, then with a digit.</summary>
        internal static string NameFor(int index, int attempt) => attempt == 0 ? NameFor(index) : NameFor(index) + attempt;

        /// <summary>
        /// The server's answer kept as the bot's copy of its roster (as the menus keep theirs, <c>MenuRoster</c>); false when
        /// the answer has no characters.
        /// </summary>
        internal static bool Keep(RosterReply reply)
        {
            var roster = new CharacterRoster();
            foreach (RosterCharacter character in reply.characters)
            {
                if (character == null || string.IsNullOrEmpty(character.id) || character.recipe.IsEmpty) continue;
                roster.Set(character.slot, new CharacterProfile
                {
                    id = character.id, name = character.name, recipe = character.recipe.Clone(), level = character.level,
                    created = character.created, lastPlayed = character.lastPlayed, mustRename = character.mustRename,
                });
            }
            Store.Save(roster);
            return roster.Count > 0;
        }
    }

    /// <summary>
    /// Asks the server for the bot's character in character select, where the roster link has the network prefab: the
    /// first one the account has, or a new one made there (the server checks the name and the look, as for a player).
    /// Then the world loads. Any refusal ends the bot, with the server's reason in its log.
    /// </summary>
    internal sealed class MeasureBotRoster : MonoBehaviour
    {
        private IEnumerator Start()
        {
            yield return null; // character select's own Start has handed over the network prefab
            if (RosterLink.NetworkPrefab == null) { Stop("character select did not hand over the network prefab."); yield break; }

            RosterReply reply = null;
            yield return Ask(new RosterRequest { op = RosterRequest.List }, r => reply = r);
            if (reply == null) yield break;
            if (!reply.ok) { Stop(reply.message); yield break; }

            RosterCharacter mine = First(reply);
            if (mine == null)
            {
                CharacterCreationDatabase database = Database();
                if (database == null) { Stop("the character database could not be found."); yield break; }
                CharacterRecipe look = MeasureBot.RandomLook(database);
                if (look.IsEmpty) { Stop("no look could be made."); yield break; }
                for (int attempt = 0; attempt < MeasureBot.NameTries && mine == null; attempt++)
                {
                    string wanted = MeasureBot.NameFor(MeasureBot.Index, attempt);
                    reply = null;
                    yield return Ask(new RosterRequest { op = RosterRequest.Create, name = wanted, recipe = look }, r => reply = r);
                    if (reply == null) yield break;
                    if (reply.ok) { mine = Find(reply, reply.characterId); Debug.Log($"[Bot] Made {wanted} on the server."); }
                    else if (reply.message != CharacterNameRules.ReasonTakenInGame) { Stop(reply.message); yield break; }
                    else Debug.Log($"[Bot] {wanted} is taken in the game.");
                }
                if (mine == null) { Stop($"every name tried for bot {MeasureBot.Index} is taken."); yield break; }
            }
            if (!MeasureBot.Keep(reply)) { Stop("the server's answer held no character."); yield break; }
            MeasureBot.CharacterId = mine.id;
            Debug.Log($"[Bot] Plays {mine.name} ({mine.id}).");
            KryticalNova.World.MapScenes.Begin(BuildSceneNames.World); // an Addressable map
        }

        // One question; null to the caller (and the bot ended) when no answer came.
        private static IEnumerator Ask(RosterRequest request, Action<RosterReply> done)
        {
            string text = null, problem = null;
            yield return RosterLink.Ask(MeasureBot.Server, request.ToJson(), (answer, why) => { text = answer; problem = why; });
            RosterReply reply = text != null ? RosterReply.FromJson(text) : null;
            if (reply == null) Stop(problem ?? "the server answered something the bot cannot read.");
            done(reply);
        }

        private static RosterCharacter First(RosterReply reply)
        {
            foreach (RosterCharacter character in reply.characters)
                if (character != null && !string.IsNullOrEmpty(character.id) && !character.recipe.IsEmpty && !character.mustRename) return character;
            return null;
        }

        private static RosterCharacter Find(RosterReply reply, string id)
        {
            foreach (RosterCharacter character in reply.characters)
                if (character != null && character.id == id) return character;
            return null;
        }

        private static CharacterCreationDatabase Database()
        {
            NetworkManager network = RosterLink.NetworkPrefab.GetComponent<NetworkManager>();
            GameObject prefab = network != null ? network.NetworkConfig.PlayerPrefab : null;
            return prefab != null && prefab.TryGetComponent(out PlayerIdentity identity) ? identity.Database : null;
        }

        private static void Stop(string why)
        {
            Debug.LogError("[Bot] No character from the server's roster: " + why);
            Application.Quit(4);
        }
    }

    /// <summary>The scene names the bot needs (the front end's own constants live in the UI assembly).</summary>
    internal static class BuildSceneNames
    {
        public const string Title = "Title";
        public const string CharacterSelect = "CharacterSelect";
        public const string World = "Zone01_StarterTown";
    }

    /// <summary>What a bot does once it is in the world: hunt with Autopilot or wander the town, and chat.</summary>
    internal sealed class MeasureBotBrain : MonoBehaviour
    {
        private const float ThinkSeconds = 2f;
        private const float WanderRadius = 45f;
        private static readonly string[] Lines =
        {
            "Anyone up for a run later?", "These Hostiles hit hard today.", "Selling Stims, cheap.", "Nice weather in town.",
            "Where is the Armorer again?", "Looking for a Squad.", "Just levelled up!", "gg",
        };

        private bool hunter, joined;
        private float nextThink, nextChat, nextWander;
        private Vector3 home;
        private bool homeKnown;

        private void Start()
        {
            hunter = MeasureBot.Index % 3 != 2;
            nextChat = Time.time + Random.Range(20f, 60f);
            Debug.Log($"[Bot] Role: {(hunter ? "hunter" : "townsfolk")}.");
        }

        private void Update()
        {
            if (Time.time < nextThink) return;
            nextThink = Time.time + ThinkSeconds;
            // A session that drops (the server stopped or stalled) ends the bot: the measuring tools start a fresh one.
            NetworkManager network = NetworkManager.Singleton;
            if (joined && (network == null || !network.IsConnectedClient))
            {
                Debug.LogWarning("[Bot] The session ended" + (network != null && !string.IsNullOrEmpty(network.DisconnectReason) ? ": " + network.DisconnectReason : "."));
                Application.Quit(5);
                enabled = false;
                return;
            }
            PlayerInventory me = PlayerInventory.Local;
            if (me == null) return; // not in yet, or between lives
            joined = true;
            if (!homeKnown) { home = me.transform.position; homeKnown = true; }

            if (Time.time >= nextChat && me.TryGetComponent(out PlayerChat chat))
            {
                nextChat = Time.time + Random.Range(40f, 90f);
                chat.Send(Random.value < 0.7f ? ChatChannel.General : ChatChannel.Faction, Lines[Random.Range(0, Lines.Length)]);
            }

            if (!me.TryGetComponent(out PointAndClickPlayer pointer)) return;
            if (hunter) Hunt(me, pointer);
            else Wander(pointer);
        }

        private void Hunt(PlayerInventory me, PointAndClickPlayer pointer)
        {
            if (!me.TryGetComponent(out PlayerAutopilot autopilot) || autopilot.IsOn) return;
            HostileZone here = HostileZone.At(me.transform.position);
            if (here != null)
            {
                autopilot.RequestSetOn(true); // refused until the measuring tool has given Autopilot time
                return;
            }
            if (pointer.IsTravelling) return;
            IReadOnlyList<HostileZone> zones = HostileZone.All;
            if (zones.Count == 0) { hunter = false; return; }
            HostileZone zone = zones[MeasureBot.Index % zones.Count];
            pointer.TravelTo(zone.Centre, "the Hostile Zone");
        }

        private void Wander(PointAndClickPlayer pointer)
        {
            if (Time.time < nextWander || pointer.IsTravelling) return;
            nextWander = Time.time + Random.Range(8f, 20f);
            Vector2 step = Random.insideUnitCircle * WanderRadius;
            Vector3 goal = home + new Vector3(step.x, 0f, step.y);
            if (NavMesh.SamplePosition(goal, out NavMeshHit hit, 10f, NavMesh.AllAreas)) pointer.TravelTo(hit.position, "a spot in town");
        }
    }
}
#endif
