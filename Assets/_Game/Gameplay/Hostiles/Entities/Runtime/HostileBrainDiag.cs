using System;
using System.Collections.Generic;
using KryticalNova.Brains;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// M5.7's timing lines (plan s4, off by default): <c>[KnBrain]</c> per Hostile when it notices (its mode leaves Idle / Return for a fight),
    /// when a swing starts and when one resolves, with the server tick, so a run's log gives the reaction table (notice, swing start = the
    /// reach tick, the resolve). On with <c>KN_BRAIN_DIAG=1</c> (like KN_PLAYHERE_DIAG). The words are pure (<see cref="Lines"/>).
    /// </summary>
    public static class HostileBrainDiag
    {
        public const string Variable = "KN_BRAIN_DIAG";

        /// <summary>Lines are written (null: from <see cref="Variable"/>, read once).</summary>
        public static bool? On;

        static bool? s_FromEnvironment;

        public static bool Enabled => On ?? (s_FromEnvironment ??= Environment.GetEnvironmentVariable(Variable) == "1");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            On = null;
            s_FromEnvironment = null;
        }

        /// <summary>Where the lines go (tests swap it); Debug.Log by default.</summary>
        public static Action<string> Sink = Debug.Log;

        static bool InFight(byte mode) => mode == HostileMode.Alert || mode == HostileMode.Engage;

        /// <summary>The lines one Hostile's change between two looks gives (pure): a notice, a swing start, or nothing.</summary>
        public static void Lines(uint tick, uint id, byte wasMode, byte mode, uint wasAttack, uint attack, byte ability, int targetGhost, List<string> into)
        {
            if (!InFight(wasMode) && InFight(mode))
                into.Add($"[KnBrain] tick {tick} hostile #{id} notice: mode {wasMode} -> {mode}, target ghost {targetGhost}");
            if (attack != 0 && attack != wasAttack)
                into.Add($"[KnBrain] tick {tick} hostile #{id} swing start: ability {ability} at tick {attack}, target ghost {targetGhost}");
        }

        /// <summary>The resolve line (pure).</summary>
        public static string Resolve(uint tick, uint id, string what) => $"[KnBrain] tick {tick} hostile #{id} resolve: {what}";

        public static void Write(string line)
        {
            if (Enabled) Sink?.Invoke(line);
        }
    }

    /// <summary>
    /// Server, after the brain tick: the <see cref="HostileBrainDiag"/> notice and swing-start lines. Off (does not update) unless the lines
    /// are on, so it costs nothing in a normal run. Managed (it writes logs): diagnostics only, never jobified.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BrainSimulationGroup))]
    public partial class HostileBrainDiagServerSystem : SystemBase
    {
        readonly Dictionary<Entity, (byte mode, uint attack)> m_Seen = new();
        readonly List<string> m_Lines = new();
        readonly HashSet<Entity> m_Now = new();

        protected override void OnCreate() => RequireForUpdate<BrainTick>();

        protected override void OnUpdate()
        {
            if (!HostileBrainDiag.Enabled) return;
            uint tick = SystemAPI.GetSingleton<BrainTick>().Tick;
            m_Now.Clear();
            foreach (var (brain, target, clock, entity) in SystemAPI.Query<RefRO<HostileBrain>, RefRO<HostileTarget>, RefRO<BrainClock>>().WithEntityAccess())
            {
                m_Now.Add(entity);
                HostileBrain b = brain.ValueRO;
                (byte mode, uint attack) was = m_Seen.TryGetValue(entity, out var seen) ? seen : (b.Mode, b.AttackStartTick);
                m_Lines.Clear();
                HostileBrainDiag.Lines(tick, clock.ValueRO.StableId, was.mode, b.Mode, was.attack, b.AttackStartTick, b.Ability, target.ValueRO.GhostId, m_Lines);
                foreach (string line in m_Lines) HostileBrainDiag.Write(line);
                m_Seen[entity] = (b.Mode, b.AttackStartTick);
            }
            if (m_Seen.Count > m_Now.Count)
            {
                var gone = new List<Entity>();
                foreach (Entity e in m_Seen.Keys) if (!m_Now.Contains(e)) gone.Add(e);
                foreach (Entity e in gone) m_Seen.Remove(e);
            }
        }
    }
}
