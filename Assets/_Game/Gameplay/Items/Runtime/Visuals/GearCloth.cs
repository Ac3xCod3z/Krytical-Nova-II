using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Real cloth for the gear parts that have a <see cref="GearClothLook"/> (a cloak): each is left out of the merged body
    /// and drawn on the character's skeleton as its own SkinnedMeshRenderer with Unity's <see cref="Cloth"/>, pinned along
    /// its top and kept off the body by capsules on the arms, hands, trunk and legs. Presentation only: nothing here runs
    /// on a machine that draws nothing (a dedicated server), and the colliders are triggers on the Ignore Raycast layer,
    /// so they never push, block or catch a click.
    /// </summary>
    public sealed class GearCloth
    {
        private const string Folder = "Meshes/PartCloth/";
        private static readonly Dictionary<Mesh, GearClothLook> Looks = new Dictionary<Mesh, GearClothLook>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Looks.Clear();

        /// <summary>The cloth settings of <paramref name="part"/>; false for a part that is merged into the body as usual.</summary>
        public static bool TryGetLook(SidekickPartRef part, out GearClothLook look)
        {
            look = null;
            if (part.mesh == null || Application.isBatchMode) return false;
            if (!Looks.TryGetValue(part.mesh, out look))
            {
                string name = part.mesh.name;
                int lod = name.IndexOf(' ');
                if (lod > 0) name = name.Substring(0, lod);
                look = name.StartsWith("SK_KRYT_", System.StringComparison.Ordinal) ? Resources.Load<GearClothLook>(Folder + name + "_Cloth") : null;
                Looks[part.mesh] = look;
            }
            return look != null;
        }

        // The capsules: bone to bone (or past the first, away from the second, by extend metres), by kind.
        private enum Kind { Arm, Trunk, Leg }
        private static readonly (string a, string b, Kind kind, float extend)[] Capsules =
        {
            ("upperarm_l", "lowerarm_l", Kind.Arm, 0f), ("upperarm_r", "lowerarm_r", Kind.Arm, 0f),
            ("lowerarm_l", "hand_l", Kind.Arm, 0f), ("lowerarm_r", "hand_r", Kind.Arm, 0f),
            ("hand_l", "lowerarm_l", Kind.Arm, 0.09f), ("hand_r", "lowerarm_r", Kind.Arm, 0.09f),
            ("pelvis", "spine_03", Kind.Trunk, 0f), ("spine_03", "neck_01", Kind.Trunk, 0f),
            ("thigh_l", "calf_l", Kind.Leg, 0f), ("thigh_r", "calf_r", Kind.Leg, 0f),
            ("calf_l", "foot_l", Kind.Leg, 0f), ("calf_r", "foot_r", Kind.Leg, 0f),
        };

        private readonly List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>();
        private readonly List<GameObject> colliders = new List<GameObject>();
        private readonly List<CapsuleCollider> capsules = new List<CapsuleCollider>();

        public IReadOnlyList<SkinnedMeshRenderer> Renderers => renderers;

        /// <summary>
        /// Draws <paramref name="parts"/> (each with its look) as cloth on the skeleton (bones by name through
        /// <paramref name="findBone"/>), in <paramref name="materialFor"/>'s material, the body shape baked in.
        /// </summary>
        public void Show(IReadOnlyList<(SidekickPartRef part, GearClothLook look)> parts, Transform parent, int layer, Transform rootBone,
            Bounds bounds, BodyShapeWeights shape, System.Func<string, Transform> findBone, System.Func<SidekickPartRef, Material> materialFor)
        {
            Clear();
            if (parts == null || parts.Count == 0) return;
            float scale = Mathf.Max(1e-4f, Mathf.Abs(parent.lossyScale.y));
            foreach (var (part, look) in parts)
            {
                var go = new GameObject(part.mesh.name + "_Cloth") { layer = layer };
                go.transform.SetParent(parent, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                var bones = new Transform[part.boneNames.Length];
                for (int i = 0; i < bones.Length; i++) bones[i] = findBone(part.boneNames[i]) ?? parent;
                smr.bones = bones;
                smr.rootBone = rootBone;
                smr.sharedMesh = part.mesh;
                var mat = materialFor(part);
                if (mat != null)
                {
                    var mats = new Material[part.mesh.subMeshCount];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    smr.sharedMaterials = mats;
                }
                smr.localBounds = bounds;
                smr.updateWhenOffscreen = false;
                for (int i = 0; i < part.mesh.blendShapeCount; i++) smr.SetBlendShapeWeight(i, shape.WeightFor(part.mesh.GetBlendShapeName(i)));
                renderers.Add(smr);

                var cloth = go.AddComponent<Cloth>();
                cloth.stretchingStiffness = look.stretchingStiffness;
                cloth.bendingStiffness = look.bendingStiffness;
                cloth.damping = look.damping;
                cloth.friction = look.friction;
                cloth.worldVelocityScale = look.worldVelocityScale;
                cloth.worldAccelerationScale = look.worldAccelerationScale;
                cloth.useGravity = look.gravity > 0f;
                cloth.externalAcceleration = Physics.gravity * (look.gravity - 1f);
                cloth.coefficients = Coefficients(part.mesh, look, scale);
                cloth.capsuleColliders = Colliders(findBone, look, parent, scale);
            }
        }

        /// <summary>Pinned above the pin line, let go over the ease band, free to maxDrop at the hem.</summary>
        private static ClothSkinningCoefficient[] Coefficients(Mesh mesh, GearClothLook look, float scale)
        {
            var verts = mesh.vertices;
            float lowest = float.MaxValue;
            foreach (var v in verts) lowest = Mathf.Min(lowest, v.y);
            var co = new ClothSkinningCoefficient[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                float below = look.pinAbove - verts[i].y;                                 // metres under the pin line
                float t = Mathf.Clamp01(below / Mathf.Max(1e-4f, look.ease));              // 0 pinned .. 1 free
                float depth = Mathf.Clamp01(below / Mathf.Max(1e-4f, look.pinAbove - lowest));
                co[i].maxDistance = t * t * (3f - 2f * t) * Mathf.Lerp(0.05f, look.maxDrop, depth) * scale;
                co[i].collisionSphereDistance = float.MaxValue;
            }
            return co;
        }

        private CapsuleCollider[] Colliders(System.Func<string, Transform> findBone, GearClothLook look, Transform parent, float scale)
        {
            if (capsules.Count > 0) return capsules.ToArray();
            foreach (var (a, b, kind, extend) in Capsules)
            {
                var ta = findBone(a); var tb = findBone(b);
                if (ta == null || tb == null) continue;
                float radius = kind == Kind.Arm ? look.armRadius : kind == Kind.Trunk ? look.trunkRadius : look.legRadius;
                var go = new GameObject("ClothCollider_" + a) { layer = 2 }; // Ignore Raycast: never catches a click
                go.transform.SetParent(ta, false);
                var cap = go.AddComponent<CapsuleCollider>();
                cap.isTrigger = true; // keeps the cloth off; pushes and blocks nothing
                Vector3 local = ta.InverseTransformPoint(tb.position);
                if (extend > 0f) local = -local.normalized * (extend / Mathf.Max(1e-4f, ta.lossyScale.y) * scale);
                cap.center = local * 0.5f;
                cap.direction = MainAxis(local);
                float lossy = Mathf.Max(1e-4f, Mathf.Abs(ta.lossyScale.y));
                cap.radius = radius * scale / lossy;
                cap.height = local.magnitude + cap.radius * 2f;
                colliders.Add(go);
                capsules.Add(cap);
            }
            return capsules.ToArray();
        }

        private static int MainAxis(Vector3 v)
        {
            Vector3 a = new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
            return a.x >= a.y && a.x >= a.z ? 0 : a.y >= a.z ? 1 : 2;
        }

        /// <summary>Turns the cloth on or off (a far-away character's cloak hangs as skinned instead).</summary>
        public void SetSimulated(bool on)
        {
            foreach (var r in renderers)
                if (r != null && r.TryGetComponent(out Cloth cloth) && cloth.enabled != on) cloth.enabled = on;
        }

        public void Clear()
        {
            foreach (var r in renderers) if (r != null) Object.Destroy(r.gameObject);
            renderers.Clear();
        }

        /// <summary>Leaves the cloth and its colliders for good (the character is gone or rebuilt on another skeleton).</summary>
        public void Dispose()
        {
            Clear();
            foreach (var c in colliders) if (c != null) Object.Destroy(c);
            colliders.Clear();
            capsules.Clear();
        }
    }
}
