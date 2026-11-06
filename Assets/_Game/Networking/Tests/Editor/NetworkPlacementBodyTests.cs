using NUnit.Framework;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Networking.Tests
{
    /// <summary>M3a-2b step 7: a placement of a body the entity netcode moves goes to its ghost; every other body is placed as before.</summary>
    public class NetworkPlacementBodyTests
    {
        sealed class RecordingMover : IBodyMover
        {
            public int Teleports;
            public Vector3 At;
            public void MoveTo(Vector3 point, bool run, bool payRun) { }
            public void Stop() { }
            public void Teleport(Vector3 position, Quaternion rotation) { Teleports++; At = position; }
            public bool JumpTo(Vector3 landing, out string reason) { reason = null; return false; }
            public bool Airborne => false;
            public bool Busy => false;
        }

        [Test]
        public void ABodyWithAMover_IsPlacedThroughIt()
        {
            var go = new GameObject("Body");
            try
            {
                var tag = go.AddComponent<BodyRoleTag>();
                tag.Assign(NetRoleKind.ServerBody, new PlayerKey(1), 1, false);
                var mover = new RecordingMover();
                tag.Mover = mover;
                NetworkPlacement.Place(go.transform, new Vector3(4f, 0f, 5f), Quaternion.identity);
                Assert.AreEqual(1, mover.Teleports);
                Assert.AreEqual(new Vector3(4f, 0f, 5f), mover.At);
                Assert.AreEqual(Vector3.zero, go.transform.position, "the mover places the body; Place itself leaves it alone");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void ABodyWithoutAMover_IsPlacedAsBefore()
        {
            var go = new GameObject("Avatar");
            try
            {
                go.AddComponent<BodyRoleTag>(); // tagged but no mover yet (a body still being built)
                NetworkPlacement.Place(go.transform, new Vector3(1f, 2f, 3f), Quaternion.identity);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), go.transform.position);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
