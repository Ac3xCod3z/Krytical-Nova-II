using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Kinemation.MotionWarping.Runtime.Core;
using Kinemation.MotionWarping.Runtime.Utility;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WarpComponent = Kinemation.MotionWarping.Runtime.Core.MotionWarping;

namespace KryticalNova.Persistence.Entities.Tests.Play
{
    /// <summary>
    /// Spike S7 (M3b design 2.3, owner answer Q3 (a): the ghost owns the root, Motion Warping does not move it). Measures whether the
    /// jump's root can be computed without the warp component, and how far a ghost that knows only the pose on each 30 Hz tick
    /// (linear between ticks, as the presentation follow would show it) is from what the warp does every frame.
    /// The real KINEMATION component plays the game's jump asset on a bare object (no Animator: the game's warp child has none either,
    /// so the play rate stays 1); <see cref="Replay"/> computes the same root from the asset's curves as a function of playback time.
    /// Result (runs 5-9, 31 Oct): the warp's own path changes with the frame rate (in flight 16 / 8 / 4 cm off the exact arc at 30 / 60 /
    /// 120 frames a second; it lands about 4 cm off its target), and before take-off it lowers the root by the clip's crouch (20 cm).
    /// Owner's answer: the DOTS jump flies the exact, frame-rate independent arc (no player gets an advantage from frame rate) and is
    /// not judged against the warp; the crouch stays as a look of the drawn body. So this asserts only that the drawn crouch follows
    /// the clip within 2 cm; the rest is logged. Plan section 53.
    /// Explicit: run on purpose (Play mode, about 2 s).
    /// </summary>
    [Explicit("Spike S7: run on purpose")]
    [Category("S7")]
    public class S7WarpVsGhostSpikeTests
    {
        const string JumpAssetPath = "Assets/_Game/Gameplay/Player/Data/Assets/Warp_Player_DoubleJump.asset";
        const float TickSeconds = 1f / 30f;
        const float PassMetres = 0.02f;

        GameObject m_Body;

        [TearDown]
        public void TearDown()
        {
            if (m_Body != null) Object.Destroy(m_Body);
            Time.captureDeltaTime = 0f;
        }

        // Run 5 (31 Oct): 16 cm worst, exactly at the apex (the warp's phase boundary), at about 30 frames a second in batch. The warp
        // enters and leaves a phase only on the first frame after its time, so its root may depend on the frame rate: the same jump
        // at two fixed frame times tells (a frame-quantised warp errs about 4x less at 1/120 s).
        [UnityTest]
        public IEnumerator At30FramesASecond() => Measure(1f / 30f);

        [UnityTest]
        public IEnumerator At60FramesASecond() => Measure(1f / 60f);

        [UnityTest]
        public IEnumerator At120FramesASecond() => Measure(1f / 120f);

