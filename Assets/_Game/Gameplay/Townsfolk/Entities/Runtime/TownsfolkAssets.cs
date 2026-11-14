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

    /// <summary>
    /// What a client needs to show townsfolk (Resources: <see cref="ResourcePath"/>): the body every townsperson is dressed on (the
    /// players' own Sidekick model), the shared animator (one controller, an override per body type), the creation database the looks
    /// come from, the casts and the personalities (their reactions and lines).
    /// </summary>
    public sealed class TownsfolkViewAssets : ScriptableObject
    {
        public const string ResourcePath = "TownsfolkViewAssets";
        public TownsfolkSettings settings;
        public GameObject body;
        public RuntimeAnimatorController masculine;
        public RuntimeAnimatorController feminine;
        public CharacterCreationDatabase database;
        public TownsfolkCast[] casts = System.Array.Empty<TownsfolkCast>();
        public TownsfolkPersonality[] personalities = System.Array.Empty<TownsfolkPersonality>();

        public TownsfolkCast CastOf(byte id)
        {
            foreach (TownsfolkCast c in casts) if (c != null && c.id == id) return c;
            return null;
        }

        public TownsfolkPersonality PersonalityOf(byte id)
        {
            foreach (TownsfolkPersonality p in personalities) if (p != null && p.id == id) return p;
            return null;
        }
    }
}
