using UnityEditor;
using UnityEngine;

namespace laplahce.Watermark.Widgets
{
    public sealed class PackageImportButtonWidget : IWidget
    {
        private const float TOP_MARGIN = 6f;
        private const float BOTTOM_MARGIN = 4f;

        private readonly string label;
        private readonly string dialogTitle;
        private readonly string dialogMessage;
        private readonly string packageGuid;
        private readonly string packName;
        private readonly float height;

        public PackageImportButtonWidget(string label, string dialogTitle, string dialogMessage, string packageGuid, string packName, float height = 24f)
        {
            this.label = label;
            this.dialogTitle = dialogTitle;
            this.dialogMessage = dialogMessage;
            this.packageGuid = packageGuid;
            this.packName = packName;
            this.height = height;
        }

        public void Draw(WidgetContext context)
        {
            GUILayout.Space(TOP_MARGIN);

            var rect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true), GUILayout.Height(height));
            var button = new GradientButtonWidget(label, Import, height, usePipelinePalette: true);
            button.DrawAt(context, rect);

            GUILayout.Space(BOTTOM_MARGIN);
        }

        private void Import()
        {
            if (!EditorUtility.DisplayDialog(dialogTitle, dialogMessage, "Continue", "Cancel")) return;

            string path = AssetDatabase.GUIDToAssetPath(packageGuid);
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("Package Not Found",
                    $"Could not locate the requested package.\n\nPlease re-import {packName} from the Package Manager or Asset Store.", "OK");
                return;
            }

            AssetDatabase.ImportPackage(path, false);
        }
    }
}