        IEnumerator Measure(float frameSeconds)
        {
            Time.captureDeltaTime = frameSeconds;
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<MotionWarpingAsset>(JumpAssetPath);
#else
            MotionWarpingAsset asset = null;
#endif
            Assert.IsNotNull(asset, JumpAssetPath);
            Assert.AreEqual(2, asset.warpPhases.Count, "take-off to apex, apex to touchdown");

            // the game's shape (PlayerJumpMotor.PlanWarpedJump): 6 m forward, 1 m down, apex 1.8 m over the start
            Vector3 start = new Vector3(2f, 0.5f, -3f);
            Vector3 landing = start + new Vector3(4.2f, -1f, 4.2f);
            float takeoff = asset.warpPhases[0].startTime, apexTime = asset.warpPhases[0].endTime, land = asset.warpPhases[1].endTime;
            Vector3 apex = Vector3.Lerp(start, landing, (apexTime - takeoff) / (land - takeoff));
            apex.y = start.y + 1.8f;
            Vector3 flat = landing - start;
            flat.y = 0f;
            Quaternion facing = Quaternion.LookRotation(flat, Vector3.up);

            m_Body = new GameObject("S7 body");
            m_Body.transform.SetPositionAndRotation(start, facing);
            var child = new GameObject("S7 warp");
            child.transform.SetParent(m_Body.transform, false);
            var warp = child.AddComponent<WarpComponent>();
            // added at runtime its events are null (the game's prefab has them serialized); Play invokes them
            warp.onWarpStarted = new UnityEngine.Events.UnityEvent();
            warp.onWarpEnded = new UnityEngine.Events.UnityEvent();
            typeof(WarpComponent).GetField("transformToWarp", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(warp, m_Body.transform);
            yield return null; // Start()
            var playback = typeof(WarpComponent).GetField("_warpPlayback", BindingFlags.Instance | BindingFlags.NonPublic);
            var updating = typeof(WarpComponent).GetField("_bUpdateWarping", BindingFlags.Instance | BindingFlags.NonPublic);

            var points = new[] { new WarpPoint { position = apex, rotation = facing }, new WarpPoint { position = landing, rotation = facing } };
            warp.Play(asset, points);
            var replay = new Replay(asset, start, facing, new[] { apex, landing });

            // the ghost: the replay sampled on 30 Hz ticks
            var ticks = new List<Vector3>();
            for (float t = 0f; t <= asset.GetLength() + TickSeconds; t += TickSeconds) ticks.Add(replay.At(Mathf.Min(t, asset.GetLength())));

            float worstReplay = 0f, worstTicks = 0f, worstAt = 0f, worstAway = 0f, worstGame = 0f, worstGameAt = 0f, worstFlight = 0f, worstFlightAt = 0f, deepestCrouch = 0f, deepestCrouchAt = 0f, worstCrouch = 0f;
            float rest = asset.rootY.Evaluate(0f);
            // the curve the entity-netcode ghost flies: the NGO motor's own planned, collision-checked arc (JumpPlanRules.WarpedFeet)
            var phases = new KryticalNova.Player.JumpPlanRules.Phases { Takeoff = takeoff, Apex = apexTime, Land = land };
            int frames = 0;
            // read in a LateUpdate after the warp's (batch mode never delivers WaitForEndOfFrame): the warp moved the root, then
            // advanced its clock by this frame's time
            var sampler = m_Body.AddComponent<LateSampler>();
            // on its last frame the warp places the root, then Stop() puts its clock back to 0: that frame's clock is not readable
            sampler.Read = () => ((bool)updating.GetValue(warp)
                ? (float)playback.GetValue(warp) - Time.deltaTime : float.NaN, m_Body.transform.position);
            while ((bool)updating.GetValue(warp) && sampler.Samples.Count < 2000) yield return null;
            sampler.enabled = false;
            // the game ends the jump at touchdown (PlayerJumpMotor.FinishWarpedJump at the clip's land time, then the agent stands
            // on the landing); the clip plays on after it, so the landing is judged at the touchdown time, between two frames
            float landOff = float.NaN;
            float prevP = float.NaN;
            Vector3 prevReal = default;
            foreach (var (sampled, real) in sampler.Samples)
            {
                if (float.IsNaN(sampled)) continue;
                float p = Mathf.Clamp(sampled, 0f, asset.GetLength());
                if (float.IsNaN(landOff) && !float.IsNaN(prevP) && prevP <= land && p >= land)
                    landOff = Vector3.Distance(Vector3.Lerp(prevReal, real, p > prevP ? (land - prevP) / (p - prevP) : 1f), landing);
                prevP = p;
                prevReal = real;
                float dReplay = Vector3.Distance(real, replay.At(p));
                int i = Mathf.Clamp(Mathf.FloorToInt(p / TickSeconds), 0, ticks.Count - 2);
                Vector3 ghost = Vector3.Lerp(ticks[i], ticks[i + 1], (p - i * TickSeconds) / TickSeconds);
                float dTicks = Vector3.Distance(real, ghost);
                if (dReplay > worstReplay) { worstReplay = dReplay; worstAt = p; }
                bool nearBoundary = Mathf.Abs(p - takeoff) < 1.5f * frameSeconds || Mathf.Abs(p - apexTime) < 1.5f * frameSeconds
                                    || Mathf.Abs(p - land) < 1.5f * frameSeconds;
                if (!nearBoundary) worstAway = Mathf.Max(worstAway, dReplay);
                if (p <= land)
                {
                    Vector3 game = KryticalNova.Player.JumpPlanRules.WarpedFeet(start, apex, landing, apex.y, phases, asset.rootY, p);
                    float dGame = Vector3.Distance(real, game);
                    if (dGame > worstGame) { worstGame = dGame; worstGameAt = p; }
                    if (p >= takeoff && dGame > worstFlight) { worstFlight = dGame; worstFlightAt = p; }
                    if (p < takeoff && start.y - real.y > deepestCrouch) { deepestCrouch = start.y - real.y; deepestCrouchAt = p; }
                    // the drawn body in the crouch: the ghost's start lowered by the clip's root (JumpFlight.CrouchMm)
                    if (p < takeoff) worstCrouch = Mathf.Max(worstCrouch, Vector3.Distance(real, game + Vector3.up * (asset.rootY.Evaluate(p) - rest)));
                }
                worstTicks = Mathf.Max(worstTicks, dTicks);
                frames++;
            }
            if (float.IsNaN(landOff)) landOff = float.MaxValue;
            Debug.Log($"[S7] frame time {frameSeconds * 1000f:F1} ms: frames {frames}, worst away from the phase boundaries {worstAway * 100f:F2} cm; the game's arc (ghost) vs warp up to touchdown worst {worstGame * 100f:F2} cm at {worstGameAt:F3} s, in flight (take-off to touchdown) {worstFlight * 100f:F2} cm at {worstFlightAt:F3} s, the warp's root dips {deepestCrouch * 100f:F2} cm in the crouch at {deepestCrouchAt:F3} s (the ghost keeps it on the start), drawn body with the crouch vs warp worst {worstCrouch * 100f:F2} cm; asset length {asset.GetLength():F3} s; replay vs warp worst {worstReplay * 100f:F2} cm at {worstAt:F3} s; " +
                      $"30 Hz ghost vs warp worst {worstTicks * 100f:F2} cm; at touchdown ({land:F3} s) {landOff * 100f:F2} cm from the target; clip end {Vector3.Distance(m_Body.transform.position, landing) * 100f:F2} cm (the clip's after-landing root motion, not used by the game)");
            Assert.Less(worstCrouch, PassMetres, "the drawn body's crouch follows the clip's (a look; owner 31 Oct)");
        }

        /// <summary>Records what <see cref="Read"/> returns in every LateUpdate, after the warp component's (order 10000).</summary>
        [DefaultExecutionOrder(10000)]
        public sealed class LateSampler : MonoBehaviour
        {
            public System.Func<(float, Vector3)> Read;
            public readonly List<(float, Vector3)> Samples = new List<(float, Vector3)>();

            void LateUpdate()
            {
                if (Read != null) Samples.Add(Read());
            }
        }

        /// <summary>
        /// The root the KINEMATION component computes (MotionWarping.WarpTranslation, phases entered and left as playback passes them),
        /// as a function of playback time, for world-space warp points and a root rotation that stays on the facing.
        /// The accumulated-motion axes are integrated at a fine fixed step from the phase start.
        /// </summary>
        public sealed class Replay
        {
            const float Step = 0.0005f;
            readonly MotionWarpingAsset m_Asset;
            readonly Vector3 m_Start;
            readonly Quaternion m_Rot;
            readonly Vector3[] m_Targets;

            public Replay(MotionWarpingAsset asset, Vector3 start, Quaternion rotation, Vector3[] targets)
            {
                m_Asset = asset;
                m_Start = start;
                m_Rot = rotation;
                m_Targets = targets;
            }

            public Vector3 At(float playback)
            {
                Vector3 origin = m_Start;
                Vector3 segmentStartCurve = Vector3.zero; // Play_Internal starts from zero, not curve(0)
                float segmentStart = 0f;
                for (int k = 0; k < m_Asset.warpPhases.Count; k++)
                {
                    WarpPhase phase = m_Asset.warpPhases[k];
                    if (playback <= phase.startTime) return origin + m_Rot * Mask(m_Asset.useAnimation, m_Asset.GetVectorValue(playback) - segmentStartCurve);
                    // free play up to the phase start, then the phase from there
                    origin += m_Rot * Mask(m_Asset.useAnimation, m_Asset.GetVectorValue(phase.startTime) - segmentStartCurve);
                    float end = Mathf.Min(playback, phase.endTime);
                    Vector3 inPhase = PhaseOffset(phase, origin, k, end);
                    if (playback <= phase.endTime) return origin + m_Rot * inPhase;
                    origin += m_Rot * inPhase;
                    segmentStartCurve = m_Asset.GetVectorValue(phase.endTime);
                    segmentStart = phase.endTime;
                }
                return origin + m_Rot * Mask(m_Asset.useAnimation, m_Asset.GetVectorValue(playback) - segmentStartCurve);
            }

            Vector3 PhaseOffset(WarpPhase phase, Vector3 origin, int k, float playback)
            {
                Vector3 startCurve = m_Asset.GetVectorValue(phase.startTime);
                Vector3 endCurve = m_Asset.GetVectorValue(phase.endTime);
                Vector3 localTarget = Quaternion.Inverse(m_Rot) * (m_Targets[k] - origin);
                Vector3 targetDelta = localTarget - Mask(m_Asset.useAnimation, endCurve - startCurve);
                Vector3 rootMotion = m_Asset.GetVectorValue(playback) - startCurve;
                float linear = Mathf.Clamp01(Mathf.InverseLerp(phase.startTime, phase.endTime, playback));
                Vector3 accum = Vector3.zero;
                Vector3 prev = Vector3.zero;
                for (float t = phase.startTime + Step; t <= playback; t += Step)
                {
                    Vector3 now = m_Asset.GetVectorValue(t) - startCurve;
                    accum += Abs(now - prev);
                    prev = now;
                }
                targetDelta.x *= m_Asset.useLinear.x ? linear : Mathf.Clamp01(Div(accum.x, phase.totalRootMotion.x));
                targetDelta.y *= m_Asset.useLinear.y ? linear : Mathf.Clamp01(Div(accum.y, phase.totalRootMotion.y));
                targetDelta.z *= m_Asset.useLinear.z ? linear : Mathf.Clamp01(Div(accum.z, phase.totalRootMotion.z));
                return Mask(m_Asset.useAnimation, rootMotion) + targetDelta;
            }

            static Vector3 Mask(VectorBool use, Vector3 v) => new Vector3(use.x ? v.x : 0f, use.y ? v.y : 0f, use.z ? v.z : 0f);
            static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            static float Div(float a, float b) => Mathf.Approximately(b, 0f) ? 0f : a / b;
        }
    }
}
