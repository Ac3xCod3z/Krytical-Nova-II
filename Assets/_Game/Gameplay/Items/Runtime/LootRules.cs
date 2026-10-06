using System;
using KryticalNova.Factions;

namespace KryticalNova.Items
{
    /// <summary>
    /// Who may pick up what lies on the ground, free of networking so it can be tested. A drop may be held for one
    /// player for a moment (the one who dropped it, later the one who made the kill); after that it is open to
    /// that player's faction, or to everyone when no player dropped it.
    /// </summary>
    public static class LootRules
    {
        public const string ReasonTooFar = "Move closer to pick that up.";
        public const string ReasonReserved = "That is someone else's for a moment.";
        public const string ReasonOtherFaction = "You can't take what another faction dropped.";
        public const string ReasonGone = "It is gone.";
        public const string ReasonDefeated = "You can't pick things up while defeated.";

        /// <summary>Why this player may not take a drop (shown to them), or null when they may.</summary>
        /// <param name="reservedForAnother">The drop is still held for a different player.</param>
        /// <param name="dropFaction">The faction of the player who dropped it; None when no player did.</param>
        /// <param name="crossFaction">True where factions may hand items to each other (the future neutral zone).</param>
        public static string PickupBlockedReason(float distance, float range, bool reservedForAnother,
            FactionId dropFaction, FactionId pickerFaction, bool crossFaction = false)
        {
            if (distance > range) return ReasonTooFar;
            if (reservedForAnother) return ReasonReserved;
            if (!crossFaction && dropFaction != FactionId.None && dropFaction != pickerFaction) return ReasonOtherFaction;
            return null;
        }

        // The golden angle: successive points on the spiral never line up, so they fill the ground evenly.
        private const double GoldenAngle = 2.39996322972865332;

        /// <summary>
        /// Where the n-th thing dropped on one spot goes, as an offset from that spot: the first lies on it, the
        /// rest wind outwards on a spiral, never closer to each other than <paramref name="spacing"/>.
        /// </summary>
        public static void ScatterOffset(int attempt, float spacing, out float x, out float z)
        {
            if (attempt <= 0)
            {
                x = 0f;
                z = 0f;
                return;
            }
            double radius = spacing * Math.Sqrt(attempt);
            double angle = attempt * GoldenAngle;
            x = (float)(radius * Math.Cos(angle));
            z = (float)(radius * Math.Sin(angle));
        }

        /// <summary>"HP Stim x5", or just the name for a single item.</summary>
        public static string Label(ItemDefinition item, int count)
        {
            if (item == null) return string.Empty;
            return count > 1 ? $"{item.displayName} x{count}" : item.displayName;
        }
    }
}
