using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Rendering.Tests
{
    /// <summary>
    /// Perf round 3, R3-4 (15 Nov 2026): the jobbed <see cref="DangleBones"/> swings every bone the same way as the old
    /// per-character code (<see cref="DangleBonesLegacy"/>). Three characters, a fox-like tail, a two-panel cape and forked
    /// hair, on a Sidekick-named body that runs, turns, stops, bends and is scaled; 120 frames each; every dangle bone's
    /// rotation stays within 1e-4 rad of the old code's every frame.
    /// </summary>
    public class DangleBonesSameAnswerTests
    {
        private const float Dt = 1f / 60f;
        private const int Frames = 120;
        private const float MaxRadians = 1e-4f;
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private sealed class Body
        {
            public GameObject root;
            public Transform spine2, head, thighL, thighR;
            public readonly List<Transform> dyn = new List<Transform>();
            public SkinnedMeshRenderer smr;
        }

        private static Transform Bone(string name, Transform parent, Vector3 local, Vector3 euler = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            t.localRotation = Quaternion.Euler(euler);
            return t;
        }

        private static void Chain(Body b, string prefix, Transform under, Vector3 start, Vector3 link, int links, Vector3 tilt)
        {
            var t = Bone(prefix, under, start, tilt);
            b.dyn.Add(t);
            for (int i = 1; i < links; i++)
            {
                t = Bone($"{prefix}_{i:00}", t, link, new Vector3(4f * i, -3f * i, 2f));
                b.dyn.Add(t);
            }
        }

        // kind 0: tail; 1: cape of two panels; 2: hair that forks.
        private Body Build(int kind, float scale)
        {
            var b = new Body { root = new GameObject("Character" + kind) };
            made.Add(b.root);
            b.root.transform.localScale = Vector3.one * scale;
            var skel = Bone("root", b.root.transform, Vector3.zero);
            var pelvis = Bone("pelvis", skel, new Vector3(0f, 1f, 0f));
            b.spine2 = Bone("spine_02", pelvis, new Vector3(0f, 0.25f, 0f));
            var spine3 = Bone("spine_03", b.spine2, new Vector3(0f, 0.15f, 0f));
            var neck = Bone("neck_01", spine3, new Vector3(0f, 0.15f, 0f));
            b.head = Bone("head", neck, new Vector3(0f, 0.1f, 0f));
            b.thighL = Bone("thigh_l", pelvis, new Vector3(-0.1f, -0.05f, 0f), new Vector3(0f, 0f, 180f));
            b.thighR = Bone("thigh_r", pelvis, new Vector3(0.1f, -0.05f, 0f), new Vector3(0f, 0f, 180f));
            var calfL = Bone("calf_l", b.thighL, new Vector3(0f, 0.45f, 0f));
            var calfR = Bone("calf_r", b.thighR, new Vector3(0f, 0.45f, 0f));
            var footL = Bone("foot_l", calfL, new Vector3(0f, 0.42f, 0f));
            var footR = Bone("foot_r", calfR, new Vector3(0f, 0.42f, 0f));
            var armL = Bone("upperarm_l", spine3, new Vector3(-0.2f, 0.1f, 0f), new Vector3(0f, 0f, 80f));
            var armR = Bone("upperarm_r", spine3, new Vector3(0.2f, 0.1f, 0f), new Vector3(0f, 0f, -80f));
            var foreL = Bone("lowerarm_l", armL, new Vector3(0f, 0.28f, 0f));
            var foreR = Bone("lowerarm_r", armR, new Vector3(0f, 0.28f, 0f));
            Bone("hand_l", foreL, new Vector3(0f, 0.25f, 0f));
            Bone("hand_r", foreR, new Vector3(0f, 0.25f, 0f));

            if (kind == 0) Chain(b, "abac_dyn_01", pelvis, new Vector3(0f, 0f, -0.14f), new Vector3(0f, -0.06f, -0.12f), 5, new Vector3(-30f, 0f, 0f));
            else if (kind == 1)
            {
                Chain(b, "tors_dyn_01_01_l", spine3, new Vector3(-0.08f, 0f, -0.15f), new Vector3(0f, -0.18f, 0f), 4, Vector3.zero);
                Chain(b, "tors_dyn_01_01_r", spine3, new Vector3(0.08f, 0f, -0.15f), new Vector3(0f, -0.18f, 0f), 4, Vector3.zero);
            }
            else
            {
                Chain(b, "hair_dyn_01", b.head, new Vector3(0f, 0.08f, -0.08f), new Vector3(0f, -0.07f, -0.03f), 3, new Vector3(20f, 0f, 0f));
                var fork = b.dyn[b.dyn.Count - 1];
                var f1 = Bone("hair_dyn_01_a", fork, new Vector3(-0.03f, -0.07f, 0f)); b.dyn.Add(f1);
                var f2 = Bone("hair_dyn_01_b", fork, new Vector3(0.03f, -0.07f, 0f)); b.dyn.Add(f2);
                b.dyn.Add(Bone("hair_dyn_01_a_01", f1, new Vector3(0f, -0.06f, 0f)));
            }

            var bones = new List<Transform>();
            foreach (var t in b.root.GetComponentsInChildren<Transform>()) if (t != b.root.transform) bones.Add(t);
            var mesh = new Mesh { name = "test" };
            made.Add(mesh);
            var verts = new Vector3[bones.Count];
            var weights = new BoneWeight[bones.Count];
            var binds = new Matrix4x4[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                verts[i] = bones[i].position;
                weights[i] = new BoneWeight { boneIndex0 = i, weight0 = 1f };
                binds[i] = bones[i].worldToLocalMatrix * b.root.transform.localToWorldMatrix;
            }
            mesh.vertices = verts;
            mesh.boneWeights = weights;
            mesh.bindposes = binds;
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            b.smr = new GameObject("mesh").AddComponent<SkinnedMeshRenderer>();
            b.smr.transform.SetParent(b.root.transform, false);
            b.smr.bones = bones.ToArray();
            b.smr.sharedMesh = mesh;
            return b;
        }

        // The same body move for both: run forward, turn, stop dead, back up, a big jump (teleport), and a bending spine.
        private static void Move(Body b, int frame)
        {
            var t = b.root.transform;
            if (frame < 40) t.position += t.forward * (6f * Dt);
            else if (frame < 55) { t.Rotate(0f, 6f, 0f); t.position += t.forward * (4f * Dt); }
            else if (frame < 70) { } // stopped dead
            else if (frame < 95) t.position -= t.forward * (8f * Dt);
            else if (frame == 95) t.position += new Vector3(5f, 0f, 0f); // past the teleport distance
            else t.position += t.right * (3f * Dt);
            b.spine2.localRotation = Quaternion.Euler(12f * Mathf.Sin(frame * 0.15f), 8f * Mathf.Sin(frame * 0.1f), 0f);
            b.head.localRotation = Quaternion.Euler(0f, 25f * Mathf.Sin(frame * 0.2f), 0f);
            b.thighL.localRotation = Quaternion.Euler(35f * Mathf.Sin(frame * 0.3f), 0f, 180f);
            b.thighR.localRotation = Quaternion.Euler(-35f * Mathf.Sin(frame * 0.3f), 0f, 180f);
        }

        // Quaternion.Angle reads anything under about 0.16 degrees as 0: the angle of the turn between the two, exactly.
        private static float Radians(Quaternion a, Quaternion b)
        {
            var r = Quaternion.Inverse(a) * b;
            double v = System.Math.Sqrt((double)r.x * r.x + (double)r.y * r.y + (double)r.z * r.z);
            return (float)(2.0 * System.Math.Atan2(v, System.Math.Abs((double)r.w)));
        }

        [TestCase(0, 1f, TestName = "SameAnswer_FoxTail")]
        [TestCase(1, 1.12f, TestName = "SameAnswer_CapeScaled")]
        [TestCase(2, 0.93f, TestName = "SameAnswer_ForkedHair")]
        public void JobbedSwingMatchesTheOldCode(int kind, float scale)
        {
            var oldBody = Build(kind, scale);
            var newBody = Build(kind, scale);
            var legacy = new DangleBonesLegacy(oldBody.root);
            legacy.Rebind(new[] { oldBody.smr });
            var jobbed = newBody.root.AddComponent<DangleBones>();
            jobbed.Rebind(new[] { newBody.smr });
            Assert.AreEqual(legacy.BoneCount, jobbed.BoneCount);
            Assert.Greater(jobbed.BoneCount, 2);

            float worst = 0f, worstMetres = 0f, moved = 0f;
            string where = "";
            var bins = new float[Frames / 10];
            var start = new Quaternion[newBody.dyn.Count];
            for (int i = 0; i < start.Length; i++) start[i] = newBody.dyn[i].localRotation;
            for (int f = 0; f < Frames; f++)
            {
                Move(oldBody, f); Move(newBody, f);
                legacy.Step(Dt); jobbed.Step(Dt);
                for (int i = 0; i < newBody.dyn.Count; i++)
                {
                    float rad = Radians(oldBody.dyn[i].rotation, newBody.dyn[i].rotation);
                    if (rad > worst) { worst = rad; where = $"{newBody.dyn[i].name} frame {f}"; }
                    bins[f / 10] = Mathf.Max(bins[f / 10], rad);
                    worstMetres = Mathf.Max(worstMetres, Vector3.Distance(oldBody.dyn[i].position, newBody.dyn[i].position));
                    moved = Mathf.Max(moved, Quaternion.Angle(start[i], newBody.dyn[i].localRotation));
                }
            }
            Assert.Greater(moved, 1f, "the test moved the pieces (degrees)");
            TestContext.WriteLine("per 10 frames: " + string.Join(" ", System.Array.ConvertAll(bins, x => x.ToString("E1"))));
            Assert.LessOrEqual(worst, MaxRadians, $"{where}: {worst:E2} rad from the old code ({worstMetres:E2} m); per 10 frames: " + string.Join(" ", System.Array.ConvertAll(bins, x => x.ToString("E1"))));
            TestContext.WriteLine($"worst {worst:E2} rad and {worstMetres:E2} m over {Frames} frames, {jobbed.BoneCount} bones, swing up to {moved:F1} deg");
        }

        // The old code against itself, the second character 10 micrometres to the side: how much float rounding alone
        // grows in each phase of the same move (the floor any rewrite of the swing is measured against).
        [TestCase(0, 1f, TestName = "NoiseFloor_FoxTail")]
        [TestCase(1, 1.12f, TestName = "NoiseFloor_CapeScaled")]
        [TestCase(2, 0.93f, TestName = "NoiseFloor_ForkedHair")]
        public void OldCodeAgainstItselfNudged(int kind, float scale)
        {
            var a = Build(kind, scale);
            var b = Build(kind, scale);
            b.root.transform.position += new Vector3(1e-5f, 0f, 0f);
            var la = new DangleBonesLegacy(a.root); la.Rebind(new[] { a.smr });
            var lb = new DangleBonesLegacy(b.root); lb.Rebind(new[] { b.smr });
            var bins = new float[Frames / 10];
            for (int f = 0; f < Frames; f++)
            {
                Move(a, f); Move(b, f);
                la.Step(Dt); lb.Step(Dt);
                for (int i = 0; i < a.dyn.Count; i++)
                    bins[f / 10] = Mathf.Max(bins[f / 10], Radians(a.dyn[i].rotation, b.dyn[i].rotation));
            }
            Assert.Fail("noise floor per 10 frames: " + string.Join(" ", System.Array.ConvertAll(bins, x => x.ToString("E1"))));
        }
    }
}
