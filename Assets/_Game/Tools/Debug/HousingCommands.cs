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
            return housing.Enter() ?? $"At the Hall sandbox. {housing.Authority.Home.lawn.Count} piece(s) stand on its lawns.";
        }

        [Command("gm.home.leave", "Return the targeted player from their Hall sandbox to where they were.")]
        private static string HomeLeave()
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            if (!housing.InHome) return "Not at the Hall.";
            return housing.Leave() ?? "Back in town.";
        }

        [Command("gm.furnish.kit", "Put n of every furnishing of the targeted player's own faction into their Furnishing Crate (5 unless told).")]
        private static string FurnishKit(int count = 5)
        {
            if (!TryGetHousing(out var housing, out var error)) return error;
            count = Mathf.Clamp(count, 1, 999);
            var own = new List<FurnishingDefinition>();
            housing.Catalog.ForFaction(housing.Faction, own);
            if (own.Count == 0) return $"The {housing.Faction} have no furnishings in the catalog.";
            foreach (FurnishingDefinition furnishing in own) housing.Crate.ServerAdd(furnishing.itemId, count);
            return $"{count} of each of {own.Count} {housing.Faction} furnishings are in the Furnishing Crate.";
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
            own.RemoveAll(f => f.place == FurnishingPlace.IndoorOnly || f.mount == FurnishingMount.Wall || f.mount == FurnishingMount.Ceiling);
            if (own.Count == 0) return $"The {housing.Faction} have no furnishings for a lawn.";

            AreaShape shape = housing.Authority.Shape(HomeArea.Lawn);
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

                int before = housing.Authority.Home.lawn.Count;
                ops.Clear();
                ops.Add(HomeOp.Place(piece));
                housing.Authority.Submit(HomeArea.Lawn, ops);
                if (housing.Authority.Home.lawn.Count > before) placed++;
                else if (housing.Authority.BudgetUsed(HomeArea.Lawn) >= housing.Authority.Budget(HomeArea.Lawn)) break;
            }
            return $"{placed} piece(s) set down. The lawns hold {housing.Authority.Home.lawn.Count}, budget {housing.Authority.BudgetUsed(HomeArea.Lawn)} / {housing.Authority.Budget(HomeArea.Lawn)}.";
        }
    }
}
