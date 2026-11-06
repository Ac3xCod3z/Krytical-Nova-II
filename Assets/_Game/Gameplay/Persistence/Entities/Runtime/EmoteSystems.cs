using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner's emote play or stop by its key (NGO's <c>PlayerEmotes</c> PlayRpc / StopRpc; M8 8.4.6).</summary>
    public struct EmoteRpc : IRpcCommand
    {
        public byte Stop;
        public FixedString64Bytes Key;
    }

    /// <summary>Server: the owners' emote requests at the Cosmetic limit (as NGO) on their server bodies; every player ghost's emote equal to its body's.</summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class EmoteServerSystem : SystemBase
    {
        EntityQuery m_Asks;

        /// <summary>Requests run and ghosts rewritten (tests).</summary>
        public int Ran { get; private set; }
        public int Writes { get; private set; }

        protected override void OnCreate() =>
            m_Asks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<EmoteRpc>());

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            if (!m_Asks.IsEmpty)
            {
                using var received = m_Asks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Asks.ToComponentDataArray<EmoteRpc>(Allocator.Temp);
                em.DestroyEntity(m_Asks);
                for (int i = 0; i < asks.Length; i++)
                {
                    if (!PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Cosmetic, out GameObject body) || !body.TryGetComponent(out IBodyEmotes emotes)) continue;
                    // NGO PlayerEmotes.RequestPlay: the weapon is put away before the emote (Shift's own sheathe). Done here, in the emote's
                    // tick, so every view gets the sheathe with the emote and plays it first (the motor's emote waits for it, 3 s at most).
                    if (asks[i].Stop == 0) SheatheOn(received[i].SourceConnection);
                    emotes.RunEmote(asks[i].Stop != 0, asks[i].Key.ToString());
                    Ran++;
                }
            }
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerEmoteTag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out IBodyEmotes emotes)) continue;
                BodyEmoteState state = emotes.State;
                PlayerEmoteTag had = em.GetComponentData<PlayerEmoteTag>(entry.Ghost);
                if (had.Serial == state.serial && had.Stance == (state.stance ? 1 : 0) && had.Key.ToString() == (state.key ?? string.Empty)) continue;
                var now = new PlayerEmoteTag { Serial = state.serial, Stance = (byte)(state.stance ? 1 : 0) };
                now.Key.CopyFromTruncated(state.key ?? string.Empty);
                em.SetComponentData(entry.Ghost, now);
                Writes++;
            }
        }

        /// <summary>The asker's ghost puts its weapon away now and its attack ends (nothing when it is sheathed already).</summary>
        void SheatheOn(Entity connection)
        {
            EntityManager em = EntityManager;
            if (!em.HasComponent<NetworkId>(connection)) return;
            if (!GhostBodyRegistry.TryGetByNetworkId(World, em.GetComponentData<NetworkId>(connection).Value, out GhostBodyEntry entry)
                || !em.Exists(entry.Ghost) || !em.HasComponent<CombatState>(entry.Ghost)) return;
            var s = em.GetComponentData<CombatState>(entry.Ghost);
            MeleeRules.Sheathe(ref s);
            em.SetComponentData(entry.Ghost, s);
        }
    }

    /// <summary>
    /// Client: every view plays the emote the server shows for its player. Last in presentation: a sheathe that came with the emote
    /// (the ghost's weapon, WeaponStancePresentSystem) starts first, so the emote waits for it as NGO's does instead of being cut short.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup), OrderLast = true)]
    public partial class EmoteClientSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<PlayerEmoteTag>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out IBodyEmotes emotes)) continue;
                PlayerEmoteTag tag = em.GetComponentData<PlayerEmoteTag>(entry.Ghost);
                emotes.MirrorState(new BodyEmoteState { key = tag.Key.ToString(), serial = tag.Serial, stance = tag.Stance != 0 });
            }
        }
    }
}
