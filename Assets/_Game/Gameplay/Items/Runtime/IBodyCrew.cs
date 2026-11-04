using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>What everyone sees of a player's Crew (NGO's <c>CrewTag</c>, here as plain fields): an empty name = no Crew.</summary>
    public struct BodyCrewTag
    {
        public string crew, title;
        public byte rank, symbol, colour, tagColour, tagBackground;
    }

    /// <summary>
    /// A player body's Crew, for the entity netcode (M8 8.4.5): the server body runs its owner's requests by NGO's CrewService (founding with
    /// its Creds, ledger line and SaveTogether included) and sends its owner the Crew window (as text: its types live in Social) and the
    /// invitation; every view shows the tag the server gives the player. Here, below Social, as <see cref="IBodyContacts"/>.
    /// </summary>
    public interface IBodyCrew
    {
        /// <summary>The server body: its owner's request (NGO's twelve RPC bodies, after the Social limit).</summary>
        void RunCrewOp(CrewOpKind op, string a, string b, byte b0, byte b1, byte b2, byte b3, bool flag);
        /// <summary>The tag this body shows now (the server's own, or what a view was told).</summary>
        BodyCrewTag ShownTag { get; }
        /// <summary>Any view: the tag the server gives this player (NGO's netTag, read by everyone).</summary>
        void MirrorTag(in BodyCrewTag tag);
        /// <summary>The own view: the Crew window as the server sent it (NGO's owner ViewRpc).</summary>
        void MirrorView(string viewText);
        void MirrorPrompt(string inviterName, string crewName, float seconds);
        void MirrorClosePrompt();
    }

    /// <summary>Server: how a server body's Crew reaches its own player (NGO's owner ViewRpc, PromptRpc, ClosePromptRpc).</summary>
    public interface ICrewOwnerLink
    {
        void View(Component part, string viewText);
        void Prompt(Component part, string inviterName, string crewName, float seconds);
        void ClosePrompt(Component part);
    }

    /// <summary>The server's <see cref="ICrewOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class CrewOwnerLink
    {
        public static ICrewOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
