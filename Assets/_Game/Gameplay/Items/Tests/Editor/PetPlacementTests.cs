using KryticalNova.Items.Visuals;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Where a pet hovers beside its owner: the same clear air at every size, its underside where it was, round its
    /// owner on a turn and never through them.
    /// </summary>
    public sealed class PetPlacementTests
    {
        private const float Millimetre = 0.001f;
        private const float NormalizedSize = 0.55f;
        private const float ClearDistance = 0.25f;
        private const float FollowSmoothTime = 0.35f;
        private const float TurnSpeed = 6f;
        private const float Frame = 1f / 60f;

        private static readonly Vector3 Authored = new Vector3(0.45f, 0.75f, -0.15f);
        private static readonly float[] Stages = { 1f, 1.08f, 1.16f, 1.24f, 1.35f, 2f };

        // The four stand-in models as they measure at scale 1 (centre, size): a wide flat one, a tall one whose
        // pivot is low and forward of its middle, a round one and a wide shallow one.
        private static readonly Bounds[] Models =
        {
            new Bounds(Vector3.zero, new Vector3(1.246f, 0.457f, 1.166f)),
            new Bounds(new Vector3(0f, 0.421f, -0.197f), new Vector3(1.592f, 2.179f, 1.264f)),
            new Bounds(Vector3.zero, new Vector3(0.696f, 0.552f, 0.605f)),
            new Bounds(Vector3.zero, new Vector3(1.527f, 1.075f, 0.990f)),
        };

        private static float BaseScale(Bounds unit) =>
            NormalizedSize / Mathf.Max(unit.size.x, Mathf.Max(unit.size.y, unit.size.z));

        // ------------------------------------------------------------------ where it hovers

        [Test]
        public void NearestEdge_KeepsTheSameDistance_AtEveryStage()
        {
            foreach (Bounds unit in Models)
            {
                float baseScale = BaseScale(unit);
                foreach (float stage in Stages)
                {
                    float scale = baseScale * stage;
                    Vector3 pivot = PetPlacement.HoverPoint(Authored, unit, baseScale, scale, ClearDistance);
                    Assert.AreEqual(ClearDistance, PetPlacement.NearestDistance(pivot, unit, scale), Millimetre, $"size {unit.size} stage x{stage}");
                }
            }
        }

        [Test]
        public void Underside_StaysAtItsBaseHeight_AtEveryStage()
        {
            foreach (Bounds unit in Models)
            {
                float baseScale = BaseScale(unit);
                float atBase = PetPlacement.Underside(Authored, unit, baseScale);
                foreach (float stage in Stages)
                {
                    float scale = baseScale * stage;
                    Vector3 pivot = PetPlacement.HoverPoint(Authored, unit, baseScale, scale, ClearDistance);
                    Assert.AreEqual(atBase, PetPlacement.Underside(pivot, unit, scale), Millimetre, $"size {unit.size} stage x{stage}");
                }
            }
        }

        [Test]
        public void BaseSize_SitsWhereItWasAuthored_WhenNoDistanceIsAsked()
        {
            foreach (Bounds unit in Models)
            {
                float baseScale = BaseScale(unit);
                Vector3 pivot = PetPlacement.HoverPoint(Authored, unit, baseScale, baseScale, -1f);
                Assert.Less((pivot - Authored).magnitude, Millimetre, $"size {unit.size}");
            }
        }

        [Test]
        public void TheAuthoredDistance_IsKeptAsThePetGrows_WhenNoDistanceIsAsked()
        {
            foreach (Bounds unit in Models)
            {
                float baseScale = BaseScale(unit);
                float authored = PetPlacement.NearestDistance(Authored, unit, baseScale);
                Vector3 pivot = PetPlacement.HoverPoint(Authored, unit, baseScale, baseScale * 1.35f, -1f);
                Assert.AreEqual(authored, PetPlacement.NearestDistance(pivot, unit, baseScale * 1.35f), Millimetre, $"size {unit.size}");
            }
        }

        [Test]
        public void AGrownPet_MovesOutAndUp_NeverInOrDown()
        {
            foreach (Bounds unit in Models)
            {
                float baseScale = BaseScale(unit);
                float grown = baseScale * 1.35f;
                Vector3 small = PetPlacement.HoverPoint(Authored, unit, baseScale, baseScale, ClearDistance);
                Vector3 big = PetPlacement.HoverPoint(Authored, unit, baseScale, grown, ClearDistance);
                Vector3 smallCentre = small + unit.center * baseScale;
                Vector3 bigCentre = big + unit.center * grown;
                Assert.Greater(bigCentre.x, smallCentre.x, "its middle moves away from the owner");
                Assert.Greater(bigCentre.y, smallCentre.y, "and up");
                Assert.GreaterOrEqual(bigCentre.x - unit.extents.x * grown, smallCentre.x - unit.extents.x * baseScale - Millimetre, "its near side does not come in");
            }
        }

        [Test]
        public void APetBehindItsOwner_KeepsItsDistanceBehind()
        {
            var behind = new Vector3(0f, 0.75f, -0.7f);
            foreach (Bounds unit in Models)
            {
                float baseScale = BaseScale(unit);
                float grown = baseScale * 1.35f;
                Vector3 pivot = PetPlacement.HoverPoint(behind, unit, baseScale, grown, ClearDistance);
                Assert.AreEqual(ClearDistance, PetPlacement.NearestDistance(pivot, unit, grown), Millimetre);
                Assert.AreEqual(behind.x, pivot.x, Millimetre, "it stays behind, not to one side");
                Assert.Less(pivot.z + unit.center.z * grown, 0f);
            }
        }

        [Test]
        public void APetOverItsOwnersHead_OnlyKeepsItsUnderside()
        {
            var above = new Vector3(0f, 1.4f, 0f);
            Bounds unit = Models[2];
            float baseScale = BaseScale(unit);
            float grown = baseScale * 1.35f;
            Vector3 pivot = PetPlacement.HoverPoint(above, unit, baseScale, grown, ClearDistance);
            Assert.AreEqual(above.x, pivot.x, Millimetre);
            Assert.AreEqual(above.z, pivot.z, Millimetre);
            Assert.AreEqual(PetPlacement.Underside(above, unit, baseScale), PetPlacement.Underside(pivot, unit, grown), Millimetre);
            Assert.AreEqual(0f, PetPlacement.NearestDistance(pivot, unit, grown), Millimetre, "nothing to keep clear of sideways");
        }

        // ------------------------------------------------------------------ measuring the model

        [Test]
        public void Measure_IsTheSame_WhicheverWayTheModelIsTurnedOrScaled()
        {
            GameObject model = BuildModel();
            try
            {
                Assert.IsTrue(PetPlacement.TryMeasure(model, out Bounds straight));
                Assert.AreEqual(2f, straight.size.x, Millimetre);
                Assert.AreEqual(0.5f, straight.size.y, Millimetre);
                Assert.AreEqual(1f, straight.size.z, Millimetre);
                Assert.Less((straight.center - new Vector3(0f, 0.3f, -0.2f)).magnitude, Millimetre);

                foreach (float yaw in new[] { 45f, 133f, 270f })
                {
                    model.transform.SetPositionAndRotation(new Vector3(12f, 3f, -7f), Quaternion.Euler(0f, yaw, 0f));
                    model.transform.localScale = Vector3.one * 0.3f;
                    Assert.IsTrue(PetPlacement.TryMeasure(model, out Bounds turned));
                    Assert.Less((turned.size - straight.size).magnitude, Millimetre, $"turned {yaw}");
                    Assert.Less((turned.center - straight.center).magnitude, Millimetre, $"turned {yaw}");
                }
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void Measure_LeavesOutEffects()
        {
            GameObject model = BuildModel();
            try
            {
                PetPlacement.TryMeasure(model, out Bounds body);
                var effect = new GameObject("Sparkles", typeof(ParticleSystem)) { hideFlags = HideFlags.HideAndDontSave };
                effect.transform.SetParent(model.transform, false);
                effect.transform.localPosition = new Vector3(5f, 5f, 5f);
                Assert.IsTrue(PetPlacement.TryMeasure(model, out Bounds withEffect));
                Assert.Less((withEffect.size - body.size).magnitude, Millimetre);
                Assert.Less((withEffect.center - body.center).magnitude, Millimetre);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        [Test]
        public void Measure_FindsNothing_InAModelWithNoMeshes()
        {
            var empty = new GameObject("Empty") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Assert.IsFalse(PetPlacement.TryMeasure(empty, out _));
                Assert.IsFalse(PetPlacement.TryMeasure(null, out _));
            }
            finally
            {
                Object.DestroyImmediate(empty);
            }
        }

        // A root with one mesh child: 2 x 0.5 x 1 metres, its middle 0.3 up and 0.2 back from the root's pivot.
        private static GameObject BuildModel()
        {
            var root = new GameObject("Model") { hideFlags = HideFlags.HideAndDontSave };
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.hideFlags = HideFlags.HideAndDontSave;
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.3f, -0.2f);
            body.transform.localScale = new Vector3(2f, 0.5f, 1f);
            return root;
        }

        // ------------------------------------------------------------------ keeping clear on the way

        [Test]
        public void FootprintDistance_AgreesWithNearestDistance_WhenThePetFacesAsItsOwnerDoes()
        {
            foreach (Bounds unit in Models)
            {
                float scale = BaseScale(unit) * 1.35f;
                Vector3 pivot = PetPlacement.HoverPoint(Authored, unit, BaseScale(unit), scale, ClearDistance);
                Assert.AreEqual(PetPlacement.NearestDistance(pivot, unit, scale), PetPlacement.FootprintDistance(pivot, Quaternion.identity, unit, scale), Millimetre);

                // Owner and pet both turned: the same distance.
                Quaternion turned = Quaternion.Euler(0f, 110f, 0f);
                Assert.AreEqual(ClearDistance, PetPlacement.FootprintDistance(turned * pivot, turned, unit, scale), Millimetre);
            }
        }

        [Test]
        public void ARestingPet_IsLeftAlone()
        {
            foreach (Bounds unit in Models)
            {
                float scale = BaseScale(unit) * 1.35f;
                Vector3 pivot = PetPlacement.HoverPoint(Authored, unit, BaseScale(unit), scale, ClearDistance);
                Vector3 velocity = Vector3.zero;
                Vector3 kept = PetPlacement.KeepClear(pivot, ref velocity, pivot, Quaternion.identity, unit, scale, ClearDistance - 0.002f, Vector3.back);
                Assert.AreEqual(pivot, kept);
                Assert.AreEqual(Vector3.zero, velocity);
            }
        }

        [Test]
        public void ATurnAbout_TakesThePetRoundItsOwner_NotThroughThem()
        {
            foreach (Bounds unit in Models)
                foreach (float stage in new[] { 1f, 1.35f })
                    AssertStaysClearAndSettles(unit, stage, 180f);
        }

        [Test]
        public void AQuarterTurn_EitherWay_StaysClearAndSettles()
        {
            foreach (Bounds unit in Models)
                foreach (float turn in new[] { 90f, -90f, 135f, -135f, 30f })
                    AssertStaysClearAndSettles(unit, 1.35f, turn);
        }

        [Test]
        public void ATurnAbout_GoesByTheOwnersBack()
        {
            Bounds unit = Models[2];
            float scale = BaseScale(unit) * 1.35f;
            Vector3 hover = PetPlacement.HoverPoint(Authored, unit, BaseScale(unit), scale, ClearDistance);
            Quaternion owner = Quaternion.Euler(0f, 180f, 0f);
            Vector3 ownerForward = owner * Vector3.forward;
            bool passedBehind = false, passedInFront = false;
            Simulate(unit, scale, hover, owner, 2.5f, (offset, rotation) =>
            {
                Vector3 centre = offset + rotation * (unit.center * scale);
                // Level with the owner, neither side: which way is it?
                Vector3 right = owner * Vector3.right;
                if (Mathf.Abs(Vector3.Dot(centre, right)) < 0.08f)
                {
                    if (Vector3.Dot(centre, ownerForward) < 0f) passedBehind = true;
                    else passedInFront = true;
                }
            });
            Assert.IsTrue(passedBehind, "it crosses behind the owner");
            Assert.IsFalse(passedInFront, "and never across their face");
        }

        [Test]
        public void AnOwnerShovedIntoItsPet_CarriesThePetAlong()
        {
            foreach (Bounds unit in Models)
            {
                float scale = BaseScale(unit) * 1.35f;
                Vector3 hover = PetPlacement.HoverPoint(Authored, unit, BaseScale(unit), scale, ClearDistance);
                float keep = ClearDistance - 0.002f;
                Vector3 offset = hover, velocity = Vector3.zero;
                float least = float.MaxValue;
                // Thrown 3 m to its right in half a second: the pet, which hangs in the world, is in the way.
                for (float time = 0f; time < 2.5f; time += Frame)
                {
                    if (time < 0.5f) offset.x -= 6f * Frame;
                    offset = Vector3.SmoothDamp(offset, hover, ref velocity, FollowSmoothTime, Mathf.Infinity, Frame);
                    offset = PetPlacement.KeepClear(offset, ref velocity, hover, Quaternion.identity, unit, scale, keep, Vector3.back);
                    least = Mathf.Min(least, PetPlacement.FootprintDistance(offset, Quaternion.identity, unit, scale));
                }
                Assert.GreaterOrEqual(least, keep - Millimetre, $"size {unit.size}");
                Assert.Less((offset - hover).magnitude, 0.01f, "and it is back in its place afterwards");
            }
        }

        [Test]
        public void APetThatJustGrew_IsMovedStraightOut_NotSideways()
        {
            foreach (Bounds unit in Models)
            {
                float baseScale = BaseScale(unit);
                float grown = baseScale * 1.35f;
                Vector3 small = PetPlacement.HoverPoint(Authored, unit, baseScale, baseScale, ClearDistance);
                Vector3 big = PetPlacement.HoverPoint(Authored, unit, baseScale, grown, ClearDistance);
                Vector3 velocity = Vector3.zero;
                // Still where the small pet hovered, already the size of the big one.
                Vector3 kept = PetPlacement.KeepClear(small, ref velocity, big, Quaternion.identity, unit, grown, ClearDistance - 0.002f, Vector3.back);
                Assert.GreaterOrEqual(PetPlacement.FootprintDistance(kept, Quaternion.identity, unit, grown), ClearDistance - 0.002f - Millimetre);
                float bearingBefore = Mathf.Atan2(small.x + unit.center.x * grown, small.z + unit.center.z * grown) * Mathf.Rad2Deg;
                float bearingAfter = Mathf.Atan2(kept.x + unit.center.x * grown, kept.z + unit.center.z * grown) * Mathf.Rad2Deg;
                // Being moved out by its near side shifts its bearing a little; being swung round would shift it far more.
                Assert.Less(Mathf.Abs(Mathf.DeltaAngle(bearingBefore, bearingAfter)), 6f, $"size {unit.size}");
            }
        }

        [Test]
        public void A_fast_owner_is_trailed_by_no_more_than_the_longest_trail()
        {
            const float maxTrail = 3f, snapDistance = 8f;
            // The fastest Run rank (29 m/s) left the pet 10 m back, past the distance at which it is put in place.
            foreach (float ownerSpeed in new[] { 5f, 10f, 23f, 29f })
            {
                Vector3 target = Authored, offset = Authored, velocity = Vector3.zero;
                float furthest = 0f;
                for (float time = 0f; time < 4f; time += Frame)
                {
                    offset += Vector3.back * (ownerSpeed * Frame); // the owner has gone on; the pet hangs in the world
                    Assert.Less((offset - target).magnitude, snapDistance, $"{ownerSpeed} m/s: far enough behind to be snapped");
                    offset = Vector3.SmoothDamp(offset, target, ref velocity, FollowSmoothTime, Mathf.Infinity, Frame);
                    offset = PetPlacement.LimitTrail(offset, target, maxTrail);
                    furthest = Mathf.Max(furthest, (offset - target).magnitude);
                }
                Assert.LessOrEqual(furthest, maxTrail + Millimetre, $"{ownerSpeed} m/s");
                if (ownerSpeed <= 5f) Assert.Less(furthest, maxTrail - 0.5f, "a walk is trailed as before, inside the limit");

                // The owner stops: the pet comes in to its place without overshooting past it.
                for (float time = 0f; time < 3f; time += Frame)
                {
                    offset = Vector3.SmoothDamp(offset, target, ref velocity, FollowSmoothTime, Mathf.Infinity, Frame);
                    offset = PetPlacement.LimitTrail(offset, target, maxTrail);
                }
                Assert.Less((offset - target).magnitude, 0.01f, $"{ownerSpeed} m/s: did not come in");
            }
        }

        [Test]
        public void No_longest_trail_leaves_the_pet_where_it_is()
        {
            Vector3 offset = new Vector3(0f, 0f, -20f);
            Assert.AreEqual(offset, PetPlacement.LimitTrail(offset, Vector3.zero, 0f));
        }

        private static void AssertStaysClearAndSettles(Bounds unit, float stage, float turn)
        {
            float scale = BaseScale(unit) * stage;
            Vector3 hover = PetPlacement.HoverPoint(Authored, unit, BaseScale(unit), scale, ClearDistance);
            float keep = PetPlacement.NearestDistance(hover, unit, scale) - 0.002f;
            Quaternion owner = Quaternion.Euler(0f, turn, 0f);
            float least = float.MaxValue;
            Vector3 last = Simulate(unit, scale, hover, owner, 3f, (offset, rotation) =>
                least = Mathf.Min(least, PetPlacement.FootprintDistance(offset, rotation, unit, scale)));
            Assert.GreaterOrEqual(least, keep - Millimetre, $"size {unit.size} stage x{stage} turn {turn}: came within {least:F3} of the centre line");
            Assert.Less((last - owner * hover).magnitude, 0.01f, $"size {unit.size} stage x{stage} turn {turn}: did not reach its place");
        }

        /// <summary>
        /// The follower's own frame, repeated: the owner has just turned to <paramref name="owner"/>, the pet still
        /// rests where it did when the owner faced forward.
        /// </summary>
        private static Vector3 Simulate(Bounds unit, float scale, Vector3 hover, Quaternion owner, float seconds, System.Action<Vector3, Quaternion> eachFrame)
        {
            float keep = PetPlacement.NearestDistance(hover, unit, scale) - 0.002f;
            Vector3 target = owner * hover;
            Vector3 back = owner * Vector3.back;
            Vector3 offset = hover, velocity = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            for (float time = 0f; time < seconds; time += Frame)
            {
                rotation = Quaternion.Slerp(rotation, owner, 1f - Mathf.Exp(-TurnSpeed * Frame));
                offset = Vector3.SmoothDamp(offset, target, ref velocity, FollowSmoothTime, Mathf.Infinity, Frame);
                offset = PetPlacement.KeepClear(offset, ref velocity, target, rotation, unit, scale, keep, back);
                eachFrame(offset, rotation);
            }
            return offset;
        }
    }
}
