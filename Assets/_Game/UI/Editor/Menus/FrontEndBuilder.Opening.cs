using KryticalNova.UI.Menus;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.UI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The game's own look at the door (owner, 16 Oct 2026): the opening (the Ascension Studios crest, then the
    /// Krytical Nova logo, <see cref="OpeningIntro"/>) in place of Unity's splash, the cover painting as the title screen
    /// in every faction skin, and the game's icons (the ring at small sizes, the name logo from 128 px up and as the
    /// default icon). The art is the owner's, from Pictures/Screenshots/Logos &amp; covertitle art.
    /// </summary>
    public static partial class FrontEndBuilder
    {
        public const string MenuArtFolder = "Assets/_Game/UI/Menus/Art";
        public const string StudioLogoPath = MenuArtFolder + "/UI_Opening_StudioLogo.png";
        public const string NameLogoPath = MenuArtFolder + "/UI_Opening_NameLogo.png";
        public const string TitleCoverPath = MenuArtFolder + "/UI_Title_Cover.png";
        public const string TitleShadePath = MenuArtFolder + "/UI_Title_BottomShade.png";
        public const string IconFolder = "Assets/_Game/Release/Icons";
        public const string RingIconPath = IconFolder + "/GameIcon_Ring.png";
        public const string LogoIconPath = IconFolder + "/GameIcon_Logo.png";

        /// <summary>Icons this size and up show the name logo; smaller ones the ring, since the lettering cannot be read there (owner).</summary>
        public const int LogoIconFromSize = 128;

        /// <summary>Where the cover's cut is taken: 1 keeps its top edge, 0.5 cuts evenly. Nearly the top, for the logo.</summary>
        private const float CoverKeepTop = 0.95f;

        /// <summary>The share of the screen the shade under the title's prompt covers, from the bottom.</summary>
        private const float TitleShadeHeight = 0.38f;

        [MenuItem("Krytical Nova/Front End/2. Build Title and Opening")]
        public static void BuildTitleAndOpening()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FrontEnd] Leave Play mode before building the title.");
                return;
            }
            EnsureBrandingArt();
            EnsureMaterials();
            EnsureGlyphs();
            EnsureBook();
            EnsureMenuGlow();
            BuildTitle();
            BuildSceneOrder.Apply();
            ApplyIconsAndSplash();
            AssetDatabase.SaveAssets();
            Debug.Log("[FrontEnd] Title rebuilt with the cover and the opening; icons and splash set.");
        }

        /// <summary>The import settings the art needs: sharp UI sprites, and icons Unity reads at full quality.</summary>
        private static void EnsureBrandingArt()
        {
            foreach (string path in new[] { StudioLogoPath, NameLogoPath, TitleCoverPath, TitleShadePath })
                Import(path, TextureImporterType.Sprite);
            foreach (string path in new[] { RingIconPath, LogoIconPath })
                Import(path, TextureImporterType.Default);
        }

        private static void Import(string path, TextureImporterType type)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                Debug.LogError("[FrontEnd] Missing art: " + path);
                return;
            }
            bool changed = importer.textureType != type || importer.mipmapEnabled || !importer.alphaIsTransparency
                           || importer.maxTextureSize != 2048 || importer.textureCompression != TextureImporterCompression.CompressedHQ
                           || importer.npotScale != TextureImporterNPOTScale.None;
            if (!changed) return;
            importer.textureType = type;
            if (type == TextureImporterType.Sprite) importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// The title's background in the cover painting: the pack's background layers, its crest and its lettering go
        /// (the cover carries the logo), the cover fills the screen without stretching (cut a little at the edges on
        /// screens of another shape), and a soft dark shade at the bottom keeps the prompt readable.
        /// </summary>
        private static void CoverBackground(Transform screen)
        {
            var background = MenuUi.Find(screen, "Background");
            if (background == null) return;
            foreach (Transform layer in background) Hide(layer);
            foreach (string vendor in new[] { "Crest", "Title", "SPR_FX_LensDirt", "Darkenator" }) Hide(MenuUi.Find(screen, vendor));
            // The cover is cut to the screen's shape by overflowing it: clipped here, or it spills past the book's page
            // onto the table round it (owner, 16 Oct 2026: no cover behind the opening's logos).
            if (background.GetComponent<RectMask2D>() == null) background.gameObject.AddComponent<RectMask2D>();

            Sprite cover = AssetDatabase.LoadAssetAtPath<Sprite>(TitleCoverPath);
            var coverImage = Layer(background, "Cover", cover);
            coverImage.preserveAspect = false;
            // The painted logo is at the very top: what the screen's shape cuts off comes from the ground at the bottom.
            coverImage.rectTransform.pivot = new Vector2(0.5f, CoverKeepTop);
            var fitter = coverImage.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = cover != null ? cover.rect.width / cover.rect.height : 1.5f;

            var shade = Layer(background, "Cover Shade", AssetDatabase.LoadAssetAtPath<Sprite>(TitleShadePath));
            var rect = shade.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1f, TitleShadeHeight);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // A picture of our own in the vendor's background, after its (hidden) layers: drawn over them.
        private static Image Layer(Transform parent, string label, Sprite sprite)
        {
            var go = new GameObject(label, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>The opening, once over the title, whichever skin the menus wear.</summary>
        private static void AddOpeningIntro()
        {
            var go = new GameObject("Opening Intro");
            Parent(go, "--- UI ---");
            var intro = go.AddComponent<OpeningIntro>();
            SetRef(intro, "studioLogo", AssetDatabase.LoadAssetAtPath<Sprite>(StudioLogoPath));
            SetRef(intro, "nameLogo", AssetDatabase.LoadAssetAtPath<Sprite>(NameLogoPath));
        }

        /// <summary>
        /// Unity's splash off (optional on every plan since Unity 6): the opening is ours. The game's icons: the name
        /// logo as the default icon and from <see cref="LogoIconFromSize"/> px up, the ring below it.
        /// </summary>
        public static void ApplyIconsAndSplash()
        {
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;

            var ring = AssetDatabase.LoadAssetAtPath<Texture2D>(RingIconPath);
            var logo = AssetDatabase.LoadAssetAtPath<Texture2D>(LogoIconPath);
            if (ring == null || logo == null)
            {
                Debug.LogError("[FrontEnd] The icon pictures are missing in " + IconFolder + ".");
                return;
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { logo }, IconKind.Any);
            int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any);
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < sizes.Length; i++) icons[i] = sizes[i] >= LogoIconFromSize ? logo : ring;
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, icons, IconKind.Any);
            Debug.Log($"[FrontEnd] Icons: the ring below {LogoIconFromSize} px, the name logo from {LogoIconFromSize} px and as the default ({string.Join(", ", sizes)}). Unity's splash is off.");
        }
    }
}
