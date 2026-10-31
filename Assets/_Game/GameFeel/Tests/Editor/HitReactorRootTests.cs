using System.Reflection;
using KryticalNova.Core;
using KryticalNova.GameFeel;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The hit reaction squashes and leans the body's visual, never the root of a body something else moves (owner's DOTS retries 3-4,
    /// 30-31 Oct 2026: on an entity-netcode body it took the root, put its first rotation back every frame, and the body never turned).
    /// </summary>
    public sealed class HitReactorRootTests
    {
        static void Call(Object target, string method) =>
            target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(target, null);

        [Test]
        public void OnAnEntityNetcodeBody_TheRootKeepsTheTurnItIsGiven()
        {
            var body = new GameObject("view body");
            try
            {
                body.AddComponent<BodyRoleTag>().Assign(NetRoleKind.ClientView, PlayerKey.None, 0UL, true);
                var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                visual.name = "Visuals";
                visual.transform.SetParent(body.transform, false);
                body.transform.rotation = Quaternion.Euler(0f, 20f, 0f);   // where the body faced when the reactor came

                var reactor = HitReactor.For(body.transform);
                Call(reactor, "Awake");
                body.transform.rotation = Quaternion.Euler(0f, 225f, 0f);  // the follow turns it for a new walk
                Call(reactor, "LateUpdate");

                Assert.AreEqual(225f, body.transform.eulerAngles.y, 0.01f, "the root keeps its new heading");
                Assert.AreEqual(Vector3.one, body.transform.localScale, "and its scale");
            }
            finally { Object.DestroyImmediate(body); }
        }
    }
}
