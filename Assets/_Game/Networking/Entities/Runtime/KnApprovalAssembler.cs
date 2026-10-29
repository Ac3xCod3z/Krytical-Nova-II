using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace KryticalNova.Networking.Entities
{
    /// <summary>
    /// Server: collects the <see cref="KnApprovalRequest"/> chunks of every connection that is not yet approved, keeps them within
    /// a pre-approval budget (<see cref="KnApprovalLimits"/>: chunks, bytes, seconds), hands the finished payload to the
    /// <see cref="IKnJoinCheck"/> and polls it each tick. Accepted: adds <c>ConnectionApproved</c>. Refused or over budget:
    /// sends <see cref="JoinRefusedRpc"/> and closes the connection a few ticks later (<see cref="KnPendingDisconnect"/>).
    /// Managed on purpose (strings, the check object); runs once per tick, not predicted.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    public partial class KnApprovalAssembler : SystemBase
    {
        /// <summary>The rule that decides a join. Set it before clients connect; replaced per test. Default: the factory, else accept all.</summary>
        public IKnJoinCheck JoinCheck;

        /// <summary>Ticks between the refusal RPC and closing the connection (the RPC leaves in the same tick it is created).</summary>
        public int RefuseDisconnectTicks = 5;

        private sealed class Entry
        {
            public string[] Parts;
            public int Have;
            public int ChunksSeen;
            public int Bytes;
            public double FirstSeen;
            public bool Complete;
            public string Payload;
            public bool Decided;
        }

        private readonly Dictionary<Entity, Entry> m_Entries = new Dictionary<Entity, Entry>();
        private readonly List<Entity> m_Keys = new List<Entity>();
        private EntityQuery m_Chunks;
        private EntityQuery m_Limits;
        private EntityQuery m_Stopping;
        private EntityQuery m_Approved;

        /// <summary>Connections let in so far that are still open (what "server full" counts). Main thread.</summary>
        public int ApprovedCount => m_Approved.CalculateEntityCount();

        protected override void OnCreate()
        {
            m_Chunks = GetEntityQuery(ComponentType.ReadOnly<ReceiveRpcCommandRequest>(), ComponentType.ReadOnly<KnApprovalRequest>());
            m_Limits = GetEntityQuery(ComponentType.ReadOnly<KnApprovalLimits>());
            m_Stopping = GetEntityQuery(ComponentType.ReadOnly<KnServerStopping>());
            m_Approved = GetEntityQuery(ComponentType.ReadOnly<ConnectionApproved>());
            JoinCheck = KnBootstrap.JoinCheckFactory?.Invoke() ?? new AcceptAllJoinCheck();
        }

        protected override void OnUpdate()
        {
            KnApprovalLimits limits = m_Limits.TryGetSingleton(out KnApprovalLimits custom) ? custom : KnApprovalLimits.Default;
            double now = World.Time.ElapsedTime;
            bool stopping = !m_Stopping.IsEmpty;
            var ecb = new EntityCommandBuffer(Allocator.Temp);

            if (!m_Chunks.IsEmpty)
            {
                var rpcEntities = m_Chunks.ToEntityArray(Allocator.Temp);
                var requests = m_Chunks.ToComponentDataArray<ReceiveRpcCommandRequest>(Allocator.Temp);
                var chunks = m_Chunks.ToComponentDataArray<KnApprovalRequest>(Allocator.Temp);
                for (int i = 0; i < rpcEntities.Length; i++)
                {
                    ecb.DestroyEntity(rpcEntities[i]);
                    Entity connection = requests[i].SourceConnection;
                    if (!EntityManager.Exists(connection)) continue;
                    if (!m_Entries.TryGetValue(connection, out Entry entry))
                        m_Entries[connection] = entry = new Entry { FirstSeen = now };
                    if (!entry.Decided) Take(ecb, connection, entry, chunks[i], limits);
                }
                rpcEntities.Dispose();
                requests.Dispose();
                chunks.Dispose();
            }

            m_Keys.Clear();
            m_Keys.AddRange(m_Entries.Keys);
            for (int i = 0; i < m_Keys.Count; i++)
            {
                Entity connection = m_Keys[i];
                Entry entry = m_Entries[connection];
                if (!EntityManager.Exists(connection))
                {
                    JoinCheck.Forget(connection);
                    m_Entries.Remove(connection);
                    continue;
                }
                if (entry.Decided) continue; // kept until the connection is gone, so late chunks are swallowed
                if (stopping)
                    Refuse(ecb, connection, entry, KnRefuseReason.ServerStopping, "The server is restarting. Join again in a minute.");
                else if (now - entry.FirstSeen > limits.MaxSeconds)
                    Refuse(ecb, connection, entry, KnRefuseReason.TimedOut, "The join took too long.");
                else if (entry.Complete)
                    Apply(ecb, connection, entry, JoinCheck.Poll(connection, entry.Payload));
            }

            ecb.Playback(EntityManager);
            ecb.Dispose();
        }

        protected override void OnDestroy()
        {
            foreach (Entity connection in m_Entries.Keys) JoinCheck?.Forget(connection);
            m_Entries.Clear();
        }

        private void Take(EntityCommandBuffer ecb, Entity connection, Entry entry, in KnApprovalRequest chunk, in KnApprovalLimits limits)
        {
            entry.ChunksSeen++;
            entry.Bytes += chunk.Data.Length;
            if (chunk.Count > limits.MaxChunks || entry.ChunksSeen > limits.MaxChunks)
                Refuse(ecb, connection, entry, KnRefuseReason.TooManyChunks, "The join request is split into too many parts.");
            else if (entry.Bytes > limits.MaxTotalBytes)
                Refuse(ecb, connection, entry, KnRefuseReason.TooManyBytes, "The join request is too large.");
            else if (chunk.Count < 1 || chunk.Index < 0 || chunk.Index >= chunk.Count || (entry.Parts != null && entry.Parts.Length != chunk.Count))
                Refuse(ecb, connection, entry, KnRefuseReason.BadRequest, "The join request is malformed.");
            else
            {
                entry.Parts ??= new string[chunk.Count];
                if (entry.Parts[chunk.Index] == null)
                {
                    entry.Parts[chunk.Index] = chunk.Data.ToString();
                    entry.Have++;
                }
                if (entry.Have == entry.Parts.Length && !entry.Complete)
                {
                    entry.Complete = true;
                    entry.Payload = KnApprovalChunks.Join(entry.Parts);
                }
            }
        }

        private void Apply(EntityCommandBuffer ecb, Entity connection, Entry entry, KnJoinVerdict verdict)
        {
            switch (verdict.Status)
            {
                case KnJoinStatus.Accepted:
                    entry.Decided = true;
                    JoinCheck.Forget(connection);
                    // Direct, not through the buffer: the next join check in this same tick must already count this connection (server full).
                    EntityManager.AddComponent<ConnectionApproved>(connection);
                    if (!string.IsNullOrEmpty(verdict.Account))
                    {
                        EntityManager.AddComponentData(connection, new ConnectionAccount
                        {
                            Account = KnFixed.Text64(verdict.Account),
                            OldAccount = KnFixed.Text64(verdict.OldAccount),
                            Proven = verdict.Proven,
                        });
                    }
                    break;
                case KnJoinStatus.Refused:
                    Refuse(ecb, connection, entry, verdict.Reason, verdict.Detail);
                    break;
            }
        }

        private void Refuse(EntityCommandBuffer ecb, Entity connection, Entry entry, KnRefuseReason reason, string detail)
        {
            entry.Decided = true;
            JoinCheck.Forget(connection);
            Entity rpc = ecb.CreateEntity();
            ecb.AddComponent(rpc, new JoinRefusedRpc { Reason = (byte)reason, Detail = KnFixed.Text512(detail) });
            ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
            ecb.AddComponent(connection, new KnPendingDisconnect { TicksLeft = RefuseDisconnectTicks });
        }
    }
}
