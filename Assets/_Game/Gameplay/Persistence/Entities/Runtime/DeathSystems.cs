using System.Collections.Generic;
using KryticalNova.Player;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// Server (M4 step 6a): each player's server body runs NGO's death clock, the respawn countdown and the return to a spawn point
    /// (<see cref="PlayerDeathMotor.ServerBodyTick"/>). The body switches the motor off (the ghost moves it), so its timer is driven
    /// from here; the death itself is heard by the motor from the body's CombatEntity. Each death and return goes on the ghost's
    /// <see cref="DeathState"/> for every view (M4 6b).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class PlayerDeathServerSystem : SystemBase
    {
        readonly Dictionary<PlayerDeathMotor, int> m_Written = new Dictionary<PlayerDeathMotor, int>();

        protected override void OnUpdate()
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry entry in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(entry.World, World) || entry.Body == null || !entry.Body.TryGetComponent(out PlayerDeathMotor death)) continue;
                death.ServerBodyTick(deltaTime);
                if (m_Written.TryGetValue(death, out int written) && written == death.ServerBodyChanges) continue;
                if (!em.Exists(entry.Ghost) || !em.HasComponent<DeathState>(entry.Ghost)) continue;
                m_Written[death] = death.ServerBodyChanges;
                DeathState state = em.GetComponentData<DeathState>(entry.Ghost);
                Vector3 direction = death.ServerBodyDirection;
                em.SetComponentData(entry.Ghost, new DeathState
                {
                    Seq = (byte)(state.Seq + 1),
                    Pose = death.ServerBodyPose,
                    Critical = (byte)(death.ServerBodyCritical ? 1 : 0),
                    DirXPermille = (short)math.clamp((int)math.round(direction.x * 1000f), -1000, 1000),
                    DirZPermille = (short)math.clamp((int)math.round(direction.z * 1000f), -1000, 1000),
                    KillerGhostId = GhostIdOf(em, death.ServerBodyKiller),
                });
                // a fall: the body travels to NGO's landing on the ghost's own plan (M4 6b-2); a de-rez stays where it was
                if (death.ServerBodyPose != 0 && death.ServerBodyKnocks && em.HasComponent<KnockPlan>(entry.Ghost) && em.HasComponent<PlayerPose>(entry.Ghost)
                    && SystemAPI.TryGetSingleton(out NetworkTime time) && time.ServerTick.IsValid)
                {
                    PlayerPose pose = em.GetComponentData<PlayerPose>(entry.Ghost);
                    KnockPlan knock = em.GetComponentData<KnockPlan>(entry.Ghost);
                    Vector3 landing = death.ServerBodyLanding;
                    em.SetComponentData(entry.Ghost, new KnockPlan
                    {
                        Seq = (byte)(knock.Seq == byte.MaxValue ? 1 : knock.Seq + 1),
                        Chain = death.ServerBodyChain,
                        StartTick = time.ServerTick,
                        FromXMm = pose.XMm, FromYMm = pose.YMm, FromZMm = pose.ZMm,
                        ToXMm = (int)math.round(landing.x * 1000f), ToYMm = (int)math.round(landing.y * 1000f), ToZMm = (int)math.round(landing.z * 1000f),
                        FromYawCdeg = pose.YawCdeg,
                        YawCdeg = ((int)math.round(death.ServerBodyFacing * 100f) % 36000 + 36000) % 36000,
                    });
                }
            }
        }

        int GhostIdOf(EntityManager em, Transform body)
        {
            if (body == null) return 0;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && (body == e.Body.transform || body.IsChildOf(e.Body.transform))
                    && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost))
                    return em.GetComponentData<GhostInstance>(e.Ghost).ghostId;
            return 0;
        }
    }

    /// <summary>
    /// Client (M4 6b): every view of a player, the own one included, plays its deaths from the ghost's <see cref="DeathState"/> as NGO's
    /// DeathClientRpc / DisintegrateClientRpc / RevivedClientRpc did, each once; a view that first meets a player already down lays it
    /// there without the fall (NGO's late joiner reading netPose).
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class PlayerDeathPresentSystem : SystemBase
    {
        readonly Dictionary<Entity, byte> m_Seen = new Dictionary<Entity, byte>();

        protected override void OnUpdate()
        {
            EntityManager em = EntityManager;
            foreach (GhostBodyEntry player in GhostBodyRegistry.Entries)
            {
                if (!ReferenceEquals(player.World, World) || player.Body == null || !em.Exists(player.Ghost) || !em.HasComponent<DeathState>(player.Ghost)
                    || !player.Body.TryGetComponent(out PlayerDeathMotor death)) continue;
                DeathState now = em.GetComponentData<DeathState>(player.Ghost);
                bool met = m_Seen.TryGetValue(player.Ghost, out byte seen);
                m_Seen[player.Ghost] = now.Seq;
                switch (DeathStates.What(met, seen, now))
                {
                    case DeathShow.LieDown: death.ShowLyingFromGhost(now.Pose); break;
                    case DeathShow.Fall:
                        death.ShowDeathFromGhost(now.Pose, now.Critical != 0, new Vector3(now.DirXPermille, 0f, now.DirZPermille) * 0.001f,
                            BodyOf(em, now.KillerGhostId));
                        break;
                    case DeathShow.GetUp: death.ShowRevivedFromGhost(); break;
                }
            }
        }

        Transform BodyOf(EntityManager em, int ghostId)
        {
            if (ghostId == 0) return null;
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, World) && e.Body != null && em.Exists(e.Ghost) && em.HasComponent<GhostInstance>(e.Ghost)
                    && em.GetComponentData<GhostInstance>(e.Ghost).ghostId == ghostId)
                    return e.Body.transform;
            return null;
        }
    }
}
