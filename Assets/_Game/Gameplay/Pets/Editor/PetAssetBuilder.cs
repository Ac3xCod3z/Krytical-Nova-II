using System.Collections.Generic;
using System.Linq;
using KryticalNova.Items;
using KryticalNova.Pets;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the pets from one table (design: _Docs/Design/12_pet_evolution_system.md): the item that is each pet,
    /// its definition (stat, maximum, five stage looks), the pet catalog and the pet settings. Every pet is Common.
    /// Re-runnable. What the table owns is rewritten on every run (name, text, rarity, which model it has, whether it
    /// stands or bobs, its movement loop);
    /// numbers and stage looks are only filled in when missing, so values tuned in the Inspector survive.
    /// Also carries out the move from the old drone pets: assets and icons are renamed in place (ids and
    /// references kept) and retired pets are removed.
    /// </summary>
    public static class PetAssetBuilder
    {
        public const string PetsRoot = "Assets/_Game/Gameplay/Pets/Data/Assets";
        public const string SettingsPath = PetsRoot + "/PetSettings.asset";
        public const string CatalogPath = PetsRoot + "/PetCatalog.asset";
        private const string ItemFolder = SidekickGearGenerator.ItemsRoot + "/Pets";
        private const string Drones = "Assets/Synty/PolygonSciFiCity/Prefabs/Props/";
        /// <summary>The pets' own models, one folder per pet.</summary>
        public const string PetModels = "Assets/_Game/Characters/Pets";

        private static readonly float[] StageScales = { 1f, 1.08f, 1.16f, 1.24f, 1.35f };
        /// <summary>The size every pet is brought to at base growth (PetFollower's Normalized Size on the player prefab), before the item's own scale.</summary>
        private const float NormalisedSize = 0.55f;
        private static readonly float[] StageGlow = { 0f, 0.3f, 0.5f, 0.75f, 1f };

        private struct Row
        {
            public ushort Id;
            /// <summary>Asset name of the item, its definition ("PetDef_" + Name) and its icon.</summary>
            public string Asset, Name;
            /// <summary>The model that stands in until the creature's own art exists.</summary>
            public string StandIn;
            /// <summary>Path of the creature's own model, once it has one. It replaces the stand-in.</summary>
            public string Model;
            /// <summary>Paths of its models at 25, 50, 75 and 100 % growth, when a stage is a different creature. Empty or null = the model above at every stage.</summary>
            public string[] StageModels;
            /// <summary>How long its longest side is at 25, 50, 75 and 100 % growth, in metres. Null = the common size steps.</summary>
            public float[] StageSizes;
            /// <summary>The animation in which it charges its owner, and the bone that animation carries forward.</summary>
            public string Charge, ChargeBone;
            /// <summary>It stands instead of bobbing like a drone, on a cloud under each of these bones of its model.</summary>
            public string[] Feet;
            /// <summary>The effects that are this pet's own mark: the effect's prefab, and the bones of the model it is worn on.</summary>
            public Fx[] Effects;
            /// <summary>The true/false parameter of its Animator that holds its one movement loop, if it has no walk and run.</summary>
            public string MoveLoop;
            /// <summary>The true/false parameter that holds the loop it waits in instead of its default state, if it has one.</summary>
            public string RestLoop;
            /// <summary>Its size against the size every pet is brought to (PetFollower's normalised size); 0 = leave the item's as it is.</summary>
            public float Scale;
            public StatType Stat;
            public float Max;
            public string Description;
            /// <summary>Names of the five stages; empty = "Stage N".</summary>
            public string[] Stages;
            /// <summary>Glow colour of the five stages, from the art brief's palette.</summary>
            public string[] Glow;
            /// <summary>Asset and icon names this pet had before, renamed on the first run.</summary>
            public string OldAsset, OldIcon;
        }

        /// <summary>
        /// One effect of a pet's own: the effect's prefab, where it is worn (bones of the model, or a place on the
        /// body moved by an offset in footprints), whether it takes the growth stage's colour, and the animation it
        /// belongs to if it is not worn all the time, with the moments at which it plays or the stretch it plays for.
        /// </summary>
        private struct Fx
        {
            public string Effect;
            public string[] Bones;
            public PetEffectAnchor Anchor;
            public Vector3 Offset;
            public bool StageColour;
            /// <summary>The growth stages it is worn at, first to last (0 to 4). Null = all of them.</summary>
            public Vector2Int? Stages;
            /// <summary>Its size against the pet's footprint; 0 = as it was made.</summary>
            public float Size;
            public string Animation;
            public float[] At;
            public Vector2 During;
        }

        // The Tidecaller's stages: the egg it starts as, and the four swimmers it grows into.
        private static readonly Vector2Int Egg = new Vector2Int(0, 0);
        private static readonly Vector2Int Swimmers = new Vector2Int(1, PetRules.LastStage);

        /// <summary>Frames of a clip <paramref name="last"/> frames long, as shares of it.</summary>
        private static float[] Frames(int last, params int[] frames)
        {
            var shares = new float[frames.Length];
            for (int i = 0; i < frames.Length; i++) shares[i] = frames[i] / (float)last;
            return shares;
        }

        private static readonly Row[] Rows =
        {
            new Row { Id = 8001, Asset = "Pet_Emberwing", Name = "Emberwing", StandIn = "SM_Prop_Drone_Quad_01",
                Model = PetModels + "/Emberwing/Pet_Emberwing_S1.prefab", MoveLoop = "Move", RestLoop = "Fly",
                // Cinders shed from the tips of its wings and the end of its tail.
                Effects = new[] { new Fx { Effect = GameFeelBuilder.PetCindersPath, Bones = new[] { "wing.L.004", "wing.R.004", "tail.001" }, StageColour = true } },
                Stat = StatType.MaxChi, Max = 2000f,
                Description = "A winged mystic beast in lacquer and gold. It grows your Flux as it matures.",
                Stages = new[] { "", "", "", "", "Ascendant" },
                Glow = new[] { "D4AF37", "D4AF37", "D4AF37", "D4AF37", "2E86AB" },
                OldAsset = "Pet_Drone", OldIcon = "Pet_DroneQuad" },
            new Row { Id = 8002, Asset = "Pet_DroneCore", Name = "Drone Core", StandIn = "SM_Prop_Drone_02",
                Model = PetModels + "/DroneCore/Pet_DroneCore_S1.prefab", MoveLoop = "FrontRoll",
                // Chips of data dropped behind it as it goes.
                Effects = new[] { new Fx { Effect = GameFeelBuilder.PetCrumbsPath, Bones = new[] { "body" }, StageColour = true } },
                Scale = 0.45f, // a cube fills its whole box: at the common size it dwarfed its owner
                Stat = StatType.MaxHP, Max = 2200f,
                Description = "A boxy little robot buddy that bolts on plating and cannon pods. It grows your Health as it matures.",
                Stages = new[] { "", "", "", "", "Gunship" },
                Glow = new[] { "4FD1FF", "4FD1FF", "F2C230", "4FD1FF", "4FD1FF" },
                OldAsset = "Pet_DroneHeavy", OldIcon = "Pet_DroneHeavy" },
            new Row { Id = 8003, Asset = "Pet_Tidecaller", Name = "Tidecaller", StandIn = "SM_Prop_Drone_Ball_01",
                Model = PetModels + "/Tidecaller/Pet_Tidecaller_S1.prefab", MoveLoop = "Move",
                // It hatches: from 25 % on it is a swimmer, larger and more armoured at each stage. The four share
                // one rig and one set of animations, which are not the egg's.
                StageModels = new[]
                {
                    PetModels + "/Tidecaller_25/Pet_Tidecaller_25.prefab", PetModels + "/Tidecaller_50/Pet_Tidecaller_50.prefab",
                    PetModels + "/Tidecaller_75/Pet_Tidecaller_75.prefab", PetModels + "/Tidecaller_100/Pet_Tidecaller_100.prefab",
                },
                // The egg is 0.30 m; each form after it is 0.1 m larger than the one before (owner, 10 Oct 2026).
                StageSizes = new[] { 0.4f, 0.5f, 0.6f, 0.7f },
                // The swimmers' Ram: it turns on its owner and butts them.
                Charge = "Ram", ChargeBone = "body",
                // Its effects belong to its animations. Moments and stretches are frames of the clip (24 a second).
                // The first five are the egg's (stage 0), the rest the swimmers' (stages 1 to 4).
                Effects = new[]
                {
                    // A hop: dust where it leaves the floor (frame 7 of 35) and where it comes down (23).
                    new Fx { Effect = GameFeelBuilder.PetHopDustPath, Anchor = PetEffectAnchor.Feet, Animation = "HopSpin", At = Frames(35, 7, 23), Stages = Egg },
                    // The victory bounce comes down three times: frames 17, 32 and 50 of 75.
                    new Fx { Effect = GameFeelBuilder.PetHopDustPath, Anchor = PetEffectAnchor.Feet, Animation = "VictoryBounce", At = Frames(75, 17, 32, 50), Stages = Egg },
                    // Rolling along: dust that runs after it, for as long as the roll plays.
                    new Fx { Effect = GameFeelBuilder.PetRollDustPath, Anchor = PetEffectAnchor.Feet, Animation = "Move", Stages = Egg },
                    // The nap: it dozes from frame 20 to 105 of 149 and starts awake at 110. Zs in a bubble over its top.
                    new Fx { Effect = GameFeelBuilder.PetZzzPath, Anchor = PetEffectAnchor.Top, Offset = new Vector3(0f, 0.72f, 0f),
                        Animation = "WobbleNap", During = new Vector2(20f / 149f, 105f / 149f), Stages = Egg },
                    // The tide call: it leans back from frame 10, heaves forward at 38 and again at 55 of 95. The wave is
                    // called at 21, gathers behind it for 0.6 s, runs past it and breaks in front at 55.
                    new Fx { Effect = GameFeelBuilder.PetTideWavePath, Anchor = PetEffectAnchor.Feet, Animation = "TideCall", At = Frames(95, 21), Stages = Egg },

                    // The swimmers' tide call is the same wave: they rear up from frame 28 of 123 and beat down at 62
                    // and at 81. Called at 47, it gathers behind them while they are reared and breaks in front on the
                    // second beat (34 frames later, as for the egg). The wave was made for the egg's footprint: the
                    // swimmers are half as wide again with their fins out, and at full size it buried their owner's head.
                    new Fx { Effect = GameFeelBuilder.PetTideWavePath, Anchor = PetEffectAnchor.Feet, Animation = "TideCall", At = Frames(123, 47), Stages = Swimmers, Size = 0.55f },
                    // Swimming along: water that parts at the nose, runs back round the body and off behind.
                    new Fx { Effect = GameFeelBuilder.PetTideFlowPath, Anchor = PetEffectAnchor.Body, Animation = "Move", Stages = Swimmers },
                    // The backward somersault turns over between frames 24 and 68 of 99: water is dragged round with it.
                    new Fx { Effect = GameFeelBuilder.PetTideSwirlPath, Anchor = PetEffectAnchor.Body, Animation = "SwimFlip",
                        During = new Vector2(20f / 99f, 70f / 99f), Stages = Swimmers },
                    // The ram lands on frame 22 of 83: a comic bump where its nose meets its owner.
                    new Fx { Effect = GameFeelBuilder.PetRamBumpPath, Bones = new[] { "body" }, Offset = new Vector3(0f, 0.1f, 0.5f),
                        Animation = "Ram", At = Frames(83, 22), Stages = Swimmers, Size = 0.55f }, // a little bump, even on the largest
                },
                Scale = 0.55f, // an egg is nearly as wide as it is tall: at the common size it was as big as its owner's chest
                Stat = StatType.DefensePower, Max = 2200f,
                Description = "A pocket-sized kaiju that never outgrows its owner's shoulder. It grows your Defense as it matures.",
                Stages = new[] { "Spawnling", "", "", "", "Apex" },
                Glow = new[] { "5AD8C6", "5AD8C6", "5AD8C6", "3FA7D6", "3FA7D6" },
                OldAsset = "Pet_DroneBall", OldIcon = "Pet_DroneBall" },
            new Row { Id = 8004, Asset = "Pet_KitsuneSpirit", Name = "Kitsune Spirit", StandIn = "SM_Prop_Drone_Insect_01",
                Model = PetModels + "/KitsuneSpirit/Pet_KitsuneSpirit_S1.prefab",
                Feet = new[] { "DEF-front_toe.L", "DEF-front_toe.R", "DEF-toe.L", "DEF-toe.R" },
                Stat = StatType.AttackPower, Max = 1100f,
                Description = "A fox spirit wrapped in paper talismans, gaining tails as it grows. It grows your Attack as it matures.",
                Stages = new[] { "One Tail", "Three Tails", "Five Tails", "Seven Tails", "Nine Tails" },
                Glow = new[] { "FF8A3D", "FF8A3D", "FF8A3D", "FF8A3D", "FFD23F" },
                OldAsset = "Pet_DroneInsect", OldIcon = "Pet_DroneInsect" },
        };

        /// <summary>Pets that no longer exist: (item asset, icon). Their item ids (8005) are never handed out again.</summary>
        private static readonly (string Asset, string Icon)[] Retired = { ("Pet_DroneQuad", "Pet_Drone") };

        [MenuItem("Krytical Nova/Items/7. Build Pets")]
        public static void Build()
        {
            WeatherVfxBuilder.EnsureFolder(PetsRoot);
            WeatherVfxBuilder.EnsureFolder(ItemFolder);

            var settings = LoadOrCreate<PetSettings>(SettingsPath);
            if (settings.auraPrefab == null)
            {
                settings.auraPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PetGlowPath);
                if (settings.auraPrefab == null)
                    Debug.LogWarning("[Pets] No pet glow effect yet. Run Krytical Nova/GameFeel/Rebuild Pet Feel, then build the pets again.");
                EditorUtility.SetDirty(settings);
            }
            var items = new List<ItemDefinition>(Rows.Length);
            var definitions = new List<PetDefinition>(Rows.Length);
            foreach (Row row in Rows)
            {
                ItemDefinition item = Item(row);
                items.Add(item);
                definitions.Add(Definition(row));
            }

            var removed = new List<ItemDefinition>();
            foreach ((string asset, string icon) in Retired)
            {
                string itemPath = $"{ItemFolder}/{asset}.asset";
                var gone = AssetDatabase.LoadAssetAtPath<ItemDefinition>(itemPath);
                if (gone != null)
                {
                    removed.Add(gone);
                    AssetDatabase.DeleteAsset(itemPath);
                }
                string iconPath = $"{ItemIconCapture.IconFolder}/{icon}.png";
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath) != null && !items.Any(i => AssetDatabase.GetAssetPath(i.icon) == iconPath))
                    AssetDatabase.DeleteAsset(iconPath);
            }

            var catalog = LoadOrCreate<PetCatalog>(CatalogPath);
            catalog.SetContents(definitions, settings);
            EditorUtility.SetDirty(catalog);

            RegisterItems(items, removed);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Pets] {items.Count} pets built: {string.Join(", ", Rows.Select(r => r.Name))}. " +
                      "Run Krytical Nova/Setup/Player Prefab if the player has no PlayerPet yet.");
        }

        private static ItemDefinition Item(in Row row)
        {
            string path = $"{ItemFolder}/{row.Asset}.asset";
            string oldPath = $"{ItemFolder}/{row.OldAsset}.asset";
            if (AssetDatabase.LoadAssetAtPath<ItemDefinition>(path) == null && AssetDatabase.LoadAssetAtPath<ItemDefinition>(oldPath) != null)
            {
                string error = AssetDatabase.RenameAsset(oldPath, row.Asset);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Pets] Could not rename {oldPath}: {error}");
            }

            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            if (item == null)
            {
                item = ScriptableObject.CreateInstance<ItemDefinition>();
                item.itemId = row.Id;
                item.petScale = 1f; // multiplier on PetFollower's normalised size
                AssetDatabase.CreateAsset(item, path);
            }
            if (item.itemId != row.Id)
                Debug.LogError($"[Pets] {path} has item id {item.itemId}, the table says {row.Id}. Ids are never renumbered: fix the table or the asset.", item);

            item.displayName = row.Name;
            item.description = row.Description;
            item.rarity = ItemRarity.Common;
            item.equipSlot = EquipSlot.Pet;
            item.maxStack = 1;
            item.itemLevel = 0; // any level may use a pet
            if (row.Scale > 0f) item.petScale = row.Scale;
            string modelPath = string.IsNullOrEmpty(row.Model) ? Drones + row.StandIn + ".prefab" : row.Model;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model != null) item.petPrefab = model;
            else Debug.LogError($"[Pets] Model {modelPath} not found for {row.Name}.");

            Sprite icon = Icon(row);
            if (icon != null) item.icon = icon;
            EditorUtility.SetDirty(item);
            return item;
        }

        /// <summary>The pet's icon under its own name, renamed from the icon of the model that stands in for it.</summary>
        private static Sprite Icon(in Row row)
        {
            string path = $"{ItemIconCapture.IconFolder}/{row.Asset}.png";
            string oldPath = $"{ItemIconCapture.IconFolder}/{row.OldIcon}.png";
            if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null && AssetDatabase.LoadAssetAtPath<Sprite>(oldPath) != null)
            {
                string error = AssetDatabase.RenameAsset(oldPath, row.Asset);
                if (!string.IsNullOrEmpty(error)) Debug.LogError($"[Pets] Could not rename {oldPath}: {error}");
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static PetDefinition Definition(in Row row)
        {
            string path = $"{PetsRoot}/PetDef_{row.Asset.Substring("Pet_".Length)}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<PetDefinition>(path);
            bool created = definition == null;
            if (created)
            {
                definition = ScriptableObject.CreateInstance<PetDefinition>();
                definition.stat = row.Stat;
                definition.maxValue = row.Max;
                AssetDatabase.CreateAsset(definition, path);
            }
            definition.itemId = row.Id;
            definition.displayName = row.Name;
            definition.description = row.Description;
            definition.moveLoop = row.MoveLoop ?? "";
            definition.restLoop = row.RestLoop ?? "";
            definition.chargeAnimation = row.Charge ?? "";
            definition.chargeBone = row.ChargeBone ?? "";
            bool stands = row.Feet != null && row.Feet.Length > 0;
            definition.bobs = !stands;
            if (stands)
            {
                definition.footBones = row.Feet;
                definition.footEffect = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelBuilder.PetCloudPath);
                if (definition.footEffect == null)
                    Debug.LogWarning($"[Pets] No pet cloud effect yet for {row.Name}. Run Krytical Nova/GameFeel/Rebuild Pet Cloud, then build the pets again.");
            }

            // Its own effects are the table's: a pet with none in the table wears none.
            int effectCount = row.Effects != null ? row.Effects.Length : 0;
            definition.effects = new PetEffect[effectCount];
            for (int i = 0; i < effectCount; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(row.Effects[i].Effect);
                if (prefab == null)
                    Debug.LogWarning($"[Pets] No effect at {row.Effects[i].Effect} for {row.Name}. Run Krytical Nova/GameFeel/Rebuild Pet Effects, then build the pets again.");
                Fx fx = row.Effects[i];
                definition.effects[i] = new PetEffect
                {
                    prefab = prefab,
                    bones = fx.Bones ?? new string[0],
                    anchor = fx.Anchor,
                    offset = fx.Offset,
                    stageColour = fx.StageColour,
                    stages = fx.Stages ?? new Vector2Int(0, PetRules.LastStage),
                    size = fx.Size > 0f ? fx.Size : 1f,
                    animation = fx.Animation ?? "",
                    at = fx.At ?? new float[0],
                    during = fx.During,
                };
            }

            if (definition.stages == null || definition.stages.Length != PetRules.StageCount)
                definition.stages = new PetStageLook[PetRules.StageCount];
            for (int stage = 0; stage < PetRules.StageCount; stage++)
            {
                PetStageLook look = definition.stages[stage];
                look.name = row.Stages[stage];
                // A stage that is a creature of its own: the table's, like the pet's model.
                if (stage > 0 && row.StageModels != null && stage - 1 < row.StageModels.Length && !string.IsNullOrEmpty(row.StageModels[stage - 1]))
                {
                    var stageModel = AssetDatabase.LoadAssetAtPath<GameObject>(row.StageModels[stage - 1]);
                    if (stageModel != null) look.model = stageModel;
                    else Debug.LogError($"[Pets] Stage model {row.StageModels[stage - 1]} not found for {row.Name}.");
                }
                // A size in metres is the table's too: the stage's scale is that size against the pet's base size.
                if (stage > 0 && row.StageSizes != null && stage - 1 < row.StageSizes.Length && row.StageSizes[stage - 1] > 0f)
                    look.scale = row.StageSizes[stage - 1] / (NormalisedSize * (row.Scale > 0f ? row.Scale : 1f));
                if (look.scale <= 0f)
                {
                    // A stage that was never set up: the stand-in look of the art brief.
                    look.scale = StageScales[stage];
                    look.glow = ColorBible.Hex(row.Glow[stage]);
                    look.glowStrength = StageGlow[stage];
                }
                definition.stages[stage] = look;
            }
            EditorUtility.SetDirty(definition);
            return definition;
        }

        /// <summary>Pets are items a player owns: they belong in the item catalog, and retired ones leave it.</summary>
        private static void RegisterItems(List<ItemDefinition> pets, List<ItemDefinition> removed)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(SidekickGearGenerator.CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning("[Pets] No item catalog yet: run Krytical Nova/Items/1 and 6, then build the pets again.");
                return;
            }
            var items = catalog.Items.Where(i => i != null && !removed.Contains(i)).Concat(pets).Distinct().OrderBy(i => i.itemId).ToList();
            var equipped = catalog.StarterEquipped.Where(i => i != null && !removed.Contains(i)).ToList();
            var bag = catalog.StarterBag.Where(s => s.item != null && !removed.Contains(s.item)).ToList();
            catalog.SetContents(items, equipped, bag);
            EditorUtility.SetDirty(catalog);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
