using KryticalNova.UI;
using KryticalNova.UI.Menus;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The menus' comic book: a MegaBook of one leaf whose page turn is the way from one menu screen to the next
    /// (<see cref="MenuBook"/>, <see cref="MenuTransition"/>). MegaBook's scripts are in the project's default
    /// assembly, which this one cannot refer to, so its builder is added and set up by name.
    /// </summary>
    public static partial class FrontEndBuilder
    {
        public const string BookPrefabPath = "Assets/_Game/UI/Menus/Prefabs/MenuBook.prefab";
        public const string BookSettingsPath = "Assets/_Game/UI/Menus/MenuBookSettings.asset";
        private const string MegaBookBuilderType = "MegaBookBuilder, Assembly-CSharp";
        // No renderer features: nothing is drawn over the page, and what is not page stays see-through.
        private const string BookRendererName = "Minimap_Renderer";
        private const string BookLayerName = "UIPreview";
        // A page the shape of a 16:9 screen; any other screen is the same page seen a little wider or narrower.
        private const float PageWidth = 1.6f, PageHeight = 0.9f;

        private static MenuBook bookPrefab;

        /// <summary>
        /// The book prefab and what it needs. An existing prefab is left alone, so the turn tuned on its MegaBook
        /// Builder survives; delete the prefab to have it made again. Without MegaBook in the project nothing is
        /// made and the menus keep their dot transition: nothing is put in MegaBook's place.
        /// </summary>
        private static void EnsureBook()
        {
            bookPrefab = null;
            System.Type builderType = System.Type.GetType(MegaBookBuilderType);
            if (builderType == null)
            {
                Debug.LogError("[FrontEnd] MegaBook is not in the project (no MegaBookBuilder script): the menus keep the dot transition.");
                return;
            }
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(BookPrefabPath);
            if (existing != null && existing.TryGetComponent(out bookPrefab)) return;

            var settings = AssetDatabase.LoadAssetAtPath<MenuBookSettings>(BookSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<MenuBookSettings>();
                AssetDatabase.CreateAsset(settings, BookSettingsPath);
            }
            const string PageShader = "KryticalNova/UI/Comic Page";
            Material face = EnsureMaterial("M_MenuBook_Face", PageShader, m =>
            {
                m.SetFloat("_Picture", 1f);
                m.SetColor("_Ink", Ink);
            });
            Material back = EnsureMaterial("M_MenuBook_Back", PageShader, m =>
            {
                m.SetFloat("_Picture", 0f);
                m.SetColor("_Paper", Paper);
                m.SetColor("_Ink", Ink);
                m.SetColor("_Tint", ShadowIndigo);
            });
            Material edge = EnsureMaterial("M_MenuBook_Edge", PageShader, m =>
            {
                m.SetFloat("_Picture", 0f);
                m.SetColor("_Paper", Ink);
                m.SetColor("_Ink", Ink);
                m.SetFloat("_Rest", 0f);
                m.SetFloat("_Shade", 0f);
                m.SetFloat("_Border", 0f);
            });
            if (face == null || back == null || edge == null) return;

            int layer = LayerMask.NameToLayer(BookLayerName);
            if (layer < 0) layer = 0;
            var root = new GameObject("MenuBook") { layer = layer };
            // MegaBook also runs in edit mode: asleep, it builds no page into the prefab. A copy is woken when a page turns.
            root.SetActive(false);

            var bookGo = new GameObject("Book") { layer = layer };
            bookGo.transform.SetParent(root.transform, false);
            var builder = (MonoBehaviour)bookGo.AddComponent(builderType);
            var so = new SerializedObject(builder);
            so.FindProperty("NumPages").intValue = 1;
            so.FindProperty("pageWidth").floatValue = PageWidth;
            so.FindProperty("pageLength").floatValue = PageHeight;
            so.FindProperty("WidthSegs").intValue = 40;
            so.FindProperty("LengthSegs").intValue = 8;
            so.FindProperty("changespineangle").boolValue = false; // the book lies still; only the leaf moves
            so.FindProperty("Snap").boolValue = false;             // the transition sets every point of the turn
            so.FindProperty("nobackgrounds").boolValue = true;     // the picture is put on the page as it is
            so.FindProperty("dontBuildOnPlay").boolValue = false;  // the page is made when the copy wakes
            so.FindProperty("Flex_Random").boolValue = false;
            so.FindProperty("Flex_RandomDegree").floatValue = 0f;
            so.FindProperty("castshadows").boolValue = false;
            so.FindProperty("receiveshadows").boolValue = false;
            so.FindProperty("pagesectioncrv").animationCurveValue = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            so.FindProperty("basematerial").objectReferenceValue = face;
            so.FindProperty("basematerial1").objectReferenceValue = back;
            so.FindProperty("basematerial2").objectReferenceValue = edge;
            SerializedProperty pages = so.FindProperty("pageparams");
            pages.arraySize = 1;
            SerializedProperty only = pages.GetArrayElementAtIndex(0);
            // A new list element starts from zeroes, not from the class's own defaults: these are the defaults.
            only.FindPropertyRelative("frontmatindex").intValue = -1;
            only.FindPropertyRelative("backmatindex").intValue = -1;
            only.FindPropertyRelative("visobjlow").floatValue = -1f;
            only.FindPropertyRelative("visobjhigh").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();

            var cameraGo = new GameObject("Book Camera") { layer = layer };
            cameraGo.transform.SetParent(root.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.enabled = false; // the book switches it on while a page turns
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            camera.cullingMask = 1 << layer;
            camera.allowHDR = false;
            camera.useOcclusionCulling = false;
            var data = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            int renderer = RendererIndex(BookRendererName);
            if (renderer >= 0) data.SetRenderer(renderer);

            var book = root.AddComponent<MenuBook>();
            SetRef(book, "book", builder);
            SetRef(book, "bookCamera", camera);
            SetRef(book, "settings", settings);
            SetRefs(book, "skins", Skins());

            WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(BookPrefabPath).Replace('\\', '/'));
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, BookPrefabPath);
            Object.DestroyImmediate(root);
            bookPrefab = saved != null ? saved.GetComponent<MenuBook>() : null;
        }

        /// <summary>The two full-screen pictures a page turn is shown with, over everything else of the transition canvas.</summary>
        private static void AddPageTurn(GameObject canvas, MenuTransition transition)
        {
            if (bookPrefab == null) return;
            RawImage still = AddPagePicture(canvas, "Page Still");
            RawImage page = AddPagePicture(canvas, "Page Turn");
            page.raycastTarget = false; // the screen under a turning page is already the player's
            SetRef(transition, "bookPrefab", bookPrefab);
            SetRef(transition, "still", still);
            SetRef(transition, "page", page);
        }

        private static RawImage AddPagePicture(GameObject canvas, string name)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = canvas.layer };
            go.transform.SetParent(canvas.transform, false);
            Stretch((RectTransform)go.transform);
            var picture = go.AddComponent<RawImage>();
            picture.color = Color.white;
            picture.enabled = false;
            return picture;
        }
    }
}
