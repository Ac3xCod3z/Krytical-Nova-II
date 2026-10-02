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
  [CustomEditor(typeof(BoingBehavior))]
  [CanEditMultipleObjects]
  public class BoingBehaviorEditor : BoingEditorBase
  {
    private SerializedProperty UpdateMode;

    private SerializedProperty TwoDDistanceCheck;
    private SerializedProperty TwoDPositionInfluence;
    private SerializedProperty TwoDRotationInfluence;
    private SerializedProperty EnablePositionEffect;
    private SerializedProperty EnableRotationEffect;
    private SerializedProperty EnableScaleEffect;
    private SerializedProperty GlobalReactionUpVector;

    private SerializedProperty TranslationLockSpace;
    private SerializedProperty LockTranslationX;
    private SerializedProperty LockTranslationY;
    private SerializedProperty LockTranslationZ;

    private SerializedProperty RotationLockSpace;
    private SerializedProperty LockRotationX;
    private SerializedProperty LockRotationY;
    private SerializedProperty LockRotationZ;

    private SerializedProperty TwoDPlane;

    private SerializedProperty SharedParams;

    private SerializedProperty PositionParameterMode;
    private SerializedProperty PositionExponentialHalfLife;
    private SerializedProperty PositionOscillationHalfLife;
    private SerializedProperty PositionOscillationFrequency;
    private SerializedProperty PositionOscillationDampingRatio;
    private SerializedProperty MoveReactionMultiplier;
    private SerializedProperty LinearImpulseMultiplier;

    private SerializedProperty RotationParameterMode;
    private SerializedProperty RotationExponentialHalfLife;
    private SerializedProperty RotationOscillationHalfLife;
    private SerializedProperty RotationOscillationFrequency;
    private SerializedProperty RotationOscillationDampingRatio;
    private SerializedProperty RotationReactionMultiplier;
    private SerializedProperty AngularImpulseMultiplier;
    private SerializedProperty RotationReactionUp;

    private SerializedProperty ScaleParameterMode;
    private SerializedProperty ScaleExponentialHalfLife;
    private SerializedProperty ScaleOscillationHalfLife;
    private SerializedProperty ScaleOscillationFrequency;
    private SerializedProperty ScaleOscillationDampingRatio;

    protected bool m_isReactor = false;
    protected bool m_isReactorField = false;
    protected bool m_isBones = false;

    public virtual void OnEnable()
    {
      var p = serializedObject.FindProperty("Params");

      UpdateMode = serializedObject.FindProperty("UpdateMode");

      TwoDDistanceCheck = serializedObject.FindProperty("TwoDDistanceCheck");
      TwoDPositionInfluence = serializedObject.FindProperty("TwoDPositionInfluence");
      TwoDRotationInfluence = serializedObject.FindProperty("TwoDRotationInfluence");
      EnablePositionEffect = serializedObject.FindProperty("EnablePositionEffect");
      EnableRotationEffect = serializedObject.FindProperty("EnableRotationEffect");
      EnableScaleEffect = serializedObject.FindProperty("EnableScaleEffect");
      GlobalReactionUpVector = serializedObject.FindProperty("GlobalReactionUpVector");

      TranslationLockSpace = serializedObject.FindProperty("TranslationLockSpace");
      LockTranslationX = serializedObject.FindProperty("LockTranslationX");
      LockTranslationY = serializedObject.FindProperty("LockTranslationY");
      LockTranslationZ = serializedObject.FindProperty("LockTranslationZ");

      RotationLockSpace = serializedObject.FindProperty("RotationLockSpace");
      LockRotationX = serializedObject.FindProperty("LockRotationX");
      LockRotationY = serializedObject.FindProperty("LockRotationY");
      LockRotationZ = serializedObject.FindProperty("LockRotationZ");

      SharedParams = serializedObject.FindProperty("SharedParams");

      TwoDPlane = p.FindPropertyRelative("TwoDPlane");

      PositionParameterMode = p.FindPropertyRelative("PositionParameterMode");
      PositionExponentialHalfLife = p.FindPropertyRelative("PositionExponentialHalfLife");
      PositionOscillationHalfLife = p.FindPropertyRelative("PositionOscillationHalfLife");
      PositionOscillationFrequency = p.FindPropertyRelative("PositionOscillationFrequency");
      PositionOscillationDampingRatio = p.FindPropertyRelative("PositionOscillationDampingRatio");
      MoveReactionMultiplier = p.FindPropertyRelative("MoveReactionMultiplier");
      LinearImpulseMultiplier = p.FindPropertyRelative("LinearImpulseMultiplier");

      RotationParameterMode = p.FindPropertyRelative("RotationParameterMode");
      RotationExponentialHalfLife = p.FindPropertyRelative("RotationExponentialHalfLife");
      RotationOscillationHalfLife = p.FindPropertyRelative("RotationOscillationHalfLife");
      RotationOscillationFrequency = p.FindPropertyRelative("RotationOscillationFrequency");
      RotationOscillationDampingRatio = p.FindPropertyRelative("RotationOscillationDampingRatio");
      RotationReactionMultiplier = p.FindPropertyRelative("RotationReactionMultiplier");
      AngularImpulseMultiplier = p.FindPropertyRelative("AngularImpulseMultiplier");
      RotationReactionUp = p.FindPropertyRelative("RotationReactionUp");

      ScaleParameterMode = p.FindPropertyRelative("ScaleParameterMode");
      ScaleExponentialHalfLife = p.FindPropertyRelative("ScaleExponentialHalfLife");
      ScaleOscillationHalfLife = p.FindPropertyRelative("ScaleOscillationHalfLife");
      ScaleOscillationFrequency = p.FindPropertyRelative("ScaleOscillationFrequency");
      ScaleOscillationDampingRatio = p.FindPropertyRelative("ScaleOscillationDampingRatio");
    }

    protected override void DrawContent()
    {
      base.DrawContent();

      serializedObject.Update();

      bool useSharedParams = (SharedParams.objectReferenceValue != null);

      if (!m_isReactorField)
      {
        Header("Updates");

        if (m_isBones)
        {
          Property(UpdateMode,
            "Update Mode",
                "Match this mode with how you update your object's transform.\n\n"
              + "Update - Use this mode if you update your object's transform in Update(). This uses variable Time.deltaTime. Use FixedUpdate if physics simulation becomes unstable.\n\n"
              + "Fixed Update - Use this mode if you update your object's transform in FixedUpdate(). This uses fixed Time.fixedDeltaTime. "
              + "Also, use this mode if the game object is affected by Unity physics (i.e. has a Rigidbody), which uses fixed updates."
          );
        }
        else
        {
          Property(UpdateMode, "Update Mode");
        }
      }

      Header("Shared Parameters");
      Property(SharedParams, "Use Asset");


      Header("Position Effects");
      {
        Property(EnablePositionEffect, "Enable");

        if (!useSharedParams)
        {
          Property(PositionParameterMode, "Parameter Mode");

          if (PositionParameterMode.enumValueIndex == (int) ParameterMode.Exponential)
          {
            Property(PositionExponentialHalfLife, "   Exponential Half-Life");
          }
          else if (PositionParameterMode.enumValueIndex == (int) ParameterMode.OscillationByHalfLife)
          {
            Property(PositionOscillationFrequency, "   Oscillation Frequency");
            Property(PositionOscillationHalfLife, "   Oscillation Half-Life");
          }
          else if (PositionParameterMode.enumValueIndex == (int) ParameterMode.OscillationByDampingRatio)
          {
            Property(PositionOscillationFrequency, "   Oscillation Frequency");
            Property(PositionOscillationDampingRatio, "   Oscillation Damping Ratio");
          }

          if (m_isReactor)
          {
            Property(MoveReactionMultiplier, "Move Reaction Multiplier");
            Property(LinearImpulseMultiplier, "Linear Impulse Multiplier");
          }
        }
      }


      Header("Rotation Effects");
      {
        Property(EnableRotationEffect, "Enable");

        if (!useSharedParams)
        {
          Property(RotationParameterMode, "Parameter Mode");

          if (RotationParameterMode.enumValueIndex == (int) ParameterMode.Exponential)
          {
            Property(RotationExponentialHalfLife, "   Exponential Half-Life");
          }
          else if (RotationParameterMode.enumValueIndex == (int) ParameterMode.OscillationByHalfLife)
          {
            Property(RotationOscillationFrequency, "   Oscillation Frequency");
            Property(RotationOscillationHalfLife, "   Oscillation Half-Life");
          }
          else if (RotationParameterMode.enumValueIndex == (int) ParameterMode.OscillationByDampingRatio)
          {
            Property(RotationOscillationFrequency, "   Oscillation Frequency");
            Property(RotationOscillationDampingRatio, "   Oscillation Damping Ratio");
          }

          if (m_isReactor)
          {
            Property(RotationReactionMultiplier, "Rotation Reaction Multiplier");
            Property(AngularImpulseMultiplier, "Angular Impulse Multiplier");
            Property(RotationReactionUp, "Reaction Up");
            Property(GlobalReactionUpVector, "Global Reaction Up");
          }
        }
      }


      if (!m_isReactor &&
          !m_isBones)
      {
        Header("Scale Effects");
        {
          Property(EnableScaleEffect, "Enable");

          if (!useSharedParams)
          {
            Property(ScaleParameterMode, "Parameter Mode");

            if (ScaleParameterMode.enumValueIndex == (int) ParameterMode.Exponential)
            {
              Property(ScaleExponentialHalfLife, "   Exponential Half-Life");
            }
            else if (ScaleParameterMode.enumValueIndex == (int) ParameterMode.OscillationByHalfLife)
            {
              Property(ScaleOscillationFrequency, "   Oscillation Frequency");
              Property(ScaleOscillationHalfLife, "   Oscillation Half-Life");
            }
            else if (ScaleParameterMode.enumValueIndex == (int) ParameterMode.OscillationByDampingRatio)
            {
              Property(ScaleOscillationFrequency, "   Oscillation Frequency");
              Property(ScaleOscillationDampingRatio, "   Oscillation Damping Ratio");
            }

            if (m_isReactor)
            {
              Property(MoveReactionMultiplier, "Move Reaction Multiplier");
              Property(LinearImpulseMultiplier, "Linear Impulse Multiplier");
            }
          }
        }
      }


      if (!m_isReactorField)
      {
        Header("Translational Locks");
        Property(TranslationLockSpace, "Space");
        Property(LockTranslationX, "Lock X Axis");
        Property(LockTranslationY, "Lock Y Axis");
        Property(LockTranslationZ, "Lock Z Axis");

        Header("Rotational Locks");
        Property(RotationLockSpace, "Space");
        Property(LockRotationX, "Lock X Axis");
        Property(LockRotationY, "Lock Y Axis");
        Property(LockRotationZ, "Lock Z Axis");
      }

      if (m_isReactor)
      {
        Header("2D Mode (Effector Response)");
        Property(TwoDDistanceCheck, "2D Distance Check");
        Property(TwoDPositionInfluence, "2D Position Influence");
        Property(TwoDRotationInfluence, "2D Rotation Influence");
        Property(TwoDPlane, "2D Plane");
      }


      serializedObject.ApplyModifiedProperties();
    }
  }
}
