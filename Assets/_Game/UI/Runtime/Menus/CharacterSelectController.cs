using System;
using System.Collections;
using KryticalNova.CharacterCreation;
using KryticalNova.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Character select: three slots, the chosen character shown live, Enter World, Create (on an empty slot) and
    /// Delete (type the name to confirm). Opens on the character that was played last. Built on Synty's
    /// "Load Game" screen: each save row is a character slot. Enter World joins the server played last (online step 4);
    /// SERVERS opens the Servers window (<see cref="MenuServers"/>) to choose another.
    /// On a server the characters are the server's (online phase 4, milestone 5; <see cref="MenuRoster"/>): the list is
    /// asked for when the screen opens or another server is chosen, and Delete and Rename go to the server. A character
    /// that lost its name when names became unique asks for a new one before it enters.
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
        [Tooltip("The worlds' NetworkManager prefab: questions about characters go to a server through a copy of it.")]
        [SerializeField] private GameObject networkManagerPrefab;

        private readonly MenuRow[] rows = new MenuRow[CharacterRoster.SlotCount];
        private CharacterRoster roster;
        private int selected = -1;
        private Transform enterPrompt, deletePrompt, serversPrompt;
        private TMP_InputField deleteInput;
        private Transform deleteConfirmPrompt;
        // The popup asks either for the name to delete or for a new name (a character that must be renamed).
        private bool renaming;
        private bool asking;
        // The server whose list could not be had: it is asked again only when the player asks (Enter, or choosing it).
        private string failedKey;
        private const string Header = "Choose Your Character";

        private void Start()
        {
            // The chosen character does not stand like a statue: it shifts, looks about and greets by itself.
            if (showroom != null) showroom.Ambient = true;
            if (networkManagerPrefab != null) RosterLink.NetworkPrefab = networkManagerPrefab;
            MenuRoster.Use(ServerDirectory.LastKeyChosen);
            roster = CharacterSession.Store.Load();
            MenuUi.SetText(screen, "Screen_Header", Header);
            var details = MenuUi.Find(screen, "Save_Details");
            if (details != null) details.gameObject.SetActive(false); // the live character stands there instead

            BindRows();
            BindPrompts();
            BindPopup();

            int initial = roster.IsUsed(FrontEnd.HighlightSlot) ? FrontEnd.HighlightSlot : roster.PreferredSlot;
            FrontEnd.HighlightSlot = -1;
            Select(Mathf.Max(initial, 0));

            ServerDirectory.Changed += ShowServer;
            ServerDirectory.Refresh();
            // Back from a join that failed: the Servers window says why.
            string failure = JoinFlow.LastFailure;
            JoinFlow.LastFailure = null;
            if (!string.IsNullOrEmpty(failure)) StartCoroutine(OpenServersNextFrame(failure));
            else FetchRosterWhenListed();
        }

        // ---------------- The server's roster (online phase 4, milestone 5) ----------------

        // Asks the chosen server for its list once it is in the server list (this PC's copy shows meanwhile).
        private void FetchRosterWhenListed()
        {
            if (asking || !MenuRoster.OnServer || MenuRoster.Fetched || MenuRoster.Listing == null || failedKey == MenuRoster.Key) return;
            StartCoroutine(FetchRoster());
        }

        private IEnumerator FetchRoster()
        {
            asking = true;
            MenuUi.SetText(screen, "Screen_Header", $"Asking {MenuRoster.ServerName} for your characters...");
            string problem = null, note = null;
            yield return MenuRoster.Fetch((why, said) => { problem = why; note = said; });
            asking = false;
            failedKey = problem != null ? MenuRoster.Key : null;
            MenuUi.SetText(screen, "Screen_Header", Header);
            ReloadRoster();
            if (problem != null) OpenServers($"Could not get your characters from {MenuRoster.ServerName}: {problem}");
            else if (note != null) OpenServers(note);
        }

        // The roster as the store has it now (after an answer, or another server chosen).
        private void ReloadRoster()
        {
            roster = CharacterSession.Store.Load();
            for (int i = 0; i < rows.Length; i++) RefreshRow(i);
            int keep = roster.IsUsed(selected) ? selected : roster.PreferredSlot;
            selected = -1;
            Select(Mathf.Max(keep, 0));
        }

        private void OnDestroy() => ServerDirectory.Changed -= ShowServer;

        private void Update()
        {
            // While the popup is open it owns Enter and Escape (the screen's hotkeys are suspended).
            if (deletePopup == null || !deletePopup.activeSelf || Keyboard.current == null) return;
            if (Keyboard.current.escapeKey.wasPressedThisFrame) ClosePopup();
            else if (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame) ConfirmPopup();
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
            MenuUi.SetLine(row, "Label_Game_Type", profile.mustRename ? "NEW NAME NEEDED"
                : played ? $"Played {MenuUi.TimeAgo(profile.lastPlayed)}" : "Not played yet");
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
            BindServersPrompt(settingsPrompt);
            if (hotkeys == null) return;
            hotkeys.Bind(Key.NumpadEnter, Confirm);
            hotkeys.Bind(Key.UpArrow, () => Select(Mathf.Max(selected - 1, 0)));
            hotkeys.Bind(Key.DownArrow, () => Select(Mathf.Min(selected + 1, CharacterRoster.SlotCount - 1)));
        }

        /// <summary>The footer prompt the scene builder adds for Settings.</summary>
        public const string SettingsPromptName = "Hotkey_Settings";

        /// <summary>The footer prompt for the Servers window: a copy of the Settings prompt, made here.</summary>
        public const string ServersPromptName = "Hotkey_Servers";

        // ---------------- Servers (online step 4) ----------------

        private void BindServersPrompt(Transform settingsPrompt)
        {
            if (settingsPrompt == null) return;
            serversPrompt = MenuUi.Find(screen, ServersPromptName);
            if (serversPrompt == null)
            {
                var copy = Instantiate(settingsPrompt.gameObject, settingsPrompt.parent);
                copy.name = ServersPromptName;
                copy.transform.SetSiblingIndex(settingsPrompt.GetSiblingIndex()); // before Settings
                serversPrompt = copy.transform;
            }
            MenuUi.MakeClickable(serversPrompt.gameObject, () => OpenServers(null));
            if (hotkeys != null) hotkeys.Bind(Key.S, () => OpenServers(null), serversPrompt);
            ShowServer();
        }

        // The prompt names the server Enter joins.
        private void ShowServer()
        {
            if (serversPrompt == null) return;
            ServerEntry last = ServerDirectory.Last;
            MenuUi.SetText(serversPrompt, "Label_Action", last != null ? "Server: " + last.Name : "Servers");
            FetchRosterWhenListed();
        }

        private IEnumerator OpenServersNextFrame(string note)
        {
            yield return null; // the menus' document is laid out
            OpenServers(note);
        }

        /// <summary>Opens the Servers window; JOIN there enters the world with the selected character on that server.</summary>
        private void OpenServers(string note)
        {
            MenuServers servers = MenuServers.For(MenuSettings.Current);
            if (servers == null) return;
            servers.Open(EnterWith, note); // the window makes its own opening sound
        }

        private void EnterWith(ServerEntry entry)
        {
            // Another server keeps other characters: show its roster and let the player choose there.
            failedKey = null; // chosen again: asked again
            if (entry.Key != MenuRoster.Key && (MenuRoster.OnServer || MenuRoster.IsServerKey(entry.Key)))
            {
                MenuRoster.Use(entry.Key);
                ReloadRoster();
                ShowServer();
                return;
            }
            ShowServer();
            if (!roster.IsUsed(selected)) return; // chosen from an empty slot: remembered for the next Enter
            if (!CanEnter()) return;
            JoinFlow.Begin(entry.PlayHere ? null : entry.Listing);
            CharacterSession.Enter(roster, selected);
            FrontEnd.Go(FrontEnd.LoadingScene);
        }

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
            if (asking) return; // the server's answer is on its way
            MenuMoments.Raise(MenuMoment.Confirm, rows[selected] != null ? rows[selected].transform : null);
            // A release game that never chose a server: characters live on one, so it is chosen first.
            if (string.IsNullOrEmpty(MenuRoster.Key))
            {
                OpenServers("Choose a server first: your characters are kept there.");
                return;
            }
            if (roster.IsUsed(selected) && !CanEnter()) return;
            if (roster.IsUsed(selected))
            {
                // Enter joins the server played last; with none yet, or one that cannot be joined now, the list opens.
                ServerEntry last = ServerDirectory.Last;
                if (last == null || !last.CanJoin) OpenServers(last == null ? null
                    : last.Status == ServerEntry.State.Off ? $"{last.Name} is not answering. It may be off, or not reachable from here." : last.Note);
                else EnterWith(last);
            }
            else
            {
                FrontEnd.PendingSlot = selected;
                FrontEnd.Go(FrontEnd.FactionSelectScene);
            }
        }

        // On a server: its list has come, and the character keeps its name. False after saying what to do.
        private bool CanEnter()
        {
            if (!MenuRoster.OnServer) return true;
            if (!MenuRoster.Fetched)
            {
                failedKey = null; // the player asks again
                FetchRosterWhenListed();
                return false;
            }
            var profile = roster.Get(selected);
            if (profile != null && profile.mustRename)
            {
                AskRename();
                return false;
            }
            return true;
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
                MenuUi.MakeClickable(deleteConfirmPrompt.gameObject, ConfirmPopup);
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
            if (profile == null || deletePopup == null || asking) return;
            renaming = false;
            var popup = deletePopup.transform;
            if (deleteConfirmPrompt != null) MenuUi.SetText(deleteConfirmPrompt, "Label_Action", "Delete");
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
            bool ready = renaming ? NewNameValid(out _, out _) : DeleteNameMatches();
            group.alpha = ready && !asking ? 1f : 0.35f;
        }

        private void ConfirmPopup()
        {
            if (asking) return;
            if (renaming) ConfirmRename();
            else ConfirmDelete();
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
            var profile = roster.Get(selected);
            if (MenuRoster.OnServer && profile != null)
            {
                StartCoroutine(AskServer(MenuRoster.Delete(profile.id, (reply, problem) => Answered(problem)), "Deleting"));
                return;
            }
            if (CharacterSession.ActiveSlot == selected) CharacterSession.Clear();
            roster.Clear(selected);
            CharacterSession.Store.Save(roster);
            ClosePopup();
            RefreshRow(selected);
            Select(selected);
        }

        // ---------------- Rename (a character whose name another took first) ----------------

        private void AskRename()
        {
            var profile = roster.Get(selected);
            if (profile == null || deletePopup == null || asking) return;
            renaming = true;
            var popup = deletePopup.transform;
            MenuUi.SetText(popup, "Label_Header", "Choose a New Name");
            MenuUi.SetText(popup, "Label_Save_Type", $"{CharacterNameRules.ReasonMustRename} ({profile.name})");
            MenuUi.SetText(popup, "Label_TypeMessageHere", "New name");
            if (deleteConfirmPrompt != null) MenuUi.SetText(deleteConfirmPrompt, "Label_Action", "Rename");
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

        private bool NewNameValid(out string name, out string reason)
        {
            name = null;
            reason = CharacterNameRules.ReasonEmpty;
            return deleteInput != null && CharacterNameRules.Validate(deleteInput.text, out name, out reason);
        }

        private void ConfirmRename()
        {
            var profile = roster.Get(selected);
            if (profile == null) { ClosePopup(); return; }
            if (!NewNameValid(out string name, out string reason))
            {
                MenuUi.SetText(deletePopup.transform, "Label_Save_Type", reason);
                MenuMoments.Raise(MenuMoment.Error, deleteInput != null ? deleteInput.transform : null);
                return;
            }
            MenuMoments.Raise(MenuMoment.Confirm);
            StartCoroutine(AskServer(MenuRoster.Rename(profile.id, name, (reply, problem) => Answered(problem)), "Renaming"));
        }

        // ---------------- Asking the server from the popup ----------------

        private string answerProblem;

        private void Answered(string problem) => answerProblem = problem;

        // Runs a request while the popup shows that it waits; a refusal is said in the popup, a success closes it.
        private IEnumerator AskServer(IEnumerator request, string doing)
        {
            asking = true;
            answerProblem = null;
            var popup = deletePopup.transform;
            MenuUi.SetText(popup, "Label_Save_Type", $"{doing}: asking {MenuRoster.ServerName}...");
            RefreshDeleteConfirm();
            yield return request;
            asking = false;
            if (answerProblem != null)
            {
                MenuUi.SetText(popup, "Label_Save_Type", answerProblem);
                MenuMoments.Raise(MenuMoment.Error, deleteInput != null ? deleteInput.transform : null);
                RefreshDeleteConfirm();
                yield break;
            }
            if (CharacterSession.ActiveSlot == selected) CharacterSession.Clear();
            ClosePopup();
            ReloadRoster();
        }

        private void ClosePopup()
        {
            if (asking) return; // the server's answer decides
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
