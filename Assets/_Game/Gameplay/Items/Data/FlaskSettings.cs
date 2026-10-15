using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// The Flux Flask (Twelve Sky's Calabash, made a Flux reservoir by the owner on 15 Oct 2026): a bottle that is
    /// kept, drunk a few times and refilled at an Apothecary for Creds. Each drink gives Flux back over a few seconds,
    /// where a Stim gives it at once. Read by <see cref="PlayerFlask"/>; the numbers are first picks, to tune.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Items/Flask Settings", fileName = "FlaskSettings")]
    public sealed class FlaskSettings : ScriptableObject
    {
        [Tooltip("Drinks a full Flask holds.")]
        [Range(1, 15)] public int charges = 3;

        [Tooltip("Share of Max Flux one drink gives back, 0.45 = 45 %.")]
        [Range(0.05f, 1f)] public float fluxShare = 0.45f;

        [Tooltip("Seconds a drink takes to give all of it back.")]
        [Min(0.5f)] public float seconds = 8f;

        [Tooltip("Creds an Apothecary takes to refill one drink.")]
        [Min(0)] public long refillPricePerCharge = 60;
    }
}
