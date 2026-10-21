using KryticalNova.World;
using UnityEngine;
using UnityEngine.Audio;

namespace KryticalNova.Audio
{
    /// <summary>
    /// Plays a zone's ambience beds (<see cref="ZoneAmbience"/>) on this machine: one looping 2D source per bed, each
    /// starting at a different place in its loop, coming in over a few seconds when the zone loads, and the day and
    /// night beds crossfading with the sun's height from the world clock (<see cref="WorldClock.SunDirection"/>), so
    /// dawn and dusk blend instead of switching. The loops are baked seamless (the music-gen skill's
    /// ambience_bake.py). Client-only: a dedicated server has no ears.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ZoneAmbiencePlayer : MonoBehaviour
    {
        [SerializeField] private ZoneAmbience ambience;
        [SerializeField] private AudioMixerGroup output;

        private AudioSource[] sources;
        private float fade;

        /// <summary>0 at night, 1 in daylight, between the two in twilight.</summary>
        public static float Daylight(float sunHeight, float dawnHeight, float twilight) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(dawnHeight - twilight, dawnHeight + twilight, sunHeight));

        private void Awake()
        {
            if (Application.isBatchMode || ambience == null)
            {
                enabled = false;
                return;
            }
            sources = new AudioSource[ambience.beds.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                ZoneAmbience.Bed bed = ambience.beds[i];
                if (bed.clip == null) continue;
                var go = new GameObject("Bed " + bed.clip.name);
                go.transform.SetParent(transform, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.clip = bed.clip;
                source.loop = true;
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                source.priority = 32;
                source.volume = 0f;
                source.outputAudioMixerGroup = output;
                sources[i] = source;
            }
        }

        private void OnEnable() => fade = 0f;

        private void OnDisable()
        {
            if (sources == null) return;
            foreach (AudioSource source in sources)
                if (source != null) source.Stop();
        }

        private void Update()
        {
            fade = Mathf.MoveTowards(fade, 1f, Time.unscaledDeltaTime / ambience.fadeInSeconds);
            float day = Daylight(WorldClock.SunDirection.y, ambience.dawnHeight, ambience.twilight);
            for (int i = 0; i < sources.Length; i++)
            {
                AudioSource source = sources[i];
                if (source == null) continue;
                ZoneAmbience.Bed bed = ambience.beds[i];
                float when = bed.when == AmbienceTime.Day ? day : bed.when == AmbienceTime.Night ? 1f - day : 1f;
                float volume = bed.volume * when * fade;
                source.volume = volume;
                if (volume > 0.0005f)
                {
                    // Each bed starts somewhere in its loop, so two zones' loads never sound the same.
                    if (!source.isPlaying)
                    {
                        source.timeSamples = Random.Range(0, Mathf.Max(1, source.clip.samples - 1));
                        source.Play();
                    }
                }
                else if (source.isPlaying && fade >= 1f)
                {
                    source.Stop();   // a day bed at night: nothing to hear, nothing to play
                }
            }
        }
    }
}
