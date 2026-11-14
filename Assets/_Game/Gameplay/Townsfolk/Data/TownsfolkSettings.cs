using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// Every number of town life that is not a personality's (plan 3 and 4; owner 13 Nov 2026: 48 by day, 20 at night). Read once at server
    /// start into the server's config and by the client view. Asset: <see cref="TownsfolkPaths.SettingsAsset"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Krytical Nova/Townsfolk/Settings", fileName = "TownsfolkSettings")]
    public sealed class TownsfolkSettings : ScriptableObject
    {
        [Header("Crowd (owner 13 Nov 2026)")]
        [Tooltip("Townsfolk out by day on an area whose cast does not give its own.")]
        public int dayCap = 48;
        [Tooltip("Townsfolk out at night.")]
        public int nightCap = 20;
        [Tooltip("Never more than this, whatever a GM sets.")]
        public int hardCap = 60;
        [Tooltip("How many come out of their doors per second at most (the town fills over a few seconds, never in one frame).")]
        public int spawnsPerSecond = 4;
        [Tooltip("Share of the crowd still out while it rains (the rest went home or under shelter by their rule).")]
        [Range(0f, 1f)] public float rainCrowdShare = 0.6f;

        [Header("Day clock (game hours)")]
        public float dawnStartHour = 5f;
        public float dayStartHour = 8f;
        public float duskStartHour = 18f;
        public float nightStartHour = 22f;
        [Tooltip("Each townsperson's plan edges move by up to this many game hours from its seed, so the town never empties on the hour.")]
        public float planJitterHours = 0.5f;

        [Header("Smart spots")]
        [Tooltip("Metres: spots nearer than this score full on distance; it falls off to half at twice this.")]
        public float preferredSpotDistance = 40f;
        [Tooltip("The best this many spots are rolled between (weighted by score), so nothing looks scripted.")]
        public int pickFromBest = 3;
        [Tooltip("Spots a townsperson remembers and does not go back to.")]
        public int recentSpots = 3;
        [Tooltip("Seconds over its stay after which a claim frees itself (nothing locks a bench forever).")]
        public float staleClaimSeconds = 30f;
        [Tooltip("Score bonus per friend already in a chat circle (personalities that chat with each other).")]
        public float friendBonus = 0.5f;
        [Tooltip("Metres from a spot's slot that count as arrived.")]
        public float arriveRadius = 0.7f;
        [Tooltip("Seconds a walk may take before the townsperson gives up and picks again (a blocked path).")]
        public float giveUpWalkSeconds = 60f;
        [Tooltip("Metres a spot marker may be off the NavMesh and still be snapped onto it.")]
        public float snapToNavMesh = 4f;
        [Tooltip("Extra walk points sampled on the area's NavMesh (strolls between spots).")]
        public int walkPoints = 60;

        [Header("Far away (Kingdom Come)")]
        [Tooltip("Seconds between plan steps of a townsperson no player is near (Brain tier 3): no walking, it is put at its next spot.")]
        public float farPlanStepSeconds = 2f;

        [Header("Client view")]
        [Tooltip("Bodies in the client's pool: the nearest this many townsfolk are shown.")]
        public int viewBudget = 48;
        [Tooltip("Metres from the camera within which townsfolk get a body.")]
        public float viewRadius = 80f;
        [Tooltip("Looks baked per cast: townsfolk of one look share one merged mesh (owner pick 3a: bake once per outfit).")]
        public int looksPerCast = 16;
        [Tooltip("Metres within which a townsperson may turn its head to the player (head-look; step 6).")]
        public float headLookRadius = 8f;
        [Tooltip("Most townsfolk looking at the local player at once.")]
        public int maxLookers = 6;
        [Tooltip("Seconds of walk-clip blending (the speed parameter's damping).")]
        public float speedDamp = 0.15f;
    }

    public static class TownsfolkPaths
    {
        public const string Root = "Assets/_Game/Gameplay/Townsfolk";
        public const string DataAssets = Root + "/Data/Assets";
        public const string SettingsAsset = DataAssets + "/TownsfolkSettings.asset";
        public const string PersonalityFolder = DataAssets + "/Personalities";
        public const string SkyConcordArea = DataAssets + "/Area_SkyConcord.asset";
        public const string SkyConcordCast = DataAssets + "/Cast_SkyConcord.asset";
    }
}
