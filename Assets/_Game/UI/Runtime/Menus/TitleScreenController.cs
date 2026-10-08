using KryticalNova.CharacterCreation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Title screen: any key or click continues. A player with at least one character goes to character select
    /// (which opens on the one played last); a new player goes straight to choosing a faction. Escape (or a click on
    /// the "Exit Game" line) is the way out: a popup asks first, Enter exits and Escape stays.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleScreenController : MonoBehaviour
    {
        /// <summary>The "Esc  Exit Game" line the scene builder puts under the title's prompt.</summary>
        public const string ExitHintName = "Label_Exit";

        [Tooltip("Ignore input for this long, so the key that started the game does not skip the title.")]
        [SerializeField, Min(0f)] private float inputDelay = 0.8f;
        [Tooltip("The Title screen instance (Screen_FantasyMenus_Title_01).")]
        [SerializeField] private Transform screen;
        [Tooltip("The confirmation popup instance (Popup_FantasyMenus_Save_Confirmation_01). Asks before the game closes.")]
        [SerializeField] private GameObject exitPopup;
        [Tooltip("Puts keyboard glyphs on the popup's prompts.")]
        [SerializeField] private MenuHotkeys hotkeys;

        private float readyAt;
        private bool leaving;
        private RectTransform exitHint;

        private bool Asking => exitPopup != null && exitPopup.activeSelf;

        private void OnEnable() => readyAt = Time.unscaledTime + inputDelay;

        private void Start()
        {
            exitHint = screen != null ? MenuUi.Find(screen, ExitHintName) as RectTransform : null;
            BindPopup();
        }

        private void Update()
        {
            if (leaving || Time.unscaledTime < readyAt) return;
            var transition = MenuTransition.Current;
            if (transition != null && transition.IsBusy) return;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            // While the popup is open it owns Enter and Escape; its two prompts can be clicked.
            if (Asking)
            {
                if (keyboard == null) return;
                if (keyboard.escapeKey.wasPressedThisFrame) CloseExit();
                else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Exit();
                return;
            }

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                AskExit();
                return;
            }
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            // The canvas is a screen-space overlay: its rectangles are in screen pixels, with no camera between.
            if (clicked && exitHint != null && RectTransformUtility.RectangleContainsScreenPoint(exitHint, mouse.position.ReadValue(), null))
            {
                AskExit();
                return;
            }

            bool pressed = (keyboard != null && keyboard.anyKey.wasPressedThisFrame) || clicked
                           || (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame
                                                           || Gamepad.current.startButton.wasPressedThisFrame));
            if (pressed) Continue();
        }

        private void Continue()
        {
            leaving = true;
            MenuMoments.Raise(MenuMoment.Confirm);
            var roster = CharacterSession.Store.Load();
            if (roster.Count > 0)
            {
                FrontEnd.HighlightSlot = roster.PreferredSlot;
                FrontEnd.Go(FrontEnd.CharacterSelectScene);
            }
            else
            {
                FrontEnd.PendingSlot = roster.FirstFree;
                FrontEnd.Go(FrontEnd.FactionSelectScene);
            }
        }

        // ---------------- Exit ----------------

        private void BindPopup()
        {
            if (exitPopup == null) return;
            var popup = exitPopup.transform;
            MenuUi.SetText(popup, "Label_Header", "Exit Game");
            MenuUi.SetText(popup, "Label_Save_Type", "Close Krytical Nova II and return to the desktop?");
            // The vendor popup asks for a save name: there is nothing to type here.
            var input = popup.GetComponentInChildren<TMP_InputField>(true);
            if (input != null) input.gameObject.SetActive(false);

            // The popup reads Enter and Escape itself (Update), so its prompts only show the keys.
            var confirm = MenuUi.Find(popup, "Hotkey_Confirm");
            if (confirm != null)
            {
                if (hotkeys != null) hotkeys.ShowKey(confirm, Key.Enter);
                MenuUi.SetText(confirm, "Label_Action", "Exit");
                MenuUi.MakeClickable(confirm.gameObject, Exit);
            }
            var cancel = MenuUi.Find(popup, "Hotkey_Cancel");
            if (cancel != null)
            {
                if (hotkeys != null) hotkeys.ShowKey(cancel, Key.Escape);
                MenuUi.SetText(cancel, "Label_Action", "Stay");
                MenuUi.MakeClickable(cancel.gameObject, CloseExit);
            }
            exitPopup.SetActive(false);
        }

        private void AskExit()
        {
            // A title scene built before the popup existed still has a way out.
            if (exitPopup == null)
            {
                Exit();
                return;
            }
            exitPopup.SetActive(true);
            MenuMoments.Raise(MenuMoment.Open, exitPopup.transform);
        }

        private void CloseExit()
        {
            if (exitPopup != null) exitPopup.SetActive(false);
            MenuMoments.Raise(MenuMoment.Back);
            // The key or click that closed it must not also count as "continue".
            readyAt = Time.unscaledTime + 0.15f;
        }

        private void Exit()
        {
            leaving = true;
            MenuMoments.Raise(MenuMoment.Confirm);
            FrontEnd.Quit();
        }
    }
}
