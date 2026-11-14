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

        [Header("Client view: Crowd Detail tiers (Settings, Graphics; index Low, Medium, High, Ultra; owner 13 Nov 2026)")]
        [Tooltip("Per tier: the nearest this many townsfolk get a full, animated body (the server's crowd is the same on every tier).")]
        public int[] fullBodiesByTier = { 16, 32, 48, 60 };
        [Tooltip("Per tier: metres from the camera within which a townsperson gets a full body; farther ones, or over the count, get a card.")]
        public float[] fullBodyDistanceByTier = { 25f, 35f, 45f, 60f };
        [Tooltip("Per tier: metres from the camera within which townsfolk are drawn at all (a body or a far card).")]
        public float[] viewRadiusByTier = { 60f, 80f, 100f, 140f };
        [Tooltip("Per tier: most far cards drawn (one instanced draw per look).")]
        public int[] cardsByTier = { 24, 48, 64, 96 };
        [Tooltip("Per tier: metres within which a body animates every frame; farther ones animate every few frames (next row).")]
        public float[] everyFrameWithinByTier = { 10f, 15f, 20f, 30f };
        [Tooltip("Per tier: a far body animates every this many frames (staggered by its seed).")]
        public int[] farAnimateEveryByTier = { 4, 3, 2, 2 };
        [Tooltip("Per tier: metres within which a body casts a shadow; farther ones never do.")]
        public float[] shadowsWithinByTier = { 8f, 12f, 18f, 25f };
        [Tooltip("Per tier: metres within which a body skins with every bone; farther, lighter skinning (CharacterLod's steps).")]
        public float[] fullSkinWithinByTier = { 10f, 15f, 20f, 30f };
        [Tooltip("Layer the far cards' snapshots are taken on (once per look, from a hidden camera): one nothing else uses.")]
        public int cardCaptureLayer = 31;
        [Tooltip("Pixels of a card snapshot (width; the height is twice it).")]
        public int cardPixels = 96;
        [Tooltip("Looks baked per cast: townsfolk of one look share one merged mesh (owner pick 3a: bake once per outfit).")]
        public int looksPerCast = 16;
        [Tooltip("Metres within which a townsperson may turn its head to the player (head-look; step 6).")]
        public float headLookRadius = 8f;
        [Tooltip("Most townsfolk looking at the local player at once.")]
        public int maxLookers = 6;
        [Tooltip("Seconds of walk-clip blending (the speed parameter's damping).")]
        public float speedDamp = 0.15f;

        [Header("Vendors (plan 3.6; owner 18.1 A: dressed with Sidekick like the crowd; client only)")]
        [Tooltip("Metres within which a vendor's head follows the player.")]
        public float vendorHeadFollowRadius = 8f;
        [Tooltip("How far the head turns at most (0-1 of the look-at IK).")]
        [Range(0f, 1f)] public float vendorHeadWeight = 0.8f;
        [Tooltip("Metres within which a vendor waves or beckons when the player looks its way.")]
        public float vendorWaveRadius = 10f;
        [Tooltip("Seconds between one vendor's waves.")]
        public float vendorWaveCooldown = 30f;
        [Tooltip("Seconds between any two vendors' waves (never all at once).")]
        public float vendorAnyWaveGap = 10f;
        [Tooltip("Seconds between a vendor's show-goods gestures while the player stands in reach.")]
        public float vendorShowGoodsCooldown = 20f;
        [Tooltip("Looks of a vendor are numbered from here (apart from the crowd's), one fixed look per vendor from its name.")]
        public int vendorLookBase = 1000;
        [Tooltip("Metres within which a vendor calls out to a passing player (its manner's lines, in a speech bubble).")]
        public float vendorCalloutRadius = 12f;
        [Tooltip("Seconds between one vendor's call-outs.")]
        public float vendorCalloutCooldown = 45f;
        [Tooltip("Seconds between any two vendors' call-outs (never a chorus).")]
        public float vendorAnyCalloutGap = 10f;
        [Tooltip("Seconds a vendor's line stays up (plus a little per character, below).")]
        public float vendorLineSeconds = 2.5f;
        [Tooltip("Extra seconds per character of a line.")]
        public float vendorLineSecondsPerChar = 0.04f;

        /// <summary>A Crowd Detail tier's value of a per-tier row (the last one when the row is short; <paramref name="fallback"/> when empty).</summary>
        public static T ByTier<T>(T[] row, int tier, T fallback)
        {
            if (row == null || row.Length == 0) return fallback;
            return row[tier < 0 ? 0 : tier >= row.Length ? row.Length - 1 : tier];
        }
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
