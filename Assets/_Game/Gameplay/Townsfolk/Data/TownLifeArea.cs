using System;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// A place with town life (plan 3.3 and 3.5): the scene object it hangs off, its smart spots in that object's own frame (snapped onto
    /// the NavMesh by the server at start), who lives there (the personality mix by day and night) and how they look (the cast).
    /// Sky Concord's is <see cref="TownsfolkPaths.SkyConcordArea"/>, written by Krytical Nova ▸ Townsfolk ▸ Build Assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Townsfolk/Area", fileName = "Area_")]
    public sealed class TownLifeArea : ScriptableObject
    {
        [Serializable]
        public struct Spot
        {
            public string label;
            public TownSpotKind kind;
            [Tooltip("Metres in the root's frame (x east, z north); y is found on the NavMesh.")]
            public Vector2 at;
            [Tooltip("Degrees the slots face (a chat circle's slots face its middle instead).")]
            public float yaw;
            [Range(1, 6)] public int slots;
            [Tooltip("Metres between slots (a chat circle: its radius).")]
            public float spacing;
            public TownSpotFlags flags;
            [Tooltip("Game hours it is open (from = to: always).")]
            public int openFrom, openTo;
            [Tooltip("Only this personality uses it (a Work spot, a Guard post); none = anyone.")]
            public TownsfolkPersonality onlyFor;
        }

        [Serializable]
        public struct MixRow
        {
            public TownsfolkPersonality personality;
            [Tooltip("How many of this personality are out by day (dawn and dusk follow from its out chances).")]
            public int day;
            public int night;
        }

        [Tooltip("The scene object the spots are measured from (found by name on the server).")]
        public string rootName = "Concord 040 (sky preview)";
        [Tooltip("Metres round the root the area's NavMesh walk points are sampled in.")]
        public float radius = 90f;
        [Tooltip("Height of the walkable deck above the root, metres (the NavMesh is searched round it).")]
        public float deckHeight = 0f;
        public uint seed = 0x5C0;
        [Tooltip("0 = the settings' caps.")]
        public int dayCap, nightCap;
        public TownsfolkCast cast;
        public MixRow[] mix = Array.Empty<MixRow>();
        public Spot[] spots = Array.Empty<Spot>();
    }
}
