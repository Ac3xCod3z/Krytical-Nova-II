using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>What a mount request asks the server's body to do (NGO's <c>PlayerMounts</c> SelectRpc and RideRpc; M8 8.4.8).</summary>
    public enum MountOpKind : byte
    {
        Select = 1,
        Ride = 2,
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
}
