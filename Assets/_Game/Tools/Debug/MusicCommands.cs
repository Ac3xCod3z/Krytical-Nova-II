using System.Text;
using KryticalNova.Audio;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for the fight music and its beat (Cadence, design 17). Like the GM commands they
    /// only run where the game is decided (host, server or offline). What plays is the zone's, so it changes for
    /// every player; the click is heard on this machine only.
    /// </summary>
    public static class MusicCommands
    {
        private const string NoMusic = "This scene has no fight music (no Music Conductor on its World Environment).";

        private static bool TryGet(out MusicConductor conductor, out string error)
        {
            conductor = null;
            // Any player will do: this only checks that we are the host, the server or offline.
            if (!GmTarget.TryGetPlayer(out _, out error)) return false;
            conductor = MusicConductor.Instance;
            if (conductor == null || conductor.Music == null) { error = NoMusic; return false; }
            return true;
        }

        [Command("gm.music", "The zone's fight music: its tracks, which one the shared clock has on, and what this machine is playing. " +
                             "It is heard only in a fight (gm.combat hold on starts one).")]
        private static string Music()
        {
            if (!TryGet(out MusicConductor conductor, out string error)) return error;
            var text = new StringBuilder();
            MusicTrack[] tracks = conductor.Music.combat;
            bool on = conductor.TryLocate(conductor.Now, out MusicTrack now, out int index, out double position);
            for (int i = 0; i < tracks.Length; i++)
            {
                MusicTrack track = tracks[i];
                if (track == null) continue;
                text.AppendLine($"{(on && i == index ? ">" : " ")} {i + 1}  {track.Title}   {track.beatsPerMinute:0.###} beats a minute, {track.Length:0} s, " +
                                $"a beat at {track.firstBeatSeconds:0.000} s, bar starts on beat {track.barBeat}");
            }
            MusicState state = conductor.State;
            text.AppendLine(on ? $"Shared clock: track {index + 1}, {position:0.0} s in." : "Shared clock: between two tracks.");
            text.AppendLine(state.Forced >= 0 ? $"One track over and over (gm.music.track round to stop)." : "The tracks play in turn.");
            if (!Mathf.Approximately(state.Nudge, 0f)) text.AppendLine($"Every beat moved by {state.Nudge * 1000f:+0;-0} ms (gm.music.nudge).");
            text.AppendLine($"This machine: {CombatMusicPlayer.Report}");
            text.Append($"Click {(CombatMusicPlayer.Click ? "on" : "off")}. Beat timing offset (Settings) {CombatBeat.OutputDelaySeconds * 1000.0:+0;-0;0} ms.");
            return text.ToString();
        }

        [Command("gm.music.track", "Play one of the zone's fight tracks over and over from its start, for everyone: gm.music.track <number|part of its name>. " +
                                   "gm.music.track round plays them in turn again.")]
        private static string Track(string which)
        {
            if (!TryGet(out MusicConductor conductor, out string error)) return error;
            which = (which ?? string.Empty).Trim();
            if (which.ToLowerInvariant() == "round")
                return conductor.ServerForce(-1) ? "The tracks play in turn again." : "Could not change the music.";

            MusicTrack[] tracks = conductor.Music.combat;
            int index = -1;
            if (int.TryParse(which, out int number)) index = number - 1;
            else
                for (int i = 0; i < tracks.Length && index < 0; i++)
                    if (tracks[i] != null && tracks[i].Title.ToLowerInvariant().Contains(which.ToLowerInvariant())) index = i;
            if (index < 0 || index >= tracks.Length || tracks[index] == null) return "No such track. gm.music lists them.";
            return conductor.ServerForce(index) ? $"Playing {tracks[index].Title} over and over." : "Could not change the music.";
        }

        [Command("gm.music.click", "A click on every beat of the fight music, higher on the first beat of the bar, on this machine only: gm.music.click [on|off]. " +
                                   "If the click is not on the beat, the track's numbers are wrong (try gm.music.nudge).")]
        private static string Click(string state = "")
        {
            if (!TryGet(out _, out string error)) return error;
            state = (state ?? string.Empty).Trim().ToLowerInvariant();
            if (state.Length > 0 && state != "on" && state != "off") return "Use gm.music.click on or off.";
            CombatMusicPlayer.Click = state.Length == 0 ? !CombatMusicPlayer.Click : state == "on";
            return CombatMusicPlayer.Click ? "Click on (heard while the fight music plays)." : "Click off.";
        }

        [Command("gm.music.nudge", "Move every beat of what is playing by so many milliseconds, for every player: gm.music.nudge <ms> (negative = earlier, 0 = as baked). " +
                                   "A way to find the right value by ear; it is not kept. Tell Claude the value that sounds right and it is baked into the track.")]
        private static string Nudge(float milliseconds)
        {
            if (!TryGet(out MusicConductor conductor, out string error)) return error;
            if (Mathf.Abs(milliseconds) > 500f) return "Give a number of milliseconds between -500 and 500.";
            return conductor.ServerNudge(milliseconds / 1000f) ? $"Every beat moved by {milliseconds:+0;-0;0} ms." : "Could not move the beat.";
        }
    }
}
