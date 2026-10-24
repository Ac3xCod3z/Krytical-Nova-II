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
            (AudioChannel.Voice, "Voices"),
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
            SettingsRows.Toggle(page, "COMBO counter when skills land on the beat",
                () => AudioPreferences.ComboMeter, value => AudioPreferences.ComboMeter = value);
            SettingsRows.Toggle(page, "Beat track above the Hotbar in a fight",
                () => AudioPreferences.BeatTrack, value => AudioPreferences.BeatTrack = value);
            SettingsRows.Toggle(page, "EARLY / LATE after a press just off the beat",
                () => AudioPreferences.BeatHint, value => AudioPreferences.BeatHint = value);
            SettingsRows.Toggle(page, "Bigger mark on the first beat of a bar",
                () => AudioPreferences.BeatCountIn, value => AudioPreferences.BeatCountIn = value);
            SettingsRows.Toggle(page, "Metronome tick on every beat",
                () => AudioPreferences.Metronome, value => AudioPreferences.Metronome = value);
        }
    }
}
