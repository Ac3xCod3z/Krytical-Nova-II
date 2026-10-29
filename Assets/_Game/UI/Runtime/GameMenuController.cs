using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using NetworkConnectionManager = KryticalNova.Networking.NetworkConnectionManager;
using KryticalNova.Persistence;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.UI.Menus;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The game menu: Resume, Settings, Guide, Log Out and Exit Game. Esc opens it when nothing else has a use for the key
    /// (an open window, a dialogue, a target, a journey: those take the Esc first, as they always did), and so does
    /// the MENU button. Log Out goes back to character select; Exit Game closes the application. Both count down
    /// first and can be cancelled until the count ends. Then the character is saved, the session is closed (a host
    /// is told who that disconnects), and what belongs to the session is cleared away so the world can be entered
    /// again in the same run. A player whose session ends under them (the server or host closed, they were sent away,
    /// the server was full, the connection dropped) is told why when the server said, and offered the same two ways
    /// out, or to stay where they stand.
    /// </summary>
    [DefaultExecutionOrder(-500)] // before the other HUD controllers: on the same Esc they still see what was open
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameMenuController : MonoBehaviour
    {
        private enum View { None, Menu, Leaving, Working, Ended }

        private const string WindowClass = "window";
        private const string LeavingNowClass = "leaving-now";
        private const float OwnDisconnectSeconds = 5f;  // a session that ends this soon after the player asked is no surprise
        private const float DarkenSeconds = 0.35f;      // the veil goes dark before anything is torn down (GameMenu.uss)
        // Things that are not windows but close on Esc too.
        private static readonly string[] OtherEscapeTakers = { "npc-dialogue", "item-menu", "item-confirm", "player-menu", "amount-dialog" };

        [Tooltip("Seconds counted down before Log Out or Exit Game happens. Cancel stops it.")]
        [SerializeField, Min(0f)] private float leaveSeconds = 5f;
        [Tooltip("Give up waiting for the session to close after this long and leave anyway.")]
        [SerializeField, Min(1f)] private float shutdownTimeoutSeconds = 6f;

        private VisualElement root, boundRoot, veil, menu, leaving, ended;
        private Label who, leavingTitle, leavingWho, count, leavingNote, endedNote;
        private Button cancel;
        private readonly List<VisualElement> escapeTakers = new List<VisualElement>(32);
        private HudSettingsController settings;
        private View view = View.None;
        private bool initialized, warned, exiting, wasInSession, suppressedHotbar;
        private float countdownEnds;
        private int shownCount = -1;

        /// <summary>True while the menu, the count or the session-ended panel is up.</summary>
        public bool IsOpen => view != View.None;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            initialized = false;
            warned = false;
        }

        /// <summary>
        /// Finds the menu in the HUD's document. Not done in OnEnable: this component runs before the document does
        /// (the execution order above), and the document only builds its tree in its own OnEnable.
        /// </summary>
        private void Bind()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            veil = root?.Q("game-menu-veil");
            if (veil == null)
            {
                if (root != null && !warned) Debug.LogError("[GameMenu] GameMenu.uxml is not instanced in this UI document.", this);
                warned = root != null;
                return;
            }
            // The same tree again (only this component was switched off and on): everything is still hooked up.
            if (boundRoot == root)
            {
                initialized = true;
                Show(View.None);
                return;
            }
            boundRoot = root;
            menu = veil.Q("game-menu");
            leaving = veil.Q("game-menu-leaving");
            ended = veil.Q("game-menu-ended");
            who = veil.Q<Label>("game-menu-who");
            leavingTitle = veil.Q<Label>("game-menu-leaving-title");
            leavingWho = veil.Q<Label>("game-menu-leaving-who");
            count = veil.Q<Label>("game-menu-count");
            leavingNote = veil.Q<Label>("game-menu-leaving-note");
            endedNote = veil.Q<Label>("game-menu-ended-note");
            cancel = veil.Q<Button>("game-menu-cancel");
            settings = GetComponent<HudSettingsController>();

            veil.Q<Button>("game-menu-resume").clicked += Close;
            veil.Q<Button>("game-menu-settings").clicked += OpenSettings;
            veil.Q<Button>("game-menu-guide").clicked += OpenGuide;
            veil.Q<Button>("game-menu-logout").clicked += () => BeginLeaving(false);
            veil.Q<Button>("game-menu-exit").clicked += () => BeginLeaving(true);
            cancel.clicked += CancelLeaving;
            veil.Q<Button>("game-menu-ended-select").clicked += () => BeginLeaving(false);
            veil.Q<Button>("game-menu-ended-exit").clicked += () => BeginLeaving(true);
            veil.Q<Button>("game-menu-ended-stay").clicked += Close;
            Button open = root.Q<Button>("open-menu");
            if (open != null) open.clicked += Toggle;

            escapeTakers.Clear();
            root.Query(className: WindowClass).ForEach(escapeTakers.Add);
            foreach (string name in OtherEscapeTakers)
            {
                VisualElement element = root.Q(name);
                if (element != null) escapeTakers.Add(element);
            }

            initialized = true;
            Show(View.None);
        }

        private void OnDisable()
        {
            if (!initialized) return;
            initialized = false;
            SuppressHotbar(false);
        }

        // ------------------------------------------------------------------ every frame

        private void Update()
        {
            if (!initialized)
            {
                if (Application.isPlaying) Bind();
                if (!initialized) return;
            }
            WatchSession();
            var keyboard = Keyboard.current;
            bool escape = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
            switch (view)
            {
                case View.None:
                    if (escape && !HudTyping.IsTyping(root) && !SomethingElseTakesEscape()) Show(View.Menu);
                    break;
                case View.Menu:
                    // A window opened by its own key takes over: the menu was only in the way.
                    if (escape || AnyWindowShowing()) Close();
                    break;
                case View.Ended:
                    if (escape) Close();
                    break;
                case View.Leaving:
                    if (escape) CancelLeaving();
                    else TickCountdown();
                    break;
            }
        }

        /// <summary>A player who was in someone's session and no longer is, without having asked to leave, is told so.</summary>
        private void WatchSession()
        {
            bool inSession = NetSession.IsConnectedClient && !NetSession.IsServer;
            if (wasInSession && !inSession && view != View.Working && view != View.Leaving && !LeftByChoice())
            {
                // The server's own words when it gave any (a GM sent them away, a shutdown, a full server).
                string reason = network != null ? network.DisconnectReason : null;
                endedNote.text = string.IsNullOrWhiteSpace(reason)
                    ? "The server closed, or the connection was lost. Your progress is kept on the server."
                    : reason + " Your progress is kept on the server.";
                Show(View.Ended);
            }
            wasInSession = inSession;
        }

        private static bool LeftByChoice()
        {
            NetworkConnectionManager connection = NetworkConnectionManager.Instance;
            return connection != null && Time.unscaledTime - connection.LastDisconnectRequest < OwnDisconnectSeconds;
        }

        // ------------------------------------------------------------------ what else wants the Esc

        private bool SomethingElseTakesEscape()
        {
            if (AnyShowing(escapeTakers)) return true;
            if (HudLayoutEditor.Current != null && HudLayoutEditor.Current.IsEditing) return true;
            // The character: Esc drops a target and stops a journey before it does anything else.
            PlayerInventory local = PlayerInventory.Local;
            return local != null && local.TryGetComponent(out PointAndClickPlayer player)
                && (player.SelectedTarget != null || player.IsTravelling);
        }

        private bool AnyWindowShowing()
        {
            foreach (VisualElement element in escapeTakers)
                if (element.ClassListContains(WindowClass) && Showing(element)) return true;
            return false;
        }

        private static bool AnyShowing(List<VisualElement> elements)
        {
            foreach (VisualElement element in elements)
                if (Showing(element)) return true;
            return false;
        }

        /// <summary>Drawn right now: neither it nor anything it sits in is switched off.</summary>
        private static bool Showing(VisualElement element)
        {
            for (VisualElement e = element; e != null; e = e.parent)
                if (e.resolvedStyle.display == DisplayStyle.None || !e.visible) return false;
            return element.panel != null;
        }

        // ------------------------------------------------------------------ the menu

        public void Toggle()
        {
            if (!initialized) return;
            if (view == View.None) Show(View.Menu);
            else if (view == View.Menu) Close();
        }

        public void Close()
        {
            if (view == View.Menu || view == View.Ended) Show(View.None);
        }

        private void OpenSettings()
        {
            Show(View.None);
            if (settings != null) settings.OpenSettings();
        }

        private void OpenGuide()
        {
            Show(View.None);
            if (settings != null) settings.OpenGuide();
        }

        private void Show(View next)
        {
            view = next;
            veil.style.display = next == View.None ? DisplayStyle.None : DisplayStyle.Flex;
            menu.style.display = next == View.Menu ? DisplayStyle.Flex : DisplayStyle.None;
            leaving.style.display = next == View.Leaving || next == View.Working ? DisplayStyle.Flex : DisplayStyle.None;
            ended.style.display = next == View.Ended ? DisplayStyle.Flex : DisplayStyle.None;
            veil.EnableInClassList(LeavingNowClass, next == View.Working);
            if (next != View.None)
            {
                veil.parent?.BringToFront();
                GearUIController.Instance?.SuppressWorldInput();
            }
            if (next == View.Menu) who.text = WhoLine();
            SuppressHotbar(next != View.None);
        }

        /// <summary>Skill keys do nothing while the menu has the screen.</summary>
        private void SuppressHotbar(bool suppress)
        {
            if (suppress == suppressedHotbar) return;
            PlayerInventory local = PlayerInventory.Local;
            if (local != null && local.TryGetComponent(out PlayerSkillHotbar hotbar)) hotbar.InputSuppressed = suppress;
            suppressedHotbar = suppress;
        }

        private static string CharacterName()
        {
            CharacterProfile profile = CharacterSession.Active;
            return profile != null && !string.IsNullOrEmpty(profile.name) ? profile.name : null;
        }

        private static string WhoLine()
        {
            string name = CharacterName();
            string session = !NetSession.IsRunning ? "not in a session"
                : NetSession.IsHost ? "hosting" : NetSession.IsServer ? "running a server" : "in a session";
            return name != null ? $"{name}  ·  {session}" : session;
        }

        // ------------------------------------------------------------------ leaving

        private bool leavingForced; // a count that cannot be called off (a new name)

        /// <summary>
        /// A renamed character goes back to character select after a count the player cannot call off, so the server
        /// knows them by the new name when they come back in (owner, 22 Oct 2026).
        /// </summary>
        public static void LeaveAfterRename(string newName, float seconds)
        {
            GameMenuController menu = FindAnyObjectByType<GameMenuController>();
            if (menu != null) menu.BeginForcedLeave("NEW NAME", newName,
                $"You are {newName} now. For the server to know you by your new name you go back to character select, saved first.", seconds);
        }

        private void BeginForcedLeave(string heading, string who, string note, float seconds)
        {
            if (view == View.Working) return;
            exiting = false;
            leavingForced = true;
            countdownEnds = Time.unscaledTime + Mathf.Max(0f, seconds);
            shownCount = -1;
            leavingTitle.text = heading;
            leavingWho.text = who ?? string.Empty;
            leavingNote.text = note ?? string.Empty;
            cancel.SetEnabled(false);
            cancel.style.display = DisplayStyle.None;
            Show(View.Leaving);
            TickCountdown();
        }

        private void BeginLeaving(bool exit)
        {
            leavingForced = false;
            exiting = exit;
            countdownEnds = Time.unscaledTime + leaveSeconds;
            shownCount = -1;
            leavingTitle.text = exit ? "EXITING GAME" : "LOGGING OUT";
            leavingWho.text = CharacterName() ?? string.Empty;
            leavingNote.text = LeavingNote(exit);
            cancel.SetEnabled(true);
            cancel.style.display = DisplayStyle.Flex;
            Show(View.Leaving);
            TickCountdown();
        }

        /// <summary>What leaving does: to the others, when this player is hosting them; otherwise to the player.</summary>
        private static string LeavingNote(bool exit)
        {
            string where = exit ? "The game closes." : "You return to character select.";
            if (NetSession.IsServer)
            {
                int others = NetSession.PlayerCount - (NetSession.IsHost ? 1 : 0);
                if (others > 0)
                    return $"You are hosting. {others} other player{(others == 1 ? "" : "s")} will be disconnected. Everyone is saved first. {where}";
                return $"You are hosting: the session ends when you leave. Your character is saved first. {where}";
            }
            return $"Your character is saved first. {where}";
        }

        private void CancelLeaving()
        {
            if (view != View.Leaving || leavingForced) return;
            Show(View.Menu);
        }

        private void TickCountdown()
        {
            float left = countdownEnds - Time.unscaledTime;
            int whole = Mathf.Max(0, Mathf.CeilToInt(left));
            if (whole != shownCount)
            {
                shownCount = whole;
                count.text = whole.ToString();
            }
            if (left <= 0f) StartCoroutine(Leave(exiting));
        }

        /// <summary>
        /// The count has ended: save, close the session, clear away what belonged to it, and go. Nothing here can be
        /// cancelled, so the panel says so.
        /// </summary>
        private IEnumerator Leave(bool exit)
        {
            Show(View.Working);
            leavingTitle.text = "SAVING";
            count.text = "...";
            cancel.style.display = DisplayStyle.None;
            // The screen is dark before anything visible is torn down.
            float darkAt = Time.unscaledTime + DarkenSeconds;
            while (Time.unscaledTime < darkAt) yield return null;

            // Where this machine decides the game (a host, or a game with no session) everything is written now.
            // A client's character is written by the server as the client leaves.
            ServerSave.FlushAll();

            NetworkManager network = NetworkManager.Singleton;
            NetworkConnectionManager connection = NetworkConnectionManager.Instance;
            if (network != null && (network.IsListening || network.ShutdownInProgress))
            {
                leavingTitle.text = "CLOSING SESSION";
                Task disconnect = connection != null ? connection.DisconnectAsync() : null;
                if (connection == null) network.Shutdown();
                float giveUp = Time.unscaledTime + shutdownTimeoutSeconds;
                while (Time.unscaledTime < giveUp
                       && ((disconnect != null && !disconnect.IsCompleted) || network.IsListening || network.ShutdownInProgress))
                    yield return null;
                if (network.IsListening || network.ShutdownInProgress)
                    Debug.LogWarning("[GameMenu] The session did not close in time; leaving anyway.", this);
                // On a host every player's character was written as it left the world; the world's own documents now.
                ServerSave.FlushAll();
            }

            if (exit)
            {
                leavingTitle.text = "GOODBYE";
                FrontEnd.Quit();
                yield break;
            }

            // The session objects (the NetworkManager and what rides on it) outlive scene changes, and they hold on
            // to this world's player. The next world brings its own: these must be gone before it loads.
            if (network != null) Destroy(network.gameObject);
            if (connection != null && (network == null || connection.gameObject != network.gameObject)) Destroy(connection.gameObject);
            yield return null;

            Time.timeScale = 1f;
            FrontEnd.HighlightSlot = CharacterSession.ActiveSlot;
            FrontEnd.Go(FrontEnd.CharacterSelectScene);
        }
    }
}
