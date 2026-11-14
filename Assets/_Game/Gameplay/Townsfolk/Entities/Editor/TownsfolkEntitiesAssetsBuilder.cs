using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.NPC;
using KryticalNova.Player.Entities.Editor;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KryticalNova.Townsfolk.Editor
{
    /// <summary>
    /// Town life's assets (owner's DAY list item 18; plan s5). Safe to run any time: the ghost prefab and its holder are updated in place
    /// (the ghost type, the prefab's GUID, never changes); the settings, the ten personality drafts, the Sky Concord cast and area are made
    /// only when missing, so the owner's edits are kept; the shared animators and the Resources holders are rewritten. Menu Krytical Nova
    /// ▸ Townsfolk ▸ Build Assets, or in batch <c>-executeMethod KryticalNova.Townsfolk.Editor.TownsfolkEntitiesAssetsBuilder.Build</c>.
    /// </summary>
    public static class TownsfolkEntitiesAssetsBuilder
    {
        const string EntitiesFolder = TownsfolkPaths.Root + "/Entities";
        public const string PrefabFolder = EntitiesFolder + "/Prefabs";
        public const string PrefabPath = PrefabFolder + "/TownsfolkGhost.prefab";
        /// <summary>The ghost's name: netcode hashes it into the ghost type, so it never changes.</summary>
        public const string GhostName = "TownsfolkGhost";
        public const string HolderName = "TownsfolkGhostPrefabHolder";
        const string ResourcesFolder = EntitiesFolder + "/Resources";
        const string AnimationFolder = TownsfolkPaths.Root + "/Data/Animation";
        const string MasculineController = AnimationFolder + "/AC_Townsfolk_Masc.controller";
        const string FeminineController = AnimationFolder + "/AC_Townsfolk_Femn.controller";
        const string BodyPrefab = "Assets/_Game/Characters/Player/Player_01/Player_01.prefab";
        const string DatabaseAsset = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets/CharacterCreationDatabase.asset";
        static readonly string[] ClipFolders =
        {
            "Assets/Synty/AnimationBaseLocomotion/Animations/Sidekick", "Assets/Synty/AnimationIdles/Animations/Sidekick",
            "Assets/Synty/AnimationEmotesAndTaunts/Animations/Sidekick",
        };

        [MenuItem("Krytical Nova/Townsfolk/Build Assets")]
        public static void BuildMenu()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        public static void Build()
        {
            if (!File.Exists(PlayerGhostAssetsBuilder.SubScenePath))
            {
                Debug.LogError("[KnBuild] " + PlayerGhostAssetsBuilder.SubScenePath + " is missing: run Krytical Nova ▸ Online ▸ Entities ▸ Build Player Ghost Assets first.");
                return;
            }
            EnsureFolder(PrefabFolder);
            EnsureFolder(ResourcesFolder);
            EnsureFolder(TownsfolkPaths.PersonalityFolder);
            EnsureFolder(AnimationFolder);
            GameObject prefab = EnsurePrefab();
            EnsureHolder(prefab);
            TownsfolkSettings settings = LoadOrCreate<TownsfolkSettings>(TownsfolkPaths.SettingsAsset, out _);
            Dictionary<byte, TownsfolkPersonality> personalities = EnsurePersonalities();
            TownsfolkCast cast = EnsureCast();
            TownLifeArea area = EnsureArea(cast, personalities);
            (RuntimeAnimatorController masc, RuntimeAnimatorController femn) = BuildAnimators();
            EnsureResources(settings, personalities.Values.OrderBy(p => p.id).ToArray(), area, cast, masc, femn);
            AssetDatabase.SaveAssets();
            Debug.Log("[KnBuild] Townsfolk assets: ghost " + PrefabPath + ", " + personalities.Count + " personalities, area " + area.name + ".");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T LoadOrCreate<T>(string path, out bool made) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            made = asset == null;
            if (!made) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ---- ghost ----

        static GameObject EnsurePrefab()
        {
            bool exists = File.Exists(PrefabPath);
            GameObject root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(GhostName);
            try
            {
                root.name = GhostName;
                var ghost = root.GetComponent<GhostAuthoringComponent>() ?? root.AddComponent<GhostAuthoringComponent>();
                ghost.HasOwner = false;
                ghost.DefaultGhostMode = GhostMode.Interpolated;
                ghost.SupportedGhostModes = GhostModeMask.Interpolated;
                ghost.OptimizationMode = GhostOptimizationMode.Dynamic;   // they walk
                ghost.Importance = PlayerGhostAssetsBuilder.Importance(ghost.Importance, s => s.importanceTownsfolk);
                if (root.GetComponent<TownsfolkGhostAuthoring>() == null) root.AddComponent<TownsfolkGhostAuthoring>();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                if (exists) PrefabUtility.UnloadPrefabContents(root);
                else Object.DestroyImmediate(root);
            }
            AssetDatabase.ImportAsset(PrefabPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            // the ghost type is the prefab's asset GUID, in a field Netcode only fills in memory: written and saved every time (as the Hostiles')
            var so = new SerializedObject(prefab.GetComponent<GhostAuthoringComponent>());
            SerializedProperty id = so.FindProperty("prefabId");
            if (id != null)
            {
                id.stringValue = AssetDatabase.AssetPathToGUID(PrefabPath);
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(prefab);
                AssetDatabase.SaveAssetIfDirty(prefab);
            }
            return prefab;
        }

        static void EnsureHolder(GameObject prefab)
        {
            bool single = Application.isBatchMode;
            Scene scene = EditorSceneManager.OpenScene(PlayerGhostAssetsBuilder.SubScenePath, single ? OpenSceneMode.Single : OpenSceneMode.Additive);
            try
            {
                TownsfolkGhostPrefabAuthoring holder = null;
                foreach (GameObject go in scene.GetRootGameObjects())
                    if (go.TryGetComponent(out holder)) break;
                if (holder == null)
                {
                    var go = new GameObject(HolderName);
                    SceneManager.MoveGameObjectToScene(go, scene);
                    holder = go.AddComponent<TownsfolkGhostPrefabAuthoring>();
                }
                if (holder.prefab == prefab) return;
                holder.prefab = prefab;
                EditorUtility.SetDirty(holder);
                if (!EditorSceneManager.SaveScene(scene, PlayerGhostAssetsBuilder.SubScenePath))
                    throw new IOException("the player ghost subscene could not be saved at " + PlayerGhostAssetsBuilder.SubScenePath);
            }
            finally
            {
                if (!single) EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---- the ten personalities (DRAFTS for the owner to edit: plan 3.4, every number PROPOSED) ----

        struct Draft
        {
            public byte id;
            public string name, file, notes;
            public Vector2 walk, jog, stay;
            public bool jogs, needsLight, nosy, declines;
            public float stop, crowding, chat;
            public (TownSpotKind kind, float w)[] likes;
            public byte[] chatsWith;
            public Vector4 outChance;
            public Vector2Int[] shifts;
            public RainBehaviour rain;
            public TownsfolkPersonality.Reaction reaction;
            public string[] barks;
        }

        static TownsfolkPersonality.Reaction R(float glance, float greet, float aside, bool runners = false, bool plays = false) =>
            new TownsfolkPersonality.Reaction { glanceSeconds = glance, greetChance = greet, stepAsideMetres = aside, watchRunners = runners, playsToPlayers = plays };

        static readonly Draft[] Drafts =
        {
            new Draft { id = TownPersonalityIds.BusyWorker, name = "Busy Worker", file = "TP_BusyWorker", notes = "Works a shift at a work spot, breaks at a stall. Off shift: home.",
                walk = new Vector2(1.6f, 1.8f), stop = 0.9f, stay = new Vector2(120f, 300f), likes = new[] { (TownSpotKind.Work, 1f), (TownSpotKind.Stall, 0.4f), (TownSpotKind.VendorStall, 0.3f) },
                chatsWith = new[] { TownPersonalityIds.BusyWorker, TownPersonalityIds.Guard }, chat = 0.2f, crowding = 1f, outChance = new Vector4(0.6f, 1f, 0.6f, 0.2f),
                shifts = new[] { new Vector2Int(6, 14), new Vector2Int(14, 22), new Vector2Int(22, 6) }, rain = RainBehaviour.Shelter, reaction = R(0f, 0f, 2.5f),
                barks = new[] { "Can't stop, shift's on.", "Mind the crates." } },
            new Draft { id = TownPersonalityIds.Stroller, name = "Stroller", file = "TP_Stroller", notes = "Wanders slowly, stops at views and benches.",
                walk = new Vector2(1.0f, 1.2f), stop = 0.5f, stay = new Vector2(20f, 60f), likes = new[] { (TownSpotKind.Viewpoint, 1f), (TownSpotKind.Bench, 0.8f), (TownSpotKind.Stall, 0.4f), (TownSpotKind.VendorStall, 0.4f), (TownSpotKind.ChatCircle, 0.3f) },
                chatsWith = new[] { TownPersonalityIds.Stroller, TownPersonalityIds.Gossip, TownPersonalityIds.Shopper }, chat = 0.5f, crowding = 1f, outChance = new Vector4(0.4f, 0.8f, 0.6f, 0.1f),
                rain = RainBehaviour.ShelterThenHome, reaction = R(3f, 0.1f, 1.5f), barks = new[] { "Lovely up here.", "You can see the whole town from the rail." } },
            new Draft { id = TownPersonalityIds.Jogger, name = "Jogger", file = "TP_Jogger", notes = "Jogs a loop of jog points, stops to stretch.",
                walk = new Vector2(1.3f, 1.5f), jogs = true, jog = new Vector2(3.2f, 3.8f), stop = 0.15f, stay = new Vector2(10f, 20f), likes = new[] { (TownSpotKind.JogPoint, 1f), (TownSpotKind.Viewpoint, 0.5f) },
                chatsWith = new byte[0], chat = 0f, declines = true, crowding = 1f, outChance = new Vector4(0.9f, 0.2f, 0.6f, 0.05f), rain = RainBehaviour.GoHome, reaction = R(0.5f, 0.05f, 2f),
                barks = new[] { "On your left!" } },
            new Draft { id = TownPersonalityIds.Gossip, name = "Gossip", file = "TP_Gossip", notes = "Lives for chat circles; best with other Gossips and Nosy Locals.",
                walk = new Vector2(1.1f, 1.3f), stop = 0.7f, stay = new Vector2(60f, 180f), likes = new[] { (TownSpotKind.ChatCircle, 1f), (TownSpotKind.Bench, 0.6f), (TownSpotKind.Stall, 0.4f), (TownSpotKind.VendorStall, 0.4f) },
                chatsWith = new[] { TownPersonalityIds.Gossip, TownPersonalityIds.NosyLocal, TownPersonalityIds.Shopper, TownPersonalityIds.Stroller }, chat = 0.9f, crowding = 0.5f,
                outChance = new Vector4(0.2f, 0.7f, 0.7f, 0.1f), rain = RainBehaviour.Shelter, reaction = R(4f, 0.15f, 1.2f),
                barks = new[] { "Did you hear what the Broker charged for that?", "Another new face. Where are they all coming from?" } },
            new Draft { id = TownPersonalityIds.Shopper, name = "Shopper", file = "TP_Shopper", notes = "Stall to stall while the stalls are open (08-20).",
                walk = new Vector2(1.2f, 1.4f), stop = 0.7f, stay = new Vector2(15f, 45f), likes = new[] { (TownSpotKind.Stall, 1f), (TownSpotKind.VendorStall, 0.9f), (TownSpotKind.Bench, 0.3f), (TownSpotKind.ChatCircle, 0.3f) },
                chatsWith = new[] { TownPersonalityIds.Shopper, TownPersonalityIds.Gossip }, chat = 0.5f, crowding = 1f, outChance = new Vector4(0f, 0.8f, 0.5f, 0f),
                rain = RainBehaviour.Shelter, reaction = R(2f, 0.05f, 1.5f), barks = new[] { "Fresh stock at the Armorer, they say.", "Too dear. I'll come back tomorrow." } },
            new Draft { id = TownPersonalityIds.Loner, name = "Loner", file = "TP_Loner", notes = "Quiet walls and edge benches; crowds count double; declines every chat.",
                walk = new Vector2(1.0f, 1.3f), stop = 0.3f, stay = new Vector2(120f, 300f), likes = new[] { (TownSpotKind.LeanWall, 1f), (TownSpotKind.Bench, 0.8f), (TownSpotKind.Viewpoint, 0.7f) },
                chatsWith = new byte[0], chat = 0f, declines = true, crowding = 2f, outChance = new Vector4(0.4f, 0.4f, 0.4f, 0.4f), rain = RainBehaviour.Ignore, reaction = R(0f, 0f, 3f),
                barks = new[] { "..." } },
            new Draft { id = TownPersonalityIds.NosyLocal, name = "Nosy Local", file = "TP_NosyLocal", notes = "Leans where the action is; spots near players score more; watches longest.",
                walk = new Vector2(1.0f, 1.2f), stop = 0.6f, stay = new Vector2(30f, 90f), likes = new[] { (TownSpotKind.LeanWall, 1f), (TownSpotKind.ChatCircle, 0.6f), (TownSpotKind.Viewpoint, 0.4f) },
                chatsWith = new[] { TownPersonalityIds.Gossip, TownPersonalityIds.NosyLocal }, chat = 0.7f, nosy = true, crowding = 0.8f, outChance = new Vector4(0.5f, 0.7f, 0.5f, 0.1f),
                rain = RainBehaviour.Shelter, reaction = R(6f, 0.25f, 1.2f), barks = new[] { "Haven't seen you round here before.", "What's that you're carrying?" } },
            new Draft { id = TownPersonalityIds.NightOwl, name = "Night Owl", file = "TP_NightOwl", notes = "Out after dusk; only lit spots at night.",
                walk = new Vector2(1.0f, 1.3f), stop = 0.4f, stay = new Vector2(60f, 180f), likes = new[] { (TownSpotKind.LeanWall, 1f), (TownSpotKind.Bench, 0.8f), (TownSpotKind.Viewpoint, 0.6f), (TownSpotKind.ChatCircle, 0.3f) },
                chatsWith = new[] { TownPersonalityIds.NightOwl, TownPersonalityIds.Loner }, chat = 0.4f, needsLight = true, crowding = 1.2f, outChance = new Vector4(0f, 0.1f, 0.5f, 0.9f),
                rain = RainBehaviour.Shelter, reaction = R(1f, 0.05f, 1.5f), barks = new[] { "Best time of day, this.", "The lamps are pretty from the rail." } },
            new Draft { id = TownPersonalityIds.StreetPerformer, name = "Street Performer", file = "TP_StreetPerformer", notes = "Plays at a perform spot for a long stretch; packs up in rain.",
                walk = new Vector2(1.0f, 1.0f), stop = 1f, stay = new Vector2(300f, 600f), likes = new[] { (TownSpotKind.Perform, 1f) },
                chatsWith = new byte[0], chat = 0f, crowding = 1f, outChance = new Vector4(0f, 0.7f, 0.8f, 0.2f), rain = RainBehaviour.PackUp, reaction = R(2f, 0.3f, 0f, plays: true),
                barks = new[] { "A coin for the song, traveller?", "This one's for the sky!" } },
            new Draft { id = TownPersonalityIds.Guard, name = "Guard", file = "TP_Guard", notes = "Walks post to post on a shift; watches runners.",
                walk = new Vector2(1.3f, 1.3f), stop = 1f, stay = new Vector2(60f, 120f), likes = new[] { (TownSpotKind.GuardPost, 1f), (TownSpotKind.Gate, 0.8f) },
                chatsWith = new[] { TownPersonalityIds.Guard }, chat = 0.2f, crowding = 1f, outChance = new Vector4(1f, 1f, 1f, 1f),
                shifts = new[] { new Vector2Int(6, 18), new Vector2Int(18, 6) }, rain = RainBehaviour.Ignore, reaction = R(2f, 0.2f, 1f, runners: true),
                barks = new[] { "Keep it civil, Concord's neutral ground.", "Walk, don't run." } },
        };

        static Dictionary<byte, TownsfolkPersonality> EnsurePersonalities()
        {
            var made = new Dictionary<byte, TownsfolkPersonality>();
            var fresh = new List<(TownsfolkPersonality p, Draft d)>();
            foreach (Draft d in Drafts)
            {
                TownsfolkPersonality p = LoadOrCreate<TownsfolkPersonality>(TownsfolkPaths.PersonalityFolder + "/" + d.file + ".asset", out bool isNew);
                made[d.id] = p;
                if (isNew) fresh.Add((p, d));
            }
            // only new drafts are filled: an asset the owner edited is never overwritten
            foreach ((TownsfolkPersonality p, Draft d) in fresh)
            {
                p.id = d.id;
                p.displayName = d.name;
                p.notes = "DRAFT (owner edits): " + d.notes;
                p.walkSpeed = d.walk;
                p.jogs = d.jogs;
                if (d.jogs) p.jogSpeed = d.jog;
                p.stopChance = d.stop;
                p.staySeconds = d.stay;
                p.likes = d.likes.Select(l => new TownsfolkPersonality.SpotLike { kind = l.kind, weight = l.w }).ToArray();
                p.crowdingDislike = d.crowding;
                p.needsLight = d.needsLight;
                p.likesPlayersNear = d.nosy;
                p.chatsWith = d.chatsWith.Select(id => made[id]).ToArray();
                p.chatChance = d.chat;
                p.declinesInvites = d.declines;
                p.outChance = d.outChance;
                p.shifts = d.shifts ?? Array.Empty<Vector2Int>();
                p.rain = d.rain;
                p.reaction = d.reaction;
                p.barks = d.barks.Select(t => new NpcLine { text = t }).ToArray();
                EditorUtility.SetDirty(p);
            }
            return made;
        }

        // ---- Sky Concord ----

        static TownsfolkCast EnsureCast()
        {
            TownsfolkCast cast = LoadOrCreate<TownsfolkCast>(TownsfolkPaths.SkyConcordCast, out bool isNew);
            if (!isNew) return cast;
            cast.id = 1;
            // a neutral town mixes every faction (owner's DAY list item 18)
            cast.factions = new[]
            {
                new TownsfolkCast.FactionWeight { faction = FactionId.Hegemony, weight = 1f },
                new TownsfolkCast.FactionWeight { faction = FactionId.Null, weight = 1f },
                new TownsfolkCast.FactionWeight { faction = FactionId.Covenant, weight = 1f },
            };
            cast.feminineShare = 0.5f;
            cast.dressed = true;
            EditorUtility.SetDirty(cast);
            return cast;
        }

        static TownLifeArea.Spot S(string label, TownSpotKind kind, float x, float z, float yaw = 0f, int slots = 1, float spacing = 1.2f,
                                   TownSpotFlags flags = TownSpotFlags.None, int from = 0, int to = 0, TownsfolkPersonality only = null) =>
            new TownLifeArea.Spot { label = label, kind = kind, at = new Vector2(x, z), yaw = yaw, slots = slots, spacing = spacing, flags = flags, openFrom = from, openTo = to, onlyFor = only };

        /// <summary>
        /// Sky Concord's area (a FIRST PASS, in Concord 040's own frame, +Z north, from Concord040Setup's vendor spots): window-shopping in
        /// front of every vendor, work spots beside the shops, chat circles and benches on the plaza, lean walls by the district fronts,
        /// views at the rails, two perform spots with an audience each, guard posts and the gate at the sky lift, home doors in the
        /// districts and a jog loop. The server snaps each onto the NavMesh and drops (and counts) any that are off it.
        /// </summary>
        static TownLifeArea EnsureArea(TownsfolkCast cast, Dictionary<byte, TownsfolkPersonality> p)
        {
            TownLifeArea area = LoadOrCreate<TownLifeArea>(TownsfolkPaths.SkyConcordArea, out bool isNew);
            if (!isNew) return area;
            area.rootName = "Concord 040 (sky preview)";
            area.radius = 90f;
            area.cast = cast;
            area.seed = 0x5C0;
            TownsfolkPersonality worker = p[TownPersonalityIds.BusyWorker], guard = p[TownPersonalityIds.Guard], performer = p[TownPersonalityIds.StreetPerformer];
            const TownSpotFlags Lit = TownSpotFlags.Lit, Cover = TownSpotFlags.Sheltered;
            var spots = new List<TownLifeArea.Spot>
            {
                // window-shopping at the vendors (west side faces east, east side faces west), open 08-20
                S("Vaultkeeper front", TownSpotKind.VendorStall, -57f, 49f, 0f, 2, 1.2f, Cover, 8, 20),
                S("Broker front", TownSpotKind.VendorStall, -60.5f, 24.5f, 270f, 2, 1.2f, Cover, 8, 20),
                S("Apothecary front", TownSpotKind.VendorStall, -58.6f, -57f, 0f, 2, 1.2f, Cover, 8, 20),
                S("Fabricator front", TownSpotKind.VendorStall, 53.5f, -23.8f, 90f, 2, 1.2f, Cover, 8, 20),
                S("Fixer front", TownSpotKind.VendorStall, 53.5f, -2.8f, 90f, 2, 1.2f, Cover, 8, 20),
                S("Armorer front", TownSpotKind.VendorStall, 53.5f, 18.2f, 90f, 2, 1.2f, Cover, 8, 20),
                S("Herald front", TownSpotKind.VendorStall, 54f, 52.9f, 90f, 2, 1.2f, Cover, 8, 20),
                S("Holoweaver front", TownSpotKind.VendorStall, 53.5f, -60.4f, 90f, 2, 1.2f, Cover, 8, 20),
                S("Store Clerk front", TownSpotKind.VendorStall, 54f, -48.9f, 90f, 2, 1.2f, Cover, 8, 20),
                S("Wayfinder front", TownSpotKind.VendorStall, 17f, -63f, 180f, 2, 1.2f, TownSpotFlags.None, 0, 0),
                // work spots beside the shops (Busy Workers only)
                S("Fabricator work", TownSpotKind.Work, 56f, -27f, 270f, 1, 1f, Cover, 0, 0, worker),
                S("Fixer work", TownSpotKind.Work, 56f, -6f, 270f, 1, 1f, Cover, 0, 0, worker),
                S("Armorer work", TownSpotKind.Work, 56f, 21.5f, 270f, 1, 1f, Cover, 0, 0, worker),
                S("Store work", TownSpotKind.Work, 56f, -45.5f, 270f, 1, 1f, Cover, 0, 0, worker),
                S("Broker work", TownSpotKind.Work, -62f, 28f, 90f, 1, 1f, Cover, 0, 0, worker),
                S("Apothecary work", TownSpotKind.Work, -55f, -60f, 180f, 1, 1f, Cover, 0, 0, worker),
                S("Vault work", TownSpotKind.Work, -53f, 52f, 180f, 1, 1f, Cover, 0, 0, worker),
                S("Wayfinder work", TownSpotKind.Work, 13.5f, -66f, 180f, 1, 1f, TownSpotFlags.None, 0, 0, worker),
                // chat circles on the plaza
                S("Plaza circle", TownSpotKind.ChatCircle, 0f, 0f, 0f, 4, 1.1f, Lit),
                S("North circle", TownSpotKind.ChatCircle, 0f, 32f, 30f, 3, 1.0f, Lit),
                S("West circle", TownSpotKind.ChatCircle, -28f, 8f, 0f, 3, 1.0f),
                S("East circle", TownSpotKind.ChatCircle, 28f, -8f, 0f, 3, 1.0f),
                S("South circle", TownSpotKind.ChatCircle, -10f, -38f, 15f, 3, 1.0f, Lit),
                S("Market circle", TownSpotKind.ChatCircle, 38f, 34f, 0f, 3, 1.0f),
                // benches (no sit clip in the Synty packs we have: they rest standing there)
                S("Plaza bench W", TownSpotKind.Bench, -12f, -18f, 90f, 2, 0.8f, Lit),
                S("Plaza bench E", TownSpotKind.Bench, 12f, 18f, 270f, 2, 0.8f, Lit),
                S("West bench", TownSpotKind.Bench, -36f, -4f, 90f, 2, 0.8f),
                S("East bench", TownSpotKind.Bench, 36f, 4f, 270f, 2, 0.8f),
                S("South bench", TownSpotKind.Bench, 0f, -26f, 0f, 2, 0.8f, Lit),
                S("North bench", TownSpotKind.Bench, 0f, 24f, 180f, 2, 0.8f, Lit),
                // lean walls by the district fronts
                S("Vault wall", TownSpotKind.LeanWall, -50f, 40f, 90f, 2, 1.2f, Lit | TownSpotFlags.FacesPlaza),
                S("Broker wall", TownSpotKind.LeanWall, -50f, 12f, 90f, 2, 1.2f, TownSpotFlags.FacesPlaza),
                S("Apothecary wall", TownSpotKind.LeanWall, -48f, -46f, 90f, 2, 1.2f, Cover),
                S("Armorer wall", TownSpotKind.LeanWall, 50f, 8f, 270f, 2, 1.2f, Lit | TownSpotFlags.FacesPlaza),
                S("Store wall", TownSpotKind.LeanWall, 50f, -36f, 270f, 2, 1.2f, Cover),
                S("Herald wall", TownSpotKind.LeanWall, 48f, 44f, 270f, 2, 1.2f, Lit),
                // views at the rails (dropped by the server if off the deck)
                S("North rail", TownSpotKind.Viewpoint, 0f, 72f, 0f, 3, 1.5f, TownSpotFlags.Quiet),
                S("East rail", TownSpotKind.Viewpoint, 72f, 0f, 90f, 3, 1.5f, TownSpotFlags.Quiet),
                S("West rail", TownSpotKind.Viewpoint, -72f, 0f, 270f, 3, 1.5f, TownSpotFlags.Quiet | Lit),
                S("South-west rail", TownSpotKind.Viewpoint, -40f, -68f, 200f, 3, 1.5f, TownSpotFlags.Quiet),
                S("North-east rail", TownSpotKind.Viewpoint, 45f, 66f, 30f, 3, 1.5f, TownSpotFlags.Quiet),
                // perform spots and their audiences (the audience is a view facing the performer)
                S("Plaza stage", TownSpotKind.Perform, 6f, 8f, 180f, 1, 1f, Lit, 0, 0, performer),
                S("Plaza stage crowd", TownSpotKind.Viewpoint, 6f, 3f, 0f, 4, 1.2f, Lit),
                S("West stage", TownSpotKind.Perform, -24f, -18f, 90f, 1, 1f, TownSpotFlags.None, 0, 0, performer),
                S("West stage crowd", TownSpotKind.Viewpoint, -19f, -18f, 270f, 3, 1.2f),
                // guard posts and the gate at the sky lift
                S("Lift post W", TownSpotKind.GuardPost, -4f, -54f, 0f, 1, 1f, Lit, 0, 0, guard),
                S("Lift post E", TownSpotKind.GuardPost, 4f, -54f, 0f, 1, 1f, Lit, 0, 0, guard),
                S("Hall post", TownSpotKind.GuardPost, 0f, 44f, 180f, 1, 1f, Lit, 0, 0, guard),
                S("West post", TownSpotKind.GuardPost, -44f, 0f, 90f, 1, 1f, TownSpotFlags.None, 0, 0, guard),
                S("East post", TownSpotKind.GuardPost, 44f, 0f, 270f, 1, 1f, TownSpotFlags.None, 0, 0, guard),
                S("Lift gate", TownSpotKind.Gate, 0f, -50f, 180f, 2, 2f, Lit),
                // shelter under the shop canopies
                S("East canopy", TownSpotKind.Shelter, 54f, -12f, 90f, 3, 1.2f, Cover),
                S("West canopy", TownSpotKind.Shelter, -56f, 0f, 270f, 3, 1.2f, Cover),
                // home doors in the districts (townsfolk come out of and go back into these)
                S("North-west door", TownSpotKind.HomeDoor, -60f, 40f),
                S("South-west door", TownSpotKind.HomeDoor, -60f, -40f),
                S("North-east door", TownSpotKind.HomeDoor, 60f, 35f),
                S("South-east door", TownSpotKind.HomeDoor, 60f, -35f),
                S("Hall door", TownSpotKind.HomeDoor, 0f, 50f),
                S("Lift door W", TownSpotKind.HomeDoor, -20f, -60f),
                S("Lift door E", TownSpotKind.HomeDoor, 20f, -60f),
            };
            // the jog loop: eight points round the plaza
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                spots.Add(S("Jog " + (i + 1), TownSpotKind.JogPoint, Mathf.Sin(a) * 42f, Mathf.Cos(a) * 42f, a * Mathf.Rad2Deg + 90f));
            }
            area.spots = spots.ToArray();
            // the plan's proposed mix (day 48, night 20)
            (byte id, int day, int night)[] mix =
            {
                (TownPersonalityIds.BusyWorker, 8, 3), (TownPersonalityIds.Stroller, 8, 2), (TownPersonalityIds.Shopper, 8, 0), (TownPersonalityIds.Gossip, 6, 1),
                (TownPersonalityIds.NosyLocal, 4, 0), (TownPersonalityIds.Guard, 4, 4), (TownPersonalityIds.Jogger, 3, 0), (TownPersonalityIds.Loner, 3, 3),
                (TownPersonalityIds.NightOwl, 2, 6), (TownPersonalityIds.StreetPerformer, 2, 1),
            };
            area.mix = mix.Select(m => new TownLifeArea.MixRow { personality = p[m.id], day = m.day, night = m.night }).ToArray();
            EditorUtility.SetDirty(area);
            return area;
        }

        // ---- the shared animator (one controller, one per body type: the same states, the body type's own clips where there are) ----

        static readonly (TownActivity activity, string[] clips)[] ActivityClips =
        {
            (TownActivity.Stand, new[] { "BL_Idle_Standing", "IDL_Base", "IDL_WeightShift_L", "IDL_CheckWatch" }),
            (TownActivity.Sit, new[] { "IDL_ArmsFolded_Casual_Loop", "IDL_Bored_SlumpBack", "IDL_UsePhone_OneHand_Loop", "IDL_Drink_Hold_Loop" }),
            (TownActivity.Lean, new[] { "IDL_Lean_B_ArmsFolded_Loop", "IDL_Lean_B_Base_Loop", "IDL_Lean_B_ArmsFoldedLegUp_Loop", "IDL_Lean_L_HandOnHip_Loop" }),
            (TownActivity.Chat, new[] { "IDL_Thoughtful_R_Loop", "IDL_HandsOnHips_Base_Loop", "IDL_PointHand_Index_F", "IDL_HeadNod_Large" }),
            (TownActivity.Shop, new[] { "IDL_Thoughtful_L_ChinScratch", "IDL_PointHand_Index_F_Small", "IDL_Thoughtful_L_Loop", "IDL_Inspect_Hands" }),
            (TownActivity.Work, new[] { "IDL_UsePhone_TwoHands_Typing", "IDL_Inspect_Hands", "IDL_PointHand_Index_L", "IDL_Thoughtful_R_Loop" }),
            (TownActivity.Perform, new[] { "EMOT_Dance_Twist", "EMOT_Celebrate_AirGuitar", "EMOT_Dance_RunningStep", "EMOT_Dance_ChestPump" }),
            (TownActivity.Watch, new[] { "IDL_ArmsFolded_Casual_Loop", "EMOT_Celebrate_Clapping_Polite", "IDL_HandsOnHips_Base_Loop", "BL_Idle_Standing" }),
            (TownActivity.Guard, new[] { "IDL_HandsOnHips_Base_Loop", "IDL_ArmsFolded_Grumpy_Loop", "IDL_Posture_Aggressive_Loop", "BL_Idle_Standing" }),
            (TownActivity.Shelter, new[] { "IDL_ArmsFolded_Casual_Loop", "IDL_CheckWatch", "IDL_UsePhone_OneHand_Scrolling", "IDL_Bored_FootTap" }),
            (TownActivity.Stretch, new[] { "IDL_Stretch_Arms", "IDL_Stretch_Calf", "IDL_Stretch_Quad", "IDL_Stretch_Shoulders" }),
        };

        static Dictionary<string, AnimationClip> s_Clips;

        /// <summary>A Sidekick clip by the middle of its name (<c>A_MOD_&lt;name&gt;_Femn</c> / <c>_Masc</c>): the body type's own, else the other's.</summary>
        static AnimationClip Clip(string name, bool feminine)
        {
            if (s_Clips == null)
            {
                s_Clips = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
                foreach (string guid in AssetDatabase.FindAssets("t:Model A_MOD_", ClipFolders.Where(AssetDatabase.IsValidFolder).ToArray()))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = Path.GetFileNameWithoutExtension(path);
                    if (file.Contains("_RM_")) continue;   // the root-motion copies: the brain moves the body
                    AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview", StringComparison.Ordinal));
                    if (clip != null) s_Clips[file] = clip;
                }
            }
            string own = "A_MOD_" + name + (feminine ? "_Femn" : "_Masc"), other = "A_MOD_" + name + (feminine ? "_Masc" : "_Femn");
            return s_Clips.TryGetValue(own, out AnimationClip a) ? a : s_Clips.TryGetValue(other, out AnimationClip b) ? b : null;
        }

        static (RuntimeAnimatorController, RuntimeAnimatorController) BuildAnimators()
        {
            s_Clips = null;
            return (BuildAnimator(MasculineController, false), BuildAnimator(FeminineController, true));
        }

        /// <summary>
        /// Speed blends idle, walk and jog (the Move state, for walking, jogging and none); each activity has up to four variants (the seed
        /// picks one through Offset); a clip that does not loop is restarted by a transition to itself. Rewritten every run.
        /// </summary>
        static RuntimeAnimatorController BuildAnimator(string path, bool feminine)
        {
            AssetDatabase.DeleteAsset(path);
            AnimatorController ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("Speed", AnimatorControllerParameterType.Float);
            ac.AddParameter("Activity", AnimatorControllerParameterType.Int);
            ac.AddParameter("Offset", AnimatorControllerParameterType.Float);
            AnimatorStateMachine sm = ac.layers[0].stateMachine;
            AnimationClip idle = Clip("BL_Idle_Standing", feminine), walk = Clip("BL_Walk_F", feminine), run = Clip("BL_Run_F", feminine);
            if (idle == null || walk == null || run == null) Debug.LogError("[KnBuild] Townsfolk animator: the Synty idle, walk or run clip is missing (" + path + ").");

            AnimatorState move = ac.CreateBlendTreeInController("Move", out BlendTree tree, 0);
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            if (idle != null) tree.AddChild(idle, 0f);
            if (walk != null) tree.AddChild(walk, 1.4f);
            if (run != null) tree.AddChild(run, 3.5f);
            move.cycleOffsetParameter = "Offset";
            move.cycleOffsetParameterActive = true;
            sm.defaultState = move;
            foreach (TownActivity moving in new[] { TownActivity.None, TownActivity.Walk, TownActivity.Jog })
                Any(sm, move, (int)moving, -1, 0.25f);

            int missing = 0;
            foreach ((TownActivity activity, string[] names) in ActivityClips)
            {
                var clips = names.Select(n => Clip(n, feminine)).ToList();
                missing += clips.Count(c => c == null);
                clips.RemoveAll(c => c == null);
                if (clips.Count == 0) clips.Add(idle);
                for (int v = 0; v < clips.Count; v++)
                {
                    AnimatorState st = sm.AddState(activity + " " + (v + 1));
                    st.motion = clips[v];
                    st.cycleOffsetParameter = "Offset";
                    st.cycleOffsetParameterActive = true;
                    if (clips[v] != null && !clips[v].isLooping)
                    {
                        AnimatorStateTransition again = st.AddTransition(st);
                        again.hasExitTime = true;
                        again.exitTime = 1f;
                        again.duration = 0.2f;
                    }
                    Any(sm, st, (int)activity, clips.Count > 1 ? v : -1, 0.35f, clips.Count);
                }
            }
            if (missing > 0) Debug.Log($"[KnBuild] Townsfolk animator {Path.GetFileName(path)}: {missing} wanted clips not found (their activity plays its other variants).");
            EditorUtility.SetDirty(ac);
            return ac;
        }

        /// <summary>From any state to <paramref name="to"/> while Activity is <paramref name="activity"/> (and Offset in the variant's share).</summary>
        static void Any(AnimatorStateMachine sm, AnimatorState to, int activity, int variant, float duration, int variants = 1)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(to);
            t.canTransitionToSelf = false;
            t.hasExitTime = false;
            t.duration = duration;
            t.AddCondition(AnimatorConditionMode.Equals, activity, "Activity");
            if (variant < 0) return;
            float lo = (float)variant / variants, hi = (float)(variant + 1) / variants;
            if (variant > 0) t.AddCondition(AnimatorConditionMode.Greater, lo, "Offset");
            if (variant < variants - 1) t.AddCondition(AnimatorConditionMode.Less, hi, "Offset");
        }

        // ---- Resources ----

        static void EnsureResources(TownsfolkSettings settings, TownsfolkPersonality[] personalities, TownLifeArea area, TownsfolkCast cast,
                                    RuntimeAnimatorController masc, RuntimeAnimatorController femn)
        {
            var world = LoadOrCreate<TownsfolkWorldAssets>(ResourcesFolder + "/" + TownsfolkWorldAssets.ResourcePath + ".asset", out _);
            world.brain = AssetDatabase.LoadAssetAtPath<KryticalNova.Brains.BrainSettings>(KryticalNova.Brains.BrainPaths.SettingsAsset);
            world.settings = settings;
            world.personalities = personalities;
            world.areas = new[] { area };
            EditorUtility.SetDirty(world);
            var view = LoadOrCreate<TownsfolkViewAssets>(ResourcesFolder + "/" + TownsfolkViewAssets.ResourcePath + ".asset", out _);
            view.settings = settings;
            view.body = AssetDatabase.LoadAssetAtPath<GameObject>(BodyPrefab);
            view.masculine = masc;
            view.feminine = femn;
            view.database = AssetDatabase.LoadAssetAtPath<CharacterCreationDatabase>(DatabaseAsset);
            view.casts = new[] { cast };
            view.personalities = personalities;
            EditorUtility.SetDirty(view);
            if (world.brain == null || view.body == null || view.database == null)
                Debug.LogError("[KnBuild] Townsfolk Resources: the Brain settings, the body (" + BodyPrefab + ") or the creation database is missing.");
        }
    }
}
