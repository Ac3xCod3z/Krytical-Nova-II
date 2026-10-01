using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Crossfades the ambience loops of the two weather profiles being blended (e.g. light rain → storm).
    /// Two 2D AudioSources; does nothing when no profile has a clip.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherAmbience : MonoBehaviour
    {
        [SerializeField] private AudioSource sourceA;
        [SerializeField] private AudioSource sourceB;
        [Range(0f, 1f)] [SerializeField] private float masterVolume = 1f;

        private void Awake()
        {
            if (Application.isBatchMode) enabled = false;
        }

        private void Update()
        {
            var env = WorldEnvironmentNetwork.Instance;
            if (env == null || sourceA == null || sourceB == null) return;

            var from = env.FromWeather;
            var to = env.ToWeather;
            float t = env.WeatherBlendT;

            AudioClip clipFrom = from != null ? from.ambienceLoop : null;
            AudioClip clipTo = to != null ? to.ambienceLoop : null;
            float volFrom = from != null ? from.ambienceVolume * (1f - t) : 0f;
            float volTo = to != null ? to.ambienceVolume * t : 0f;

            if (clipFrom == clipTo)
            {
                Drive(sourceA, clipTo, volFrom + volTo);
                Drive(sourceB, null, 0f);
            }
            else
            {
                Drive(sourceA, clipFrom, volFrom);
                Drive(sourceB, clipTo, volTo);
            }
        }

        private void Drive(AudioSource source, AudioClip clip, float volume)
        {
            if (clip == null || volume <= 0.001f)
            {
                if (source.isPlaying) source.Stop();
                return;
            }
            if (source.clip != clip)
            {
                source.clip = clip;
                source.loop = true;
            }
            source.volume = volume * masterVolume;
            if (!source.isPlaying) source.Play();
        }
    }
}
