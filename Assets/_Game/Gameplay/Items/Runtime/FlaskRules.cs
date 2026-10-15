using System;

namespace KryticalNova.Items
{
    /// <summary>
    /// The rules of the Flux Flask, kept apart so they can be tested. A Flask carries the drinks it has used in its
    /// stack's data: 0 is a full Flask, which is what a new one (bought, dropped, given) always is.
    /// </summary>
    public static class FlaskRules
    {
        public const string ReasonEmpty = "Your Flask is empty. An Apothecary refills it.";
        public const string ReasonFlowing = "Your Flask is still flowing.";
        public const string ReasonFull = "Your Flux is full.";
        public const string ReasonNoneToRefill = "Your Flasks are full.";
        public const string ReasonNoApothecary = "Only an Apothecary refills a Flask.";

        /// <summary>Drinks used, from a stack's data, never more than the Flask holds.</summary>
        public static int Used(ulong data, int charges) => (int)Math.Min(data & 0xFF, (ulong)Math.Max(0, charges));

        /// <summary>Drinks left in a Flask.</summary>
        public static int Left(ulong data, int charges) => Math.Max(0, charges - Used(data, charges));

        public static bool HasDrink(ulong data, int charges) => Left(data, charges) > 0;

        /// <summary>The data after one drink.</summary>
        public static ulong AfterDrink(ulong data, int charges) => (data & ~0xFFUL) | (ulong)Math.Min(charges, Used(data, charges) + 1);

        /// <summary>The data of a refilled Flask.</summary>
        public static ulong Refilled(ulong data) => data & ~0xFFUL;

        /// <summary>What refilling a Flask costs: its used drinks at the price of one.</summary>
        public static long RefillCost(ulong data, int charges, long pricePerCharge) => Used(data, charges) * Math.Max(0L, pricePerCharge);

        /// <summary>Flux a drink gives back each second.</summary>
        public static float FluxPerSecond(float maxFlux, float share, float seconds) =>
            seconds > 0f ? maxFlux * share / seconds : maxFlux * share;
    }
}
