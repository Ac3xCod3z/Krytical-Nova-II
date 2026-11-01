using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// The seams to the rest of the game for the player ghost: wiring the managed pieces into a world, spawning the ghost on the
    /// server, and the server-side calls other systems use (a walk, a placement). No state of its own, no statics.
    /// </summary>
    public static class PlayerEntitiesSetup
    {
        /// <summary>
        /// Gives a world its path planner and, in a client world, its command source. Call once after the world exists and before
        /// the first player acts: the planner for the server world, planner + source for the client world. The same planner object
        /// may serve several worlds in tests. Worlds without the matching system are left alone. The jump's planner goes to the server
        /// and to the owner's gather (its prediction); its cost to the server only.
        /// </summary>
        public static void Install(World world, IPathPlanner planner, IPlayerCommandSource source = null, IRunCost runCost = null,
            IClickBudget clickBudget = null, IJumpPlanner jumpPlanner = null, IJumpCost jumpCost = null, IVaultPlanner vaultPlanner = null,
            IWaterPlanner waterPlanner = null, IWaterFlux waterFlux = null, IWallPlanner wallPlanner = null, IWallGrip wallGrip = null)
        {
            // Wallstride's wall walk (M3c): its planner to the server, the owner's gather and the Grip system (the push-off); Grip to the server
            var grip = world.GetExistingSystemManaged<WallGripSystem>();
            if (grip != null)
            {
                grip.Planner = wallPlanner;
                grip.Grip = wallGrip;
            }
            // Waterstride (M3c): its planner to the server, the owner's gather and the Flux system (the way back); its Flux to the server
            var water = world.GetExistingSystemManaged<WaterstrideFluxSystem>();
            if (water != null)
            {
                water.Planner = waterPlanner;
                water.Flux = waterFlux;
            }
            var landing = world.GetExistingSystemManaged<WaterstrideLandingSystem>();
            if (landing != null) landing.Planner = waterPlanner;
            // the vault's planner, like the jump's, goes to the server and to the owner's gather (its prediction; M3c)
            var jump = world.GetExistingSystemManaged<PlayerJumpServerSystem>();
            if (jump != null)
            {
                jump.Planner = jumpPlanner;
                jump.Cost = jumpCost;
            }
            var pay = world.GetExistingSystemManaged<PlayerJumpPaySystem>();
            if (pay != null) pay.Cost = jumpCost;
            var server = world.GetExistingSystemManaged<PlayerServerPlanSystem>();
            if (server != null)
            {
                server.Planner = planner;
                server.RunCost = runCost;
                server.ClickBudget = clickBudget;
                server.VaultPlanner = vaultPlanner;
                server.WaterPlanner = waterPlanner;
                server.WallPlanner = wallPlanner;
            }
            var gather = world.GetExistingSystemManaged<PlayerInputGatherSystem>();
            if (gather != null)
            {
                gather.Planner = planner;
                gather.JumpPlanner = jumpPlanner;
                gather.VaultPlanner = vaultPlanner;
                gather.WaterPlanner = waterPlanner;
                gather.WallPlanner = wallPlanner;
                if (source != null) gather.Source = source;
            }
        }

        /// <summary>
        /// Server: makes the player ghost of one connection and puts it on a spot. <paramref name="networkId"/> is the connection's
        /// NetworkId (the ghost's owner). Returns the new ghost entity. The caller adds it to the connection's LinkedEntityGroup.
        /// </summary>
        public static Entity SpawnServer(EntityManager em, Entity prefab, int networkId, int3 positionMm, int yawCdeg = 0)
        {
            var ghost = em.Instantiate(prefab);
            em.SetComponentData(ghost, new GhostOwner { NetworkId = networkId });
            Place(em, ghost, positionMm, yawCdeg);
            return ghost;
        }

        /// <summary>Sets the pose and the transform of a ghost that has not moved yet (spawn). For a placement of a live player use <see cref="Teleport"/>.</summary>
        public static void Place(EntityManager em, Entity ghost, int3 positionMm, int yawCdeg = 0)
        {
            em.SetComponentData(ghost, new PlayerPose { XMm = positionMm.x, YMm = positionMm.y, ZMm = positionMm.z, YawCdeg = yawCdeg });
            var t = em.GetComponentData<Unity.Transforms.LocalTransform>(ghost);
            t.Position = new float3(positionMm.x, positionMm.y, positionMm.z) * 0.001f;
            t.Rotation = quaternion.RotateY(math.radians(yawCdeg * 0.01f));
            em.SetComponentData(ghost, t);
        }

        /// <summary>
        /// Server: walk the player to a point (skill walk-in, follow, housing, bench, guide). Planned by the server's planner next
        /// tick; wins over a click. <paramref name="runPaid"/> only after the server has taken the Chi for running.
        /// </summary>
        public static void IssueOrder(EntityManager serverEm, Entity ghost, int3 goalMm, bool runPaid = false)
        {
            serverEm.SetComponentData(ghost, new ServerOrderRequest
            {
                GoalXMm = goalMm.x, GoalYMm = goalMm.y, GoalZMm = goalMm.z,
                Flags = (byte)(runPaid ? OrderFlags.RunPaid : 0),
            });
            serverEm.SetComponentEnabled<ServerOrderRequest>(ghost, true);
        }

        /// <summary>Server: place the player (travel arrival, respawn, GM). Drops any walk and bumps <see cref="TeleportSeq"/>.</summary>
        public static void Teleport(EntityManager serverEm, Entity ghost, int3 positionMm, int yawCdeg = 0)
        {
            serverEm.SetComponentData(ghost, new TeleportRequest { XMm = positionMm.x, YMm = positionMm.y, ZMm = positionMm.z, YawCdeg = yawCdeg });
            serverEm.SetComponentEnabled<TeleportRequest>(ghost, true);
        }
    }
}
