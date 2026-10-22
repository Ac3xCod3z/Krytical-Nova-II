using System;
using KryticalNova.Economy;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using KryticalNova.Wallet;

namespace KryticalNova.UI
{
    /// <summary>
    /// The HUD's number dialog: Creds to move, a price, how many of a stack. One at a time. The number is only a
    /// request: whatever asked for it sends it to the server, which checks it again.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class AmountDialogController : MonoBehaviour
    {
        private VisualElement root, dialog;
        private Label title, detail, error;
        private TextField field;
        private long min, max;
        private Action<long> confirm;
        // Text mode (a Name Change Pass): the same dialog asks for words; the amount buttons hide.
        private Func<string, string> textCheck;
        private Action<string> textConfirm;
        private bool textMode;
        private int openedFrame = -1;

        /// <summary>The dialog of this HUD, or null.</summary>
        public static AmountDialogController Instance { get; private set; }

        public bool IsOpen => dialog != null && dialog.style.display == DisplayStyle.Flex;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            dialog = root?.Q("amount-dialog");
            if (dialog == null)
            {
                Debug.LogError("[AmountDialog] SocialPanels.uxml is not instanced in this UI document.", this);
                return;
            }
            title = dialog.Q<Label>("amount-title");
            detail = dialog.Q<Label>("amount-detail");
            error = dialog.Q<Label>("amount-error");
            field = dialog.Q<TextField>("amount-field");
            HudTyping.Guard(field);
            field.RegisterValueChangedCallback(_ => error.text = string.Empty);
            Bind("amount-min", () => Set(min));
            Bind("amount-half", () => Set(Math.Max(min, max / 2)));
            Bind("amount-max", () => Set(max));
            Bind("amount-ok", Confirm);
            Bind("amount-cancel", Close);
            dialog.style.display = DisplayStyle.None;
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Asks for a whole number between <paramref name="minimum"/> and <paramref name="maximum"/>.
        /// <paramref name="onConfirm"/> gets the number; nothing is called when the player cancels.
        /// </summary>
        public static void Ask(string heading, string explanation, long minimum, long maximum, long initial, Action<long> onConfirm)
        {
            if (Instance != null) Instance.Open(heading, explanation, minimum, maximum, initial, onConfirm);
        }

        /// <summary>
        /// Asks for a word (a new name): <paramref name="check"/> says what is wrong with it, or null; <paramref name="problem"/>
        /// is shown at once (the server refused the last one).
        /// </summary>
        public static void AskText(string heading, string explanation, string initial, Func<string, string> check, Action<string> onConfirm,
            string problem = null)
        {
            if (Instance == null) return;
            Instance.Open(heading, explanation, 0, 0, 0, null);
            Instance.textMode = true;
            Instance.textCheck = check;
            Instance.textConfirm = onConfirm;
            Instance.ShowAmountButtons(false);
            Instance.field.SetValueWithoutNotify(initial ?? string.Empty);
            Instance.error.text = problem ?? string.Empty;
        }

        private void ShowAmountButtons(bool shown)
        {
            foreach (string name in new[] { "amount-min", "amount-half", "amount-max" })
            {
                Button button = dialog.Q<Button>(name);
                if (button != null) button.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private void Open(string heading, string explanation, long minimum, long maximum, long initial, Action<long> onConfirm)
        {
            textMode = false;
            textCheck = null;
            textConfirm = null;
            ShowAmountButtons(true);
            min = Math.Max(0, minimum);
            max = Math.Max(min, maximum);
            confirm = onConfirm;
            title.text = (heading ?? "AMOUNT").ToUpperInvariant();
            detail.text = string.IsNullOrEmpty(explanation) ? $"Up to {WalletRules.Format(max)}." : explanation;
            error.text = string.Empty;
            Set(Math.Min(max, Math.Max(min, initial)));
            dialog.style.display = DisplayStyle.Flex;
            dialog.BringToFront();
            openedFrame = Time.frameCount;
            field.schedule.Execute(() => { field.Focus(); field.SelectAll(); });
        }

        private void Set(long value) => field.SetValueWithoutNotify(value.ToString());

        private void Update()
        {
            if (!IsOpen || Time.frameCount == openedFrame) return;
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.escapeKey.wasPressedThisFrame) Close();
            else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame) Confirm();
        }

        private void Confirm()
        {
            if (textMode)
            {
                string words = (field.value ?? string.Empty).Trim();
                string wrong = textCheck?.Invoke(words);
                if (wrong != null)
                {
                    error.text = wrong;
                    return;
                }
                Action<string> said = textConfirm;
                Close();
                said?.Invoke(words);
                return;
            }
            string typed = (field.value ?? string.Empty).Replace(",", string.Empty).Replace(" ", string.Empty);
            if (!long.TryParse(typed, out long value))
            {
                error.text = "Type a whole number.";
                return;
            }
            if (value < min || value > max)
            {
                error.text = $"Between {WalletRules.Format(min)} and {WalletRules.Format(max)}.";
                return;
            }
            Action<long> done = confirm;
            Close();
            done?.Invoke(value);
        }

        private void Close()
        {
            confirm = null;
            textConfirm = null;
            textCheck = null;
            if (dialog == null) return;
            dialog.style.display = DisplayStyle.None;
            field.Blur();
            if (GearUIController.Instance != null) GearUIController.Instance.SuppressWorldInput();
        }

        private void Bind(string elementName, Action callback)
        {
            Button button = dialog.Q<Button>(elementName);
            if (button != null) button.clicked += callback;
        }
    }
}
