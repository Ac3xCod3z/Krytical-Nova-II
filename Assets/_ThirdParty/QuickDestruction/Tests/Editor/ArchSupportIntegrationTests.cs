using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace RipVertices.QuickDestruction.Tests
{
    public sealed class ArchSupportIntegrationTests
    {
        [TestCase(0, false)]
        [TestCase(1, false)]
        [TestCase(0, true)]
        [TestCase(1, true)]
        public void DemoArchFallsOnSeveredSideBeforeOtherColumnIsHit(
            int side, bool useAuthoredThresholds)
        {
            const string path = "Assets/QuickDestruction/Demo/Demo_Profiles/" +
                "pillars_QD_Profile_UniformConcrete_41616c0f/pillars_Destructible.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null);
            var instance = Object.Instantiate(prefab);
            var root = instance.GetComponent<DestructibleRoot>();
            var profile = Object.Instantiate(root.Profile);
            var groundObject = new GameObject("Arch Regression Ground");
            try
            {
                root.Profile = profile;
                profile.EnforceActiveBodyBudget = false;
                profile.ActivateAllOnFirstBreak = false;
                profile.UseDefaultHitParticles = false;
                if (!useAuthoredThresholds)
                {
                    profile.RequiredSupportRatio = 0f;
                    profile.MaxUnsupportedSpan = 0f;
                }

                root.FracturedRoot.SetActive(true);
                Physics.SyncTransforms();
                var bounds = new Dictionary<int, Bounds>();
                foreach (FragmentRecord fragment in root.Fragments)
                {
                    Bounds combined = fragment.Renderer.bounds;
                    if (bounds.TryGetValue(fragment.StructuralGroup, out Bounds previous))
                        combined.Encapsulate(previous);
                    bounds[fragment.StructuralGroup] = combined;
                }
                var groundedGroups = new HashSet<int>(root.Fragments
                    .Where(f => f.GroundAnchor).Select(f => f.StructuralGroup));
                int arch = bounds.Keys.Single(g => !groundedGroups.Contains(g));
                int[] columns = groundedGroups.OrderBy(g => bounds[g].center.x).ToArray();
                Assert.That(columns.Length, Is.EqualTo(2));
                Bounds columnBounds = bounds[columns[side]];
                var ground = groundObject.AddComponent<BoxCollider>();
                ground.size = new Vector3(100f, 1f, 100f);
                groundObject.transform.position = new Vector3(columnBounds.center.x,
                    columnBounds.min.y - 0.5f, columnBounds.center.z);
                root.FracturedRoot.SetActive(false);
                Physics.SyncTransforms();

                void Hit(int group, Vector3 point)
                {
                    // The demo's default click radius, with zero impulse to isolate support.
                    root.BreakNow(new BreakContext(point, Vector3.forward,
                        0.75f, 0f, 17, Vector3.forward, 0f, group));
                }

                for (int run = 0; run < 2; run++)
                {
                    if (run > 0)
                    {
                        root.ResetDestructible(ResetMode.Silent);
                        Physics.SyncTransforms();
                    }
                    Vector3 crown = bounds[arch].center;
                    crown.y = bounds[arch].max.y - 0.3f;
                    Hit(arch, crown);
                    FragmentRecord[] remainingArch = root.Fragments.Where(f =>
                        f.StructuralGroup == arch && !f.Activated && !f.Detached).ToArray();
                    FragmentRecord[] unsupportedHalf = remainingArch.Where(f =>
                        (f.Renderer.bounds.center.x < crown.x) == (side == 0)).ToArray();
                    FragmentRecord[] supportedHalf = remainingArch.Except(unsupportedHalf).ToArray();
                    Assert.That(unsupportedHalf, Is.Not.Empty);
                    Assert.That(supportedHalf, Is.Not.Empty);

                    Hit(columns[side], columnBounds.center);

                    foreach (FragmentRecord fragment in unsupportedHalf)
                    {
                        Assert.That(fragment.Activated, Is.True,
                            $"Run {run}: {fragment.Transform.name} must fall with its own column.");
                        Assert.That(fragment.Body.isKinematic, Is.False);
                        Assert.That(fragment.Body.useGravity, Is.True);
                    }
                    Assert.That(supportedHalf.All(f => !f.Activated && !f.Detached), Is.True,
                        "The opposite arch half still stands on its untouched column.");
                    Assert.That(root.Fragments.Where(f => f.StructuralGroup == columns[1 - side])
                        .All(f => !f.Activated && !f.Detached), Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(groundObject);
            }
        }
    }
}
