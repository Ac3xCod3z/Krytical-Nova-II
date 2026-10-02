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
  [CustomEditor(typeof(SharedBoingParams))]
  [CanEditMultipleObjects]
  public class SharedBoingParamsEditor : BoingEditorBase
  {
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

    public virtual void OnEnable()
    {
      var p = serializedObject.FindProperty("Params");

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

    public override void OnInspectorGUI()
    {
      serializedObject.Update();


      Header("Position Effects");
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

        Property(MoveReactionMultiplier, "Move Reaction Multiplier");
        Property(LinearImpulseMultiplier, "Linear Impulse Multiplier");
      }


      Header("Rotation Effects");
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

        Property(RotationReactionMultiplier, "Rotation Reaction Multiplier");
        Property(AngularImpulseMultiplier, "Angular Impulse Multiplier");
        Property(RotationReactionUp, "Reaction Up");
      }


      Header("Scale Effects");
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
      }


      serializedObject.ApplyModifiedProperties();
    }
  }
}
