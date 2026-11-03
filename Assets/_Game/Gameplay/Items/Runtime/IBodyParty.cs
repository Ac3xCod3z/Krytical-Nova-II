using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>One row of a Squad roster as the entity netcode sends it (NGO's <c>PartyMember</c>; 0 = the player it is sent to).</summary>
    public struct BodyPartyMember
    {
        public ulong id;
        public string name;
        public int level;
    }

    /// <summary>
    /// A player body's Squad, for the entity netcode (M8 8.4.3): the server body runs its owner's requests by NGO's PlayerParty rules and
    /// sends its owner the roster and the invitation; every view shows the Squad id (from the ghost) and the own view the roster. Here, below
    /// Social, as <see cref="IBodyChat"/>.
    /// </summary>
    public interface IBodyParty
    {
        /// <summary>0 when in no Squad; the server body's own, or what a view was told.</summary>
        ulong PartyId { get; }
        /// <summary>The server body: its owner's request (NGO's six RPC bodies, after the Social limit).</summary>
        void RunPartyOp(PartyOpKind op, string name, bool flag);
        /// <summary>Any view: the Squad id the server gives this player (NGO's netPartyId, read by everyone).</summary>
        void MirrorPartyId(ulong partyId);
        /// <summary>The own view: the roster, leader first (NGO's owner RosterRpc).</summary>
        void MirrorRoster(BodyPartyMember[] members);
        void MirrorInvite(string inviterName, float seconds);
        void MirrorInviteClosed();
    }

    /// <summary>Server: how a server body's Squad reaches its own player (NGO's owner RosterRpc, InvitePromptRpc, InviteClosedRpc).</summary>
    public interface IPartyOwnerLink
    {
        void Roster(Component part, BodyPartyMember[] members);
        void Invite(Component part, string inviterName, float seconds);
        void InviteClosed(Component part);
    }

    /// <summary>The server's <see cref="IPartyOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class PartyOwnerLink
    {
        public static IPartyOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
