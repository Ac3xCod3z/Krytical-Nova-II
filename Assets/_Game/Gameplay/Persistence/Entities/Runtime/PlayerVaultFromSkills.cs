using KryticalNova.Player;
using KryticalNova.Player.Entities;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Plans a vault with the ghost's body in <paramref name="world"/> (the server's body, or the owner's own view body for its
    /// prediction): NGO's rules (<see cref="PlayerVaultMotor.PlanForGhost"/>) and timing (<see cref="PlayerVaultMotor.VaultTiming"/>) in
    /// the ghost's integers (M3c, `m3c-vault-design.md`). The ghost's pose is the body's root, the agent's base offset above the feet;
    /// the rules work on the feet. No numbers of its own.
    /// </summary>
    public sealed class BodyVaultPlanner : IVaultPlanner
    {
        readonly World m_World;

        /// <summary>Why the last walk got no vault (NGO's words; tests and the scripted run).</summary>
        public string LastDecision { get; private set; }

        public BodyVaultPlanner(World world) => m_World = world;

        public bool TryPlan(Entity ghost, int3 startMm, int3 goalMm, int walkLengthMm, int speedMmps, out VaultSplice vault)
        {
            vault = default;
            GameObject body = PlayerJumpFromSkills.BodyOf(m_World, ghost);
            var motor = body != null ? body.GetComponent<PlayerVaultMotor>() : null;
            if (motor == null) { LastDecision = "no vault motor"; return false; }
            var agent = body.GetComponent<NavMeshAgent>();
            float offset = agent != null ? agent.baseOffset : 0f;
            var click = body.GetComponent<PointAndClickPlayer>();
            int layers = click != null ? click.RaycastLayers.value : Physics.DefaultRaycastLayers;
            Vector3 feet = M(startMm) - Vector3.up * offset;
            float walk = walkLengthMm == int.MaxValue ? float.PositiveInfinity : walkLengthMm * 0.001f;
            LastDecision = motor.PlanForGhost(feet, M(goalMm), walk, layers, out PlayerVaultMotor.GhostVault g);
            if (LastDecision != null) return false;

            PlayerVaultMotor.VaultTiming(Flat(g.Takeoff, g.Landing), Flat(g.Takeoff, g.NearEdge), Flat(g.Takeoff, g.FarEdge), g.Top,
                g.Style.airTime, motor.WarpLength, g.Style.poseClearance, motor.Clearance, speedMmps * 0.001f,
                out float air, out float raise, out float riseEnds, out float fallStarts);
            if (air <= 0f) { LastDecision = "no air time"; return false; }
            vault = new VaultSplice
            {
                TakeoffMm = Mm(g.Takeoff + Vector3.up * offset),
                LandingMm = Mm(g.Landing + Vector3.up * offset),
                AirMicros = Mathf.RoundToInt(air * 1e6f),
                RaiseMm = Mathf.RoundToInt(raise * 1000f),
                RiseEndsPermille = (short)Mathf.Clamp(Mathf.RoundToInt(riseEnds / air * 1000f), 1, 999),
                FallStartsPermille = (short)Mathf.Clamp(Mathf.RoundToInt(fallStarts / air * 1000f), 1, 999),
                Style = (byte)g.Height,
            };
            LastDecision = "vault";
            return true;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }

        static Vector3 M(int3 mm) => new Vector3(mm.x, mm.y, mm.z) * 0.001f;

        static int3 Mm(Vector3 m) => new int3(Mathf.RoundToInt(m.x * 1000f), Mathf.RoundToInt(m.y * 1000f), Mathf.RoundToInt(m.z * 1000f));
    }
}
