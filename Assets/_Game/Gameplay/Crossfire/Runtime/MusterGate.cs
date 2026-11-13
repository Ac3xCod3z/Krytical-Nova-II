using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Crossfire
{
    /// <summary>
    /// A Muster Yard's gate (owner's DAY list item 17): shut until the war starts, open while it runs, shut again at its end. Shut, it is
    /// a carved <see cref="NavMeshObstacle"/>, so no walk is planned through it on the server or on the owner's game (both plan on the
    /// NavMesh); its look is the shut and open objects. It follows the server's Crossfire clock (<see cref="CrossfireView.ForGates"/>):
    /// on the server its own copy, on a client the one it received. It decides nothing about who may pass.
    /// </summary>
    public sealed class MusterGate : MonoBehaviour
    {
        [Tooltip("The carved obstacle that blocks the walk while the gate is shut (Carve on).")]
        public NavMeshObstacle blocker;
        [Tooltip("Shown while the gate is shut (the door, the portcullis, the blast wall).")]
        public GameObject shutLook;
        [Tooltip("Shown while the gate is open (optional: the raised door).")]
        public GameObject openLook;

        bool m_Shown, m_Open;

        /// <summary>The gate stands open now (tests).</summary>
        public bool IsOpen => m_Open;

        void OnEnable()
        {
            m_Shown = false;
            Apply(CrossfireView.ForGates.GatesOpen);
        }

        void Update()
        {
            bool open = CrossfireView.ForGates.GatesOpen;
            if (!m_Shown || open != m_Open) Apply(open);
        }

        /// <summary>Opens or shuts the gate (the server's clock decides; this only shows it and blocks the walk).</summary>
        public void Apply(bool open)
        {
            m_Shown = true;
            m_Open = open;
            if (blocker != null)
            {
                blocker.carving = true;
                blocker.enabled = !open;
            }
            if (shutLook != null) shutLook.SetActive(!open);
            if (openLook != null) openLook.SetActive(open);
        }
    }
}
