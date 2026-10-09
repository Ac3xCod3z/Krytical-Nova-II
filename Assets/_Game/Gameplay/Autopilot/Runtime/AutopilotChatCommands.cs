using KryticalNova.Social;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>Chat commands for Autopilot and Magnet: /auto and /magnet, each with on, off or nothing to flip it.</summary>
    public static class AutopilotChatCommands
    {
        private const string NoPlayer = "You are not in the world yet.";

        // ChatCommands forgets its commands at SubsystemRegistration (no domain reload); this runs just after.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            ChatCommands.Register("auto", Auto);
            ChatCommands.Register("autopilot", Auto);
            ChatCommands.Register("magnet", Magnet);
        }

        private static bool Wanted(string arguments, bool current)
        {
            string text = (arguments ?? string.Empty).Trim().ToLowerInvariant();
            return text == "on" || (text != "off" && !current);
        }

        private static string Auto(string arguments)
        {
            PlayerAutopilot autopilot = PlayerAutopilot.Local;
            if (autopilot == null) return NoPlayer;
            bool on = Wanted(arguments, autopilot.IsOn);
            autopilot.RequestSetOn(on);
            // The server answers a request to switch it on; here is only what is known already.
            return on ? $"Autopilot time left: {PlayerAutopilot.FormatTime(autopilot.SecondsLeft)}." : null;
        }

        private static string Magnet(string arguments)
        {
            PlayerAutopilot autopilot = PlayerAutopilot.Local;
            if (autopilot == null) return NoPlayer;
            AutopilotPreferences preferences = autopilot.Preferences;
            preferences.magnetOn = Wanted(arguments, preferences.magnetOn);
            autopilot.SavePreferences(preferences);
            return preferences.magnetOn ? "Magnet on." : "Magnet off.";
        }
    }
}
