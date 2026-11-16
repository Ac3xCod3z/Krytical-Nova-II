using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Characters that look exactly alike show ONE merged mesh (R3-5 B, 16 Nov 2026): 200 players joining in the same
    /// gear cost one <see cref="SkinnedMeshMerger.Merge"/> per distinct look instead of one per body, which was the
    /// client's join freeze (worst frame 965 ms). The key is everything a merge reads (the rig's reference bones, the
    /// parts in order, the body shape, the resting face, the live shapes, the bare body), so a shared mesh is the
    /// mesh the character's own merge would have built. Everything per body stays per body: bones, paints (materials
    /// on the renderer), cloth, lights, eye glow and dangle bones. The mesh is counted by the characters showing it and
    /// destroyed when the last one lets it go. Main thread only, like the merger.
    /// </summary>
    public static class MergedMeshCache
    {
        /// <summary>One merged mesh and what its merge gave besides the mesh.</summary>
        public sealed class Entry
        {
            internal Key key;
            internal int users;
            public Mesh Mesh { get; internal set; }
            public string[] BoneNames { get; internal set; }
            public GearPaintLook[] Paints { get; internal set; }
            public SidekickPartRef[] PaintParts { get; internal set; }
            /// <summary>How many characters show this mesh now.</summary>
            public int Users => users;
        }

        private static readonly Dictionary<Key, Entry> ByKey = new Dictionary<Key, Entry>();
        private static readonly Dictionary<Mesh, Entry> ByMesh = new Dictionary<Mesh, Entry>();
        private static readonly Key Scratch = new Key();

        /// <summary>Merges made and merges saved since the game started (tests and the performance log read them).</summary>
        public static long Merged { get; private set; }
        public static long Shared { get; private set; }
        /// <summary>Distinct merged meshes alive now.</summary>
        public static int Count => ByKey.Count;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ByKey.Clear();
            ByMesh.Clear();
            Merged = Shared = 0;
        }

        /// <summary>
        /// Fills the look to find (<see cref="TryShare"/>) or to keep (<see cref="Keep"/>); false when this look must merge
        /// on its own (a part bound past its bind poses takes the live pose, so no two such merges are alike).
        /// </summary>
        public static bool Describe(object rig, IReadOnlyList<SidekickPartRef> parts, in BodyShapeWeights shape,
            IReadOnlyList<FaceShapeWeight> face, IReadOnlyList<string> liveShapes, IReadOnlyList<SidekickPartRef> bareBody)
        {
            if (rig == null) return false;
            foreach (var part in parts)
                if (part.IsValid && part.mesh.isReadable && part.mesh.bindposeCount < part.boneNames.Length) return false;
            Scratch.Fill(rig, parts, shape, face, liveShapes, bareBody);
            return true;
        }

        /// <summary>The mesh already merged for the look last <see cref="Describe"/>d, now counted for one more character; null if none.</summary>
        public static Entry TryShare()
        {
            if (!ByKey.TryGetValue(Scratch, out var entry) || entry.Mesh == null) return null;
            entry.users++;
            Shared++;
            return entry;
        }

        /// <summary>Keeps a fresh merge of the look last <see cref="Describe"/>d, counted for the character that made it.</summary>
        public static Entry Keep(Mesh mesh, List<Transform> bones, IReadOnlyList<GearPaintLook> paints, IReadOnlyList<SidekickPartRef> paintParts)
        {
            var names = new string[bones.Count];
            for (int i = 0; i < names.Length; i++) names[i] = bones[i].name;
            var entry = new Entry
            {
                key = Scratch.Copy(), users = 1, Mesh = mesh, BoneNames = names,
                Paints = ToArray(paints), PaintParts = ToArray(paintParts),
            };
            if (ByKey.TryGetValue(entry.key, out var stale)) ByMesh.Remove(stale.Mesh); // its mesh was destroyed from outside
            ByKey[entry.key] = entry;
            ByMesh[mesh] = entry;
            return entry;
        }

        /// <summary>Counts a merge made without the cache, for the totals.</summary>
        public static void CountMerge() => Merged++;

        /// <summary>
        /// A character stops showing <paramref name="mesh"/>: a kept mesh is destroyed when nobody shows it any more. False
        /// for a mesh this never kept (the caller destroys its own).
        /// </summary>
        public static bool Release(Mesh mesh)
        {
            if (mesh == null || !ByMesh.TryGetValue(mesh, out var entry)) return false;
            if (--entry.users > 0) return true;
            ByMesh.Remove(mesh);
            ByKey.Remove(entry.key);
            if (Application.isPlaying) UnityEngine.Object.Destroy(mesh);
            else UnityEngine.Object.DestroyImmediate(mesh); // tests and Editor tools
            return true;
        }

        private static T[] ToArray<T>(IReadOnlyList<T> list)
        {
            var array = new T[list.Count];
            for (int i = 0; i < array.Length; i++) array[i] = list[i];
            return array;
        }

        /// <summary>Everything a merge reads, compared exactly.</summary>
        internal sealed class Key : IEquatable<Key>
        {
            private object rig;
            private readonly List<SidekickPartRef> parts = new List<SidekickPartRef>(48);
            private readonly List<SidekickPartRef> bare = new List<SidekickPartRef>(8);
            private readonly List<FaceShapeWeight> face = new List<FaceShapeWeight>(8);
            private readonly List<string> live = new List<string>(8);
            private BodyShapeWeights shape;
            private int hash;

            public void Fill(object rigKey, IReadOnlyList<SidekickPartRef> partList, in BodyShapeWeights bodyShape,
                IReadOnlyList<FaceShapeWeight> faceShapes, IReadOnlyList<string> liveShapes, IReadOnlyList<SidekickPartRef> bareBody)
            {
                rig = rigKey;
                shape = bodyShape;
                parts.Clear();
                if (partList != null) for (int i = 0; i < partList.Count; i++) parts.Add(partList[i]);
                bare.Clear();
                if (bareBody != null) for (int i = 0; i < bareBody.Count; i++) bare.Add(bareBody[i]);
                face.Clear();
                if (faceShapes != null) for (int i = 0; i < faceShapes.Count; i++) face.Add(faceShapes[i]);
                live.Clear();
                if (liveShapes != null) for (int i = 0; i < liveShapes.Count; i++) live.Add(liveShapes[i]);

                unchecked
                {
                    int h = rig.GetHashCode();
                    h = h * 31 + parts.Count;
                    foreach (var p in parts) h = h * 31 + (p.mesh != null ? p.mesh.GetHashCode() : 0);
                    foreach (var p in bare) h = h * 31 + (p.mesh != null ? p.mesh.GetHashCode() : 0);
                    h = h * 31 + shape.masculineFeminine.GetHashCode();
                    h = h * 31 + shape.skinny.GetHashCode();
                    h = h * 31 + shape.heavy.GetHashCode();
                    h = h * 31 + shape.buff.GetHashCode();
                    h = h * 31 + shape.bust.GetHashCode();
                    h = h * 31 + shape.butt.GetHashCode();
                    h = h * 31 + shape.defined.GetHashCode();
                    h = h * 31 + face.Count;
                    h = h * 31 + live.Count;
                    hash = h;
                }
            }

            public Key Copy()
            {
                var copy = new Key { rig = rig, shape = shape, hash = hash };
                copy.parts.AddRange(parts);
                copy.bare.AddRange(bare);
                copy.face.AddRange(face);
                copy.live.AddRange(live);
                return copy;
            }

            public override int GetHashCode() => hash;
            public override bool Equals(object obj) => obj is Key other && Equals(other);

            public bool Equals(Key other)
            {
                if (other == null || hash != other.hash || !ReferenceEquals(rig, other.rig)) return false;
                if (!SameShape(shape, other.shape)) return false;
                if (!SameParts(parts, other.parts) || !SameParts(bare, other.bare)) return false;
                if (face.Count != other.face.Count || live.Count != other.live.Count) return false;
                for (int i = 0; i < face.Count; i++)
                    if (!string.Equals(face[i].shape, other.face[i].shape, StringComparison.Ordinal) || !face[i].weight.Equals(other.face[i].weight)) return false;
                for (int i = 0; i < live.Count; i++)
                    if (!string.Equals(live[i], other.live[i], StringComparison.Ordinal)) return false;
                return true;
            }

            private static bool SameShape(in BodyShapeWeights a, in BodyShapeWeights b) =>
                a.masculineFeminine.Equals(b.masculineFeminine) && a.skinny.Equals(b.skinny) && a.heavy.Equals(b.heavy) &&
                a.buff.Equals(b.buff) && a.bust.Equals(b.bust) && a.butt.Equals(b.butt) && a.defined.Equals(b.defined);

            private static bool SameParts(List<SidekickPartRef> a, List<SidekickPartRef> b)
            {
                if (a.Count != b.Count) return false;
                for (int i = 0; i < a.Count; i++)
                {
                    var x = a[i];
                    var y = b[i];
                    if (x.mesh != y.mesh || x.region != y.region || x.usesWrap != y.usesWrap) return false;
                    if (!SameNames(x.boneNames, y.boneNames) || !SameExtras(x.extraBones, y.extraBones)) return false;
                }
                return true;
            }

            private static bool SameNames(string[] a, string[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
                return true;
            }

            private static bool SameExtras(ExtraBone[] a, ExtraBone[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                int na = a?.Length ?? 0, nb = b?.Length ?? 0;
                if (na != nb) return false;
                for (int i = 0; i < na; i++)
                {
                    var x = a[i];
                    var y = b[i];
                    if (!string.Equals(x.name, y.name, StringComparison.Ordinal) || !string.Equals(x.parent, y.parent, StringComparison.Ordinal)) return false;
                    if (!x.localPosition.Equals(y.localPosition) || !x.localRotation.Equals(y.localRotation) || !x.localScale.Equals(y.localScale)) return false;
                }
                return true;
            }
        }
    }
}
