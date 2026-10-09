using System;
using System.Collections.Generic;
using KryticalNova.Combat;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Social;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>Who a player menu is about: always a name, and their player object when it is in sight.</summary>
    public readonly struct PlayerMenuTarget
    {
        public readonly string Name;
        /// <summary>The player's object on this machine, or null (named in a list, out of sight, offline).</summary>
        public readonly NetworkObject Player;

        public PlayerMenuTarget(string name, NetworkObject player = null)
        {
            Name = name ?? string.Empty;
            Player = player;
        }

        public bool InSight => Player != null && Player.IsSpawned;
    }

    /// <summary>
    /// The menu of what can be done with another player: whisper, Squad invite, Callout, and whatever other systems
    /// add (trade, Contacts, Crew). Opened by Shift+click on a player, by MORE on the target strip, or from a name in
    /// a list. It only starts requests; the server decides each of them.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class PlayerMenuController : MonoBehaviour
    {
        private sealed class Entry
        {
            public string Label;
            public int Order;
            public bool Danger;
            public Func<PlayerMenuTarget, bool> Visible;
            public Action<PlayerMenuTarget> Run;
        }

        private static readonly List<Entry> Entries = new List<Entry>(12);

        private VisualElement root, menu, actions;
        private Label title;
        private PointAndClickPlayer boundControls;
        private float nextBind;
        private int openedFrame = -1;

        /// <summary>The menu of this HUD, or null.</summary>
        public static PlayerMenuController Instance { get; private set; }

        public bool IsOpen => menu != null && menu.style.display == DisplayStyle.Flex;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Entries.Clear();

        /// <summary>
        /// Adds an action to every player menu. <paramref name="order"/> sorts it (lower first; built-ins use
        /// 10, 20, 30); <paramref name="visible"/> may hide it for a target. Registering a label again replaces it.
        /// </summary>
        public static void Register(string label, int order, Func<PlayerMenuTarget, bool> visible, Action<PlayerMenuTarget> run, bool danger = false)
        {
            if (string.IsNullOrEmpty(label) || run == null) return;
            Entries.RemoveAll(entry => entry.Label == label);
            Entries.Add(new Entry { Label = label, Order = order, Visible = visible, Run = run, Danger = danger });
            Entries.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            menu = root?.Q("player-menu");
            if (menu == null)
            {
                Debug.LogError("[PlayerMenu] SocialPanels.uxml is not instanced in this UI document.", this);
                return;
            }
            title = menu.Q<Label>("player-menu-title");
            actions = menu.Q("player-menu-actions");
            menu.style.display = DisplayStyle.None;
            Instance = this;

            Register("Whisper", 10, _ => true, target => GetComponent<ChatWindowController>()?.BeginWhisper(target.Name));
            Register("Invite to Squad", 20, target => PlayerParty.Local != null && PlayerParty.Local.IsSpawned && MayJoinMine(target), target =>
            {
                PlayerParty mine = PlayerParty.Local;
                if (mine == null) return;
                if (target.InSight && target.Player.TryGetComponent(out PlayerParty theirs)) mine.Invite(theirs);
                else mine.Invite(target.Name);
            });
            Register("Callout", 30, target => target.InSight && PlayerDuel.Local != null && PlayerDuel.Local.IsSpawned, target =>
            {
                if (target.Player.TryGetComponent(out PlayerDuel theirs)) PlayerDuel.Local.Request(theirs);
            });
        }

        private void OnDisable()
        {
            Bind(null);
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (menu == null) return;
            float now = Time.unscaledTime;
            if (now >= nextBind)
            {
                nextBind = now + 0.5f;
                PlayerInventory local = PlayerInventory.Local;
                Bind(local != null ? local.GetComponent<PointAndClickPlayer>() : null);
            }
            if (!IsOpen || Time.frameCount == openedFrame) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) { Close(); return; }
            var mouse = Mouse.current;
            if (mouse == null || !(mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)) return;
            Vector2 screen = mouse.position.ReadValue();
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
            if (!menu.worldBound.Contains(panelPoint)) Close();
        }

        private void Bind(PointAndClickPlayer next)
        {
            if (boundControls == next) return;
            if (boundControls != null) boundControls.PlayerMenuRequested -= OnPlayerMenuRequested;
            boundControls = next;
            if (boundControls != null) boundControls.PlayerMenuRequested += OnPlayerMenuRequested;
        }

        private void OnPlayerMenuRequested(Transform player, Vector2 screenPosition)
        {
            if (player == null || root?.panel == null) return;
            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screenPosition.x, Screen.height - screenPosition.y));
            Open(TargetOf(player), panelPoint);
        }

        /// <summary>The menu target for a player object in the world.</summary>
        public static PlayerMenuTarget TargetOf(Transform player)
        {
            var identity = player != null ? player.GetComponentInParent<PlayerIdentity>() : null;
            return identity != null ? new PlayerMenuTarget(identity.CharacterName, identity.NetworkObject) : default;
        }

        /// <summary>
        /// False for a player in sight who is of another faction: a Squad and a Crew are one faction, so their
        /// invitations are not offered. A player known only by name is not ruled out here; the server decides.
        /// </summary>
        public static bool MayJoinMine(PlayerMenuTarget target) =>
            !target.InSight || MayJoinMine(target.Player.GetComponent<PlayerIdentity>());

        public static bool MayJoinMine(PlayerIdentity other)
        {
            PlayerInventory local = PlayerInventory.Local;
            if (other == null || local == null || !local.TryGetComponent(out PlayerIdentity mine)) return true;
            return other.Faction == mine.Faction;
        }

        /// <summary>Opens the menu for a player at a point of the panel. Nothing happens for the local player or an empty name.</summary>
        public void Open(PlayerMenuTarget target, Vector2 panelPosition)
        {
            if (menu == null || string.IsNullOrEmpty(target.Name)) return;
            PlayerInventory local = PlayerInventory.Local;
            if (local != null && target.Player != null && target.Player.gameObject == local.gameObject) return;

            title.text = target.Name.ToUpperInvariant();
            actions.Clear();
            foreach (Entry entry in Entries)
            {
                if (entry.Visible != null && !entry.Visible(target)) continue;
                Entry chosen = entry;
                var button = new Button(() => { Close(); chosen.Run(target); }) { text = entry.Label.ToUpperInvariant() };
                if (entry.Danger) button.AddToClassList("kn-button-danger");
                actions.Add(button);
            }
            if (actions.childCount == 0) return;

            menu.style.display = DisplayStyle.Flex;
            menu.BringToFront();
            menu.style.left = panelPosition.x + 6f;
            menu.style.top = panelPosition.y + 6f;
            openedFrame = Time.frameCount;
            menu.schedule.Execute(ClampIntoView);
            if (GearUIController.Instance != null) GearUIController.Instance.SuppressWorldInput();
        }

        public void Close()
        {
            if (menu == null) return;
            menu.style.display = DisplayStyle.None;
            if (GearUIController.Instance != null) GearUIController.Instance.SuppressWorldInput();
        }

        private void ClampIntoView()
        {
            Rect bounds = menu.worldBound;
            float maxX = root.layout.width - bounds.width - 4f;
            float maxY = root.layout.height - bounds.height - 4f;
            if (bounds.xMin > maxX) menu.style.left = menu.layout.x - (bounds.xMin - maxX);
            if (bounds.yMin > maxY) menu.style.top = menu.layout.y - (bounds.yMin - maxY);
        }
    }
}
