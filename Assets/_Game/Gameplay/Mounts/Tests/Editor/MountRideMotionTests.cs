using KryticalNova.Mounts;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The four ways on and off a mount (owner, 6 Nov 2026): a standing summon hop, a running hop, a standing front flip
    /// and a running launch flip into a roll. The rider starts and ends where they should, the mount is solid as the rider
    /// lands on it and gone as the rider lands off it, and only running picks the running kind.
    /// </summary>
    public sealed class MountRideMotionTests
    {
        private MountSettings settings;

        [SetUp] public void Make() => settings = ScriptableObject.CreateInstance<MountSettings>();
        [TearDown] public void Drop() => Object.DestroyImmediate(settings);

        private static float LiftAt(MountTransition t, float seconds) => MountRideMotion.Lift(t.lift, seconds, t.seconds);

        [Test]
        public void TheClipCarriesTheHeight_TheCodeKeepsTheRiderOnTheSeat([Values(true, false)] bool on, [Values(false, true)] bool running)
        {
            // (both running getting-off endings are covered by RunningDismount_IsAllInTheClip_NoDrawnArc)
            // The Brambleback's seat is only 0.2-0.5 m above the rider's feet, so the hops and flips are keyed per stage
            // from the ground to the seat; the code's lift stays on the seat the whole way.
            MountTransition t = settings.Transition(on, running);
            for (float s = 0f; s <= t.seconds; s += 1f / 30f) Assert.AreEqual(1f, LiftAt(t, s), 0.001f);
        }

        [Test]
        public void GettingOff_IsBackOnThePlayer_BeforeTheRiderIsFree([Values(false, true)] bool running)
        {
            MountTransition off = settings.Transition(false, running);
            Assert.AreEqual(0f, MountRideMotion.Ahead(off.forward, off.freeSeconds, off.seconds), 0.001f,
                "back on the player's real position before the rider is free (no snap)");
        }

        [Test]
        public void RunningDismount_IsAllInTheClip_NoDrawnArc([Values(true, false)] bool endsRunning)
        {
            // Owner-approved 7 Nov 2026: take-off, flip, roll and ending are keyed; the player is never drawn ahead.
            MountTransition t = settings.Transition(false, true, endsRunning);
            for (float s = 0f; s <= t.seconds; s += 1f / 30f)
                Assert.AreEqual(0f, MountRideMotion.Ahead(t.forward, s, t.seconds), 0.001f);
        }

        [Test]
        public void RunningDismount_TwoEndings_TheIdleOneIsLonger()
        {
            Assert.AreSame(settings.getOffRunning, settings.Transition(false, true, true));
            Assert.AreSame(settings.getOffRunningToIdle, settings.Transition(false, true, false));
            Assert.Greater(settings.getOffRunningToIdle.seconds, settings.getOffRunning.seconds);
            Assert.Less(settings.getOffRunning.rollEndsAt, settings.getOffRunning.freeSeconds, "the roll ends before the rider is free");
            Assert.AreEqual(4f, settings.rollIntoRunMinDistance, 0.001f, "owner, 7 Nov 2026: 4 m");
        }

        [Test]
        public void RollIntoRun_OnlyWhenTheClickedSpotIsStillFarAsTheRollEnds()
        {
            // 10 m left at the press, 3 m/s on foot, roll ends at 1.77 s: 4.69 m left then, so up running.
            Assert.IsTrue(MountRules.DismountEndsRunning(10f, 3f, 1.77f, 4f));
            // 8 m left: 2.69 m left as the roll ends, so the roll ends standing.
            Assert.IsFalse(MountRules.DismountEndsRunning(8f, 3f, 1.77f, 4f));
            // No path (or the spot already reached): standing.
            Assert.IsFalse(MountRules.DismountEndsRunning(0f, 3f, 1.77f, 4f));
            // Exactly the threshold counts as running.
            Assert.IsTrue(MountRules.DismountEndsRunning(4f, 0f, 1.77f, 4f));
        }

        [Test]
        public void AHigherSeat_LowersTheRiderOnlyWhileTheFeetAreOnTheGround()
        {
            // Getting on: on the ground until the feet leave, on the seat from arrival.
            Assert.AreEqual(1f, MountRideMotion.OnGround(true, 0f, 0.2f, 0.77f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.OnGround(true, 0.2f, 0.2f, 0.77f), 0.001f);
            Assert.AreEqual(0f, MountRideMotion.OnGround(true, 0.77f, 0.2f, 0.77f), 0.001f);
            // Getting off: in the saddle until the take-off, on the ground from touchdown, and after it.
            Assert.AreEqual(0f, MountRideMotion.OnGround(false, 0.3f, 0.4f, 1.02f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.OnGround(false, 1.02f, 0.4f, 1.02f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.OnGround(false, 1.6f, 0.4f, 1.02f), 0.001f);
            // No times: the seat throughout.
            Assert.AreEqual(0f, MountRideMotion.OnGround(false, 1.6f, 0f, 0f), 0.001f);
            // The Circuit Strider's 50 % seat (0.48 m) against the Brambleback's the clips were keyed on (0.34 m).
            Assert.AreEqual(0.14f, MountRideMotion.GroundDrop(0.48f, 0.34f), 0.001f);
            // Keyed on this seat (or no keyed seat given): no drop.
            Assert.AreEqual(0f, MountRideMotion.GroundDrop(0.5f, 0f), 0.001f);
        }

        // Where the soles end, metres above the ground, after a getting-off on a built stage: the rider's origin is on the
        // seat less the ground drop (all of it once the feet are down), and the clip puts the soles the keyed seat height
        // below its origin (the stage's own seat when it was keyed on it).
        private float LandingSoles(MountDefinition mount, int stage)
        {
            MountStageLook look = mount.Stage(stage);
            MountTransition off = settings.Transition(false, false);
            float keyed = look.clipSeatHeight > 0f ? look.clipSeatHeight : look.seat.y;
            float drop = MountRideMotion.GroundDrop(look.seat.y, look.clipSeatHeight);
            float origin = look.seat.y - drop * MountRideMotion.OnGround(false, off.arrivesAt, off.leavesAt, off.arrivesAt);
            return origin - keyed;
        }

        private static MountDefinition Def(string key) => UnityEditor.AssetDatabase.LoadAssetAtPath<MountDefinition>(
            $"Assets/_Game/Gameplay/Mounts/Data/Assets/MountDef_{key}.asset");

        [Test]
        public void TheBrambleback_HasNoGroundDrop([Values(0, 1, 2)] int stage)
        {
            MountDefinition bramble = Def("Brambleback");
            Assume.That(bramble != null && bramble.IsBuilt, "the Brambleback is built (Items > 11. Build Mounts)");
            MountStageLook look = bramble.Stage(stage);
            Assert.AreEqual(0f, MountRideMotion.GroundDrop(look.seat.y, look.clipSeatHeight), 0.0001f);
        }

        [Test]
        public void TheCircuitStrider_LandsWithItsSolesOnTheGround([Values(0, 1, 2)] int stage)
        {
            MountDefinition strider = Def("CircuitStrider");
            Assume.That(strider != null && strider.IsBuilt, "the Circuit Strider is built (Items > 11. Build Mounts)");
            Assert.That(strider.hopMountUp, "the Strider rides the shared summon hop");
            Assert.Greater(strider.Stage(stage).clipSeatHeight, 0f, "its stages say which seat the shared clips were keyed on");
            Assert.AreEqual(0f, LandingSoles(strider, stage), 0.01f, "soles within 1 cm of the ground");
        }

        // Owner, 7 Nov 2026: the legs crossed at the end of the dismounts in game, though the clips keep them apart: the
        // rider clips' avatar measured leg angles from the seat straddle. Played on the player's real avatar, every
        // getting-off clip must keep the feet apart (along the hips) from the touchdown to its end.
        [Test]
        public void TheDismounts_NeverCrossTheLegsOnThePlayer(
            [Values("Dismount", "DismountRun", "DismountRunIdle")] string take, [Values(0, 50, 100)] int percent)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            AnimationClip clip = null;
            foreach (Object o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath($"Assets/_Game/Characters/Mounts/Rider/Animations/A_Rider_{take}_{percent}.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clip = c;
            Assume.That(prefab != null && clip != null, "the player prefab and the rider clip are in the project");
            GameObject player = Object.Instantiate(prefab);
            UnityEditor.AnimationMode.StartAnimationMode();
            try
            {
                Animator animator = null;
                foreach (Animator a in player.GetComponentsInChildren<Animator>(true)) if (a.avatar != null && a.avatar.isHuman) { animator = a; break; }
                Assert.NotNull(animator, "the player has a humanoid avatar");
                const float Touchdown = 1.02f;   // the latest touchdown of the three (MountSettings.getOff.arrivesAt)
                for (float t = Touchdown; t <= clip.length + 0.001f; t += 1f / 30f)
                {
                    UnityEditor.AnimationMode.BeginSampling();
                    UnityEditor.AnimationMode.SampleAnimationClip(animator.gameObject, clip, Mathf.Min(t, clip.length));
                    UnityEditor.AnimationMode.EndSampling();
                    Vector3 side = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position
                                   - animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;
                    side.y = 0f;
                    float apart = Vector3.Dot(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position
                                              - animator.GetBoneTransform(HumanBodyBones.RightFoot).position, side.normalized);
                    Assert.Greater(apart, 0.08f, $"{clip.name} at {t:F2} s: the feet are {apart * 100f:F0} cm apart (crossed or together)");
                }
            }
            finally
            {
                UnityEditor.AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(player);
            }
        }

        // Owner, 7 Nov 2026: at the stand-up the body dropped and the legs folded for a moment. The getting-off clip is keyed
        // on the seat origin and the idle on the ground origin; the origin came down at once while the clip was still
        // fading out. With the hand-over eased in step with the fade (MountRideMotion.HandOverShare), the hips never dip
        // and the feet never go under the ground, through the whole fade into the player's own idle.
        [Test]
        public void TheHandOverIntoTheIdle_NeitherDropsTheBodyNorSinksTheFeet(
            [Values("Dismount", "DismountRunIdle")] string take, [Values(0, 1, 2)] int stage)
        {
            int percent = new[] { 0, 50, 100 }[stage];
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            MountDefinition bramble = Def("Brambleback");
            AnimationClip clip = null, idle = null;
            foreach (Object o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath($"Assets/_Game/Characters/Mounts/Rider/Animations/A_Rider_{take}_{percent}.fbx"))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) clip = c;
            Assume.That(prefab != null && clip != null && bramble != null && bramble.IsBuilt, "the player, the clip and the Brambleback are in the project");
            foreach (MonoBehaviour mb in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb != null && mb.GetType().Name == "PlayerAnimancerMotor")
                    idle = new UnityEditor.SerializedObject(mb).FindProperty("idleClip").objectReferenceValue as AnimationClip;
            Assume.That(idle != null, "the player motor has its idle clip");

            float seat = bramble.Stage(stage).seat.y;
            GameObject player = Object.Instantiate(prefab);
            var graph = UnityEngine.Playables.PlayableGraph.Create("handover-test");
            try
            {
                Animator animator = null;
                foreach (Animator a in player.GetComponentsInChildren<Animator>(true)) if (a.avatar != null && a.avatar.isHuman) { animator = a; break; }
                animator.applyRootMotion = false;
                Transform rider = animator.transform;
                Vector3 rest = rider.localPosition;
                float ground = rider.position.y;
                var output = UnityEngine.Animations.AnimationPlayableOutput.Create(graph, "o", animator);
                var mixer = UnityEngine.Animations.AnimationMixerPlayable.Create(graph, 2);
                var end = UnityEngine.Animations.AnimationClipPlayable.Create(graph, clip);
                var start = UnityEngine.Animations.AnimationClipPlayable.Create(graph, idle);
                UnityEngine.Playables.PlayableExtensions.SetTime(end, clip.length);
                UnityEngine.Playables.PlayableExtensions.Pause(end);
                UnityEngine.Playables.PlayableExtensions.Pause(start);
                end.SetApplyFootIK(false); start.SetApplyFootIK(false);
                graph.Connect(end, 0, mixer, 0); graph.Connect(start, 0, mixer, 1);
                UnityEngine.Playables.PlayableOutputExtensions.SetSourcePlayable(output, mixer);

                float hipsAtEnd = 0f;
                for (int i = -1; i <= 10; i++)
                {
                    float w = i < 0 ? 0f : i / 10f;   // i = -1: the clip's last frame, still on the seat origin
                    UnityEngine.Playables.PlayableExtensions.SetInputWeight(mixer, 0, 1f - w);
                    UnityEngine.Playables.PlayableExtensions.SetInputWeight(mixer, 1, w);
                    float share = i < 0 ? 1f : MountRideMotion.HandOverShare(w, 1f);
                    rider.localPosition = rest + Vector3.up * (seat * share);
                    graph.Evaluate(0f);
                    float hips = animator.GetBoneTransform(HumanBodyBones.Hips).position.y - ground;
                    if (i < 0) { hipsAtEnd = hips; continue; }
                    float ankle = Mathf.Min(animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y,
                                            animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y) - ground;
                    Assert.LessOrEqual(hipsAtEnd - hips, 0.10f, $"{clip.name}: the hips dip {(hipsAtEnd - hips) * 100f:F0} cm at fade {w:F1}");
                    Assert.GreaterOrEqual(ankle, 0f, $"{clip.name}: an ankle is {ankle * 100f:F0} cm into the ground at fade {w:F1}");
                }
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void EveryGettingOff_HasItsBeatsInOrderWithinTheClip([Values(false, true)] bool running, [Values(false, true)] bool endsRunning)
        {
            MountTransition t = settings.Transition(false, running, endsRunning);
            Assert.Greater(t.apexAt, t.leavesAt, "the apex comes after the take-off");
            Assert.Greater(t.arrivesAt, t.apexAt, "the touchdown comes after the apex");
            if (running)
            {
                Assert.GreaterOrEqual(t.rollStartsAt, t.arrivesAt, "the roll starts at or after the touchdown");
                Assert.Greater(t.rollEndsAt, t.rollStartsAt, "the roll ends after it starts");
            }
            else Assert.AreEqual(0f, t.rollStartsAt, "the standing dismount has no roll");
            bool stands = !running || !endsRunning;
            if (stands) Assert.Greater(t.standsAt, running ? t.rollEndsAt - 0.2f : t.arrivesAt, "the stand-up comes after the landing or the roll");
            else Assert.AreEqual(0f, t.standsAt, "ending running there is no stand-up");
            Assert.LessOrEqual(Mathf.Max(t.standsAt, t.rollEndsAt), t.seconds, "every beat is within the clip");
        }

        [Test]
        public void EveryHopTransition_SaysWhenTheFeetLeaveAndArrive([Values(false, true)] bool on, [Values(false, true)] bool running)
        {
            foreach (bool endsRunning in new[] { false, true })
            {
                MountTransition t = settings.Transition(on, running, endsRunning);
                Assert.Greater(t.arrivesAt, t.leavesAt, "the feet must leave before they arrive");
                Assert.LessOrEqual(t.arrivesAt, t.seconds, "they arrive within the clip");
                // Getting off, the feet are down before the rider is free, so the tail plays on the ground.
                if (!on) Assert.LessOrEqual(t.arrivesAt, t.freeSeconds);
            }
        }

        [Test]
        public void RunningDismount_NeverStallsBeforeTheRollEnds()
        {
            // Ending running: the run speed the whole way.
            Assert.AreEqual(28f, MountRules.DismountTravelSpeed(true, 60f, 28f, 1.76f), 0.001f);
            // Ending standing, 20 m left: 20 / 1.76 = 11.36 m/s, so the body reaches the spot as the roll ends.
            Assert.AreEqual(20f / 1.76f, MountRules.DismountTravelSpeed(false, 20f, 28f, 1.76f), 0.001f);
            // Never faster than the run, even with a long path.
            Assert.AreEqual(28f, MountRules.DismountTravelSpeed(false, 100f, 28f, 1.76f), 0.001f);
            // No path left: no travel.
            Assert.AreEqual(0f, MountRules.DismountTravelSpeed(false, 0f, 28f, 1.76f), 0.001f);
        }

        [Test]
        public void TheMount_IsSolidAsTheRiderLandsOnIt([Values(false, true)] bool running)
        {
            MountTransition on = settings.Transition(true, running);
            Assert.AreEqual(on.stirrupsAt * on.seconds, on.effectDelay + on.effectSeconds, 0.03f,
                "the mount is solid as the rider lands on the seat (owner pick)");
            Assert.LessOrEqual(on.freeSeconds, on.seconds);
        }

        [Test]
        public void TheMount_IsGoneByTheTimeTheRiderIsFree([Values(false, true)] bool running, [Values(true, false)] bool endsRunning)
        {
            MountTransition off = settings.Transition(false, running, endsRunning);
            Assert.LessOrEqual(off.effectDelay + off.effectSeconds, off.freeSeconds);
        }

        [Test]
        public void OnlyRunning_PicksTheRunningKind()
        {
            Assert.IsTrue(MountRules.IsRunningTransition(true, true));
            Assert.IsFalse(MountRules.IsRunningTransition(true, false), "Run selected but standing still");
            Assert.IsFalse(MountRules.IsRunningTransition(false, true), "walking: the rider stops");
            Assert.IsFalse(MountRules.IsRunningTransition(false, false));
        }

        [Test]
        public void Curves_ClampOutsideTheClip_AndDefaultWithNoCurve()
        {
            AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            Assert.AreEqual(0f, MountRideMotion.Lift(curve, -1f, 1f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.Lift(curve, 5f, 1f), 0.001f);
            Assert.AreEqual(1f, MountRideMotion.Lift(null, 0.3f, 1f), 0.001f);
            Assert.AreEqual(0f, MountRideMotion.Ahead(null, 0.3f, 1f), 0.001f);
        }
    }
}
