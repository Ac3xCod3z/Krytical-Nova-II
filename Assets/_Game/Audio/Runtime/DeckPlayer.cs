using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// The Deck (owner, 22 Oct 2026): a player's own music, on this machine only. While it plays, the zone's
    /// walking-about music is silent here (<see cref="ExploreMusicPlayer"/>) and nobody else hears a thing. A fight
    /// takes over: the Deck steps aside for the fight music and its beat (Cadence), and comes back where it was after.
    /// Fight music is never in its packs. The last track, the volume, shuffle and repeat are kept on this machine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeckPlayer : MonoBehaviour
    {
        public enum Repeat : byte { All, One, Off }

        private const string TrackKey = "kn.deck.track", VolumeKey = "kn.deck.volume", ShuffleKey = "kn.deck.shuffle", RepeatKey = "kn.deck.repeat";

        private static DeckPlayer instance;
        private DeckLibrary library;
        private AudioSource source;
        private readonly List<MusicTrack> all = new List<MusicTrack>();
        private readonly List<string> packOf = new List<string>();
        private int index = -1;
        private bool playing;      // the player's wish: on (it may be stepped aside for a fight)
        private float fightLevel = 1f;
        private float startedAt = -10f; // a track just asked for may not have begun yet: it has not ended
        private float volume = 0.7f;
        private bool shuffle;
        private Repeat repeat;
        private readonly System.Random random = new System.Random();

        /// <summary>The Deck on this machine, made the first time it is asked for.</summary>
        public static DeckPlayer Ensure(DeckLibrary from)
        {
            if (instance != null) return instance;
            if (from == null || Application.isBatchMode) return null;
            var go = new GameObject("[Deck]");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DeckPlayer>();
            instance.Load(from);
            return instance;
        }

        /// <summary>True while the Deck is on: the zone's walking-about music is silent on this machine.</summary>
        public static bool HoldsMusic => instance != null && instance.playing;

        public static DeckPlayer Instance => instance;
        public DeckLibrary Library => library;
        public IReadOnlyList<MusicTrack> Tracks => all;
        public int Index => index;
        public MusicTrack Current => index >= 0 && index < all.Count ? all[index] : null;
        public string PackOf(int i) => i >= 0 && i < packOf.Count ? packOf[i] : string.Empty;
        public bool IsPlaying => playing;
        /// <summary>Stepped aside for a fight right now.</summary>
        public bool InFight => playing && fightLevel < 0.99f;
        public bool Shuffle { get => shuffle; set { shuffle = value; PlayerPrefs.SetInt(ShuffleKey, value ? 1 : 0); } }
        public Repeat RepeatMode { get => repeat; set { repeat = value; PlayerPrefs.SetInt(RepeatKey, (int)value); } }
        public float Volume { get => volume; set { volume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(VolumeKey, volume); } }
        public float Time01 => source != null && source.clip != null && source.clip.length > 0f ? source.time / source.clip.length : 0f;
        public float Seconds => source != null ? source.time : 0f;
        public float Length => source != null && source.clip != null ? source.clip.length : 0f;

        private void Load(DeckLibrary from)
        {
            library = from;
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = from.output;
            all.Clear();
            packOf.Clear();
            foreach (DeckPack pack in from.packs)
            {
                if (pack == null) continue;
                foreach (MusicTrack track in pack.tracks)
                {
                    if (track == null || track.clip == null) continue;
                    all.Add(track);
                    packOf.Add(pack.packName);
                }
            }
            volume = PlayerPrefs.GetFloat(VolumeKey, from.defaultVolume);
            shuffle = PlayerPrefs.GetInt(ShuffleKey, 0) == 1;
            repeat = (Repeat)Mathf.Clamp(PlayerPrefs.GetInt(RepeatKey, 0), 0, 2);
            string last = PlayerPrefs.GetString(TrackKey, string.Empty);
            index = all.FindIndex(t => t.name == last);
            if (index < 0 && all.Count > 0) index = 0;
            if (index >= 0) source.clip = all[index].clip;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        // ------------------------------------------------------------------ the buttons

        public void PlayPause()
        {
            if (playing) Pause();
            else Play();
        }

        public void Play()
        {
            if (all.Count == 0) return;
            if (index < 0) index = 0;
            if (source.clip != all[index].clip) source.clip = all[index].clip;
            playing = true;
            startedAt = Time.unscaledTime;
            if (fightLevel > 0.01f) source.UnPause();
            if (!source.isPlaying) source.Play();
        }

        public void Pause()
        {
            playing = false;
            source.Pause();
        }

        public void PlayAt(int i)
        {
            if (i < 0 || i >= all.Count) return;
            index = i;
            source.clip = all[i].clip;
            source.time = 0f;
            PlayerPrefs.SetString(TrackKey, all[i].name);
            playing = true;
            startedAt = Time.unscaledTime;
            source.Play();
        }

        public void Next()
        {
            if (all.Count == 0) return;
            PlayAt(shuffle && all.Count > 1 ? Other() : (index + 1) % all.Count);
        }

        /// <summary>Back to the start of the track, or (near its start already) to the one before.</summary>
        public void Previous()
        {
            if (all.Count == 0) return;
            if (source.time > 3f) { source.time = 0f; return; }
            PlayAt((index - 1 + all.Count) % all.Count);
        }

        public void Seek(float t01)
        {
            if (source.clip == null) return;
            source.time = Mathf.Clamp01(t01) * Mathf.Max(0f, source.clip.length - 0.05f);
        }

        /// <summary>The music's spectrum on this machine, for the window's visualiser.</summary>
        public void Spectrum(float[] into)
        {
            if (source == null || !source.isPlaying) { System.Array.Clear(into, 0, into.Length); return; }
            source.GetSpectrumData(into, 0, FFTWindow.BlackmanHarris);
        }

        private int Other()
        {
            int next = index;
            while (next == index) next = random.Next(all.Count);
            return next;
        }

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (source == null) return;
            // A fight takes over: the Deck steps aside quickly and comes back slowly, where it was.
            float want = 1f - CombatMusicPlayer.Live;
            float seconds = want < fightLevel ? library.duckSeconds : library.returnSeconds;
            fightLevel = Mathf.MoveTowards(fightLevel, want, Time.unscaledDeltaTime / Mathf.Max(0.05f, seconds));
            MusicTrack track = Current;
            source.volume = volume * (track != null ? track.volume : 0.6f) * fightLevel;
            if (!playing) return;
            if (fightLevel <= 0.01f) { if (source.isPlaying) source.Pause(); return; }
            bool ended = !source.isPlaying && Time.unscaledTime - startedAt > 0.5f
                && (source.time <= 0.01f || source.clip != null && source.time >= source.clip.length - 0.05f);
            if (ended)
            {
                // The track ended: the next one, the same again, or stop.
                if (repeat == Repeat.One) PlayAt(index);
                else if (repeat == Repeat.Off && !shuffle && index == all.Count - 1) { playing = false; source.time = 0f; }
                else Next();
                return;
            }
            if (!source.isPlaying) source.UnPause();
        }
    }
}
