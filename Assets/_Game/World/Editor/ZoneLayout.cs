using System;
using System.Collections.Generic;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// One map's layout, as <see cref="ZoneMapBuilder"/> reads it from <c>World/Data/ZoneLayouts/&lt;scene&gt;.json</c>:
    /// the terrain's size and shape, how it is painted, the trees, the set pieces and the scattered props, the markers
    /// where gameplay will go and the camera shots its concept art is taken from. Positions are metres from the map's
    /// centre, x east and z north; heights are world metres, 0 being the ground a player arrives on. Written from the
    /// place's section of Design/Zone-Build-Kits.md.
    /// </summary>
    [Serializable]
    public sealed class ZoneLayout
    {
        public string scene;
        public float sizeX = 2000f, sizeZ = 2000f;
        [UnityEngine.Tooltip("Metres from the lowest to the highest point the terrain may have.")]
        public float height = 300f;
        [UnityEngine.Tooltip("Metres the terrain may go below the ground (river beds, pits, craters).")]
        public float depth = 40f;
        public int heightRes = 1025, alphaRes = 1024;
        public int seed = 1;
        public List<string> layers = new List<string>();
        public int slopeLayer = -1;
        public float slopeStart = 28f, slopeFull = 40f;
        public List<Band> bands = new List<Band>();
        public float waterLevel = float.NaN;
        public string waterMaterial = "";
        [UnityEngine.Tooltip("Greater than 0: the water is only this rectangle (x, z, width, depth, rotation) instead of under the whole map.")]
        public float waterX, waterZ, waterW, waterD, waterRot;
        public List<Stamp> stamps = new List<Stamp>();
        public List<Shape> paint = new List<Shape>();
        public List<Road> roads = new List<Road>();
        public List<Scatter> trees = new List<Scatter>();
        public List<Scatter> scatter = new List<Scatter>();
        public List<Piece> objects = new List<Piece>();
        public List<Ring> rings = new List<Ring>();
        public List<Line> lines = new List<Line>();
        public List<Marker> markers = new List<Marker>();
        public List<Shot> shots = new List<Shot>();

        [Serializable]
        public sealed class Band { public int layer; public float from, to, blend = 15f; }

        /// <summary>
        /// A change to the ground. type: noise (amp, scale; everywhere, faded by r/r2 when given), floor (h: the ground never dips below it), slope (h: the land rises northward by h between z = r and z = r2), hill (x, z, r, h:
        /// a smooth bump), plateau (x, z, r, h, falloff: sets the ground to h), rect (x, z, w, d, rot, h, falloff: a
        /// flat box), crater (x, z, r, h = depth below its rim, rim = rim height), line (pts, w, h, falloff, mode
        /// add | set | carve), rim (w, h: mountains round the edge, open where gaps say), terraces (x, z, r, r2, h =
        /// top, steps: rings stepping down to the centre).
        /// </summary>
        [Serializable]
        public sealed class Stamp
        {
            public string type = "hill";
            public float x, z, r, r2, w, d, rot, h, falloff = 20f, amp, scale = 0.004f, rim, steps;
            public string mode = "add";
            public List<float> pts = new List<float>();
            [UnityEngine.Tooltip("Rim only: gaps left open, as x, z, width triples.")]
            public List<float> gaps = new List<float>();
        }

        /// <summary>An area: shape circle (x, z, r), ring (x, z, r inner, r2 outer), rect (x, z, w, d, rot), line (pts, w) or all.</summary>
        [Serializable]
        public class Shape
        {
            public string shape = "circle";
            public float x, z, r, r2, w, d, rot;
            [UnityEngine.Tooltip("Ring only: when a1 > a0, just the sector between these compass angles (0 = north, 90 = east).")]
            public float a0, a1;
            public List<float> pts = new List<float>();
            public int layer;
            public float falloff = 6f;
        }

        [Serializable]
        public sealed class Road { public List<float> pts = new List<float>(); public float w = 10f; public int layer = -1; public bool flatten = true; public float shoulder = 8f; }

        [Serializable]
        public sealed class Scatter : Shape
        {
            public List<string> prefabs = new List<string>();
            public int count = 50;
            public float minDist = 4f;
            public float scaleMin = 0.9f, scaleMax = 1.1f;
            [UnityEngine.Tooltip("Greater than 0: each piece is sized to a height between fitMin and fitMax metres instead of scaled.")]
            public float fitMin, fitMax;
            [UnityEngine.Tooltip("Turn each piece square to the shape's centre, so a district's buildings line its streets.")]
            public bool faceCenter;
            public float maxSlope = 35f;
            public float minY = -9999f, maxY = 9999f;
            public bool align;
            public float dy;
            public bool collide;
            public string group = "Scatter";
            public bool keepOffRoads = true;
        }

        [Serializable]
        public sealed class Piece
        {
            public string prefab, name, group = "Set Pieces";
            public float x, z, dy, rot, tilt, roll, scale = 1f;
            [UnityEngine.Tooltip("Greater than 0: the piece is sized to stand this many metres high.")]
            public float fitHeight;
            [UnityEngine.Tooltip("When set, the piece stands at this world height instead of on the ground.")]
            public float y = float.NaN;
            public bool collide = true;
        }

        [Serializable]
        public sealed class Ring
        {
            public List<string> prefabs = new List<string>();
            public float x, z, r, dy, scale = 1f, rotOffset, startAngle, endAngle = 360f, fitHeight;
            [UnityEngine.Tooltip("0: as many as fit edge to edge.")]
            public int count;
            public bool faceOut = true;
            public string group = "Rings";
            public bool collide = true;
        }

        [Serializable]
        public sealed class Line
        {
            public List<string> prefabs = new List<string>();
            public List<float> pts = new List<float>();
            [UnityEngine.Tooltip("0: edge to edge, by the piece's own length.")]
            public float spacing, dy, scale = 1f, rotOffset, jitter, fitHeight;
            [UnityEngine.Tooltip("Metres to the side of the line (a row of trees beside a road).")]
            public float offset;
            public bool align = true;
            public string group = "Lines";
            public bool collide = true;
        }

        [Serializable]
        public sealed class Marker { public string name; public float x, z, dy; }

        [Serializable]
        public sealed class Shot
        {
            public string name;
            public float x, y, z, tx, ty, tz, fov = 40f;
            [UnityEngine.Tooltip("Greater than 0: an orthographic shot this many metres tall (the layout picture).")]
            public float ortho;
        }
    }
}
