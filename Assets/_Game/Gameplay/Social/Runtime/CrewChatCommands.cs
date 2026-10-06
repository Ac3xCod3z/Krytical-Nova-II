using KryticalNova.Player;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The chat box's Crew commands: /cinvite [name], /caccept, /cdecline, /cleave, /ckick name. ("/crew message"
    /// and "/g message" talk on the Crew channel and live in <see cref="ChatCommands"/>.) They act for the local
    /// player; the server decides. Founding and the rest are in the Crew window [G].
    /// </summary>
    public static class CrewChatCommands
    {
        private const string Offline = "Crews need an online session.";

        // ChatCommands forgets its commands at SubsystemRegistration (no domain reload); this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            ChatCommands.Register("cinvite", Invite);
            ChatCommands.Register("caccept", _ => Answer(true));
            ChatCommands.Register("cdecline", _ => Answer(false));
            ChatCommands.Register("cleave", _ => Leave());
            ChatCommands.Register("ckick", Kick);
        }

        private static bool TryGetMine(out PlayerCrew mine)
        {
            mine = PlayerCrew.Local;
            return mine != null && mine.IsSpawned;
        }

        private static string Invite(string name)
        {
            if (!TryGetMine(out PlayerCrew mine)) return Offline;
            if (name.Length > 0)
            {
                mine.Invite(name);
                return null; // the server answers
            }
            Transform selected = mine.TryGetComponent(out PointAndClickPlayer controls) ? controls.SelectedTarget : null;
            PlayerCrew target = selected != null ? selected.GetComponentInParent<PlayerCrew>() : null;
            if (target == null || target == mine) return "Select a player first, or use /cinvite <name>.";
            mine.Invite(target);
            return null;
        }

        private static string Answer(bool accept)
        {
            if (!TryGetMine(out PlayerCrew mine)) return Offline;
            mine.Respond(accept);
            return null;
        }

        private static string Leave()
        {
            if (!TryGetMine(out PlayerCrew mine)) return Offline;
            mine.Leave();
            return null;
        }

        private static string Kick(string name)
        {
            if (!TryGetMine(out PlayerCrew mine)) return Offline;
            if (name.Length == 0) return "Use /ckick <name>.";
            if (!mine.TryFindMember(name, out CrewMemberView member)) return $"No Crew member is named '{name}'.";
            mine.Expel(member.id.ToString());
            return null;
        }
    }
}
