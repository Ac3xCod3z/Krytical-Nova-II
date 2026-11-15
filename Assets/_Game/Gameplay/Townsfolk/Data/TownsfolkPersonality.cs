using System;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// One of the ten personalities (owner's DAY list item 18: DRAFTS for the owner to edit; plan 3.4). How fast it walks, how often and
    /// how long it stops, which spots it likes, who it chats with, when it is out, what rain does to it and how it reacts to players.
    /// The server reads the plan part; the client the reaction part. Assets: <see cref="TownsfolkPaths.PersonalityFolder"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Townsfolk/Personality", fileName = "TP_Personality")]
    public sealed class TownsfolkPersonality : ScriptableObject
    {
        [Serializable]
        public struct SpotLike
        {
            public TownSpotKind kind;
            [Range(0f, 1f)] public float weight;
        }

        [Serializable]
        public struct Reaction
        {
            [Tooltip("Seconds it looks at a player passing within the head-look radius (0 = never looks).")]
            public float glanceSeconds;
            [Range(0f, 1f)] public float greetChance;
            [Tooltip("Metres from a player at which it steps aside.")]
            public float stepAsideMetres;
            [Tooltip("Watches anyone who runs past within the head-look radius (Guard).")]
            public bool watchRunners;
            [Tooltip("Bows or cheers to a player who stops near it (Street Performer).")]
            public bool playsToPlayers;
        }

        [Tooltip("Stable number, sent in the ghost. Never reuse or renumber.")]
        public byte id;
        public string displayName = "Personality";
        [TextArea(2, 4)] public string notes;

        [Header("Walking")]
        [Tooltip("Walk speed range, m/s (Synty walk clip about 1.4).")]
        public Vector2 walkSpeed = new Vector2(1.1f, 1.3f);
        public bool jogs;
        [Tooltip("Jog speed range, m/s (Synty run clip about 3.5).")]
        public Vector2 jogSpeed = new Vector2(3.2f, 3.8f);

        [Header("Stopping")]
        [Tooltip("Chance it stops at a spot after a walk (else it walks on to another walk point).")]
        [Range(0f, 1f)] public float stopChance = 0.5f;
        [Tooltip("Seconds at a spot (rolled between the two).")]
        public Vector2 staySeconds = new Vector2(20f, 60f);
        public SpotLike[] likes = Array.Empty<SpotLike>();
        [Tooltip("How much a crowded spot puts it off: 1 normal, 2 = a Loner.")]
        public float crowdingDislike = 1f;
        [Tooltip("At night it only likes lit spots (Night Owl).")]
        public bool needsLight;
        [Tooltip("Spots near players score more (Nosy Local).")]
        public bool likesPlayersNear;

        [Header("Company")]
        public TownsfolkPersonality[] chatsWith = Array.Empty<TownsfolkPersonality>();
        [Range(0f, 1f)] public float chatChance = 0.5f;
        public bool declinesInvites;

        [Header("Day plan")]
        [Tooltip("Chance to be out at dawn, day, dusk, night (the area's mix gives the day and night counts; these give dawn and dusk).")]
        public Vector4 outChance = new Vector4(0.4f, 0.8f, 0.6f, 0.1f);
        [Tooltip("Work shifts in game hours (x start, y end; empty = no shifts). A new one comes out on the shift that is running.")]
        public Vector2Int[] shifts = Array.Empty<Vector2Int>();
        public RainBehaviour rain = RainBehaviour.Shelter;

        [Header("Players (client only)")]
        public Reaction reaction;
        [Tooltip("Lines it may say near a player (text bubbles first, owner pick 4a).")]
        public NpcLine[] barks = Array.Empty<NpcLine>();

        public float Likes(TownSpotKind kind)
        {
            foreach (SpotLike l in likes) if (l.kind == kind) return l.weight;
            return 0f;
        }
    }
}
