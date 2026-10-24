using System;
using KryticalNova.Core;
using KryticalNova.Factions;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The first-time setup (owner, 24 Oct 2026): the first time the game starts on a PC, on the title and before the
    /// opening plays, three short steps. The photosensitivity warning is an agreement: "I understand and agree" (or
    /// agree with Reduce flashing), kept on this PC so it never comes back by itself, and recorded on the player's
    /// account once they join (<c>PlayerSaveAgent</c>). Then the display, HDR on or off and its brightness, when the
    /// display has HDR; and the colours, a colour-blind correction. Each can be changed later in Settings
    /// (Accessibility and Graphics), and the Accessibility page can show the setup again.
    /// A UI Toolkit document of its own (UI/Settings/Resources/FirstRunSetup.uxml) on its own panel over everything;
    /// it wears the game's skins: the character's faction in the game, the menus' skin before that.
    /// </summary>
    public sealed class FirstRunSetup : MonoBehaviour
    {
        private const string DocumentResource = "FirstRunSetup";
        private const string PanelResource = "FirstRunPanelSettings";
        private static readonly string[] ColourNames = { "OFF", "PROTANOPIA", "DEUTERANOPIA", "TRITANOPIA" };
        private static readonly Color Gold = new Color(1f, 0.82f, 0.32f, 1f);

        private static FirstRunSetup open;

        private Label title, stepLabel, body;
        private VisualElement content, buttons;
        private int step;

        /// <summary>True while the setup is on screen: the title and the opening wait for it.</summary>
        public static bool IsOpen => open != null;

        // Enter Play Mode runs without a domain reload: the last Play's hooks are cleared before any scene loads, or the
        // title's load would open the setup through them as well as through Boot (two on screen).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            open = null;
            SceneManager.sceneLoaded -= OnLoaded;
            AccessibilitySettingsTab.FirstRunRequested -= Show;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            AccessibilitySettingsTab.FirstRunRequested -= Show;
            AccessibilitySettingsTab.FirstRunRequested += Show;
            OnLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        private static void OnLoaded(Scene scene, LoadSceneMode mode)
        {
            // Until the warning is agreed to (and the setup done) it comes up on the title; never again after.
            if (scene.name == FrontEnd.TitleScene && (!AccessibilityPreferences.FirstRunDone || !AccessibilityPreferences.WarningAgreed)) Show();
        }

        /// <summary>Shows the setup from its first step, over whatever screen is up.</summary>
        public static void Show()
        {
            if (open != null) return;
            var tree = Resources.Load<VisualTreeAsset>(DocumentResource);
            var panel = Resources.Load<PanelSettings>(PanelResource);
            if (tree == null || panel == null)
            {
                Debug.LogError($"[FirstRun] Missing Resources/{DocumentResource}.uxml or Resources/{PanelResource}.asset (UI/Settings/Resources).");
                return;
            }
            var go = new GameObject("First-time setup");
            DontDestroyOnLoad(go);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = panel;
            document.visualTreeAsset = tree;
            open = go.AddComponent<FirstRunSetup>();
        }

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            title = root.Q<Label>("first-run-title");
            stepLabel = root.Q<Label>("first-run-step");
            body = root.Q<Label>("first-run-body");
            content = root.Q("first-run-content");
            buttons = root.Q("first-run-buttons");
            WearSkin(root);
            ShowStep(0);
        }

        private void OnDestroy()
        {
            if (open == this) open = null;
        }

        // The character's faction in the game; before there is one, the menus' skin (as MenuSettings wears it).
        private static void WearSkin(VisualElement root)
        {
            VisualElement top = root.panel != null ? root.panel.visualTree : root;
            FactionId skin = FactionSkin.Current != FactionId.None ? FactionSkin.Current : MenuSkin.Current;
            top.EnableInClassList(FactionSkin.HegemonyClass, skin == FactionId.Hegemony);
            top.EnableInClassList(FactionSkin.NullClass, skin == FactionId.Null);
            top.EnableInClassList(FactionSkin.CovenantClass, skin == FactionId.Covenant);
        }

        // ------------------------------------------------------------------ the steps

        private void ShowStep(int next)
        {
            step = next;
            content.Clear();
            buttons.Clear();
            switch (step)
            {
                case 0: Warning(); break;
                case 1:
                    if (!GraphicsSettingsApplier.HdrAvailable) { ShowStep(2); return; }
                    Display();
                    break;
                case 2: Colours(); break;
                default:
                    AccessibilityPreferences.FirstRunDone = true;
                    Destroy(gameObject);
                    return;
            }
            int steps = GraphicsSettingsApplier.HdrAvailable ? 3 : 2;
            int shown = step == 0 ? 1 : (step == 1 || !GraphicsSettingsApplier.HdrAvailable ? 2 : 3);
            stepLabel.text = shown + " / " + steps;
        }

        private void Warning()
        {
            title.text = "PHOTOSENSITIVITY WARNING";
            body.text = "A very small share of people may have a seizure when exposed to some visual images, such as flashing lights " +
                        "or patterns, which this game has: lights that pulse to the music in a fight, flashes on hits and lightning. " +
                        "Even people with no history of seizures or epilepsy can have one.\n\n" +
                        "If you feel dizzy, see things blur or twitch, lose awareness or feel unwell while playing, stop at once and see a " +
                        "doctor.\n\nYou can turn every flashing effect down, or off, now or any time in Settings > Accessibility. " +
                        "By agreeing you confirm you have read and understood this warning.";
            AddButton(buttons, "I UNDERSTAND AND AGREE", () => Agree(false), primary: true);
            AddButton(buttons, "AGREE, REDUCE FLASHING", () => Agree(true));
        }

        // The answer is kept on this PC, so the warning does not come back on every start.
        private void Agree(bool reduce)
        {
            AccessibilityPreferences.WarningAgreedOn = DateTime.UtcNow.ToString("o");
            if (reduce) AccessibilityPreferences.ReduceFlashing = true;
            ShowStep(1);
        }

        private void Display()
        {
            title.text = "DISPLAY: HDR";
            body.text = "Your display can show HDR: brighter highlights and deeper colour. Set the brightness so the middle square is " +
                        "only just lighter than the left one. You can change it later in Settings > Graphics.";
            var value = new Label { text = Line() };
            value.AddToClassList("first-run-value");
            content.Add(value);
            string Line() => (GraphicsPreferences.HdrOutput ? "HDR ON" : "HDR OFF") + "   ·   brightness " + Mathf.RoundToInt(GraphicsPreferences.HdrPaperWhite) + " nits";

            var swatches = Row(content, "first-run-swatches");
            AddSwatch(swatches, new Color(0.06f, 0.06f, 0.06f));
            AddSwatch(swatches, new Color(0.11f, 0.11f, 0.11f));
            AddSwatch(swatches, new Color(0.95f, 0.95f, 0.95f));

            var choices = Row(content, "first-run-choices");
            AddButton(choices, GraphicsPreferences.HdrOutput ? "TURN HDR OFF" : "TURN HDR ON", () =>
            {
                GraphicsPreferences.HdrOutput = !GraphicsPreferences.HdrOutput;
                ShowStep(1);
            });
            AddButton(choices, "DARKER", () => { GraphicsPreferences.HdrPaperWhite -= 20f; value.text = Line(); });
            AddButton(choices, "BRIGHTER", () => { GraphicsPreferences.HdrPaperWhite += 20f; value.text = Line(); });
            AddButton(buttons, "NEXT", () => ShowStep(2), primary: true);
        }

        private void Colours()
        {
            title.text = "COLOURS";
            body.text = "If some colours are hard to tell apart, the game can correct the screen for it. Choose the one that fits, " +
                        "or OFF. You can change it later in Settings > Accessibility.";
            Color[] samples = { new Color(0.9f, 0.2f, 0.2f), new Color(0.25f, 0.8f, 0.3f), new Color(0.25f, 0.45f, 0.95f), Gold, new Color(0.3f, 0.85f, 1f) };
            var swatches = Row(content, "first-run-swatches");
            // The menu is drawn over the picture, where the screen's correction does not reach: corrected here, to show it.
            foreach (Color sample in samples) AddSwatch(swatches, Accessibility.Correct(sample));

            var choices = Row(content, "first-run-choices");
            for (int i = 0; i < ColourNames.Length; i++)
            {
                var mode = (ColourBlindMode)i;
                Button choice = AddButton(choices, ColourNames[i], () =>
                {
                    AccessibilityPreferences.ColourBlind = mode;
                    ShowStep(2);
                });
                choice.EnableInClassList("first-run-chosen", AccessibilityPreferences.ColourBlind == mode);
            }
            AddButton(buttons, "DONE", () => ShowStep(3), primary: true);
        }

        // ------------------------------------------------------------------ parts

        private static VisualElement Row(VisualElement parent, string className)
        {
            var row = new VisualElement();
            row.AddToClassList(className);
            parent.Add(row);
            return row;
        }

        private static void AddSwatch(VisualElement parent, Color colour)
        {
            var swatch = new VisualElement();
            swatch.AddToClassList("first-run-swatch");
            swatch.style.backgroundColor = colour;
            parent.Add(swatch);
        }

        private static Button AddButton(VisualElement parent, string text, Action onClick, bool primary = false)
        {
            var button = new Button(onClick) { text = text };
            if (primary) button.AddToClassList("kn-button-primary");
            parent.Add(button);
            return button;
        }
    }
}
