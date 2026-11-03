using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's Follow, for the entity netcode (M8 8.4.3b): the server body starts its owner's Follow by NGO's PlayerFollow rules and
    /// walks it through its ghost; the player's own clicks stop it (NGO's MoveOrdered); the owner's view shows whom it follows. Here, below
    /// Social, as <see cref="IBodyParty"/>.
    /// </summary>
    public interface IBodyFollow
    {
        /// <summary>The server body: follow the Squad member of that name (NGO's FollowByNameRpc body, after the Move limit).</summary>
        void RunFollow(string memberName);
        /// <summary>The server body: its player ordered a move of their own (a click or a jump), which ends a Follow as NGO's MoveOrdered.</summary>
        void OwnMoveOrdered();
        /// <summary>The own view: the member followed, or empty (NGO's owner FollowingRpc).</summary>
        void MirrorFollowing(string memberName);
    }

    /// <summary>Server: how a server body's Follow reaches its own player (NGO's owner FollowingRpc).</summary>
    public interface IFollowOwnerLink
    {
        void Following(Component part, string memberName);
    }

    /// <summary>The server's <see cref="IFollowOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class FollowOwnerLink
    {
        public static IFollowOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
