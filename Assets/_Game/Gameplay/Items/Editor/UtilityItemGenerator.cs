using System.Collections.Generic;
using System.Linq;
using KryticalNova.Crafting;
using KryticalNova.Items;
using UnityEditor;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Creates every item that is not gear from one table: the Stims, permits, rations, forge materials, Serums and
    /// what the appearance services use up today, and whatever a later system needs (Recall Tags, Skill Chips) as a
    /// new row. Registers them in the item catalog.
    /// An asset that already exists keeps its numbers and text, so hand tuning survives a re-run; only what the
    /// table owns outright (what kind of item it is) is refreshed. IDs are never renumbered: append.
    /// </summary>
    public static class UtilityItemGenerator
    {
        private const string ItemsRoot = "Assets/_Game/Gameplay/Items/Data/Assets";
        private const string Folder = ItemsRoot + "/Consumables";
        /// <summary>What the forge uses up: Alloys, Failsafes, the Fusion Catalyst, Tuning Chips.</summary>
        private const string MaterialFolder = ItemsRoot + "/GearMaterials";
        /// <summary>What the Fabricator uses up that is not for the forge: colour cells, Dossier Pages.</summary>
        public const string CraftFolder = ItemsRoot + "/CraftMaterials";
        public const string ConsumableFolder = Folder;
        private const string CatalogPath = ItemsRoot + "/ItemCatalog.asset";

        private const ushort StimStack = 99;
        private const ushort StarterStims = 10;
        // Three sizes of each Stim (Twelve Sky 2 sold its pills in sizes too). The first Stims are the small ones.
        // A first pick (15 Oct 2026): S fills a new character, L a large part of a level-112 one. To tune.
        private const float StimAmount = 300f, StimAmountM = 900f, StimAmountL = 2400f;
        /// <summary>The Dual Stim restores HP : Flux at 3 : 1.</summary>
        private const float DualFluxAmount = StimAmount / 3f;

        /// <summary>One utility item. <see cref="Starter"/> is how many a new character is handed (0 = none).</summary>
        private struct Row
        {
            public ushort Id;
            public string Asset, Name, Glyph, Description;
            public ItemRarity Rarity;
            public ushort Stack, Starter;
            public float Health, Flux;
            public bool Hotbar;
            public ItemUseEffect Effect;
            /// <summary>Seconds before the next consumable can be used. 0 = the item's usual one.</summary>
            public float Cooldown;
            /// <summary>Null = the consumables folder.</summary>
            public string Folder;
            /// <summary>The asset name of the item whose icon this one shows. Null = an icon of its own.</summary>
            public string IconOf;
        }

        // Stims keep their first asset names (Pill_*): saves, hotbars and GM commands are keyed on them.
        private static readonly Row[] Rows =
        {
            new Row { Id = 9001, Asset = "Pill_Vitality", Name = "HP Stim (S)", Glyph = "HP", Rarity = ItemRarity.Common,
                Description = $"Restores {StimAmount:0} HP.", Stack = StimStack, Starter = StarterStims, Health = StimAmount, Hotbar = true },
            new Row { Id = 9002, Asset = "Pill_Chi", Name = "Flux Stim (S)", Glyph = "FLUX", Rarity = ItemRarity.Common,
                Description = $"Restores {StimAmount:0} Flux.", Stack = StimStack, Starter = StarterStims, Flux = StimAmount, Hotbar = true },
            new Row { Id = 9003, Asset = "Pill_DualHarmony", Name = "Dual Stim (S)", Glyph = "HP+FLUX", Rarity = ItemRarity.Uncommon,
                Description = $"Restores {StimAmount:0} HP and {DualFluxAmount:0} Flux.", Stack = StimStack,
                Health = StimAmount, Flux = DualFluxAmount, Hotbar = true },
            Stim(9004, "Stim_HP_M", "HP Stim (M)", "HP M", ItemRarity.Common, StimAmountM, 0f),
            Stim(9005, "Stim_HP_L", "HP Stim (L)", "HP L", ItemRarity.Uncommon, StimAmountL, 0f),
            Stim(9006, "Stim_Flux_M", "Flux Stim (M)", "FLUX M", ItemRarity.Common, 0f, StimAmountM),
            Stim(9007, "Stim_Flux_L", "Flux Stim (L)", "FLUX L", ItemRarity.Uncommon, 0f, StimAmountL),
            Stim(9008, "Stim_Dual_M", "Dual Stim (M)", "DUAL M", ItemRarity.Uncommon, StimAmountM, StimAmountM / 3f),
            Stim(9009, "Stim_Dual_L", "Dual Stim (L)", "DUAL L", ItemRarity.Rare, StimAmountL, StimAmountL / 3f),

            // Twelve Sky's Calabash as a Flux reservoir (owner, 15 Oct 2026): kept and refilled, never stacked, since
            // each bottle carries the drinks it has used. What a drink gives is FlaskSettings.
            new Row { Id = FluxFlaskId, Asset = "Flux_Flask", Name = "Flux Flask", Glyph = "FLASK", Rarity = ItemRarity.Uncommon,
                Description = "A bottle of Flux to carry: each drink gives a large part of your Flux back over a few seconds, and the bottle is kept. An Apothecary refills it for Creds.",
                Stack = 1, Hotbar = true, Effect = ItemUseEffect.FluxFlask },

            // A quick trip back to town (Twelve Sky 2: Scroll of Return). What stops it is RecallSettings.
            new Row { Id = RecallTagId, Asset = "Recall_Tag", Name = "Recall Tag", Glyph = "RECALL", Rarity = ItemRarity.Common,
                Description = "Takes you back to town after a few seconds standing still. Moving, being hit or a fight stops it, and the Tag comes back to your Pack. Not in a fight, a Callout or a Crew Hall.",
                Stack = StimStack, Hotbar = true, Effect = ItemUseEffect.Recall },

            // Pets (design: _Docs/Design/12_pet_evolution_system.md).
            new Row { Id = PetRationId, Asset = "Pet_Ration", Name = "Pet Ration", Glyph = "FOOD", Rarity = ItemRarity.Common,
                Description = "Feeds the pet you have out: its hunger goes back to 100%. A starving pet gives no bonus and earns no XP.",
                Stack = 99, Effect = ItemUseEffect.FeedPet },

            // Kiosks: a permit is used up when a Kiosk is opened for its length. Sold in the Nova Store later.
            new Row { Id = KioskPermit14Id, Asset = "Permit_Kiosk14", Name = "Kiosk Permit (14 Days)", Glyph = "14D", Rarity = ItemRarity.Uncommon,
                Description = "Opens your Kiosk for 14 days instead of 3. Used up when the Kiosk opens.", Stack = 99 },
            new Row { Id = KioskPermit30Id, Asset = "Permit_Kiosk30", Name = "Kiosk Permit (30 Days)", Glyph = "30D", Rarity = ItemRarity.Rare,
                Description = "Opens your Kiosk for 30 days instead of 3. Used up when the Kiosk opens.", Stack = 99 },

            // Creds as they lie on the ground (a Hostile's drop). Never in a Pack: picking it up pays the wallet.
            new Row { Id = LootRules.CredsItemId, Asset = "Creds_Drop", Name = "Creds", Glyph = "CREDS", Rarity = ItemRarity.Common,
                Description = "Creds. Picked up, they go straight into your wallet.", Stack = LootRules.MaxCredsPerDrop },

            // Autopilot (_Docs/Plans/hostiles-plan.md, section 8; owner 15 Oct 2026): a licence by the clock, like the
            // Autobuff's. The 3-day Pass was the Autopilot Chip (same id, asset renamed); it drops from Hostiles, and
            // all three are Nova Store stock.
            AutopilotPass(AutopilotPass3Id, "Pass_Autopilot3", 3, ItemRarity.Uncommon, null, "Dropped now and then by Hostiles of level 10 and up, and sold in the Nova Store."),
            AutopilotPass(AutopilotPass14Id, "Pass_Autopilot14", 14, ItemRarity.Rare, "Pass_Autopilot3", "Sold in the Nova Store."),
            AutopilotPass(AutopilotPass30Id, "Pass_Autopilot30", 30, ItemRarity.Epic, "Pass_Autopilot3", "Sold in the Nova Store."),

            // The Autobuff (owner, 15 Oct 2026): a licence by the clock, like a Kiosk's, that casts up to eight slotted
            // buffs from one Hotbar key. The 3-day Pass drops from Hostiles; all three are Nova Store stock.
            AutobuffPass(AutobuffPass3Id, "Pass_Autobuff3", 3, ItemRarity.Uncommon, null, "Dropped now and then by Hostiles of level 10 and up, and sold in the Nova Store."),
            AutobuffPass(AutobuffPass14Id, "Pass_Autobuff14", 14, ItemRarity.Rare, "Pass_Autobuff3", "Sold in the Nova Store."),
            AutobuffPass(AutobuffPass30Id, "Pass_Autobuff30", 30, ItemRarity.Epic, "Pass_Autobuff3", "Sold in the Nova Store."),

            // The appearance services: what one change uses up at the Outfitter, at the Stylist or the Reshaper, and
            // at the Coach. Sold in the Nova Store later; only GM tools give them out for now. EconomySettings names
            // the ids (dyeItemId, restyleItemId, trainingItemId).
            new Row { Id = CraftItemIds.ChromaCell, Asset = "Chroma_Cell", Name = "Chroma Cell", Glyph = "DYE", Rarity = ItemRarity.Uncommon,
                Description = "Dyes one piece of your outfit in another colourway at an Outfitter. Used up by the dye. A Fabricator makes one from a Cyan, a Magenta, a Yellow and a Black Cell.", Stack = 99 },
            new Row { Id = CraftItemIds.PrismCell, Asset = "Prism_Cell", Name = "Prism Cell", Glyph = "PRISM", Rarity = ItemRarity.Uncommon,
                Description = "Dyes one weapon in another colourway at an Outfitter. Used up by the dye. A Fabricator makes one from a Cyan, a Magenta, a Yellow and a Black Cell.", Stack = 99 },
            new Row { Id = CraftItemIds.RestylePass, Asset = "Pass_Restyle", Name = "Restyle Pass", Glyph = "STYLE", Rarity = ItemRarity.Rare,
                Description = "One change of hair at a Stylist, or of face at a Reshaper. Used up by the change.", Stack = 99 },
            new Row { Id = CraftItemIds.TrainingPass, Asset = "Pass_Training", Name = "Training Pass", Glyph = "TRAIN", Rarity = ItemRarity.Rare,
                Description = "One change of build and muscle at a Coach. Used up by the change.", Stack = 99 },

            // The four colours of a Chroma Cell: Hostile drops, and the Outfitter sells one of them each in-game day.
            Colour(CraftItemIds.CyanCell, "Cell_Cyan", "Cyan Cell", "C"),
            Colour(CraftItemIds.MagentaCell, "Cell_Magenta", "Magenta Cell", "M"),
            Colour(CraftItemIds.YellowCell, "Cell_Yellow", "Yellow Cell", "Y"),
            Colour(CraftItemIds.BlackCell, "Cell_Black", "Black Cell", "K"),

            // Dossiers (Twelve Sky 2: Archives). Each teaches one skill when it is used; a Fabricator binds it from
            // its four Pages. Pages are Hostile drops, and so, far more rarely, is the Dossier itself.
            Dossier(CraftItemIds.BacklashDossier, "Dossier_Backlash", "Backlash"),
            Dossier(CraftItemIds.IronNerveDossier, "Dossier_IronNerve", "Iron Nerve"),
            Dossier(CraftItemIds.ShatterpointDossier, "Dossier_Shatterpoint", "Shatterpoint"),
            Page(CraftItemIds.BacklashDossier, 1, "Backlash"), Page(CraftItemIds.BacklashDossier, 2, "Backlash"),
            Page(CraftItemIds.BacklashDossier, 3, "Backlash"), Page(CraftItemIds.BacklashDossier, 4, "Backlash"),
            Page(CraftItemIds.IronNerveDossier, 1, "Iron Nerve"), Page(CraftItemIds.IronNerveDossier, 2, "Iron Nerve"),
            Page(CraftItemIds.IronNerveDossier, 3, "Iron Nerve"), Page(CraftItemIds.IronNerveDossier, 4, "Iron Nerve"),
            Page(CraftItemIds.ShatterpointDossier, 1, "Shatterpoint"), Page(CraftItemIds.ShatterpointDossier, 2, "Shatterpoint"),
            Page(CraftItemIds.ShatterpointDossier, 3, "Shatterpoint"), Page(CraftItemIds.ShatterpointDossier, 4, "Shatterpoint"),

            // Zenith Dossiers (Twelve Sky 2: the War God skills). One for each weapon class's Zenith technique; each
            // unlocks that one skill. Dropped whole in the Prime grounds: no Pages, no recipe.
            ZenithDossier(CraftItemIds.ZenithMonoblade, "Monoblade", "Market Crash"),
            ZenithDossier(CraftItemIds.ZenithGlaive, "Glaive", "Martial Law"),
            ZenithDossier(CraftItemIds.ZenithCarbine, "Carbine", "Orbital Call"),
            ZenithDossier(CraftItemIds.ZenithHarvester, "Harvester", "Terminal Harvest"),
            ZenithDossier(CraftItemIds.ZenithServoClaws, "Servo Claws", "Zero Point"),
            ZenithDossier(CraftItemIds.ZenithArcCaster, "Arc Caster", "Extinction Beam"),
            ZenithDossier(CraftItemIds.ZenithLongaxe, "Longaxe", "Northfall"),
            ZenithDossier(CraftItemIds.ZenithClaymore, "Claymore", "Kingsfall"),
            ZenithDossier(CraftItemIds.ZenithWarbow, "Warbow", "Skyfall"),

            // XP boosters (Nova Store stock; what each adds and for how long is EconomySettings.boosters). Names are a
            // first pick (11 Oct 2026).
            Booster(9500, "Booster_Field", "Field Booster", "Field XP", "the XP of your Hostile kills"),
            Booster(9501, "Booster_Companion", "Companion Booster", "Pet XP", "the XP your pet earns from your Hostile kills"),
            Booster(9502, "Booster_Tandem", "Tandem Booster", "Both XP", "the XP of your Hostile kills, and your pet's"),

            // The forge (_Docs/Reference/12Sky/02_Gear_System_Spec.md). Ids are named in GearMaterialIds, which the
            // settings asset reads; what each one does is decided there, not here.
            Material(GearMaterialIds.Alumite, "Alloy_Alumite", "Alumite", "+3%", ItemRarity.Common,
                "Alloy. Tempers a piece of gear by 3% on a success."),
            Material(GearMaterialIds.Iridion, "Alloy_Iridion", "Iridion", "+6%", ItemRarity.Common,
                "Alloy. Tempers a piece of gear by 6% on a success."),
            Material(GearMaterialIds.Orichalc, "Alloy_Orichalc", "Orichalc", "+9%", ItemRarity.Uncommon,
                "Alloy. Tempers a piece of gear by 9% on a success."),
            Material(GearMaterialIds.Eversteel, "Alloy_Eversteel", "Eversteel", "+12%", ItemRarity.Rare,
                "Alloy. Tempers a piece of gear by 12% on a success."),
            Material(GearMaterialIds.TrinityPlatinum, "Alloy_TrinityPlatinum", "Trinity Platinum", "+15%", ItemRarity.Epic,
                "Alloy. Tempers a piece of gear by 15% on a success."),
            Material(GearMaterialIds.PrimeAlloy, "Alloy_Prime", "Prime Alloy", "PRIME", ItemRarity.Epic,
                "For a later system: Tempering beyond 120%. It has no use yet."),
            Material(GearMaterialIds.Failsafe, "Failsafe", "Failsafe", "SAFE", ItemRarity.Rare,
                "Stops a failed Tempering from breaking the piece. Used up only when it does."),
            Material(GearMaterialIds.AdvancedFailsafe, "Failsafe_Advanced", "Advanced Failsafe", "SAFE+", ItemRarity.Epic,
                "A failed Tempering costs the piece nothing and never breaks it. Used up on a fail."),
            Material(GearMaterialIds.LuckyFailsafe, "Failsafe_Lucky", "Lucky Failsafe", "LUCK", ItemRarity.Rare,
                "Adds 5% to one Tempering attempt and stops that attempt from breaking the piece. Used up on the attempt."),
            Material(GearMaterialIds.FusionCatalyst, "Fusion_Catalyst", "Fusion Catalyst", "FS", ItemRarity.Rare,
                "Used up by every Fusion attempt."),
            Material(GearMaterialIds.TuningChip, "Tuning_Chip", "Tuning Chip", "TUNE", ItemRarity.Rare,
                "Used up by a Tuning attempt, stages 1 to 12."),
            Material(GearMaterialIds.VioletChip, "Tuning_VioletChip", "Violet Chip", "VIOLET", ItemRarity.Epic,
                "Used up by a Tuning attempt, stages 13 to 25."),
            Material(GearMaterialIds.ResetChip, "Tuning_ResetChip", "Reset Chip", "RESET", ItemRarity.Rare,
                "Returns a piece of gear to Tune 0. Nothing is refunded."),

            Serum(KryticalNova.Progression.SerumIds.Strength, "Serum_Strength", "Strength Serum", "STR",
                "A one-way dose for the muscle: what it adds to your strikes stays for good."),
            Serum(KryticalNova.Progression.SerumIds.Vitality, "Serum_Vitality", "Vitality Serum", "VIT",
                "A one-way dose for the body: the health it adds stays for good."),
            Serum(KryticalNova.Progression.SerumIds.Agility, "Serum_Agility", "Agility Serum", "AGI",
                "A one-way dose for the nerves: surer strikes and quicker feet, for good."),
            Serum(KryticalNova.Progression.SerumIds.Spirit, "Serum_Spirit", "Spirit Serum", "SPI",
                "A one-way dose for the mind: the Flux it adds stays for good."),
            Serum(KryticalNova.Progression.SerumIds.ElementalStrike, "Serum_ElementalStrike", "Elemental Strike Serum", "E.ATK",
                "A one-way dose that charges your strikes with the elements, for good."),
            Serum(KryticalNova.Progression.SerumIds.ElementalWard, "Serum_ElementalWard", "Elemental Ward Serum", "E.DEF",
                "A one-way dose that hardens you against the elements, for good."),
        };

        // Serums (Twelve Sky 2: Elixirs). What each one gives and how many a character can take is SerumSettings;
        // the tooltip reads it from there, so the description carries no number. They are taken many at a time
        // (a hundred of a kind), so one does not hold the next up for a whole second.
        private const float SerumCooldown = 0.25f;

        private static Row Serum(ushort id, string asset, string name, string glyph, string description) =>
            new Row { Id = id, Asset = asset, Name = name, Glyph = glyph, Rarity = ItemRarity.Rare, Description = description,
                Stack = 99, Effect = ItemUseEffect.Serum, Cooldown = SerumCooldown };

        private static Row AutobuffPass(ushort id, string asset, int days, ItemRarity rarity, string iconOf, string source) =>
            new Row { Id = id, Asset = asset, Name = $"Autobuff Pass ({days} Days)", Glyph = $"AB {days}D", Rarity = rarity, Stack = 99,
                Effect = ItemUseEffect.AutobuffTime, IconOf = iconOf,
                Description = $"Adds {days} days of Autobuff: every buff skill you slot in the Autopilot window is cast with one Hotbar key, and Autopilot keeps them up. The days count by the clock, as a Kiosk Permit's do; another Pass adds to the end. {source}" };

        private static Row AutopilotPass(ushort id, string asset, int days, ItemRarity rarity, string iconOf, string source) =>
            new Row { Id = id, Asset = asset, Name = $"Autopilot Pass ({days} Days)", Glyph = $"AP {days}D", Rarity = rarity, Stack = 99,
                Effect = ItemUseEffect.AutopilotTime, IconOf = iconOf,
                Description = $"Adds {days} days of Autopilot: your character hunts by itself inside a Hostile Zone and picks up what Magnet is set to take. The days count by the clock, as a Kiosk Permit's do, whether Autopilot is on or not; another Pass adds to the end. {source}" };

        private static Row Stim(ushort id, string asset, string name, string glyph, ItemRarity rarity, float health, float flux) =>
            new Row { Id = id, Asset = asset, Name = name, Glyph = glyph, Rarity = rarity, Stack = StimStack, Health = health, Flux = flux, Hotbar = true,
                Description = health > 0f && flux > 0f ? $"Restores {health:0} HP and {flux:0} Flux." : health > 0f ? $"Restores {health:0} HP." : $"Restores {flux:0} Flux." };
        // A method, not a table: the rows above are built before any field declared after them.
        private static string Numeral(int page) => page == 1 ? "I" : page == 2 ? "II" : page == 3 ? "III" : "IV";

        private static Row Colour(ushort id, string asset, string name, string glyph) =>
            new Row { Id = id, Asset = asset, Name = name, Glyph = glyph, Rarity = ItemRarity.Common, Stack = 99, Folder = CraftFolder,
                Description = "One of the four colours of a Chroma Cell. A Fabricator joins one of each into a Chroma Cell." };

        private static Row Dossier(ushort id, string asset, string skill) =>
            new Row { Id = id, Asset = asset, Name = $"{skill} Dossier", Glyph = "DOSSIER", Rarity = ItemRarity.Epic, Stack = 10,
                Effect = ItemUseEffect.LearnSkill,
                Description = $"Unlocks {skill}, which is then learned with skill points. Used up when it is read. A Fabricator binds one from its four Pages." };

        private static Row ZenithDossier(ushort id, string weaponClass, string skill) =>
            new Row { Id = id, Asset = $"Dossier_Zenith_{weaponClass.Replace(" ", string.Empty)}", Name = $"Zenith Dossier: {skill}", Glyph = "ZENITH",
                Rarity = ItemRarity.Legendary, Stack = 10, Effect = ItemUseEffect.LearnSkill,
                Description = $"Unlocks {skill}, the {weaponClass}'s Zenith technique, which is then learned with skill points. Used up when it is read. Found only in the Prime grounds." };

        /// <summary>
        /// The Skill Chips (11 Oct 2026): one for each class's level 60 pair and level 80 pair, its two-hit attacks of
        /// levels 60 and 80 (16 Oct 2026), and one each for Aegis, Killer Instinct and Hot Streak. Their names follow the skills', which are the class skill builder's.
        /// </summary>
        private static IEnumerable<Row> SkillChipRows()
        {
            foreach (KryticalNova.Progression.WeaponClassId weaponClass in KryticalNova.Progression.WeaponClasses.All)
            {
                string className = KryticalNova.Progression.WeaponClasses.DisplayName(weaponClass);
                foreach (KryticalNova.Progression.ClassSkillSlot slot in CraftItemIds.SkillChipSlots)
                {
                    bool prime = CraftItemIds.IsPrimeChipSlot(slot);
                    string skill = ClassSkillBuilder.SkillName(weaponClass, slot);
                    string asset = $"Chip_{weaponClass}_{slot}";
                    yield return SkillChip(CraftItemIds.SkillChip(weaponClass, slot), asset, skill,
                        $"a {className} skill of level {(prime ? 80 : 60)}", prime ? ItemRarity.Epic : ItemRarity.Rare,
                        prime ? "Found only in the Prime grounds." : "Dropped by Hostiles of level 60 and up.", asset == AttackChipIcon ? null : AttackChipIcon);
                }
            }
            yield return SkillChip(CraftItemIds.AegisChip, BuffChipIcon, "Aegis", "a buff of level 60 every class can learn", ItemRarity.Rare, "Dropped by Hostiles of level 60 and up.", null);
            yield return SkillChip(CraftItemIds.KillerInstinctChip, "Chip_KillerInstinct", "Killer Instinct", "a buff of level 60 every class can learn", ItemRarity.Rare, "Dropped by Hostiles of level 60 and up.", BuffChipIcon);
            yield return SkillChip(CraftItemIds.HotStreakChip, "Chip_HotStreak", "Hot Streak", "a buff of level 70 every class can learn", ItemRarity.Rare, "Dropped by Hostiles of level 70 and up.", BuffChipIcon);
        }

        /// <summary>
        /// Two pictures for all the Skill Chips (owner, 11 Oct 2026): one for the chips of attack skills, one for the
        /// chips of buffs. The chip named here has the picture in the icon library; the others of its kind show it.
        /// What a chip teaches is in its name and its text.
        /// </summary>
        public const string AttackChipIcon = "Chip_Monoblade_Lv60Single", BuffChipIcon = "Chip_Aegis";

        private static Row SkillChip(ushort id, string asset, string skill, string what, ItemRarity rarity, string source, string iconOf) =>
            new Row { Id = id, Asset = asset, Name = $"Skill Chip: {skill}", Glyph = "CHIP", Rarity = rarity, Stack = 10, Effect = ItemUseEffect.LearnSkill,
                IconOf = iconOf,
                Description = $"Unlocks {skill}, {what}, which is then learned with skill points. Used up when it is read. {source}" };

        // What it adds and for how long is a number of the Economy Settings: the item's text does not repeat it (its tooltip reads it there).
        private static Row Booster(ushort id, string asset, string name, string glyph, string what) =>
            new Row { Id = id, Asset = asset, Name = name, Glyph = glyph, Rarity = ItemRarity.Rare, Stack = 99, Effect = ItemUseEffect.ExperienceBoost,
                Description = $"Raises {what} for a time. Using another adds its time; different boosters add up. From the Nova Store." };

        /// <summary>A Dossier's Page. Asset and name are worked out from the Dossier's: Page_Backlash_1, "Backlash Page I".</summary>
        private static Row Page(ushort dossier, int page, string skill) =>
            new Row { Id = CraftItemIds.PageOf(dossier, page), Asset = $"Page_{skill.Replace(" ", string.Empty)}_{page}", Name = $"{skill} Page {Numeral(page)}",
                Glyph = "PAGE " + Numeral(page), Rarity = ItemRarity.Rare, Stack = 99, Folder = CraftFolder,
                Description = $"One of the four Pages of the {skill} Dossier. A Fabricator binds all four into the Dossier." };

        private static Row Material(ushort id, string asset, string name, string glyph, ItemRarity rarity, string description) =>
            new Row { Id = id, Asset = asset, Name = name, Glyph = glyph, Rarity = rarity, Description = description, Stack = 999, Folder = MaterialFolder };

        // Ids other systems name. 9000–9999 belongs to this table.
        public const ushort PetRationId = 9010;
        public const ushort RecallTagId = 9011;
        public const ushort FluxFlaskId = 9012;
        public const ushort KioskPermit14Id = 9020;
        public const ushort KioskPermit30Id = 9021;
        public const ushort AutopilotPass3Id = 9030, AutopilotPass14Id = 9034, AutopilotPass30Id = 9035;
        public const ushort AutobuffPass3Id = 9031, AutobuffPass14Id = 9032, AutobuffPass30Id = 9033;

        [MenuItem("Krytical Nova/Items/5. Generate Utility Items (Stims, Permits, Rations, Forge Materials, Serums)")]
        public static void Generate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogError($"[Items] No item catalog at {CatalogPath}. Run 'Krytical Nova/Items/1. Generate Gear Looks From Synty' first.");
                return;
            }

            WeatherVfxBuilder.EnsureFolder(Folder);
            WeatherVfxBuilder.EnsureFolder(MaterialFolder);
            WeatherVfxBuilder.EnsureFolder(CraftFolder);
            var made = new List<ItemDefinition>(Rows.Length);
            var bag = catalog.StarterBag.ToList();
            var byAsset = new Dictionary<string, ItemDefinition>();
            var rows = Rows.Concat(SkillChipRows()).ToList();
            foreach (Row row in rows)
            {
                ItemDefinition item = Item(row);
                made.Add(item);
                byAsset[row.Asset] = item;
                if (row.Starter > 0 && !bag.Any(s => s.item == item)) bag.Add(new StarterItem { item = item, count = row.Starter });
            }
            // A family that shares one picture: each shows the icon of the item named, which the icon library gives it.
            foreach (Row row in rows)
            {
                ItemDefinition item = byAsset[row.Asset];
                ItemDefinition source = row.IconOf != null && byAsset.TryGetValue(row.IconOf, out ItemDefinition found) ? found : null;
                if (item.sharesIconOf == source && (source == null || item.icon == source.icon)) continue;
                item.sharesIconOf = source;
                if (source != null) item.icon = source.icon;
                EditorUtility.SetDirty(item);
            }

            var items = catalog.Items.Where(i => i != null).Concat(made).Distinct().OrderBy(i => i.itemId).ToList();
            catalog.SetContents(items, catalog.StarterEquipped.ToList(), bag);
            EditorUtility.SetDirty(catalog);
            int drops = AddPassDrop(byAsset, "Pass_Autobuff3") + AddPassDrop(byAsset, "Pass_Autopilot3");
            AssetDatabase.SaveAssets();
            Debug.Log($"[Items] {made.Count} utility items registered in {CatalogPath}; {drops} drop row(s) added.");
        }

        private const string HostileSettingsPath = "Assets/_Game/Gameplay/Hostiles/Data/Assets/HostileSettings.asset";
        private const float PassDropChance = 0.3f; // placeholder, percent before the level gap and the server drop rate
        private const int PassDropLevel = 10;

        /// <summary>A 3-day Pass (Autobuff, Autopilot) joins the Hostile drop table (its chance may have been tuned since: a row that is there is left).</summary>
        private static int AddPassDrop(Dictionary<string, ItemDefinition> byAsset, string asset)
        {
            if (!byAsset.TryGetValue(asset, out ItemDefinition pass) || pass == null) return 0;
            var hostiles = AssetDatabase.LoadAssetAtPath<KryticalNova.Hostiles.HostileSettings>(HostileSettingsPath);
            if (hostiles == null) { Debug.LogWarning($"[Items] No Hostile settings at {HostileSettingsPath}: the {asset} drop was not added."); return 0; }
            var rows = new List<KryticalNova.Hostiles.MaterialDrop>(hostiles.materialDrops ?? System.Array.Empty<KryticalNova.Hostiles.MaterialDrop>());
            if (rows.Exists(row => row.item == pass)) return 0;
            rows.Add(new KryticalNova.Hostiles.MaterialDrop { item = pass, chancePercent = PassDropChance, minLevel = PassDropLevel });
            hostiles.materialDrops = rows.ToArray();
            EditorUtility.SetDirty(hostiles);
            return 1;
        }

        private static ItemDefinition Item(in Row row)
        {
            string path = $"{row.Folder ?? Folder}/{row.Asset}.asset";
            var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool created = def == null;
            if (created)
            {
                def = ScriptableObject.CreateInstance<ItemDefinition>();
                def.itemId = row.Id;
                def.displayName = row.Name;
                def.description = row.Description;
                def.rarity = row.Rarity;
                def.maxStack = row.Stack;
                def.restoreHealth = row.Health;
                def.restoreChi = row.Flux;
                if (row.Cooldown > 0f) def.useCooldown = row.Cooldown;
            }

            // What the table owns on every run: the kind of item this is, and the text it shows until it has an icon.
            // A Skill Chip's words are the table's too: they say which skill it teaches, which is all that tells chips apart.
            if (row.Effect == ItemUseEffect.LearnSkill && row.Glyph == "CHIP")
            {
                def.displayName = row.Name;
                def.description = row.Description;
            }
            def.placeholderGlyph = row.Glyph;
            def.equipSlot = EquipSlot.None;
            def.usableFromHotbar = row.Hotbar;
            def.useEffect = row.Effect;

            if (created) AssetDatabase.CreateAsset(def, path);
            else EditorUtility.SetDirty(def);
            return def;
        }
    }
}
