#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
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
    /// build started with <c>-bot &lt;n&gt; -botServer &lt;address:port&gt;</c> (and <c>-batchmode -nographics</c>, no
    /// window) makes a character of its own the first time (a random look, one of the three factions in turn, a name like
    /// "Botkari"), skips the menus, joins that server through the same join as a player and then plays: two in three
    /// walk to a Hostile Zone and hunt with Autopilot (the measuring tool gives them gear and Autopilot time with GM
    /// commands, so nothing here bends the game's rules), the rest wander the town, and all of them chat now and then.
    /// Never in a player's build: only development builds carry it.
    /// </summary>
    public static class MeasureBot
    {
        public const string BotArgument = "-bot";
        public const string ServerArgument = "-botServer";

        private static readonly string[] Syllables = { "ka", "ri", "mo", "te", "lu", "sa", "no", "vi", "ze", "po" };
        private static readonly FactionId[] Factions = { FactionId.Hegemony, FactionId.Null, FactionId.Covenant };

        /// <summary>This copy's number, or -1 when it is not a bot.</summary>
        public static int Index { get; private set; } = -1;

        public static bool Active => Index >= 0;

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
            Debug.Log($"[Bot] {NameFor(Index)} (bot {Index}) joins {address}:{port}.");
            Application.runInBackground = true;
            JoinFlow.Failed += reason =>
            {
                Debug.LogWarning("[Bot] The join failed: " + reason);
                Application.Quit(3);
            };
            JoinFlow.Begin(new ServerListing { name = "Measure server", address = address, port = port });
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

        // The first scene is the title: straight on to the world, where the join runs (NetworkConnectionManager).
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != BuildSceneNames.World)
            {
                if (scene.name == BuildSceneNames.Title) SceneManager.LoadScene(BuildSceneNames.World);
                return;
            }
            // Before the world's Start: the character must be chosen before the player is asked for.
            if (!EnsureCharacter()) { Application.Quit(4); return; }
            new GameObject("Measure Bot").AddComponent<MeasureBotBrain>();
        }

        // A character of the bot's own, made once and kept under its profile, as the character creator would.
        private static bool EnsureCharacter()
        {
            CharacterRoster roster = CharacterSession.Store.Load();
            if (roster.IsUsed(0))
            {
                CharacterSession.Enter(roster, 0);
                return true;
            }
            NetworkManager network = NetworkManager.Singleton;
            GameObject prefab = network != null ? network.NetworkConfig.PlayerPrefab : null;
            CharacterCreationDatabase database = prefab != null && prefab.TryGetComponent(out PlayerIdentity identity) ? identity.Database : null;
            if (database == null) { Debug.LogError("[Bot] The character database could not be found."); return false; }

            var lineages = new List<LineageDefinition>();
            database.GetLineages(Factions[Index % Factions.Length], lineages);
            if (lineages.Count == 0) { Debug.LogError("[Bot] No lineage for the bot's faction."); return false; }
            var random = new System.Random(Index * 7919 + 17);
            LineageDefinition lineage = lineages[random.Next(lineages.Count)];
            BodyType body = random.Next(2) == 0 ? BodyType.Masculine : BodyType.Feminine;
            CharacterRecipe recipe = RecipeEdit.Randomize(lineage, body, database, random);
            roster.Set(0, CharacterProfile.Create(NameFor(Index), recipe));
            CharacterSession.Store.Save(roster);
            CharacterSession.Enter(roster, 0);
            Debug.Log($"[Bot] Made {NameFor(Index)}: {lineage.name}, {body}.");
            return true;
        }
    }

    /// <summary>The scene names the bot needs (the front end's own constants live in the UI assembly).</summary>
    internal static class BuildSceneNames
    {
        public const string Title = "Title";
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
