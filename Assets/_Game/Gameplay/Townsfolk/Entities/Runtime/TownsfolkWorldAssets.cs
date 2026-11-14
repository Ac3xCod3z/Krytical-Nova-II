using KryticalNova.Brains;
using KryticalNova.CharacterCreation;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// What the server's town life needs (Resources: <see cref="ResourcePath"/>; written by Krytical Nova ▸ Townsfolk ▸ Build Assets): the
    /// Brain's settings (installed only when the Hostiles did not already), the town settings, the ten personalities and the areas.
    /// Nothing a client draws: the server never loads a body (plan 3.8, the black-sky lesson).
    /// </summary>
    public sealed class TownsfolkWorldAssets : ScriptableObject
    {
        public const string ResourcePath = "TownsfolkWorldAssets";
        public BrainSettings brain;
        public TownsfolkSettings settings;
        public TownsfolkPersonality[] personalities = System.Array.Empty<TownsfolkPersonality>();
        public TownLifeArea[] areas = System.Array.Empty<TownLifeArea>();

        public TownsfolkPersonality PersonalityOf(byte id)
        {
            foreach (TownsfolkPersonality p in personalities) if (p != null && p.id == id) return p;
            return null;
        }
    }
}
