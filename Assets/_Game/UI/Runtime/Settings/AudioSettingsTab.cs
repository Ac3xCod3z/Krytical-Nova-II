using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>The Audio page of Settings: one slider per mixer group.</summary>
    public sealed class AudioSettingsTab
    {
        private static readonly (AudioChannel Channel, string Label)[] Channels =
        {
            (AudioChannel.Master, "Master"),
            (AudioChannel.Music, "Music"),
            (AudioChannel.Effects, "Effects"),
            (AudioChannel.Ambience, "Ambience"),
            (AudioChannel.Interface, "Interface"),
        };

        public void Build(VisualElement page)
        {
            page.Clear();
            SettingsRows.Header(page, "VOLUME");
            foreach (var entry in Channels)
            {
                AudioChannel channel = entry.Channel;
                SettingsRows.Slider(page, entry.Label, 0f, 1f, () => AudioPreferences.Get(channel),
                    value => AudioPreferences.Set(channel, value), out VisualElement row);
                // A channel the mixer has no group for is shown but cannot be moved.
                if (AudioPreferences.Has(channel)) continue;
                row.AddToClassList("unavailable");
                row.SetEnabled(false);
                row.tooltip = "This mixer has no group for this channel yet.";
            }
            SettingsRows.Toggle(page, "Mute while the game is in the background",
                () => AudioPreferences.MuteInBackground, value => AudioPreferences.MuteInBackground = value);
        }
    }
}
