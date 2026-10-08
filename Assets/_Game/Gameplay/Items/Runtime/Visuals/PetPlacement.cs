using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Where a pet hovers beside its owner, free of scene state so it can be tested. The owner's centre line is the
    /// vertical line through the owner; a pet's box is its model's bounds at scale 1 (<see cref="TryMeasure"/>)
    /// times its scale. The rule: the pet's nearest point stays a fixed distance from the centre line and its
    /// underside stays at the height it has at base size, so a pet that grows moves out and up, never in or down.
    /// </summary>
    public static class PetPlacement
    {
        private const float Epsilon = 0.0001f;
        // Being this little short of clear is clear: stops a resting pet being nudged by rounding.
        private const float Slack = 0.0005f;
        private const int PushPasses = 8;
        // A hover point less than this far round the owner is straight ahead: the pet is only moved out, not sideways.
        private const float SwingAngle = 25f;
        // From here up the hover point is as good as straight across the owner, and there is no shorter way round.
        private const float TurnAboutAngle = 170f;

        /// <summary>
        /// The box of a model in its own root's space, from its meshes: the same whichever way the model is turned
        /// or scaled in the world. Particles, trails and lines are effects, not the body, and are left out.
        /// </summary>
        public static bool TryMeasure(GameObject model, out Bounds bounds)
        {
            bounds = default;
            if (model == null) return false;
            bool any = false;
            Matrix4x4 toRoot = model.transform.worldToLocalMatrix;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                Bounds own;
                Transform space = renderer.transform;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    own = skinned.localBounds;
                    if (skinned.rootBone != null) space = skinned.rootBone;
                }
                else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                    own = filter.sharedMesh.bounds;
                else continue;

                Matrix4x4 toModel = toRoot * space.localToWorldMatrix;
                for (int corner = 0; corner < 8; corner++)
                {
                    var sign = new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
                    Vector3 point = toModel.MultiplyPoint3x4(own.center + Vector3.Scale(own.extents, sign));
                    if (any) bounds.Encapsulate(point);
                    else
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        any = true;
                    }
                }
            }
            return any;
        }

        /// <summary>
        /// Where the pet's pivot belongs at <paramref name="scale"/>, in the owner's frame (x right, y up, z forward),
        /// given the point it was authored to hover at with <paramref name="baseScale"/>. Its nearest point is put
        /// <paramref name="clearDistance"/> from the owner's centre line, on the side the authored point is on; a
        /// negative distance keeps the one the authored point gives at base size. Its underside stays where it is at
        /// base size. A pet authored over the centre line (above the owner's head) only keeps its underside.
        /// </summary>
        public static Vector3 HoverPoint(Vector3 authored, Bounds unit, float baseScale, float scale, float clearDistance)
        {
            Vector3 baseCentre = authored + unit.center * baseScale;
            Vector3 baseHalf = unit.extents * baseScale;
            Vector3 half = unit.extents * scale;

            float nearX = NearSide(baseCentre.x, baseHalf.x);
            float nearZ = NearSide(baseCentre.z, baseHalf.z);
            float authoredDistance = Mathf.Sqrt(nearX * nearX + nearZ * nearZ);

            Vector3 centre = baseCentre;
            if (authoredDistance > Epsilon)
            {
                // The nearest point slides along its own line from the owner, so the pet stays on the side it was put.
                float reach = clearDistance >= 0f ? clearDistance / authoredDistance : 1f;
                centre.x = Anchored(baseCentre.x, nearX, nearX * reach, half.x);
                centre.z = Anchored(baseCentre.z, nearZ, nearZ * reach, half.z);
            }
            centre.y = baseCentre.y - baseHalf.y + half.y;
            return centre - unit.center * scale;
        }

        /// <summary>
        /// From the owner's centre line to the nearest point of a pet that faces the way its owner does, with its
        /// pivot at <paramref name="pivot"/> in the owner's frame. 0 when the box stands over the line.
        /// </summary>
        public static float NearestDistance(Vector3 pivot, Bounds unit, float scale)
        {
            Vector3 centre = pivot + unit.center * scale;
            Vector3 half = unit.extents * scale;
            float x = NearSide(centre.x, half.x);
            float z = NearSide(centre.z, half.z);
            return Mathf.Sqrt(x * x + z * z);
        }

        /// <summary>The height of the pet's underside, with its pivot at <paramref name="pivot"/>.</summary>
        public static float Underside(Vector3 pivot, Bounds unit, float scale) => pivot.y + (unit.center.y - unit.extents.y) * scale;

        /// <summary>
        /// From the owner's centre line to the nearest point of the pet's box however the pet is turned.
        /// <paramref name="offset"/> is the pet's pivot relative to the owner in world axes, <paramref name="rotation"/>
        /// the pet's own (about the vertical). 0 when the box stands over the line.
        /// </summary>
        public static float FootprintDistance(Vector3 offset, Quaternion rotation, Bounds unit, float scale) =>
            Footprint(offset, rotation, unit, scale, out _);

        // The same, and which way (in world axes) the box would have to move to get further from the line. That way
        // is zero when the line passes through the box.
        private static float Footprint(Vector3 offset, Quaternion rotation, Bounds unit, float scale, out Vector3 away)
        {
            Vector3 half = unit.extents * scale;
            // The centre line seen from the box: where the owner is in the pet's own axes, from the middle of the box.
            Vector3 line = Quaternion.Inverse(rotation) * -offset - unit.center * scale;
            float x = Mathf.Abs(line.x) - half.x;
            float z = Mathf.Abs(line.z) - half.z;
            // From the box's nearest point to the line.
            var toLine = new Vector3(x > 0f ? Mathf.Sign(line.x) * x : 0f, 0f, z > 0f ? Mathf.Sign(line.z) * z : 0f);
            float distance = toLine.magnitude;
            away = distance > Epsilon ? rotation * (toLine / -distance) : Vector3.zero;
            return distance;
        }

        /// <summary>
        /// Stops a pet on its way to its hover point from cutting through its owner. A pet whose box, turned as it
        /// is, comes nearer the owner's centre line than <paramref name="keep"/> is moved straight out again; and
        /// when its hover point is round the other side (the owner turned), what it could not travel inward it
        /// travels sideways instead, so it swings round the owner rather than stalling against them. A turn-about
        /// has no shorter way round: the pet goes by the owner's back. Offsets are the pet's pivot relative to the
        /// owner in world axes; <paramref name="velocity"/> is the follow damping's own and is turned with the pet.
        /// </summary>
        public static Vector3 KeepClear(Vector3 offset, ref Vector3 velocity, Vector3 target, Quaternion rotation,
            Bounds unit, float scale, float keep, Vector3 ownerBack)
        {
            if (keep <= 0f) return offset;
            float pushed = PushOut(ref offset, rotation, unit, scale, keep, ownerBack);
            if (pushed <= 0f) return offset;

            Vector3 toCentre = rotation * (unit.center * scale);
            Vector3 here = Flat(offset + toCentre);
            Vector3 there = Flat(target + toCentre);
            float radius = here.magnitude;
            if (radius < Epsilon || there.sqrMagnitude < Epsilon * Epsilon) return offset;

            float angle = Vector3.SignedAngle(here, there, Vector3.up);
            float share = Mathf.Clamp01(Mathf.Abs(angle) / SwingAngle);
            if (share <= 0f) return offset;
            float side = Mathf.Abs(angle) < TurnAboutAngle
                ? Mathf.Sign(angle)
                : Mathf.Sign(Vector3.SignedAngle(here, Flat(ownerBack), Vector3.up));
            float step = Mathf.Min(share * pushed / radius * Mathf.Rad2Deg, Mathf.Abs(angle));
            Quaternion swing = Quaternion.AngleAxis(side * step, Vector3.up);

            Vector3 swung = swing * here;
            offset.x += swung.x - here.x;
            offset.z += swung.z - here.z;
            // Its momentum goes round with it: the speed it had towards the owner becomes speed round them.
            velocity = swing * velocity;
            Vector3 outward = swung / radius;
            float inward = -Vector3.Dot(velocity, outward);
            if (inward > 0f)
            {
                Vector3 round = Quaternion.AngleAxis(side * 90f, Vector3.up) * outward;
                velocity += outward * inward + round * (inward * share);
            }

            PushOut(ref offset, rotation, unit, scale, keep, ownerBack);
            return offset;
        }

        // Moves the pivot until the box is `keep` clear of the centre line: straight away from the line by the shortest
        // way, or, while the line is still inside the box, the way its middle already lies. Returns how far it moved.
        private static float PushOut(ref Vector3 offset, Quaternion rotation, Bounds unit, float scale, float keep, Vector3 ownerBack)
        {
            Vector3 toCentre = rotation * (unit.center * scale);
            float pushed = 0f;
            for (int pass = 0; pass < PushPasses; pass++)
            {
                float deficit = keep - Footprint(offset, rotation, unit, scale, out Vector3 away);
                if (deficit <= Slack) break;
                if (away == Vector3.zero)
                {
                    away = Flat(offset + toCentre);
                    float radius = away.magnitude;
                    // Dead on the centre line there is no "away": leave by the owner's back.
                    away = radius > Epsilon ? away / radius : Flat(ownerBack).normalized;
                }
                offset += away * deficit;
                pushed += deficit;
            }
            return pushed;
        }

        // Coordinate of the box's nearest point to 0 on one axis: its near face, or 0 when the box spans 0.
        private static float NearSide(float centre, float half)
        {
            if (centre - half > 0f) return centre - half;
            if (centre + half < 0f) return centre + half;
            return 0f;
        }

        // Centre of a box of this half-size with its near face at `near`. A box that spanned 0 at base size stays put.
        private static float Anchored(float baseCentre, float baseNear, float near, float half)
        {
            if (baseNear > 0f) return near + half;
            if (baseNear < 0f) return near - half;
            return baseCentre;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
