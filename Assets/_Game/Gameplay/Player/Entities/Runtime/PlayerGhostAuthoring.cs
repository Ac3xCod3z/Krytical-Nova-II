using Unity.Entities;
using UnityEngine;

namespace KryticalNova.Player.Entities
{
    /// <summary>
    /// Put on the player ghost prefab next to <c>GhostAuthoringComponent</c> (Default Ghost Mode: Owner Predicted, Has Owner on,
    /// Dynamic optimisation). Bakes every component of the player's walk. Lives in this non-Editor assembly because an
    /// authoring component in an Editor-only assembly cannot be added to a prefab in tests (spike M1). The ghost prefab only
    /// reaches a world through a SubScene (see <see cref="PlayerGhostPrefabAuthoring"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGhostAuthoring : MonoBehaviour
    {
        [Tooltip("Walk speed in mm/s (the server rewrites it from rank, mount and gear).")]
        public int walkMmps = 5000;
        [Tooltip("Run speed in mm/s.")]
        public int runMmps = 9000;
        [Tooltip("Length of the per-tick history ring kept for tests and the divergence log; 0 = none.")]
        public int traceTicks;

        public int TraceTicks => traceTicks;

        sealed class Baker : Baker<PlayerGhostAuthoring>
        {
            public override void Bake(PlayerGhostAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.Dynamic);
                AddComponent<PlayerGhostTag>(e);
                AddComponent<PlayerInput>(e);
                AddComponent<ServerOrder>(e);
                AddComponent<MoveState>(e);
                AddComponent<PlayerPose>(e);
                AddComponent<TeleportSeq>(e);
                AddComponent<PlayerLook>(e);
                AddComponent<PlayerGmTag>(e);
                AddComponent<PlayerLevelTag>(e);
                AddComponent<PlayerNovaTag>(e);
                AddComponent<PlayerPartyTag>(e);
                AddComponent<PlayerCrewTag>(e);
                AddComponent<PlayerEmoteTag>(e);
                AddComponent<PlayerPetTag>(e);
                AddComponent(e, new MoveStats { WalkMmps = a.walkMmps, RunMmps = a.runMmps });
                AddComponent<MoveRanks>(e);
                AddComponent<VaultPlan>(e);
                AddComponent<ClientVaultPlan>(e);
                AddComponent<WaterPlan>(e);
                AddComponent<ClientWaterPlan>(e);
                AddComponent<WaterFluxOwed>(e);
                AddComponent<WallPlan>(e);
                AddComponent<ClientWallPlan>(e);
                AddBuffer<WallCorner>(e);
                AddBuffer<ClientWallCorner>(e);
                AddComponent<PoseTilt>(e);
                AddComponent<WallGripOwed>(e);
                AddComponent<WallGoal>(e);
                AddComponent<OwnerFlux>(e);
                AddComponent<OwnerProgress>(e);
                AddComponent<OwnerSerums>(e);
                AddBuffer<PathCorner>(e);
                AddComponent<PathPlan>(e);
                AddBuffer<ClientPlanCorner>(e);
                AddComponent<ClientPlanHeader>(e);
                AddComponent<MotionPlan>(e);
                AddComponent<ClientMotionPlan>(e);
                AddComponent<PlayerMoveDiag>(e);
                AddComponent<ServerOrderRequest>(e);
                SetComponentEnabled<ServerOrderRequest>(e, false);
                AddComponent<TeleportRequest>(e);
                SetComponentEnabled<TeleportRequest>(e, false);
                if (a.traceTicks > 0)
                {
                    var trace = AddBuffer<PlayerMoveTrace>(e);
                    for (int i = 0; i < a.traceTicks; i++) trace.Add(default);
                }
            }
        }
    }
}
