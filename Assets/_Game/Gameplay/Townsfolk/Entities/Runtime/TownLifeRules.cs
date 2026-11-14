using KryticalNova.Brains;
using Unity.Mathematics;

namespace KryticalNova.Townsfolk
{
    /// <summary>The ten personalities' stable ids (plan 3.4; sent in the ghost, kept in the assets).</summary>
    public static class TownPersonalityIds
    {
        public const byte BusyWorker = 1, Stroller = 2, Jogger = 3, Gossip = 4, Shopper = 5, Loner = 6, NosyLocal = 7, NightOwl = 8,
            StreetPerformer = 9, Guard = 10;
    }

    /// <summary>The town-life mind's rules (pure, Burst, tested): the hour's period, shifts, open spots, the spot score and the pick.</summary>
    public static class TownLifeRules
    {
        /// <summary>The part of the day at <paramref name="hours"/> (0-24) with this one's plan edges moved by <paramref name="jitter"/> hours.</summary>
        public static byte PeriodOf(float hours, float4 starts, float jitter = 0f)
        {
            float h = Wrap(hours - jitter);
            if (h >= starts.w || h < starts.x) return (byte)TownPeriod.Night;
            if (h < starts.y) return (byte)TownPeriod.Dawn;
            if (h < starts.z) return (byte)TownPeriod.Day;
            return (byte)TownPeriod.Dusk;
        }

        public static float Wrap(float hours) => hours - 24f * math.floor(hours / 24f);

        /// <summary>Whether <paramref name="hours"/> lies in [from, to) on a 24 h clock (to before from: across midnight); from == to: always.</summary>
        public static bool InHours(float hours, float from, float to)
        {
            if (from == to) return true;
            float h = Wrap(hours);
            return from < to ? h >= from && h < to : h >= from || h < to;
        }

        /// <summary>
        /// How many of a personality the area wants out in <paramref name="period"/>: its day count by day, its night count at night, and at
        /// dawn and dusk its day count scaled by its out chance there against its day one (the owner's table).
        /// </summary>
        public static int Wanted(int dayCount, int nightCount, float4 outChance, byte period)
        {
            switch (period)
            {
                case (byte)TownPeriod.Day: return dayCount;
                case (byte)TownPeriod.Night: return nightCount;
                default:
                    float chance = period == (byte)TownPeriod.Dawn ? outChance.x : outChance.z;
                    float dayChance = math.max(0.01f, outChance.y);
                    return math.max(0, (int)math.round(dayCount * math.saturate(chance / dayChance)));
            }
        }

        /// <summary>The cap for the period: the day cap by day, the night cap at night, half way between at dawn and dusk; rain thins it.</summary>
        public static int Cap(int dayCap, int nightCap, int hardCap, byte period, bool raining, float rainShare)
        {
            int cap = period == (byte)TownPeriod.Day ? dayCap : period == (byte)TownPeriod.Night ? nightCap : (dayCap + nightCap + 1) / 2;
            if (raining) cap = (int)math.round(cap * math.saturate(rainShare));
            return math.clamp(cap, 0, math.max(0, hardCap));
        }

        /// <summary>What a townsperson does at a spot of this kind.</summary>
        public static byte ActivityAt(byte kind)
        {
            switch ((TownSpotKind)kind)
            {
                case TownSpotKind.Bench: return (byte)TownActivity.Sit;
                case TownSpotKind.Stall:
                case TownSpotKind.VendorStall: return (byte)TownActivity.Shop;
                case TownSpotKind.LeanWall: return (byte)TownActivity.Lean;
                case TownSpotKind.ChatCircle: return (byte)TownActivity.Chat;
                case TownSpotKind.Viewpoint: return (byte)TownActivity.Watch;
                case TownSpotKind.Work: return (byte)TownActivity.Work;
                case TownSpotKind.Perform: return (byte)TownActivity.Perform;
                case TownSpotKind.GuardPost:
                case TownSpotKind.Gate: return (byte)TownActivity.Guard;
                case TownSpotKind.Shelter: return (byte)TownActivity.Shelter;
                case TownSpotKind.JogPoint: return (byte)TownActivity.Stretch;
                default: return (byte)TownActivity.Stand;
            }
        }

        /// <summary>A spot's kind may be stopped at (walk points and doors are passed through, never claimed).</summary>
        public static bool Claimable(byte kind) => kind != (byte)TownSpotKind.Walk && kind != (byte)TownSpotKind.HomeDoor;

