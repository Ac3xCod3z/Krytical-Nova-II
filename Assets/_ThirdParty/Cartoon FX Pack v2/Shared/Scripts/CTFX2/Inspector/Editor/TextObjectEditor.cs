using UnityEngine;
using UnityEditor;
using CTFX2.Creator.Objects;
using PSD.Creator.Objects;

namespace PSD.Creator.Editor.Inspector.Editors
{
    [CustomEditor(typeof(TextObject))]
    public class TextObjectEditor : ParticleObjectEditor
    {
        private int charMapIndex = 0;
        private string charMapInput = "";
        private bool mappingActive = false;

        public override void OnInspectorGUI()
        {
            if (target == null) return;
            var obj = (TextObject)target;

            EditorGUI.BeginChangeCheck();

            serializedObject.Update();
            base.OnInspectorGUI();

            EditorGUILayout.Space(10);
            EditorGUILayout.Separator();

            if (!mappingActive)
            {
                if (!obj.IsInitialized())
                {
                    EditorGUILayout.HelpBox("Character map is not initialized. Please generate the character map by clicking the button below.", MessageType.Error);
                }

                if (GUILayout.Button("Generate Character Map"))
                {
                    if (obj.IsInitialized())
                    {
                        int choice = EditorUtility.DisplayDialogComplex(
                            "Character Map Exists",
                            "A character map already exists. What would you like to do?",
                            "Generate New Map",
                            "Add to Existing",
                            "Cancel"
                        );
                        if (choice == 0)
                        {
                            obj.ClearCharacterMap();
                            EditorUtility.SetDirty(obj);
                        }
                        else if (choice == 2)
                            return;
                    }
                    // Start mapping session
                    charMapIndex = 0;
                    charMapInput = "";
                    mappingActive = true;
                }
            }
            else
                GenerateCharacterMap(obj);

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
            }
        }

        private bool shouldFocusInput = false;

        private void GenerateCharacterMap(TextObject obj)
        {
            Texture2D fontTex = obj.albedo;
            Vector2 tiling = obj.flipbookSize;
            int cols = Mathf.RoundToInt(tiling.x);
            int rows = Mathf.RoundToInt(tiling.y);
            int totalSlices = cols * rows;

            if (charMapIndex < totalSlices)
            {
                int sliceIndex = charMapIndex;
                int col = sliceIndex % cols;
                int row = sliceIndex / cols;
                float x = (float)col / cols;
                float y = 1f - ((float)row + 1) / rows;
                float w = 1f / cols;
                float h = 1f / rows;

                EditorGUILayout.LabelField($"Slice {sliceIndex + 1}/{totalSlices} (col {col}, row {row})");
                if (fontTex != null && tiling.x > 0 && tiling.y > 0)
                {
                    Rect texRect = GUILayoutUtility.GetRect(64, 64, GUILayout.ExpandWidth(false));
                    GUI.DrawTextureWithTexCoords(texRect, fontTex, new Rect(x, y, w, h));
                }
                else
                {
                    EditorGUILayout.HelpBox("Font texture or tiling not set.", MessageType.Warning);
                }

                // Handle Enter key for Save & Next
                var e = Event.current;
                bool enterPressed = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
                GUI.SetNextControlName("CharInputField");
                charMapInput = EditorGUILayout.TextField("Character", charMapInput);
                // Ensure focus and selection if requested
                if (Event.current.type == EventType.Repaint && shouldFocusInput)
                {
                    EditorGUI.FocusTextInControl("CharInputField");
                    shouldFocusInput = false;
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Back"))
                {
                    if (charMapIndex > 0)
                    {
                        charMapIndex--;
                        charMapInput = "";
                        shouldFocusInput = true;
                    }
                }
                bool saveAndNext = GUILayout.Button("Save & Next") || enterPressed;
                if (saveAndNext)
                {
                    if (!string.IsNullOrEmpty(charMapInput) && charMapInput.Length == 1)
                    {
                        obj.SetCharacterMap(charMapInput[0], sliceIndex);
                        charMapIndex++;
                        charMapInput = "";
                        EditorUtility.SetDirty(obj);
                        shouldFocusInput = true;
                        if (enterPressed)
                        {
                            e.Use();
                        }
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Invalid Input", "Please enter a single character.", "OK");
                        charMapInput = "";
                        shouldFocusInput = true;
                    }
                }

                if (GUILayout.Button("Skip"))
                {
                    charMapIndex++;
                    charMapInput = "";
                }
                if (GUILayout.Button("Cancel"))
                {
                    mappingActive = false;
                    charMapInput = "";
                    charMapIndex = 0;
                }
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("All slices have been mapped.", MessageType.Info);
                if (GUILayout.Button("Finish"))
                {
                    mappingActive = false;
                    charMapInput = "";
                    charMapIndex = 0;
                }
            }
        }
    }
}