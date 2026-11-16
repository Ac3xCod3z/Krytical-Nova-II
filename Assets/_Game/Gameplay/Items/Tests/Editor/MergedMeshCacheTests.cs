using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Items.Tests
{
    /// <summary>
    /// Characters that look alike share one merged mesh (R3-5 B, 16 Nov 2026): the shared mesh is the one the character's
    /// own merge would have built, the skeleton gets the same dangle bones, and the mesh lives while anyone shows it.
    /// </summary>
    public sealed class MergedMeshCacheTests
    {
        private readonly List<Object> made = new List<Object>();
        private static readonly FaceShapeWeight[] Face = { new FaceShapeWeight("mouthSmileLeft", 22f) };
        private static readonly string[] Live = { "eyeBlinkLeft" };

        [TearDown]
        public void CleanUp()
        {
            foreach (var o in made) if (o != null) Object.DestroyImmediate(o);
            made.Clear();
        }

        // A rig: root > hips (the reference bones, bind poses at rest).
        private (Transform root, SkinnedMeshMerger merger) Rig()
        {
            var root = new GameObject("Body").transform;
            made.Add(root.gameObject);
            var hips = new GameObject("hips").transform;
            hips.SetParent(root, false);
            hips.localPosition = new Vector3(0, 1, 0);
            var bones = new[] { root, hips };
            var poses = new[] { root.worldToLocalMatrix, hips.worldToLocalMatrix };
            return (root, new SkinnedMeshMerger(root, bones, poses) { KeepReadable = true });
        }

        // A skirt part weighted to hips and a dangle bone it brings, with a body key, a face key and a live blink.
        private SidekickPartRef Skirt()
        {
            var mesh = new Mesh { name = "SK_TEST_Skirt" };
            mesh.SetVertices(new List<Vector3> { new Vector3(0, 0.9f, 0), new Vector3(0.2f, 0.9f, 0), new Vector3(0.1f, 0.6f, 0) });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1f }, new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                new BoneWeight { boneIndex0 = 1, weight0 = 1f },
            };
            mesh.bindposes = new[] { Matrix4x4.Translate(new Vector3(0, -1, 0)), Matrix4x4.Translate(new Vector3(0, -0.8f, 0)) };
            mesh.AddBlendShapeFrame("10TORSBlends.defaultHeavy", 100f,
                new[] { new Vector3(0.01f, 0, 0), new Vector3(0.02f, 0, 0), new Vector3(0.03f, 0, 0) }, null, null);
            mesh.AddBlendShapeFrame("10TORSBlends.mouthSmileLeft", 100f,
                new[] { new Vector3(0, 0.01f, 0), Vector3.zero, Vector3.zero }, null, null);
            mesh.AddBlendShapeFrame("10TORSBlends.eyeBlinkLeft", 100f,
                new[] { Vector3.zero, new Vector3(0, 0, 0.05f), Vector3.zero }, null, null);
            made.Add(mesh);
            return new SidekickPartRef
            {
                region = BodyRegion.Torso, mesh = mesh, boneNames = new[] { "hips", "hips_dyn_01" },
                extraBones = new[]
                {
                    new ExtraBone { name = "hips_dyn_01", parent = "hips", localPosition = new Vector3(0, -0.2f, 0.05f), localRotation = Quaternion.Euler(10, 0, 0), localScale = Vector3.one },
                },
            };
        }

        private static readonly BodyShapeWeights Shape = new BodyShapeWeights { heavy = 40f, buff = 90f };

        private Mesh MergeAlone(SkinnedMeshMerger merger, SidekickPartRef[] parts, BodyShapeWeights shape)
        {
            var bones = new List<Transform>();
            var mesh = merger.Merge(parts, shape, bones, "alone", Face, Live);
            made.Add(mesh);
            return mesh;
        }

        [Test]
        public void ASharedMeshIsTheMeshTheCharactersOwnMergeBuilds()
        {
            var parts = new[] { Skirt() };
            var first = Rig();
            var second = Rig();
            Assert.That(second.merger.ReferenceKey, Is.SameAs(first.merger.ReferenceKey), "same rig, same reference key");

            // The first character merges and keeps the mesh.
            Assert.That(MergedMeshCache.Describe(first.merger.ReferenceKey, parts, Shape, Face, Live, null), Is.True);
            Assert.That(MergedMeshCache.TryShare(), Is.Null);
            var bones = new List<Transform>();
            var kept = first.merger.Merge(parts, Shape, bones, "kept", Face, Live);
            var entry = MergedMeshCache.Keep(kept, bones, first.merger.Paints, first.merger.PaintParts);

            // The second, same look, gets that mesh, counted twice.
            Assert.That(MergedMeshCache.Describe(second.merger.ReferenceKey, parts, Shape, Face, Live, null), Is.True);
            var shared = MergedMeshCache.TryShare();
            Assert.That(shared, Is.SameAs(entry));
            Assert.That(shared.Users, Is.EqualTo(2));

            // ...which is exactly what its own merge builds.
            var third = Rig();
            var alone = MergeAlone(third.merger, parts, Shape);
            AssertSameMesh(shared.Mesh, alone);
            Assert.That(shared.BoneNames, Is.EqualTo(new[] { "Body", "hips", "hips_dyn_01" }));

            MergedMeshCache.Release(kept);
            Assert.That(kept != null, Is.True, "still shown by one character");
            MergedMeshCache.Release(kept);
            Assert.That(kept == null, Is.True, "destroyed when the last one lets it go");
        }

        [Test]
        public void PrepareBonesGrowsTheSameDangleBonesAsAMerge()
        {
            var parts = new[] { Skirt() };
            var merged = Rig();
            MergeAlone(merged.merger, parts, Shape);
            var prepared = Rig();
            Assert.That(prepared.merger.FindBone("hips_dyn_01"), Is.Null);
            prepared.merger.PrepareBones(parts);

            var a = merged.merger.FindBone("hips_dyn_01");
            var b = prepared.merger.FindBone("hips_dyn_01");
            Assert.That(b, Is.Not.Null);
            Assert.That(b.parent.name, Is.EqualTo(a.parent.name));
            Assert.That(b.localPosition, Is.EqualTo(a.localPosition));
            Assert.That(b.localRotation, Is.EqualTo(a.localRotation));
            Assert.That(b.localScale, Is.EqualTo(a.localScale));
        }

        [Test]
        public void AnyDifferenceInTheLookMergesAgain()
        {
            var parts = new[] { Skirt() };
            var rig = Rig();
            MergedMeshCache.Describe(rig.merger.ReferenceKey, parts, Shape, Face, Live, null);
            var bones = new List<Transform>();
            var kept = rig.merger.Merge(parts, Shape, bones, "kept", Face, Live);
            MergedMeshCache.Keep(kept, bones, rig.merger.Paints, rig.merger.PaintParts);

            var heavier = Shape;
            heavier.heavy = 41f;
            MergedMeshCache.Describe(rig.merger.ReferenceKey, parts, heavier, Face, Live, null);
            Assert.That(MergedMeshCache.TryShare(), Is.Null, "another body shape");

            MergedMeshCache.Describe(rig.merger.ReferenceKey, parts, Shape, new[] { new FaceShapeWeight("mouthSmileLeft", 23f) }, Live, null);
            Assert.That(MergedMeshCache.TryShare(), Is.Null, "another resting face");

            MergedMeshCache.Describe(rig.merger.ReferenceKey, parts, Shape, Face, new string[0], null);
            Assert.That(MergedMeshCache.TryShare(), Is.Null, "no live blink");

            MergedMeshCache.Describe(rig.merger.ReferenceKey, new[] { Skirt() }, Shape, Face, Live, null);
            Assert.That(MergedMeshCache.TryShare(), Is.Null, "another part");

            var otherRig = Rig();
            otherRig.root.Find("hips").localPosition = new Vector3(0, 1.1f, 0);
            var other = new SkinnedMeshMerger(otherRig.root, new[] { otherRig.root, otherRig.root.Find("hips") },
                new[] { otherRig.root.worldToLocalMatrix, otherRig.root.Find("hips").worldToLocalMatrix });
            Assert.That(other.ReferenceKey, Is.Not.SameAs(rig.merger.ReferenceKey), "another rig's bind poses");

            // The same look again shares (the part arrays may be copies: compared by content).
            var copy = parts[0];
            copy.boneNames = (string[])copy.boneNames.Clone();
            MergedMeshCache.Describe(rig.merger.ReferenceKey, new[] { copy }, Shape, Face, Live, null);
            var entry = MergedMeshCache.TryShare();
            Assert.That(entry, Is.Not.Null);
            MergedMeshCache.Release(kept);
            MergedMeshCache.Release(kept);
        }

        private static void AssertSameMesh(Mesh a, Mesh b)
        {
            Assert.That(a.vertices, Is.EqualTo(b.vertices));
            Assert.That(a.normals, Is.EqualTo(b.normals));
            Assert.That(a.tangents, Is.EqualTo(b.tangents));
            Assert.That(a.triangles, Is.EqualTo(b.triangles));
            Assert.That(a.subMeshCount, Is.EqualTo(b.subMeshCount));
            Assert.That(a.bindposes, Is.EqualTo(b.bindposes));
            var wa = a.boneWeights;
            var wb = b.boneWeights;
            Assert.That(wa.Length, Is.EqualTo(wb.Length));
            for (int i = 0; i < wa.Length; i++) Assert.That(wa[i], Is.EqualTo(wb[i]));
            Assert.That(a.blendShapeCount, Is.EqualTo(b.blendShapeCount));
            for (int s = 0; s < a.blendShapeCount; s++)
            {
                Assert.That(a.GetBlendShapeName(s), Is.EqualTo(b.GetBlendShapeName(s)));
                int n = a.vertexCount;
                var dva = new Vector3[n]; var dna = new Vector3[n]; var dta = new Vector3[n];
                var dvb = new Vector3[n]; var dnb = new Vector3[n]; var dtb = new Vector3[n];
                a.GetBlendShapeFrameVertices(s, 0, dva, dna, dta);
                b.GetBlendShapeFrameVertices(s, 0, dvb, dnb, dtb);
                Assert.That(dva, Is.EqualTo(dvb));
                Assert.That(dna, Is.EqualTo(dnb));
            }
        }
    }
}
