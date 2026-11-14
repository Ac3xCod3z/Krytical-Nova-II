using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Townsfolk.Tests
{
    /// <summary>
    /// The owner's 14 Nov 2026 notes on item 18: far cards much farther, five distance bands with lighter meshes, life over the whole of
    /// Sky Concord (not only by the Sky Lift arrival), and no weapons on townsfolk or vendors.
    /// </summary>
    public sealed class TownsfolkBandsTests
    {
        readonly List<Object> m_Made = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in m_Made) if (o != null) Object.DestroyImmediate(o);
            m_Made.Clear();
        }

        static TownsfolkSettings Settings()
        {
            var s = AssetDatabase.LoadAssetAtPath<TownsfolkSettings>(TownsfolkPaths.SettingsAsset);
            Assert.IsNotNull(s, TownsfolkPaths.SettingsAsset);
            return s;
        }

        [Test]
        public void TheFiveBandsFollowTheTierEdges()
        {
            var e = new TownsfolkBands.Edges(25f, 55f, 85f, 220f);
            Assert.AreEqual(TownsfolkBands.Close, TownsfolkBands.BandOf(10f, e, 255, 2f));
            Assert.AreEqual(TownsfolkBands.Medium, TownsfolkBands.BandOf(40f, e, 255, 2f));
            Assert.AreEqual(TownsfolkBands.Far, TownsfolkBands.BandOf(70f, e, 255, 2f));
            Assert.AreEqual(TownsfolkBands.Card, TownsfolkBands.BandOf(150f, e, 255, 2f));
            Assert.AreEqual(TownsfolkBands.Hidden, TownsfolkBands.BandOf(400f, e, 255, 2f), "too far: not drawn at all");
            // no flicker on a line: a band is kept until its edge plus the hysteresis, both ways
            Assert.AreEqual(TownsfolkBands.Far, TownsfolkBands.BandOf(86.5f, e, TownsfolkBands.Far, 2f), "just past FAR: still a body");
            Assert.AreEqual(TownsfolkBands.Card, TownsfolkBands.BandOf(87.5f, e, TownsfolkBands.Far, 2f));
            Assert.AreEqual(TownsfolkBands.Card, TownsfolkBands.BandOf(84f, e, TownsfolkBands.Card, 2f), "just inside FAR: still a card");
            Assert.AreEqual(TownsfolkBands.Far, TownsfolkBands.BandOf(82f, e, TownsfolkBands.Card, 2f));
        }

        [Test]
        public void CardsBeginFarOutOnEveryTier()
        {
            TownsfolkSettings s = Settings();
            for (int tier = 0; tier < 4; tier++)
            {
                var e = TownsfolkBands.Edges.Of(s, tier);
                Assert.Less(e.Close, e.Medium, $"tier {tier}");
                Assert.Less(e.Medium, e.Far, $"tier {tier}");
                Assert.Less(e.Far, e.Cards, $"tier {tier}");
                // owner: the card he saw was "way too close"; Low may bring them in, never so close a card reads as a flat figure
                Assert.GreaterOrEqual(e.Far, 45f, $"tier {tier}: cards only far out");
                if (tier > 0) Assert.Greater(e.Far, TownsfolkBands.Edges.Of(s, tier - 1).Far, "a higher tier pushes the cards out");
            }
            Assert.GreaterOrEqual(TownsfolkBands.Edges.Of(s, 2).Far, 85f, "High: full bodies to 85 m (was 45)");
        }

        [Test]
        public void TheLodGroupSwitchesAtTheBandEdgeInMetres()
        {
            const float fov = 60f;
            foreach (float metres in new[] { 25f, 55f, 85f })
            {
                float h = TownsfolkBands.ScreenHeight(metres, fov, 1f);
                // a 2 m body at that distance fills exactly this share of a 60 degree view
                float share = TownsfolkBands.GroupSize / (2f * metres * Mathf.Tan(30f * Mathf.Deg2Rad));
                Assert.AreEqual(share, h, 1e-4f);
                Assert.Greater(h, TownsfolkBands.ScreenHeight(metres + 10f, fov, 1f), "farther switches at a smaller height");
            }
            Assert.AreEqual(2f * TownsfolkBands.ScreenHeight(55f, fov, 1f), TownsfolkBands.ScreenHeight(55f, fov, 2f), 1e-4f, "the LOD bias moves the threshold so the edge stays in metres");
        }

        /// <summary>A bumpy grid in two submeshes with UVs over several swatch cells and bone weights: a stand-in for a merged look.</summary>
        Mesh Grid(int n)
        {
            var mesh = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var w = new List<BoneWeight>();
            for (int z = 0; z <= n; z++)
                for (int x = 0; x <= n; x++)
                {
                    v.Add(new Vector3(x / (float)n, 2f * z / n, 0.05f * Mathf.Sin(x * 0.7f) * Mathf.Cos(z * 0.5f)));
                    uv.Add(new Vector2(x < n / 2 ? 0.1f : 0.6f, z < n / 2 ? 0.1f : 0.6f));
                    w.Add(new BoneWeight { boneIndex0 = z * 2 / (n + 1), weight0 = 1f });
                }
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.boneWeights = w.ToArray();
            mesh.bindposes = new[] { Matrix4x4.identity, Matrix4x4.identity };
            var a = new List<int>();
            var b = new List<int>();
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    int i = z * (n + 1) + x;
                    List<int> t = x < n / 2 ? a : b;
                    t.AddRange(new[] { i, i + n + 1, i + 1, i + 1, i + n + 1, i + n + 2 });
                }
            mesh.subMeshCount = 2;
            mesh.SetTriangles(a, 0);
            mesh.SetTriangles(b, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            m_Made.Add(mesh);
            return mesh;
        }

        [Test]
        public void LighterMeshesKeepAboutTheirShareOfTriangles()
        {
            Mesh full = Grid(120);
            int whole = TownsfolkMeshLod.Triangles(full);
            Mesh lod1 = TownsfolkMeshLod.Make(full, 0.5f, "LOD1");
            Mesh lod2 = TownsfolkMeshLod.Make(full, 0.25f, "LOD2");
            m_Made.Add(lod1);
            m_Made.Add(lod2);
            Assert.IsNotNull(lod1);
            Assert.IsNotNull(lod2);
            float s1 = TownsfolkMeshLod.Triangles(lod1) / (float)whole, s2 = TownsfolkMeshLod.Triangles(lod2) / (float)whole;
            Assert.That(s1, Is.InRange(0.3f, 0.55f), "LOD1 about half");
            Assert.That(s2, Is.InRange(0.1f, 0.3f), "LOD2 about a quarter");
            Assert.AreEqual(full.subMeshCount, lod1.subMeshCount, "same materials");
            Assert.AreEqual(lod1.vertexCount, lod1.boneWeights.Length, "every kept vertex skins");
            Assert.AreEqual(full.bindposes.Length, lod1.bindposes.Length, "the same bones");
        }

        [Test]
        public void LifeIsSpreadOverTheWholeDeck()
        {
            // a 200 m square deck (two triangles) round the middle, the arrival 60 m south of it like Sky Concord's
            Vector3[] verts = { new(-100f, 0f, -100f), new(100f, 0f, -100f), new(100f, 0f, 100f), new(-100f, 0f, 100f) };
            int[] tris = { 0, 2, 1, 0, 3, 2 };
            var points = new List<Vector3>();
            TownSpread.Sample(verts, tris, Vector3.zero, 160f, 26f, 9f, 240, 0x5C0, points);
            Assert.Greater(points.Count, 150, "the deck is filled");
            int[] quarters = new int[4];
            foreach (Vector3 p in points) quarters[(p.x < 0f ? 0 : 1) + (p.z < 0f ? 0 : 2)]++;
            foreach (int q in quarters) Assert.Greater(q, points.Count / 6, "every quarter of the district has life");
            for (int i = 0; i < points.Count; i++)
                for (int j = i + 1; j < points.Count; j++)
                    Assert.GreaterOrEqual((points[i] - points[j]).magnitude, 9f - 1e-3f, "spread, never bunched");
            var again = new List<Vector3>();
            TownSpread.Sample(verts, tris, Vector3.zero, 160f, 26f, 9f, 240, 0x5C0, again);
            CollectionAssert.AreEqual(points, again, "the same seed, the same district");

            // the arrival keeps no more than its share of the crowd
            float3 arrival = new(0f, 0f, -56f);
            Assert.IsTrue(TownSpread.MayStepOutAt(new float3(0f, 0f, -50f), true, arrival, 28f, 2, 48, 0.15f));
            Assert.IsFalse(TownSpread.MayStepOutAt(new float3(0f, 0f, -50f), true, arrival, 28f, 8, 48, 0.15f), "full: nobody steps out there");
            Assert.IsTrue(TownSpread.MayStepOutAt(new float3(0f, 0f, 60f), true, arrival, 28f, 8, 48, 0.15f), "elsewhere is always fine");
            Assert.IsTrue(TownSpread.ArrivalCrowded(9, 48, 0.15f));
            Assert.IsFalse(TownSpread.ArrivalCrowded(5, 48, 0.15f));
        }

        [Test]
        public void TownsfolkAndVendorsWearNoWeapons()
        {
            TownsfolkSettings s = Settings();
            CollectionAssert.Contains(s.noWeaponRegions, BodyRegion.AttachmentBack, "back mounts (swords, rifles, quivers)");
            CollectionAssert.Contains(s.noWeaponRegions, BodyRegion.AttachmentHipsLeft, "holsters and sheaths");
            CollectionAssert.Contains(s.noWeaponRegions, BodyRegion.AttachmentHipsRight);
            var go = new GameObject("Look");
            m_Made.Add(go);
            var appearance = go.AddComponent<CharacterAppearance>();
            appearance.ExcludeRegions(s.noWeaponRegions);
            foreach (BodyRegion r in s.noWeaponRegions) Assert.IsTrue(appearance.Excludes(r), r.ToString());
            Assert.IsFalse(appearance.Excludes(BodyRegion.Torso), "the clothes stay");
            Assert.IsFalse(appearance.Excludes(BodyRegion.AttachmentHead), "hats stay");
        }
    }
}
