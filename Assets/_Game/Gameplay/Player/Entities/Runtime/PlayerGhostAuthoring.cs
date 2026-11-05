using Unity.Entities;
using Unity.Mathematics;
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
        [Tooltip("The NGO Player prefab's PlayerMeleeCombat combo clips, the same assets (copied by Build Player Ghost Assets).")]
        public AnimationClip combo1Clip, combo2Clip, combo3Clip;

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
                AddComponent<PlayerMountTag>(e);
                AddComponent<PlayerVitals>(e);
                AddComponent<PositionHistory>(e);   // server only, never sent: the lag compensation ring (M4.3)
                AddComponent<CombatState>(e);       // the basic attack under way, predicted (M4.4c)
                AddBuffer<HitEvent>(e);             // the attacker's landed hits, for every client's presentation (M4.4h)
                AddComponent<HitEventSeq>(e);
                AddBuffer<CastEvent>(e);            // the player's casts, for the other clients (M4.5c)
                AddComponent<CastEventSeq>(e);
                AddComponent<ChargeState>(e);       // a hold-to-charge, for the other clients (M4.5f)
                AddBuffer<SquadBuffEvent>(e);       // Squad members' Stances given to this player, for every client (M4.5g)
                AddComponent<SquadBuffSeq>(e);
                AddBuffer<RunningBuff>(e);          // the server's running buffs, every view's copy kept equal (M4.5i)
                AddComponent<DeathState>(e);        // how the player went down, for every view (M4 6b)
                AddComponent<KnockPlan>(e);         // the dead body's travel to its landing (M4 6b-2)
                // M4.4b: the strike lengths come from the clips themselves, as NGO's GetStrikeDuration reads clip.length
                DependsOn(a.combo1Clip);
                DependsOn(a.combo2Clip);
                DependsOn(a.combo3Clip);
                if (a.combo1Clip != null && a.combo2Clip != null && a.combo3Clip != null)
                    AddComponent(e, new MeleeTimingData { Value = MeleeTiming.Prefab(new int3(Micros(a.combo1Clip), Micros(a.combo2Clip), Micros(a.combo3Clip))) });
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

            /// <summary>A clip's length in whole microseconds.</summary>
            static int Micros(AnimationClip clip) => (int)math.round(clip.length * 1000000.0);
        }
    }
}
