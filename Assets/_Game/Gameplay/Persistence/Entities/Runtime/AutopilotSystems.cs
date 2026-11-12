using System.Collections.Generic;
using KryticalNova.Autopilot;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>The owner asks the server to switch Autopilot on or off (NGO's <c>SetOnServerRpc</c>); the server decides and may refuse, saying why.</summary>
    public struct AutopilotSetOnRpc : IRpcCommand
    {
        public byte On;
    }

    /// <summary>The owner's Autopilot choices (NGO's <c>SetPreferencesServerRpc</c>): JSON of at most 1018 bytes in two halves (about 515 at its largest, so one packet); the server brings them inside their limits.</summary>
    public struct AutopilotPrefsRpc : IRpcCommand
    {
        public FixedString512Bytes Json, JsonMore;   // two halves: 1018 B in all, the RPC stays under the 1300 B packet
    }

    /// <summary>The server's word to the owner (NGO's netOn, netExpiresUtc, netAnchor and PreferencesRpc): on or off, where, until when, and the choices.</summary>
    public struct AutopilotStateRpc : IRpcCommand
    {
        public byte On;
        public float3 Anchor;
        public long ExpiresUtc;
        public FixedString512Bytes Preferences, PreferencesMore;   // two halves, as AutopilotPrefsRpc
    }

    /// <summary>
    /// The preferences JSON in two FixedString512Bytes halves (Unity.Collections has no 1 KB string): at most <see cref="MaxBytes"/>
    /// bytes, split on a character boundary. The largest real preferences are about 515 B.
    /// </summary>
    public static class AutopilotRpcText
    {
        public static int MaxBytes => 2 * FixedString512Bytes.UTF8MaxLengthInBytes;

        public static bool TrySplit(string text, out FixedString512Bytes first, out FixedString512Bytes second)
        {
            first = default; second = default;
            text ??= "";
            if (System.Text.Encoding.UTF8.GetByteCount(text) > MaxBytes) return false;
            int cut = 0, bytes = 0;
            while (cut < text.Length)
            {
                int step = char.IsHighSurrogate(text[cut]) && cut + 1 < text.Length ? 2 : 1;
                int size = System.Text.Encoding.UTF8.GetByteCount(text.Substring(cut, step));
                if (bytes + size > FixedString512Bytes.UTF8MaxLengthInBytes) break;
                bytes += size; cut += step;
            }
            string rest = text.Substring(cut);
            if (System.Text.Encoding.UTF8.GetByteCount(rest) > FixedString512Bytes.UTF8MaxLengthInBytes) return false;
            first = new FixedString512Bytes(text.Substring(0, cut));
            second = new FixedString512Bytes(rest);
            return true;
        }

        public static string Join(in FixedString512Bytes first, in FixedString512Bytes second) => first.ToString() + second.ToString();
    }

    /// <summary>A message for the owner from Autopilot (NGO's <c>TellRpc</c>): why it did not start, why it stopped, a Pass used.</summary>
    public struct AutopilotTellRpc : IRpcCommand
    {
        public FixedString512Bytes Text;
    }

    /// <summary>
    /// Server: Autopilot's side of the entity netcode (wave 2, 12 Nov 2026). The owner's requests (Account limit, as NGO) go to the server body's
    /// <see cref="PlayerAutopilot"/>, which alone decides; its state goes to the owner whenever it changes (and on arrival), its messages go to the
    /// owner, and its on flag goes to every client as the ghost's <see cref="AutopilotTag"/> (the AUTO on a namecard). Nothing here saves or spends.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AutopilotServerSystem : SystemBase
    {
        EntityQuery m_SetOns, m_Prefs, m_InGame;
        readonly Dictionary<Entity, string> m_Told = new Dictionary<Entity, string>();
        readonly List<(PlayerAutopilot autopilot, string text)> m_Texts = new List<(PlayerAutopilot, string)>();
        readonly List<Entity> m_Gone = new List<Entity>();

        /// <summary>Requests taken, states sent, messages sent (tests).</summary>
        public int Asked { get; private set; }
        public int StatesSent { get; private set; }
        public int TellsSent { get; private set; }

        protected override void OnCreate()
        {
            m_SetOns = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<AutopilotSetOnRpc>());
            m_Prefs = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<AutopilotPrefsRpc>());
            m_InGame = GetEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            PlayerAutopilot.ServerTold += OnTold;
        }

        protected override void OnDestroy() => PlayerAutopilot.ServerTold -= OnTold;

        void OnTold(PlayerAutopilot autopilot, string text) => m_Texts.Add((autopilot, text));

        protected override void OnUpdate()
        {
            var em = EntityManager;
            if (!m_SetOns.IsEmpty)
            {
                using var received = m_SetOns.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_SetOns.ToComponentDataArray<AutopilotSetOnRpc>(Allocator.Temp);
                em.DestroyEntity(m_SetOns);
                for (int i = 0; i < asks.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Account, out GameObject body) && body.TryGetComponent(out PlayerAutopilot autopilot))
                    {
                        bool on = asks[i].On != 0;
                        autopilot.ServerSetOn(on, on ? null : "Autopilot off.");
                        Asked++;
                    }
            }
            if (!m_Prefs.IsEmpty)
            {
                using var received = m_Prefs.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                using var asks = m_Prefs.ToComponentDataArray<AutopilotPrefsRpc>(Allocator.Temp);
                em.DestroyEntity(m_Prefs);
                for (int i = 0; i < asks.Length; i++)
                    if (PlayerRequestServerSystem.TryBody(World, received[i].SourceConnection, CallKind.Account, out GameObject body) && body.TryGetComponent(out PlayerAutopilot autopilot))
                    {
                        autopilot.ServerSetPreferences(AutopilotRpcText.Join(asks[i].Json, asks[i].JsonMore));
                        Asked++;
                    }
            }

            m_Gone.Clear();
            foreach (Entity ghost in m_Told.Keys) if (!em.Exists(ghost)) m_Gone.Add(ghost);
            foreach (Entity ghost in m_Gone) m_Told.Remove(ghost);
            using var connections = m_InGame.ToEntityArray(Allocator.Temp);
            using var ids = m_InGame.ToComponentDataArray<NetworkId>(Allocator.Temp);
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !entry.Body.TryGetComponent(out PlayerAutopilot autopilot)) continue;
                var state = autopilot.StateForOwner();
                if (em.HasComponent<AutopilotTag>(entry.Ghost) && em.GetComponentData<AutopilotTag>(entry.Ghost).On != (byte)(state.on ? 1 : 0))
                    em.SetComponentData(entry.Ghost, new AutopilotTag { On = (byte)(state.on ? 1 : 0) });
                Entity connection = Connection(connections, ids, entry.NetworkId);
                if (connection == Entity.Null) continue;
                string key = state.on + "|" + state.anchor + "|" + state.expiresUtc + "|" + state.preferences;
                if (m_Told.TryGetValue(entry.Ghost, out string told) && told == key) continue;
                if (!AutopilotRpcText.TrySplit(state.preferences, out FixedString512Bytes prefs, out FixedString512Bytes prefsMore))
                {
                    Debug.LogError($"[Autopilot] preferences of {System.Text.Encoding.UTF8.GetByteCount(state.preferences ?? "")} B do not fit the state RPC ({AutopilotRpcText.MaxBytes} B): not sent.");
                    continue;
                }
                m_Told[entry.Ghost] = key;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new AutopilotStateRpc
                {
                    On = (byte)(state.on ? 1 : 0), Anchor = new float3(state.anchor.x, state.anchor.y, state.anchor.z), ExpiresUtc = state.expiresUtc,
                    Preferences = prefs, PreferencesMore = prefsMore,
                });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                StatesSent++;
            }

            foreach ((PlayerAutopilot autopilot, string text) in m_Texts)
            {
                if (autopilot == null) continue;
                int networkId = -1;
                foreach (GhostBodyEntry known in GhostBodyRegistry.Entries)
                    if (ReferenceEquals(known.World, World) && known.Body == autopilot.gameObject) { networkId = known.NetworkId; break; }
                if (networkId < 0) continue;
                Entity connection = Connection(connections, ids, networkId);
                if (connection == Entity.Null) continue;
                Entity rpc = em.CreateEntity();
                var line = new FixedString512Bytes();
                line.CopyFromTruncated(text ?? string.Empty);
                em.AddComponentData(rpc, new AutopilotTellRpc { Text = line });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                TellsSent++;
            }
            m_Texts.Clear();
        }

        static Entity Connection(NativeArray<Entity> connections, NativeArray<NetworkId> ids, int networkId)
        {
            for (int i = 0; i < ids.Length; i++) if (ids[i].Value == networkId) return connections[i];
            return Entity.Null;
        }
    }

    /// <summary>
    /// Client: the owner's way to the server (<see cref="PlayerAutopilot.Link"/>), the server's word onto the own view, the messages, and every
    /// player's AUTO tag onto their body (read by the namecard). The own view only shows: it cannot switch itself on or give itself time.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class AutopilotClientSystem : SystemBase
    {
        sealed class Link : PlayerAutopilot.IOwnerLink
        {
            readonly World m_World;
            internal Link(World world) => m_World = world;

            public void SetOn(bool value)
            {
                if (m_World == null || !m_World.IsCreated) return;
                var em = m_World.EntityManager;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new AutopilotSetOnRpc { On = (byte)(value ? 1 : 0) });
                em.AddComponent<SendRpcCommandRequest>(rpc);   // no target: to the server
            }

            public void SendPreferences(string json)
            {
                if (m_World == null || !m_World.IsCreated || string.IsNullOrEmpty(json)) return;
                if (!AutopilotRpcText.TrySplit(json, out FixedString512Bytes half, out FixedString512Bytes more))
                { Debug.LogError($"[Autopilot] preferences of {System.Text.Encoding.UTF8.GetByteCount(json)} B do not fit the RPC ({AutopilotRpcText.MaxBytes} B): not sent."); return; }
                var em = m_World.EntityManager;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new AutopilotPrefsRpc { Json = half, JsonMore = more });
                em.AddComponent<SendRpcCommandRequest>(rpc);
            }
        }

        EntityQuery m_States, m_Tells;
        Link m_Link;
        AutopilotStateRpc m_Pending;
        bool m_HasPending;

        protected override void OnCreate()
        {
            m_States = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<AutopilotStateRpc>());
            m_Tells = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<AutopilotTellRpc>());
            m_Link = new Link(World);
            PlayerAutopilot.Link = m_Link;
        }

        protected override void OnDestroy()
        {
            if (ReferenceEquals(PlayerAutopilot.Link, m_Link)) PlayerAutopilot.Link = null;
        }

        protected override void OnUpdate()
        {
            if (PlayerAutopilot.Link == null) PlayerAutopilot.Link = m_Link;   // re-bound: a relog in the same process can leave the static null (as the GM link)
            var em = EntityManager;
            if (!m_States.IsEmpty)
            {
                using var states = m_States.ToComponentDataArray<AutopilotStateRpc>(Allocator.Temp);
                em.DestroyEntity(m_States);
                m_Pending = states[states.Length - 1];   // the last word is the one that counts
                m_HasPending = true;
            }
            if (!m_Tells.IsEmpty)
            {
                using var tells = m_Tells.ToComponentDataArray<AutopilotTellRpc>(Allocator.Temp);
                em.DestroyEntity(m_Tells);
                foreach (AutopilotTellRpc tell in tells) ClientPresentationRegistry.Messages?.ShowMessage(tell.Text.ToString());
            }
            bool tagsNeeded = false;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null) continue;
                if (entry.IsLocal && m_HasPending && entry.Body.TryGetComponent(out PlayerAutopilot own))
                {
                    AutopilotStateRpc s = m_Pending;
                    own.ApplyServerState(s.On != 0, new Vector3(s.Anchor.x, s.Anchor.y, s.Anchor.z), s.ExpiresUtc, AutopilotRpcText.Join(s.Preferences, s.PreferencesMore));
                    m_HasPending = false;   // the own view may arrive after the word: it is kept until a view takes it
                }
                if (!entry.IsLocal && em.Exists(entry.Ghost) && em.HasComponent<AutopilotTag>(entry.Ghost))
                {
                    PlayerAutopilot.SetRemoteAuto(entry.Body, em.GetComponentData<AutopilotTag>(entry.Ghost).On != 0);
                    tagsNeeded = true;
                }
            }
            if (!tagsNeeded) PlayerAutopilot.ClearRemoteAuto();
        }
    }
}
