using KryticalNova.Audio;
using KryticalNova.Factions;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The menus' music (<see cref="MenuMusic"/>), kept alive from scene to scene of the title flow:
    ///   Title                   the title theme, looped
    ///   leaving the title       the outro sting; the theme fades; the Selection theme follows after a moment
    ///   FactionSelect           the highlighted faction's coat (<see cref="Focus"/>)
    ///   CharacterSelect, creator the Selection theme of the skin the menus wear (<see cref="MenuSkin"/>)
    ///   Loading and the world   fades out; back on the title (logging out) it starts again
    /// Each menu scene holds one; the first alive keeps playing and the others remove themselves, so starting Play
    /// from any menu scene has music. Client-only: a dedicated server has no ears.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuMusicPlayer : MonoBehaviour
    {
        [SerializeField] private MenuMusic music;
        [SerializeField] private AudioMixerGroup output;

        private static MenuMusicPlayer instance;
        private static FactionId focus;

        private AudioSource[] voices;
        private float[] level;
        private float[] target;
        private float[] speed;
        private float[] trackVolume;
        private AudioSource sting;
        private int current = -1;
        private MusicTrack wanted;
        private MusicTrack pending;
        private float pendingAt;
        private string lastScene;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            focus = FactionId.None;
        }

        /// <summary>The faction highlighted on faction select: its coat plays there. Faction select calls this.</summary>
        public static void Focus(FactionId faction) => focus = faction;

        private void Awake()
        {
            if (instance != null && instance != this || Application.isBatchMode)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            transform.SetParent(null, false);
            DontDestroyOnLoad(gameObject);
            voices = new[] { MakeSource("Theme A"), MakeSource("Theme B") };
            level = new float[voices.Length];
            target = new float[voices.Length];
            speed = new float[voices.Length];
            trackVolume = new float[voices.Length];
            sting = MakeSource("Sting");
            sting.loop = false;
        }

        private void Start()
        {
            // The player's volumes apply from the very first sound of the title page, not only once a Settings
            // window has been built (character select and the world). Not in Awake: a mixer ignores SetFloat then.
            if (output != null && output.audioMixer != null && !AudioPreferences.IsBound) AudioPreferences.Bind(output.audioMixer);
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private AudioSource MakeSource(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.priority = 0;
            source.volume = 0f;
            source.outputAudioMixerGroup = output;
            return source;
        }

        private void Update()
        {
            if (music == null) return;
            string scene = SceneManager.GetActiveScene().name;
            if (scene != lastScene)
            {
                if (lastScene == FrontEnd.TitleScene && scene != FrontEnd.TitleScene) LeaveTitle();
                if (scene != FrontEnd.FactionSelectScene) focus = FactionId.None;
                lastScene = scene;
            }

            MusicTrack next = Wanted(scene);
            if (next != wanted && pending == null) Change(next, next == null ? music.loadingFadeSeconds : music.fadeOutSeconds);
            if (pending != null && Time.unscaledTime >= pendingAt)
            {
                MusicTrack now = Wanted(scene);
                if (now == null) now = pending;
                pending = null;
                StartTrack(now);
            }

            for (int i = 0; i < voices.Length; i++)
            {
                level[i] = Mathf.MoveTowards(level[i], target[i], Time.unscaledDeltaTime * speed[i]);
                voices[i].volume = level[i] * music.volume * trackVolume[i];
                if (level[i] <= 0f && target[i] <= 0f && voices[i].isPlaying) voices[i].Stop();
            }
        }

        private MusicTrack Wanted(string scene)
        {
            switch (scene)
            {
                case FrontEnd.TitleScene:
                    return music.title;
                case FrontEnd.FactionSelectScene:
                    MusicTrack coat = CoatOf(focus);
                    return coat != null ? coat : SelectionOf(MenuSkin.Current);
                case FrontEnd.CharacterSelectScene:
                case FrontEnd.CharacterCreatorScene:
                    return SelectionOf(MenuSkin.Current);
                default:
                    return null;   // Loading and the world
            }
        }

        private MusicTrack SelectionOf(FactionId skin)
        {
            switch (skin)
            {
                case FactionId.Null: return music.selectNull;
                case FactionId.Covenant: return music.selectCovenant;
                default: return music.selectHegemony;
            }
        }

        private MusicTrack CoatOf(FactionId faction)
        {
            switch (faction)
            {
                case FactionId.Hegemony: return music.coatHegemony;
                case FactionId.Null: return music.coatNull;
                case FactionId.Covenant: return music.coatCovenant;
                default: return null;
            }
        }

        /// <summary>The key was pressed on the title page: the sting, the theme going out, the next one after a moment.</summary>
        private void LeaveTitle()
        {
            if (music.titleOutro != null && music.titleOutro.clip != null)
            {
                sting.clip = music.titleOutro.clip;
                sting.volume = music.volume * music.titleOutro.volume;
                sting.Play();
            }
            Change(null, music.titleFadeOutSeconds);
            pending = Wanted(SceneManager.GetActiveScene().name);
            pendingAt = Time.unscaledTime + music.afterTitleSeconds;
            if (pending == null) pendingAt = 0f;
        }

        /// <summary>Fades out what plays now over <paramref name="outSeconds"/> and, unless <paramref name="next"/> is
        /// none, brings it in on the other voice.</summary>
        private void Change(MusicTrack next, float outSeconds)
        {
            if (current >= 0) FadeTo(current, 0f, outSeconds);
            current = -1;
            wanted = next;
            if (next != null) StartTrack(next);
        }

        private void StartTrack(MusicTrack track)
        {
            wanted = track;
            if (track == null || track.clip == null) return;
            // The quieter voice takes the new theme; if it was still fading out from an earlier change it stops at once.
            int i = level[0] <= level[1] ? 0 : 1;
            if (current >= 0 && current != i) FadeTo(current, 0f, music.fadeOutSeconds);
            voices[i].Stop();
            level[i] = 0f;
            trackVolume[i] = track.volume;
            voices[i].clip = track.clip;
            voices[i].Play();
            FadeTo(i, 1f, music.fadeInSeconds);
            current = i;
        }

        private void FadeTo(int i, float to, float seconds)
        {
            target[i] = to;
            speed[i] = 1f / Mathf.Max(0.05f, seconds);
        }
    }
}
