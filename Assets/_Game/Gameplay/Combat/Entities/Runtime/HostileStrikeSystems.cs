using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Combat.Entities
{
    /// <summary>
    /// One Hostile's blow on a player, already rolled (M4 step 9; NGO HostileBrainLink.Strike after its reach checks and
    /// CombatCalculator.CalculateAttack). The attacker side appends it (M5's monster brain; tests), the server applies it on the
    /// player's server body.
    /// </summary>
    public struct HostileStrike : IBufferElementData
    {
        /// <summary>The player's ghost in the server world.</summary>
        public Entity Target;
        public DamageResult Result;
        /// <summary>Where the attacker stood: the killing blow's direction comes from it.</summary>
        public float3 From;
        /// <summary>The <see cref="HostileAttackers"/> key of the stand-in attacker; 0 = none (no Hostile's XP loss).</summary>
        public int Attacker;
        /// <summary>The attack is a heavy one (NGO's flag 8).</summary>
        public bool Heavy;
    }

    /// <summary>The one entity in the server world that holds the <see cref="HostileStrike"/> queue.</summary>
    public struct HostileStrikeQueue : IComponentData { }

    /// <summary>
    /// A Hostile's blow as the hit player's own game shows it (NGO HostileNet.StrikeClientRpc, of which only the hit player
    /// shows the number). Reliable, like <see cref="HitNumberRpc"/>.
    /// </summary>
    public struct HostileStrikeRpc : IRpcCommand
    {
        public int DamageMilli;
        /// <summary>NGO's bits: critical 1, evaded 2, reflected 4, heavy 8.</summary>
        public byte Flags;
    }

    /// <summary>
    /// The stand-in attackers a <see cref="HostileStrike"/> names: hidden objects carrying what the player's side reads off a Hostile
    /// (its <see cref="CharacterCombatStats"/>, and HostileNet with its level gap for the XP lost to it). Server, main thread.
    /// </summary>
    public static class HostileAttackers
    {
        static readonly Dictionary<int, CharacterCombatStats> s_StandIns = new Dictionary<int, CharacterCombatStats>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_StandIns.Clear();

        public static void Set(int key, CharacterCombatStats standIn)
        {
            if (key == 0) return;
            if (standIn == null) s_StandIns.Remove(key);
            else s_StandIns[key] = standIn;
        }

        public static CharacterCombatStats Of(int key) =>
            key != 0 && s_StandIns.TryGetValue(key, out CharacterCombatStats stats) && stats != null ? stats : null;
    }

    public static class HostileStrikes
    {
        public const byte Critical = 1, Evaded = 2, Reflected = 4, Heavy = 8, Blocked = 16; // Blocked: build 17 item 8

        /// <summary>Server: queues one blow for the next update. False when the world has no queue.</summary>
        public static bool Queue(World server, in HostileStrike strike)
        {
            if (server == null || !server.IsCreated) return false;
            EntityManager em = server.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<HostileStrikeQueue>());
            if (query.IsEmpty) return false;
            em.GetBuffer<HostileStrike>(query.GetSingletonEntity()).Add(strike);
            return true;
        }

        /// <summary>
        /// The blow on the player's body, NGO's <c>target.TakeDamage(result, mine)</c>: shield, channel break, the killing blow and
        /// death all run as they do there. Never on a view (it only shows) and never on the dead. False when it did not land.
        /// </summary>
        public static bool Apply(CombatEntity target, in HostileStrike strike, CharacterCombatStats attacker)
        {
            if (target == null || !target.IsAlive) return false;
            BodyRoleTag tag = BodyRoleTag.Of(target);
            if (tag != null && tag.Kind == NetRoleKind.ClientView) return false;
            if (attacker != null) attacker.transform.position = strike.From;   // one stand-in serves many Hostiles: stand where this one stood
            target.TakeDamage(strike.Result, attacker);
            return true;
        }

        public static HostileStrikeRpc Of(in DamageResult result, bool heavy) => new HostileStrikeRpc
        {
            DamageMilli = Mathf.RoundToInt(result.DamageDealt * 1000f),
            Flags = (byte)((result.IsCritical ? Critical : 0) | (result.IsEvaded ? Evaded : 0) | (result.IsReflected ? Reflected : 0)
                | (heavy ? Heavy : 0) | (result.IsBlocked ? Blocked : 0)),
        };

        /// <summary>The number as NGO HostileNet.ShowStrike builds it for the hit player.</summary>
        public static DamageResult ResultOf(in HostileStrikeRpc strike)
        {
            float damage = strike.DamageMilli / 1000f;
            bool reflected = (strike.Flags & Reflected) != 0;
            return new DamageResult
            {
                DamageDealt = damage,
                IsCritical = (strike.Flags & Critical) != 0,
                IsEvaded = (strike.Flags & Evaded) != 0,
                IsReflected = reflected,
                IsBlocked = (strike.Flags & Blocked) != 0,
                ReflectedDamage = reflected ? damage : 0f,
            };
        }
    }

    /// <summary>
    /// Server: applies every queued Hostile blow on the hit player's server body and sends the number to that player's own game.
    /// A blow for a player who has left, a view or the dead is dropped.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class HostileStrikeServerSystem : SystemBase
    {
        Entity m_Queue;

        /// <summary>Blows that landed, blows dropped, numbers sent (tests).</summary>
        public int Applied { get; private set; }
        public int Dropped { get; private set; }
        public int Sent { get; private set; }

        protected override void OnCreate()
        {
            m_Queue = EntityManager.CreateEntity(typeof(HostileStrikeQueue));
            EntityManager.AddBuffer<HostileStrike>(m_Queue);
        }

        protected override void OnUpdate()
        {
            DynamicBuffer<HostileStrike> queue = EntityManager.GetBuffer<HostileStrike>(m_Queue);
            if (queue.Length == 0) return;
            using var strikes = queue.ToNativeArray(Allocator.Temp);
            queue.Clear();
            foreach (HostileStrike strike in strikes)
            {
                GhostBodyEntry hit = null;
                foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                    if (ReferenceEquals(entry.World, World) && entry.Ghost == strike.Target && entry.Body != null) { hit = entry; break; }
                if (hit == null || !hit.Body.TryGetComponent(out CombatEntity target)
                    || !HostileStrikes.Apply(target, strike, HostileAttackers.Of(strike.Attacker)))
                {
                    Dropped++;
                    continue;
                }
                Applied++;
                if (HitNumbers.SendTo(World, hit.NetworkId, HostileStrikes.Of(strike.Result, strike.Heavy))) Sent++;
            }
        }
    }

    /// <summary>
    /// Client: a Hostile's blow on this player shows its number on the own view (NGO ShowStrike's <c>targetIsLocal</c> part). The comic
    /// hit at the Hostile comes with the Hostile's own ghost (M5).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class HostileStrikeClientSystem : SystemBase
    {
        EntityQuery m_Strikes;

        /// <summary>Blows received (tests).</summary>
        public int Received { get; private set; }

        protected override void OnCreate() =>
            m_Strikes = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<HostileStrikeRpc>());

        protected override void OnUpdate()
        {
            if (m_Strikes.IsEmpty) return;
            using var strikes = m_Strikes.ToComponentDataArray<HostileStrikeRpc>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Strikes);
            GameObject own = null;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
                if (entry.IsLocal && ReferenceEquals(entry.World, World) && entry.Body != null) { own = entry.Body; break; }
            foreach (HostileStrikeRpc strike in strikes)
            {
                Received++;
                if (own != null) ClientPresentationRegistry.Feedback?.ShowCombatFeedback(own.transform.position, HostileStrikes.ResultOf(strike));
            }
        }
    }
}
