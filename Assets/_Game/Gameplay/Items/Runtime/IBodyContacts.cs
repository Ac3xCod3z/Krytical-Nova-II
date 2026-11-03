using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's Contacts, for the entity netcode (M8 8.4.2): the server body runs its owner's requests by NGO's ContactsService and
    /// sends its owner the list (as text: the list's type lives in Social), the prompt and its closing; the owner's view shows them. Here,
    /// below Social, as <see cref="IBodyChat"/>.
    /// </summary>
    public interface IBodyContacts
    {
        /// <summary>The server body: its owner's request (NGO's twelve RPC bodies, after the Social limit). <paramref name="a"/> a name or a character id.</summary>
        void RunContactOp(ContactOpKind op, string a, string b, bool flag);
        /// <summary>The own view: the list as the server sent it.</summary>
        void MirrorView(string viewText);
        void MirrorPrompt(string fromId, string fromName);
        void MirrorClosePrompt(string fromId);
    }

    /// <summary>Server: how a server body's Contacts reach its own player (NGO's owner ViewRpc, PromptRpc, ClosePromptRpc).</summary>
    public interface IContactsOwnerLink
    {
        void View(Component part, string viewText);
        void Prompt(Component part, string fromId, string fromName);
        void ClosePrompt(Component part, string fromId);
    }

    /// <summary>The server's <see cref="IContactsOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class ContactsOwnerLink
    {
        public static IContactsOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
