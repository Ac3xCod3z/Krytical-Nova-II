using UnityEngine;

namespace KryticalNova.Audio
{
    /// <summary>
    /// A looping 3D sound on a thing in the zone (river water, a dead holo-ad's buzz, a fallen rail bridge): its
    /// AudioSource plays the seamless loop from a random place, so a row of them never plays in step, and is louder
    /// as the player walks up to it. Placed by Krytical Nova > Setup > Ambience. Client-only.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AmbienceEmitter : MonoBehaviour
    {
        private AudioSource source;

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            if (Application.isBatchMode) enabled = false;
        }

        private void OnEnable()
        {
            if (source == null || source.clip == null) return;
            source.loop = true;
            source.timeSamples = Random.Range(0, Mathf.Max(1, source.clip.samples - 1));
            source.Play();
        }

        private void OnDisable()
        {
            if (source != null) source.Stop();
        }
    }
}
