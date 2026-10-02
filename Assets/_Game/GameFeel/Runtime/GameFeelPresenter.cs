using System.Collections;
using System.Collections.Generic;
using Animancer;
using KryticalNova.Core;
using KryticalNova.Networking;
using KryticalNova.UI;
using MoreMountains.Feedbacks;
using Unity.Netcode.Components;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// Client-side game feel. Gameplay reports moments through <see cref="IGameFeelSink"/>; this turns them into FEEL
    /// (MMF_Player) feedbacks — camera impulses, FOV kicks, URP screen pulses, squash/flash on whatever got hit, sound
    /// slots — plus URP-converted POLYGON Particle FX spawned through <see cref="NetworkCombatVFXPool"/>.
    ///
    /// Camera-level feedback only plays for the local player's own actions (or when the local player is hit); world
    /// effects (sparks, dust, the struck target's reaction) play on every client. Hit-stop slows only the attacker's and
    /// target's Animancer graphs: Time.timeScale is never touched, so netcode ticks and other players are unaffected.
    /// Built by GameFeelBuilder (Krytical Nova ▸ GameFeel ▸ Build Game Feel); tune the MMF_Players on the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameFeelPresenter : MonoBehaviour, IGameFeelSink
    {
        [Header("FEEL players (camera / screen / sound)")]
        [SerializeField] private MMF_Player swingFeel;
        [SerializeField] private MMF_Player hitFeel;
        [SerializeField] private MMF_Player heavyHitFeel;
        [SerializeField] private MMF_Player damagedFeel;
        [SerializeField] private MMF_Player clickFeel;
        [SerializeField] private MMF_Player selectFeel;
        [SerializeField] private MMF_Player runFeel;
        [SerializeField] private MMF_Player jumpFeel;
        [SerializeField] private MMF_Player landFeel;
        [Tooltip("Squash + white flash, cloned under each struck target (its scale / flicker targets are set per target).")]
        [SerializeField] private MMF_Player targetReactionTemplate;

        [Header("World VFX (URP copies of POLYGON Particle FX)")]
        [SerializeField] private GameObject hitSparkVfx;
        [SerializeField] private GameObject heavyHitVfx;
        [SerializeField] private GameObject clickVfx;
        [SerializeField] private GameObject selectHostileVfx;
        [SerializeField] private GameObject selectFriendlyVfx;
        [SerializeField] private GameObject runDustVfx;
        [SerializeField] private GameObject jumpDustVfx;
        [SerializeField] private GameObject landDustVfx;

        [Header("Hit-stop (Animancer graphs only)")]
        [Range(0f, 0.2f)] [SerializeField] private float hitStopSeconds = 0.06f;
        [Range(0f, 0.2f)] [SerializeField] private float heavyHitStopSeconds = 0.11f;
        [Range(0f, 1f)] [SerializeField] private float hitStopSpeed = 0.05f;

        private readonly Dictionary<Transform, MMF_Player> reactions = new Dictionary<Transform, MMF_Player>();
        private readonly Dictionary<AnimancerComponent, int> frozen = new Dictionary<AnimancerComponent, int>();
        private MMF_Player[] cameraPlayers;

        private void OnEnable()
        {
            ClientPresentationRegistry.RegisterFeel(this);
            cameraPlayers = new[] { swingFeel, hitFeel, heavyHitFeel, damagedFeel, clickFeel, selectFeel, runFeel, jumpFeel, landFeel };
            HudPreferences.Changed += ApplyScreenShakePreference;
            ApplyScreenShakePreference();
        }

        private void OnDisable()
        {
            ClientPresentationRegistry.UnregisterFeel(this);
            HudPreferences.Changed -= ApplyScreenShakePreference;
            foreach (var kv in frozen)
                if (kv.Key != null) kv.Key.Graph.Speed = 1f;
            frozen.Clear();
        }

        // ------------------------------------------------------------------ IGameFeelSink

        public void Swing(Transform attacker, int comboStep, bool isLocalPlayer)
        {
            if (!isLocalPlayer || attacker == null) return;
            // Every swing whooshes; the finisher also leans the camera in.
            Play(swingFeel, attacker.position, comboStep == 3 ? 1f : 0.45f);
        }

        public void Hit(in HitFeel hit)
        {
            bool heavy = hit.IsCritical || hit.ComboStep >= 3;
            Spawn(heavy && heavyHitVfx != null ? heavyHitVfx : hitSparkVfx, hit.Point, Quaternion.LookRotation(hit.TowardAttacker, Vector3.up));

            if (hit.Target != null) React(hit.Target, heavy);

            if (hit.AttackerIsLocalPlayer)
            {
                Play(heavy ? heavyHitFeel : hitFeel, hit.Point, hit.IsCritical ? 1f : 0.75f);
                float stop = heavy ? heavyHitStopSeconds : hitStopSeconds;
                HitStop(hit.Attacker, stop);
                HitStop(hit.Target, stop);
            }
        }

        public void LocalPlayerDamaged(float fraction01)
        {
            Play(damagedFeel, Vector3.zero, Mathf.Clamp(fraction01 * 6f, 0.4f, 1f));
        }

        public void MoveCommand(Vector3 point, Vector3 normal)
        {
            if (normal.sqrMagnitude < 0.01f) normal = Vector3.up;
            Spawn(clickVfx, point + normal * 0.05f, Quaternion.FromToRotation(Vector3.up, normal));
            Play(clickFeel, point, 1f);
        }

        public void TargetSelected(Transform target, WorldTargetKind kind)
        {
            if (target == null) return;
            var vfx = kind == WorldTargetKind.Hostile ? selectHostileVfx : selectFriendlyVfx;
            Spawn(vfx, FootPoint(target), Quaternion.identity);
            Play(selectFeel, target.position, kind == WorldTargetKind.Hostile ? 1f : 0.6f);
            React(target, false, flash: kind == WorldTargetKind.Hostile);
        }

        public void RunStarted(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            Spawn(runDustVfx, FootPoint(actor), Quaternion.LookRotation(-FlatForward(actor), Vector3.up));
            if (isLocalPlayer) Play(runFeel, actor.position, 1f);
        }

        public void JumpTakeoff(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            Spawn(jumpDustVfx, FootPoint(actor), Quaternion.identity);
            if (isLocalPlayer) Play(jumpFeel, actor.position, 1f);
        }

        public void Landed(Transform actor, bool isLocalPlayer)
        {
            if (actor == null) return;
            Spawn(landDustVfx, FootPoint(actor), Quaternion.identity);
            if (isLocalPlayer) Play(landFeel, actor.position, 1f);
        }

        // ------------------------------------------------------------------ helpers

        private static void Play(MMF_Player player, Vector3 position, float intensity)
        {
            if (player != null) player.PlayFeedbacks(position, intensity);
        }

        private static void Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var pool = NetworkCombatVFXPool.Instance;
            if (pool != null && prefab != null) pool.Spawn(prefab, position, rotation);
        }

        /// <summary>Squash (and optionally white-flash) the struck target with a per-target clone of the reaction player.</summary>
        private void React(Transform target, bool heavy, bool flash = true)
        {
            if (targetReactionTemplate == null) return;
            if (!reactions.TryGetValue(target, out var player) || player == null)
            {
                var squashRoot = VisualRoot(target);
                var renderer = squashRoot.GetComponentInChildren<Renderer>();
                if (renderer == null) return;
                player = Instantiate(targetReactionTemplate, target, false);
                player.name = "GameFeel Reaction";
                foreach (var f in player.FeedbacksList)
                {
                    if (f is MMF_Scale scale) scale.AnimateScaleTarget = squashRoot;
                    else if (f is MMF_Flicker flicker) flicker.BoundRenderer = renderer;
                }
                player.Initialization();
                reactions[target] = player;
            }
            foreach (var f in player.FeedbacksList)
                if (f is MMF_Flicker) f.Active = flash;
            player.PlayFeedbacks(target.position, heavy ? 1f : 0.6f);
        }

        /// <summary>
        /// The transform to squash: the root unless a NetworkTransform syncs it (then scale would be fought over by
        /// replication), in which case the first child holding the visuals.
        /// </summary>
        private static Transform VisualRoot(Transform target)
        {
            if (target.GetComponent<NetworkTransform>() == null) return target;
            foreach (Transform child in target)
                if (child.GetComponentInChildren<SkinnedMeshRenderer>() != null || child.GetComponentInChildren<MeshRenderer>() != null)
                    return child;
            return target;
        }

        private void HitStop(Transform actor, float seconds)
        {
            if (actor == null || seconds <= 0f) return;
            var animancer = actor.GetComponentInChildren<AnimancerComponent>();
            if (animancer == null || !animancer.IsGraphInitialized) return;
            StartCoroutine(HitStopRoutine(animancer, seconds));
        }

        private IEnumerator HitStopRoutine(AnimancerComponent animancer, float seconds)
        {
            frozen.TryGetValue(animancer, out int count);
            frozen[animancer] = count + 1;
            animancer.Graph.Speed = hitStopSpeed;
            yield return new WaitForSecondsRealtime(seconds);
            if (animancer == null) yield break;
            frozen[animancer] -= 1;
            if (frozen[animancer] <= 0)
            {
                frozen.Remove(animancer);
                animancer.Graph.Speed = 1f;
            }
        }

        private static Vector3 FootPoint(Transform t) => t.position + Vector3.up * 0.05f;

        private static Vector3 FlatForward(Transform t)
        {
            var f = t.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
        }

        /// <summary>Screen-shake toggle: mutes every camera / screen feedback but keeps sounds and world VFX.</summary>
        private void ApplyScreenShakePreference()
        {
            bool on = HudPreferences.ScreenShake;
            if (cameraPlayers == null) return;
            foreach (var player in cameraPlayers)
            {
                if (player == null) continue;
                foreach (var f in player.FeedbacksList)
                    if (IsScreenFeedback(f)) f.Active = on;
            }
        }

        private static bool IsScreenFeedback(MMF_Feedback f)
        {
            string type = f.GetType().Name;
            return type.Contains("Cinemachine") || type.Contains("FieldOfView") || type.EndsWith("_URP") || type.Contains("CameraShake");
        }
    }
}
