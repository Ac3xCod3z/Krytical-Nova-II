using System;
using KryticalNova.Combat;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>What every machine needs to know to be at the same place in the zone's music (~14 bytes).</summary>
    public struct MusicState : INetworkSerializable, IEquatable<MusicState>
    {
        /// <summary>Clock time at which the round of tracks (or the one forced track) began.</summary>
        public double Epoch;
        /// <summary>-1: the zone's round of tracks. Otherwise that one track, over and over (GM).</summary>
        public short Forced;
        /// <summary>Seconds every beat of what is playing is moved by (GM: to try a correction before it is baked into the track).</summary>
        public float Nudge;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Epoch);
            serializer.SerializeValue(ref Forced);
            serializer.SerializeValue(ref Nudge);
        }

        public bool Equals(MusicState other) => Epoch.Equals(other.Epoch) && Forced == other.Forced && Nudge.Equals(other.Nudge);
    }

    /// <summary>
    /// Where the zone's fight music is, for everyone: the tracks of <see cref="ZoneMusic"/> run round and round on
    /// the shared clock (server time; local time offline, as the weather does) whether or not anyone hears them,
    /// so every machine can work out which track is on and how far into it, and every fighter in the zone is on
    /// the same beat. It makes no sound: <see cref="CombatMusicPlayer"/> plays the music, on a player's own game
    /// and only while that player is in a fight. One small state is replicated and only a GM changes it.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-45)]
    public sealed class MusicConductor : NetworkBehaviour, IBeatClock
    {
        public static MusicConductor Instance { get; private set; }

        [SerializeField] private ZoneMusic music;

        private readonly NetworkVariable<MusicState> netState = new NetworkVariable<MusicState>(
            new MusicState { Forced = -1 }, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private MusicState state = new MusicState { Forced = -1 };
        private double[] lengths = Array.Empty<double>();
        private readonly double[] one = new double[1];

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instance = null;

        public ZoneMusic Music => music;
        public MusicState State => state;

        /// <summary>True offline, on the server and on a host. Only these may change what plays.</summary>
        public bool HasAuthority => !IsSpawned || IsServer;

        /// <summary>Shared clock: server time when networked, local time offline.</summary>
        public double Now => IsSpawned && NetworkManager != null ? NetworkManager.ServerTime.Time : Time.timeAsDouble;

        private void Awake()
        {
            if (Instance != null && Instance != this) return;
            Instance = this;
            BeatClock.Register(this);
        }

        private void OnEnable()
        {
            if (Instance == null) Instance = this;
            if (Instance == this) BeatClock.Register(this);
        }

        public override void OnDestroy()
        {
            if (Instance == this) Instance = null;
            BeatClock.Unregister(this);
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) netState.Value = state;
            else
            {
                state = netState.Value;
                netState.OnValueChanged += OnNetStateChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (!IsServer) netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(MusicState previous, MusicState current) => state = current;

        /// <summary>
        /// What is playing at clock time <paramref name="at"/>, and how many seconds into it. False between two
        /// tracks and when the zone has no music. The same answer on every machine for the same time.
        /// </summary>
        public bool TryLocate(double at, out MusicTrack track, out int index, out double position)
        {
            track = null;
            index = -1;
            position = 0.0;
            MusicTrack[] tracks = music != null ? music.combat : null;
            if (tracks == null || tracks.Length == 0) return false;

            double gap = music.gapSeconds;
            if (state.Forced >= 0 && state.Forced < tracks.Length && tracks[state.Forced] != null)
            {
                one[0] = tracks[state.Forced].Length;
                if (!BeatMath.Locate(at - state.Epoch, one, gap, out _, out position)) return false;
                index = state.Forced;
            }
            else
            {
                if (lengths.Length != tracks.Length) lengths = new double[tracks.Length];
                for (int i = 0; i < tracks.Length; i++) lengths[i] = tracks[i] != null ? tracks[i].Length : 0.0;
                if (!BeatMath.Locate(at - state.Epoch, lengths, gap, out index, out position)) return false;
            }
            track = tracks[index];
            return track != null;
        }

        /// <summary>The time in a track of the beat every other is counted from, with the GM's nudge.</summary>
        public double FirstBeatOf(MusicTrack track) => track != null ? track.firstBeatSeconds + state.Nudge : 0.0;

        // Cadence reads the beat through IBeatClock (Combat), so the fight's rules do not depend on Audio.
        bool IBeatClock.HasBeat => CombatBeat.HasBeat;
        float IBeatClock.Live01 => CombatBeat.Live01;
        double IBeatClock.ClockLagSeconds => CombatBeat.ClockLagSeconds;
        bool IBeatClock.CalloutsOnly => music != null && music.calloutsOnly;

        bool IBeatClock.TryBeatAt(double at, out ScriptableObject track, out double errorSeconds, out double beats)
        {
            errorSeconds = beats = 0.0;
            bool located = TryLocate(at, out MusicTrack found, out _, out double position);
            track = found;
            if (!located) return false;
            double first = FirstBeatOf(found);
            errorSeconds = BeatMath.ErrorSeconds(position, first, found.BeatSeconds);
            beats = BeatMath.Beats(position, first, found.BeatSeconds);
            return true;
        }

        /// <summary>
        /// Seconds between clock time <paramref name="at"/> and the nearest beat of what is playing then: negative
        /// before the beat. False when nothing with a beat is playing.
        /// </summary>
        public bool TryBeatError(double at, out double seconds)
        {
            seconds = 0.0;
            if (!TryLocate(at, out MusicTrack track, out _, out double position)) return false;
            seconds = BeatMath.ErrorSeconds(position, FirstBeatOf(track), track.BeatSeconds);
            return true;
        }

        // ------------------------------------------------------------------ GM (server or offline)

        /// <summary>Plays one track of the zone over and over from now, or goes back to the round with -1.</summary>
        public bool ServerForce(int index)
        {
            if (!HasAuthority) return false;
            MusicTrack[] tracks = music != null ? music.combat : null;
            if (index >= 0 && (tracks == null || index >= tracks.Length || tracks[index] == null)) return false;
            Write(new MusicState { Epoch = index >= 0 ? Now : 0.0, Forced = (short)Math.Max(-1, index), Nudge = 0f });
            return true;
        }

        /// <summary>Moves every beat of what is playing by this many seconds, for every machine.</summary>
        public bool ServerNudge(float seconds)
        {
            if (!HasAuthority) return false;
            MusicState next = state;
            next.Nudge = seconds;
            Write(next);
            return true;
        }

        private void Write(MusicState next)
        {
            state = next;
            if (IsSpawned) netState.Value = next;
        }
    }
}
