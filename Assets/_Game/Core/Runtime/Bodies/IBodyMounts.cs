using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>What a mount request asks the server's body to do (NGO's <c>PlayerMounts</c> SelectRpc and RideRpc; M8 8.4.8).</summary>
    public enum MountOpKind : byte
    {
        Select = 1,
        Ride = 2,
        /// <summary>The Mount Companion (day list item 19): the selected mount walks beside its owner, or goes home.</summary>
        Companion = 3,
        /// <summary>The companion's stance (the box byte carries it; phases 1-2: FREE only).</summary>
        Stance = 4,
    }

    /// <summary>
    /// A player body's mounts, for the entity netcode (M8 8.4.8): the server body decides everything by NGO's PlayerMounts (its part
    /// "mounts" is real), every view shows the server's selected mount, ride phase, growth and fed from the ghost, and the owner's view
    /// also gets the four boxes. Here in Core because Mounts does not use Items.
    /// </summary>
    public interface IBodyMounts
    {
        /// <summary>The selected box, ride phase, growth percent and fed the body shows (the server's own, or what a view was told).</summary>
        byte ShownSelected { get; }
        byte ShownPhase { get; }
        byte ShownGrowth { get; }
        bool ShownFed { get; }
        /// <summary>The getting on or off in play is the running kind, and a running getting-off ends running (NGO's netState.running / endsRunning).</summary>
        bool ShownRunning { get; }
        bool ShownEndsRunning { get; }
        /// <summary>Any view: what the server gives this player's mount (NGO's netState, read by everyone).</summary>
        void MirrorShown(byte selected, byte phase, byte growth, bool fed, bool running = false, bool endsRunning = true);
        /// <summary>The owner's view: the four boxes (NGO's netBook, read by the owner): owned bits, then each box's EXP and hunger.</summary>
        void MirrorBook(byte owned, uint xp1, uint xp2, uint xp3, uint xp4, uint hunger1, uint hunger2, uint hunger3, uint hunger4);
        /// <summary>The server body: a request of its owner (NGO's SelectRpc / RideRpc bodies, after the call limit).</summary>
        void RunMountOp(MountOpKind op, int box);
        /// <summary>Any view: the selected mount evolved to <paramref name="stage"/> (NGO's EvolvedRpc).</summary>
        void PlayEvolved(int stage);
    }

    /// <summary>Server: how a server body's mounts reach machines: the four boxes to its own player (false: its connection is not there yet), an evolution to every machine.</summary>
    public interface IMountOwnerLink
    {
        bool Book(Component part, byte owned, uint xp1, uint xp2, uint xp3, uint xp4, uint hunger1, uint hunger2, uint hunger3, uint hunger4);
        void Evolved(Component part, int stage);
    }

    /// <summary>The server's <see cref="IMountOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class MountOwnerLink
    {
        public static IMountOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }

    /// <summary>
    /// A server body's Mount Companion (day list item 19), read by the companion's ghost systems (Mounts.Entities): whether the selected
    /// mount is out beside its owner, what it looks like and its HP. The owner's view also gets the four boxes' HP and rest.
    /// </summary>
    public interface IBodyMountCompanion
    {
        /// <summary>Server: the selected mount walks beside its owner.</summary>
        bool CompanionOut { get; }
        int CompanionBox { get; }
        int CompanionStage { get; }
        /// <summary>Its HP left, permille of its max.</summary>
        int CompanionHpPermille { get; }
        byte CompanionStance { get; }
        /// <summary>Rises by one at each knock-out, so a view plays the knock-out rather than a plain going home.</summary>
        int CompanionKnockOuts { get; }
        /// <summary>The mount's own run speed at its growth (the follow's top speed before catch-up), m/s.</summary>
        float CompanionRunSpeed { get; }
        /// <summary>Server: a hit on its body (damage to its HP).</summary>
        void HurtCompanion(float damage);
        /// <summary>Server, phase 3 (day list 19): its DPS strikes and HEALER pulses so far this session (the Play proof reads them).</summary>
        int CompanionStrikes { get; }
        int CompanionPulses { get; }
        /// <summary>Server, phase 3: counts one DPS strike or HEALER pulse (the companion's server system calls it).</summary>
        void CountCompanionFight(bool strike);
        /// <summary>Server, phase 4: its TANK taunts so far this session, and counting one (the Play proof reads them).</summary>
        int CompanionTaunts { get; }
        void CountCompanionTaunt();
        /// <summary>The owner's view: out, stance and each box's HP left (permille) and rest left (seconds), from the server.</summary>
        void MirrorCompanion(byte isOut, byte stance, ushort hp1, ushort hp2, ushort hp3, ushort hp4, uint rest1, uint rest2, uint rest3, uint rest4);
    }

    /// <summary>Server: how a server body's companion boxes reach its own player (false: its connection is not there yet).</summary>
    public interface IMountCompanionLink
    {
        bool Companion(Component part, byte isOut, byte stance, ushort hp1, ushort hp2, ushort hp3, ushort hp4, uint rest1, uint rest2, uint rest3, uint rest4);
    }

    /// <summary>The server's <see cref="IMountCompanionLink"/>; null where there is no entity netcode server.</summary>
    public static class MountCompanionLink
    {
        public static IMountCompanionLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }

    /// <summary>
    /// Something that can be hit but stands for a player (the Mount Companion's body, day list item 19): who may hit it is decided as if it
    /// were <see cref="StandsFor"/> (CombatHostility), so a friend's area skill never hurts your companion and an enemy's does.
    /// </summary>
    public interface IStandIn
    {
        Transform StandsFor { get; }
    }
}
