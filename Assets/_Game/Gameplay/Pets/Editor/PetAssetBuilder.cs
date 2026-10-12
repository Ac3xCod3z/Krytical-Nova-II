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
            /// <summary>The animation whose first frame is the pose it rests in, for a model whose file holds another pose.</summary>
            public string RestPose;
            /// <summary>Animations followed by another instead of going back to rest: (the animation, how many times it plays, what follows).</summary>
            public (string Animation, int Times, string Then)[] FollowOns;
            /// <summary>What it says in its animations: (the animation, the share of it at which it is said, the words, the stages it is said at).</summary>
            public (string Animation, float At, string Text, Vector2Int Stages)[] Lines;
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
            /// <summary>Worn on a bone, it looks down the bone, and its offset is in the bone's frame in metres of the model (z = down the bone).</summary>
            public bool Along;
            /// <summary>Taken away at once when its animation is cut short.</summary>
            public bool Ends;
        }

        // The Drone Core's stages: every form, the four grown ones, those with a cannon, the two with a minigun, the last.
        private static readonly Vector2Int Grown = new Vector2Int(1, PetRules.LastStage);
        private static readonly Vector2Int Minigun = new Vector2Int(2, 3);
        private static readonly Vector2Int Gunship = new Vector2Int(PetRules.LastStage, PetRules.LastStage);

        /// <summary>
        /// The Drone Core's effects. Its data chips at every stage; the camera flash, the scan, the blur of a spin and
        /// the steam of a temper for the forms that have those animations; and from 50 % what its cannon does, each
        /// at the muzzle of that form's own barrel (the cannon bone's length: 0.137, 0.170 and 0.178 m).
        /// </summary>
        private static Fx[] DroneCoreEffects()
        {
            var cannon = new[] { "cannon" };
            var list = new List<Fx>
            {
                // Chips of data dropped behind it as it goes.
                new Fx { Effect = GameFeelBuilder.PetCrumbsPath, Bones = new[] { "body" }, StageColour = true },
                // Taking a photo: the flash goes off on frame 34 of 80.
                new Fx { Effect = GameFeelBuilder.DroneFlashPath, Bones = new[] { "flash" }, Animation = "BarrelTurn", At = Frames(80, 34), Stages = Grown, Size = 0.5f },
                // Looking about: a cone of scan lines out of its lens.
                new Fx { Effect = GameFeelBuilder.DroneScanPath, Bones = new[] { "lens" }, Animation = "EyeLook", Ends = true },
                // Spinning: hoops of streaks round it.
                new Fx { Effect = GameFeelBuilder.DroneWhirlPath, Animation = "AxisSpin", Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneWhirlPath, Animation = "AxisSpinDiagonal", Ends = true },
                // Angry: it lets off steam.
                new Fx { Effect = GameFeelBuilder.DroneSteamPath, Anchor = PetEffectAnchor.Top, Animation = "Mad", During = new Vector2(0.15f, 0.85f), Size = 0.25f },
                // The swarm (100 %): scan lines as the cube bursts (frame 10 of 44) and as it locks again (36 of 46),
                // arcs between the blocks while they are out, sparks on the lock (39 of 46).
                new Fx { Effect = GameFeelBuilder.DroneGlitchPath, Animation = "Disassemble", At = Frames(44, 10), Stages = Gunship },
                new Fx { Effect = GameFeelBuilder.DroneGlitchPath, Animation = "Assemble", At = Frames(46, 36), Stages = Gunship },
                new Fx { Effect = GameFeelBuilder.DroneArcsPath, Animation = "Disassemble", During = new Vector2(10f / 44f, 1f), Stages = Gunship, Size = 0.3f, Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneArcsPath, Animation = "SwarmHover", Stages = Gunship, Size = 0.3f, Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneArcsPath, Animation = "Assemble", During = new Vector2(0.001f, 36f / 46f), Stages = Gunship, Size = 0.3f, Ends = true },
                new Fx { Effect = GameFeelBuilder.DroneLockPath, Animation = "Assemble", At = Frames(46, 39), Stages = Gunship, Size = 0.35f },
            };
            float[] muzzles = { 0.137f, 0.170f, 0.178f };
            for (int stage = 2; stage <= PetRules.LastStage; stage++)
            {
                var here = new Vector2Int(stage, stage);
                var muzzle = new Vector3(0f, 0f, muzzles[stage - 2]);
                // Two shots with recoil, on frames 12 and 30 of 60.
                list.Add(new Fx { Effect = GameFeelBuilder.DroneShotPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "CannonFire", At = Frames(60, 12, 30), Stages = here, Size = 0.3f });
                if (stage <= Minigun.y)
                {
                    // The barrel spins up from frame 1, fires from 14 to 60 of 80 and winds down.
                    var firing = new Vector2(14f / 80f, 60f / 80f);
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneSleevePath, Bones = cannon, Along = true, Animation = "MinigunFire", During = new Vector2(0.001f, 72f / 80f), Stages = here, Ends = true });
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneBurstPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "MinigunFire", During = firing, Stages = here, Size = 0.3f, Ends = true });
                    // The spent rounds, thrown out at the breech for as long as it fires (owner, 11 Oct 2026).
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneShellsPath, Bones = cannon, Along = true, Offset = muzzle * 0.2f, Animation = "MinigunFire", During = firing, Stages = here, Size = 0.7f });
                    // The 75 % form's pod arms kick with each shot of the cannon: a rocket leaves each (owner, 11 Oct 2026).
                    // A pod is 0.33 m long from its bone.
                    // It flies straight for a third of a second and bursts: the burst is played where it has got to,
                    // eight frames after it left (its speed at the effect's size on this form, 0.637 m across).
                    if (stage == 3)
                    {
                        const float rocketSize = 0.45f, podLength = 0.33f, footprint = 0.637f;
                        float flown = GameFeelBuilder.DroneRocketSpeed * GameFeelBuilder.DroneRocketSeconds * rocketSize * footprint;
                        var pods = new[] { "pod.L", "pod.R" };
                        list.Add(new Fx { Effect = GameFeelBuilder.DroneRocketPath, Bones = pods, Along = true, Offset = new Vector3(0f, 0f, podLength), Animation = "CannonFire", At = Frames(60, 12, 30), Stages = here, Size = rocketSize });
                        list.Add(new Fx { Effect = GameFeelBuilder.DroneBoomPath, Bones = pods, Along = true, Offset = new Vector3(0f, 0f, podLength + flown), Animation = "CannonFire", At = Frames(60, 20, 38), Stages = here, Size = 0.2f });
                    }
                }
                else
                {
                    // Eight shots off a spinning barrel, six frames apart from frame 26 of 91.
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneSleevePath, Bones = cannon, Along = true, Animation = "SpinShots", During = new Vector2(0.001f, 72f / 91f), Stages = here, Ends = true });
                    list.Add(new Fx { Effect = GameFeelBuilder.DronePopPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "SpinShots", At = Frames(91, 26, 32, 38, 44, 50, 56, 62, 68), Stages = here, Size = 0.3f });
                    // The fake-out: it gathers power through "Ka... Me... Ha!" (the thrust lands on frame 31 of 130) and
                    // only a puff comes out; the beam is on from frame 72 to 110.
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneChargePath, Bones = cannon, Along = true, Offset = muzzle, Animation = "FakeoutBlast", During = new Vector2(0.001f, 29f / 130f), Stages = here, Size = 0.3f, Ends = true });
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneFizzlePath, Bones = cannon, Along = true, Offset = muzzle, Animation = "FakeoutBlast", At = Frames(130, 31), Stages = here, Size = 0.2f });
                    list.Add(new Fx { Effect = GameFeelBuilder.DroneBeamPath, Bones = cannon, Along = true, Offset = muzzle, Animation = "FakeoutBlast", During = new Vector2(72f / 130f, 110f / 130f), Stages = here, Size = 0.45f, Ends = true });
                }
            }
            return list.ToArray();
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
                // The four grown forms are models of their own, each with more animations than the one before.
                StageModels = new[]
                {
                    PetModels + "/DroneCore_25/Pet_DroneCore_25.prefab", PetModels + "/DroneCore_50/Pet_DroneCore_50.prefab",
                    PetModels + "/DroneCore_75/Pet_DroneCore_75.prefab", PetModels + "/DroneCore_100/Pet_DroneCore_100.prefab",
                },
                // The longest side of each form with its cannon, pods and fins, at the scale the base cube is shown
                // at (0.30 m as made, 0.25 m in the game): the cubes themselves come out 0.28, 0.31, 0.35 and 0.38 m.
                StageSizes = new[] { 0.28f, 0.47f, 0.53f, 0.63f },
                // The swarm: it comes apart, hovers as blocks for two rounds, and comes together again.
                FollowOns = new[] { ("Disassemble", 1, "SwarmHover"), ("SwarmHover", 2, "Assemble") },
                // The fake-out, at 24 frames a second in a clip of 130.
                Lines = new[]
                {
                    ("FakeoutBlast", 1f / 130f, "Ka...", Gunship), ("FakeoutBlast", 13f / 130f, "Me...", Gunship), ("FakeoutBlast", 27f / 130f, "Ha!", Gunship),
                    ("FakeoutBlast", 45f / 130f, "...just kidding", Gunship), ("FakeoutBlast", 65f / 130f, "Die!", Gunship),
                },
                Effects = DroneCoreEffects(),
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
            definition.restPose = row.RestPose ?? "";
            int followCount = row.FollowOns != null ? row.FollowOns.Length : 0;
            definition.followOns = new PetFollowOn[followCount];
            for (int i = 0; i < followCount; i++)
                definition.followOns[i] = new PetFollowOn { animation = row.FollowOns[i].Animation, times = row.FollowOns[i].Times, then = row.FollowOns[i].Then };
            int lineCount = row.Lines != null ? row.Lines.Length : 0;
            definition.lines = new PetLine[lineCount];
            for (int i = 0; i < lineCount; i++)
                definition.lines[i] = new PetLine { animation = row.Lines[i].Animation, at = row.Lines[i].At, text = row.Lines[i].Text, stages = row.Lines[i].Stages };
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
                    alongBone = fx.Along,
                    endsWithAnimation = fx.Ends,
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
