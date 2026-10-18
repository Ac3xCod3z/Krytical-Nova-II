using KryticalNova.Networking;
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
    /// on, where it was switched on, and how much Autopilot time the character has left. It refuses to start
    /// with no time or outside a Hostile Zone, and switches it off when the time runs out, the player dies,
    /// leaves the zone, or is attacked by another player. Everyone sees the on flag (the AUTO tag on a namecard).
    /// An Autopilot Chip adds time; the time and the character's choices are saved with the character.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAutopilot : NetworkBehaviour, ISaveParticipant, IItemUseHandler
    {
        private const float TickSeconds = 0.5f;
        private const float TimeSendSeconds = 5f;
        private const int MaxPreferencesLength = 4096;

        [SerializeField] private AutopilotSettings settings;

        private readonly NetworkVariable<bool> netOn = new NetworkVariable<bool>(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> netSeconds = new NetworkVariable<float>(writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> netAnchor = new NetworkVariable<Vector3>(writePerm: NetworkVariableWritePermission.Server);

        private CombatEntity entity;
        private bool on;
        private float seconds;
        private Vector3 anchor;
        private HostileZone zone;
        private bool free;                 // GM: runs without using time
        private float nextTick, nextTimeSend;
        private AutopilotPreferences preferences = new AutopilotPreferences();

        /// <summary>Raised on every peer when Autopilot goes on or off, or the time or the choices change.</summary>
        public event Action Changed;

        public static PlayerAutopilot Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerAutopilot>() : null;

        public AutopilotSettings Settings => settings;
        public bool IsOn => IsSpawned ? netOn.Value : on;
        /// <summary>Autopilot time left, seconds. On a client it is the server's last word, a few seconds old.</summary>
        public float SecondsLeft => IsSpawned && !IsServer ? netSeconds.Value : seconds;
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
            netSeconds.OnValueChanged += OnNetSecondsChanged;
            if (IsServer)
            {
                netOn.Value = on = false;
                netSeconds.Value = seconds;
                if (!IsOwner) PreferencesRpc(preferences.ToJson());
            }
        }

        public override void OnNetworkDespawn()
        {
            netOn.OnValueChanged -= OnNetChanged;
            netSeconds.OnValueChanged -= OnNetSecondsChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetChanged(bool previous, bool current) => Changed?.Invoke();
        private void OnNetSecondsChanged(float previous, float current) => Changed?.Invoke();

        // ------------------------------------------------------------------ the owner asks

        /// <summary>Owner: switch Autopilot on or off. The server may refuse to switch it on, and says why.</summary>
        public void RequestSetOn(bool value)
        {
            if (IsSpawned && !IsOwner) return;
            if (IsSpawned && !IsServer) SetOnServerRpc(value);
            else ServerSetOn(value, value ? null : "Autopilot off.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetOnServerRpc(bool value)
        {
            if (!RpcBudget.Allow(this, CallKind.Account, nameof(SetOnServerRpc))) return;
            ServerSetOn(value, value ? null : "Autopilot off.");
        }

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
            if (!RpcBudget.Allow(this, CallKind.Account, nameof(SetPreferencesServerRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(SetPreferencesServerRpc), json, MaxPreferencesLength)) return;
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
            if (!free && seconds <= 0f) return "No Autopilot time left. Use an Autopilot Chip.";
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
            if (IsSpawned) { netOn.Value = on; netSeconds.Value = seconds; }
            else Changed?.Invoke();
            if (!string.IsNullOrEmpty(reason)) Tell(reason);
        }

        private void Update()
        {
            if (!Decides || settings == null || Time.time < nextTick) return;
            float elapsed = TickSeconds + Mathf.Max(0f, Time.time - nextTick);
            nextTick = Time.time + TickSeconds;

            bool timeRuns = !free && seconds > 0f && (on || !settings.timeRunsOnlyWhileOn);
            if (timeRuns)
            {
                seconds = Mathf.Max(0f, seconds - elapsed);
                if (IsSpawned && (seconds <= 0f || Time.time >= nextTimeSend))
                {
                    nextTimeSend = Time.time + TimeSendSeconds;
                    netSeconds.Value = seconds;
                }
            }
            if (!on) return;

            if (!free && seconds <= 0f) { ServerSetOn(false, "Autopilot time has run out. Use an Autopilot Chip for more."); return; }
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

        /// <summary>Server or offline: adds Autopilot time, up to the most a character can hold.</summary>
        public void ServerAddSeconds(float amount)
        {
            if (!Decides || settings == null) return;
            ServerSetSeconds(seconds + amount);
        }

        /// <summary>Server or offline (GM): sets the Autopilot time left.</summary>
        public void ServerSetSeconds(float value)
        {
            if (!Decides) return;
            seconds = Mathf.Clamp(value, 0f, settings != null ? settings.MaxSeconds : value);
            if (IsSpawned) netSeconds.Value = seconds;
            else Changed?.Invoke();
        }

        /// <summary>Server or offline (GM): Autopilot runs without time and without using any.</summary>
        public void ServerSetFree(bool value)
        {
            if (Decides) free = value;
        }

        public bool Free => free;

        // ------------------------------------------------------------------ the Autopilot Chip

        public ItemUseEffect Effect => ItemUseEffect.AutopilotTime;

        public string UseBlockedReason(ItemDefinition item)
        {
            if (settings == null) return "Autopilot is not set up.";
            return seconds + settings.SecondsPerChip > settings.MaxSeconds ? "You cannot hold any more Autopilot time." : null;
        }

        public void ServerUse(ItemDefinition item)
        {
            ServerAddSeconds(settings.SecondsPerChip);
            Tell($"+{settings.hoursPerChip:0.#} hours of Autopilot. You have {FormatTime(seconds)}.");
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
            public float seconds;
            public string preferences;
        }

        public string SaveKey => "autopilot";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => JsonUtility.ToJson(new AutopilotSave { seconds = seconds, preferences = preferences.ToJson() });

        public void RestoreSave(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            AutopilotSave save = JsonUtility.FromJson<AutopilotSave>(json);
            seconds = Mathf.Clamp(save.seconds, 0f, settings != null ? settings.MaxSeconds : save.seconds);
            preferences = AutopilotPreferences.FromJson(save.preferences, settings);
            if (IsSpawned && IsServer)
            {
                netSeconds.Value = seconds;
                if (!IsOwner) PreferencesRpc(preferences.ToJson());
            }
            Changed?.Invoke();
        }
    }
}
