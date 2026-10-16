using System.Text;
using KryticalNova.World;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for moving between maps until portals lead somewhere: gm.zones lists every map in
    /// the zone catalog, gm.travel takes the session (everyone in it) or the offline player there, behind the travel
    /// screen. Host / offline only, like every GM command.
    /// </summary>
    public static class TravelCommands
    {
        [Command("gm.zones", "List the maps you can travel to (scene, name, level band, PvP rule).")]
        private static string Zones()
        {
            ZoneCatalog catalog = ZoneRules.Current != null ? ZoneRules.Current.Catalog : null;
            if (catalog == null) return "This scene has no zone catalog (no ZoneRules).";
            string here = ZoneRules.Current.gameObject.scene.name;
            var sb = new StringBuilder();
            foreach (ZoneEntry zone in catalog.zones)
            {
                if (zone == null) continue;
                sb.Append(zone.sceneName == here ? "> " : "  ")
                  .Append(zone.sceneName.PadRight(36))
                  .Append(zone.displayName.PadRight(22))
                  .Append(zone.LevelBand.PadRight(14))
                  .AppendLine(Rule(zone.pvp));
            }
            return sb.Append("gm.travel <scene or name> goes there (partial names work).").ToString();
        }

        [Command("gm.travel", "Travel to another map: gm.travel <scene or name>, e.g. gm.travel crestfall. In a session the host takes everyone.")]
        private static string Travel(string zone)
        {
            ZoneTravel.TryTravel(zone, out string message);
            return message;
        }

        private static string Rule(ZonePvpRule rule) => rule switch
        {
            ZonePvpRule.Neutral => "neutral ground",
            ZonePvpRule.AlwaysOn => "always-on PvP",
            _ => "faction war",
        };
    }
}
