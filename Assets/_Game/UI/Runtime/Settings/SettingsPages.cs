using System;
using KryticalNova.Player;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The pages of the Settings window: one copy of the logic for both places the window opens, in the game
    /// (<see cref="HudSettingsController"/>) and from character select (MenuSettings). Both show the same markup
    /// (Settings/SettingsWindow.uxml) over the same preference stores, so a value changed in one place is the value
    /// in the other. A setting takes effect as its control is moved; APPLY keeps the changes, REVERT puts back what
    /// was there when the window opened (or was last applied), RESET TO DEFAULTS sets the page shown to its
    /// defaults. What only makes sense with a character in the world (the HUD layout editor) is hidden in the menus.
    /// </summary>
    public sealed class SettingsPages
    {
        public static readonly string[] Pages = { "graphics", "audio", "interface", "gameplay", "controls" };
        /// <summary>Elements of the window that need a character in the world.</summary>
        public const string InGameOnlyClass = "in-game-only";
        private static readonly string[] MenuThemeNames = { "ROTATE", "HEGEMONY", "NULL", "COVENANT" };

        private readonly GraphicsSettingsTab graphicsTab = new GraphicsSettingsTab();
        private readonly AudioSettingsTab audioTab = new AudioSettingsTab();
        private readonly SettingsLedger ledger = new SettingsLedger();
        private ControlsSettingsTab controlsTab;
        private Func<InputActionAsset> playerActions;
        private VisualElement root;
        private Button applyButton, revertButton;
        private string activePage = Pages[0];
        private bool inGame;

        public ControlsSettingsTab Controls => controlsTab;
        public string ActivePage => activePage;
        /// <summary>True while a value differs from what it was when the window opened or was last applied.</summary>
        public bool HasChanges => ledger.HasChanges;

        /// <summary>Raised after APPLY, REVERT or RESET TO DEFAULTS did its work (for feedback).</summary>
        public event Action<string> Acted;

        public void Build(VisualElement windowRoot, Func<InputActionAsset> actions, Action<bool> listeningChanged, bool inTheGame)
        {
            root = windowRoot;
            playerActions = actions;
            inGame = inTheGame;
            if (controlsTab == null) controlsTab = new ControlsSettingsTab(actions, listeningChanged);

            foreach (string page in Pages)
            {
                string name = page;
                Click("settings-tab-" + name, () => Show(name));
            }

            ledger.Clear();
            SettingsLedger.Recording = ledger;
            try
            {
                graphicsTab.Build(root.Q("settings-page-graphics"));
                audioTab.Build(root.Q("settings-page-audio"));
                BuildInterface();
                BuildGameplay(root.Q("settings-page-gameplay"));
            }
            finally
            {
                SettingsLedger.Recording = null;
            }
            // The keys are one entry: a rebind is put back by loading every key as it was.
            ledger.Add(() => KeyBindings.Capture(playerActions()), saved =>
            {
                KeyBindings.Restore(saved, playerActions());
                RebuildControls();
            }, null);
            RebuildControls();

            root.Query(className: InGameOnlyClass).ForEach(element => element.style.display = inGame ? DisplayStyle.Flex : DisplayStyle.None);

            applyButton = root.Q<Button>("settings-apply");
            revertButton = root.Q<Button>("settings-revert");
            Click("settings-apply", Apply);
            Click("settings-revert", Revert);
            Click("settings-reset", ResetPage);
            // The two buttons are live only while there is something to keep or to put back.
            root.schedule.Execute(ShowFooter).Every(200);
            Show(activePage);
            ShowFooter();
        }

        /// <summary>The window was opened: every row shows the stored value, which is what REVERT will put back.</summary>
        public void Opened()
        {
            ledger.ShowAll();
            graphicsTab.Refresh();
            ledger.Mark();
            ShowFooter();
        }

        /// <summary>The player's own actions arrived or changed: the Controls page lists them.</summary>
        public void RebuildControls()
        {
            if (root != null && controlsTab != null && !controlsTab.IsListening) controlsTab.Build(root.Q("settings-page-controls"));
        }

        public void Show(string page)
        {
            if (page != "controls") controlsTab?.Cancel();
            activePage = page;
            foreach (string name in Pages)
            {
                root.Q("settings-page-" + name)?.EnableInClassList("active", name == page);
                root.Q<Button>("settings-tab-" + name)?.EnableInClassList("active", name == page);
            }
        }

        // ------------------------------------------------------------------ apply, revert, reset

        private void Apply()
        {
            ledger.Mark();
            ShowFooter();
            Acted?.Invoke("apply");
        }

        private void Revert()
        {
            controlsTab?.Cancel();
            ledger.Revert();
            graphicsTab.Refresh();
            ShowFooter();
            Acted?.Invoke("revert");
        }

        /// <summary>The page shown goes back to the values the game ships with. REVERT can still undo it.</summary>
        private void ResetPage()
        {
            controlsTab?.Cancel();
            switch (activePage)
            {
                case "graphics": GraphicsPreferences.ResetToDefaults(); break;
                case "audio": AudioPreferences.ResetToDefaults(); break;
                case "interface": HudPreferences.ResetToDefaults(); break;
                case "gameplay":
                    CameraPreferences.ResetToDefaults();
                    HudPreferences.ResetGameplayToDefaults();
                    break;
                case "controls":
                    KeyBindings.ResetAll(playerActions());
                    RebuildControls();
                    break;
            }
            ledger.ShowAll();
            graphicsTab.Refresh();
            ShowFooter();
            Acted?.Invoke("reset");
        }

        private void ShowFooter()
        {
            bool changed = ledger.HasChanges;
            applyButton?.SetEnabled(changed);
            revertButton?.SetEnabled(changed);
        }

        // ------------------------------------------------------------------ the Interface page

        private void BuildInterface()
        {
            Toggle("setting-snap-grid", () => HudPreferences.SnapToGrid, value => HudPreferences.SnapToGrid = value);
            Toggle("setting-move-marker", () => HudPreferences.ShowMoveMarker, value => HudPreferences.ShowMoveMarker = value);
            Toggle("setting-target-ring", () => HudPreferences.ShowTargetRing, value => HudPreferences.ShowTargetRing = value);
            Toggle("setting-nameplates", () => HudPreferences.ShowNameplates, value => HudPreferences.ShowNameplates = value);
            Toggle("setting-own-namecard", () => HudPreferences.ShowOwnNamecard, value => HudPreferences.ShowOwnNamecard = value);
            Toggle("setting-screen-shake", () => HudPreferences.ScreenShake, value => HudPreferences.ScreenShake = value);
            Toggle("setting-controls-hint", () => HudPreferences.ShowControlsHint, value => HudPreferences.ShowControlsHint = value);
            Toggle("setting-pet-chatter", () => HudPreferences.PetChatter, value => HudPreferences.PetChatter = value);
            Toggle("setting-minimap-rotate", () => HudPreferences.MinimapRotates, value => HudPreferences.MinimapRotates = value);

            VisualElement layoutOptions = root.Q("settings-layout-options");
            if (layoutOptions != null)
            {
                layoutOptions.Clear();
                string[] sizes = Array.ConvertAll(HudPreferences.GridSizes, size => size + " PX");
                SettingsRows.Choice(layoutOptions, "Grid size", sizes,
                    () => Math.Max(0, Array.IndexOf(HudPreferences.GridSizes, HudPreferences.GridSize)),
                    index => HudPreferences.GridSize = HudPreferences.GridSizes[index]);
            }
            VisualElement interfaceOptions = root.Q("settings-interface-options");
            if (interfaceOptions != null)
            {
                interfaceOptions.Clear();
                SettingsRows.Slider(interfaceOptions, "Interface size (in the game)", HudPreferences.MinInterfaceScale, HudPreferences.MaxInterfaceScale,
                    () => HudPreferences.InterfaceScale, value => HudPreferences.InterfaceScale = (float)Math.Round(value * 20f) / 20f);
                // The skin of the title screens only: the game itself always wears the character's faction.
                SettingsRows.Choice(interfaceOptions, "Menu theme", MenuThemeNames,
                    () => (int)HudPreferences.MenuTheme, index => HudPreferences.MenuTheme = (MenuTheme)index);
            }
        }

        // ------------------------------------------------------------------ the Gameplay page

        /// <summary>Options of play that are this machine's own business: the camera, what is shown, what is declined.</summary>
        private static void BuildGameplay(VisualElement page)
        {
            if (page == null) return;
            page.Clear();
            SettingsRows.Header(page, "CAMERA");
            SettingsRows.Slider(page, "How fast the camera turns", CameraPreferences.MinSensitivity, CameraPreferences.MaxSensitivity,
                () => CameraPreferences.Sensitivity, value => CameraPreferences.Sensitivity = (float)Math.Round(value * 20f) / 20f);
            SettingsRows.Toggle(page, "Invert the camera's up and down", () => CameraPreferences.InvertVertical, value => CameraPreferences.InvertVertical = value);
            SettingsRows.Slider(page, "How fast the wheel zooms", CameraPreferences.MinZoomSpeed, CameraPreferences.MaxZoomSpeed,
                () => CameraPreferences.ZoomSpeed, value => CameraPreferences.ZoomSpeed = (float)Math.Round(value * 20f) / 20f);
            SettingsRows.Slider(page, "How far the camera pulls back", CameraPreferences.MinFurthest, 1f,
                () => CameraPreferences.Furthest, value => CameraPreferences.Furthest = (float)Math.Round(value * 20f) / 20f);

            SettingsRows.Header(page, "COMBAT");
            SettingsRows.Toggle(page, "Damage numbers", () => HudPreferences.DamageNumbers, value => HudPreferences.DamageNumbers = value);

            SettingsRows.Header(page, "REQUESTS FROM OTHER PLAYERS");
            SettingsRows.Help(page, "A kind of request that is switched on here is declined for you, without being shown.");
            SettingsRows.Toggle(page, "Decline Squad invitations", () => HudPreferences.RefuseSquadInvites, value => HudPreferences.RefuseSquadInvites = value);
            SettingsRows.Toggle(page, "Decline Callouts", () => HudPreferences.RefuseCallouts, value => HudPreferences.RefuseCallouts = value);
            SettingsRows.Toggle(page, "Decline trade requests", () => HudPreferences.RefuseTrades, value => HudPreferences.RefuseTrades = value);
            SettingsRows.Toggle(page, "Decline contact requests", () => HudPreferences.RefuseContacts, value => HudPreferences.RefuseContacts = value);
            SettingsRows.Toggle(page, "Decline Crew invitations", () => HudPreferences.RefuseCrewInvites, value => HudPreferences.RefuseCrewInvites = value);
        }

        private void Toggle(string elementName, Func<bool> get, Action<bool> set)
        {
            Toggle toggle = root.Q<Toggle>(elementName);
            if (toggle != null) SettingsRows.Bind(toggle, get, set);
        }

        private void Click(string elementName, Action callback)
        {
            Button button = root.Q<Button>(elementName);
            if (button != null) button.clicked += callback;
        }
    }
}
