using UnityEngine;

namespace KryticalNova.Player
{
    /// <summary>The answer to "can this player run up this surface now": yes (CanRunUpWall), or the reason it cannot.</summary>
    public enum ClimbVerdict : byte
    {
        CanRunUpWall,
        /// <summary>Wallstride is not open yet (level 60, Run and Jump at rank 20).</summary>
        Locked,
        /// <summary>Dead, already climbing, or mid-jump in a way that cannot catch.</summary>
        Busy,
        /// <summary>Neither Run selected nor a jump in flight.</summary>
        NoStride,
        /// <summary>Not a wall or a tree: it moves, it can be hurt or used, or it is marked Never.</summary>
        NotClimbable,
        /// <summary>A slope, not a wall.</summary>
        TooFlat,
        /// <summary>It leans out over the climber too far.</summary>
        Overhang,
        /// <summary>Met along its face rather than at it.</summary>
        Sideways,
        TooLow,
        TooHigh,
        /// <summary>A wall with no walkable top within reach, a tree with nowhere to stand.</summary>
        NoTop,
        /// <summary>The top has no room for the body.</summary>
        NoRoom,
        NoFlux,
    }

    /// <summary>Everything that decides a climb, gathered by the motor from the surface and the player.</summary>
    public struct ClimbQuery
    {
        public bool Unlocked;
        public bool Busy;
        /// <summary>Run is the selected stride, or a jump is in flight.</summary>
        public bool Stride;
        /// <summary>Still and solid, on a wall layer or marked climbable, and not marked Never.</summary>
        public bool Eligible;
        public ClimbableKind Kind;
        /// <summary>The surface's face, pointing out of it.</summary>
        public Vector3 Normal;
        /// <summary>Which way the player is going (or the way to the surface for a click).</summary>
        public Vector3 Travel;
        /// <summary>Metres from the foot of the surface to where the climb ends.</summary>
        public float Height;
        public bool HasTop;
        public bool RoomAtTop;
        public bool CanPay;
    }

    /// <summary>
    /// Wallstride's rule (owner, 16 Oct 2026): whether a player can run up a surface, worked out from the player and
    /// the surface alone, so the server and the player's own game come to the same answer. The order of the checks
    /// is the order of the reasons a player is told.
    /// </summary>
    public static class WallRunRules
    {
        /// <summary>Wallstride opens at a level, with Run and Jump both at a rank (all three in the settings).</summary>
        public static bool IsOpen(int level, int runRank, int jumpRank, WallstrideSettings settings) =>
            settings != null && level >= settings.requiredLevel && runRank >= settings.requiredRunRank && jumpRank >= settings.requiredJumpRank;

        /// <summary>Degrees between the surface's face and the ground: 90 is upright, more leans out over the climber.</summary>
        public static float SurfaceAngle(Vector3 normal) => Vector3.Angle(normal, Vector3.up);

        /// <summary>Degrees between the way the player goes and straight at the surface, on the ground.</summary>
        public static float ApproachAngle(Vector3 travel, Vector3 normal)
        {
            Vector3 flatTravel = new Vector3(travel.x, 0f, travel.z), into = new Vector3(-normal.x, 0f, -normal.z);
            if (flatTravel.sqrMagnitude < 1e-6f || into.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(flatTravel, into);
        }

        public static ClimbVerdict Evaluate(in ClimbQuery q, WallstrideSettings s)
        {
            if (s == null || !q.Unlocked) return ClimbVerdict.Locked;
            if (q.Busy) return ClimbVerdict.Busy;
            if (!q.Stride) return ClimbVerdict.NoStride;
            if (!q.Eligible || q.Kind == ClimbableKind.Never) return ClimbVerdict.NotClimbable;
            if (q.Kind == ClimbableKind.Wall)
            {
                float angle = SurfaceAngle(q.Normal);
                if (angle < s.minSurfaceAngle) return ClimbVerdict.TooFlat;
                if (angle > 90f + s.maxOverhang) return ClimbVerdict.Overhang;
            }
            if (ApproachAngle(q.Travel, q.Normal) > s.maxApproachAngle) return ClimbVerdict.Sideways;
            if (q.Kind == ClimbableKind.Wall && q.Height < s.minWallHeight) return ClimbVerdict.TooLow;
            if (q.Height > s.maxClimbHeight) return ClimbVerdict.TooHigh;
            if (!q.HasTop) return ClimbVerdict.NoTop;
            if (!q.RoomAtTop) return ClimbVerdict.NoRoom;
            if (!q.CanPay) return ClimbVerdict.NoFlux;
            return ClimbVerdict.CanRunUpWall;
        }

        /// <summary>True when the player can run up the surface: the canRunUpWall flag.</summary>
        public static bool CanRunUpWall(in ClimbQuery q, WallstrideSettings s) => Evaluate(q, s) == ClimbVerdict.CanRunUpWall;

        /// <summary>The climb's speed: a share of the player's speed on the ground, within the limits.</summary>
        public static float ClimbSpeed(float groundSpeed, WallstrideSettings s) =>
            s == null ? 6f : Mathf.Clamp(groundSpeed * s.climbSpeedShare, s.minClimbSpeed, s.maxClimbSpeed);

        /// <summary>What the player is told when a climb is refused.</summary>
        public static string Reason(ClimbVerdict verdict, WallstrideSettings s)
        {
            switch (verdict)
            {
                case ClimbVerdict.Locked:
                    return s != null ? $"Wallstride opens at level {s.requiredLevel} with Run and Jump at rank {s.requiredRunRank}." : "Wallstride is not open yet.";
                case ClimbVerdict.Busy: return "Not now.";
                case ClimbVerdict.NoStride: return "Select Run to run up walls, or jump at them.";
                case ClimbVerdict.NotClimbable: return "That cannot be climbed.";
                case ClimbVerdict.TooFlat: return "Too gentle a slope to run up: walk it.";
                case ClimbVerdict.Overhang: return "It leans out too far to run up.";
                case ClimbVerdict.Sideways: return "Come at it head-on to run up it.";
                case ClimbVerdict.TooLow: return "Low enough to vault or walk.";
                case ClimbVerdict.TooHigh: return s != null ? $"Too high: Wallstride reaches {s.maxClimbHeight:0} m." : "Too high.";
                case ClimbVerdict.NoTop: return "There is nowhere to stand at the top.";
                case ClimbVerdict.NoRoom: return "There is no room at the top.";
                case ClimbVerdict.NoFlux: return s != null ? $"Not enough Flux ({s.fluxCost:0} to climb)." : "Not enough Flux.";
                default: return string.Empty;
            }
        }
    }
}
