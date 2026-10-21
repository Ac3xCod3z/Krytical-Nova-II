using KryticalNova.Combat;
using UnityEngine;
using KryticalNova.Core;

namespace KryticalNova.Audio
{
    /// <summary>
    /// Plays the zone's fight music on this machine, and only while this machine's player is in a fight: the track
    /// <see cref="MusicConductor"/> says is on comes in at the place it has reached, and goes out when the fight is
    /// over. Out of a fight it plays nothing and costs nothing. It also keeps <see cref="CombatBeat"/>: the beat as
    /// this player hears it, read from the audio clock, which is the clock the sound itself is played on.
    /// Client-only: a dedicated server has no ears.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    public sealed class CombatMusicPlayer : MonoBehaviour
    {
        // The music is booked this far ahead on the audio clock, so that it starts on the sample asked for.
        private const double StartLead = 0.15;
        // The sound may sit this far from where the shared clock says it should be before it is set right again...
        private const double DriftAllowed = 0.045;
        // ...and only when it has been that far off for this long: one late frame is not drift.
        private const float DriftPatience = 0.75f;
        private const double ClickLead = 0.2;
        // Seconds a second the kept lead of the audio clock over real time may sink: far less than time itself moves.
        private const double LeadSink = 0.002;

        [SerializeField] private MusicConductor conductor;
        [SerializeField] private AudioSource source;
        [Tooltip("Plays the GM's click on every beat (gm.music.click). Not heard in normal play.")]
        [SerializeField] private AudioSource clickSource;

        private float live;
        private MusicTrack playing;
        private double dspStart;
        private double positionAtStart;
        private float adrift;
        private double dspLead;
        private double leadReadAt;
        private bool leadKnown;
        private AudioClip click;
        private AudioClip barClick;
        private long clickBooked = long.MinValue;

        /// <summary>GM: a click on every beat of the fight music, to check a track's beat by ear.</summary>
        public static bool Click { get; set; }

        /// <summary>GM: what the sound is doing, in one line.</summary>
        public static string Report { get; private set; } = "No fight music in this scene.";

        /// <summary>How far in the fight music is on this machine, 0 (out of a fight) to 1. The walking-about music
        /// (<see cref="ExploreMusicPlayer"/>) steps aside by as much.</summary>
        public static float Live { get; private set; }

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Click = false;
            Report = "No fight music in this scene.";
            Live = 0f;
        }

        private void Awake()
        {
            if (Application.isBatchMode) enabled = false;
            if (conductor == null) conductor = GetComponentInParent<MusicConductor>();
        }

        private void OnDisable()
        {
            Silence();
            live = 0f;
            Live = 0f;
            CombatBeat.Rest(0f);
        }

        private void Update()
        {
            ZoneMusic music = conductor != null ? conductor.Music : null;
            if (music == null || source == null)
            {
                Live = 0f;
                CombatBeat.Rest(0f);
                return;
            }

            bool fighting = LocalPlayerFights();
            float seconds = fighting ? music.fadeInSeconds : music.fadeOutSeconds;
            live = Mathf.MoveTowards(live, fighting ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds));
            Live = live;
            if (live <= 0f)
            {
                Silence();
                CombatBeat.Rest(0f);
                Report = "Out of a fight: silent.";
                return;
            }

            double dsp = SmoothDsp();
            if (!conductor.TryLocate(conductor.Now, out MusicTrack track, out _, out double position) || track.clip == null)
            {
                // Between two tracks: what was playing runs out by itself, and there is no beat.
                if (playing != null && !source.isPlaying && dsp > dspStart) playing = null;
                CombatBeat.Rest(live);
                Report = "Between two tracks.";
                return;
            }

            if (track != playing) StartAt(track, position, dsp);
            double heard = positionAtStart + (dsp - dspStart);
            double off = position - heard;
            adrift = System.Math.Abs(off) > DriftAllowed ? adrift + Time.unscaledDeltaTime : 0f;
            if (adrift > DriftPatience)
            {
                StartAt(track, position, dsp);
                heard = positionAtStart + (dsp - dspStart);
            }

