using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// What a display character can do besides stand there: facial expressions, idle stances and emotes. The creator
    /// uses it to let the player see their character alive before they commit. Faces are Sidekick's face poses, read
    /// out of its animation clips by the editor baker into plain blend-shape weights, so they can be blended at
    /// runtime on our merged mesh. Stances and emotes are humanoid clips from Synty's Idles and Emotes &amp; Taunts
    /// packs, which come in a masculine and a feminine performance of each motion.
    /// </summary>
    public sealed class CharacterExpressionLibrary : ScriptableObject
    {
        [Serializable]
        public struct ShapeWeight
        {
            [Tooltip("Index into the library's shape list.")]
            public ushort shape;
            [Range(0f, 100f)] public float weight;
        }

        [Serializable]
        public struct Face
        {
            public string name;
            public ShapeWeight[] weights;
        }

        [Serializable]
        public struct Motion
        {
            public string name;
            [Tooltip("The pack's category (Arms Folded, Greet, Taunt...): the creator browses by it.")]
            public string group;
            [Tooltip("The masculine performance. Empty when the pack made this motion for feminine bodies only.")]
            public AnimationClip clip;
            [Tooltip("The feminine performance. Empty when the pack made this motion for masculine bodies only.")]
            public AnimationClip feminine;
            [Tooltip("Stances only: the move into the pose, played once before the loop.")]
            public AnimationClip enter;
            public AnimationClip enterFeminine;

            /// <summary>True when the pack has this motion performed for that body type. A character only shows those.</summary>
            public bool Has(BodyType body) => (body == BodyType.Feminine ? feminine : clip) != null;

            /// <summary>The performance made for a body type, or null: a body never borrows the other one's.</summary>
            public AnimationClip For(BodyType body) => body == BodyType.Feminine ? feminine : clip;

            /// <summary>The way into a stance for a body type, or null when it has none.</summary>
            public AnimationClip EnterFor(BodyType body) => body == BodyType.Feminine ? enterFeminine : enter;
        }

        [Tooltip("Every face blend shape the faces use, by Sidekick's short name (browRaiseLeft, mouthSmileRight...). " +
                 "A display character keeps these live on its mesh.")]
        [SerializeField] private List<string> shapes = new List<string>();
        [Tooltip("The first face is the neutral one.")]
        [SerializeField] private List<Face> faces = new List<Face>();
        [Tooltip("Looping stances, ordered by group. The first is the default.")]
        [SerializeField] private List<Motion> idles = new List<Motion>();
        [Tooltip("One-shot gestures, ordered by group; the character returns to its stance afterwards.")]
        [SerializeField] private List<Motion> emotes = new List<Motion>();

        public IReadOnlyList<string> Shapes => shapes;
        public IReadOnlyList<Face> Faces => faces;
        public IReadOnlyList<Motion> Idles => idles;
        public IReadOnlyList<Motion> Emotes => emotes;

        /// <summary>Editor bake entry point.</summary>
        public void SetContents(List<string> shapeNames, List<Face> faceList, List<Motion> idleList, List<Motion> emoteList)
        {
            shapes = shapeNames;
            faces = faceList;
            idles = idleList;
            emotes = emoteList;
        }

        /// <summary>
        /// The groups of a motion list in order, and for each the indices of its motions that were performed for
        /// <paramref name="body"/>: how the creator's two rows (group, then motion) walk the list.
        /// </summary>
        public static void Group(IReadOnlyList<Motion> motions, BodyType body, List<string> groups, List<List<int>> members)
        {
            groups.Clear();
            members.Clear();
            for (int i = 0; i < motions.Count; i++)
            {
                if (!motions[i].Has(body)) continue;
                string group = string.IsNullOrEmpty(motions[i].group) ? "Other" : motions[i].group;
                int at = groups.IndexOf(group);
                if (at < 0)
                {
                    at = groups.Count;
                    groups.Add(group);
                    members.Add(new List<int>());
                }
                members[at].Add(i);
            }
        }
    }
}
