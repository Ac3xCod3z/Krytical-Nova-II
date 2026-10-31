using Kinemation.MotionWarping.Runtime.Core;
using KryticalNova.Player.Entities;
using KryticalNova.Progression;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Persistence.Entities.Tests
{
    /// <summary>
    /// The entity-netcode jump reads the same assets and the same rules as the NGO jump (M3b, plan section 53): the jump clip's
    /// phases and height curve, <c>JumpMotion.asset</c> and the Jump ranks.
    /// </summary>
    public class JumpSourceTests
    {
        const string Folder = "Assets/_Game/Gameplay/Progression/Data/Assets/Skills/MovementSkills/";
        const string WarpPath = "Assets/_Game/Gameplay/Player/Data/Assets/Warp_Player_DoubleJump.asset";

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.IsNotNull(asset, path);
            return asset;
        }

        [Test]
        public void TheCurves_ComeFromTheJumpClip()
        {
            var warp = Load<MotionWarpingAsset>(WarpPath);
            var motion = Load<JumpMotionData>(Folder + "JumpMotion.asset");
            Assert.AreEqual(2, warp.warpPhases.Count, "take-off to apex, apex to touchdown");
            float takeoff = warp.warpPhases[0].startTime, apex = warp.warpPhases[0].endTime, land = warp.warpPhases[1].endTime;
            var curves = PlayerMotionCurves.FromClip(takeoff, apex, land, motion.landingCooldown, t => warp.rootY.Evaluate(t));
            Debug.Log($"[KnJump] clip take-off {takeoff:F3} s, apex {apex:F3} s, touchdown {land:F3} s, cooldown {motion.landingCooldown:F2} s; " +
                      $"rise {string.Join(",", ToArray(curves.Rise))}; fall {string.Join(",", ToArray(curves.Fall))}");
            Assert.AreEqual(183333, curves.TakeoffMicros, 1000);
            Assert.AreEqual(766667, curves.ApexMicros, 1000);
            Assert.AreEqual(1406667, curves.LandMicros, 1000);
            Assert.AreEqual(250000, curves.CooldownMicros);
            Assert.AreEqual(0, curves.Rise[0]);
            Assert.AreEqual(1000, curves.Rise[PlayerMotionCurves.Samples]);
            Assert.AreEqual(0, curves.Fall[0]);
            Assert.AreEqual(1000, curves.Fall[PlayerMotionCurves.Samples]);
            // the defaults carry the same times, so a world without the baked singleton still jumps on the clip's clock
            var defaults = PlayerMotionCurves.Default;
            Assert.AreEqual(defaults.TakeoffMicros, curves.TakeoffMicros, 1000);
            Assert.AreEqual(defaults.ApexMicros, curves.ApexMicros, 1000);
            Assert.AreEqual(defaults.LandMicros, curves.LandMicros, 1000);
        }

        [Test]
        public void TheGamesCurves_AreBakedFromThePlayerPrefabsJump()
        {
            var player = Load<GameObject>("Assets/_Game/Gameplay/Player/Prefabs/Player.prefab");
            var warp = Load<MotionWarpingAsset>(WarpPath);
            KryticalNova.Player.Entities.PlayerMotionCurves? baked = KryticalNova.Persistence.Entities.PlayerJumpFromSkills.CurvesOf(player);
            Assert.IsTrue(baked.HasValue, "the Player prefab has a jump motor with a two-phase clip");
            var expected = PlayerMotionCurves.FromClip(warp.warpPhases[0].startTime, warp.warpPhases[0].endTime, warp.warpPhases[1].endTime,
                player.GetComponent<KryticalNova.Player.PlayerJumpMotor>().MotionData.landingCooldown, t => warp.rootY.Evaluate(t));
            Assert.AreEqual(expected.TakeoffMicros, baked.Value.TakeoffMicros);
            Assert.AreEqual(expected.ApexMicros, baked.Value.ApexMicros);
            Assert.AreEqual(expected.LandMicros, baked.Value.LandMicros);
            Assert.AreEqual(expected.CooldownMicros, baked.Value.CooldownMicros);
            CollectionAssert.AreEqual(ToArray(expected.Rise), ToArray(baked.Value.Rise));
            CollectionAssert.AreEqual(ToArray(expected.Fall), ToArray(baked.Value.Fall));
            CollectionAssert.AreEqual(ToArray(expected.Crouch), ToArray(baked.Value.Crouch));
            int deepest = 0;
            foreach (short mm in ToArray(baked.Value.Crouch)) deepest = Mathf.Min(deepest, mm);
            Debug.Log($"[KnJump] crouch {string.Join(",", ToArray(baked.Value.Crouch))} mm; deepest {deepest} mm");
            Assert.Less(deepest, -100, "the jump clip crouches (S7: the warp lowered the root about 20 cm)");
        }

        [Test]
        public void TheOwnersRanks_AreTheServersRanks_SoItsOwnJumpCheckIsTheServers()
        {
            // GUI run 4 (31 Oct): the owner's body had no ranks, so its planner said "Learn Jump first." and the jump waited for the server
            var go = new GameObject("ranks");
            try
            {
                var skills = go.AddComponent<PlayerMovementSkills>();
                var so = new SerializedObject(skills);
                so.FindProperty("runSkill").objectReferenceValue = Load<MovementSkillData>(Folder + "Run.asset");
                so.FindProperty("jumpSkill").objectReferenceValue = Load<MovementSkillData>(Folder + "Jump.asset");
                so.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsFalse(skills.TryGetLearnedRank(MovementSkillId.Jump, out _), "no rank before the server sends one");

                skills.MirrorRanks(3, 2);
                Assert.AreEqual(3, skills.RunRank);
                Assert.AreEqual(2, skills.JumpRank);
                Assert.IsTrue(skills.TryGetLearnedRank(MovementSkillId.Jump, out MovementSkillRank rank));
                Assert.IsTrue(skills.GetDefinition(MovementSkillId.Jump).TryGetRank(2, out MovementSkillRank expected));
                Assert.AreEqual(expected.jumpDistance, rank.jumpDistance, "the owner's limits are the rank's");

                // the server's side of the same field reads the body's ranks
                MoveRanks? sent = KryticalNova.Persistence.Entities.PlayerMoveFromSkills.RanksOf(go);
                Assert.IsTrue(sent.HasValue);
                Assert.AreEqual(3, sent.Value.Run);
                Assert.AreEqual(2, sent.Value.Jump);

                // out of range is clamped; 0 takes the rank away again, refused with NGO's words on both sides
                skills.MirrorRanks(999, 0);
                Assert.AreEqual(skills.GetDefinition(MovementSkillId.Run).MaxRank, skills.RunRank);
                Assert.IsFalse(skills.TryGetLearnedRank(MovementSkillId.Jump, out _));
                Assert.AreEqual("Learn Jump first.", KryticalNova.Player.PlayerJumpMotor.LearnJumpFirst);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheOwnersFlux_IsTheServersFlux_AndItsOwnStatsNeverOverwriteIt()
        {
            // Master, 31 Oct: an owner that predicts a jump it cannot pay for starts it and is snapped back
            var go = new GameObject("flux");
            try
            {
                var resources = go.AddComponent<PlayerResources>();
                var so = new SerializedObject(resources);
                so.FindProperty("resourceData").objectReferenceValue =
                    Load<CharacterResourceData>("Assets/_Game/Gameplay/Progression/Data/Assets/CharacterData/CharacterResources.asset");
                so.ApplyModifiedPropertiesWithoutUndo();

                resources.MirrorChi(4f, 100f);
                Assert.AreEqual(4f, resources.Chi, 1e-4f);
                Assert.AreEqual(100f, resources.MaxChi, 1e-4f, "the server's maximum, not one from this body's stats");
                resources.MirrorChi(150f, 100f);
                Assert.AreEqual(100f, resources.Chi, 1e-4f, "never above the maximum");

                KryticalNova.Player.Entities.OwnerFlux? sent = KryticalNova.Persistence.Entities.PlayerMoveFromSkills.FluxOf(go);
                Assert.IsTrue(sent.HasValue);
                Assert.AreEqual(100000, sent.Value.Milli);
                Assert.AreEqual(100000, sent.Value.MaxMilli);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TheRanksLimits_AreTheNgoJumps()
        {
            var jump = Load<MovementSkillData>(Folder + "Jump.asset");
            Assert.IsTrue(jump.TryGetRank(1, out MovementSkillRank rank1));
            Assert.IsTrue(jump.TryGetRank(jump.MaxRank, out MovementSkillRank top));
            Debug.Log($"[KnJump] rank 1: {rank1.jumpDistance} m, +{rank1.jumpHeight} m, {rank1.chiCost} Flux; top rank {jump.MaxRank}: " +
                      $"{top.jumpDistance} m, +{top.jumpHeight} m, {top.chiCost} Flux");
            Vector3 start = new Vector3(1f, 2f, 3f);
            float d = rank1.jumpDistance, h = rank1.jumpHeight;
            Assert.IsNull(JumpMotionData.LimitRefusal(start, start + new Vector3(d - 0.01f, 0f, 0f), d, h));
            StringAssert.StartsWith("Too far", JumpMotionData.LimitRefusal(start, start + new Vector3(d + 0.01f, 0f, 0f), d, h));
            StringAssert.StartsWith("Too high", JumpMotionData.LimitRefusal(start, start + new Vector3(5f, h + 0.01f, 0f), d, h));
            Assert.IsNull(JumpMotionData.LimitRefusal(start, start + new Vector3(5f, -500f, 0f), d, h), "down has no limit");
            StringAssert.StartsWith("Choose a landing point farther", JumpMotionData.LimitRefusal(start, start + new Vector3(0.1f, 0f, 0f), d, h));
        }

        [Test]
        public void TheApex_IsTheClearanceOrTheRiseTheDistanceEarns()
        {
            var motion = Load<JumpMotionData>(Folder + "JumpMotion.asset");
            Vector3 start = Vector3.zero;
            // a short hop: the clearance over the higher end wins
            float hop = motion.PeakHeight(start, new Vector3(1f, 0f, 0f), 20f, 20f);
            Assert.AreEqual(motion.apexClearance, hop, 1e-4f);
            // 6 m of 20: rise 20 x 0.3^1.5 = 3.29 m beats the 1.4 m clearance
            float mid = motion.PeakHeight(start, new Vector3(6f, 0f, 0f), 20f, 20f);
            Assert.AreEqual(Mathf.Max(motion.apexClearance, 20f * Mathf.Pow(0.3f, motion.heightByDistanceExponent)), mid, 1e-3f);
            // full range climbs to the rank's height
            Assert.AreEqual(20f, motion.PeakHeight(start, new Vector3(20f, 0f, 0f), 20f, 20f), 1e-3f);
        }

        static short[] ToArray(Unity.Collections.FixedList128Bytes<short> list)
        {
            var array = new short[list.Length];
            for (int i = 0; i < list.Length; i++) array[i] = list[i];
            return array;
        }
    }
}
