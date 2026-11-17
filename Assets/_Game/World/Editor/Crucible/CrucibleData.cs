using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace KryticalNova.EditorTools.Crucible
{
    /// <summary>
    /// The Crucible's Blender data (Fable's checkpoint 022b, 17 Nov 2026) as the builder reads it: placements, exact world matrices,
    /// materials, colliders, blockers, stairs, gates, Wallstride faces, lights and lava. Everything Fable wrote in Unity axes is used
    /// as it is; Blender placements go through <see cref="ToUnity"/>: Unity (x, y, z) = Blender (-x, z, -y), metres.
    /// </summary>
    public sealed class CrucibleData
    {
        public const string Folder = "Assets/_Game/World/Zones/Crucible";
        public const string DataFolder = Folder + "/Data";
        public const string MeshPath = Folder + "/Meshes/Crucible_022_CustomPieces.fbx";
        public const string TextureFolder = Folder + "/Textures";
        public const string MaterialFolder = Folder + "/Materials";
        public const string PrefabPath = Folder + "/Prefabs/Crucible.prefab";
        public const string NavMeshPath = DataFolder + "/NavMesh-Crucible.asset";

        public JArray Objects, Materials, Boxes, ConvexHulls, GroundMeshes, Blockers, Flights, Gates, WallFaces, Lights, LavaPaths, HazeBoxes;
        public JArray EmberPoints, SmokePoints, StairSideWalls, WallFacesCutByStairs;
        public Dictionary<string, float[]> Matrices = new Dictionary<string, float[]>();

        public static CrucibleData Load()
        {
            JObject Read(string file) => JObject.Parse(File.ReadAllText(Path.Combine(DataFolder, file)));
            var d = new CrucibleData();
            JObject objects = Read("CRUCIBLE_022b_OBJECTS.json");
            d.Objects = (JArray)objects["objects"];
            d.Materials = (JArray)Read("MATERIALS_022b.json")["materials"];
            foreach (var kv in (JObject)Read("MATRICES_022b.json")["matrices"])
                d.Matrices[kv.Key] = kv.Value.ToObject<float[]>();
            JObject col = Read("COLLIDERS_022.json");
            d.Boxes = (JArray)col["boxes"]; d.ConvexHulls = (JArray)col["convex_hulls"];
            d.GroundMeshes = (JArray)col["ground_mesh_colliders"]; d.Blockers = (JArray)col["blockers"];
            JObject stairs = Read("STAIRS_022.json");
            d.Flights = (JArray)stairs["flights"]; d.StairSideWalls = (JArray)stairs["side_walls_not_wallstride"];
            d.WallFacesCutByStairs = (JArray)stairs["wallstride_faces_cut_by_stairs"];
            d.Gates = (JArray)Read("GATES_022.json")["gates"];
            d.WallFaces = (JArray)Read("WALLSTRIDE_022.json")["faces"];
            d.Lights = (JArray)Read("FX_022.json")["markers"];
            JObject lava = Read("LAVA_022.json");
            d.LavaPaths = (JArray)lava["paths"]; d.HazeBoxes = (JArray)lava["haze_boxes"];
            d.EmberPoints = (JArray)lava["ember_points_unity"]; d.SmokePoints = (JArray)lava["smoke_points_unity"];
            return d;
        }

        // Blender -> Unity basis: u = (-x, z, -y). M is orthogonal (its inverse is its transpose) with determinant -1, so M W M^T keeps
        // a Blender rotation a proper rotation in Unity's left-handed frame.
        static readonly Matrix4x4 M = new Matrix4x4(
            new Vector4(-1f, 0f, 0f, 0f), new Vector4(0f, 0f, -1f, 0f), new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 0f, 1f));

        /// <summary>
        /// Fable's FBX is written Z-up, so Unity imports its vertices as Blender (-x, y, z) and bakeAxisConversion has nothing to bake;
        /// this turn (-90 degrees about X) takes them to Blender (-x, z, -y), the frame the matrices are converted into (run 4 proof:
        /// floors and stairs came in standing on end).
        /// </summary>
        public static readonly Matrix4x4 CustomMeshFix = Matrix4x4.Rotate(Quaternion.Euler(-90f, 0f, 0f));

        public static Vector3 ToUnity(Vector3 b) => new Vector3(-b.x, b.z, -b.y);
        public static Vector3 ToUnity(JToken t) => ToUnity(V(t));
        public static Vector3 V(JToken t) => new Vector3((float)t[0], (float)t[1], (float)t[2]);
        /// <summary>A Blender size (x, y, z extents) as Unity extents.</summary>
        public static Vector3 SizeToUnity(Vector3 b) => new Vector3(b.x, b.z, b.y);

        /// <summary>The Unity world matrix of a Blender object's frame, optionally moved to a pivot given in its own (Blender) space.</summary>
        public bool TryUnityMatrix(string name, Vector3 blenderLocalOffset, out Matrix4x4 unity)
        {
            unity = Matrix4x4.identity;
            if (!Matrices.TryGetValue(name, out float[] r)) return false;
            var w = new Matrix4x4();
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                    w[i, j] = r[i * 4 + j];
            Vector3 origin = w.MultiplyPoint3x4(blenderLocalOffset);
            w.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1f));
            unity = M * w * M.transpose;
            return true;
        }

        /// <summary>Puts a transform (parented at identity under the map root) at a Unity world matrix (no mirrored objects in 022b).</summary>
        public static void Apply(Transform t, Matrix4x4 m)
        {
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            t.localPosition = m.GetColumn(3);
            t.localRotation = Quaternion.LookRotation(z.normalized, y.normalized);
            t.localScale = new Vector3(x.magnitude, y.magnitude, z.magnitude);
        }

        public static Color Colour(JToken t, float a = 1f) => t == null ? Color.white : new Color((float)t[0], (float)t[1], (float)t[2], a);
    }
}
