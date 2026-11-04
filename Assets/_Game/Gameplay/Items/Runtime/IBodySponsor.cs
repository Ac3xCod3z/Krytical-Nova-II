using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's Sponsor / Recruit links, for the entity netcode (M8 8.4.4): the server body runs its owner's requests by NGO's
    /// SponsorService, earns its points and its bonus there, and sends its owner the links (as text: the view's type lives in Social), the
    /// points, the bonus and the request prompt; the owner's view shows them. Here, below Social, as <see cref="IBodyContacts"/>.
    /// </summary>
    public interface IBodySponsor
    {
        /// <summary>The server body: its owner's request (NGO's RequestByNameRpc / RespondRpc / EndRpc bodies, after the Social limit).</summary>
        void RunSponsorOp(SponsorOpKind op, string a, bool flag);
        /// <summary>The own view: the links, the points and the bonus as the server sent them (NGO's owner ViewRpc and netBoostOn).</summary>
        void MirrorView(string viewText, int points, bool boostOn);
        void MirrorPrompt(string fromName, bool theyWouldSponsor, float seconds);
        void MirrorClosePrompt();
    }

    /// <summary>Server: how a server body's Sponsor links reach its own player (NGO's owner ViewRpc, PromptRpc, ClosePromptRpc).</summary>
    public interface ISponsorOwnerLink
    {
        void View(Component part, string viewText, int points, bool boostOn);
        void Prompt(Component part, string fromName, bool theyWouldSponsor, float seconds);
        void ClosePrompt(Component part);
    }

    /// <summary>The server's <see cref="ISponsorOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class SponsorOwnerLink
    {
        public static ISponsorOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
