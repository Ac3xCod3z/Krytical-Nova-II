using KryticalNova.Core;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Crossfire
{
    /// <summary>
    /// A Muster Yard's Rally Gate (owner's DAY list item 17; owner, 16 Nov 2026: shut through the 5-min join window and the 1-min settle,
    /// the three opening together when the War starts). It follows the server's Crossfire clock (<see cref="CrossfireView.ForGates"/>: on
    /// the server its own copy, on a client the one it received) at the server-synchronised time (<see cref="NetClock"/>), so every gate on
    /// every machine is in the same <see cref="Phase"/>. While shut or opening it is a carved <see cref="NavMeshObstacle"/>, so no walk is
    /// planned through it until it stands fully open; its look is the shut and open objects, and a gate presenter animates the leaves from
    /// <see cref="OpenAmount"/> (presentation only, never on the server). It decides nothing about who may pass.
    /// </summary>
    public sealed class MusterGate : MonoBehaviour
    {
        [Tooltip("The carved obstacle that blocks the walk while the gate is shut or still opening (Carve on).")]
        public NavMeshObstacle blocker;
        [Tooltip("Shown while the gate is shut or opening (the door, the portcullis, the blast wall; a gate presenter moves its leaves).")]
        public GameObject shutLook;
        [Tooltip("Shown once the gate stands open (optional: the raised door).")]
        public GameObject openLook;
        [Tooltip("Seconds this gate takes to open; 0 = the Crossfire settings' Gate Opening Seconds (all three gates alike).")]
        [Min(0f)] public float openingSeconds;

        CrossfireGatePhase m_Phase;
        bool m_Shown;

        /// <summary>The gate is opening or open: the War has started (tests).</summary>
        public bool IsOpen => m_Phase != CrossfireGatePhase.Shut;

        /// <summary>Shut, opening or open, as the server's clock has it now.</summary>
        public CrossfireGatePhase Phase => m_Phase;

        /// <summary>How far the gate has opened, 0 (shut) to 1 (open): what a gate presenter shows on the leaves.</summary>
        public float OpenAmount { get; private set; }

        /// <summary>The way through is blocked (shut, or not yet fully open).</summary>
        public bool Blocks => m_Phase != CrossfireGatePhase.Open;

        double OpeningSeconds
        {
            get
            {
                if (openingSeconds > 0f) return openingSeconds;
                CrossfireSettings s = CrossfireSettings.Instance;
                return s != null ? s.gateOpeningSeconds : 0.0;
            }
        }

        void OnEnable()
        {
            m_Shown = false;
            Follow();
        }

        void Update() => Follow();

        void Follow()
        {
            CrossfireState state = CrossfireView.ForGates;
            double now = NetClock.Now, opening = openingSeconds > 0f || !state.Known ? OpeningSeconds : state.GateOpeningSeconds;
            OpenAmount = state.GateOpenAmount(now, opening);
            Apply(state.GatePhase(now, opening));
        }

        /// <summary>Opens or shuts the gate at once (tests, a GM's hand): true = open, false = shut.</summary>
        public void Apply(bool open)
        {
            OpenAmount = open ? 1f : 0f;
            Apply(open ? CrossfireGatePhase.Open : CrossfireGatePhase.Shut);
        }

        /// <summary>Shows a gate phase (the server's clock decides it; this only shows it and blocks the walk).</summary>
        public void Apply(CrossfireGatePhase phase)
        {
            if (m_Shown && phase == m_Phase) return;
            m_Shown = true;
            m_Phase = phase;
            bool open = phase == CrossfireGatePhase.Open;
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
