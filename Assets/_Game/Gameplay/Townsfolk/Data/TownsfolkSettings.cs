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
        [Tooltip("Metres from a home door that count as home (several may go in at once; they never all fit on its point).")]
        public float homeDoorRadius = 2f;
        [Tooltip("Seconds a walk may take before the townsperson gives up and picks again (a blocked path).")]
        public float giveUpWalkSeconds = 60f;
        [Tooltip("Metres a spot marker may be off the NavMesh and still be snapped onto it.")]
        public float snapToNavMesh = 4f;
        [Tooltip("Extra walk points sampled on the area's NavMesh (strolls between spots).")]
        public int walkPoints = 60;

        [Header("Life over the whole district (owner 14 Nov 2026: not only by the Sky Lift arrival)")]
        [Tooltip("Metres between the points spread over every walkable floor of the area (plazas, streets, stairs, upper tiers, edges), found on its NavMesh.")]
        public float spreadSpacing = 9f;
        [Tooltip("Metres up or down from the area's deck that still count as its floors (Sky Concord's upper tiers and stairs).")]
        public float spreadHeight = 26f;
        [Tooltip("Most spread points (walk points, plus the ones below that become places to stand).")]
        public int spreadMostPoints = 240;
        [Tooltip("Metres from the area's outer edge within which a spread point near the NavMesh rim is a viewpoint looking out (strollers).")]
        public float spreadRimBand = 18f;
        [Tooltip("Metres a rim point may be from the NavMesh's edge (a rail).")]
        public float spreadRimEdge = 2.5f;
        [Tooltip("Metres from every hand-placed spot past which a spread point is a back street: some become quiet lean spots (loners).")]
        public float backStreetFrom = 18f;
        [Tooltip("Share of back-street points that become quiet places to stand; the rest stay walk points.")]
        [Range(0f, 1f)] public float backStreetStandShare = 0.4f;
        [Tooltip("Share of the spread points (not rim, not back street) that become places to stand and look round; the rest stay walk points.")]
        [Range(0f, 1f)] public float spreadStandShare = 0.15f;
        [Tooltip("Share of new townsfolk that step out somewhere on the district (round a corner) instead of from a home door.")]
        [Range(0f, 1f)] public float spawnAnywhereShare = 0.7f;
        [Tooltip("Metres round the Sky Lift arrival that count as near it.")]
        public float arrivalRadius = 28f;
        [Tooltip("Most share of the crowd that may be near the arrival: past it nobody steps out there and spots there score less.")]
        [Range(0f, 1f)] public float arrivalMostShare = 0.15f;
        [Tooltip("Score of a spot near the arrival while the share there is past the most (0-1 of its score).")]
        [Range(0f, 1f)] public float arrivalFullScore = 0.25f;

        [Header("Far away (Kingdom Come)")]
        [Tooltip("Seconds between plan steps of a townsperson no player is near (Brain tier 3): no walking, it is put at its next spot.")]
        public float farPlanStepSeconds = 2f;

        [Header("Client view: five distance bands per Crowd Detail tier (Settings, Graphics; index Low, Medium, High, Ultra; owner 14 Nov 2026)")]
        [Tooltip("Per tier: the nearest this many townsfolk get a body (CLOSE, MEDIUM or FAR band); over the count they are cards (the server's crowd is the same on every tier).")]
        public int[] fullBodiesByTier = { 20, 32, 48, 60 };
        [Tooltip("Per tier: CLOSE band ends here (metres from the camera). CLOSE = the whole mesh, every frame animated, head-look, shadows.")]
        public float[] closeWithinByTier = { 12f, 18f, 25f, 35f };
        [Tooltip("Per tier: MEDIUM band ends here. MEDIUM = LOD1 (about half the triangles), animated every 2nd frame, shadows, no head-look.")]
        public float[] mediumWithinByTier = { 28f, 40f, 55f, 75f };
        [Tooltip("Per tier: FAR band ends here, the bodies end and the cards begin (REALLY FAR). FAR = LOD2 (about a quarter), animated every 4th frame, no shadows. Low never pulls this so close that a card reads as a flat figure.")]
        public float[] farWithinByTier = { 45f, 65f, 85f, 115f };
        [Tooltip("Per tier: REALLY FAR band (the picture cards) ends here; past it townsfolk are TOO FAR and not drawn at all (the server still keeps them where they are).")]
        public float[] cardsWithinByTier = { 120f, 160f, 220f, 300f };
        [Tooltip("Per tier: most far cards drawn (one instanced draw per look).")]
        public int[] cardsByTier = { 32, 48, 64, 96 };
        [Tooltip("MEDIUM bodies animate every this many frames, FAR bodies every this many (staggered by their seed).")]
        public int mediumAnimateEvery = 2, farAnimateEvery = 4;
        [Tooltip("Share of the triangles the baked LOD1 and LOD2 keep (made once per look when it is baked, shared by every body wearing it).")]
        [Range(0.05f, 1f)] public float lod1Share = 0.5f, lod2Share = 0.25f;
        [Tooltip("Metres past a band edge a townsperson must go before it changes back (no flicker on the line).")]
        public float bandHysteresis = 2f;
        [Tooltip("Layer the far cards' snapshots are taken on (once per look, from a hidden camera): one nothing else uses.")]
        public int cardCaptureLayer = 31;
        [Tooltip("Pixels of a card snapshot (width; the height is twice it).")]
        public int cardPixels = 96;
        [Tooltip("Looks baked per cast: townsfolk of one look share one merged mesh (owner pick 3a: bake once per outfit).")]
        public int looksPerCast = 16;
        [Tooltip("Sidekick parts townsfolk and vendors never wear (owner 14 Nov 2026: no weapons): back mounts, holsters and sheaths are left out of the baked mesh. Guards too, until the Master says otherwise.")]
        public KryticalNova.Items.BodyRegion[] noWeaponRegions =
        {
            KryticalNova.Items.BodyRegion.AttachmentBack, KryticalNova.Items.BodyRegion.AttachmentHipsBack,
            KryticalNova.Items.BodyRegion.AttachmentHipsLeft, KryticalNova.Items.BodyRegion.AttachmentHipsRight,
        };
        [Tooltip("Metres within which a townsperson in the CLOSE band may turn its head to the player (head-look).")]
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

        [Header("Vendors out of step (owner 14 Nov 2026: never the same emote at the same time)")]
        [Tooltip("Share of its standing animation's cycle a vendor may start into, by its own seed (1 = anywhere in it, 0 = all start together).")]
        [Range(0f, 1f)] public float vendorStartSpread = 1f;
        [Tooltip("Each vendor's animation runs faster or slower by up to this share, by its own seed (they drift apart).")]
        [Range(0f, 0.3f)] public float vendorSpeedJitter = 0.08f;
        [Tooltip("Every vendor cooldown and gap above is longer or shorter by up to this share, rolled anew each time.")]
        [Range(0f, 0.9f)] public float vendorCooldownJitter = 0.3f;
        [Tooltip("A wave, beckon or show-goods gesture starts up to this many seconds late, rolled per vendor each time (two vendors never start on one frame).")]
        public float vendorGestureDelay = 0.8f;

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
        public const string CrossFadeShader = Root + "/Data/Shaders/KN_Townsfolk_Sidekick.shadergraph";
    }
}
