using KryticalNova.UI;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.UI.Tests
{
    /// <summary>
    /// Floating combat text is shown only at a real point over a body (owner bug, 14 Nov 2026: "MISS / EVADE" and damage
    /// numbers stuck on the portrait frame or at screen 0,0). No camera, a point that is not a number, or a point behind
    /// the camera means not shown, never a fallback corner.
    /// </summary>
    public sealed class CombatFeedbackPlacementTests
    {
        Camera camera;

        [SetUp]
        public void MakeCamera()
        {
            camera = new GameObject("FeedbackTestCamera").AddComponent<Camera>();
            camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            camera.pixelRect = new Rect(0f, 0f, 1920f, 1080f);
        }

        [TearDown]
        public void DropCamera() => Object.DestroyImmediate(camera.gameObject);

        [Test]
        public void ABodyInFront_IsPlacedOverItsPoint_NotAtTheCorner()
        {
            Assert.IsTrue(CombatFeedbackManager.TryProject(camera, new Vector3(0f, 0f, 10f), 1080f, out Vector2 at));
            Assert.That(at.x, Is.EqualTo(camera.pixelRect.center.x).Within(1f), "the point straight ahead is the screen's middle");
            Assert.That(at.y, Is.EqualTo(1080f - camera.pixelRect.center.y).Within(1f));
            Assert.Greater(at.magnitude, 100f, "never the top-left corner (the portrait frame)");
        }

        [Test]
        public void NoCamera_ADestroyedOrSwitchedOffCamera_ShowsNothing()
        {
            Assert.IsFalse(CombatFeedbackManager.TryProject(null, new Vector3(0f, 0f, 10f), 1080f, out _));
            camera.enabled = false;
            Assert.IsFalse(CombatFeedbackManager.IsUsable(camera));
            Assert.IsFalse(CombatFeedbackManager.TryProject(camera, new Vector3(0f, 0f, 10f), 1080f, out _));
        }

        [Test]
        public void APointThatIsNotANumber_ShowsNothing()
        {
            Assert.IsFalse(CombatFeedbackManager.TryProject(camera, new Vector3(float.NaN, 0f, 10f), 1080f, out _));
            Assert.IsFalse(CombatFeedbackManager.TryProject(camera, new Vector3(0f, float.PositiveInfinity, 10f), 1080f, out _));
        }

        [Test]
        public void APointBehindTheCamera_ShowsNothing()
        {
            Assert.IsFalse(CombatFeedbackManager.TryProject(camera, new Vector3(0f, 0f, -10f), 1080f, out _));
        }
    }
}
