using UnityEngine;

namespace KryticalNova.Crossfire
{
    /// <summary>
    /// The look of a Rally Gate opening (owner's list, 16 Nov 2026, section 2): the server's Crossfire clock decides the gate
    /// (<see cref="MusterGate"/>: shut through the join window and the settle, the three opening together when the War starts); this
    /// only moves the leaves, the Covenant and Hegemony leaves sliding into the wall and the Null portcullis rising, eased over the
    /// opening time. The gate's collision is MusterGate's own (its carved blocker, and its shut look holding the leaf colliders), so it
    /// is the same on the server, which never runs this (Crossfire.Presentation is left out of the server build).
    /// </summary>
    public sealed class RallyGatePresenter : MonoBehaviour
    {
        [Tooltip("The gate whose state is shown.")]
        public MusterGate gate;
        [Tooltip("The moving leaves (art only, no colliders).")]
        public Transform[] leaves = new Transform[0];
        [Tooltip("Each leaf's move from shut to open, in its parent's space (Fable's GATES_022.json slide vectors).")]
        public Vector3[] openOffsets = new Vector3[0];
        [Tooltip("Seconds from shut to fully open (the Crossfire settings' gate opening time, 3 s).")]
        [Min(0.1f)] public float openingSeconds = 3f;

        Vector3[] m_Shut;
        float m_Amount = -1f;

        void Awake()
        {
            m_Shut = new Vector3[leaves.Length];
            for (int i = 0; i < leaves.Length; i++)
                if (leaves[i] != null) m_Shut[i] = leaves[i].localPosition;
        }

        void Update()
        {
            float target = gate != null && gate.IsOpen ? 1f : 0f;
            // a gate already open when the map loads (late join) is shown open at once
            m_Amount = m_Amount < 0f ? target : Mathf.MoveTowards(m_Amount, target, Time.deltaTime / openingSeconds);
            float eased = m_Amount * m_Amount * (3f - 2f * m_Amount);
            for (int i = 0; i < leaves.Length && i < openOffsets.Length; i++)
                if (leaves[i] != null) leaves[i].localPosition = m_Shut[i] + openOffsets[i] * eased;
        }
    }
}
