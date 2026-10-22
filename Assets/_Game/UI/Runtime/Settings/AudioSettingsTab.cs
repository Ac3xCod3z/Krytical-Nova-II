using UnityEngine;
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

            // Cadence: the beat of the fight music, moved to where this player hears it.
            SettingsRows.Header(page, "FIGHT MUSIC");
            SettingsRows.Slider(page, "Beat timing offset", -AudioPreferences.BeatOffsetLimit, AudioPreferences.BeatOffsetLimit,
                () => AudioPreferences.BeatOffset, value => AudioPreferences.BeatOffset = value, out VisualElement beat,
                amount => (amount > 0f ? "+" : "") + Mathf.RoundToInt(amount) + " ms");
            beat.tooltip = "If the beat seems to come before you hear it, move this later (+); if after, earlier (-). Wireless headsets often need +100 or more.";
            SettingsRows.Toggle(page, "Beat Ring round your pet in a fight",
                () => AudioPreferences.BeatRing, value => AudioPreferences.BeatRing = value);
        }
    }
}
