using UnityEngine;
using UnityEngine.Rendering;

namespace GI
{
    static class Util
    {
        private static Mesh _quad;

        public static Mesh Quad
        {
            get
            {
                if (_quad == null)
                {
                    _quad = new Mesh();

                    _quad.vertices = new Vector3[]
                    {
                        new Vector3(-1, -1, 0),
                        new Vector3(-1,  1, 0),
                        new Vector3( 1,  1, 0),
                        new Vector3( 1, -1, 0)
                    };

                    _quad.uv = new Vector2[]
                    {
                        new Vector2(0, 1),
                        new Vector2(0, 0),
                        new Vector2(1, 0),
                        new Vector2(1, 1)
                    };

                    _quad.SetIndices(new int[] { 0, 1, 2, 3 }, MeshTopology.Quads, 0);

                    return _quad;
                }

                return _quad;
            }
        }

        public static void SetCameraParameters(Material material, Camera camera)
        {
            // material.SetMatrix(P.unity_MatrixV, camera.worldToCameraMatrix);
            // material.SetMatrix(P.unity_MatrixInvV, camera.worldToCameraMatrix.inverse);
            // material.SetVector(P._ScreenParams, new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f + 1.0f / camera.pixelWidth, 1.0f + 1.0f / camera.pixelHeight));

            material.SetMatrix(P.unity_MatrixInvVP, Util.ViewProjectionInverse(camera));
            material.SetVector(P._ScaledScreenParams, new Vector4(camera.scaledPixelWidth, camera.scaledPixelHeight, 1.0f + 1.0f / camera.scaledPixelWidth, 1.0f + 1.0f / camera.scaledPixelHeight));
        }

        public static void SetCameraParameters(CommandBuffer cmd, Kernel kernel, Camera camera)
        {
            kernel.Set(cmd, P._ScaledScreenParams, new Vector4(camera.scaledPixelWidth, camera.scaledPixelHeight, 1.0f + 1.0f / camera.scaledPixelWidth, 1.0f + 1.0f / camera.scaledPixelHeight));
            kernel.Set(cmd, P._ScreenParams, new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f + 1.0f / camera.pixelWidth, 1.0f + 1.0f / camera.pixelHeight));

            kernel.Set(cmd, P.unity_MatrixInvV, camera.worldToCameraMatrix.inverse);
            kernel.Set(cmd, P.unity_MatrixInvVP, Util.ViewProjectionInverse(camera));
            kernel.Set(cmd, P.unity_MatrixV, camera.worldToCameraMatrix);
        }

        public static Vector3Int GridCenterCoord(Vector3 reference_position, float cell_dim)
        {
            return new Vector3Int(Mathf.FloorToInt(reference_position.x / cell_dim),
                                  Mathf.FloorToInt(reference_position.y / cell_dim),
                                  Mathf.FloorToInt(reference_position.z / cell_dim));
        }

        public static Vector3Int GridOriginCoord(Vector3 reference_position, float cell_dim, int grid_dim)
        {
            var grid_center = GridCenterCoord(reference_position, cell_dim);

            return new Vector3Int(grid_center.x - grid_dim / 2,
                                  grid_center.y - grid_dim / 2,
                                  grid_center.z - grid_dim / 2);
        }

        public static Vector3 GridCoordToPosition(Vector3Int coord, float cell_dim)
        {
            return new Vector3(coord.x * cell_dim, coord.y * cell_dim, coord.z * cell_dim);
        }

        public static float HorizontalAngle(Vector3 vector)
        {
            var v = new Vector2(vector.z, vector.x);

            if (v.sqrMagnitude > 0.01f)
                v.Normalize();

            var sign = (v.y < 0) ? -1.0f : 1.0f;
            return Vector2.Angle(Vector2.right, v) * sign;
        }

        public static float VerticalAngle(Vector3 vector)
        {
            var horizontal = vector;
            horizontal.y = 0;

            var sign = (vector.y > 0) ? -1.0f : 1.0f;
            return Vector2.Angle(Vector2.right, new Vector2(horizontal.magnitude, vector.y)) * sign;
        }

        public static float VerticalAngle(float height, float distance)
        {
            var sign = (height > 0) ? -1.0f : 1.0f;
            return Vector2.Angle(Vector2.right, new Vector2(distance, height)) * sign;
        }

        public static Vector3 Vector(float horizontal, float vertical)
        {
            horizontal *= Mathf.Deg2Rad;
            vertical *= Mathf.Deg2Rad;

            var x = Mathf.Cos(vertical);
            var y = -Mathf.Sin(vertical);

            return new Vector3(Mathf.Sin(horizontal) * x, y, Mathf.Cos(horizontal) * x);
        }

        public static Matrix4x4 ViewProjection(Camera camera)
        {
            return GL.GetGPUProjectionMatrix(camera.projectionMatrix, true) * camera.worldToCameraMatrix;
        }

        public static Matrix4x4 ViewProjectionInverse(Camera camera)
        {
            return ViewProjection(camera).inverse;
        }
    }
}
