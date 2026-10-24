using System;
using System.Collections;
using System.Threading.Tasks;
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
    /// Before the server's list is asked for, a player not yet signed in to an account is asked to sign in (online phase 4,
    /// milestone 7; <see cref="UnityAccount"/>): Unity's page opens in the browser, and the account is linked to the player
    /// the game already is, so every character stays. A release game waits for it; the Editor and development builds may
    /// go on without ("Not now").
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
        private Transform enterPrompt, deletePrompt, serversPrompt, backPrompt;
        private TMP_InputField deleteInput;
        private Transform deleteConfirmPrompt;
        // The popup asks either for the name to delete or for a new name (a character that must be renamed).
        private bool renaming;
        // ... or for signing in to an account (milestone 7), or for signing out of it.
        private bool signingIn, signingOut;
        private Transform accountPrompt;
        // "Not now" in the Editor or a development build: not asked again until the game restarts.
        private static bool signInSkipped;
        private bool accountChecked;
        // Making a character waits for a sign-in (or the account check, or the server's list): this slot is made once
        // it has come, unless the account brought a character of its own there (owner, 20 Oct 2026).
        private int resumeSlot = -1;
        // "Not now" to the sign-in asked for by making a character (Editor and test builds): it goes on without, once.
        private bool createWithoutAccount;
        private bool asking;
        // The server whose list could not be had: it is asked again only when the player asks (Enter, or choosing it).
        private string failedKey;
        private const string Header = "Choose Your Character";

        // The server whose characters show first: the one chosen last; in an Editor test session that joins a test
        // server, that server (owner, 24 Oct 2026: a test session must not open on a server left over from another test).
        private static string StartingServer()
        {
            if (EditorTestSession.JoinOnly && EditorTestSession.TryGetPort(out ushort port))
                return "127.0.0.1:" + port;
            return ServerDirectory.LastKeyChosen;
        }

        private void Start()
        {
            // The chosen character does not stand like a statue: it shifts, looks about and greets by itself.
            if (showroom != null) showroom.Ambient = true;
            if (networkManagerPrefab != null) RosterLink.NetworkPrefab = networkManagerPrefab;
            MenuRoster.Use(StartingServer());
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

        // ---------------- Signing in to an account (online phase 4, milestone 7) ----------------

        // Whether the player is linked to an account yet: asked once, before the first list from a server.
        private IEnumerator CheckAccount()
        {
            asking = true;
            MenuUi.SetText(screen, "Screen_Header", "Checking your account...");
            Task<bool> check = UnityAccount.IsLinkedAsync();
            while (!check.IsCompleted) yield return null;
            asking = false;
            accountChecked = true;
            MenuUi.SetText(screen, "Screen_Header", Header);
            ShowAccount();
            if (resumeSlot >= 0) { Resume(); yield break; }
            if (!UnityAccount.KnownLinked && !signInSkipped) AskSignIn();
            else FetchRosterWhenListed();
        }

        // Making a character in the remembered slot goes on, now that what it waited for has come.
        private void Resume()
        {
            int slot = resumeSlot;
            resumeSlot = -1;
            if (slot < 0 || roster.IsUsed(slot)) return; // the account's own character is there: its list is shown
            Select(slot);
            Confirm();
        }

        private bool NeedsSignIn => !UnityAccount.KnownLinked && (UnityAccount.Required || !signInSkipped);

        private void AskSignIn(string why = null)
        {
            if (deletePopup == null || asking) return;
            signingIn = true;
            signingOut = false;
            renaming = false;
            var popup = deletePopup.transform;
            MenuUi.SetText(popup, "Label_Header", "Sign In");
            MenuUi.SetText(popup, "Label_Save_Type", why ?? "Sign in to keep your characters safe and play them on any PC: with an email and a password, " +
                                                   "Google or Apple. Your browser opens Unity's sign-in page; come back here when it says you are done.");
            if (deleteInput != null) deleteInput.gameObject.SetActive(false);
            if (deleteConfirmPrompt != null) MenuUi.SetText(deleteConfirmPrompt, "Label_Action", "Sign In");
            SetCancelLabel(UnityAccount.Required ? "Back" : "Not now");
            if (hotkeys != null) hotkeys.Suspended = true;
            deletePopup.SetActive(true);
            MenuMoments.Raise(MenuMoment.Open, popup);
            RefreshDeleteConfirm();
        }

        private IEnumerator DoSignIn()
        {
            asking = true;
            var popup = deletePopup.transform;
            MenuUi.SetText(popup, "Label_Save_Type", "Waiting for the sign-in page in your browser...");
            RefreshDeleteConfirm();
            Task<string> signIn = UnityAccount.SignInWithBrowserAsync();
            while (!signIn.IsCompleted) yield return null;
            asking = false;
            string problem = signIn.IsFaulted ? signIn.Exception?.GetBaseException().Message : signIn.Result;
            if (problem != null)
            {
                MenuUi.SetText(popup, "Label_Save_Type", problem);
                MenuMoments.Raise(MenuMoment.Error, popup);
                RefreshDeleteConfirm();
                yield break;
            }
            MenuMoments.Raise(MenuMoment.Confirm);
            GameWindow.BringToFront(); // back in front of the browser's "DONE!" page
            ClosePopup();
            ShowAccount();
            // Always the signed-in account's own list, asked for fresh.
            failedKey = null;
            MenuRoster.Forget();
            ReloadRoster();
            FetchRosterWhenListed();
        }

        // Cancel in the sign-in popup: a release game goes back to the title; the Editor or a test build goes on without.
        private void CancelSignIn()
        {
            ClosePopup();
            if (UnityAccount.Required) { resumeSlot = -1; FrontEnd.Go(FrontEnd.TitleScene); return; }
            signInSkipped = true;
            if (resumeSlot >= 0) { createWithoutAccount = true; Resume(); return; }
            FetchRosterWhenListed();
        }

        private void SetCancelLabel(string label)
        {
            var cancel = deletePopup != null ? MenuUi.Find(deletePopup.transform, "Hotkey_Cancel") : null;
            if (cancel != null) MenuUi.SetText(cancel, "Label_Action", label);
        }

        // ---------------- The server's roster (online phase 4, milestone 5) ----------------

        // Asks the chosen server for its list once it is in the server list (this PC's copy shows meanwhile).
        private void FetchRosterWhenListed()
        {
            if (asking || UnityAccount.Changing || !MenuRoster.OnServer || MenuRoster.Fetched || MenuRoster.Listing == null || failedKey == MenuRoster.Key) return;
            if (!accountChecked) { StartCoroutine(CheckAccount()); return; }
            if (NeedsSignIn) { AskSignIn(); return; }
            StartCoroutine(FetchRoster());
        }

        private IEnumerator FetchRoster()
        {
            asking = true;
            MenuUi.SetText(screen, "Screen_Header", $"Asking {MenuRoster.ServerName} for your characters...");
            string problem = null, note = null;
            string player = UnityAccount.PlayerNow;
            yield return MenuRoster.Fetch((why, said) => { problem = why; note = said; });
            asking = false;
            if (problem == RosterLink.AccountChanged)
            {
                // Asked again for the player signed in now (never a list kept for another account).
                MenuUi.SetText(screen, "Screen_Header", Header);
                StartCoroutine(RetryFetchNextFrame());
                yield break;
            }
            Debug.Log(problem == null
                ? $"[Roster] {MenuRoster.ServerName} listed {CharacterSession.Store.Load().Count} character(s) for player {player}."
                : $"[Roster] {MenuRoster.ServerName} did not list the characters of player {player}: {problem}");
            failedKey = problem != null ? MenuRoster.Key : null;
            MenuUi.SetText(screen, "Screen_Header", Header);
            ReloadRoster();
            if (problem != null) resumeSlot = -1;
            if (problem != null) OpenServers($"Could not get your characters from {MenuRoster.ServerName}: {problem}");
            else if (note != null) OpenServers(note);
            else if (resumeSlot >= 0) Resume();
        }

        private IEnumerator RetryFetchNextFrame()
        {
            yield return null;
            FetchRosterWhenListed();
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
            if (Keyboard.current.escapeKey.wasPressedThisFrame) CancelPopup();
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
            if (drawer != null) drawer.Want(deletePrompt, profile != null);
            else if (deletePrompt != null) deletePrompt.gameObject.SetActive(profile != null);
        }

        // ---------------- Actions ----------------

        private void BindPrompts()
        {
            enterPrompt = Prompt("Hotkey_Select", Key.Enter, "Enter World", Confirm);
            deletePrompt = Prompt("Hotkey_Delete_Save", Key.Delete, "Delete Character", AskDelete);
            backPrompt = Prompt("Hotkey_Back", Key.Escape, "Back", Back);
            // The game's Settings, from here: the same window and the same values as in the world.
            var settingsPrompt = MenuUi.Find(screen, SettingsPromptName);
            if (settingsPrompt != null)
            {
                MenuUi.SetText(settingsPrompt, "Label_Action", "Settings");
                MenuUi.MakeClickable(settingsPrompt.gameObject, OpenSettings);
                if (hotkeys != null) hotkeys.Bind(Key.O, OpenSettings, settingsPrompt);
            }
            BindServersPrompt(settingsPrompt);
            BindAccountPrompt(settingsPrompt);
            // The menu button never stops the screen from starting: without it every prompt simply stays in the row.
            try { BindMenuButton(settingsPrompt); }
            catch (Exception e) { Debug.LogException(e, this); }
            if (hotkeys == null) return;
            hotkeys.Bind(Key.NumpadEnter, Confirm);
            hotkeys.Bind(Key.UpArrow, () => Select(Mathf.Max(selected - 1, 0)));
            hotkeys.Bind(Key.DownArrow, () => Select(Mathf.Min(selected + 1, CharacterRoster.SlotCount - 1)));
        }

        /// <summary>The footer prompt the scene builder adds for Settings.</summary>
        public const string SettingsPromptName = "Hotkey_Settings";

        /// <summary>The footer prompt for the Servers window: a copy of the Settings prompt, made here.</summary>
        public const string ServersPromptName = "Hotkey_Servers";

        /// <summary>The footer's menu button the scene builder adds: the skin's icon in the pack's icon button.</summary>
        public const string MenuButtonName = "Button_Menu";

        /// <summary>The footer prompt that holds the menu button and shows its key: a copy of the Settings prompt.</summary>
        public const string MenuPromptName = "Hotkey_Menu";

        private MenuDrawer drawer;

        // The whole footer folds into one prompt, the menu icon with its key (Tab): pressing the key or clicking it
        // springs every prompt out (Enter World, Delete, Server, Account, Settings, Back), and again folds them away
        // (owner, 20 Oct 2026). Every prompt's own key works either way.
        private void BindMenuButton(Transform settingsPrompt)
        {
            var button = MenuUi.Find(screen, MenuButtonName);
            if (button == null || settingsPrompt == null) return;
            var prompt = MenuUi.Find(screen, MenuPromptName);
            if (prompt == null)
            {
                var copy = Instantiate(settingsPrompt.gameObject, settingsPrompt.parent);
                copy.name = MenuPromptName;
                prompt = copy.transform;
            }
            prompt.SetAsLastSibling(); // at the right end of the row, where Back was: the prompts spring out to its left
            // The row keeps the button where it is while the prompts grow out of it.
            if (prompt.parent.TryGetComponent(out HorizontalLayoutGroup row)) row.childAlignment = TextAnchor.MiddleRight;
            var label = MenuUi.Find(prompt, "Label_Action");
            if (label != null) label.gameObject.SetActive(false); // the icon says it
            button.SetParent(prompt, false);
            button.SetAsLastSibling(); // after the key cap
            var menu = prompt.TryGetComponent(out MenuDrawer had) ? had : prompt.gameObject.AddComponent<MenuDrawer>();
            // The packs' icon button is a switch (a Toggle): it is on while the prompts are out.
            var toggle = button.GetComponent<Toggle>();
            Action flip = () =>
            {
                menu.Toggle();
                if (toggle != null) toggle.SetIsOnWithoutNotify(menu.IsOpen);
            };
            if (toggle != null)
            {
                toggle.group = null;
                toggle.SetIsOnWithoutNotify(false);
                toggle.onValueChanged.RemoveAllListeners();
                toggle.onValueChanged.AddListener(_ => flip());
            }
            else MenuUi.MakeClickable(button.gameObject, () => flip());
            MenuUi.MakeClickable(prompt.gameObject, () => flip()); // the key cap too
            bool canDelete = deletePrompt != null && deletePrompt.gameObject.activeSelf;
            menu.Hold(new[] { backPrompt, settingsPrompt, accountPrompt, serversPrompt, deletePrompt, enterPrompt }); // nearest first
            menu.Want(deletePrompt, canDelete);
            menu.Changed += () => { if (toggle != null) toggle.SetIsOnWithoutNotify(menu.IsOpen); };
            if (hotkeys != null) hotkeys.Bind(Key.Tab, flip, prompt);
            drawer = menu;
        }

        /// <summary>The footer prompt for the account (sign in, or sign out to use another): a copy of the Settings prompt.</summary>
        public const string AccountPromptName = "Hotkey_Account";

        private void BindAccountPrompt(Transform settingsPrompt)
        {
            if (settingsPrompt == null) return;
            accountPrompt = MenuUi.Find(screen, AccountPromptName);
            if (accountPrompt == null)
            {
                var copy = Instantiate(settingsPrompt.gameObject, settingsPrompt.parent);
                copy.name = AccountPromptName;
                copy.transform.SetSiblingIndex(settingsPrompt.GetSiblingIndex()); // before Settings
                accountPrompt = copy.transform;
            }
            MenuUi.MakeClickable(accountPrompt.gameObject, AskAccount);
            if (hotkeys != null) hotkeys.Bind(Key.A, AskAccount, accountPrompt);
            ShowAccount();
        }

        // The prompt says what it does now: sign in, or the account to sign out of.
        private void ShowAccount()
        {
            if (accountPrompt != null) MenuUi.SetText(accountPrompt, "Label_Action", UnityAccount.KnownLinked ? "Account" : "Sign In");
        }

        private void AskAccount()
        {
            if (deletePopup != null && deletePopup.activeSelf) return;
            if (asking) return;
            if (!UnityAccount.KnownLinked) { signInSkipped = false; AskSignIn(); return; }
            signingOut = true;
            signingIn = false;
            renaming = false;
            var popup = deletePopup.transform;
            MenuUi.SetText(popup, "Label_Header", "Account");
            MenuUi.SetText(popup, "Label_Save_Type", "You are signed in. Sign out to play with another email, Google or Apple account on this PC; " +
                                                   "your characters stay with this account and come back when you sign in to it again.");
            if (deleteInput != null) deleteInput.gameObject.SetActive(false);
            if (deleteConfirmPrompt != null) MenuUi.SetText(deleteConfirmPrompt, "Label_Action", "Sign Out");
            SetCancelLabel("Cancel");
            if (hotkeys != null) hotkeys.Suspended = true;
            deletePopup.SetActive(true);
            MenuMoments.Raise(MenuMoment.Open, popup);
            RefreshDeleteConfirm();
        }

        private void ConfirmSignOut()
        {
            UnityAccount.SignOut();
            signingOut = false;
            signInSkipped = false;
            MenuRoster.Forget(); // the next account's characters are asked for once it is signed in
            if (CharacterSession.ActiveSlot >= 0) CharacterSession.Clear();
            // The list of the account signed out is not shown to whoever signs in next: it comes back with its account.
            if (MenuRoster.OnServer) CharacterSession.Store.Save(new CharacterRoster());
            ReloadRoster();
            MenuMoments.Raise(MenuMoment.Confirm);
            ClosePopup();
            ShowAccount();
            StartCoroutine(SignInNextFrame());
        }

        // The popup has closed: it opens again, now asking for the next account.
        private IEnumerator SignInNextFrame()
        {
            yield return null;
            yield return null;
            AskSignIn();
        }

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
            // Characters live on the server under the account: a player not signed in signs in first, and making a
            // character always asks (owner, 20 Oct 2026); once signed in, the creator opens for the slot chosen.
            bool creating = !roster.IsUsed(selected);
            if (MenuRoster.OnServer && !UnityAccount.KnownLinked)
            {
                bool goOn = creating && createWithoutAccount;
                createWithoutAccount = false;
                if (!accountChecked) { resumeSlot = creating ? selected : -1; StartCoroutine(CheckAccount()); return; }
                if (NeedsSignIn || (creating && !goOn))
                {
                    resumeSlot = creating ? selected : -1;
                    AskSignIn(creating ? "Sign in to make a character: it is kept with your account on the server, safe and " +
                                         "ready on any PC. With an email and a password, Google or Apple; your browser opens " +
                                         "Unity's sign-in page. Come back here when it says you are done." : null);
                    return;
                }
            }
            // A new character on a server waits for the server's list: the slot may hold one already.
            if (creating && MenuRoster.OnServer && !MenuRoster.Fetched && !string.IsNullOrEmpty(MenuRoster.Key))
            {
                resumeSlot = selected;
                failedKey = null;
                FetchRosterWhenListed();
                return;
            }
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
            if (deletePopup != null && deletePopup.activeSelf) { CancelPopup(); return; }
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
                    CancelPopup();
                });
            }
            deletePopup.SetActive(false);
        }

        private void AskDelete()
        {
            var profile = roster.Get(selected);
            if (profile == null || deletePopup == null || asking) return;
            renaming = false;
            signingIn = signingOut = false;
            ShowFieldAndCancel();
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
            bool ready = signingIn || signingOut || (renaming ? NewNameValid(out _, out _) : DeleteNameMatches());
            group.alpha = ready && !asking ? 1f : 0.35f;
        }

        private void ConfirmPopup()
        {
            if (asking) return;
            if (signingIn) StartCoroutine(DoSignIn());
            else if (signingOut) ConfirmSignOut();
            else if (renaming) ConfirmRename();
            else ConfirmDelete();
        }

        private void CancelPopup()
        {
            if (asking) return;
            if (signingIn) CancelSignIn();
            else { signingOut = false; ClosePopup(); }
        }

        // The delete and rename popups type a name and can always be cancelled.
        private void ShowFieldAndCancel()
        {
            if (deleteInput != null) deleteInput.gameObject.SetActive(true);
            SetCancelLabel("Cancel");
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
            signingIn = signingOut = false;
            ShowFieldAndCancel();
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
