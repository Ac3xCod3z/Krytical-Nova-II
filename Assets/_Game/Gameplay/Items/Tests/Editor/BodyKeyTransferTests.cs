using System.Collections.Generic;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Items.Tests
{
    /// <summary>Worn parts without the body keys take them from the bare body under them (22 Oct 2026).</summary>
    public sealed class BodyKeyTransferTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void CleanUp()
        {
            foreach (var o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        // A square metre of "body" at z = 0 whose kn_bust key pushes it 4 cm forward, more at the top than the bottom.
        private SidekickPartRef Body()
        {
            var mesh = new Mesh { name = "Body" };
            mesh.SetVertices(new List<Vector3> { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0) });
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.AddBlendShapeFrame("10TORSBlends.kn_bust", 100f,
                new[] { new Vector3(0, 0, 0.02f), new Vector3(0, 0, 0.02f), new Vector3(0, 0, 0.06f), new Vector3(0, 0, 0.06f) }, null, null);
            made.Add(mesh);
            return new SidekickPartRef { region = BodyRegion.Torso, mesh = mesh, boneNames = new[] { "root" } };
        }

        // A gear vertex floating 3 cm in front of the body's middle.
        private SidekickPartRef Worn(BodyRegion region, Vector3[] ownKey = null, string ownKeyName = "02HAIRBlends.defaultHeavy")
        {
            var mesh = new Mesh { name = "Worn" };
            mesh.SetVertices(new List<Vector3> { new Vector3(0.5f, 0.5f, 0.03f), new Vector3(0.6f, 0.5f, 0.03f), new Vector3(0.5f, 0.6f, 0.03f) });
            mesh.SetTriangles(new[] { 0, 1, 2 }, 0);
            if (ownKey != null) mesh.AddBlendShapeFrame(ownKeyName, 100f, ownKey, null, null);
            made.Add(mesh);
            return new SidekickPartRef { region = region, mesh = mesh, boneNames = new[] { "root" } };
        }

        private static List<Vector3> Rest(SidekickPartRef part)
        {
            var list = new List<Vector3>();
            part.mesh.GetVertices(list);
            return list;
        }

        [Test]
        public void GearTakesTheSliderOfTheBodyUnderIt()
        {
            var source = BodyKeyTransfer.SourceFor(new[] { Body() });
            var gear = Worn(BodyRegion.Torso);
            var positions = Rest(gear);
            BodyKeyTransfer.Apply(source, gear, new BodyShapeWeights { bust = 50f }, positions);
            // Halfway up the body the key moves 4 cm; at weight 50 that is 2 cm.
            Assert.That(positions[0].z - 0.03f, Is.EqualTo(0.02f).Within(1e-4f));
            Assert.That(positions[2].z - 0.03f, Is.EqualTo(0.022f).Within(1e-4f)); // 0.6 of the way up: 4.4 cm, halved
        }

        [Test]
        public void AttachmentsAndTheBodyItselfTakeNothing()
        {
            var body = Body();
            var source = BodyKeyTransfer.SourceFor(new[] { body });
            var attachment = Worn(BodyRegion.AttachmentBack);
            var positions = Rest(attachment);
            BodyKeyTransfer.Apply(source, attachment, new BodyShapeWeights { bust = 100f }, positions);
            Assert.That(positions[0].z, Is.EqualTo(0.03f).Within(1e-6f));

            var own = Rest(body);
            BodyKeyTransfer.Apply(source, body, new BodyShapeWeights { bust = 100f }, own);
            Assert.That(own[0].z, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void APartWithAKeyOfItsOwnKeepsIt()
        {
            var bodyMesh = Body();
            bodyMesh.mesh.AddBlendShapeFrame("10TORSBlends.defaultHeavy", 100f,
                new[] { Vector3.up * 0.05f, Vector3.up * 0.05f, Vector3.up * 0.05f, Vector3.up * 0.05f }, null, null);
            var source = BodyKeyTransfer.SourceFor(new[] { bodyMesh });

            var emptyHair = Worn(BodyRegion.Hair, new Vector3[3]);
            Assert.That(BodyKeyTransfer.KeepsOwnKey(source, emptyHair, "02HAIRBlends.defaultHeavy"), Is.False);
            var positions = Rest(emptyHair);
            BodyKeyTransfer.Apply(source, emptyHair, new BodyShapeWeights { heavy = 100f }, positions);
            Assert.That(positions[0].y - 0.5f, Is.EqualTo(0.05f).Within(1e-4f));

            var ownHair = Worn(BodyRegion.Hair, new[] { Vector3.right * 0.03f, Vector3.right * 0.03f, Vector3.right * 0.03f });
            Assert.That(BodyKeyTransfer.KeepsOwnKey(source, ownHair, "02HAIRBlends.defaultHeavy"), Is.True);
            positions = Rest(ownHair);
            BodyKeyTransfer.Apply(source, ownHair, new BodyShapeWeights { heavy = 100f }, positions);
            Assert.That(positions[0].y, Is.EqualTo(0.5f).Within(1e-6f));

            // Any real key of its own is kept, however small next to the body's (the fixed hair copies bake theirs).
            var weakHair = Worn(BodyRegion.Hair, new[] { Vector3.up * 0.01f, Vector3.up * 0.01f, Vector3.up * 0.01f });
            Assert.That(BodyKeyTransfer.KeepsOwnKey(source, weakHair, "02HAIRBlends.defaultHeavy"), Is.True);

            // Gear takes only the three sliders, not Synty's four.
            var gear = Worn(BodyRegion.Torso);
            positions = Rest(gear);
            BodyKeyTransfer.Apply(source, gear, new BodyShapeWeights { heavy = 100f }, positions);
            Assert.That(positions[0].y, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void HairKeepsItsOwnFeminineShape()
        {
            Assert.That(System.Array.IndexOf(BodyKeyTransfer.KeysFor(BodyRegion.Hair), "masculineFeminine"), Is.LessThan(0));
            Assert.That(BodyKeyTransfer.KeysFor(BodyRegion.AttachmentShoulderLeft), Is.Null);
        }

        [Test]
        public void TheNearestPointIsFoundOnTheSurface()
        {
            Vector3 a = Vector3.zero, b = Vector3.right, c = Vector3.up;
            Vector3 w = BodyKeyTransfer.ClosestOnTriangle(new Vector3(0.25f, 0.25f, 1f), a, b, c);
            Assert.That(w.x, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(w.y, Is.EqualTo(0.25f).Within(1e-5f));
            w = BodyKeyTransfer.ClosestOnTriangle(new Vector3(2f, -1f, 0f), a, b, c);
            Assert.That(w.y, Is.EqualTo(1f).Within(1e-5f));
        }
    }
}
