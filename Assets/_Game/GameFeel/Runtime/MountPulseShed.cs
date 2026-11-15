using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// One pooled copy of a mount's pulse particles (the Brambleback's leaves), kept under the mount between landings instead of a
    /// new copy made and destroyed on every landing (perf night chunk 5). It hides itself when the particles are done (the stop
    /// action that used to destroy the copy now switches it off) or at the old 1.6 s destroy time, whichever comes first, so it is
    /// on screen exactly as long as before. Made and reused by <see cref="MountPulse"/> only.
    /// </summary>
    public sealed class MountPulseShed : MonoBehaviour
    {
        /// <summary>The prefab this is a copy of (a mount's stages may shed different ones).</summary>
        public GameObject Prefab { get; private set; }
        /// <summary>When the old code destroyed the copy (<see cref="Time.time"/>).</summary>
        public float Until { get; private set; }

        private ParticleSystem[] systems;

        internal static MountPulseShed Make(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
        {
            GameObject copy = Instantiate(prefab, position, rotation, parent);
            MountPulseShed shed = copy.AddComponent<MountPulseShed>();
            shed.Prefab = prefab;
            shed.systems = copy.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in shed.systems)
            {
                // this copy only (never the prefab): "destroy when done" becomes "switch off when done", so it can be shed again
                ParticleSystem.MainModule main = ps.main;
                if (main.stopAction == ParticleSystemStopAction.Destroy) main.stopAction = ParticleSystemStopAction.Disable;
            }
            return shed;
        }

        /// <summary>Free once hidden, or once its old destroy time has passed (it may not have hidden itself yet this frame).</summary>
        internal bool IsFree(float now) => !gameObject.activeSelf || now >= Until;

        /// <summary>Shed again: off, emptied, placed, on, so it plays from the start exactly as a fresh copy does (play on awake).</summary>
        internal void Restart(Vector3 position, Quaternion rotation)
        {
            gameObject.SetActive(false);
            foreach (ParticleSystem ps in systems) if (ps != null) ps.Clear(false);
            transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);
        }

        internal void Live(float now, float seconds)
        {
            Until = now + seconds;
            enabled = true;
        }

        private void Update()
        {
            if (Time.time >= Until) gameObject.SetActive(false);
        }
    }
}
