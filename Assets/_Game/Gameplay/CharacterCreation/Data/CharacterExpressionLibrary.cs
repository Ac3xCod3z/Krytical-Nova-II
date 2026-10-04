using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// What a display character can do besides stand there: facial expressions, idle stances and emotes. The creator
    /// uses it to let the player see their character alive before they commit. Faces are Sidekick's face poses, read
    /// out of its animation clips by the editor baker into plain blend-shape weights, so they can be blended at
    /// runtime on our merged mesh. Idles and emotes are ordinary humanoid clips.
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
            public AnimationClip clip;
        }

        [Tooltip("Every face blend shape the faces use, by Sidekick's short name (browRaiseLeft, mouthSmileRight...). " +
                 "A display character keeps these live on its mesh.")]
        [SerializeField] private List<string> shapes = new List<string>();
        [Tooltip("The first face is the neutral one.")]
        [SerializeField] private List<Face> faces = new List<Face>();
        [Tooltip("Looping stances. The first is the default.")]
        [SerializeField] private List<Motion> idles = new List<Motion>();
        [Tooltip("One-shot gestures; the character returns to its idle afterwards.")]
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
    }
}
