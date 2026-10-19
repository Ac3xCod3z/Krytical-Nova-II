using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>
    /// The rules of a Recall Tag, kept apart from the component so they can be tested: when one may be used, and
    /// what stops a trip once it has begun.
    /// </summary>
    public static class RecallRules
    {
        public const string ReasonInFight = "You cannot recall in a fight.";
        public const string ReasonCallout = "You cannot recall during a Callout.";
        public const string ReasonInHall = "Leave your Hall first.";
        public const string ReasonAlready = "You are already on your way back.";
        public const string ReasonNoTown = "There is no town to go back to here.";

        public const string StopMoved = "You moved: the Recall is off.";
        public const string StopHit = "You were hit: the Recall is off.";
        public const string StopFight = "A fight started: the Recall is off.";
        public const string StopDied = "The Recall is off.";

        /// <summary>Why a Recall Tag cannot be used now, or null when it can. The first reason that applies wins.</summary>
        public static string BlockedReason(bool recalling, bool inFight, bool duelling, bool inHall, bool townKnown)
        {
            if (recalling) return ReasonAlready;
            if (duelling) return ReasonCallout;
            if (inFight) return ReasonInFight;
            if (inHall) return ReasonInHall;
            if (!townKnown) return ReasonNoTown;
            return null;
        }

        /// <summary>True when the character has drifted far enough from where the channel began to call it a move.</summary>
        public static bool Moved(Vector3 start, Vector3 now, float tolerance)
        {
            Vector3 offset = now - start;
            offset.y = 0f; // a step up or down the ground is not a walk
            return offset.sqrMagnitude > tolerance * tolerance;
        }

        /// <summary>Seconds left of a channel, never below zero.</summary>
        public static float SecondsLeft(float now, float endsAt) => Mathf.Max(0f, endsAt - now);
    }
}
