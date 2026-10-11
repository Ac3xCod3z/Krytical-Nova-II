using KryticalNova.GameFeel;
using KryticalNova.Progression;
using MoreMountains.Feedbacks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The comic motion smear as the project is tuned: seen when a run takes off at any rank and on a thrown body,
    /// never on a walk, strong at its start and gone by its end, and dropped when a character is put somewhere in
    /// one step. And the blink of slow motion a critical kill gets, which must hand the body back exactly where it
    /// really is.
    /// </summary>
    public sealed class SmearTests
    {
        private const string SettingsPath = "Assets/_Game/GameFeel/Data/Assets/SmearSettings.asset";
        private const string FeelPrefabPath = "Assets/_Game/GameFeel/Prefabs/PFX_GameFeel.prefab";
        private const string MovementPath = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/MovementSkills/Movement.asset";
        private const string RunPath = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/MovementSkills/Run.asset";
        // The first frame of a critical death's throw as measured in Play: the fastest a body really moves.
        private const float ThrowLaunchSpeed = 80f;
        // The fastest a body moves when a normal death knocks it over, as measured in Play.
        private const float KnockDownSpeed = 2.5f;
        private const float Tolerance = 0.0001f;
        private static readonly float[] FrameTimes = { 1f / 144f, 1f / 60f, 1f / 30f };

        private SmearSettings settings;
        private MovementSettings movement;
        private MovementSkillData run;

        [OneTimeSetUp]
        public void Load()
        {
            settings = AssetDatabase.LoadAssetAtPath<SmearSettings>(SettingsPath);
            movement = AssetDatabase.LoadAssetAtPath<MovementSettings>(MovementPath);
            run = AssetDatabase.LoadAssetAtPath<MovementSkillData>(RunPath);
            Assert.NotNull(settings, SettingsPath);
            Assert.NotNull(movement, MovementPath);
            Assert.NotNull(run, RunPath);
        }

        /// <summary>How far a body is stretched that has held one speed for the whole of the smear's trail.</summary>
        private static float StretchAt(in SmearProfile profile, float speed, float life) =>
            profile.Stretch(Vector3.back * (speed * profile.trailSeconds), profile.trailSeconds, life).magnitude;

        /// <summary>A speed at which the profile's smear is whole and at its longest.</summary>
        private static float Fast(in SmearProfile profile) =>
            Mathf.Max(profile.slowest * 2f, profile.longest / profile.trailSeconds) * 2f;

        // ------------------------------------------------------------------ the smear

        [Test]
        public void Settings_CarryTheSmearPackagesShader()
        {
            Assert.NotNull(settings.shader, "the shader is linked in the asset so that a build ships it");
            Assert.AreEqual("WuHaipeng/smear_urp", settings.shader.name);
        }

        [Test]
        public void Walking_IsNeverSmeared()
        {
            Assert.Less(movement.walkSpeed, settings.takeOff.slowest);
            for (int step = 0; step <= 10; step++)
                Assert.AreEqual(0f, StretchAt(settings.takeOff, movement.walkSpeed, step / 10f), "a walk, at " + step + " tenths of the smear's life");
        }

        [Test]
        public void StandingStill_IsNeverSmeared()
        {
            Assert.AreEqual(Vector3.zero, settings.takeOff.Stretch(Vector3.zero, settings.takeOff.trailSeconds, 0f));
            Assert.AreEqual(Vector3.zero, settings.deathThrow.Stretch(Vector3.zero, settings.deathThrow.trailSeconds, 0f));
            // A smear that has only just begun has no past to be stretched back to.
            Assert.AreEqual(Vector3.zero, settings.takeOff.Stretch(Vector3.zero, 0f, 0f));
        }

        [Test]
        public void EveryRunRank_IsSmeared_WhenItTakesOff()
        {
            Assert.Greater(run.MaxRank, 0);
            for (int rank = 1; rank <= run.MaxRank; rank++)
            {
                Assert.IsTrue(run.TryGetRank(rank, out MovementSkillRank value));
                float stretch = StretchAt(settings.takeOff, value.runSpeed, 0f);
                Assert.Greater(stretch, 0.25f, $"run rank {rank} ({value.runSpeed} m/s)");
                Assert.LessOrEqual(stretch, settings.takeOff.longest + Tolerance, $"run rank {rank}");
            }
        }

        [Test]
        public void TheThrowOfADeath_IsSmearedToItsLongest()
        {
            Assert.AreEqual(settings.deathThrow.longest, StretchAt(settings.deathThrow, ThrowLaunchSpeed, 0f), Tolerance);
        }

        [Test]
        public void TheKnockDownOfANormalDeath_IsNotSmeared()
        {
            for (int step = 0; step <= 10; step++)
                Assert.AreEqual(0f, StretchAt(settings.deathThrow, KnockDownSpeed, step / 10f), "a knock-down, at " + step + " tenths of the smear's life");
        }

        [Test]
        public void ASmear_IsStrongAtItsStart_AndGoneByItsEnd()
        {
            foreach (SmearProfile profile in new[] { settings.takeOff, settings.deathThrow })
            {
                float fast = Fast(profile);
                float before = StretchAt(profile, fast, 0f);
                Assert.AreEqual(profile.longest, before, Tolerance, "whole at its start, and no longer than its longest");
                for (int step = 1; step <= 20; step++)
                {
                    float now = StretchAt(profile, fast, step / 20f);
                    Assert.LessOrEqual(now, before + Tolerance, "it never grows back (at " + step + " twentieths of its life)");
                    before = now;
                }
                Assert.AreEqual(0f, before, Tolerance, "gone by its end");
            }
        }

        [Test]
        public void TheSmear_PointsBackToWhereTheBodyWas()
        {
            Vector3 back = new Vector3(-2f, 0.3f, 1f);
            Vector3 stretch = settings.deathThrow.Stretch(back, settings.deathThrow.trailSeconds, 0f);
            Assert.Greater(stretch.magnitude, 0f);
            Assert.AreEqual(1f, Vector3.Dot(stretch.normalized, back.normalized), Tolerance);
        }

        [Test]
        public void BeingPutSomewhere_IsNotMotion()
        {
            var from = new Vector3(3f, 1f, -12f);
            // A teleport, a respawn, a large network correction: metres covered within one frame.
            Assert.IsTrue(settings.WasPlaced(from, from + Vector3.right * 8f, 1f / 60f));
            Assert.IsTrue(settings.WasPlaced(from, from + Vector3.forward * 8f, 1f / 144f));
            Assert.IsTrue(settings.WasPlaced(from, from + Vector3.forward * 40f, 1f / 30f));
            Assert.IsFalse(settings.WasPlaced(from, from, 1f / 60f));
        }

        [Test]
        public void TheFastestRealMotion_IsNotTakenForAPlacement()
        {
            Assert.IsTrue(run.TryGetRank(run.MaxRank, out MovementSkillRank top));
            foreach (float frame in FrameTimes)
            {
                Assert.IsFalse(settings.WasPlaced(Vector3.zero, Vector3.forward * (top.runSpeed * frame), frame), $"the top run rank, a frame of {frame} s");
                Assert.IsFalse(settings.WasPlaced(Vector3.zero, Vector3.forward * (ThrowLaunchSpeed * frame), frame), $"a death's throw, a frame of {frame} s");
            }
            // A stutter: one long frame at a full run must not cost the runner the smear.
            Assert.IsFalse(settings.WasPlaced(Vector3.zero, Vector3.forward * (top.runSpeed * 0.1f), 0.1f));
        }

        [TestCase("Run Start", SmearKind.TakeOff)]
        [TestCase("Death", SmearKind.DeathThrow)]
        [TestCase("Critical Death", SmearKind.DeathThrow)]
        public void TheFeelPrefab_PlaysTheSmear(string moment, SmearKind kind)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FeelPrefabPath);
            Assert.NotNull(prefab, FeelPrefabPath);
            Transform holder = prefab.transform.Find("World/" + moment);
            Assert.NotNull(holder, "World/" + moment);
            var player = holder.GetComponent<MMF_Player>();
            Assert.NotNull(player, moment + " has no MMF_Player");
            var smears = player.FeedbacksList.FindAll(feedback => feedback is MMF_Smear);
            Assert.AreEqual(1, smears.Count, "smear feedbacks on " + moment);
            var smear = (MMF_Smear)smears[0];
            Assert.IsTrue(smear.Active);
            Assert.AreSame(settings, smear.Settings);
            Assert.AreEqual(kind, smear.Kind);
        }

        // ------------------------------------------------------------------ when a run takes off

        private const float Repeat = 0.4f;
        private const float Rest = 1.5f;
        private const float Quiet = 1f;

        [Test]
        public void TakeOff_PlaysOnEveryRunFromAStandstill_NotOnlyTheFirst()
        {
            // Stop and go, a second apart: every start smears and kicks the view.
            float lastOrder = -100f, lastTakeOff = -100f, lastKick = -100f;
            foreach (float stood in new[] { 10f, 11f, 12f, 13f })
            {
                float start = stood + 0.05f;
                Assert.IsTrue(RunTakeOffRules.TakesOff(start, lastTakeOff, Repeat), $"the smear of the run that starts at {start}");
                Assert.IsTrue(RunTakeOffRules.KicksView(start, stood, lastOrder, lastKick, Rest, Quiet), $"the kick of the run that starts at {start}");
                lastOrder = lastTakeOff = lastKick = start;
            }
        }

        [Test]
        public void TakeOff_PlaysForANewDestinationChosenWhileRunning()
        {
            // Running since 10; a new destination every 0.6 s: each smears, none kicks the view again.
            const float stood = 9.9f, start = 10f;
            float lastOrder = start, lastTakeOff = start;
            for (float order = start + 0.6f; order < start + 4f; order += 0.6f)
            {
                Assert.IsTrue(RunTakeOffRules.TakesOff(order, lastTakeOff, Repeat), $"the smear of the order at {order}");
                Assert.IsFalse(RunTakeOffRules.KicksView(order, stood, lastOrder, start, Rest, Quiet), $"the kick of the order at {order}");
                lastOrder = lastTakeOff = order;
            }
        }

        [Test]
        public void TakeOff_IsNotRepeatedWithinAMoment()
        {
            Assert.IsFalse(RunTakeOffRules.TakesOff(10f + Repeat * 0.5f, 10f, Repeat));
            Assert.IsTrue(RunTakeOffRules.TakesOff(10f + Repeat * 1.1f, 10f, Repeat));
        }

        [Test]
        public void ViewKick_ComesBackAfterAQuietSpell_WithoutAStop()
        {
            const float stood = 10f, start = 10.4f;
            Assert.IsFalse(RunTakeOffRules.KicksView(start + Quiet * 0.9f, stood, start, start, Rest, Quiet));
            Assert.IsTrue(RunTakeOffRules.KicksView(start + 6f, stood, start + 6f - Quiet * 1.1f, start, Rest, Quiet));
        }

        // ------------------------------------------------------------------ the slow motion of a critical kill

        private static void CritKillSlow(out float slow, out float catchUp, out float rate)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FeelPrefabPath);
            Assert.NotNull(prefab, FeelPrefabPath);
            var presenter = prefab.GetComponentInChildren<GameFeelPresenter>(true);
            Assert.NotNull(presenter, "the game feel prefab has no presenter");
            var serialized = new SerializedObject(presenter);
            Vector2 seconds = serialized.FindProperty("critKillSlowSeconds").vector2Value;
            slow = seconds.x;
            catchUp = seconds.y;
            rate = serialized.FindProperty("critKillSlowRate").floatValue;
        }

        [Test]
        public void SlowMotion_FallsBehind_ThenHandsTheBodyBackExactly()
        {
            CritKillSlow(out float slow, out float catchUp, out float rate);
            Assert.Greater(slow, 0f, "the slow motion of a critical kill is switched on");
            float furthest = slow * (1f - rate);
            Assert.AreEqual(0f, ActorTimeSlow.LagAt(0f, slow, rate, catchUp));
            Assert.AreEqual(furthest * 0.5f, ActorTimeSlow.LagAt(slow * 0.5f, slow, rate, catchUp), Tolerance);
            Assert.AreEqual(furthest, ActorTimeSlow.LagAt(slow, slow, rate, catchUp), Tolerance);
            Assert.AreEqual(0f, ActorTimeSlow.LagAt(slow + catchUp, slow, rate, catchUp));
            Assert.AreEqual(0f, ActorTimeSlow.LagAt(slow + catchUp + 1f, slow, rate, catchUp));

            const int steps = 400;
            for (int step = 0; step <= steps; step++)
            {
                float lag = ActorTimeSlow.LagAt((slow + catchUp) * step / steps, slow, rate, catchUp);
                Assert.GreaterOrEqual(lag, 0f, "never ahead of where the body really is");
                Assert.LessOrEqual(lag, furthest + Tolerance, "never further behind than the slow motion put it");
            }
        }

        [Test]
        public void SlowMotion_NeverRunsThePictureBackwards_NorFasterThanItsAnimationCan()
        {
            CritKillSlow(out float slow, out float catchUp, out float rate);
            // The picture's pace is what the body's animation is set to, so that pose and place stay together: it
            // must stay inside what the animation is allowed, or the two drift apart.
            foreach (float frame in FrameTimes)
            {
                float before = 0f;
                for (float t = frame; t < slow + catchUp + frame; t += frame)
                {
                    float lag = ActorTimeSlow.LagAt(t, slow, rate, catchUp);
                    float pace = 1f - (lag - before) / frame;
                    Assert.GreaterOrEqual(pace, ActorTimeSlow.SlowestPace, $"at {t} s, a frame of {frame} s");
                    Assert.LessOrEqual(pace, ActorTimeSlow.FastestPace, $"at {t} s, a frame of {frame} s");
                    before = lag;
                }
            }
        }
    }
}
