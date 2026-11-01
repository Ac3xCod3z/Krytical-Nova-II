using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Progression;
using KryticalNova.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.Player
{
    /// <summary>
    /// Waterstride (owner, 22 Oct 2026): from level 60 with Run and Jump at rank 20, a click on water with Run selected
    /// runs out across it. The player runs to the shore nearest the way, then over the water by the shortest way the
    /// zone's <see cref="WaterMap"/> allows, and stands on the water at the end; a click on land from the water runs to
    /// the shore nearest it and walks on. Every second on the water costs Flux; out of Flux, the player is carried back
    /// to the nearest shore. Built like Wallstride: decided and moved where movement is authoritative, the agent off the
    /// NavMesh while on the water and given back on the shore; NetworkTransform carries the body to the other machines,
    /// whose own run and idle play from its travel, and a flag (a NetworkVariable, so a late joiner sees it) tells them
    /// to show the wake.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent), typeof(PlayerMovementSkills), typeof(PlayerResources))]
    public sealed class PlayerWaterstrideMotor : NetworkBehaviour
    {
        private const float ShoreTrigger = WaterstrideRules.ShoreTrigger;
        private const float ArriveDistance = 0.08f;
        private const float TurnSpeed = 720f;      // degrees a second the body turns to the way it runs
        private const float WalkWatchSeconds = 0.25f;
        private const float StrandedReach = WaterstrideRules.StrandedReach;

        private enum State : byte { None, ToShore, Running, Standing, Stranded }

        [SerializeField] private WaterstrideSettings settings;

        private readonly NetworkVariable<bool> netOnWater = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private NavMeshAgent agent;
        private PlayerMovementSkills movementSkills;
        private PlayerResources resources;
        private PlayerCharacterProgression progression;
        private PointAndClickPlayer pointer;
        private PlayerJumpMotor jumpMotor;
        private PlayerVaultMotor vaultMotor;
        private PlayerWallRunMotor wallRunMotor;

        // Authority.
        private State state;
        private readonly List<Vector3> route = new List<Vector3>(32);
        private int routeIndex;
        private Vector3 shoreFeet;
        private float walkStartedAt;
        private bool landAtEnd;
        private Vector3 landFeet, walkOnTo;
        private float fluxOwed;
        private bool shownOnWater;

        public WaterstrideSettings Settings => settings;

        /// <summary>Waterstride is open for this character: level 60, Run and Jump at rank 20 (the settings' numbers).</summary>
        public bool WaterstrideOpen => movementSkills != null && progression != null
            && WaterstrideRules.IsOpen(settings, progression.CurrentLevel, movementSkills.RunRank, movementSkills.JumpRank);

        /// <summary>On the water now (every machine).</summary>
        public bool IsOnWater => HasAuthority ? state == State.Running || state == State.Standing || state == State.Stranded : netOnWater.Value;

        /// <summary>On the water, or on the way to it (where movement is decided).</summary>
        public bool IsBusy => HasAuthority ? state != State.None : netOnWater.Value;

        /// <summary>Where the last click it took was: on the water's surface, or the land clicked (for the move marker).</summary>
        public Vector3 LastClickPoint { get; private set; }

        /// <summary>A line for the player: why a run onto the water did not start, or what happened on it.</summary>
        public event Action<string> Message;

        private bool HasAuthority => !IsSpawned || IsServer;
        private float Offset => agent != null ? agent.baseOffset : 0f;
        private Vector3 Feet => transform.position - Vector3.up * Offset;

        private static WaterMap ZoneMap
        {
            get
            {
                ZoneRules rules = ZoneRules.Current;
                return rules != null && rules.Entry != null ? rules.Entry.waterMap : null;
            }
        }

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            movementSkills = GetComponent<PlayerMovementSkills>();
            resources = GetComponent<PlayerResources>();
            progression = GetComponent<PlayerCharacterProgression>();
            pointer = GetComponent<PointAndClickPlayer>();
            jumpMotor = GetComponent<PlayerJumpMotor>();
            vaultMotor = GetComponent<PlayerVaultMotor>();
            wallRunMotor = GetComponent<PlayerWallRunMotor>();
        }

        public override void OnNetworkSpawn()
        {
            netOnWater.OnValueChanged += OnWaterChanged;
            Show(netOnWater.Value);
        }

        public override void OnNetworkDespawn()
        {
            netOnWater.OnValueChanged -= OnWaterChanged;
            Show(false);
        }

        private void OnWaterChanged(bool before, bool now) => Show(now);

        /// <summary>The wake at the feet, on every machine.</summary>
        private void Show(bool onWater)
        {
            if (onWater == shownOnWater) return;
            shownOnWater = onWater;
            ClientPresentationRegistry.Feel?.WaterStriding(transform, onWater);
        }

        // ------------------------------------------------------------------ a click (the player's own game asks)

        /// <summary>
        /// A click whose ray is <paramref name="ray"/> and whose first hit is <paramref name="hit"/>. Taken when the ray
        /// meets the zone's runnable water before anything else and Waterstride can run there (Run selected, or already on
        /// the water), or when the player is on the water and clicked land. True when it was dealt with (started, or
        /// refused with the reason told).
        /// </summary>
        public bool TryClick(Ray ray, RaycastHit hit)
        {
            WaterMap map = ZoneMap;
            if (map == null || settings == null) return false;
            // With Jump selected the click is a jump's landing, onto the water or off it (PlayerJumpMotor).
            if (movementSkills != null && movementSkills.IsJumpSelected) return false;
            bool onWater = IsOnWater;
            if (!onWater && !(movementSkills != null && movementSkills.IsRunning && WaterstrideOpen)) return false;
            if (WaterstrideRules.WaterUnderRay(map, ray, hit.distance, out Vector3 onSurface))
            {
                LastClickPoint = onSurface;
                if (HasAuthority) { if (!ServerWaterClick(onSurface, out string why)) Message?.Invoke(why); }
                else RequestWaterServerRpc(onSurface);
                return true;
            }
            if (!onWater) return false;
            LastClickPoint = hit.point;
            if (HasAuthority) { if (!ServerLandClick(hit.point, out string why)) Message?.Invoke(why); }
            else RequestLandServerRpc(hit.point);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestWaterServerRpc(Vector3 point)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(RequestWaterServerRpc))) return;
            if (!RpcBudget.PointFits(this, nameof(RequestWaterServerRpc), point)) return;
            if (!ServerWaterClick(point, out string why)) RefusedRpc(why);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestLandServerRpc(Vector3 point)
        {
            if (!RpcBudget.Allow(this, CallKind.Move, nameof(RequestLandServerRpc))) return;
            if (!RpcBudget.PointFits(this, nameof(RequestLandServerRpc), point)) return;
            if (!ServerLandClick(point, out string why)) RefusedRpc(why);
        }

        [Rpc(SendTo.Owner)]
        private void RefusedRpc(string reason) { if (!string.IsNullOrEmpty(reason)) Message?.Invoke(reason); }

        [Rpc(SendTo.Owner)]
        private void ToldRpc(string line) { if (!string.IsNullOrEmpty(line)) Message?.Invoke(line); }

        // ------------------------------------------------------------------ where movement is decided

        private bool ServerWaterClick(Vector3 point, out string reason)
        {
            reason = WaterstrideRules.PlanWaterClick(ZoneMap, settings, AskNow(), Shore(), Feet, point, route, out WaterstrideRules.Way way);
            if (reason != null) return false;
            reason = string.Empty;
            if (!way.FromLand || way.StartsNow) { RunRoute(landAfter: false); return true; }
            var path = new NavMeshPath();
            if (!agent.CalculatePath(way.ShoreFeet, path) || path.status != NavMeshPathStatus.PathComplete) { route.Clear(); reason = "There is no way to that shore."; return false; }
            shoreFeet = way.ShoreFeet;
            agent.SetPath(path);
            agent.isStopped = false;
            state = State.ToShore;
            walkStartedAt = Time.time;
            return true;
        }

        private bool ServerLandClick(Vector3 point, out string reason)
        {
            reason = WaterstrideRules.PlanLandClick(ZoneMap, settings, AskNow(), Shore(), Feet, point, route, out WaterstrideRules.Way way);
            if (reason != null) return false;
            reason = string.Empty;
            landFeet = way.LandFeet;
            walkOnTo = way.WalkOnTo;
            RunRoute(landAfter: true);
            return true;
        }

        /// <summary>The body's answers for the rules' refusals.</summary>
        private WaterstrideRules.Ask AskNow() => new WaterstrideRules.Ask
        {
            Open = WaterstrideOpen,
            Alive = resources != null && resources.IsAlive,
            OtherMoveBusy = (jumpMotor != null && jumpMotor.IsBusy) || (vaultMotor != null && vaultMotor.IsBusy) || (wallRunMotor != null && wallRunMotor.IsBusy),
            Running = movementSkills != null && movementSkills.IsRunning,
            HasFluxForASecond = resources != null && resources.CanSpendChi(WaterstrideRules.FluxPerSecond(settings)),
            OnWater = IsOnWater,
            Stranded = state == State.Stranded,
            OnWalkableGround = agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh,
        };

        private WaterstrideRules.IShore Shore() => new WaterstrideRules.NavMeshShore(Filter());

        private void RunRoute(bool landAfter)
        {
            if (route.Count == 0) return;
            if (!IsOnWater)
            {
                if (agent.isActiveAndEnabled && agent.isOnNavMesh)
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                    agent.velocity = Vector3.zero;
                }
                agent.enabled = false; // the water is not NavMesh: this moves the body until the shore
                fluxOwed = 0f;
            }
            landAtEnd = landAfter;
            routeIndex = 0;
            state = State.Running;
            SetOnWater(true);
        }

        private void Update()
        {
            if (!HasAuthority || state == State.None) return;
            if (resources != null && !resources.IsAlive) return; // the death motor takes the body (ReleaseForDeath)
            if (state == State.ToShore) { WatchToShore(); return; }

            if (state != State.Stranded && !PayForTime()) { Strand(); return; }
            if (state == State.Standing) return;

            WaterMap map = ZoneMap;
            float speed = WaterstrideRules.Speed(settings, movementSkills != null ? movementSkills.CurrentSpeed : 6f, state == State.Stranded);
            float step = speed * Time.deltaTime;
            Vector3 feet = Feet;
            while (step > 0f && routeIndex < route.Count)
            {
                Vector3 goal = route[routeIndex];
                goal.y = map != null ? map.waterLevel : goal.y;
                Vector3 to = goal - feet;
                to.y = 0f;
                float left = to.magnitude;
                if (left <= step || left < ArriveDistance)
                {
                    feet = new Vector3(goal.x, goal.y, goal.z);
                    step -= left;
                    routeIndex++;
                    continue;
                }
                feet += to / left * step;
                feet.y = goal.y;
                Face(to);
                step = 0f;
            }
            transform.position = feet + Vector3.up * Offset;
            if (routeIndex < route.Count) return;

            if (landAtEnd || state == State.Stranded) { Land(landFeet, walkOnTo, landAtEnd && state != State.Stranded); return; }
            state = State.Standing;
        }

        /// <summary>Walking to the shore: the run onto the water starts there; another order called it off.</summary>
        private void WatchToShore()
        {
            if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) { state = State.None; return; }
            if (Time.time - walkStartedAt > WalkWatchSeconds && Flat(agent.destination, shoreFeet) > 0.6f && Flat(Feet, shoreFeet) > ShoreTrigger)
            { state = State.None; return; } // another order took the agent
            if (Flat(Feet, shoreFeet) > ShoreTrigger + agent.velocity.magnitude * Time.deltaTime * 2f) return;
            RunRoute(landAfter: false);
        }

        /// <summary>Flux for the time on the water, a whole point at a time. False when it has run out.</summary>
        private bool PayForTime()
        {
            if (settings.fluxPerSecond <= 0f || resources == null) return true;
            fluxOwed += settings.fluxPerSecond * Time.deltaTime;
            if (fluxOwed < 1f) return true;
            float whole = Mathf.Floor(fluxOwed);
            if (!resources.TrySpendChi(whole)) return false;
            fluxOwed -= whole;
            return true;
        }

        /// <summary>Out of Flux: carried back over the water to the nearest shore, for nothing more.</summary>
        private void Strand()
        {
            if (!WaterstrideRules.PlanStranded(ZoneMap, settings, Shore(), Feet, route, out Vector3 shore)) { Cancel(); return; }
            landFeet = shore;
            walkOnTo = shore;
            state = State.Stranded;
            routeIndex = 0;
            if (IsSpawned) ToldRpc("Out of Flux: back to the shore.");
            else Message?.Invoke("Out of Flux: back to the shore.");
        }

        private void Land(Vector3 feet, Vector3 then, bool walkOn)
        {
            transform.position = feet + Vector3.up * Offset;
            agent.enabled = true;
            if (!agent.isOnNavMesh && NavMesh.SamplePosition(feet, out NavMeshHit hit, 2f, Filter()))
            {
                agent.enabled = false;
                transform.position = hit.position + Vector3.up * Offset;
                agent.enabled = true;
            }
            if (agent.isOnNavMesh)
            {
                agent.ResetPath();
                agent.isStopped = false;
            }
            else agent.enabled = false; // PointAndClickPlayer puts a lost agent back after a moment
            state = State.None;
            route.Clear();
            SetOnWater(false);
            if (walkOn && pointer != null && Flat(feet, then) > 0.5f) pointer.ServerMoveTo(then, payRun: false);
        }

        /// <summary>Off the water at once, onto the nearest shore (a GM teleport, a map change).</summary>
        public void Cancel()
        {
            if (!HasAuthority || state == State.None) return;
            if (state == State.ToShore) { state = State.None; return; }
            if (NavMesh.SamplePosition(Feet, out NavMeshHit shore, StrandedReach, Filter())) Land(shore.position, shore.position, false);
            else { state = State.None; route.Clear(); SetOnWater(false); }
        }

        /// <summary>Killed on the water: the run ends where it is; the death motor takes the body from there.</summary>
        public void ReleaseForDeath()
        {
            if (!HasAuthority || state == State.None) return;
            state = State.None;
            route.Clear();
            SetOnWater(false);
        }

        // ------------------------------------------------------------------ jumping off and onto the water (owner, 24 Oct 2026)

        /// <summary>
        /// Where a jump clicked at <paramref name="clicked"/> would come down on this zone's water: the point on its surface.
        /// False where the place is not water (the click met the ground or the bed of shallow water).
        /// </summary>
        public bool TryWaterLanding(Vector3 clicked, out Vector3 surface) => TryWaterLandingAt(clicked, out surface);

        /// <summary><see cref="TryWaterLanding"/> without a body: the zone's water map only (the entity-netcode jump planner).</summary>
        public static bool TryWaterLandingAt(Vector3 clicked, out Vector3 surface)
        {
            return WaterstrideRules.WaterLanding(ZoneMap, clicked, out surface);
        }

        /// <summary>Where movement is decided: a jump leaves the water from here; the stand ends and the jump takes the body.</summary>
        public void ServerLeaveForJump()
        {
            if (!HasAuthority || !IsOnWater) return;
            state = State.None;
            route.Clear();
            SetOnWater(false);
        }

        /// <summary>
        /// Where movement is decided: a jump came down on the water at <paramref name="feet"/>. With Waterstride open and
        /// Flux for it the player stands there as on ground (and runs on from it like any Waterstride); otherwise they
        /// fall in and come up on the nearest shore, losing a share of their HP.
        /// </summary>
        public void ServerLandedOnWater(Vector3 feet, bool splash = true)
        {
            if (!HasAuthority) return;
            WaterMap map = ZoneMap;
            if (map != null) feet.y = map.waterLevel;
            if (splash) ServerSplash(feet);
            bool stand = resources != null && WaterstrideRules.StandsOnLanding(settings, WaterstrideOpen, resources.IsAlive,
                resources.CanSpendChi(WaterstrideRules.FluxPerSecond(settings)));
            if (!stand) { FallIn(feet); return; }
            if (agent != null) agent.enabled = false;
            transform.position = feet + Vector3.up * Offset;
            route.Clear();
            fluxOwed = 0f;
            state = State.Standing;
            SetOnWater(true);
        }

        /// <summary>In the water: out on the nearest shore, a share of Max HP lost (never the last of it).</summary>
        private void FallIn(Vector3 feet)
        {
            float lost = resources != null ? WaterstrideRules.FallInLoss(settings, resources.MaxHealth, resources.Health, resources.IsAlive) : 0f;
            if (lost > 0f) resources.ApplyDamage(lost);
            float reach = settings != null ? settings.fallInShoreReach : StrandedReach;
            if (NavMesh.SamplePosition(feet, out NavMeshHit shore, reach, Filter())) Land(shore.position, shore.position, false);
            else { state = State.None; route.Clear(); SetOnWater(false); } // no shore near: PointAndClickPlayer puts a lost agent back
            string line = WaterstrideRules.FallInLine(lost);
            if (IsSpawned) ToldRpc(line);
            else Message?.Invoke(line);
        }

        /// <summary>A body struck the water hard here (a jump off it or onto it, a fall in): the big splash on every machine.</summary>
        public void ServerSplash(Vector3 at)
        {
            if (!HasAuthority) return;
            ClientPresentationRegistry.Feel?.WaterSplash(transform, at, BodyRoleTag.IsMine(this, !IsSpawned || IsOwner));
            if (IsSpawned) SplashRpc(at);
        }

        [Rpc(SendTo.NotServer)]
        private void SplashRpc(Vector3 at) => ClientPresentationRegistry.Feel?.WaterSplash(transform, at, IsOwner);

        private void SetOnWater(bool on)
        {
            if (IsSpawned) netOnWater.Value = on;
            Show(on);
        }

        private void Face(Vector3 travel)
        {
            if (travel.sqrMagnitude < 1e-6f) return;
            // The model's own forward may be turned from the root's (PointAndClickPlayer's model offset).
            Quaternion want = Quaternion.LookRotation(travel, Vector3.up) * Quaternion.AngleAxis(pointer != null ? pointer.ModelForwardOffset : 0f, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * Time.deltaTime);
        }

        private NavMeshQueryFilter Filter() => new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0, areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas };

        private static float Flat(Vector3 a, Vector3 b)
        {
            a.y = b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
