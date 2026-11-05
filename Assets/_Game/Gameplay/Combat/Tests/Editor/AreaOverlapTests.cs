using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Combat.Tests
{
    /// <summary>Owner, 5 Nov 2026: an area skill hits every target in range; a crowd larger than the buffer is never cut.</summary>
    public sealed class AreaOverlapTests
    {
        // Far from anything a scene open in the Editor may hold.
        private static readonly Vector3 Centre = new Vector3(0f, -10000f, 0f);
        private readonly List<GameObject> made = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in made) Object.DestroyImmediate(go);
            made.Clear();
        }

        [Test]
        public void FortyBodiesWithTwoCollidersEach_AllAreFound_ThroughASmallBuffer()
        {
            for (int i = 0; i < 40; i++)
            {
                var body = new GameObject("Body " + i);
                made.Add(body);
                body.transform.position = Centre + Quaternion.Euler(0f, i * 9f, 0f) * Vector3.forward * (1f + i * 0.05f);
                for (int c = 0; c < 2; c++)
                {
                    var part = new GameObject("Part " + c, typeof(BoxCollider));
                    part.transform.SetParent(body.transform, false);
                    part.transform.localPosition = Vector3.up * c;
                    part.GetComponent<BoxCollider>().size = Vector3.one * 0.3f;
                }
            }
            Physics.SyncTransforms();

            var buffer = new Collider[8]; // smaller than the crowd: it must grow, never cut
            int count = AreaOverlap.All(Centre, 6f, ~0, QueryTriggerInteraction.Collide, ref buffer);

            Assert.AreEqual(80, count, "every collider of every body");
            var bodies = new HashSet<Transform>();
            for (int i = 0; i < count; i++) bodies.Add(buffer[i].transform.parent);
            Assert.AreEqual(40, bodies.Count, "every body once");
        }

        [Test]
        public void AnEmptySphere_FindsNothing()
        {
            Collider[] buffer = null;
            Assert.AreEqual(0, AreaOverlap.All(Centre + Vector3.down * 500f, 2f, ~0, QueryTriggerInteraction.Collide, ref buffer));
            Assert.IsNotNull(buffer);
        }
    }
}
