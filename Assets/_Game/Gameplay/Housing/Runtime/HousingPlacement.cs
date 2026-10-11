using UnityEngine;

namespace KryticalNova.Housing
{
    /// <summary>The arithmetic of setting a piece down: the grid, the turn steps, and whether two footprints overlap.</summary>
    public static class HousingPlacement
    {
        public static float Snap(float value, float step) => step > 0f ? Mathf.Round(value / step) * step : value;

        /// <summary>Snaps across and along the area; height is left to the surface the piece stands on.</summary>
        public static Vector3 SnapToGrid(Vector3 local, float step) =>
            new Vector3(Snap(local.x, step), local.y, Snap(local.z, step));

        public static float SnapYaw(float yaw, float step) => Mathf.DeltaAngle(0f, Snap(yaw, step));

        /// <summary>The next turn step from where a piece is, one way or the other.</summary>
        public static float StepYaw(float yaw, float step, int direction) =>
            Mathf.DeltaAngle(0f, Snap(yaw, step) + step * Mathf.Sign(direction));

        /// <summary>
        /// Whether two rectangles on the floor overlap, each given by its middle, half its width and depth, and its
        /// turn about the upright (separating axes). Touching edges do not count: pieces may stand side by side.
        /// </summary>
        public static bool FootprintsOverlap(Vector2 centerA, Vector2 halfA, float yawA, Vector2 centerB, Vector2 halfB, float yawB)
        {
            const float Skin = 0.01f;
            halfA = Vector2.Max(halfA - Vector2.one * Skin, Vector2.zero);
            halfB = Vector2.Max(halfB - Vector2.one * Skin, Vector2.zero);
            Axes(yawA, out Vector2 ax, out Vector2 az);
            Axes(yawB, out Vector2 bx, out Vector2 bz);
            Vector2 between = centerB - centerA;
            return !Separated(ax, between, halfA.x, bx, bz, halfB)
                && !Separated(az, between, halfA.y, bx, bz, halfB)
                && !Separated(bx, between, halfB.x, ax, az, halfA)
                && !Separated(bz, between, halfB.y, ax, az, halfA);
        }

        // Floor axes of a rectangle turned by yaw (x across, y = the world's z).
        private static void Axes(float yaw, out Vector2 across, out Vector2 along)
        {
            float radians = yaw * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);
            across = new Vector2(cos, -sin);
            along = new Vector2(sin, cos);
        }

        private static bool Separated(Vector2 axis, Vector2 between, float ownHalf, Vector2 otherX, Vector2 otherZ, Vector2 otherHalf)
        {
            float reach = ownHalf + Mathf.Abs(Vector2.Dot(axis, otherX)) * otherHalf.x + Mathf.Abs(Vector2.Dot(axis, otherZ)) * otherHalf.y;
            return Mathf.Abs(Vector2.Dot(axis, between)) >= reach;
        }

        /// <summary>The middle of a footprint on the floor, for a piece whose model is not centred on its pivot.</summary>
        public static Vector2 FootprintCenter(Vector3 position, float yaw, Vector2 pivotToCenter, float scale)
        {
            Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * new Vector3(pivotToCenter.x, 0f, pivotToCenter.y) * scale;
            return new Vector2(position.x + offset.x, position.z + offset.z);
        }
    }
}
