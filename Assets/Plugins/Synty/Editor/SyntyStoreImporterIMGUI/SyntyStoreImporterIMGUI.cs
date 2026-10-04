using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Synty.Editor
{
    public class SyntyStoreImporterIMGUI : EditorWindow
    {
        private static string EditorDirectoryPath
        {
            get
            {
                return window == null
                    ? null
                    : (editorDirectoryPath ??= Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(window))));
            }
        }

        private const string PATHKEY = "SyntyStoreImporter.Path";

        private static string editorDirectoryPath;
        private static SyntyStoreImporterIMGUI window;

        private Label pathLabel;
        private List<VisualElement> hideIfNoPath = new List<VisualElement>();
        private ScrollView scrollView;
        private FileInfo[] infoPackage, infoImg;
        private ToolbarSearchField toolbarSearchField;
        private PackageElement[] packageElements;
        private List<string> packageSelected = new List<string>();
        private string[] allPackages;

        [MenuItem("Tools/Synty Store Importer")]
        private static void OpenWindow()
        {
            GetWindow<SyntyStoreImporterIMGUI>().Show();
            window.minSize = new Vector2(650, 500);
            window.titleContent = new GUIContent("Synty Store Importer");
            GC.Collect();
        }

        public void CreateGUI()
        {
            window = this;
            VisualElement root = rootVisualElement;

            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{EditorDirectoryPath}/SyntyStoreImporterIMGUI.uxml");
            root.Add(visualTree.Instantiate());

            Button storeButton = root.Q<Button>("StoreButton");
            storeButton.clicked += OpenStore;
            storeButton.style.backgroundImage = new StyleBackground(LoadPNG($"{EditorDirectoryPath}/SyntyPackagesHeader.png"));
            root.Q<Button>("ChooseFolder").clicked += ChooseFolder;

            Button importSelectedButton = root.Q<Button>("ImportSelected");
            importSelectedButton.clicked += ImportSelected;

            Button importAllButton = root.Q<Button>("ImportAll");
            importAllButton.clicked += ImportAll;

            hideIfNoPath.Add(importSelectedButton);
            hideIfNoPath.Add(importAllButton);

            toolbarSearchField = root.Q<ToolbarSearchField>();
            toolbarSearchField.RegisterValueChangedCallback(ShowOnlySearchedItems);
            hideIfNoPath.Add(toolbarSearchField.parent.parent);

            scrollView = root.Q<ScrollView>();

            pathLabel = root.Q<Label>("Path");
            pathLabel.text = "";
            pathLabel.RegisterValueChangedCallback(ToggleHideElements);
            LoadKey();
        }

        private async void LoadKey()
        {
            await System.Threading.Tasks.Task.Delay(100);
            pathLabel.text = EditorPrefs.GetString(PATHKEY, "");
        }

        private void ShowOnlySearchedItems(ChangeEvent<string> evt)
        {
            for (int i = 0; i < packageElements.Length; i++)
            {
                bool containsText = packageElements[i].name.ToUpperInvariant().Contains(evt.newValue.ToUpperInvariant());
                packageElements[i].style.display = containsText ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void LoadPackages()
        {
            string folderName = pathLabel.text;
            DirectoryInfo dir = new DirectoryInfo(folderName + "/");
            infoPackage = dir.GetFiles("*.unitypackage*");
            DirectoryInfo dirImg = new DirectoryInfo(folderName + "/");
            infoImg = dirImg.GetFiles("*.png");

            RenamePackages(infoPackage);
            allPackages = new string[infoPackage.Length];
            packageSelected.Clear();
            scrollView.Clear();
            for (int i = 0; i < infoPackage.Length; i++)
            {
                string name = infoPackage[i].Name;
                bool hasImg = i < infoImg.Length;
                PackageElement child = new PackageElement(name, hasImg ? LoadPNG($"{folderName}/{infoImg[i].Name}") : null, i > 0);
                child.OnToggle += v => ToggleSelection(v, child.name);
                child.OnImportPackage += () => ImportPackage(child.name);

                allPackages[i] = child.name;
                scrollView.Add(child);
            }
            toolbarSearchField.SetValueWithoutNotify("");
            packageElements = scrollView.Children().Cast<PackageElement>().ToArray();
        }

        private void RenamePackages(FileInfo[] infoPackage)
        {
            foreach (FileInfo fileInfo in infoPackage)
            {
                int index = fileInfo.Name.IndexOf("_Unity");
                if (index == -1) continue;
                string nameWithoutVersionning = fileInfo.Name.Substring(0, index) + ".unitypackage";
                fileInfo.MoveTo(fileInfo.Directory.FullName + "\\" + nameWithoutVersionning);
            }
        }

        private void OpenStore()
        {
            Application.OpenURL("https://syntystore.com/");
        }

        private void ChooseFolder()
        {
            pathLabel.text = EditorUtility.OpenFolderPanel("Choose Import Directory", "", "");
        }

        private async void ToggleHideElements(ChangeEvent<string> evt)
        {
            EditorPrefs.SetString(PATHKEY, evt.newValue);
            DisplayStyle hide = evt.newValue == "" ? DisplayStyle.None : DisplayStyle.Flex;
            foreach (VisualElement element in hideIfNoPath) element.style.display = hide;
            await System.Threading.Tasks.Task.Delay(100);
            LoadPackages();
        }

        private void ImportPackage(string name)
        {
            AssetDatabase.ImportPackage($"{pathLabel.text}/{name}", false);
        }

        private void ToggleSelection(bool selected, string name)
        {
            if (selected) packageSelected.Add(name);
            else packageSelected.Remove(name);
        }

        private void ImportSelected()
        {
            if (EditorUtility.DisplayDialog("WARNING!",
                "This will import SELECTED packages including any existing packages in your project.\nThis may be time consuming.",
                "Yes import selected", "Cancel"))
            {
                for (int i = 0; i < packageSelected.Count; i++)
                {
                    string packageName = packageSelected[i];
                    EditorUtility.DisplayProgressBar("Importing Synty Packages", name, i / (float)packageSelected.Count);
                    ImportPackage(packageName);
                }
                EditorUtility.ClearProgressBar();
            }
        }

        private void ImportAll()
        {
            if (EditorUtility.DisplayDialog("WARNING!",
                "This will import ALL packages including any existing packages in your project.\nThis may be time consuming.",
                "Yes import all", "Cancel"))
            {

                for (int i = 0; i < allPackages.Length; i++)
                {
                    string packageName = allPackages[i];
                    EditorUtility.DisplayProgressBar("Importing All Synty Packages", name, i / (float)packageSelected.Count);
                    ImportPackage(packageName);
                }
                EditorUtility.ClearProgressBar();
            }
        }

        public static Texture2D LoadPNG(string filePath)
        {
            Texture2D tex = null;
            byte[] fileData;

            if (File.Exists(filePath))
            {
                fileData = File.ReadAllBytes(filePath);
                tex = new Texture2D(2, 2);
                tex.LoadImage(fileData);
            }
            return tex;
        }

        private void OnFocus()
        {
            if (pathLabel == null || pathLabel.text == "") return;
            LoadPackages();
        }

        private class PackageElement : VisualElement
        {
            public event Action<bool> OnToggle;
            public event Action OnImportPackage;

            private VisualElement separator, icon;
            private Label label;
            public PackageElement(string label, Texture2D icon = null, bool separator = false)
            {
                VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>($"{EditorDirectoryPath}/SyntyStorePackageElement.uxml");
                Add(visualTree.Instantiate());

                this.label = this.Q<Label>();
                this.label.text = label;
                name = label;

                this.icon = this.Q<VisualElement>("Icon");
                this.icon.style.backgroundImage = new StyleBackground(icon);

                this.separator = this.Q<VisualElement>("Separator");
                this.separator.style.display = separator ? DisplayStyle.Flex : DisplayStyle.None;

                this.Q<Toggle>().RegisterValueChangedCallback(v => OnToggle?.Invoke(v.newValue));
                this.Q<Button>().clicked += () => OnImportPackage?.Invoke();
            }
        }
    }
}