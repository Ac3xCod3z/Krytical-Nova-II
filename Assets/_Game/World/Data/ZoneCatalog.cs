using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>What a map is: the starter town, a faction's Home City, a field zone, a Run (dungeon) or The Scar.</summary>
    public enum ZoneKind { StarterTown, HomeCity, FieldZone, Run, Scar }

    /// <summary>
    /// The PvP rule a map is tagged with. <see cref="Neutral"/>: the factions are at peace on the map's neutral ground
    /// (its TradeZone marked so; only the starter town has one). <see cref="FactionWar"/>: the usual rule, other
    /// factions are hostile while cross-faction PvP is on (gm.pvp). <see cref="AlwaysOn"/>: other factions are hostile
    /// whatever the switch says (The Scar).
    /// </summary>
    public enum ZonePvpRule { Neutral, FactionWar, AlwaysOn }

    /// <summary>A region of the World Atlas: its name, whose land it is, its concept painting and a few lines of lore.</summary>
    [Serializable]
    public sealed class ZoneRegion
    {
        public string name;
        public string faction;
        public Texture2D art;
        [TextArea(2, 6)] public string lore;
    }

    /// <summary>One map that can be travelled to: its scene, its name for players, its region, kind, level band and rule.</summary>
    [Serializable]
    public sealed class ZoneEntry
    {
        [Tooltip("The scene's file name (it must be in the build list).")]
        public string sceneName;
        public string displayName;
        [Tooltip("The region's name as in Regions; empty for the starter town.")]
        public string region;
        public ZoneKind kind;
        [Min(0)] public int minLevel;
        [Min(0)] public int maxLevel;
        public ZonePvpRule pvp = ZonePvpRule.FactionWar;
        [TextArea(2, 6)] public string lore;

        public string LevelBand => maxLevel <= 0 ? "All levels" : $"Lv {minLevel} – {maxLevel}";
    }

    /// <summary>One picture and its lore on the travel screen.</summary>
    public readonly struct TravelPage
    {
        public readonly Texture2D Art;
        public readonly string Title;
        public readonly string Lore;

        public TravelPage(Texture2D art, string title, string lore)
        {
            Art = art;
            Title = title;
            Lore = lore;
        }
    }

    /// <summary>
    /// Every map players can travel to, and the regions they lie in. Built by Krytical Nova ▸ World ▸ Build World
    /// Scenes from the World Atlas (Design/World-Atlas.md); each scene's <see cref="ZoneRules"/> points at it.
    /// The travel screen shows a map's pages: its region's painting with the map's lore first, then the region's own
    /// page, then the other regions. Hidden regions (the Ronin's) are never listed here, and no picture that shows
    /// them (the World Atlas's spider map names every Ronin map) is used.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/World/Zone Catalog", fileName = "ZoneCatalog")]
    public sealed class ZoneCatalog : ScriptableObject
    {
        public List<ZoneRegion> regions = new List<ZoneRegion>();
        public List<ZoneEntry> zones = new List<ZoneEntry>();

        public ZoneEntry Find(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return null;
            foreach (var zone in zones)
                if (zone != null && string.Equals(zone.sceneName, sceneName, StringComparison.OrdinalIgnoreCase)) return zone;
            return null;
        }

        /// <summary>A map by scene name or by its player-facing name; a part of either works when only one map matches.</summary>
        public ZoneEntry Resolve(string query, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(query)) { error = "Name a map (gm.zones lists them)."; return null; }
            string q = Squash(query);
            ZoneEntry partial = null;
            int partials = 0;
            foreach (var zone in zones)
            {
                if (zone == null) continue;
                string scene = Squash(zone.sceneName), display = Squash(zone.displayName);
                if (scene == q || display == q) return zone;
                if (scene.Contains(q) || display.Contains(q)) { partial = zone; partials++; }
            }
            if (partials == 1) return partial;
            error = partials == 0 ? $"No map called '{query}' (gm.zones lists them)." : $"'{query}' matches {partials} maps: be more exact.";
            return null;
        }

        public ZoneRegion Region(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var region in regions)
                if (region != null && string.Equals(region.name, name, StringComparison.OrdinalIgnoreCase)) return region;
            return null;
        }

        /// <summary>The travel screen's pages for a trip to <paramref name="zone"/>, its own first.</summary>
        public List<TravelPage> BuildDeck(ZoneEntry zone)
        {
            var deck = new List<TravelPage>();
            ZoneRegion home = zone != null ? Region(zone.region) : null;
            // A map outside every region (the starter town) opens on the first region's painting.
            Texture2D first = home != null ? home.art : regions.Count > 0 && regions[0] != null ? regions[0].art : null;
            if (zone != null)
                deck.Add(new TravelPage(first, zone.displayName, zone.lore));
            if (home != null && home.art != null)
                deck.Add(new TravelPage(home.art, home.name, home.lore));
            foreach (var region in regions)
                if (region != null && region != home && region.art != null)
                    deck.Add(new TravelPage(region.art, region.name, region.lore));
            return deck;
        }

        private static string Squash(string s)
        {
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            return sb.ToString();
        }
    }
}