            source.volume = track.volume * live;
            double firstBeat = conductor.FirstBeatOf(track);
            CombatBeat.Set(live, track, heard, firstBeat, position);
            BookClick(track, firstBeat, heard, dsp);
            Report = $"{track.Title}: {track.beatsPerMinute:0.###} beats a minute, {heard:0.0} s in, beat {CombatBeat.Index}, " +
                     $"sound {off * 1000.0:+0;-0} ms from the shared clock, volume {live:0.00}.";
        }

        private bool LocalPlayerFights()
        {
            PlayerCombatState local = LocalPlayer.Get<PlayerCombatState>();
            ZoneMusic music = conductor != null ? conductor.Music : null;
            return local != null && local.InFightFor(music != null && music.calloutsOnly);
        }

        /// <summary>Books the track on the audio clock so that it sounds, to the sample, where the shared clock has it.</summary>
        private void StartAt(MusicTrack track, double position, double dsp)
        {
            adrift = 0f;
            clickBooked = long.MinValue;
            double from = position + StartLead;
            if (from >= track.Length - 0.1)
            {
                // Too near the end to be worth starting: the next track is seconds away.
                source.Stop();
                playing = track;
                dspStart = dsp + StartLead;
                positionAtStart = from;
                return;
            }
            source.Stop();
            source.clip = track.clip;
            source.loop = false;
            source.volume = track.volume * live;
            source.timeSamples = Mathf.Clamp((int)System.Math.Round(from * track.clip.frequency), 0, track.clip.samples - 1);
            dspStart = dsp + StartLead;
            positionAtStart = (double)source.timeSamples / track.clip.frequency;
            source.PlayScheduled(dspStart);
            playing = track;
        }

        private void Silence()
        {
            if (playing == null) return;
            if (source != null) source.Stop();
            if (clickSource != null) clickSource.Stop();
            playing = null;
            clickBooked = long.MinValue;
        }

        /// <summary>
        /// The audio clock moves a whole buffer at a time (about 20 ms) and is exact only at the moment it moves. Real
        /// time fills in between: the audio clock is taken to be real time plus the largest lead over real time it has
        /// shown lately, which is its lead at the steps themselves. Read this way the beat is smooth every frame and
        /// never runs backwards.
        /// </summary>
        private double SmoothDsp()
        {
            double real = Time.realtimeSinceStartupAsDouble;
            double lead = AudioSettings.dspTime - real;
            // Between two steps the lead only falls behind. The kept one sinks slowly, so that a real drift between the
            // two clocks is still followed.
            dspLead = leadKnown ? System.Math.Max(lead, dspLead - LeadSink * (real - leadReadAt)) : lead;
            leadKnown = true;
            leadReadAt = real;
            return real + dspLead;
        }

        // ------------------------------------------------------------------ the GM's click

        private void BookClick(MusicTrack track, double firstBeat, double heard, double dsp)
        {
            if (!Click || clickSource == null) return;
            double beatSeconds = track.BeatSeconds;
            long next = BeatMath.BeatIndex(heard, firstBeat, beatSeconds) + 1;
            if (next <= clickBooked) return;
            double at = firstBeat + next * beatSeconds;
            if (at - heard > ClickLead) return;

            if (click == null)
            {
                click = MakeClick("GM Beat Click", 1175f);
                barClick = MakeClick("GM Bar Click", 1760f);
            }
            clickSource.clip = BeatMath.IsBarBeat(next, track.barBeat, track.beatsPerBar) ? barClick : click;
            clickSource.PlayScheduled(dspStart + (at - positionAtStart));
            clickBooked = next;
        }

        private static AudioClip MakeClick(string name, float hertz)
        {
            const int rate = 48000;
            var samples = new float[rate * 45 / 1000];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / rate;
                samples[i] = Mathf.Sin(2f * Mathf.PI * hertz * t) * Mathf.Exp(-t * 90f) * Mathf.Min(1f, i / 48f);
            }
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}
