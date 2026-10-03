using UnityEngine;
using UnityEditor;
using laplahce.Watermark.Core;
using laplahce.Watermark.Widgets;

namespace laplahce.Watermark.Editor
{
    internal static class CTFX2WelcomeScreen
    {
        private const string REVIEW_URL = "https://assetstore.unity.com/packages/slug/393578#reviews";

        private const string UPGRADE_HDRP_GUID = "251d579c00424d8418044c093157ba6c";
        private const string UPGRADE_URP_GUID = "6b222f645fcaf134f9cb1c03dcaa4630";
        private const string UPGRADE_BUILTIN_GUID = "5d801544251d2a34b95d3ba44c7dbf19";

        private const string TEXTURE_HIGH_GUID = "bf4d6942ff1aa2c428a2480423a61526";
        private const string TEXTURE_MEDIUM_GUID = "f4f8f0efe8fcc9b429f9296cd6e0bed2";
        private const string TEXTURE_LOW_GUID = "e38d9f981910b444e97dd6c6f6ca905c";

        private const string SOURCE_PSD_GUID = "aaf728decb438da439666219a6a26d43";

        private static readonly WatermarkWindowConfig CONFIG = new WatermarkWindowConfig
        {
            PackName = "Cartoon FX Pack v2",
            Slug = "CTFX2",
            PackVersion = "2.0",
            Tagline = "FX pack & creator for any Unity project.",
            DontShowAgainKey = "CTFX2_Watermark_DontShow",
            MenuPath = "Tools/CTFX2/Welcome Screen",
            MenuPriority = 1,
            Size = new Vector2(410f, 520f)
        };

        [MenuItem("Tools/CTFX2/Welcome Screen", priority = 1)]
        private static void OpenFromMenu()
        {
            WatermarkWindowLauncher.Show(CONFIG, Build);
        }

        [InitializeOnLoadMethod]
        private static void ShowOnStartup()
        {
            string sessionKey = CONFIG.Slug + "_ShownThisSession";
            if (SessionState.GetBool(sessionKey, false)) return;
            SessionState.SetBool(sessionKey, true);

            EditorApplication.delayCall += () => WatermarkWindowLauncher.ShowIfNotDismissed(CONFIG, Build);
        }

