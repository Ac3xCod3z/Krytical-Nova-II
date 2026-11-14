using KryticalNova.Townsfolk;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// GM commands for town life on Sky Concord (owner's DAY list item 18; plan 3.9). The crowd's numbers work where the server runs (the
    /// server, the host, the Editor's Play); the labels on the client that types them. The GM window's World tab has the same controls;
    /// the time of day is the existing <c>time.*</c> commands (the townsfolk follow the world's clock).
    /// </summary>
    public static class TownsfolkCommands
    {
        const string NoServer = "No town life here: run this on the server (or the host / the Editor's Play), with Resources/TownsfolkWorldAssets built.";

        [Command("gm.town", "Town life: who is out, the hour and the spots.")]
        static string Status()
        {
            TownsfolkServerSystem town = TownsfolkServerSystem.Find();
            return town == null ? NoServer : town.Status();
        }

        [Command("gm.town.debug", "Labels over every townsperson you see: personality, activity, chat circle, look (on/off).")]
        static string Debug(bool on)
        {
            TownsfolkViewClientSystem.DebugLabels = on;
            return "Townsfolk labels " + (on ? "on." : "off.");
        }

        [Command("gm.town.cap", "The crowd's cap by day and at night (-1 = the area's own, 48 / 20).")]
        static string Cap(int day, int night)
        {
            TownsfolkServerSystem town = TownsfolkServerSystem.Find();
            if (town == null) return NoServer;
            town.DayCapOverride = day;
            town.NightCapOverride = night;
            return $"Town cap: day {(day < 0 ? "area's own" : day.ToString())}, night {(night < 0 ? "area's own" : night.ToString())}.";
        }

        [Command("gm.town.clear", "Everyone removed at once (and extra spawns forgotten); the crowd comes out again by the hour's numbers.")]
        static string Clear()
        {
            TownsfolkServerSystem town = TownsfolkServerSystem.Find();
            return town == null ? NoServer : $"Removed {town.Clear()} townsfolk.";
        }

        [Command("gm.town.spawn", "Adds townsfolk of a personality (1 Busy Worker, 2 Stroller, 3 Jogger, 4 Gossip, 5 Shopper, 6 Loner, 7 Nosy Local, 8 Night Owl, 9 Street Performer, 10 Guard) on top of the hour's number, until gm.town.clear.")]
        static string Spawn(byte personality, int count)
        {
            TownsfolkServerSystem town = TownsfolkServerSystem.Find();
            if (town == null) return NoServer;
            return town.AddExtra(personality, count) ? $"{count} more of personality {personality} come out (a few a second)." : "No such personality in this area's mix.";
        }
    }
}
