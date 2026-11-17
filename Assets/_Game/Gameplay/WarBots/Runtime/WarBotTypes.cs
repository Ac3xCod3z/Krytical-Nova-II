using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.WarBots
{
    /// <summary>A bot's personality, rolled once (owner-picked addition): how it chooses its fights.</summary>
    public enum WarBotStyle : byte
    {
        /// <summary>Goes in, stays in longer, ignores "a fight it would clearly lose".</summary>
        Aggressive = 0,
        /// <summary>Runs away earlier and keeps out of lost fights.</summary>
        Careful = 1,
        /// <summary>Buffs first, helps teammates under attack first.</summary>
        Support = 2,
        /// <summary>Hunts isolated enemies away from the crowd.</summary>
        LoneWolf = 3,
    }

    /// <summary>How a bot plays its weapon class (owner, 16 Nov: support / tank / damage; there are no healers).</summary>
    public enum WarBotRole : byte
    {
        /// <summary>An offense class: closes in and hits.</summary>
        Damage = 0,
        /// <summary>A defense class: stays with its side and takes the enemies attacking it.</summary>
        Tank = 1,
        /// <summary>A ranged class: keeps its distance and backs off (kites).</summary>
        Ranged = 2,
    }

    /// <summary>What a bot was rolled when it came in: everything that makes it an individual.</summary>
    public struct WarBotProfile
    {
        public WarBotStyle Style;
        public WarBotRole Role;
        /// <summary>0-1: target choices, the Cadence beat, slips.</summary>
        public float Skill;
        /// <summary>Its own fastest and slowest reaction, seconds.</summary>
        public float ReactFast, ReactSlow;
        public int Level;
        /// <summary>Its gear score (<see cref="WarBotRules.GearScore"/>): the rarity and Temper it wears.</summary>
        public int GearScore;
        public bool HasMount;
        /// <summary>Its own random seed (rolls during the war).</summary>
        public int Seed;
    }

    /// <summary>One player of the war a bot may see: a real player or another bot, of any side.</summary>
    public struct WarBotCombatant
    {
        public int GhostId;
        public FactionId Side;
        public Vector3 Position;
        public bool Alive;
        /// <summary>Health share 0-1.</summary>
        public float Health;
        /// <summary>The ghost this one is attacking now (0 none).</summary>
        public int TargetGhostId;
    }

    /// <summary>A party order of the leader (owner-picked: follow / hold / attack).</summary>
    public enum WarBotOrder : byte { None = 0, Follow = 1, Hold = 2, Attack = 3 }

    /// <summary>What a pass knows about one bot.</summary>
    public struct WarBotView
    {
        public int GhostId;
        public FactionId Side;
        public Vector3 Position;
        public bool Alive;
        public float Health;
        public WarBotProfile Profile;
        /// <summary>The ghost its attack is on now (0 none) and whether that one is still a fair target (alive, an enemy, in sight).</summary>
        public int TargetGhostId;
        public bool TargetValid;
        /// <summary>A server walk is under way.</summary>
        public bool Walking;
        /// <summary>It ran from a swarm and is not back above the return health yet.</summary>
        public bool Retreating;
        /// <summary>An HP Stim is in its Pack and off cooldown.</summary>
        public bool StimReady;
        /// <summary>A buff skill it does not have on is ready to cast.</summary>
        public bool BuffReady;
        /// <summary>Its next attack skill may go now (its own gap is over, one is off cooldown).</summary>
        public bool SkillReady;
        /// <summary>Where its side gathers (the Rally Point landing), and where to fall back to.</summary>
        public Vector3 Rally;
        /// <summary>In a party with a leader that is not itself: the leader's spot, target and order.</summary>
        public bool HasLeader;
        public Vector3 LeaderPosition;
        public int LeaderTargetGhostId;
        public WarBotOrder Order;
        public Vector3 HoldAt;
    }

    /// <summary>What the brain does for one bot this pass.</summary>
    public enum WarBotAct : byte
    {
        /// <summary>Nothing new (dead, or the fight under way goes on).</summary>
        Wait = 0,
        /// <summary>Basic attack on <see cref="WarBotPlan.TargetGhostId"/> (the server walks it in).</summary>
        Attack,
        /// <summary>An attack skill at the target (the Cadence combo picks which).</summary>
        Skill,
        /// <summary>Put on a buff.</summary>
        Buff,
        /// <summary>Use an HP Stim.</summary>
        Stim,
        /// <summary>Walk to <see cref="WarBotPlan.MoveTo"/>: kite, follow, regroup, back to the side.</summary>
        Move,
        /// <summary>Run away from a swarm to <see cref="WarBotPlan.MoveTo"/>.</summary>
        Retreat,
    }

    public struct WarBotPlan
    {
        public WarBotAct Act;
        public int TargetGhostId;
        public Vector3 MoveTo;
        /// <summary>The reason, for the log and the tests ("kite", "follow", "help", "isolated", ...).</summary>
        public string Why;
    }
}
