using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Rendering.Tests
{
    /// <summary>The dangle-bone swing on a small Sidekick-named skeleton: a cape chain on the back under <c>spine_03</c>.</summary>
    public class DangleBonesTests
    {
        private const float Dt = 1f / 60f;
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        private sealed class Rig
        {
            public GameObject root;
            public DangleBones dangle;
            public Transform pelvis, neck;
            public Transform[] cape;
        }

        private static Transform Bone(string name, Transform parent, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = local;
            return t;
        }

        // A cape of three links hanging from 15 cm behind the upper spine, skinned by a tiny mesh.
        private Rig Build(float capeTilt = 0f)
        {
            var root = new GameObject("Character");
            made.Add(root);
            var skel = Bone("root", root.transform, Vector3.zero);
            var pelvis = Bone("pelvis", skel, new Vector3(0f, 1f, 0f));
            var spine2 = Bone("spine_02", pelvis, new Vector3(0f, 0.25f, 0f));
            var spine3 = Bone("spine_03", spine2, new Vector3(0f, 0.15f, 0f));
            var neck = Bone("neck_01", spine3, new Vector3(0f, 0.15f, 0f));
            var head = Bone("head", neck, new Vector3(0f, 0.1f, 0f));
            var c0 = Bone("abac_dyn_01", spine3, new Vector3(0f, 0f, -0.15f));
            c0.localRotation = Quaternion.Euler(capeTilt, 0f, 0f);
            var c1 = Bone("abac_dyn_01_01", c0, new Vector3(0f, -0.2f, 0f));
            var c2 = Bone("abac_dyn_01_01_01", c1, new Vector3(0f, -0.2f, 0f));
            var bones = new[] { skel, pelvis, spine2, spine3, neck, head, c0, c1, c2 };

            var mesh = new Mesh { name = "test" };
            made.Add(mesh);
            var verts = new Vector3[bones.Length];
            var weights = new BoneWeight[bones.Length];
            var binds = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                verts[i] = bones[i].position;
                weights[i] = new BoneWeight { boneIndex0 = i, weight0 = 1f };
                binds[i] = bones[i].worldToLocalMatrix * root.transform.localToWorldMatrix;
            }
            mesh.vertices = verts;
            mesh.boneWeights = weights;
            mesh.bindposes = binds;
            mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);

            var smr = new GameObject("mesh").AddComponent<SkinnedMeshRenderer>();
            smr.transform.SetParent(root.transform, false);
            smr.bones = bones;
            smr.sharedMesh = mesh;

            var dangle = root.AddComponent<DangleBones>();
            dangle.Rebind(new[] { smr });
            return new Rig { root = root, dangle = dangle, pelvis = pelvis, neck = neck, cape = new[] { c0, c1, c2 } };
        }

        private static float DistanceToSegment(Vector3 a, Vector3 b, Vector3 p)
        {
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        [Test]
        public void FindsTheChain()
        {
            var rig = Build();
            Assert.AreEqual(3, rig.dangle.BoneCount);
            Assert.IsTrue(rig.dangle.enabled);
        }

        [Test]
        public void StandingStillTheCapeStaysAtRest()
        {
            var rig = Build();
            Vector3 tip = rig.cape[2].position;
            for (int i = 0; i < 120; i++) rig.dangle.Step(Dt);
            Assert.Less(Vector3.Distance(tip, rig.cape[2].position), 0.01f, "a cape hanging straight down barely moves");
        }

        [Test]
        public void RunningTheCapeTrailsBehind()
        {
            var rig = Build();
            Vector3 rest = rig.cape[2].position - rig.root.transform.position;
            rig.dangle.Step(Dt);
            for (int i = 0; i < 30; i++)
            {
                rig.root.transform.position += Vector3.forward * (6f * Dt); // a sprint forward (+z)
                rig.dangle.Step(Dt);
            }
            Vector3 now = rig.cape[2].position - rig.root.transform.position;
            Assert.Less(now.z, rest.z - 0.02f, "the hem swings back behind the runner");
        }

        [Test]
        public void BackingIntoTheCapeNeverPassesThroughTheBody()
        {
            var rig = Build();
            rig.dangle.Step(Dt);
            const float keepOut = 0.12f + 0.02f - 0.005f; // spine capsule + cloth thickness, less a little slack
            for (int i = 0; i < 90; i++)
            {
                rig.root.transform.position += Vector3.back * (8f * Dt); // backwards fast: the cape is thrown at the back
                rig.dangle.Step(Dt);
                for (int c = 1; c < rig.cape.Length; c++)
                {
                    float d = DistanceToSegment(rig.pelvis.position, rig.neck.position, rig.cape[c].position);
                    Assert.GreaterOrEqual(d, keepOut, $"{rig.cape[c].name} went into the body on frame {i}");
                }
            }
        }

        [Test]
        public void StoppingDeadTheCapeStaysBehindTheBody()
        {
            // Owner, 24 Oct 2026: stopping from a run threw the Dock Runner's cape forward through the body.
            var rig = Build();
            rig.dangle.Step(Dt);
            for (int i = 0; i < 40; i++)
            {
                rig.root.transform.position += Vector3.forward * (9f * Dt);
                rig.dangle.Step(Dt);
            }
            for (int i = 0; i < 90; i++) // a dead stop: the cape swings on forward
            {
                rig.dangle.Step(Dt);
                for (int c = 1; c < rig.cape.Length; c++)
                {
                    float z = rig.cape[c].position.z - rig.root.transform.position.z;
                    Assert.Less(z, 0f, $"{rig.cape[c].name} came through to the front on frame {i}");
                }
            }
        }

        [Test]
        public void LinksKeepTheirLength()
        {
            var rig = Build();
            rig.dangle.Step(Dt);
            for (int i = 0; i < 60; i++)
            {
                rig.root.transform.position += new Vector3(Mathf.Sin(i * 0.3f), 0f, Mathf.Cos(i * 0.2f)) * 0.1f;
                rig.root.transform.rotation = Quaternion.Euler(0f, i * 7f, 0f);
                rig.dangle.Step(Dt);
            }
            Assert.AreEqual(0.2f, Vector3.Distance(rig.cape[0].position, rig.cape[1].position), 0.001f);
            Assert.AreEqual(0.2f, Vector3.Distance(rig.cape[1].position, rig.cape[2].position), 0.001f);
        }

        [Test]
        public void GravityPullsAStickingOutPieceDown()
        {
            var rig = Build(-90f); // the chain points straight back at rest
            Vector3 rest = rig.cape[2].position;
            Assert.AreEqual(rig.cape[0].position.y, rest.y, 0.001f);
            for (int i = 0; i < 120; i++) rig.dangle.Step(Dt);
            Assert.Less(rig.cape[2].position.y, rest.y - 0.02f);
        }

        [Test]
        public void RestoringPutsTheBonesBackAtRest()
        {
            var rig = Build();
            Quaternion rest0 = rig.cape[0].localRotation, rest1 = rig.cape[1].localRotation;
            rig.dangle.Step(Dt);
            for (int i = 0; i < 20; i++)
            {
                rig.root.transform.position += Vector3.forward * 0.1f;
                rig.dangle.Step(Dt);
            }
            Assert.Greater(Quaternion.Angle(rest0, rig.cape[0].localRotation) + Quaternion.Angle(rest1, rig.cape[1].localRotation), 1f);
            rig.dangle.RestorePose();
            Assert.Less(Quaternion.Angle(rest0, rig.cape[0].localRotation), 0.01f);
            Assert.Less(Quaternion.Angle(rest1, rig.cape[1].localRotation), 0.01f);
        }
    }
}
