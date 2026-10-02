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
using UnityEngine;

namespace BoingKit
{
  [CustomEditor(typeof(BoingReactorField))]
  [CanEditMultipleObjects]
  public class BoingReactorFieldEditor : BoingReactorEditor
  {
    private SerializedProperty HardwareMode;

    private SerializedProperty CellSize;
    private SerializedProperty CellMoveMode;
    private SerializedProperty CellsX;
    private SerializedProperty CellsY;
    private SerializedProperty CellsZ;

    private SerializedProperty FalloffMode;
    private SerializedProperty FalloffRatio;
    private SerializedProperty FalloffDimensions;

    private SerializedProperty Effectors;

    private SerializedProperty EnablePropagation;
    private SerializedProperty PositionPropagation;
    private SerializedProperty RotationPropagation;
    private SerializedProperty PropagationDepth;
    private SerializedProperty AnchorPropagationAtBorder;

    public BoingReactorFieldEditor()
    {
      m_isReactorField = true;
    }

    public override void OnEnable()
    {
      HardwareMode = serializedObject.FindProperty("HardwareMode");

      CellMoveMode = serializedObject.FindProperty("CellMoveMode");
      CellsX = serializedObject.FindProperty("CellsX");
      CellsY = serializedObject.FindProperty("CellsY");
      CellsZ = serializedObject.FindProperty("CellsZ");
      CellSize = serializedObject.FindProperty("CellSize");

      FalloffMode = serializedObject.FindProperty("FalloffMode");
      FalloffRatio = serializedObject.FindProperty("FalloffRatio");
      FalloffDimensions = serializedObject.FindProperty("FalloffDimensions");

      Effectors = serializedObject.FindProperty("Effectors");

      EnablePropagation = serializedObject.FindProperty("EnablePropagation");
      PositionPropagation = serializedObject.FindProperty("PositionPropagation");
      RotationPropagation = serializedObject.FindProperty("RotationPropagation");
      PropagationDepth = serializedObject.FindProperty("PropagationDepth");
      AnchorPropagationAtBorder = serializedObject.FindProperty("AnchorPropagationAtBorder");

      base.OnEnable();
    }

    protected override void DrawContent()
    {
      serializedObject.Update();


      Header("Reactor Field");

      Property(HardwareMode, "Hardware Mode");
      Property(CellMoveMode, "Cell Move Mode");

      EditorGUILayout.BeginHorizontal();
      {
        Vector3 cells =
          EditorGUILayout.Vector3Field
          (
            new GUIContent()
            {
              text = "  Cell Count",
              tooltip = "Number of cells in each dimension."
            }, 
            new Vector3(CellsX.intValue, CellsY.intValue, CellsZ.intValue)
          );

        CellsX.intValue = (int) Mathf.Clamp(cells.x, 1, 128);
        CellsY.intValue = (int) Mathf.Clamp(cells.y, 1, 128);
        CellsZ.intValue = (int) Mathf.Clamp(cells.z, 1, 128);
      }
      EditorGUILayout.EndHorizontal();

      Property(CellSize, "Cell Size");
      Property(FalloffMode, "Fall-off Mode");
      Property(FalloffRatio, "Fall-off Ratio");
      Property(FalloffDimensions, "Fall-off Dimensions");

      Header("Effects Propagation");

      Property(EnablePropagation, "Enable");
      Property(PositionPropagation, "Position Propagation");
      Property(RotationPropagation, "Rotation Propagation");
      Property(PropagationDepth, "Depth");
      Property(AnchorPropagationAtBorder, "Anchored Border");

      Space();

      Array(Effectors, "Effectors");


      serializedObject.ApplyModifiedProperties();


      base.DrawContent();


      serializedObject.Update();


      serializedObject.ApplyModifiedProperties();
    }
  }

}
