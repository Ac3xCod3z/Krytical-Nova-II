using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Player.Entities.Presentation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>
    /// Server: <see cref="IMountCompanionLink"/> for the bodies of the entity netcode: the four boxes' companion HP and rest go to the
    /// body's own connection (as <c>EntityMountOwnerLink</c> sends the boxes' EXP and hunger).
    /// </summary>
    public sealed class EntityMountCompanionLink : IMountCompanionLink
    {
        /// <summary>Messages sent (tests).</summary>
        public static int Sent { get; private set; }

        public bool Companion(Component part, byte isOut, byte stance, ushort hp1, ushort hp2, ushort hp3, ushort hp4, uint rest1, uint rest2, uint rest3, uint rest4)
        {
            BodyRoleTag tag = BodyRoleTag.Of(part);
            if (tag == null || tag.Kind != NetRoleKind.ServerBody || tag.Key == PlayerKey.None) return false;
            if (!GhostBodyRegistry.TryGetByKey(tag.Key, out GhostBodyEntry entry) || entry.World == null || !entry.World.IsCreated) return false;
            EntityManager em = entry.World.EntityManager;
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkId>(), ComponentType.ReadOnly<NetworkStreamInGame>());
            using var connections = query.ToEntityArray(Allocator.Temp);
            using var ids = query.ToComponentDataArray<NetworkId>(Allocator.Temp);
            for (int i = 0; i < connections.Length; i++)
            {
                if (ids[i].Value != entry.NetworkId) continue;
                Entity rpc = em.CreateEntity();
                em.AddComponentData(rpc, new MountCompanionBookRpc
                {
                    Out = isOut, Stance = stance, Hp1 = hp1, Hp2 = hp2, Hp3 = hp3, Hp4 = hp4, Rest1 = rest1, Rest2 = rest2, Rest3 = rest3, Rest4 = rest4,
                });
                em.AddComponentData(rpc, new SendRpcCommandRequest { TargetConnection = connections[i] });
                Sent++;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Server (day list item 19, phases 1-2): one Mount Companion ghost for every server body whose <see cref="PlayerMounts"/> has it out.
    /// It follows the owner's player ghost by <see cref="CompanionFollower"/> (every <see cref="MountSettings.companionFollowSeconds"/>),
    /// carries the HP, stance and warps in its <see cref="CompanionView"/>, and keeps a hidden <see cref="CompanionBody"/> where it stands so
    /// enemy players' area skills reach it. Sent home or knocked out, it shows the going for a moment (so every view plays the vanish) and
    /// is then destroyed; a body that leaves the world (logout, map change) takes its companion at once.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CompanionServerSystem : SystemBase
    {
        sealed class Live
        {
            public Entity Ghost;
            public readonly CompanionFollower Follow = new CompanionFollower();
            public CompanionBody Body;
            public Vector3 LastOwner;
            public bool HasLast, Leaving;
            public float Wait, LeaveAt;
            public int KnockOutsSeen;
            public byte Warps;
        }

        readonly Dictionary<GameObject, Live> m_Live = new Dictionary<GameObject, Live>();
        readonly HashSet<GameObject> m_Seen = new HashSet<GameObject>();
        readonly List<GameObject> m_Drop = new List<GameObject>();

        /// <summary>Companions spawned and destroyed (tests).</summary>
        public int Spawned { get; private set; }
        public int Destroyed { get; private set; }
        public int LiveCount => m_Live.Count;

        protected override void OnCreate()
        {
            RequireForUpdate<MountCompanionGhostPrefab>();
            MountCompanionLink.Current ??= new EntityMountCompanionLink();
        }

        protected override void OnDestroy()
        {
            foreach (Live live in m_Live.Values) if (live.Body != null) Object.Destroy(live.Body.gameObject);
            m_Live.Clear();
        }

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            float dt = SystemAPI.Time.DeltaTime;
            float now = (float)SystemAPI.Time.ElapsedTime;
            Entity prefab = SystemAPI.GetSingleton<MountCompanionGhostPrefab>().Prefab;
            m_Seen.Clear();
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !em.Exists(entry.Ghost) || !em.HasComponent<LocalTransform>(entry.Ghost)
                    || !entry.Body.TryGetComponent(out PlayerMounts mounts)) continue;
                m_Seen.Add(entry.Body);
                m_Live.TryGetValue(entry.Body, out Live live);
                IBodyMountCompanion c = mounts;
                MountSettings settings = mounts.Settings;
                LocalTransform owner = em.GetComponentData<LocalTransform>(entry.Ghost);
                Vector3 ownerAt = owner.Position;
                float ownerYaw = YawOf(owner.Rotation);
                if (c.CompanionOut)
                {
                    if (live != null && live.Leaving) { DestroyGhost(em, live); live = null; }
                    live ??= Spawn(em, prefab, entry.Body, mounts, ownerAt, ownerYaw, now);
                    Step(em, entry, live, mounts, settings, ownerAt, ownerYaw, dt, now);
                }
                else if (live != null && !live.Leaving) BeginLeave(em, live, c, settings, now);
            }

            m_Drop.Clear();
            foreach (KeyValuePair<GameObject, Live> kv in m_Live)
            {
                bool bodyGone = kv.Key == null || !m_Seen.Contains(kv.Key);
                if (bodyGone || (kv.Value.Leaving && now >= kv.Value.LeaveAt)) m_Drop.Add(kv.Key);
            }
            foreach (GameObject body in m_Drop)
            {
                DestroyGhost(em, m_Live[body]);
                m_Live.Remove(body);
            }
        }

        Live Spawn(EntityManager em, Entity prefab, GameObject body, PlayerMounts mounts, Vector3 ownerAt, float ownerYaw, float now)
        {
            MountSettings settings = mounts.Settings;
            CompanionTuning t = settings != null ? settings.Companion : CompanionTuning.Default;
            var live = new Live { Ghost = em.Instantiate(prefab), KnockOutsSeen = ((IBodyMountCompanion)mounts).CompanionKnockOuts };
            Vector3 spot = CompanionRules.RestSpot(ownerAt, ownerYaw, t);
            live.Follow.Place(spot, ownerYaw);
            live.Body = CompanionBody.Make(mounts, settings != null ? settings.companionBodyRadius : 0.9f, settings != null ? settings.companionBodyHeight : 2.2f);
            live.Body.transform.position = spot;
            em.SetComponentData(live.Ghost, LocalTransform.FromPositionRotation(spot, quaternion.RotateY(math.radians(ownerYaw))));
            m_Live[body] = live;
            Spawned++;
            Debug.Log($"[KnCompanion] server: spawned for {body.name} at {spot}");
            return live;
        }

        void Step(EntityManager em, GhostBodyEntry entry, Live live, PlayerMounts mounts, MountSettings settings, Vector3 ownerAt, float ownerYaw, float dt, float now)
        {
            IBodyMountCompanion c = mounts;
            live.Wait += dt;
            float every = settings != null ? settings.companionFollowSeconds : 0.1f;
            if (live.Wait >= every)
            {
                float step = live.Wait;
                live.Wait = 0f;
                float ownerSpeed = live.HasLast ? CompanionFlat(ownerAt, live.LastOwner) / step : 0f;
                live.LastOwner = ownerAt;
                live.HasLast = true;
                CompanionTuning t = settings != null ? settings.Companion : CompanionTuning.Default;
                if (live.Follow.Step(ownerAt, ownerYaw, ownerSpeed, c.CompanionRunSpeed, step, now, t) == CompanionFollower.Result.Warped) live.Warps++;
                var place = LocalTransform.FromPositionRotation(live.Follow.Position, quaternion.RotateY(math.radians(live.Follow.Yaw)));
                LocalTransform had = em.GetComponentData<LocalTransform>(live.Ghost);
                if (!had.Position.Equals(place.Position) || !had.Rotation.Equals(place.Rotation)) em.SetComponentData(live.Ghost, place);
                if (live.Body != null) live.Body.transform.position = live.Follow.Position;
            }
            int ownerGhostId = em.HasComponent<GhostInstance>(entry.Ghost) ? em.GetComponentData<GhostInstance>(entry.Ghost).ghostId : 0;
            var view = new CompanionView
            {
                OwnerGhostId = ownerGhostId, Box = (byte)c.CompanionBox, Stage = (byte)c.CompanionStage,
                SpeedStep = (byte)math.clamp((int)math.round(live.Follow.Speed / CompanionView.SpeedStepMetres), 0, 255),
                State = (byte)CompanionState.Out, Stance = c.CompanionStance, HpPermille = (ushort)math.clamp(c.CompanionHpPermille, 0, CompanionRules.Permille),
                Warps = live.Warps,
            };
            WriteView(em, live.Ghost, view);
        }

        void BeginLeave(EntityManager em, Live live, IBodyMountCompanion c, MountSettings settings, float now)
        {
            live.Leaving = true;
            live.LeaveAt = now + (settings != null ? settings.companionVanishSeconds : 0.6f);
            bool knockedOut = c.CompanionKnockOuts != live.KnockOutsSeen;
            CompanionView view = em.GetComponentData<CompanionView>(live.Ghost);
            view.State = (byte)(knockedOut ? CompanionState.KnockedOut : CompanionState.Leaving);
            view.SpeedStep = 0;
            if (knockedOut) view.HpPermille = 0;
            WriteView(em, live.Ghost, view);
            if (live.Body != null) { Object.Destroy(live.Body.gameObject); live.Body = null; }
            Debug.Log($"[KnCompanion] server: {(knockedOut ? "knocked out" : "going home")}");
        }

        void DestroyGhost(EntityManager em, Live live)
        {
            if (live.Body != null) { Object.Destroy(live.Body.gameObject); live.Body = null; }
            if (em.Exists(live.Ghost)) em.DestroyEntity(live.Ghost);
            Destroyed++;
        }

        static void WriteView(EntityManager em, Entity ghost, CompanionView view)
        {
            CompanionView had = em.GetComponentData<CompanionView>(ghost);
            if (had.OwnerGhostId == view.OwnerGhostId && had.Box == view.Box && had.Stage == view.Stage && had.SpeedStep == view.SpeedStep
                && had.State == view.State && had.Stance == view.Stance && had.HpPermille == view.HpPermille && had.Warps == view.Warps) return;
            em.SetComponentData(ghost, view);
        }

        static float CompanionFlat(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>Degrees about Y of a rotation's forward.</summary>
        public static float YawOf(quaternion rotation)
        {
            float3 f = math.mul(rotation, new float3(0f, 0f, 1f));
            return math.degrees(math.atan2(f.x, f.z));
        }
    }
}
