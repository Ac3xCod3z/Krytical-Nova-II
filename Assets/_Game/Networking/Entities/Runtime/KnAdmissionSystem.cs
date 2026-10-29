using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Server: the group where game rules run each tick, after the ghost simulation has moved everything (plan section 2; M3a-2b
    /// design R7). Admission lives here, and the player-body sync systems of the next steps. The placement is proven by a test
    /// that records the order of a probe system against <see cref="GhostSimulationSystemGroup"/>.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(GhostSimulationSystemGroup))]
    public partial class KnRulesGroup : ComponentSystemGroup
    {
    }

    /// <summary>
    /// Server: turns the pages of a <see cref="GoInGameRequest"/> into one <see cref="KnAdmissionRequest"/> per connection and hands it to
    /// <see cref="Admission"/> (the game). Admitted: the connection gets <see cref="NetworkStreamInGame"/> and snapshots start.
    /// Refused, or no admission installed: the player is told why and disconnected. A request counts once per connection; one from a
    /// connection whose key has died, that is malformed, or that comes after the decision, is dropped.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(KnRulesGroup))]
    public partial class KnAdmissionSystem : SystemBase
    {
        public const string NotReadyText = "This server cannot let players in yet. Try again in a moment.";
        public const string MalformedText = "The server could not understand which character to play. Go back to character select and choose again.";
        public const string FailedText = "The server could not let you in. Try again in a moment.";

        /// <summary>The game's admission; null = every request is refused with <see cref="NotReadyText"/> (never a silent hang).</summary>
        public IKnAdmission Admission;

        private sealed class Pending
        {
            public string CharacterId;
            public string Name;
            public readonly GoInGamePages.Assembler Pages = new GoInGamePages.Assembler();
        }

        private readonly Dictionary<Entity, Pending> m_Pending = new Dictionary<Entity, Pending>();
        private readonly List<Entity> m_Gone = new List<Entity>();
        private EntityQuery m_Requests;

        /// <summary>Requests handed to the game, refusals sent (for tests and the status page).</summary>
        public int Admitted { get; private set; }
        public int Refused { get; private set; }

        protected override void OnCreate()
        {
            m_Requests = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<GoInGameRequest>());
            RequireForUpdate(m_Requests);
        }

        protected override void OnUpdate()
        {
            ForgetGoneConnections();
            var rpcs = m_Requests.ToEntityArray(Allocator.Temp);
            var received = m_Requests.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
            var requests = m_Requests.ToComponentDataArray<GoInGameRequest>(Allocator.Temp);
            EntityManager.DestroyEntity(m_Requests); // taken; the loop works on the copies
            for (int i = 0; i < rpcs.Length; i++) Take(received[i].SourceConnection, requests[i]);
            rpcs.Dispose();
            received.Dispose();
            requests.Dispose();
        }

        private void ForgetGoneConnections()
        {
            if (m_Pending.Count == 0) return;
            m_Gone.Clear();
            foreach (Entity connection in m_Pending.Keys)
                if (!EntityManager.Exists(connection) || !EntityManager.HasComponent<ConnectionPlayerKey>(connection)) m_Gone.Add(connection);
            foreach (Entity connection in m_Gone) m_Pending.Remove(connection);
        }

        private void Take(Entity connection, in GoInGameRequest request)
        {
            if (connection == Entity.Null || !EntityManager.Exists(connection) || !EntityManager.HasComponent<ConnectionPlayerKey>(connection)) return;
            ConnectionPlayerKey owner = EntityManager.GetComponentData<ConnectionPlayerKey>(connection);
            if (!PlayerKeys.IsAlive(owner.PlayerKey)) return;                       // the key died: nobody is there to answer
            if (EntityManager.HasComponent<KnAdmissionDone>(connection)) return;    // decided already: a repeat changes nothing

            if (!m_Pending.TryGetValue(connection, out Pending pending))
            {
                pending = new Pending { CharacterId = request.CharacterId.ToString(), Name = request.Name.ToString() };
                m_Pending[connection] = pending;
            }
            else if (pending.CharacterId != request.CharacterId.ToString() || pending.Name != request.Name.ToString())
            {
                Decide(connection, owner, MalformedText);
                return;
            }

            KnRosterPages.Add result = pending.Pages.Take(request.Page, request.PageCount, request.Chunk.ToString());
            if (result == KnRosterPages.Add.Bad) { Decide(connection, owner, MalformedText); return; }
            if (result != KnRosterPages.Add.Complete) return;

            var admission = new KnAdmissionRequest
            {
                Connection = connection,
                Key = owner.PlayerKey,
                Serial = owner.Serial,
                NetworkId = EntityManager.HasComponent<NetworkId>(connection) ? EntityManager.GetComponentData<NetworkId>(connection).Value : 0,
                CharacterId = pending.CharacterId,
                Name = pending.Name,
                RecipeText = pending.Pages.Text,
            };
            string refusal;
            if (Admission == null) refusal = NotReadyText;
            else
            {
                try { refusal = Admission.Admit(admission); }
                catch (System.Exception e)
                {
                    Debug.LogError("[KnServer] admission threw for " + owner.PlayerKey + ": " + e);
                    refusal = FailedText;
                }
            }
            Decide(connection, owner, refusal);
        }

        // refusal null = admitted
        private void Decide(Entity connection, ConnectionPlayerKey owner, string refusal)
        {
            m_Pending.Remove(connection);
            EntityManager.AddComponent<KnAdmissionDone>(connection);
            if (refusal == null)
            {
                Admitted++;
                if (!EntityManager.HasComponent<NetworkStreamInGame>(connection)) EntityManager.AddComponent<NetworkStreamInGame>(connection);
                return;
            }
            Refused++;
            KnPlayerRefusals.Enqueue(owner.PlayerKey, refusal);
        }
    }
}
