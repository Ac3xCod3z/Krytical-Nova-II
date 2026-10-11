using KryticalNova.Core;
using KryticalNova.Player;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The chat box's Sponsor command: "/sponsor name" asks that player (or the selected one, with no name given
    /// and a player selected); "/sponsor" alone opens the Sponsor window. It acts for the local player; the server
    /// decides. Who sponsors whom is settled by the two levels.
    /// </summary>
    public static class SponsorChatCommands
    {
        private const string Offline = "Sponsoring needs an online session.";

        // ChatCommands forgets its commands at SubsystemRegistration (no domain reload); this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register() => ChatCommands.Register("sponsor", Sponsor);

        private static string Sponsor(string name)
        {
            PlayerSponsor mine = PlayerSponsor.Local;
            if (mine == null || !mine.IsSpawned) return Offline;
            if (name.Length > 0)
            {
                mine.Request(name);
                return null; // the server answers
            }
            Transform selected = mine.TryGetComponent(out PointAndClickPlayer controls) ? controls.SelectedTarget : null;
            PlayerSponsor target = selected != null ? selected.GetComponentInParent<PlayerSponsor>() : null;
            if (target != null && target != mine)
            {
                mine.Request(target);
                return null;
            }
            // Nobody named and nobody selected: the window, where the links are.
            IServiceWindowSink windows = ClientPresentationRegistry.Services;
            return windows != null && windows.OpenService(ServiceIds.Sponsor) ? null : "Use /sponsor <name>, or select a player first.";
        }
    }
}
