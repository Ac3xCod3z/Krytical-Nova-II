using System.Collections.Generic;
using KryticalNova.Housing;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for Crew Halls. Like the other GM commands they only run where the game is decided
    /// (host, server or offline) and act on the player picked by <see cref="GmTarget"/>. Until furnishings are
    /// sold and dropped, <c>gm.furnish.kit</c> is how a Furnishing Crate is filled.
    /// </summary>
    public static class HousingCommands
    {
        private static bool TryGetHousing(out PlayerHousing housing, out string error)
        {
            housing = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            housing = player.GetComponent<PlayerHousing>();
            if (housing == null || housing.Settings == null || housing.Catalog == null)
            {
                error = "That player has no housing (run Krytical Nova/Housing/1 and 2, then Krytical Nova/Setup/Player Prefab).";
                return false;
            }
            return true;
        }

        [Command("gm.home", "Take the targeted player to their Hall sandbox: a Lot with a Hall on it, far from town. Open the Furnishing Crate there to arrange it.")]
        private static string Home()
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            if (housing.InHome) return "Already at the Hall. gm.home.leave returns to town.";
            return housing.Enter() ?? "On the way to the Hall sandbox.";
        }

        [Command("gm.home.inside", "Take the targeted player through their Hall's door into the room inside. They must be at the Hall.")]
        private static string HomeInside()
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            return housing.GoInside() ?? "On the way in.";
        }

        [Command("gm.home.floor", "Take the targeted player to a floor inside their Hall: ground, up or basement. They must be inside.")]
        private static string HomeFloor(string floor = "ground")
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            HomeArea to = floor.StartsWith("u") ? HomeArea.Upper : floor.StartsWith("b") ? HomeArea.Basement : HomeArea.Interior;
            return housing.GoToFloor(to) ?? $"On the way to the {HomeAreas.Name(to)}.";
        }

        [Command("gm.home.outside", "Take the targeted player out of their Hall's room to its doorstep.")]
        private static string HomeOutside()
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            return housing.GoOutside() ?? "On the way out.";
        }

        [Command("gm.home.leave", "Return the targeted player from their Hall sandbox to where they were.")]
        private static string HomeLeave()
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            if (!housing.InHome) return "Not at the Hall.";
            return housing.Leave() ?? "On the way back to town.";
        }

        [Command("gm.furnish.kit", "Put n of every furnishing in the targeted player's own faction's style into their Furnishing Crate (5 unless told), with one of each of its Facades and Finishes. Add 'all' for the furnishings and Finishes of every style (Facades stay the faction's own).")]
        private static string FurnishKit(int count = 5, string which = "own")
        {
            bool every = which.StartsWith("a");
            if (!TryGetHousing(out var housing, out var error)) return error;
            count = Mathf.Clamp(count, 1, 999);
            var own = new List<FurnishingDefinition>();
            if (every) own.AddRange(housing.Catalog.Furnishings);
            else housing.Catalog.ForFaction(housing.Faction, own);
            if (own.Count == 0) return $"The {housing.Faction} have no furnishings in the catalog.";
            foreach (FurnishingDefinition furnishing in own) housing.Crate.ServerAdd(furnishing.itemId, count);
            int looks = 0;
            foreach (HallLookDefinition look in housing.Catalog.Looks)
            {
                bool theirs = look != null && (look.faction == housing.Faction || (every && look.IsFinish));
                if (!theirs || look.standard || housing.Crate.Count(look.itemId) > 0) continue;
                // One is all a Hall can wear; the one it wears is not handed out again.
                HomeSave home = housing.Authority != null ? housing.Authority.Home : null;
                if (home != null && home.Look(look.slot) == look.itemId) continue;
                housing.Crate.ServerAdd(look.itemId, 1);
                looks++;
            }
            return $"{count} of each of {own.Count} furnishings ({(every ? "every style" : housing.Faction + " style")}) are in the Furnishing Crate, and {looks} Facade(s) and Finish(es).";
        }

        [Command("gm.furnish.clear", "Empty the targeted player's Furnishing Crate.")]
        private static string FurnishClear()
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            housing.Crate.ServerClear();
            return "The Furnishing Crate is empty.";
        }

        [Command("gm.home.reset", "Put everything that stands at the targeted player's Hall back into their Furnishing Crate. They must be at the Hall.")]
        private static string HomeReset()
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            if (housing.LocalAuthority == null) return "Not at the Hall.";
            return $"{housing.LocalAuthority.PutEverythingAway()} piece(s) put away.";
        }

        [Command("gm.home.fill", "Set down n furnishings on the lawns of the targeted player's Hall, taken from the Crate and filled first as needed (a load test). They must be at the Hall.")]
        private static string HomeFill(int count = 100)
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            if (housing.Authority == null) return "Not at the Hall.";
            var own = new List<FurnishingDefinition>();
            housing.Catalog.ForFaction(housing.Faction, own);
            bool indoors = HomeAreas.IsInside(housing.Area);
            own.RemoveAll(f => f.place == (indoors ? FurnishingPlace.LawnOnly : FurnishingPlace.IndoorOnly) || f.mount == FurnishingMount.Wall || f.mount == FurnishingMount.Ceiling);
            if (own.Count == 0) return $"The {housing.Faction} have no furnishings for that part of a Hall.";

            AreaShape shape = housing.Authority.Shape(housing.Area);
            var random = new System.Random(12345);
            var ops = new List<HomeOp>(1);
            int placed = 0;
            for (int n = 0; n < count * 4 && placed < count; n++)
            {
                FurnishingDefinition furnishing = own[random.Next(own.Count)];
                if (housing.Crate.Count(furnishing.itemId) < 1) housing.Crate.ServerAdd(furnishing.itemId, 50);
                var piece = new PlacedPiece { i = furnishing.itemId, s = PlacedPiece.NormalScale };
                piece.Position = new Vector3(
                    Mathf.Lerp(-shape.halfSize.x, shape.halfSize.x, (float)random.NextDouble()),
                    -furnishing.Bottom,
                    Mathf.Lerp(-shape.halfSize.y, shape.halfSize.y, (float)random.NextDouble()));
                piece.Yaw = random.Next(24) * 15f;
                if (shape.IsBlocked(piece.Position)) continue;

                int before = housing.Authority.Home.Area(housing.Area).Count;
                ops.Clear();
                ops.Add(HomeOp.Place(piece));
                housing.Authority.Submit(housing.Area, ops);
                if (housing.Authority.Home.Area(housing.Area).Count > before) placed++;
                else if (housing.Authority.BudgetUsed(housing.Area) >= housing.Authority.Budget(housing.Area)) break;
            }
            return $"{placed} piece(s) set down. That part of the Hall holds {housing.Authority.Home.Area(housing.Area).Count}, budget {housing.Authority.BudgetUsed(housing.Area)} / {housing.Authority.Budget(housing.Area)}.";
        }
    }
}
