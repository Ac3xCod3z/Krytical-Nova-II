/******************************************************************************/
/*
  Project   - Boing Kit
  Publisher - Long Bunny Labs
              http://LongBunnyLabs.com
  Author    - Ming-Lun "Allen" Chou
              http://AllenChou.net
*/
/******************************************************************************/

using UnityEditor;

namespace BoingKit
{
  [CustomEditor(typeof(BoingBones))]
  [CanEditMultipleObjects]
  public class BoingBonesEditor : BoingReactorEditor
  {
    private SerializedProperty TwistPropagation;
    private SerializedProperty BoneChains;
    private SerializedProperty MaxCollisionResolutionSpeed;
    private SerializedProperty BoingColliders;
    private SerializedProperty UnityColliders;

    private SerializedProperty DebugDrawRawBones;
    private SerializedProperty DebugDrawTargetBones;
    private SerializedProperty DebugDrawBoingBones;
    private SerializedProperty DebugDrawFinalBones;
    private SerializedProperty DebugDrawColliders;
    private SerializedProperty DebugDrawBoneNames;
    private SerializedProperty DebugDrawLengthFromRoot;
    private SerializedProperty DebugDrawChainBounds;

    public BoingBonesEditor()
    {
      m_isBones = true;
    }

    public override void OnEnable()
    {
      base.OnEnable();

      TwistPropagation = serializedObject.FindProperty("TwistPropagation");
      BoneChains = serializedObject.FindProperty("BoneChains");
      MaxCollisionResolutionSpeed = serializedObject.FindProperty("MaxCollisionResolutionSpeed");
      BoingColliders = serializedObject.FindProperty("BoingColliders");
      UnityColliders = serializedObject.FindProperty("UnityColliders");

      DebugDrawRawBones = serializedObject.FindProperty("DebugDrawRawBones");
      DebugDrawTargetBones = serializedObject.FindProperty("DebugDrawTargetBones");
      DebugDrawBoingBones = serializedObject.FindProperty("DebugDrawBoingBones");
      DebugDrawFinalBones = serializedObject.FindProperty("DebugDrawFinalBones");
      DebugDrawColliders = serializedObject.FindProperty("DebugDrawColliders");
      DebugDrawChainBounds = serializedObject.FindProperty("DebugDrawChainBounds");
      DebugDrawBoneNames = serializedObject.FindProperty("DebugDrawBoneNames");
      DebugDrawLengthFromRoot = serializedObject.FindProperty("DebugDrawLengthFromRoot");
    }

    protected override void DrawContent()
    {
      base.DrawContent();


      serializedObject.Update();


      Header("Bones");
      {
        Property(TwistPropagation, "Twist Propagation");
        Property(BoneChains, "Bone Chains");
        Property(BoingColliders, "Boing Colliders");
        Property(UnityColliders, "Unity Colliders");
        Property(MaxCollisionResolutionSpeed, "Max Collision Resolution Speed");
      }


      Header("Debug Draw");
      {
        Property(DebugDrawRawBones,       "Raw Bones");
        Property(DebugDrawTargetBones,    "Target Bones (Play Mode Only)");
        Property(DebugDrawBoingBones,     "Boing Bones (Play Mode Only)");
        Property(DebugDrawFinalBones,     "Final Bones (Play Mode Only)");
        Property(DebugDrawColliders,      "Colliders");
        Property(DebugDrawChainBounds,    "Chain Bounds (Play Mode Only)");
        Property(DebugDrawBoneNames,      "Bone Names");
        Property(DebugDrawLengthFromRoot, "Chain Length From Root");
      }

      serializedObject.ApplyModifiedProperties();
    }
  }

}
