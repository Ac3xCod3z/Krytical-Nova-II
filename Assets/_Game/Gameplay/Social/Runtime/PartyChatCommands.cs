using KryticalNova.Player;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The chat box's party commands: /invite [name], /join, /refuse, /leave, /kick name, /promote name.
    /// They act for the local player; "/p message" (party chat) is a channel and lives in <see cref="ChatCommands"/>.
    /// </summary>
    public static class PartyChatCommands
    {
        private const string Offline = "Squads need an online session with another player.";

        // ChatCommands forgets its commands at SubsystemRegistration (no domain reload); this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            ChatCommands.Register("invite", Invite);
            ChatCommands.Register("join", _ => Answer(true));
            ChatCommands.Register("refuse", _ => Answer(false));
            ChatCommands.Register("leave", _ => Leave());
            ChatCommands.Register("kick", Kick);
            ChatCommands.Register("promote", Promote);
        }

        private static string Invite(string name)
        {
            if (!TryGetMine(out PlayerParty mine)) return Offline;
            if (name.Length > 0)
            {
                mine.Invite(name);
                return null; // the server answers
            }

            Transform selected = mine.TryGetComponent(out PointAndClickPlayer controls) ? controls.SelectedTarget : null;
            PlayerParty target = selected != null ? selected.GetComponentInParent<PlayerParty>() : null;
            if (target == null || target == mine) return "Select a player first, or use /invite <name>.";
            mine.Invite(target);
            return null;
        }

        private static string Answer(bool accept)
        {
            if (!TryGetMine(out PlayerParty mine)) return Offline;
            mine.Respond(accept);
            return null;
        }

        private static string Leave()
        {
            if (!TryGetMine(out PlayerParty mine)) return Offline;
            mine.Leave();
            return null;
        }

        private static string Kick(string name)
        {
            if (!TryGetMine(out PlayerParty mine)) return Offline;
            if (name.Length == 0) return "Use /kick <name>.";
            mine.Kick(name);
            return null;
        }

        private static string Promote(string name)
        {
            if (!TryGetMine(out PlayerParty mine)) return Offline;
            if (name.Length == 0) return "Use /promote <name>.";
            mine.Promote(name);
            return null;
        }

        private static bool TryGetMine(out PlayerParty mine)
        {
            mine = PlayerParty.Local;
            return mine != null && mine.IsSpawned;
        }
    }
}
