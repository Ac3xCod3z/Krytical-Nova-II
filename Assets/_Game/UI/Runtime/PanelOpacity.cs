using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// How solid the windows and the panels inside them are, as the player set it on the Interface page
    /// (<see cref="HudPreferences.WindowOpacity"/>, <see cref="HudPreferences.CardOpacity"/>). A style sheet cannot
    /// take one number of a colour from a setting, so each faction skin carries one rule for every step
    /// (FactionSkinBuilder writes them), and this puts the class of the chosen step on every UI document's root,
    /// under the faction's own class on the panel (<see cref="FactionSkin"/>). Tooltips, menus and dialogs are not
    /// windows: they stay solid.
    /// </summary>
    public static class PanelOpacity
    {
        public const int MinPercent = 20;
        public const int MaxPercent = 100;
        public const int StepPercent = 5;
        /// <summary>What the game ships with: a window lets the world through (owner, 11 Oct 2026: 65 % see-through).</summary>
        public const float DefaultWindow = 0.35f;
        public const float DefaultCard = 0.6f;

        private const string WindowPrefix = "kn-win-";
        private const string CardPrefix = "kn-card-";
        private static readonly List<string> Old = new List<string>(2);

        public static string WindowClass(int percent) => WindowPrefix + percent;
        public static string CardClass(int percent) => CardPrefix + percent;

        /// <summary>The step a share (0 to 1) falls on, in percent.</summary>
        public static int Percent(float share) =>
            Mathf.Clamp(Mathf.RoundToInt(share * 100f / StepPercent) * StepPercent, MinPercent, MaxPercent);

        /// <summary>A share (0 to 1) moved onto the nearest step.</summary>
        public static float Snap(float share) => Percent(share) / 100f;

        // After FactionSkin has reset its own event (Enter Play Mode runs without a domain reload).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialise()
        {
            HudPreferences.Changed -= Apply;
            HudPreferences.Changed += Apply;
            FactionSkin.Changed -= Apply;
            FactionSkin.Changed += Apply;
            // The HUD's tree is built again when the HUD comes back: its new root has no step on it yet.
            Hud.HudChrome.KitChanged -= Apply;
            Hud.HudChrome.KitChanged += Apply;
        }

        /// <summary>Puts the chosen steps on every UI document. A newly loaded scene's documents get theirs with the faction skin.</summary>
        public static void Apply()
        {
            string window = WindowClass(Percent(HudPreferences.WindowOpacity));
            string card = CardClass(Percent(HudPreferences.CardOpacity));
            foreach (var document in Object.FindObjectsByType<UIDocument>(FindObjectsInactive.Include))
            {
                VisualElement top = document.rootVisualElement;
                if (top == null) continue;
                if (top.ClassListContains(window) && top.ClassListContains(card)) continue;
                Old.Clear();
                foreach (string name in top.GetClasses())
                    if (name.StartsWith(WindowPrefix) || name.StartsWith(CardPrefix)) Old.Add(name);
                foreach (string name in Old) top.RemoveFromClassList(name);
                top.AddToClassList(window);
                top.AddToClassList(card);
            }
        }
    }
}
