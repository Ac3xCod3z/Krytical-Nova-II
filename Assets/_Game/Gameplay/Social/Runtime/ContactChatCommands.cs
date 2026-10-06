using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The chat box's Contacts commands: /contact add name, /contact remove name, /block name, /unblock name.
    /// They act for the local player; the server decides. Requests are answered in the Contacts window [F].
    /// </summary>
    public static class ContactChatCommands
    {
        private const string Offline = "Contacts need an online session.";
        private const string Usage = "Use /contact add <name> or /contact remove <name>.";

        // ChatCommands forgets its commands at SubsystemRegistration (no domain reload); this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            ChatCommands.Register("contact", Contact);
            ChatCommands.Register("block", name => Named(name, "Use /block <name>.", (mine, who) => mine.BlockByName(who)));
            ChatCommands.Register("unblock", name => Named(name, "Use /unblock <name>.", (mine, who) => mine.UnblockByName(who)));
        }

        private static string Contact(string arguments)
        {
            int space = arguments.IndexOf(' ');
            string verb = (space < 0 ? arguments : arguments.Substring(0, space)).ToLowerInvariant();
            string name = space < 0 ? string.Empty : arguments.Substring(space + 1).Trim();
            switch (verb)
            {
                case "add": return Named(name, Usage, (mine, who) => mine.Add(who));
                case "remove": return Named(name, Usage, (mine, who) => mine.RemoveByName(who));
                default: return Usage;
            }
        }

        private static string Named(string name, string usage, System.Action<PlayerContacts, string> act)
        {
            PlayerContacts mine = PlayerContacts.Local;
            if (mine == null || !mine.IsSpawned) return Offline;
            if (name.Length == 0) return usage;
            act(mine, name);
            return null; // the server answers
        }
    }
}
