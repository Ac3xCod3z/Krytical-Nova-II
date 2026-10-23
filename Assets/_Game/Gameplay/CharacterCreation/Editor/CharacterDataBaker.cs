using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Bakes everything the character creator and the player's look need, so runtime code never touches Sidekick's
    /// database: the six lineages (basic outfit, head options per body type, builds, ready-made faces, skin tones),
    /// colour palettes from the premade characters, the colour channels and the attachment-joint table.
    /// Safe to re-run: IDs are stable, and names, descriptions and traits edited on a lineage asset are kept.
    /// </summary>
    public static class CharacterDataBaker
    {
        public const string Folder = "Assets/_Game/Gameplay/CharacterCreation/Data/Assets";
        public const string DatabasePath = Folder + "/CharacterCreationDatabase.asset";
        public const string PalettePath = Folder + "/CharacterPalettes.asset";
        public const string LineageFolder = Folder + "/Lineages";
        private const string PremadeRoot = "Assets/Synty/SidekickCharacters/Characters";
        private const string BaseModelPath = SidekickPartCatalogBaker.MeshRoot + "/SK_BaseModel.fbx";

        // Colour channel IDs are stored in saved characters: never renumber.
        public const byte ChannelHair = CharacterColorChannels.Hair, ChannelEyes = CharacterColorChannels.Eyes,
            ChannelGlow = CharacterColorChannels.Glow, ChannelBone = CharacterColorChannels.Bone, ChannelMetal = CharacterColorChannels.Metal;

        private sealed class LineageSpec
        {
            public byte id;
            public FactionId faction;
            public string asset, name, description;
            public string[] traits;
            public string species;      // rig suffix letters of this lineage's own head: HU, EV, SN, RO
            public string skinSpecies;  // species code of Sidekick's skin presets, or null when it has no skin
            public BodyFit body;
            public string ownPack;      // the pack whose variant 01 is the basic outfit
            public string basePack;     // species base body, used where the own pack has no variant 01
            public string barePack;     // the pack of the bare body (empty gear slots); null = the species base
            public byte bareVariant = 1; // which variant of that pack is the bare body
            public string[] headPacks;  // packs whose head parts are offered, own first
            public string[] premades;   // premade character folders used for palettes and ready-made faces
            public string wrapPart;
            public byte[] channels;
        }

        private static readonly LineageSpec[] Specs =
        {
            new LineageSpec
            {
                id = 1, faction = FactionId.Hegemony, asset = "Civilian", name = "Civilian", species = "HU", skinSpecies = "HN",
                description = "Citizens, technicians and corporate staff of the Hegemony's cities.",
                traits = new[] { "Adaptable", "Connected", "Resourceful" }, body = BodyFit.Humanoid,
                ownPack = "SCFI_CIVL", basePack = "HUMN_BASE", headPacks = new[] { "HUMN_BASE", "SCFI_CIVL", "SCFI_SOLD" },
                premades = new[] { "ScifiCivilians" }, wrapPart = "SK_HUMN_BASE_01_38WRAP_HU01",
                channels = new[] { ChannelHair, ChannelEyes }
            },
            new LineageSpec
            {
                id = 2, faction = FactionId.Hegemony, asset = "Soldier", name = "Soldier", species = "HU", skinSpecies = "HN",
                description = "Void-marines and enforcers who hold the Hegemony's borders.",
                traits = new[] { "Disciplined", "Armored", "Relentless" }, body = BodyFit.Humanoid,
                ownPack = "SCFI_SOLD", basePack = "HUMN_BASE", headPacks = new[] { "HUMN_BASE", "SCFI_SOLD", "SCFI_CIVL" },
                premades = new[] { "ScifiSoldiers" }, wrapPart = "SK_HUMN_BASE_01_38WRAP_HU01",
                channels = new[] { ChannelHair, ChannelEyes }
            },
            new LineageSpec
            {
                id = 3, faction = FactionId.Null, asset = "Robot", name = "Robot", species = "RO", skinSpecies = null,
                description = "The labour machines that woke up. They compute. They do not forgive.",
                traits = new[] { "Tireless", "Precise", "Unfeeling" }, body = BodyFit.Robot,
                // The robot's species pack is heads only: its bare chassis is one of its own pack's plainer bodies.
                ownPack = "SCFI_ROBO", basePack = null, barePack = "SCFI_ROBO", bareVariant = 4, headPacks = new[] { "ROBO_BASE" },
                premades = new[] { "ScifiRobots" }, wrapPart = null,
                channels = new[] { ChannelMetal, ChannelGlow }
            },
            new LineageSpec
            {
                id = 4, faction = FactionId.Null, asset = "Skeleton", name = "Skeleton", species = "SN", skinSpecies = null,
                description = "The dead who woke in skeletal bodies when the cycle broke: the undead half of the Null.",
                traits = new[] { "Deathless", "Ancient", "Hollow" }, body = BodyFit.Skeleton,
                ownPack = "FANT_SKTN", basePack = "SKTN_BASE", headPacks = new[] { "SKTN_BASE" },
                premades = new[] { "FantasySkeletons" }, wrapPart = null,
                channels = new[] { ChannelBone, ChannelGlow }
            },
            new LineageSpec
            {
                id = 5, faction = FactionId.Covenant, asset = "Viking", name = "Viking", species = "HU", skinSpecies = "HN",
                description = "Clans of the northern fjords, oath-bound and hard to kill.",
                traits = new[] { "Fearless", "Hardy", "Kin-bound" }, body = BodyFit.Humanoid,
                ownPack = "VIKG_WARR", basePack = "HUMN_BASE", headPacks = new[] { "HUMN_BASE", "VIKG_WARR", "ELVN_WARR" },
                premades = new[] { "VikingWarriors" }, wrapPart = "SK_HUMN_BASE_01_38WRAP_HU01",
                channels = new[] { ChannelHair, ChannelEyes }
            },
            new LineageSpec
            {
                id = 6, faction = FactionId.Covenant, asset = "Elf", name = "Elf", species = "EV", skinSpecies = "EV",
                description = "The forest houses: swift, patient, and done with patience.",
                traits = new[] { "Keen", "Graceful", "Long-lived" }, body = BodyFit.Humanoid,
                ownPack = "ELVN_WARR", basePack = "ELVN_BASE", headPacks = new[] { "ELVN_BASE", "ELVN_WARR", "VIKG_WARR", "HUMN_BASE" },
                premades = new[] { "ElvenWarriors", "ElvenSpecies" }, wrapPart = "SK_ELVN_BASE_01_38WRAP_EV01",
                channels = new[] { ChannelHair, ChannelEyes }
            },
        };

        private static readonly BodyRegion[] HeadRegions =
        {
            BodyRegion.Head, BodyRegion.Hair, BodyRegion.EyebrowLeft, BodyRegion.EyebrowRight, BodyRegion.EyeLeft,
            BodyRegion.EyeRight, BodyRegion.EarLeft, BodyRegion.EarRight, BodyRegion.FacialHair, BodyRegion.Nose,
            BodyRegion.Teeth, BodyRegion.Tongue
        };
        // These must be the lineage's own species (an elf keeps elf ears); hair, brows and beards fit any head.
        private static readonly BodyRegion[] OwnSpeciesOnly =
        {
            BodyRegion.Head, BodyRegion.EyeLeft, BodyRegion.EyeRight, BodyRegion.EarLeft, BodyRegion.EarRight,
            BodyRegion.Nose, BodyRegion.Teeth, BodyRegion.Tongue
        };
        private static readonly BodyRegion[] OptionalRegions =
            { BodyRegion.Hair, BodyRegion.EyebrowLeft, BodyRegion.EyebrowRight, BodyRegion.FacialHair };

        private static readonly Dictionary<int, string> JointOfRegion = new Dictionary<int, string>
        {
            { 24, "backAttach" }, { 25, "hipAttachFront" }, { 26, "hipAttachBack" }, { 27, "hipAttach_l" }, { 28, "hipAttach_r" },
            { 29, "shoulderAttach_l" }, { 30, "shoulderAttach_r" }, { 31, "elbowAttach_l" }, { 32, "elbowAttach_r" },
            { 33, "kneeAttach_l" }, { 34, "kneeAttach_r" }
        };

        // ---- Sidekick database rows (sqlite-net maps columns to these property names) ----
        private sealed class PropertyRow
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int U { get; set; }
            public int V { get; set; }
        }
        private sealed class ColorRow
        {
            public int Owner { get; set; }
            public int Prop { get; set; }
            public string Hex { get; set; }
        }
        private sealed class PresetRow
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public string Species { get; set; }
        }
        private sealed class BodyRow
        {
            public string Name { get; set; }
            public double BodyType { get; set; }
            public double BodySize { get; set; }
            public double Muscle { get; set; }
        }
        private sealed class RigRow
        {
            public int PartType { get; set; }
            public int Blend { get; set; }
            public double Ox { get; set; }
            public double Oy { get; set; }
            public double Oz { get; set; }
            public double Rx { get; set; }
            public double Ry { get; set; }
            public double Rz { get; set; }
        }

        private sealed class Premade
        {
            public string name;
            public List<string> parts;
            public BodyType bodyType;
            public sbyte build, muscle;
            public Dictionary<int, Color32> colors;
        }

        /// <summary>Wipes this machine's saved characters, to test the first-launch flow again.</summary>
        [MenuItem("Krytical Nova/Characters/Clear Saved Characters")]
        public static void ClearSavedCharacters()
        {
            new PlayerPrefsCharacterStore(KryticalNova.Networking.NetworkConnectionManager.LocalProfile()).DeleteAll();
            CharacterSession.Clear();
            Debug.Log("[Characters] Saved characters cleared.");
        }

        [MenuItem("Krytical Nova/Characters/Bake Everything (Parts, Factions, Characters, Gear)")]
        public static void BakeEverything()
        {
            Bake();
            SidekickGearGenerator.Generate();
            CharacterExpressionBaker.Bake();
        }

        [MenuItem("Krytical Nova/Characters/3. Bake Character Data (Lineages, Palettes, Faces)")]
        public static void Bake()
        {
            var parts = SidekickPartCatalogBaker.Bake();
            var factions = FactionAssetBuilder.Build();
            WeatherVfxBuilder.EnsureFolder(Folder);
            WeatherVfxBuilder.EnsureFolder(LineageFolder);

            List<PropertyRow> properties;
            List<ColorRow> defaultColors, presetColors;
            List<PresetRow> skinPresets;
            List<BodyRow> bodyRows;
            List<RigRow> rigRows;
            using (var db = SidekickDatabase.OpenReadOnly())
            {
                if (db == null) return;
                properties = db.Query<PropertyRow>("select id as Id, name as Name, u as U, v as V from sk_color_property");
                defaultColors = db.Query<ColorRow>(
                    "select ptr_color_set as Owner, ptr_color_property as Prop, color as Hex from sk_color_row " +
                    "where ptr_color_set = (select id from sk_color_set where name = 'Default')");
                skinPresets = db.Query<PresetRow>(
                    "select p.id as Id, p.name as Name, s.code as Species from sk_color_preset p " +
                    "join sk_species s on s.id = p.ptr_species where p.color_group = 1 order by p.id");
                presetColors = db.Query<ColorRow>(
                    "select ptr_color_preset as Owner, ptr_color_property as Prop, color as Hex from sk_color_preset_row");
                bodyRows = db.Query<BodyRow>(
                    "select name as Name, cast(body_type as real) as BodyType, cast(body_size as real) as BodySize, " +
                    "cast(musculature as real) as Muscle from sk_body_shape_preset");
                rigRows = db.Query<RigRow>(
                    "select part_type as PartType, blend_type as Blend, max_offset_x as Ox, max_offset_y as Oy, max_offset_z as Oz, " +
                    "max_rotation_x as Rx, max_rotation_y as Ry, max_rotation_z as Rz from sk_blend_shape_rig_movement");
            }

            var propertyById = properties.ToDictionary(p => p.Id);
            var propertyByName = new Dictionary<string, PropertyRow>(StringComparer.Ordinal);
            foreach (var p in properties) propertyByName[p.Name] = p;

            var palettes = LoadOrCreate<CharacterPaletteLibrary>(PalettePath);
            var channels = BuildChannels(propertyByName);
            var builds = BuildBodyPresets(bodyRows);

            var lineages = new List<LineageDefinition>();
            foreach (var spec in Specs)
            {
                var premades = ReadPremades(spec);
                foreach (var premade in premades)
                    palettes.Upsert(new CharacterPaletteLibrary.Palette
                    {
                        name = premade.name, lineage = spec.id,
                        cells = BuildCells(propertyById, defaultColors, premade.colors)
                    });
                lineages.Add(BuildLineage(spec, parts, palettes, premades, builds, skinPresets, presetColors, propertyById, propertyByName));
            }
            EditorUtility.SetDirty(palettes);

            var database = LoadOrCreate<CharacterCreationDatabase>(DatabasePath);
            database.SetContents(factions, parts, palettes, lineages, channels, ReadJointRests(), BuildJointOffsets(rigRows));
            EditorUtility.SetDirty(database);
            AssetDatabase.SaveAssets();
            PartLibrary.Clear();
            Debug.Log($"[Characters] Baked {lineages.Count} lineages, {palettes.Palettes.Count} palettes, {channels.Count} colour channels " +
                      $"into {Folder}. Next: Krytical Nova/Items/1. Generate Test Gear From Synty.");
        }

        // ---------------------------------------------------------------- lineages

        private static LineageDefinition BuildLineage(LineageSpec spec, SidekickPartCatalog parts, CharacterPaletteLibrary palettes,
            List<Premade> premades, Dictionary<BodyType, List<LineageDefinition.BuildPreset>> builds, List<PresetRow> skinPresets,
            List<ColorRow> presetColors, Dictionary<int, PropertyRow> propertyById, Dictionary<string, PropertyRow> propertyByName)
        {
            string path = $"{LineageFolder}/Lineage_{spec.asset}.asset";
            var lineage = AssetDatabase.LoadAssetAtPath<LineageDefinition>(path);
            if (lineage == null)
            {
                lineage = ScriptableObject.CreateInstance<LineageDefinition>();
                lineage.displayName = spec.name;
                lineage.description = spec.description;
                lineage.traits = spec.traits;
                AssetDatabase.CreateAsset(lineage, path);
            }
            lineage.id = spec.id;
            lineage.faction = spec.faction;
            lineage.speciesCode = spec.species;
            lineage.body = spec.body;
            lineage.ownPack = spec.ownPack;
            lineage.wrapPartId = spec.wrapPart != null ? parts.IdOf(spec.wrapPart) : (ushort)0;
            lineage.defaultPalette = premades.Count > 0 ? palettes.IdOf(premades[0].name) : (ushort)0;

            // The basic outfit: variant 01 of the lineage's own pack, the species base where the pack has none.
            var outfit = new List<ushort>();
            for (var region = BodyRegion.Torso; region <= BodyRegion.FootRight; region++)
            {
                if (parts.TryFind(spec.ownPack, region, 1, out var part)) outfit.Add(part.id);
                else if (spec.basePack != null && parts.TryFind(spec.basePack, region, 1, out part)) outfit.Add(part.id);
                else Debug.LogWarning($"[Characters] {spec.name}: no variant 01 part for {region}.");
            }
            lineage.basicOutfit = outfit.ToArray();
            lineage.bareBody = BareBodyOf(spec, parts);

            var skinTones = BuildSkinTones(spec, skinPresets, presetColors, propertyById);
            lineage.masculine = BuildSetup(spec, BodyType.Masculine, lineage, parts, premades, builds, skinTones, propertyByName);
            lineage.feminine = BuildSetup(spec, BodyType.Feminine, lineage, parts, premades, builds, skinTones, propertyByName);
            EditorUtility.SetDirty(lineage);
            return lineage;
        }

        /// <summary>The body under the gear: the species' base body, or the variant the spec names where there is none.</summary>
        private static ushort[] BareBodyOf(LineageSpec spec, SidekickPartCatalog parts)
        {
            string pack = spec.barePack ?? spec.basePack;
            var body = new List<ushort>();
            if (pack == null) return body.ToArray();
            for (var region = BodyRegion.Torso; region <= BodyRegion.FootRight; region++)
            {
                if (parts.TryFind(pack, region, spec.bareVariant, out var part)) body.Add(part.id);
                else Debug.LogWarning($"[Characters] {spec.name}: no bare body part for {region} ({pack} variant {spec.bareVariant}).");
            }
            return body.ToArray();
        }

        /// <summary>
        /// Writes only the bare body of each lineage, leaving everything hand-tuned on the assets alone.
        /// </summary>
        [MenuItem("Krytical Nova/Characters/Bake Bare Bodies Only")]
        public static void BakeBareBodies()
        {
            var parts = AssetDatabase.LoadAssetAtPath<SidekickPartCatalog>(SidekickPartCatalogBaker.CatalogPath);
            if (parts == null) { Debug.LogError("[Characters] No part catalog: bake it first."); return; }
            foreach (var spec in Specs)
            {
                var lineage = AssetDatabase.LoadAssetAtPath<LineageDefinition>($"{LineageFolder}/Lineage_{spec.asset}.asset");
                if (lineage == null) continue;
                lineage.bareBody = BareBodyOf(spec, parts);
                EditorUtility.SetDirty(lineage);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Characters] Baked the bare body of every lineage.");
        }

        private static LineageDefinition.Setup BuildSetup(LineageSpec spec, BodyType bodyType, LineageDefinition lineage,
            SidekickPartCatalog parts, List<Premade> premades, Dictionary<BodyType, List<LineageDefinition.BuildPreset>> builds,
            List<LineageDefinition.SkinTone> skinTones, Dictionary<string, PropertyRow> propertyByName)
        {
            var setup = new LineageDefinition.Setup
            {
                builds = builds[bodyType],
                skinTones = skinTones,
                channels = spec.channels
            };

            foreach (var region in HeadRegions)
            {
                if (region == BodyRegion.FacialHair && bodyType == BodyType.Feminine) continue;
                bool ownOnly = Array.IndexOf(OwnSpeciesOnly, region) >= 0;
                var options = parts.Parts
                    .Where(p => p.region == region && Array.IndexOf(spec.headPacks, p.pack) >= 0)
                    .Where(p => p.species == spec.species || (!ownOnly && p.species == "HU"))
                    .OrderBy(p => Array.IndexOf(spec.headPacks, p.pack)).ThenBy(p => p.variant)
                    .Select(p => p.id).ToArray();
                if (options.Length == 0) continue;
                setup.regions.Add(new LineageDefinition.RegionOptions
                {
                    region = region, optional = Array.IndexOf(OptionalRegions, region) >= 0, partIds = options
                });
            }

            // Ready-made faces: this body type's premades first, then the others adapted to it.
            int index = 1;
            foreach (var premade in premades.OrderBy(p => p.bodyType == bodyType ? 0 : 1))
            {
                var recipe = new CharacterRecipe
                {
                    version = CharacterRecipe.CurrentVersion,
                    faction = spec.faction,
                    lineage = spec.id,
                    bodyType = bodyType,
                    // A ready-made face changes the head only; the body starts neutral and is the player's to shape.
                    build = 0,
                    muscle = 0,
                    height = CharacterRecipe.DefaultHeight,
                    palette = lineage.defaultPalette,
                    skinTone = NearestSkinTone(skinTones, premade, propertyByName),
                    parts = HeadParts(setup, parts, premade, index),
                    colors = PremadeColors(spec, premade, propertyByName)
                };
                setup.presets.Add(new LineageDefinition.Preset { name = $"{spec.name} {index++:00}", recipe = recipe });
            }
            if (setup.presets.Count == 0)
                setup.presets.Add(new LineageDefinition.Preset
                {
                    name = spec.name + " 01",
                    recipe = new CharacterRecipe
                    {
                        version = CharacterRecipe.CurrentVersion, faction = spec.faction, lineage = spec.id, bodyType = bodyType,
                        height = CharacterRecipe.DefaultHeight, palette = lineage.defaultPalette, parts = HeadParts(setup, parts, null, 0), colors = Array.Empty<ColorOverride>()
                    }
                });
            return setup;
        }

        /// <summary>
        /// The premade's head parts this setup allows, with every required region filled from its first option.
        /// Premades that hid their hair under a helmet (which we strip) get hair and brows, so no default is bald.
        /// </summary>
        private static ushort[] HeadParts(LineageDefinition.Setup setup, SidekickPartCatalog parts, Premade premade, int variety)
        {
            var chosen = new Dictionary<BodyRegion, ushort>();
            if (premade != null)
                foreach (var partName in premade.parts)
                {
                    if (!parts.TryGetByName(partName, out var entry)) continue;
                    if (setup.Allows(entry.region, entry.id)) chosen[entry.region] = entry.id;
                }
            foreach (var options in setup.regions)
            {
                if (chosen.ContainsKey(options.region)) continue;
                if (!options.optional) chosen[options.region] = options.partIds[0];
                else if (options.region == BodyRegion.Hair) chosen[options.region] = options.partIds[variety * 3 % options.partIds.Length];
                else if (options.region == BodyRegion.EyebrowLeft || options.region == BodyRegion.EyebrowRight)
                    chosen[options.region] = options.partIds[0];
            }
            setup.InvalidateCache();
            return chosen.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToArray();
        }

        private static ColorOverride[] PremadeColors(LineageSpec spec, Premade premade, Dictionary<string, PropertyRow> propertyByName)
        {
            var list = new List<ColorOverride>();
            void Add(byte channel, string property)
            {
                if (Array.IndexOf(spec.channels, channel) < 0) return;
                if (propertyByName.TryGetValue(property, out var row) && premade.colors.TryGetValue(row.Id, out var color))
                    list.Add(new ColorOverride(channel, color));
            }
            Add(ChannelHair, "Hair 01");
            Add(ChannelEyes, "Eye Color Left");
            Add(ChannelGlow, "Glow 01");
            Add(ChannelBone, "Bone 01");
            Add(ChannelMetal, "Metal 01");
            return list.ToArray();
        }

        private static byte NearestSkinTone(List<LineageDefinition.SkinTone> tones, Premade premade, Dictionary<string, PropertyRow> propertyByName)
        {
            if (tones.Count == 0 || !propertyByName.TryGetValue("Skin 01", out var skin)
                || !premade.colors.TryGetValue(skin.Id, out var color)) return 0;
            int best = 0;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < tones.Count; i++)
            {
                var s = tones[i].swatch;
                int d = (s.r - color.r) * (s.r - color.r) + (s.g - color.g) * (s.g - color.g) + (s.b - color.b) * (s.b - color.b);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return (byte)(best + 1);
        }

        private static List<LineageDefinition.SkinTone> BuildSkinTones(LineageSpec spec, List<PresetRow> presets, List<ColorRow> rows,
            Dictionary<int, PropertyRow> propertyById)
        {
            var tones = new List<LineageDefinition.SkinTone>();
            if (spec.skinSpecies == null) return tones;
            int index = 1;
            foreach (var preset in presets.Where(p => p.Species == spec.skinSpecies))
            {
                var cells = new List<LineageDefinition.CellColor>();
                Color32 swatch = new Color32(255, 255, 255, 255);
                foreach (var row in rows.Where(r => r.Owner == preset.Id))
                {
                    if (!propertyById.TryGetValue(row.Prop, out var property) || !IsSkinProperty(property.Name)) continue;
                    var color = ParseHex(row.Hex);
                    cells.Add(new LineageDefinition.CellColor { u = (byte)property.U, v = (byte)property.V, color = color });
                    if (property.Name == "Skin 01") swatch = color;
                }
                if (cells.Count == 0) continue;
                string label = preset.Name.StartsWith("Human - ", StringComparison.Ordinal) ? preset.Name.Substring(8) : $"Tone {index:00}";
                tones.Add(new LineageDefinition.SkinTone { name = label, swatch = swatch, cells = cells.ToArray() });
                index++;
            }
            return tones;
        }

        // A skin tone recolours the skin and what is made of it; hair, eyes and teeth keep their own colours.
        private static bool IsSkinProperty(string name) =>
            name.StartsWith("Skin", StringComparison.Ordinal) || name == "Scar" || name == "Lips" || name == "Nose" || name == "Mouth"
            || name.StartsWith("Ear ", StringComparison.Ordinal) || name.StartsWith("Eyelids", StringComparison.Ordinal)
            || name.StartsWith("Fingernails", StringComparison.Ordinal) || name.StartsWith("Toenails", StringComparison.Ordinal)
            || name.EndsWith("Stubble", StringComparison.Ordinal);

        private static Dictionary<BodyType, List<LineageDefinition.BuildPreset>> BuildBodyPresets(List<BodyRow> rows)
        {
            var result = new Dictionary<BodyType, List<LineageDefinition.BuildPreset>>
            {
                { BodyType.Masculine, new List<LineageDefinition.BuildPreset>() },
                { BodyType.Feminine, new List<LineageDefinition.BuildPreset>() }
            };
            foreach (var row in rows.OrderBy(r => r.BodySize).ThenBy(r => r.Muscle))
            {
                int dash = row.Name.IndexOf(" - ", StringComparison.Ordinal);
                result[row.BodyType > 0 ? BodyType.Feminine : BodyType.Masculine].Add(new LineageDefinition.BuildPreset
                {
                    name = dash >= 0 ? row.Name.Substring(dash + 3) : row.Name,
                    build = ToSByte(row.BodySize),
                    muscle = ToSByte(row.Muscle)
                });
            }
            return result;
        }

        // ---------------------------------------------------------------- colours

        private static List<CharacterCreationDatabase.ColorChannel> BuildChannels(Dictionary<string, PropertyRow> propertyByName)
        {
            CharacterCreationDatabase.ColorTarget Target(string property, float shade)
            {
                if (!propertyByName.TryGetValue(property, out var row))
                {
                    Debug.LogWarning($"[Characters] Colour property '{property}' not found in Sidekick's database.");
                    return default;
                }
                return new CharacterCreationDatabase.ColorTarget { u = (byte)row.U, v = (byte)row.V, shade = shade };
            }

            return new List<CharacterCreationDatabase.ColorChannel>
            {
                new CharacterCreationDatabase.ColorChannel
                {
                    id = ChannelHair, displayName = "Hair",
                    targets = new[]
                    {
                        Target("Hair 01", 1f), Target("Hair 02", 1f), Target("Eyebrow Left", 1f), Target("Eyebrow Right", 1f),
                        Target("Facial Hair 01", 1f), Target("Facial Hair 02", 1f)
                    },
                    swatches = Swatches("1C1A1A", "3B2A20", "50392F", "6E4630", "8A4B32", "B55A30", "C9A46A", "E3D7B8", "8E8E94",
                        "E8E8EC", "3FC6D8", "D04FA8", "7A4FE0", "4FA36A", "C0303A", "3A66D0")
                },
                new CharacterCreationDatabase.ColorChannel
                {
                    id = ChannelEyes, displayName = "Eyes",
                    targets = new[]
                    {
                        Target("Eye Color Left", 1f), Target("Eye Color Right", 1f),
                        Target("Eye Edge Left", 0.75f), Target("Eye Edge Right", 0.75f)
                    },
                    swatches = Swatches("6B4A27", "8F6B46", "C08A2E", "4E8F5A", "2F9AA0", "3F7FD0", "8C98A8", "8A5FD8", "C83A3A", "E8B04A")
                },
                new CharacterCreationDatabase.ColorChannel
                {
                    id = ChannelGlow, displayName = "Glow",
                    targets = new[] { Target("Glow 01", 1f), Target("Glow 02", 1f) },
                    swatches = Swatches("4FE3F0", "FF3B30", "FF8A1E", "FFD23C", "5CFF7A", "3F8CFF", "A064FF", "FF4FC8", "FFFFFF")
                },
                new CharacterCreationDatabase.ColorChannel
                {
                    id = ChannelBone, displayName = "Bone",
                    targets = new[] { Target("Bone 01", 1f), Target("Bone 02", 0.85f) },
                    swatches = Swatches("E6DEC8", "D2C4A0", "A9A9A0", "4A4640", "8A9A70", "9A5A50", "9AA8B8")
                },
                new CharacterCreationDatabase.ColorChannel
                {
                    id = ChannelMetal, displayName = "Plating",
                    targets = new[] { Target("Metal 01", 1f), Target("Metal 02", 0.8f), Target("Metal 03", 0.62f), Target("Metal 04", 0.45f) },
                    swatches = Swatches("9AA4AE", "4A525C", "25282C", "DADFE4", "B08A3C", "B0603A", "8A2A2E", "2E4468", "5A6238", "5A3E8A")
                },
            };
        }

        private static Color32[] Swatches(params string[] hex) => hex.Select(ParseHex).ToArray();

        /// <summary>The 16x16 colour grid: Sidekick's default colours with the premade's own on top.</summary>
        private static Color32[] BuildCells(Dictionary<int, PropertyRow> propertyById, List<ColorRow> defaults, Dictionary<int, Color32> colors)
        {
            var cells = new Color32[CharacterPaletteLibrary.CellCount];
            for (int i = 0; i < cells.Length; i++) cells[i] = new Color32(255, 255, 255, 255);
            void Set(int propertyId, Color32 color)
            {
                if (!propertyById.TryGetValue(propertyId, out var p)) return;
                if (p.U < 0 || p.V < 0 || p.U >= CharacterPaletteLibrary.Grid || p.V >= CharacterPaletteLibrary.Grid) return;
                cells[p.V * CharacterPaletteLibrary.Grid + p.U] = color;
            }
            foreach (var row in defaults) Set(row.Prop, ParseHex(row.Hex));
            foreach (var kv in colors) Set(kv.Key, kv.Value);
            return cells;
        }

        // ---------------------------------------------------------------- premades

        private static readonly Regex ColorRowPattern = new Regex(@"- ColorProperty:\s*(\d+)\s*\r?\n\s*MainColor:\s*([0-9A-Fa-f]{6})");

        private static List<Premade> ReadPremades(LineageSpec spec)
        {
            var list = new List<Premade>();
            foreach (var folder in spec.premades)
            {
                string root = $"{PremadeRoot}/{folder}";
                if (!Directory.Exists(root)) continue;
                foreach (var path in Directory.GetFiles(root, "*.sk", SearchOption.AllDirectories)
                             .Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var partNames = PlayerAppearanceSetup.ReadRecipe(path, out var shape);
                    var colors = new Dictionary<int, Color32>();
                    foreach (Match m in ColorRowPattern.Matches(File.ReadAllText(path)))
                        colors[int.Parse(m.Groups[1].Value)] = ParseHex(m.Groups[2].Value);
                    list.Add(new Premade
                    {
                        name = Path.GetFileNameWithoutExtension(path),
                        parts = partNames,
                        bodyType = shape.masculineFeminine > 50f ? BodyType.Feminine : BodyType.Masculine,
                        build = ToSByte(shape.heavy > 0f ? shape.heavy : -shape.skinny),
                        muscle = ToSByte(shape.buff * 2f - 100f),
                        colors = colors
                    });
                }
            }
            return list;
        }

        // ---------------------------------------------------------------- attachment joints

        /// <summary>The attachment joints' untouched local poses, read from Sidekick's base model.</summary>
        private static List<CharacterCreationDatabase.JointRest> ReadJointRests()
        {
            var rests = new List<CharacterCreationDatabase.JointRest>();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(BaseModelPath);
            if (model == null)
            {
                Debug.LogWarning($"[Characters] {BaseModelPath} not found; attachment joints will not follow the body shape.");
                return rests;
            }
            var transforms = model.GetComponentsInChildren<Transform>(true);
            foreach (var joint in JointOfRegion.Values)
            {
                var t = transforms.FirstOrDefault(x => x.name == joint);
                if (t == null) { Debug.LogWarning($"[Characters] Joint '{joint}' not found on the base model."); continue; }
                rests.Add(new CharacterCreationDatabase.JointRest { joint = joint, localPosition = t.localPosition, localRotation = t.localRotation });
            }
            return rests;
        }

        private static List<CharacterCreationDatabase.JointOffset> BuildJointOffsets(List<RigRow> rows)
        {
            var offsets = new List<CharacterCreationDatabase.JointOffset>();
            foreach (var row in rows)
            {
                if (!JointOfRegion.TryGetValue(row.PartType, out var joint) || row.Blend < 0 || row.Blend > 3) continue;
                offsets.Add(new CharacterCreationDatabase.JointOffset
                {
                    joint = joint,
                    blend = (CharacterCreationDatabase.BodyBlend)row.Blend,
                    maxOffset = new Vector3((float)row.Ox, (float)row.Oy, (float)row.Oz),
                    maxRotationEuler = new Vector3((float)row.Rx, (float)row.Ry, (float)row.Rz)
                });
            }
            return offsets;
        }

        // ---------------------------------------------------------------- helpers

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Color32 ParseHex(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex.Trim().TrimStart('#'), out var color))
                return color;
            return new Color32(255, 255, 255, 255);
        }

        private static sbyte ToSByte(double value) => (sbyte)Mathf.Clamp(Mathf.RoundToInt((float)value), -100, 100);
    }
}
