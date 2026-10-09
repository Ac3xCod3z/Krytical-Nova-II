using System;
using System.Collections;
using KryticalNova.CharacterCreation;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Character select: three slots, the chosen character shown live, Enter World, Create (on an empty slot) and
    /// Delete (type the name to confirm). Opens on the character that was played last. Built on Synty's
    /// "Load Game" screen: each save row is a character slot.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSelectController : MonoBehaviour
    {
        private const int VendorRowCount = 6;

        [Tooltip("The Load Game screen instance (Screen_FantasyMenus_Load_Game_01).")]
        [SerializeField] private Transform screen;
        [Tooltip("The confirmation popup instance (Popup_FantasyMenus_Save_Confirmation_01), inactive in the scene.")]
        [SerializeField] private GameObject deletePopup;
        [SerializeField] private CharacterShowroom showroom;
        [SerializeField] private MenuHotkeys hotkeys;

        private readonly MenuRow[] rows = new MenuRow[CharacterRoster.SlotCount];
        private CharacterRoster roster;
        private int selected = -1;
        private Transform enterPrompt, deletePrompt;
        private TMP_InputField deleteInput;
        private Transform deleteConfirmPrompt;

        private void Start()
        {
            // The chosen character does not stand like a statue: it shifts, looks about and greets by itself.
            if (showroom != null) showroom.Ambient = true;
            roster = CharacterSession.Store.Load();
            MenuUi.SetText(screen, "Screen_Header", "Choose Your Character");
            var details = MenuUi.Find(screen, "Save_Details");
            if (details != null) details.gameObject.SetActive(false); // the live character stands there instead

            BindRows();
            BindPrompts();
            BindPopup();

            int initial = roster.IsUsed(FrontEnd.HighlightSlot) ? FrontEnd.HighlightSlot : roster.PreferredSlot;
            FrontEnd.HighlightSlot = -1;
            Select(Mathf.Max(initial, 0));
        }

        private void Update()
        {
            // While the popup is open it owns Enter and Escape (the screen's hotkeys are suspended).
            if (deletePopup == null || !deletePopup.activeSelf || Keyboard.current == null) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame) ClosePopup();
            else if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame) ConfirmDelete();
        }

        // ---------------- Rows ----------------

        private void BindRows()
        {
            var list = MenuUi.Require(screen, "Saved_Games", this);
            if (list == null) return;
            for (int i = 0; i < VendorRowCount; i++)
            {
                var row = MenuUi.Find(list, $"Save_{i + 1:00}");
                if (row == null) continue;
                if (i >= CharacterRoster.SlotCount) { row.gameObject.SetActive(false); continue; }

                int slot = i;
                if (!row.TryGetComponent<MenuRow>(out var menuRow)) menuRow = row.gameObject.AddComponent<MenuRow>();
                menuRow.Clicked += () => Select(slot);
                menuRow.DoubleClicked += Confirm;
                rows[i] = menuRow;
                RefreshRow(i);
            }
        }

        private void RefreshRow(int slot)
        {
            var row = rows[slot] != null ? rows[slot].transform : null;
            if (row == null) return;
            var profile = roster.Get(slot);
            var crest = MenuUi.Find<Image>(row, "SPR_Screenshot");
            var database = showroom != null ? showroom.Database : null;

            // The vendor row has two lines of small labels under the header: (type, date, time) and (game type, game time).
            if (profile == null)
            {
                MenuUi.SetText(row, "Label_Header", "Empty Slot");
                MenuUi.SetLine(row, "Label_Save_Type", "CREATE CHARACTER");
                MenuUi.SetLine(row, "Label_Save_Date", null);
                MenuUi.SetLine(row, "Label_Save_Time", null);
                MenuUi.SetLine(row, "Label_Game_Type", $"Slot {slot + 1}");
                MenuUi.SetLine(row, "Label_Game_Time", null);
                if (crest != null) crest.enabled = false;
                return;
            }

            var lineage = database != null ? database.GetLineage(profile.recipe.lineage) : null;
            var faction = database != null && database.Factions != null ? database.Factions.Get(profile.recipe.faction) : null;
            MenuUi.SetText(row, "Label_Header", profile.name);
            MenuUi.SetLine(row, "Label_Save_Type", faction != null ? faction.displayName.ToUpperInvariant() : "UNALIGNED");
            MenuUi.SetLine(row, "Label_Save_Date", lineage != null ? lineage.displayName : null);
            MenuUi.SetLine(row, "Label_Save_Time", $"Level {profile.level}");
            // A new character is stamped "played" when it is made; it has really been played once that moves on.
            bool played = profile.lastPlayed > profile.created;
            MenuUi.SetLine(row, "Label_Game_Type", played ? $"Played {MenuUi.TimeAgo(profile.lastPlayed)}" : "Not played yet");
            MenuUi.SetLine(row, "Label_Game_Time", null);
            if (crest != null)
            {
                crest.enabled = faction != null && faction.crest != null;
                if (crest.enabled)
                {
                    crest.sprite = faction.crest;
                    crest.color = faction.accentColor;
                    crest.preserveAspect = true;
                }
            }
        }

        private void Select(int slot)
        {
            if (slot < 0 || slot >= CharacterRoster.SlotCount) return;
            if (deletePopup != null && deletePopup.activeSelf) return; // the popup is about the selected character
            if (slot != selected && rows[slot] != null) MenuMoments.Raise(MenuMoment.Toggle, rows[slot].transform);
            selected = slot;
            for (int i = 0; i < rows.Length; i++)
                if (rows[i] != null) rows[i].SetSelected(i == slot);

            var profile = roster.Get(slot);
            if (showroom != null)
            {
                if (profile != null) showroom.Show(profile.recipe, profile.worn); // as it was last played: look and gear
                else showroom.Hide();
            }
            if (enterPrompt != null) MenuUi.SetText(enterPrompt, "Label_Action", profile != null ? "Enter World" : "Create Character");
            if (deletePrompt != null) deletePrompt.gameObject.SetActive(profile != null);
        }

        // ---------------- Actions ----------------

        private void BindPrompts()
        {
            enterPrompt = Prompt("Hotkey_Select", Key.Enter, "Enter World", Confirm);
            deletePrompt = Prompt("Hotkey_Delete_Save", Key.Delete, "Delete Character", AskDelete);
            Prompt("Hotkey_Back", Key.Escape, "Back", Back);
            // The game's Settings, from here: the same window and the same values as in the world.
            var settingsPrompt = MenuUi.Find(screen, SettingsPromptName);
            if (settingsPrompt != null)
            {
                MenuUi.SetText(settingsPrompt, "Label_Action", "Settings");
                MenuUi.MakeClickable(settingsPrompt.gameObject, OpenSettings);
                if (hotkeys != null) hotkeys.Bind(Key.O, OpenSettings, settingsPrompt);
            }
            if (hotkeys == null) return;
            hotkeys.Bind(Key.NumpadEnter, Confirm);
            hotkeys.Bind(Key.UpArrow, () => Select(Mathf.Max(selected - 1, 0)));
            hotkeys.Bind(Key.DownArrow, () => Select(Mathf.Min(selected + 1, CharacterRoster.SlotCount - 1)));
        }

        /// <summary>The footer prompt the scene builder adds for Settings.</summary>
        public const string SettingsPromptName = "Hotkey_Settings";

        private static void OpenSettings()
        {
            MenuSettings settings = MenuSettings.Current;
            if (settings != null && !settings.IsOpen) settings.Open();
        }

        /// <summary>A footer prompt: its keyboard key (glyph and shortcut), its label, and a click that does the same.</summary>
        private Transform Prompt(string promptName, Key key, string label, Action action)
        {
            var prompt = MenuUi.Require(screen, promptName, this);
            if (prompt == null) return null;
            MenuUi.SetText(prompt, "Label_Action", label);
            MenuUi.MakeClickable(prompt.gameObject, () => action());
            if (hotkeys != null) hotkeys.Bind(key, action, prompt);
            return prompt;
        }

        /// <summary>Enter the world with the selected character, or start creating one in the selected empty slot.</summary>
        private void Confirm()
        {
            if (deletePopup != null && deletePopup.activeSelf) return;
            MenuMoments.Raise(MenuMoment.Confirm, rows[selected] != null ? rows[selected].transform : null);
            if (roster.IsUsed(selected))
            {
                CharacterSession.Enter(roster, selected);
                FrontEnd.Go(FrontEnd.LoadingScene);
            }
            else
            {
                FrontEnd.PendingSlot = selected;
                FrontEnd.Go(FrontEnd.FactionSelectScene);
            }
        }

        private void Back()
        {
            MenuMoments.Raise(MenuMoment.Back);
            if (deletePopup != null && deletePopup.activeSelf) { ClosePopup(); return; }
            FrontEnd.Go(FrontEnd.TitleScene);
        }

        // ---------------- Delete (type the name to confirm) ----------------

        private void BindPopup()
        {
            if (deletePopup == null) return;
            var popup = deletePopup.transform;
            deleteInput = popup.GetComponentInChildren<TMP_InputField>(true);
            if (deleteInput != null)
            {
                deleteInput.characterLimit = CharacterNameRules.MaxLength;
                deleteInput.richText = false;
                deleteInput.onValueChanged.AddListener(_ => RefreshDeleteConfirm());
                // The vendor field ships with raycasts off, so it cannot be clicked into.
                if (deleteInput.TryGetComponent<Graphic>(out var hit)) hit.raycastTarget = true;
            }
            deleteConfirmPrompt = MenuUi.Find(popup, "Hotkey_Confirm");
            // The popup reads Enter and Escape itself (Update), so its prompts only show the keys.
            if (deleteConfirmPrompt != null)
            {
                if (hotkeys != null) hotkeys.ShowKey(deleteConfirmPrompt, Key.Enter);
                MenuUi.SetText(deleteConfirmPrompt, "Label_Action", "Delete");
                MenuUi.MakeClickable(deleteConfirmPrompt.gameObject, ConfirmDelete);
            }
            var cancel = MenuUi.Find(popup, "Hotkey_Cancel");
            if (cancel != null)
            {
                if (hotkeys != null) hotkeys.ShowKey(cancel, Key.Escape);
                MenuUi.SetText(cancel, "Label_Action", "Cancel");
                MenuUi.MakeClickable(cancel.gameObject, () =>
                {
                    MenuMoments.Raise(MenuMoment.Back);
                    ClosePopup();
                });
            }
            deletePopup.SetActive(false);
        }

        private void AskDelete()
        {
            var profile = roster.Get(selected);
            if (profile == null || deletePopup == null) return;
            var popup = deletePopup.transform;
            MenuUi.SetText(popup, "Label_Header", "Delete Character");
            MenuUi.SetText(popup, "Label_Save_Type", $"This cannot be undone. Type {profile.name} to delete this character.");
            MenuUi.SetText(popup, "Label_TypeMessageHere", profile.name);
            if (hotkeys != null) hotkeys.Suspended = true;
            deletePopup.SetActive(true);
            MenuMoments.Raise(MenuMoment.Open, popup);
            if (deleteInput != null)
            {
                deleteInput.text = string.Empty;
                deleteInput.Select();
                deleteInput.ActivateInputField();
            }
            RefreshDeleteConfirm();
        }

        private bool DeleteNameMatches()
        {
            var profile = roster.Get(selected);
            return profile != null && deleteInput != null
                   && string.Equals(deleteInput.text.Trim(), profile.name, StringComparison.OrdinalIgnoreCase);
        }

        private void RefreshDeleteConfirm()
        {
            if (deleteConfirmPrompt == null) return;
            if (!deleteConfirmPrompt.TryGetComponent<CanvasGroup>(out var group)) group = deleteConfirmPrompt.gameObject.AddComponent<CanvasGroup>();
            group.alpha = DeleteNameMatches() ? 1f : 0.35f;
        }

        private void ConfirmDelete()
        {
            if (!DeleteNameMatches())
            {
                // The name typed is not this character's: the field says no.
                MenuMoments.Raise(MenuMoment.Error, deleteInput != null ? deleteInput.transform : null);
                return;
            }
            MenuMoments.Raise(MenuMoment.Confirm);
            if (CharacterSession.ActiveSlot == selected) CharacterSession.Clear();
            roster.Clear(selected);
            CharacterSession.Store.Save(roster);
            ClosePopup();
            RefreshRow(selected);
            Select(selected);
        }

        private void ClosePopup()
        {
            if (deletePopup != null) deletePopup.SetActive(false);
            if (hotkeys != null) StartCoroutine(ResumeHotkeys());
        }

        // The screen gets the keyboard back a frame later, so the Enter or Escape that closed the popup does not
        // also enter the world or leave the screen.
        private IEnumerator ResumeHotkeys()
        {
            yield return null;
            hotkeys.Suspended = false;
        }
    }
}