        private static WatermarkWindow Build(WatermarkWindow window) => window
            .Tab("Home")
                .Section("Thanks for picking up " + CONFIG.Slug)
                .Space(6)
                .Text($"{CONFIG.Slug} brings cartoonish, drag-and-drop FXs in any Unity project. This screen covers setup, render pipeline upgrades and the resources you'll want next.")
                .Space(10)
                .ReviewCard("Enjoying it so far? A review helps a lot.", "★  Rate the Pack", REVIEW_URL)
                .Space(10).Line().Space(10)
                .Section("Recommended First Steps")
                .Space(6)
                .LinkCardToTab("1. Upgrade to your render pipeline", "Go to Setup  →", "Setup", "Match the pack to URP, HDRP or Built-In.")
                .Space(4)
                .LinkCardToTab("2. Pick a texture quality", "Go to Setup  →", "Setup", "Balance fidelity for your platform.")
                .Space(4)
                .LinkCardToTab("3. Watch the 3-minute intro", "Go to Resources  →", "Resources", "Fastest way to see the whole workflow.")
                .Space(10).Line().Space(10)
                .ButtonRow(
                    new GradientButtonWidget("📖  Documentation", () => Application.OpenURL("https://laplahce.com/docs/cartoon-fx-pack-v2")),
                    new GradientButtonWidget("📝  Report a bug", () => Application.OpenURL("https://laplahce.com/contact")),
                    new GradientButtonWidget("💬  Support", () => Application.OpenURL("https://laplahce.com/contact")))
            .Tab("Setup")
                .Section("Render Pipeline")
                .Space(4)
                .Text("Switch the pack's shaders to match your RP. Confirm the target pipeline is already installed before upgrading.")
                .Space(6)
                .ButtonRow(
                    ImportButtonWidget("⬆  HDRP", "Upgrade to HDRP", "This will import the HDRP upgrade package.\nMake sure HDRP is installed in the Package Manager.", UPGRADE_HDRP_GUID),
                    ImportButtonWidget("⬆  URP", "Upgrade to URP", "This will import the URP upgrade package.\nMake sure URP is installed in the Package Manager.", UPGRADE_URP_GUID),
                    ImportButtonWidget("⬆  Built-In", "Upgrade to Built-In", "This will import the Built-In render pipeline upgrade package.", UPGRADE_BUILTIN_GUID))
                .Space(10).Line().Space(10)
                .Section("Texture Quality")
                .Space(4)
                .Text("Swap in a different resolution variant. This overwrites the pack's existing textures.")
                .Space(6)
                .ButtonRow(
                    ImportButtonWidget("⬇  High", "Import High Quality Textures", "This will import the High Quality texture package, overwriting any existing pack textures.", TEXTURE_HIGH_GUID),
                    ImportButtonWidget("⬇  Medium", "Import Medium Quality Textures", "This will import the Medium Quality texture package, overwriting any existing pack textures.", TEXTURE_MEDIUM_GUID),
                    ImportButtonWidget("⬇  Low", "Import Low Quality Textures", "This will import the Low Quality texture package, overwriting any existing pack textures.", TEXTURE_LOW_GUID))
                .Space(10).Line().Space(10)
                .Section("Source Files")
                .Space(4)
                .Text("Optional: import the editable Photoshop (.psd) sources.")
                .Space(6)
                .ImportButton("🖼  Import PSD Source Files", "Import PSD Source Files", "This will import the Photoshop (.psd) source files package.", SOURCE_PSD_GUID)
                .Space(10).Line().Space(10)
                .ButtonRow(
                    new GradientButtonWidget("📖  Documentation", () => Application.OpenURL("https://laplahce.com/docs/cartoon-fx-pack-v2")),
                    new GradientButtonWidget("📝  Report a bug", () => Application.OpenURL("https://laplahce.com/contact")),
                    new GradientButtonWidget("💬  Support", () => Application.OpenURL("https://laplahce.com/contact")))
            .Tab("Resources")
                .Section("Learn the Pack")
                .Space(4)
                .Text("Short videos and written docs, most under 10 minutes.")
                .Space(6)
                .LinkCard("3-minute Tutorial", "Open ↗", "https://youtu.be/fJNnC6ASYgI", "Watch the quick 3-minute tutorial.")
                .Space(4)
                .LinkCard("Installation", "Open ↗", "https://laplahce.com/docs/cartoon-fx-pack-v2/installation", "Make sure you installed everything correctly.")
                .Space(4)
                .LinkCard("Getting Started", "Open ↗", "https://laplahce.com/docs/cartoon-fx-pack-v2/getting-started", "Set up your first effect in under 5 minutes")
                .Space(4)
                .LinkCard("Full Documentation", "Open ↗", "https://laplahce.com/docs/cartoon-fx-pack-v2", "Written guides & documentation")
                .Space(10).Line().Space(10)
                .Section("More of my packages")
                .Space(4)
                .Text("Check out some of my other packages.")
                .Space(6)
                .LinkCard("Obstacle Course Kit", "View →", "https://assetstore.unity.com/packages/slug/293703")
                .Space(4)
                .LinkCard("Cartoon Explosion Pack", "View →", "https://assetstore.unity.com/packages/slug/334986")
                .Space(4)
                .LinkCard("Stylized Trees Pack", "View →", "https://assetstore.unity.com/packages/slug/261728")
                .Space(10).Line().Space(10)
                .ButtonRow(
                    new GradientButtonWidget("📖  Documentation", () => Application.OpenURL("https://laplahce.com/docs/cartoon-fx-pack-v2")),
                    new GradientButtonWidget("📝  Report a bug", () => Application.OpenURL("https://laplahce.com/contact")),
                    new GradientButtonWidget("💬  Support", () => Application.OpenURL("https://laplahce.com/contact")));

        private static PackageImportButtonWidget ImportButtonWidget(string label, string dialogTitle, string dialogMessage, string packageGuid) =>
            new PackageImportButtonWidget(label, dialogTitle, dialogMessage, packageGuid, CONFIG.PackName);
    }
}