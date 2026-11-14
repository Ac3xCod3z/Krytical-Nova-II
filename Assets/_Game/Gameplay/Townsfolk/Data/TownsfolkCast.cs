using System;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// Who lives in an area and how they look (plan 3.8): which factions (weights) and lineages. A faction's Home City lists only that
    /// faction; a neutral town such as Sky Concord lists all three. Looks are Sidekick recipes made from a seed (the same on every client).
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Townsfolk/Cast", fileName = "Cast_")]
    public sealed class TownsfolkCast : ScriptableObject
    {
        [Serializable]
        public struct FactionWeight
        {
            public FactionId faction;
            [Min(0f)] public float weight;
            [Tooltip("Lineage ids allowed (empty = every lineage of the faction).")]
            public byte[] lineages;
        }

        [Tooltip("Stable number, sent in the ghost.")]
        public byte id = 1;
        public FactionWeight[] factions = Array.Empty<FactionWeight>();
        [Range(0f, 1f)] public float feminineShare = 0.5f;
        [Tooltip("Townsfolk wear their lineage's basic outfit (the players' starting clothes) instead of the bare body.")]
        public bool dressed = true;
    }
}
