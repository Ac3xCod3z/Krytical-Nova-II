using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Builds the gear <b>looks</b> from the Synty packs and seeds the gear dictionary's lines:
    ///  - one look template per Sidekick outfit piece (Armor / Gloves / Boots / Cape, every pack and variant),
    ///  - one per weapon mesh of each of the nine weapon classes (every mesh is Synty),
    ///  - the ring and amulet icons, and the pet items.
    /// Look templates are not items a player can own: <see cref="GearStatBaker"/> turns the dictionary's lines into
    /// the real, levelled items, copying a template's parts, icon and weapon model onto every tier that wears it.
    /// Existing assets and existing lines are never overwritten, so hand-tuned grips, names and look picks survive.
    /// </summary>
    public static class SidekickGearGenerator
    {
        public const string ItemsRoot = "Assets/_Game/Gameplay/Items/Data/Assets";
        public const string CatalogPath = ItemsRoot + "/ItemCatalog.asset";
        public const string DictionaryPath = ItemsRoot + "/GearDictionary.asset";
        public const string WeaponPrefabFolder = ItemsRoot + "/Prefabs/Weapons";
        public const string WeaponMaterialFolder = ItemsRoot + "/Materials";
        public const string OutfitFolder = "Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits/ScifiSoldiers";
        public const string HumanFolder = "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans";
        // Sidekick demo weapons ship without materials; they are UV-mapped to the Sidekick colour palette.
        private const string FallbackWeaponMaterial = "Assets/Synty/SidekickCharacters/Resources/Materials/M_BaseMaterial.mat";

        // Legacy ID ranges of the look templates and pets. Ladder items live at GearDictionary.LadderIdBase and up.
        private const ushort ArmorBase = 2000, GlovesBase = 3000, BootsBase = 4000;
        // 8000–8999 belongs to the pets (PetAssetBuilder).
        private const ushort RingBase = 5000, AmuletBase = 6000, CapeBase = 7000;

        // Band indices: the order of GearStatTable.bands (see GearStatBaker.SeedDefaults).
        public const int BandCommon = 0, BandUncommon = 1, BandRareOne = 2, BandRareTwo = 3, BandEpic = 4, BandCount = 5;
        private static readonly ItemRarity[] BandRarity =
            { ItemRarity.Common, ItemRarity.Uncommon, ItemRarity.Rare, ItemRarity.Rare, ItemRarity.Epic };
        // Where in a pack's variant list each band looks for its outfit: plainest first, most detailed last.
        private static readonly float[] BandLookFraction = { 0f, 0.25f, 0.5f, 0.75f, 1f };
        // Jewellery has no mesh, so its look is an icon: which Ring_NN / Amulet_NN template each band wears.
        private static readonly int[] BandJewelleryTemplate = { 0, 1, 2, 2, 3 };

        private static readonly Regex PartName = new Regex(@"^SK_(?<fam>[A-Z]+)_(?<set>[A-Z]+)_(?<vv>\d{2})_(?<nn>\d{2})(?<code>[A-Z]{4})_HU01$");

        private static readonly string[] OutfitNames =
            { "Recruit", "Ranger", "Sentinel", "Vanguard", "Warden", "Striker", "Phantom", "Bastion", "Raider", "Commander" };

        // ---------------------------------------------------------------- weapon classes

        private struct WeaponLook
        {
            public string pack;   // folder under Assets/Synty
            public string prefab; // prefab file name, without extension
            public string name;   // item name of the tiers that wear it
            public bool paired;   // one in each hand, carried on the hips: sidearms and claws
            public Hold hold;     // how the hand takes it
            public float scale;   // model scale, where a pack's mesh is out of proportion (0 = as it comes)
        }

        /// <summary>How a weapon sits in the hand. The mount's own convention is the katana's: <see cref="Blade"/>.</summary>
        private enum Hold
        {
            /// <summary>Long axis out of the top of the fist: swords, axes, polearms.</summary>
            Blade,
            /// <summary>Long axis along the forearm, the grip across the fist: guns, punch daggers, claws.</summary>
            Forward,
            /// <summary>In the left hand, upright: bows. The right hand draws the string.</summary>
            Bow,
            /// <summary>Grip in the fist, barrel ahead and upright: pistols.</summary>
            Pistol
        }

        // A model's long axis is +Z and its top +Y. Turned so the top lies along the fist and the long axis points
        // where the arm points (worked out from the hand socket's frame, checked on the Sidearm in Play).
        private static readonly Vector3 ForwardHoldEuler = new Vector3(270f, 0f, 180f);
        // A bow stands along the fist as a blade does, turned so its string is on the archer's side (checked at the
        // aim of Synty's shooting clip, which holds the bow in the left hand).
        private static readonly Vector3 BowHoldEuler = new Vector3(0f, 0f, 180f);
        // A pistol held the Forward way lay with its top along the forearm, barrel off to the side (owner, 24 Oct
        // 2026: "gun placement is way off"). Turned so the barrel points ahead, upright, through the Carbine strikes,
        // which fire from the hip (measured in Play on the Sidearm and the Dock Runner Carbine).
        internal static readonly Vector3 PistolHoldEuler = new Vector3(0f, 180f, 180f);

        private sealed class ClassWeapons
        {
            public WeaponClassId id;
            public WeaponLook[] looks; // one per band: Common, Uncommon, Rare I, Rare II, Epic
        }

        private static WeaponLook Look(string pack, string prefab, string name, bool paired = false, Hold hold = Hold.Blade, float scale = 0f) =>
            new WeaponLook { pack = pack, prefab = prefab, name = name, paired = paired, hold = hold, scale = scale };

        private static WeaponLook Gun(string pack, string prefab, string name, bool paired = false, float scale = 0f) =>
            Look(pack, prefab, name, paired, Hold.Forward, scale);

        // Starting picks, plainest to hero mesh. To change a look, edit the line in GearDictionary.asset: this table
        // only seeds lines that do not exist yet.
        private static readonly ClassWeapons[] WeaponClassLooks =
        {
            new ClassWeapons { id = WeaponClassId.Monoblade, looks = new[]
            {
                Look("PolygonSamuraiEmpire", "SM_Wep_Sword_Training_01", "Training Blade"),
                Look("PolygonSamurai", "SM_Wep_Sword", "Service Blade"),
                Look("PolygonSamurai", "SM_Wep_Ninjato_01", "Ninjato"),
                Look("PolygonSamurai", "SM_Wep_Odachi_01", "Odachi"),
                Look("PolygonSamurai", "SM_Wep_Odachi_Covered_01", "Executive Odachi"),
            } },
            new ClassWeapons { id = WeaponClassId.Glaive, looks = new[]
            {
                Look("PolygonSamurai", "SM_Wep_BoStaff_01", "Riot Staff"),
                Look("PolygonSamuraiEmpire", "SM_Wep_Spear_01", "Patrol Spear"),
                Look("PolygonSamurai", "SM_Wep_Naginata_01", "Naginata"),
                Look("PolygonSamurai", "SM_Wep_JuMonjiYari_01", "Cross Spear"),
                Look("PolygonSamurai", "SM_Wep_Naginata_02", "Warden Naginata"),
            } },
            new ClassWeapons { id = WeaponClassId.Carbine, looks = new[]
            {
                Look("PolygonSciFiCity", "SM_Wep_Pistol_01", "Sidearm", paired: true, hold: Hold.Pistol),
                Gun("PolygonSamuraiEmpire", "SM_Wep_Rifle_01", "Service Rifle", scale: 0.65f), // the pack models it over two metres long
                Gun("PolygonSciFiWorlds", "SM_Wep_Assault_01", "Assault Carbine"),
                Gun("PolygonSciFiWorlds", "SM_Wep_Sniper_01", "Marksman Rifle", scale: 0.85f),
                Gun("PolygonSciFiWorlds", "SM_Wep_Assault_03", "Vanguard Carbine"),
            } },
            new ClassWeapons { id = WeaponClassId.Harvester, looks = new[]
            {
                Look("PolygonSamuraiEmpire", "SM_Wep_Sickle_01", "Scrap Sickle"),
                Look("PolygonDarkFantasy", "SM_Wep_Sickle_01", "Reaping Sickle"),
                Look("PolygonDarkFantasy", "SM_Wep_Cleaver_01", "Rendering Cleaver"),
                Look("PolygonDarkFortress", "SM_Wep_Saw_01", "Bonesaw"),
                Look("PolygonDarkFantasy", "SM_Wep_Scythe_01", "Terminus Scythe"),
            } },
            new ClassWeapons { id = WeaponClassId.ServoClaws, looks = new[]
            {
                Look("PolygonSamuraiEmpire", "SM_Wep_Spike_01", "Piston Spike", paired: true, hold: Hold.Forward, scale: 2.5f), // eight centimetres as it comes
                Look("PolygonFantasyKingdom", "SM_Wep_KnuckleDagger_01", "Knuckle Blade", paired: true, hold: Hold.Forward),
                Look("PolygonSciFiWorlds", "SM_Wep_Dagger_03", "Servo Dagger", paired: true),
                Look("PolygonSamuraiEmpire", "SM_Wep_Spikes_01", "Twin Spikes", paired: true, hold: Hold.Forward),
                Look("PolygonSamuraiEmpire", "SM_Wep_Claw_01", "Apex Claw", paired: true, hold: Hold.Forward),
            } },
            new ClassWeapons { id = WeaponClassId.ArcCaster, looks = new[]
            {
                Gun("PolygonSciFiCity", "SM_Wep_Rifle_Laser_01", "Laser Emitter"),
                Gun("PolygonSciFiWorlds", "SM_Wep_Alien_01", "Arc Projector"),
                Gun("PolygonSciFiCity", "SM_Wep_Rifle_Plasma_01", "Plasma Caster"),
                Gun("PolygonSciFiWorlds", "SM_Wep_Alien_03", "Storm Projector"),
                Gun("PolygonSciFiWorlds", "SM_Wep_Launcher_01", "Extinction Launcher"),
            } },
            new ClassWeapons { id = WeaponClassId.Longaxe, looks = new[]
            {
                Look("PolygonVikings", "SM_Wep_Axe_01", "Hewing Axe"),
                Look("PolygonVikings", "SM_Wep_Axe_02", "Raider Axe"),
                Look("PolygonElvenRealm", "SM_Wep_Axe_03", "Elven Axe"),
                Look("PolygonFantasyKingdom", "SM_Wep_Axe_08", "Jarl's Axe"),
                Look("PolygonVikings", "SM_Wep_LargeAxe_01", "Northfall Greataxe"),
            } },
            new ClassWeapons { id = WeaponClassId.Claymore, looks = new[]
            {
                Look("PolygonVikings", "SM_Wep_Sword_01", "Clan Sword"),
                Look("PolygonKnights", "SM_Wep_Broadsword_01", "Broadsword"),
                Look("PolygonElvenRealm", "SM_Wep_Sword_04", "Elven Blade"),
                Look("PolygonFantasyKingdom", "SM_Wep_Sword_12", "Kingdom Greatsword"),
                Look("PolygonKnights", "SM_Wep_Zweihander_01", "Zweihander"),
            } },
            new ClassWeapons { id = WeaponClassId.Warbow, looks = new[]
            {
                Look("AnimationBowCombat", "Wep_RecurveBow_01", "Hunting Bow", hold: Hold.Bow),
                Look("AnimationBowCombat", "Wep_RecurveBow_04", "Ranger Bow", hold: Hold.Bow),
                Look("AnimationBowCombat", "Wep_LongBow_01", "Longbow", hold: Hold.Bow),
                Look("AnimationBowCombat", "Wep_LongBow_03", "War Longbow", hold: Hold.Bow),
                Look("AnimationBowCombat", "Wep_CompoundBow_02", "Stormflight Bow", hold: Hold.Bow),
            } },
        };

        // Icon templates only (Ring_01.., Amulet_01..): the gem colour follows the rarity.
        private static readonly (string name, ItemRarity rarity)[] Rings =
        {
            ("Iron Band", ItemRarity.Common), ("Silver Signet", ItemRarity.Uncommon), ("Ring of Focus", ItemRarity.Rare),
            ("Ember Loop", ItemRarity.Epic), ("Ring of the Sovereign", ItemRarity.Legendary)
        };

        private static readonly (string name, ItemRarity rarity)[] Amulets =
        {
            ("Bone Charm", ItemRarity.Common), ("Jade Pendant", ItemRarity.Uncommon), ("Amulet of Flux", ItemRarity.Rare),
            ("Heart of Embers", ItemRarity.Epic), ("Celestial Talisman", ItemRarity.Legendary)
        };

        [MenuItem("Krytical Nova/Items/1. Generate Gear Looks From Synty")]
        public static void Generate()
        {
            var created = new List<string>();

            AssetDatabase.StartAssetEditing();
            try { EnsureReadable(OutfitFolder); EnsureReadable(HumanFolder); }
            finally { AssetDatabase.StopAssetEditing(); }

            var partCatalog = AssetDatabase.LoadAssetAtPath<SidekickPartCatalog>(SidekickPartCatalogBaker.CatalogPath);
            if (partCatalog == null || partCatalog.Parts.Count == 0) partCatalog = SidekickPartCatalogBaker.Bake();

            var outfitLooks = GenerateOutfitLooks(partCatalog, created);
            var weaponLooks = GenerateWeaponLooks(created);

            var rings = new ItemDefinition[Rings.Length];
            for (int i = 0; i < Rings.Length; i++)
                rings[i] = Simple(EquipSlot.Ring, (ushort)(RingBase + i + 1), $"Ring_{i + 1:00}", Rings[i].name, Rings[i].rarity,
                    "A ring.", created);
            var amulets = new ItemDefinition[Amulets.Length];
            for (int i = 0; i < Amulets.Length; i++)
                amulets[i] = Simple(EquipSlot.Amulet, (ushort)(AmuletBase + i + 1), $"Amulet_{i + 1:00}", Amulets[i].name, Amulets[i].rarity,
                    "An amulet.", created);
            // Pets are not made here: they have their own data and builder (Krytical Nova/Items/7. Build Pets).

            int added = SeedLines(partCatalog, outfitLooks, weaponLooks, rings, amulets);
            GearThemeBuilder.Build();
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] Gear looks: {created.Count} new template assets, {added} new dictionary lines. Baking the ladder...");
            GearStatBaker.Bake();
        }

        // ---------------------------------------------------------------- outfit looks from every pack

        private sealed class GearPack
        {
            public string code;        // pack code in part names
            public string tag;         // short token used in asset names
            public ushort idOffset;    // added to the slot's ID base; one block of 100 per pack, never reused
            public FactionMask factions;
            public BodyFit bodies;
            public bool helmets;       // armor carries the head attachment with the same variant number
            public string[] sets;      // look name per mesh variant: names the look templates only
            public string[] bandSets;  // the set of each band (Common, Uncommon, Rare I, Rare II, Epic): what its pieces are called
            public string armor, gloves, boots, cape, ring, amulet;
        }

        // The gear groups. Each has its own outfit, ring and amulet lines, so each can be its own drop table.
        // Default owners: to let another faction use a line, edit it in GearDictionary.asset. This table only seeds
        // new lines; hand-edited faction, body and look values are kept.
        private static readonly GearPack[] Packs =
        {
            new GearPack
            {
                code = "SCFI_SOLD", tag = "SOLD", idOffset = 0, factions = FactionMask.Hegemony, bodies = BodyFit.Humanoid,
                sets = OutfitNames, bandSets = new[] { "Recruit", "Sentinel", "Striker", "Bastion", "Commander" },
                armor = "Armor", gloves = "Gauntlets", boots = "Boots", cape = "Cape", ring = "Signet", amulet = "Tags"
            },
            new GearPack
            {
                code = "SCFI_CIVL", tag = "CIVL", idOffset = 100, factions = FactionMask.Hegemony, bodies = BodyFit.Humanoid,
                sets = new[] { "Commuter", "Technician", "Courier", "Analyst", "Executive", "Director", "Magnate" },
                bandSets = new[] { "Commuter", "Courier", "Analyst", "Director", "Magnate" },
                armor = "Outfit", gloves = "Gloves", boots = "Shoes", cape = "Pack", ring = "Band", amulet = "Lanyard"
            },
            new GearPack
            {
                code = "SCFI_ROBO", tag = "ROBO", idOffset = 200, factions = FactionMask.Null, bodies = BodyFit.Robot,
                sets = new[] { "Servitor", "Hauler", "Welder", "Sentry", "Enforcer", "Ravager", "Juggernaut", "Overseer", "Annihilator", "Prime" },
                bandSets = new[] { "Servitor", "Welder", "Ravager", "Overseer", "Prime" },
                armor = "Plating", gloves = "Servos", boots = "Stabilizers", cape = "Back Unit", ring = "Coupling", amulet = "Capacitor"
            },
            new GearPack
            {
                code = "FANT_SKTN", tag = "SKTN", idOffset = 300, factions = FactionMask.Null, bodies = BodyFit.Skeleton, helmets = true,
                sets = new[] { "Graveworn", "Boneguard", "Cryptwarden", "Deathsworn", "Dreadknight", "Lichbound", "Revenant" },
                bandSets = new[] { "Graveworn", "Cryptwarden", "Deathsworn", "Lichbound", "Revenant" },
                armor = "Armor", gloves = "Gauntlets", boots = "Greaves", cape = "Shroud", ring = "Knucklebone", amulet = "Phylactery"
            },
            new GearPack
            {
                code = "VIKG_WARR", tag = "VIKG", idOffset = 400, factions = FactionMask.Covenant, bodies = BodyFit.Humanoid,
                sets = new[] { "Thrall", "Huscarl", "Reaver", "Berserker", "Jarl", "Warlord" },
                bandSets = new[] { "Thrall", "Huscarl", "Berserker", "Jarl", "Warlord" },
                armor = "Armor", gloves = "Bracers", boots = "Boots", cape = "Cloak", ring = "Arm Ring", amulet = "Torc"
            },
            new GearPack
            {
                code = "ELVN_WARR", tag = "ELVN", idOffset = 500, factions = FactionMask.Covenant, bodies = BodyFit.Humanoid,
                sets = new[] { "Scout", "Warden", "Ranger", "Bladesinger", "Highguard", "Sunlord" },
                // Uncommon is Wildrunner, not the look's "Warden": a Warden is a faction's leader (the glossary).
                bandSets = new[] { "Scout", "Wildrunner", "Bladesinger", "Highguard", "Sunlord" },
                armor = "Armor", gloves = "Gloves", boots = "Boots", cape = "Cloak", ring = "Leaf Ring", amulet = "Talisman"
            },
        };

        private static readonly EquipSlot[] OutfitSlots = { EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Cape };

        private static readonly BodyRegion[] ArmorBody =
        {
            BodyRegion.Torso, BodyRegion.ArmUpperLeft, BodyRegion.ArmUpperRight, BodyRegion.ArmLowerLeft,
            BodyRegion.ArmLowerRight, BodyRegion.Hips, BodyRegion.LegLeft, BodyRegion.LegRight
        };
        private static readonly BodyRegion[] ArmorAttachments =
        {
            BodyRegion.AttachmentHipsFront, BodyRegion.AttachmentHipsBack, BodyRegion.AttachmentHipsLeft,
            BodyRegion.AttachmentHipsRight, BodyRegion.AttachmentShoulderLeft, BodyRegion.AttachmentShoulderRight,
            BodyRegion.AttachmentElbowLeft, BodyRegion.AttachmentElbowRight, BodyRegion.AttachmentKneeLeft,
            BodyRegion.AttachmentKneeRight
        };

        /// <summary>One outfit look: the template asset plus what a line needs to know about it.</summary>
        private struct OutfitLook
        {
            public ItemDefinition template;
            public byte variant;
            public string name;        // "Recruit Armor"
            public string description;
        }

        /// <summary>Names a look template after its mesh variant. Players never read it: a line is named after its set.</summary>
        private static string SetName(GearPack pack, byte variant) =>
            variant - 1 < pack.sets.Length ? pack.sets[variant - 1] : $"Mark {variant:00}";

        /// <summary>
        /// The set a pack's pieces of one band belong to. Every piece carries it, whichever mesh variant it wears: a
        /// pack has fewer glove and boot meshes than armors, so naming a piece after its mesh split the sets.
        /// </summary>
        private static string SetOf(GearPack pack, int band) => pack.bandSets[Mathf.Clamp(band, 0, pack.bandSets.Length - 1)];

        private static string NounOf(GearPack pack, EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Armor: return pack.armor;
                case EquipSlot.Gloves: return pack.gloves;
                case EquipSlot.Boots: return pack.boots;
                case EquipSlot.Cape: return pack.cape;
                case EquipSlot.Ring: return pack.ring;
                case EquipSlot.Amulet: return pack.amulet;
                default: return null;
            }
        }

        /// <summary>Creates / refreshes a template per (pack, slot, variant). Result: looks per (pack index, slot), lowest variant first.</summary>
        private static Dictionary<(int pack, EquipSlot slot), List<OutfitLook>> GenerateOutfitLooks(SidekickPartCatalog catalog, List<string> created)
        {
            var result = new Dictionary<(int, EquipSlot), List<OutfitLook>>();
            for (int index = 0; index < Packs.Length; index++)
            {
                int packIndex = index;
                var pack = Packs[packIndex];
                // Variant numbers that exist per region in this pack.
                var variants = new Dictionary<BodyRegion, List<byte>>();
                foreach (var part in catalog.Parts)
                {
                    if (part.pack != pack.code) continue;
                    if (!variants.TryGetValue(part.region, out var list)) variants[part.region] = list = new List<byte>();
                    if (!list.Contains(part.variant)) list.Add(part.variant);
                }
                foreach (var list in variants.Values) list.Sort();

                AddSlot(EquipSlot.Armor, ArmorBase, BodyRegion.Torso, pack.armor, "Worn on the body: torso, arms, hips and legs.");
                AddSlot(EquipSlot.Gloves, GlovesBase, BodyRegion.HandLeft, pack.gloves, "Worn on the hands.");
                AddSlot(EquipSlot.Boots, BootsBase, BodyRegion.FootLeft, pack.boots, "Worn on the feet.");
                AddSlot(EquipSlot.Cape, CapeBase, BodyRegion.AttachmentBack, pack.cape, "Worn across the back.");

                void AddSlot(EquipSlot slot, ushort idBase, BodyRegion keyRegion, string noun, string description)
                {
                    var looks = new List<OutfitLook>();
                    result[(packIndex, slot)] = looks;
                    if (!variants.TryGetValue(keyRegion, out var slotVariants)) return;
                    foreach (byte vv in slotVariants)
                    {
                        var ids = PartIdsFor(catalog, pack, slot, vv, variants);
                        if (ids.Count == 0) continue;
                        string name = $"{SetName(pack, vv)} {noun}";
                        var def = Simple(slot, (ushort)(idBase + pack.idOffset + vv), $"{AssetPrefix(slot)}_{pack.tag}_{vv:00}",
                            name, ItemRarity.Common, description, created, out _);
                        // Part data is derived from the catalog, so it is always refreshed (names are left alone).
                        def.partCatalog = catalog;
                        def.appearancePartIds = ids.ToArray();
                        def.appearanceParts = new List<SidekickPartRef>(); // no direct mesh references: loaded on demand
                        def.colorCells = GearThemeBuilder.PaintedCells(def); // what a set's colour theme may recolour
                        EditorUtility.SetDirty(def);
                        looks.Add(new OutfitLook { template = def, variant = vv, name = name, description = description });
                    }
                }
            }
            return result;
        }

        private static List<ushort> PartIdsFor(SidekickPartCatalog catalog, GearPack pack, EquipSlot slot, byte vv,
            Dictionary<BodyRegion, List<byte>> variants)
        {
            var ids = new List<ushort>();
            void Exact(BodyRegion region)
            {
                if (catalog.TryFind(pack.code, region, vv, out var part)) ids.Add(part.id);
            }
            // A pack can have fewer arm or leg variants than torsos. Reuse one it does have, so a set never shows
            // the wearer's basic outfit poking through its own armor.
            void Nearest(BodyRegion region)
            {
                if (!variants.TryGetValue(region, out var list) || list.Count == 0) return;
                byte pick = list.Contains(vv) ? vv : list[(vv - 1) % list.Count];
                if (catalog.TryFind(pack.code, region, pick, out var part)) ids.Add(part.id);
            }

            switch (slot)
            {
                case EquipSlot.Armor:
                    foreach (var region in ArmorBody) Nearest(region);
                    foreach (var region in ArmorAttachments) Exact(region);
                    if (pack.helmets) Exact(BodyRegion.AttachmentHead);
                    break;
                case EquipSlot.Gloves:
                    Exact(BodyRegion.HandLeft);
                    Exact(BodyRegion.HandRight);
                    break;
                case EquipSlot.Boots:
                    Exact(BodyRegion.FootLeft);
                    Exact(BodyRegion.FootRight);
                    break;
                case EquipSlot.Cape:
                    Exact(BodyRegion.AttachmentBack);
                    break;
            }
            return ids;
        }

        // ---------------------------------------------------------------- weapon looks for the nine classes

        /// <summary>
        /// Rebuilds the model of every class weapon look from its Synty mesh (after the mesh table or the alignment
        /// rule changed), re-captures their icons and re-bakes. Grip poses tuned on a template are reset.
        /// </summary>
        [MenuItem("Krytical Nova/Items/Rebuild Weapon Look Models")]
        public static void RebuildWeaponLooks()
        {
            var looks = GenerateWeaponLooks(new List<string>(), force: true);
            AssetDatabase.SaveAssets();
            ItemIconCapture.Capture(looks.Values.SelectMany(templates => templates).Where(t => t != null).ToList());
            GearStatBaker.Bake();
        }

        /// <summary>One template (asset + prefab) per class and band. A missing source mesh leaves that template without a model.</summary>
        private static Dictionary<WeaponClassId, ItemDefinition[]> GenerateWeaponLooks(List<string> created, bool force = false)
        {
            var result = new Dictionary<WeaponClassId, ItemDefinition[]>();
            foreach (var cls in WeaponClassLooks)
            {
                var templates = new ItemDefinition[BandCount];
                for (int band = 0; band < BandCount && band < cls.looks.Length; band++)
                {
                    var look = cls.looks[band];
                    string asset = $"Weapon_{cls.id}_{band + 1:00}";
                    var def = Simple(EquipSlot.Weapon, 0, asset, look.name, BandRarity[band],
                        $"{WeaponClasses.DisplayName(cls.id)} look.", created, out bool isNew);
                    templates[band] = def;
                    if (def.weaponClass != cls.id) { def.weaponClass = cls.id; EditorUtility.SetDirty(def); }
                    // Which looks are pairs, and which hand holds a look, is this table's to say.
                    bool leftHanded = look.hold == Hold.Bow;
                    if (def.paired != look.paired || def.leftHanded != leftHanded)
                    {
                        def.paired = look.paired;
                        def.leftHanded = leftHanded;
                        EditorUtility.SetDirty(def);
                    }
                    if (!force && !isNew && def.weaponPrefab != null) continue; // keeps a hand-tuned grip; the Rebuild menu forces it

                    string source = FindPrefab(look.pack, look.prefab);
                    if (source == null) { Debug.LogWarning($"[Items] Weapon mesh missing: {look.pack}/{look.prefab} ({cls.id})."); continue; }
                    string prefabPath = $"{WeaponPrefabFolder}/W_{cls.id}_{band + 1:00}.prefab";
                    var prefab = BuildWeaponPrefab(source, prefabPath, out Bounds bounds);
                    if (prefab == null) continue;
                    float scale = look.scale > 0f ? look.scale : 1f;
                    bounds = new Bounds(bounds.center * scale, bounds.size * scale);
                    def.weaponPrefab = prefab;
                    // The starting pose of a look. From here it is tuned on the template, in the hand and carried.
                    def.weaponLocalPosition = Vector3.zero;
                    def.weaponLocalEuler = look.hold == Hold.Forward ? ForwardHoldEuler : look.hold == Hold.Bow ? BowHoldEuler
                        : look.hold == Hold.Pistol ? PistolHoldEuler : Vector3.zero;
                    def.weaponLocalScale = Vector3.one * scale;
                    def.offHandLocalPosition = Vector3.zero;
                    def.offHandLocalEuler = Vector3.zero;
                    def.carry = look.paired ? WeaponCarry.Hips : WeaponCarry.Back;
                    def.carryLocalPosition = look.paired
                        ? Vector3.zero
                        : new Vector3(0f, 0f, BackCarryShift(bounds.min.z, bounds.max.z) + BackCarryDrop(look.hold));
                    def.carryLocalEuler = Vector3.zero;
                    float length = Mathf.Max(0.3f, bounds.max.z);
                    def.hitboxCenter = new Vector3(0f, bounds.center.y, length * 0.5f + 0.05f);
                    def.hitboxSize = new Vector3(Mathf.Max(0.18f, bounds.size.x), Mathf.Max(0.18f, bounds.size.y), length);
                    EditorUtility.SetDirty(def);
                }
                result[cls.id] = templates;
            }
            return result;
        }

        /// <summary>
        /// How far along its own length a weapon is slid on the back sheath, which sits at the right shoulder with
        /// the weapon running down across the back. A blade hangs there by its grip. A polearm or a bow has its grip
        /// in the middle, so as it comes half of it would stand over the head and the other half drag on the ground:
        /// it is slid until a short length shows above the shoulder, and a very long one further, until its foot is
        /// clear of the ground.
        /// </summary>
        internal static float BackCarryShift(float gripEnd, float farEnd)
        {
            const float above = 0.45f; // what may stand above the shoulder
            const float below = 1.3f;  // how far down the back a weapon reaches before it drags
            if (gripEnd >= -above) return 0f;
            float atShoulder = gripEnd + above;                       // this point of the weapon sits on the sheath
            if (farEnd - atShoulder > below) atShoulder = farEnd - below;
            return -atShoulder;
        }

        /// <summary>
        /// The sheath was placed for a katana, whose hilt stands at the head. A gun's stock and a bow's upper limb
        /// look wrong that high: they ride lower down the back.
        /// </summary>
        private static float BackCarryDrop(Hold hold) => hold == Hold.Forward ? 0.25f : hold == Hold.Bow ? 0.15f : 0f;

        /// <summary>Finds a Synty prefab by file name inside a pack, wherever the pack keeps it.</summary>
        private static string FindPrefab(string pack, string prefabName)
        {
            string root = $"Assets/Synty/{pack}";
            if (!AssetDatabase.IsValidFolder(root)) return null;
            foreach (string guid in AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { root }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(path) == prefabName) return path;
            }
            return null;
        }

        // ---------------------------------------------------------------- dictionary lines

        /// <summary>
        /// Adds the lines the dictionary does not have yet (a line is identified by slot, tag and band). Existing
        /// lines keep everything, including a re-picked look. Returns how many were added.
        /// </summary>
        private static int SeedLines(SidekickPartCatalog catalog, Dictionary<(int pack, EquipSlot slot), List<OutfitLook>> outfitLooks,
            Dictionary<WeaponClassId, ItemDefinition[]> weaponLooks, ItemDefinition[] rings, ItemDefinition[] amulets)
        {
            var dictionary = AssetDatabase.LoadAssetAtPath<GearDictionary>(DictionaryPath);
            if (dictionary == null)
            {
                dictionary = ScriptableObject.CreateInstance<GearDictionary>();
                AssetDatabase.CreateAsset(dictionary, DictionaryPath);
            }

            var lines = dictionary.Lines.ToList();
            ushort nextId = (ushort)(lines.Count == 0 ? 1 : lines.Max(l => l.lineId) + 1);
            int added = 0;

            void Add(GearDictionary.Line line)
            {
                if (line.look == null) return;
                foreach (var existing in lines)
                    if (existing.slot == line.slot && existing.tag == line.tag && existing.band == line.band) return;
                line.lineId = nextId++;
                lines.Add(line);
                added++;
            }

            // Outfits first, then weapons, then jewellery: line IDs are handed out in this order on the first run.
            for (int packIndex = 0; packIndex < Packs.Length; packIndex++)
            {
                var pack = Packs[packIndex];
                foreach (var slot in OutfitSlots)
                {
                    if (!outfitLooks.TryGetValue((packIndex, slot), out var looks) || looks.Count == 0) continue;
                    for (int band = 0; band < BandCount; band++)
                    {
                        if (slot == EquipSlot.Cape && band < BandRareOne) continue; // a cape comes with Rare gear and up
                        var look = looks[LookIndex(band, looks.Count)];
                        string set = SetOf(pack, band);
                        Add(new GearDictionary.Line
                        {
                            tag = pack.tag, slot = slot, rarity = BandRarity[band], band = (byte)band, pack = pack.code,
                            factions = pack.factions, bodies = pack.bodies, look = look.template,
                            set = set, displayName = $"{set} {NounOf(pack, slot)}", description = look.description
                        });
                    }
                }
            }

            foreach (var cls in WeaponClassLooks)
            {
                if (!weaponLooks.TryGetValue(cls.id, out var templates)) continue;
                var role = WeaponClasses.RoleOf(cls.id);
                for (int band = 0; band < BandCount; band++)
                {
                    Add(new GearDictionary.Line
                    {
                        tag = cls.id.ToString(), slot = EquipSlot.Weapon, rarity = BandRarity[band], band = (byte)band,
                        pack = string.Empty, factions = WeaponClassRules.MaskOf(cls.id), bodies = BodyFit.All,
                        weaponClass = cls.id, look = templates[band], displayName = cls.looks[band].name,
                        description = $"{WeaponClasses.DisplayName(cls.id)}: the {WeaponClasses.RoleName(role).ToLowerInvariant()} weapon of the {WeaponClassRules.FactionOf(cls.id)}."
                    });
                }
            }

            // Rings and amulets belong to a group like the outfits do. They fit every body of that group's faction,
            // and are named after the same set as the group's outfit of that band.
            for (int packIndex = 0; packIndex < Packs.Length; packIndex++)
            {
                var pack = Packs[packIndex];
                for (int band = 0; band < BandCount; band++)
                {
                    string set = SetOf(pack, band);
                    Add(new GearDictionary.Line
                    {
                        tag = pack.tag, slot = EquipSlot.Ring, rarity = BandRarity[band], band = (byte)band, pack = pack.code,
                        factions = pack.factions, bodies = BodyFit.All, look = rings[BandJewelleryTemplate[band]],
                        set = set, displayName = $"{set} {pack.ring}", description = "Worn on a finger."
                    });
                    Add(new GearDictionary.Line
                    {
                        tag = pack.tag, slot = EquipSlot.Amulet, rarity = BandRarity[band], band = (byte)band, pack = pack.code,
                        factions = pack.factions, bodies = BodyFit.All, look = amulets[BandJewelleryTemplate[band]],
                        set = set, displayName = $"{set} {pack.amulet}", description = "Worn around the neck."
                    });
                }
            }

            int renamed = NameLinesAfterSets(lines);
            if (renamed > 0) Debug.Log($"[Items] {renamed} gear lines named after their set.");

            // The basic tiers have no cape (owner, 6 Oct 2026). Lines seeded before that rule go; their IDs are not reused.
            int capes = lines.RemoveAll(l => l.slot == EquipSlot.Cape && l.band < BandRareOne);
            if (capes > 0) Debug.Log($"[Items] {capes} cape lines below Rare removed: capes come with Rare gear and up.");

            dictionary.SetLines(catalog, lines);
            EditorUtility.SetDirty(dictionary);
            return added;
        }

        /// <summary>
        /// Gives every outfit, ring and amulet line that names no set its pack's set for that band, and the name that
        /// goes with it ("Sunlord Gloves"). A line that already names a set is left alone, so a name edited by hand
        /// in the dictionary survives. Returns how many lines changed.
        /// </summary>
        private static int NameLinesAfterSets(List<GearDictionary.Line> lines)
        {
            int changed = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                if (line.slot == EquipSlot.Weapon || !string.IsNullOrEmpty(line.set)) continue;
                GearPack pack = null;
                foreach (var candidate in Packs)
                    if (candidate.tag == line.tag) { pack = candidate; break; }
                string noun = pack != null ? NounOf(pack, line.slot) : null;
                if (noun == null) continue;
                line.set = SetOf(pack, line.band);
                line.displayName = $"{line.set} {noun}";
                lines[i] = line;
                changed++;
            }
            return changed;
        }

        /// <summary>Common wears the pack's first variant (the basic outfit); the rest spread towards the last one.</summary>
        private static int LookIndex(int band, int count)
        {
            if (band == BandCommon || count <= 1) return 0;
            int index = Mathf.FloorToInt((count - 1) * BandLookFraction[band] + 0.5f);
            return Mathf.Clamp(index, 1, count - 1);
        }

        /// <summary>Asset name prefix per slot.</summary>
        public static string AssetPrefix(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Weapon: return "Weapon";
                case EquipSlot.Gloves: return "Gloves";
                case EquipSlot.Boots: return "Boots";
                case EquipSlot.Cape: return "Cape";
                case EquipSlot.Ring: return "Ring";
                case EquipSlot.Amulet: return "Amulet";
                default: return "Armor";
            }
        }

        public static bool IsDictionaryGear(ItemDefinition item) =>
            item != null && (item.equipSlot == EquipSlot.Armor || item.equipSlot == EquipSlot.Gloves
                             || item.equipSlot == EquipSlot.Boots || item.equipSlot == EquipSlot.Cape);

        public static SidekickPartRef LoadPart(string fbxPath, BodyRegion region)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var smr = go != null ? go.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
            if (smr == null)
            {
                Debug.LogWarning($"[Items] No SkinnedMeshRenderer in {fbxPath}");
                return default;
            }
            return KryticalNova.Items.Visuals.SidekickPartRefs.FromRenderer(smr, region);
        }

        public static bool TryParsePart(string partName, out BodyRegion region)
        {
            var m = PartName.Match(partName);
            region = m.Success ? (BodyRegion)int.Parse(m.Groups["nn"].Value) : BodyRegion.None;
            return m.Success;
        }

        /// <summary>
        /// Wraps a weapon model in an identity root, rotated so its long axis runs along +Z away from the grip (the
        /// mount's convention). The source pivot is treated as the grip. A weapon that needs a different hold is
        /// tuned on its look template afterwards (weaponLocalPosition / weaponLocalEuler); the baker copies that to
        /// every tier that wears the look.
        /// </summary>
        internal static GameObject BuildWeaponPrefab(string source, string prefabPath, out Bounds bounds)
        {
            bounds = default;
            WeatherVfxBuilder.EnsureFolder(WeaponPrefabFolder);
            var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            try
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>(source);
                if (src == null) { Debug.LogWarning($"[Items] Weapon source missing: {source}"); return null; }
                var model = (GameObject)Object.Instantiate(src, root.transform, false);
                model.name = "Model";
                model.transform.localPosition = Vector3.zero;
                AlignLongestAxisToZ(root.transform, model.transform);

                foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (var rb in root.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
                foreach (var animator in root.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                var fallback = AssetDatabase.LoadAssetAtPath<Material>(FallbackWeaponMaterial);
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null) { mats[i] = fallback; changed = true; }
                        else if (NeedsConversion(mats[i])) { mats[i] = UrpMaterialFor(mats[i]); changed = true; }
                    }
                    if (changed) r.sharedMaterials = mats;
                }
                bounds = LocalBounds(root.transform);
                return PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A material made for the Built-in pipeline draws magenta here.</summary>
        private static bool NeedsConversion(Material material)
        {
            string shader = material.shader != null ? material.shader.name : string.Empty;
            return shader == "Standard" || shader == "Standard (Specular setup)" || shader.StartsWith("Legacy Shaders/")
                   || shader == "Hidden/InternalErrorShader" || shader.Length == 0;
        }

        /// <summary>
        /// A URP copy of a vendor material (same texture and tint), kept in our own folder so the vendor file is not
        /// touched. Made once per source material.
        /// </summary>
        private static Material UrpMaterialFor(Material source)
        {
            WeatherVfxBuilder.EnsureFolder(WeaponMaterialFolder);
            string path = $"{WeaponMaterialFolder}/M_{source.name}_URP.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) return source;
            material = new Material(lit) { name = Path.GetFileNameWithoutExtension(path) };
            if (source.HasProperty("_MainTex")) material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
            material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            material.SetFloat("_Smoothness", source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.2f);
            material.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// The mount's convention, set by the katana it was tuned for: the long axis runs along +Z away from the grip
        /// and the broad side (blade width, a rifle's height, a bow's belly) lies along Y.
        /// </summary>
        private static void AlignLongestAxisToZ(Transform root, Transform model)
        {
            var b = LocalBounds(root);
            Vector3 size = b.size;
            Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
            // Point the end farthest from the pivot (the grip) along +Z.
            float toMax = Vector3.Dot(b.max, axis), toMin = -Vector3.Dot(b.min, axis);
            Vector3 bladeDir = toMax >= toMin ? axis : -axis;
            model.localRotation = Quaternion.FromToRotation(bladeDir, Vector3.forward) * model.localRotation;

            // Roll about the long axis so the broader of the two remaining sides lies along Y.
            Vector3 rolled = LocalBounds(root).size;
            if (rolled.x > rolled.y * 1.15f)
                model.localRotation = Quaternion.AngleAxis(90f, Vector3.forward) * model.localRotation;
        }

        public static Bounds LocalBounds(Transform root)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var mb = mesh.bounds;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static ItemDefinition Simple(EquipSlot slot, ushort id, string asset, string displayName, ItemRarity rarity,
            string description, List<string> created) => Simple(slot, id, asset, displayName, rarity, description, created, out _);

        private static ItemDefinition Simple(EquipSlot slot, ushort id, string asset, string displayName, ItemRarity rarity,
            string description, List<string> created, out bool isNew)
        {
            string folder = $"{ItemsRoot}/{FolderFor(slot)}";
            WeatherVfxBuilder.EnsureFolder(folder);
            string path = $"{folder}/{asset}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            isNew = def == null;
            if (!isNew) return def;

            def = ScriptableObject.CreateInstance<ItemDefinition>();
            def.itemId = id;
            def.displayName = displayName;
            def.description = description;
            def.rarity = rarity;
            def.equipSlot = slot;
            def.maxStack = 1;
            AssetDatabase.CreateAsset(def, path);
            created.Add(path);
            return def;
        }

        public static string FolderFor(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Weapon: return "Weapons";
                case EquipSlot.Armor: return "Armor";
                case EquipSlot.Gloves: return "Gloves";
                case EquipSlot.Boots: return "Boots";
                case EquipSlot.Ring: return "Rings";
                case EquipSlot.Amulet: return "Amulets";
                case EquipSlot.Cape: return "Capes";
                case EquipSlot.Pet: return "Pets";
                default: return "Misc";
            }
        }

        private static readonly EquipSlot[] TemplateSlots =
            { EquipSlot.Weapon, EquipSlot.Armor, EquipSlot.Gloves, EquipSlot.Boots, EquipSlot.Ring, EquipSlot.Amulet, EquipSlot.Cape };

        /// <summary>True for an asset in one of the folders that hold look templates rather than items a player can own.</summary>
        public static bool IsLookTemplatePath(string assetPath)
        {
            foreach (var slot in TemplateSlots)
                if (assetPath.StartsWith($"{ItemsRoot}/{FolderFor(slot)}/")) return true;
            return false;
        }

        private static void EnsureReadable(string folder)
        {
            foreach (var path in Directory.GetFiles(folder, "*.fbx").Select(p => p.Replace('\\', '/')))
            {
                if (AssetImporter.GetAtPath(path) is ModelImporter mi && !mi.isReadable)
                {
                    mi.isReadable = true;
                    mi.SaveAndReimport();
                }
            }
        }
    }
}
