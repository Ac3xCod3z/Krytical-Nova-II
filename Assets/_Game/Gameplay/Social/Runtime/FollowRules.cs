namespace KryticalNova.Social
{
    /// <summary>What a follower does at one look at the member it follows.</summary>
    public enum FollowStep : byte
    {
        /// <summary>Close enough: stand.</summary>
        Hold = 0,
        /// <summary>Walk toward the member.</summary>
        Move = 1,
        /// <summary>Following is over.</summary>
        End = 2,
    }

    /// <summary>Why following ended.</summary>
    public enum FollowEnd : byte
    {
        None = 0,
        /// <summary>The follower gave a move order of their own.</summary>
        Ordered = 1,
        /// <summary>The follower struck, was struck, or is in a Callout.</summary>
        Combat = 2,
        /// <summary>The two are no longer in one Squad.</summary>
        LeftSquad = 3,
        /// <summary>The member is no longer on this map.</summary>
        Gone = 4,
        /// <summary>The member got too far ahead.</summary>
        TooFar = 5,
        /// <summary>The follower was defeated.</summary>
        Defeated = 6,
        /// <summary>The member jumped and the follower could not (Jump not learned, no Flux, too far for its rank).</summary>
        JumpRefused = 7,
    }

    /// <summary>One jump of the member's, to be copied: where it took off, where it landed, and when.</summary>
    public struct FollowHop
    {
        public UnityEngine.Vector3 takeoff, landing;
        public float at;
    }

    /// <summary>What the follower does about the next jump of the member's it has to copy.</summary>
    public enum HopStep : byte
    {
        /// <summary>Wait: the follower is in the air, or its landing is still settling.</summary>
        None = 0,
        /// <summary>Walk to where the member took off.</summary>
        WalkToTakeoff = 1,
        /// <summary>Jump to where the member landed.</summary>
        Jump = 2,
        /// <summary>Forget it: too old, or the follower is already past the landing.</summary>
        Drop = 3,
    }

    /// <summary>What the server knows about the follower and the next hop. Distances are metres on the ground.</summary>
    public struct HopFacts
    {
        public float age, distanceToTakeoff, distanceToLanding;
        /// <summary>The follower's jump is free to start (not in the air, landing settled).</summary>
        public bool jumpReady;
        public bool jumping;
    }

    /// <summary>What the server knows at one look. Distances are metres on the ground.</summary>
    public struct FollowFacts
    {
        /// <summary>The member's player is still in this session and on this map.</summary>
        public bool targetPresent;
        public bool sameSquad;
        public bool followerAlive;
        /// <summary>The follower gave a move or stop order of their own since the last look.</summary>
        public bool followerOrdered;
        public bool followerInCombat;
        public float distance;
        /// <summary>The follower is on its way to the member (as opposed to standing by it).</summary>
        public bool moving;
    }

    /// <summary>
    /// Following a Squad member: the follower walks after the member and stops a short way off. It ends the moment
    /// the follower moves by their own order, fights, or is defeated, the two stop being Squad mates, the member
    /// leaves the map, or the member gets too far ahead. Pure rules; <see cref="PlayerFollow"/> applies them on the server.
    /// </summary>
    public static class FollowRules
    {
        /// <summary>The spot exactly <paramref name="keep"/> metres from the member, on the follower's side of it.</summary>
        public static UnityEngine.Vector3 StandPoint(UnityEngine.Vector3 follower, UnityEngine.Vector3 member, float keep)
        {
            UnityEngine.Vector3 back = follower - member;
            back.y = 0f;
            float distance = back.magnitude;
            if (distance <= keep || distance < 0.001f) return follower;
            return member + back * (keep / distance);
        }

        /// <summary>Remembers a jump of the member's; when more are kept than <paramref name="max"/>, the oldest gives way.</summary>
        public static void Enqueue(System.Collections.Generic.List<FollowHop> hops, in FollowHop hop, int max)
        {
            if (hops == null) return;
            hops.Add(hop);
            int most = max < 1 ? 1 : max;
            while (hops.Count > most) hops.RemoveAt(0);
        }

        /// <summary>What to do about the next hop: wait, walk to its take-off, jump, or forget it.</summary>
        public static HopStep NextHop(in HopFacts facts, float reach, float maxAge)
        {
            if (facts.jumping) return HopStep.None;
            if (facts.age > maxAge) return HopStep.Drop;
            if (facts.distanceToLanding <= reach) return HopStep.Drop; // already across
            if (facts.distanceToTakeoff > reach) return HopStep.WalkToTakeoff;
            return facts.jumpReady ? HopStep.Jump : HopStep.None;
        }

        /// <summary>Why a player may not start following another, or null when they may.</summary>
        public static string StartRefusal(bool targetPresent, bool isSelf, bool sameSquad, bool followerAlive, float distance, float maxDistance, string targetName)
        {
            if (!targetPresent) return "That player is not here.";
            if (isSelf) return "Follow another member of your squad.";
            if (!sameSquad) return $"{targetName} is not in your squad.";
            if (!followerAlive) return "You cannot follow anyone while defeated.";
            if (distance > maxDistance) return $"{targetName} is too far away to follow.";
            return null;
        }

        public static FollowStep Evaluate(in FollowFacts facts, float keepDistance, float resumeDistance, float maxDistance, out FollowEnd end)
        {
            end = Ending(facts, maxDistance);
            if (end != FollowEnd.None) return FollowStep.End;
            if (facts.distance <= keepDistance) return FollowStep.Hold;
            // Between the two distances a follower keeps doing what it was doing, so it does not shuffle.
            if (facts.distance < resumeDistance && !facts.moving) return FollowStep.Hold;
            return FollowStep.Move;
        }

        private static FollowEnd Ending(in FollowFacts facts, float maxDistance)
        {
            if (!facts.followerAlive) return FollowEnd.Defeated;
            if (facts.followerOrdered) return FollowEnd.Ordered;
            if (facts.followerInCombat) return FollowEnd.Combat;
            if (!facts.targetPresent) return FollowEnd.Gone;
            if (!facts.sameSquad) return FollowEnd.LeftSquad;
            if (facts.distance > maxDistance) return FollowEnd.TooFar;
            return FollowEnd.None;
        }

        /// <summary>The line the follower is shown when following ends.</summary>
        /// <summary>The line for an ending with a reason of its own: "...could not follow their jump: Learn Jump first."</summary>
        public static string Describe(FollowEnd end, string targetName, string detail)
        {
            string line = Describe(end, targetName);
            if (line == null || string.IsNullOrWhiteSpace(detail)) return line;
            return end == FollowEnd.JumpRefused ? $"You stopped following {targetName}: you could not follow their jump. {detail.Trim()}" : line;
        }

        public static string Describe(FollowEnd end, string targetName)
        {
            switch (end)
            {
                case FollowEnd.Ordered: return $"You stopped following {targetName}.";
                case FollowEnd.Combat: return $"You stopped following {targetName}: you are in a fight.";
                case FollowEnd.LeftSquad: return $"You stopped following {targetName}: you are no longer in one squad.";
                case FollowEnd.Gone: return $"You stopped following {targetName}: they are no longer here.";
                case FollowEnd.TooFar: return $"You stopped following {targetName}: they are too far ahead.";
                case FollowEnd.Defeated: return $"You stopped following {targetName}.";
                case FollowEnd.JumpRefused: return $"You stopped following {targetName}: you could not follow their jump.";
                default: return null;
            }
        }
    }
}
