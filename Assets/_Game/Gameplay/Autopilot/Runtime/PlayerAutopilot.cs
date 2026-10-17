using System;
using KryticalNova.Core;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// The server's side of Autopilot. Autopilot itself runs on the player's own game and only presses the
    /// buttons a player could (<see cref="AutopilotController"/>); the server keeps three things: whether it is
    /// on, where it was switched on, and when the character's Autopilot time ends. It refuses to start with no
    /// time or outside a Hostile Zone, and switches it off when the time runs out, the player dies, leaves the
    /// zone, or is attacked by another player. Everyone sees the on flag (the AUTO tag on a namecard).
    /// An Autopilot Pass (3, 14 or 30 days, owner 15 Oct 2026) lengthens the time by the clock, as a Kiosk Permit
    /// opens a Kiosk: the days pass whether Autopilot is on or not, and whether the character plays or not. The
    /// time and the character's choices are saved with the character.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAutopilot : NetworkBehaviour, ISaveParticipant, IItemUseHandler
    {
        private const float TickSeconds = 0.5f;
        private const int MaxPreferencesLength = 4096;

        [SerializeField] private AutopilotSettings settings;

        private readonly NetworkVariable<bool> netOn = new NetworkVariable<bool>(writePerm: NetworkVariableWritePermission.Server);
        // When the time ends, UTC ticks. Only the owner reads it: how long another player may hunt is theirs.
        private readonly NetworkVariable<long> netExpiresUtc = new NetworkVariable<long>(default,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> netAnchor = new NetworkVariable<Vector3>(writePerm: NetworkVariableWritePermission.Server);

        private CombatEntity entity;
        private bool on;
        private long expiresUtc;
        private Vector3 anchor;
        private HostileZone zone;
        private bool free;                 // GM: runs without using time
        private float nextTick;
        private AutopilotPreferences preferences = new AutopilotPreferences();

        /// <summary>Raised on every peer when Autopilot goes on or off, or the time or the choices change.</summary>
        public event Action Changed;

        public static PlayerAutopilot Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerAutopilot>() : null;

        public AutopilotSettings Settings => settings;
        public bool IsOn => IsSpawned ? netOn.Value : on;
        /// <summary>When the Autopilot time ends, UTC ticks. On the owner's client it is the server's word.</summary>
        public long ExpiresUtc => IsSpawned && !IsServer ? netExpiresUtc.Value : expiresUtc;
        /// <summary>True while the character has Autopilot time.</summary>
        public bool HasTime => PassClock.IsActive(DateTime.UtcNow.Ticks, ExpiresUtc);
        /// <summary>Autopilot time left, seconds.</summary>
        public float SecondsLeft => PassClock.SecondsLeft(DateTime.UtcNow.Ticks, ExpiresUtc);
        /// <summary>"Autopilot: 2d 13h left." or "No Autopilot time: use an Autopilot Pass."</summary>
        public string TimeLeftLine => PassClock.TimeLeftLine("Autopilot", DateTime.UtcNow.Ticks, ExpiresUtc);
        /// <summary>Where it was switched on: the middle of a hunt by radius.</summary>
        public Vector3 Anchor => IsSpawned ? netAnchor.Value : anchor;
        /// <summary>This character's choices. On the owner and where the game is decided; defaults elsewhere.</summary>
        public AutopilotPreferences Preferences => preferences;
        /// <summary>The Hostile Zone the player stands in, or null.</summary>
        public HostileZone CurrentZone => HostileZone.At(transform.position);

        private bool Decides => !IsSpawned || IsServer;

        private void Awake() => entity = GetComponent<CombatEntity>();

        private void OnEnable()
        {
            if (entity != null) entity.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            if (entity != null) entity.Damaged -= OnDamaged;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netOn.OnValueChanged += OnNetChanged;
            netExpiresUtc.OnValueChanged += OnNetExpiresChanged;
            if (IsServer)
            {
                netOn.Value = on = false;
                netExpiresUtc.Value = expiresUtc;
                if (!IsOwner) PreferencesRpc(preferences.ToJson());
            }
        }

        public override void OnNetworkDespawn()
        {
            netOn.OnValueChanged -= OnNetChanged;
            netExpiresUtc.OnValueChanged -= OnNetExpiresChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetChanged(bool previous, bool current) => Changed?.Invoke();
        private void OnNetExpiresChanged(long previous, long current) => Changed?.Invoke();

        // ------------------------------------------------------------------ the owner asks

        /// <summary>Owner: switch Autopilot on or off. The server may refuse to switch it on, and says why.</summary>
        public void RequestSetOn(bool value)
        {
            if (IsSpawned && !IsOwner) return;
            if (IsSpawned && !IsServer) SetOnServerRpc(value);
            else ServerSetOn(value, value ? null : "Autopilot off.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetOnServerRpc(bool value) => ServerSetOn(value, value ? null : "Autopilot off.");

        /// <summary>Owner: keep these choices. They are saved with the character.</summary>
        public void SavePreferences(AutopilotPreferences changed)
        {
            if (changed == null || (IsSpawned && !IsOwner)) return;
            changed.Sanitise(settings);
            preferences = changed;
            if (IsSpawned && !IsServer) SetPreferencesServerRpc(changed.ToJson());
            Changed?.Invoke();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetPreferencesServerRpc(string json)
        {
            if (json == null || json.Length > MaxPreferencesLength) return;
            preferences = AutopilotPreferences.FromJson(json, settings);
        }

        [Rpc(SendTo.Owner)]
        private void PreferencesRpc(string json)
        {
            preferences = AutopilotPreferences.FromJson(json, settings);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ the server decides

        /// <summary>Why Autopilot cannot start right now, or null when it can. Where the game is decided.</summary>
        public string StartBlockedReason()
        {
            if (settings == null) return "Autopilot is not set up.";
            if (entity != null && !entity.IsAlive) return "You cannot start Autopilot while defeated.";
            if (!free && !HasTime) return "No Autopilot time left. Use an Autopilot Pass.";
            if (!settings.allowedOutsideHostileZones && CurrentZone == null) return "Autopilot only works inside a Hostile Zone.";
            return null;
        }

        /// <summary>Server or offline: switch Autopilot on or off, telling the player why.</summary>
        public void ServerSetOn(bool value, string reason = null)
        {
            if (!Decides) return;
            if (value == on) return;
            if (value)
            {
                string blocked = StartBlockedReason();
                if (blocked != null) { Tell(blocked); return; }
                zone = CurrentZone;
                anchor = transform.position;
                if (IsSpawned) netAnchor.Value = anchor;
                reason ??= "Autopilot on.";
            }
            on = value;
            if (IsSpawned) netOn.Value = on;
            else Changed?.Invoke();
            if (!string.IsNullOrEmpty(reason)) Tell(reason);
        }

        private void Update()
        {
            if (!Decides || settings == null || Time.time < nextTick) return;
            nextTick = Time.time + TickSeconds;
            if (!on) return;

            if (!free && !HasTime) { ServerSetOn(false, "Autopilot time has run out. Use an Autopilot Pass for more."); return; }
            if (entity != null && !entity.IsAlive) { ServerSetOn(false, "Autopilot off: you were defeated."); return; }
            if (!settings.allowedOutsideHostileZones)
            {
                // The zone it started in: wandering into another zone does not keep it going.
                if (zone == null) { ServerSetOn(false, "Autopilot off: the Hostile Zone is gone."); return; }
                Vector3 offset = transform.position - zone.Centre;
                offset.y = 0f;
                float limit = zone.Radius + settings.zoneExitMargin;
                if (offset.sqrMagnitude > limit * limit) { ServerSetOn(false, "Autopilot off: you left the Hostile Zone."); return; }
            }
        }

        private void OnDamaged(DamageResult result, CharacterCombatStats attacker)
        {
            if (!Decides || !on || attacker == null || result.IsEvaded) return;
            if (preferences.whenAttacked != WhenAttacked.SwitchOff) return;
            if (attacker.GetComponent<PlayerIdentity>() == null) return; // a Hostile: that is what Autopilot is for
            ServerSetOn(false, "ALERT: another player attacked you. Autopilot is off.");
        }

        private void Tell(string text)
        {
            if (IsSpawned && !IsOwner) TellRpc(text);
            else if (!IsSpawned || IsOwner) ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ time

        /// <summary>Server or offline (GM): the Autopilot time runs this many days from now; 0 ends it.</summary>
        public void ServerSetDays(int days)
        {
            if (!Decides) return;
            expiresUtc = PassClock.SetDays(DateTime.UtcNow.Ticks, days);
            Push();
        }

        private void Push()
        {
            if (IsSpawned) netExpiresUtc.Value = expiresUtc;
            Changed?.Invoke();
        }

        /// <summary>Server or offline (GM): Autopilot runs without time and without using any.</summary>
        public void ServerSetFree(bool value)
        {
            if (Decides) free = value;
        }

        public bool Free => free;

        // ------------------------------------------------------------------ the Autopilot Pass

        public ItemUseEffect Effect => ItemUseEffect.AutopilotTime;

        public string UseBlockedReason(ItemDefinition item)
        {
            if (settings == null) return "Autopilot is not set up.";
            PassTier? tier = settings.AutopilotTierFor(item != null ? item.itemId : (ushort)0);
            if (tier == null) return "It does nothing here.";
            return PassClock.WouldExceed(DateTime.UtcNow.Ticks, expiresUtc, tier.Value.days, settings.autopilotMaxDays)
                ? $"You cannot hold more than {settings.autopilotMaxDays} days of Autopilot." : null;
        }

        public void ServerUse(ItemDefinition item)
        {
            PassTier? tier = settings != null ? settings.AutopilotTierFor(item.itemId) : null;
            if (tier == null) return;
            expiresUtc = PassClock.Extend(DateTime.UtcNow.Ticks, expiresUtc, tier.Value.days);
            Push();
            Tell($"+{tier.Value.days} days of Autopilot. {TimeLeftLine}");
        }

        /// <summary>Time as "4 h 59 min" or "12 min 30 s".</summary>
        public static string FormatTime(float totalSeconds)
        {
            int whole = Mathf.Max(0, Mathf.CeilToInt(totalSeconds));
            int hours = whole / 3600, minutes = whole % 3600 / 60, secs = whole % 60;
            return hours > 0 ? $"{hours} h {minutes:00} min" : $"{minutes} min {secs:00} s";
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private struct AutopilotSave
        {
            public long expiresUtc;
            public string preferences;
        }

        public string SaveKey => "autopilot";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => JsonUtility.ToJson(new AutopilotSave { expiresUtc = expiresUtc, preferences = preferences.ToJson() });

        public void RestoreSave(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            AutopilotSave save = JsonUtility.FromJson<AutopilotSave>(json);
            // Hours from the old Autopilot Chip are not carried over (owner, 17 Oct 2026: no players yet).
            expiresUtc = save.expiresUtc;
            preferences = AutopilotPreferences.FromJson(save.preferences, settings);
            if (IsSpawned && IsServer)
            {
                netExpiresUtc.Value = expiresUtc;
                if (!IsOwner) PreferencesRpc(preferences.ToJson());
            }
            Changed?.Invoke();
        }
    }
}
