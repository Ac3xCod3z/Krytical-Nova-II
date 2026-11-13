using System.Collections.Generic;
using KryticalNova.Brains;
using KryticalNova.Combat.Entities;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// M5.6 (plan s4, HIGH): a swing the entity brain resolved, rolled by the game's own rules on the Hostile's server body, as NGO's
    /// <c>HostileBrainLink.Strike</c> does (M5.0 H06-H08): <see cref="HostileStrikeRules.TryRoll"/> (both alive, the reach above, the wall rule,
    /// the attack's multiplier, the target's reflection, <c>CombatCalculator.CalculateAttack</c>), then the blow for M4.9's queue
    /// (<see cref="HostileStrikes"/>), which applies it on the player's server body with <c>TakeDamage</c>: the shield, the killing blow and the
    /// XP lost to a Hostile all run as in NGO. The attacker the player's side sees is the real Hostile body (its CharacterCombatStats, its
    /// HostileNet with the level gap), registered in <see cref="HostileAttackers"/> under <see cref="AttackerKey"/>.
    /// </summary>
    public static class HostileStrikeBridge
    {
        // one key per body for as long as the process runs (a pooled body keeps its key); far above the small keys tests use
        static readonly Dictionary<HostileNet, int> s_Keys = new Dictionary<HostileNet, int>();
        static int s_Next = FirstKey;
        const int FirstKey = 1 << 20;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Keys.Clear();
            s_Next = FirstKey;
        }

        /// <summary>The <see cref="HostileAttackers"/> key of a Hostile body (never 0).</summary>
        public static int AttackerKey(HostileNet hostile)
        {
            if (hostile == null) return 0;
            if (!s_Keys.TryGetValue(hostile, out int key)) s_Keys[hostile] = key = s_Next++;
            return key;
        }

        /// <summary>
        /// Rolls attack <paramref name="ability"/> of <paramref name="hostile"/>, standing at <paramref name="from"/>, on <paramref name="target"/>.
        /// False when it cannot land (NGO's reasons); otherwise the blow for the queue, aimed at <paramref name="targetGhost"/>.
        /// </summary>
        public static bool TryStrike(HostileNet hostile, int ability, CombatEntity target, Vector3 from, bool heavy, Entity targetGhost, out HostileStrike strike)
        {
            strike = default;
            if (hostile == null || hostile.Entity == null || target == null) return false;
            hostile.transform.position = from;   // where the brain swung from (the body follows the brain each tick)
            if (!HostileStrikeRules.TryRoll(hostile.Entity, hostile.Definition, ability, target, hostile.transform, out DamageResult result)) return false;
            int key = AttackerKey(hostile);
            HostileAttackers.Set(key, hostile.Entity.CombatStats);
            strike = new HostileStrike { Target = targetGhost, Result = result, From = new float3(from.x, from.y, from.z), Attacker = key, Heavy = heavy,
                Height = hostile.Definition != null ? hostile.Definition.HeightOf(ability) : KryticalNova.Combat.AttackHeight.Any };
            return true;
        }
    }

    /// <summary>
    /// Server, after the bodies took their brains' places and before M4.9 applies the blows: every <see cref="PendingStrike"/> of the tick
    /// through <see cref="HostileStrikeBridge.TryStrike"/>, each once. A swing whose Hostile body or target player body is gone is dropped.
    /// NGO's <c>ReportStrike</c> is not called: on a server body it would play the comic hit at the hidden body; the hit player's number
    /// goes with M4.9's HostileStrikeRpc, the threat note (H18) stays HostileNet's own. Managed bridge: job later (perf phase).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(HostileBodySyncServerSystem))]
    [UpdateBefore(typeof(HostileStrikeServerSystem))]
    public partial class HostileStrikeBridgeServerSystem : SystemBase
    {
        HostileBodyLinkServerSystem m_Link;
        readonly Dictionary<Entity, EcsHostileBrain> m_ByGhost = new();

        /// <summary>Swings queued as blows, swings that missed by the rules, swings dropped (a body gone) (tests).</summary>
        public int Queued { get; private set; }
        public int Missed { get; private set; }
        public int Dropped { get; private set; }

        protected override void OnCreate()
        {
            m_Link = World.GetOrCreateSystemManaged<HostileBodyLinkServerSystem>();
            RequireForUpdate<PendingStrikeQueue>();
        }

        protected override void OnUpdate()
        {
            DynamicBuffer<PendingStrike> pending = SystemAPI.GetSingletonBuffer<PendingStrike>();
            if (pending.Length == 0) return;
            using NativeArray<PendingStrike> swings = pending.ToNativeArray(Allocator.Temp);
            pending.Clear();

            m_ByGhost.Clear();
            foreach (KeyValuePair<EcsHostileBrain, Entity> pair in m_Link.Linked)
                if (pair.Key != null) m_ByGhost[pair.Value] = pair.Key;

            foreach (PendingStrike swing in swings)
            {
                HostileNet hostile = m_ByGhost.TryGetValue(swing.Hostile, out EcsHostileBrain body) && body != null ? body.GetComponent<HostileNet>() : null;
                CombatEntity target = null;
                foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                    if (ReferenceEquals(entry.World, World) && entry.Ghost == swing.Target && entry.Body != null) { target = entry.Body.GetComponent<CombatEntity>(); break; }
                string what;
                if (hostile == null || target == null) { Dropped++; what = "dropped (a body gone)"; }
                else if (!HostileStrikeBridge.TryStrike(hostile, swing.Ability, target, swing.From, swing.Heavy != 0, swing.Target, out HostileStrike strike)) { Missed++; what = "missed (NGO's rules)"; }
                else if (HostileStrikes.Queue(World, strike)) { Queued++; what = $"hit for {strike.Result.DamageDealt:0.#}{(strike.Result.IsEvaded ? " (evaded)" : string.Empty)}"; }
                else { Dropped++; what = "dropped (no blow queue)"; }
                if (HostileBrainDiag.Enabled)   // M5.7's timing lines: off by default
                {
                    uint tick = SystemAPI.TryGetSingleton(out BrainTick bt) ? bt.Tick : 0;
                    uint id = EntityManager.Exists(swing.Hostile) && EntityManager.HasComponent<BrainClock>(swing.Hostile) ? EntityManager.GetComponentData<BrainClock>(swing.Hostile).StableId : 0;
                    HostileBrainDiag.Write(HostileBrainDiag.Resolve(tick, id, what));
                }
            }
        }
    }
}