        public struct SpotInputs
        {
            public float Like;
            public bool Open;
            public bool Allowed;
            public float Distance;
            public float PreferredDistance;
            public int FreeSlots, SlotCount;
            public bool Recent;
            public bool Raining;
            public byte Rain;
            public bool Sheltered;
            public bool Night;
            public bool NeedsLight;
            public bool Lit;
            public float CrowdingDislike;
            public int Friends;
            public float FriendBonus;
            public bool PlayersNear;
            public bool LikesPlayersNear;
        }

        /// <summary>
        /// A spot's score for one townsperson (plan 3.5), 0 when it cannot be picked (not liked, shut, full, used lately, not for it):
        /// liked weight x distance (full within the preferred distance, half at twice it) x crowding x rain x light, made up for the number
        /// of factors (<see cref="Score.Compensate"/>), then friends and players near on top.
        /// </summary>
        public static float SpotScore(in SpotInputs s)
        {
            if (s.Like <= 0f || !s.Open || !s.Allowed || s.FreeSlots <= 0 || s.Recent) return 0f;
            float distance = s.Distance <= s.PreferredDistance ? 1f : math.saturate(s.PreferredDistance / math.max(s.Distance, 1e-3f));
            float used = s.SlotCount > 0 ? 1f - (float)s.FreeSlots / s.SlotCount : 0f;
            float crowding = math.saturate(1f - 0.5f * used * math.max(0f, s.CrowdingDislike));
            float rain = 1f;
            if (s.Raining && s.Rain != (byte)RainBehaviour.Ignore) rain = s.Sheltered ? 1f : 0.1f;
            float light = s.Night && s.NeedsLight && !s.Lit ? 0.3f : 1f;
            float product = math.saturate(s.Like) * distance * crowding * rain * light;
            float score = Score.Compensate(product, 5);
            if (s.Friends > 0) score *= 1f + s.FriendBonus * s.Friends;
            if (s.PlayersNear && s.LikesPlayersNear) score *= 1.5f;
            return score;
        }

        /// <summary>
        /// One of the best <paramref name="count"/> scores (already sorted best first in <paramref name="scores"/>), each by its share of their
        /// sum; <paramref name="roll"/> in [0, 1). -1 when none scores.
        /// </summary>
        public static int PickWeighted(Unity.Collections.NativeArray<float> scores, int count, float roll)
        {
            float sum = 0f;
            int n = math.min(count, scores.Length);
            for (int i = 0; i < n; i++) sum += math.max(0f, scores[i]);
            if (sum <= 0f) return -1;
            float at = roll * sum;
            for (int i = 0; i < n; i++)
            {
                at -= math.max(0f, scores[i]);
                if (at < 0f) return i;
            }
            return n - 1;
        }

        /// <summary>Whether a townsperson stays out: shift workers only on their shift, the rain-shy not in rain.</summary>
        public static bool StaysOut(bool hasShift, float hours, float shiftStart, float shiftEnd, bool raining, byte rain)
        {
            if (hasShift && !InHours(hours, shiftStart, shiftEnd)) return false;
            if (raining && (rain == (byte)RainBehaviour.GoHome || rain == (byte)RainBehaviour.PackUp)) return false;
            return true;
        }

        /// <summary>The slots of a spot: in a row along its facing (spacing apart) or, for a chat circle, round its middle facing in.</summary>
        public static void SlotPlace(float3 centre, float yawDeg, byte kind, int slot, int count, float spacing, out float3 pos, out float yaw)
        {
            if (kind == (byte)TownSpotKind.ChatCircle)
            {
                float a = math.radians(yawDeg) + 2f * math.PI * slot / math.max(1, count);
                float r = math.max(0.6f, spacing);
                pos = centre + new float3(math.sin(a), 0f, math.cos(a)) * r;
                yaw = a + math.PI;   // facing the middle
                return;
            }
            float y = math.radians(yawDeg);
            float3 side = new float3(math.cos(y), 0f, -math.sin(y));
            float offset = (slot - (count - 1) * 0.5f) * spacing;
            pos = centre + side * offset;
            yaw = y;
        }

        /// <summary>A quantised speed for the ghost (steps of 0.05 m/s, at most 12.75 m/s).</summary>
        public static byte SpeedSteps(float metresPerSecond) => (byte)math.clamp((int)math.round(metresPerSecond / 0.05f), 0, 255);
    }
}
