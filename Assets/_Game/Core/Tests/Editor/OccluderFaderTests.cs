using KryticalNova.Rendering;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// What a faded prop may cost is bounded. One wall of the North Gate, a piece of the town's combined static
    /// mesh, was listed with that whole mesh's 208 parts, and the pass that draws "the faded props whole" drew the
    /// town three times over (11 Oct 2026: 5 ms of the graphics card became 19).
    /// </summary>
    public sealed class OccluderFaderTests
    {
        [Test]
        public void APieceOfACombinedMeshAnswersForItsOwnPartsOnly()
        {
            var root = new GameObject("Batch");
            try
            {
                var pieces = new GameObject[6];
                for (int i = 0; i < pieces.Length; i++)
                {
                    pieces[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    pieces[i].transform.SetParent(root.transform, false);
                    pieces[i].transform.localPosition = new Vector3(i * 2f, 0f, 0f);
                }
                StaticBatchingUtility.Combine(pieces, root);
                var piece = pieces[2].GetComponent<MeshRenderer>();
                var mesh = pieces[2].GetComponent<MeshFilter>().sharedMesh;
                if (!piece.isPartOfStaticBatch) Assert.Inconclusive("static batching did not combine the pieces here");

                Assert.Greater(mesh.subMeshCount, 1, "the piece's mesh is the batch's: it holds every piece's parts");
                Assert.AreEqual(1, OccluderFader.OwnSubmeshes(piece), "one material, one part of its own, whatever the combined mesh holds");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void AnOrdinaryPropAnswersForNoMorePartsThanItsMeshHas()
        {
            GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                var renderer = prop.GetComponent<MeshRenderer>();
                Assert.AreEqual(1, OccluderFader.OwnSubmeshes(renderer));
                // Three materials on a mesh of one part: one part is drawn.
                renderer.sharedMaterials = new[] { renderer.sharedMaterial, renderer.sharedMaterial, renderer.sharedMaterial };
                Assert.AreEqual(1, OccluderFader.OwnSubmeshes(renderer));
                Assert.AreEqual(0, OccluderFader.OwnSubmeshes(null));
            }
            finally
            {
                Object.DestroyImmediate(prop);
            }
        }

        [Test]
        public void NoMoreThanAFewDozenPiecesAreEverFadedAtOnce() =>
            Assert.LessOrEqual(OccluderFader.MostFadedRenderers, 64, "the limit is what keeps a fade from becoming a second drawing of the world");
    }
}
