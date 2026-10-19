using KryticalNova.Networking;
using System;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Autopilot
{
    /// <summary>
    /// The server's side of the Autobuff (owner, 15 Oct 2026): when the character's licence runs out, by the clock,
    /// and the eight buff skills the character slotted. An Autobuff Pass (3, 14 or 30 days) lengthens the licence,
    /// as a Kiosk Permit opens a Kiosk; the days pass whether the character plays or not. The owner edits the cells
    /// in the Autopilot window and the server keeps them; the casting itself runs on the owner's own game
    /// (<see cref="AutobuffCaster"/>) through the ordinary cast path, so the Autobuff gives no power a hand press
    /// would not. Both are saved with the character.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerAutobuff : NetworkBehaviour, ISaveParticipant, IItemUseHandler
    {
        private const int MaxSlotsJsonLength = 1024;

        [SerializeField] private AutopilotSettings settings;

        private readonly NetworkVariable<long> netExpiresUtc = new NetworkVariable<long>(writePerm: NetworkVariableWritePermission.Server);
        private long expiresUtc;
        private string[] skillIds = new string[AutobuffRules.Slots];

        /// <summary>Raised on the owner and the server when the licence or the cells change.</summary>
        public event Action Changed;

        public static PlayerAutobuff Local => LocalPlayer.Get<PlayerAutobuff>();

        public AutopilotSettings Settings => settings;
        /// <summary>When the licence ends, UTC ticks. On a client it is the server's word.</summary>
        public long ExpiresUtc => IsSpawned && !IsServer ? netExpiresUtc.Value : expiresUtc;
        public bool IsActive => AutobuffRules.IsActive(DateTime.UtcNow.Ticks, ExpiresUtc);
        /// <summary>The eight cells: a skill id or empty each. On the owner and where the game is decided; empty elsewhere.</summary>
        public string[] SkillIds => skillIds;
        public string TimeLeftLine => AutobuffRules.TimeLeftLine(DateTime.UtcNow.Ticks, ExpiresUtc);

        private bool Decides => !IsSpawned || IsServer;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netExpiresUtc.OnValueChanged += OnNetChanged;
            if (IsServer)
            {
                netExpiresUtc.Value = expiresUtc;
                if (!IsOwner) SlotsRpc(ToJson(skillIds));
            }
        }

        public override void OnNetworkDespawn()
        {
            netExpiresUtc.OnValueChanged -= OnNetChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetChanged(long previous, long current) => Changed?.Invoke();

        // ------------------------------------------------------------------ the owner asks

        /// <summary>Owner: keep these cells. They are saved with the character.</summary>
        public void SaveSlots(string[] ids)
        {
            if (IsSpawned && !IsOwner) return;
            skillIds = AutobuffRules.Sanitise(ids);
            if (IsSpawned && !IsServer) SetSlotsServerRpc(ToJson(skillIds));
            Changed?.Invoke();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetSlotsServerRpc(string json)
        {
            if (!RpcBudget.Allow(this, CallKind.Account, nameof(SetSlotsServerRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(SetSlotsServerRpc), json, 4096)) return;
            if (json == null || json.Length > MaxSlotsJsonLength) return;
            skillIds = AutobuffRules.Sanitise(FromJson(json));
            Changed?.Invoke();
        }

        [Rpc(SendTo.Owner)]
        private void SlotsRpc(string json)
        {
            skillIds = AutobuffRules.Sanitise(FromJson(json));
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ the server decides

        /// <summary>Server or offline (GM): the licence runs for this many days from now; 0 ends it.</summary>
        public void ServerSetDays(int days)
        {
            if (!Decides) return;
            long now = DateTime.UtcNow.Ticks;
            expiresUtc = days <= 0 ? now : KioskRules.ExpiryTicks(now, days);
            Push();
        }

        private void Push()
        {
            if (IsSpawned) netExpiresUtc.Value = expiresUtc;
            Changed?.Invoke();
        }

        private void Tell(string text)
        {
            if (IsSpawned && !IsOwner) TellRpc(text);
            else if (!IsSpawned || IsOwner) ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ the Autobuff Pass

        public ItemUseEffect Effect => ItemUseEffect.AutobuffTime;

        public string UseBlockedReason(UsedItem item)
        {
            if (settings == null) return "Autobuff is not set up.";
            PassTier? tier = settings.AutobuffTierFor(!item.IsNone ? item.Id : (ushort)0);
            if (tier == null) return "It does nothing here.";
            return AutobuffRules.WouldExceed(DateTime.UtcNow.Ticks, expiresUtc, tier.Value.days, settings.autobuffMaxDays)
                ? $"You cannot hold more than {settings.autobuffMaxDays} days of Autobuff." : null;
        }

        public void ServerUse(UsedItem item)
        {
            PassTier? tier = settings != null ? settings.AutobuffTierFor(item.Id) : null;
            if (tier == null) return;
            expiresUtc = AutobuffRules.Extend(DateTime.UtcNow.Ticks, expiresUtc, tier.Value.days);
            Push();
            Tell($"+{tier.Value.days} days of Autobuff. {TimeLeftLine}");
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private struct AutobuffSave
        {
            public long expiresUtc;
            public string[] skills;
        }

        [Serializable]
        private struct SlotsJson
        {
            public string[] ids;
        }

        private static string ToJson(string[] ids) => JsonUtility.ToJson(new SlotsJson { ids = ids });

        private static string[] FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<SlotsJson>(json).ids; }
            catch (ArgumentException) { return null; }
        }

        public string SaveKey => "autobuff";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave() => JsonUtility.ToJson(new AutobuffSave { expiresUtc = expiresUtc, skills = skillIds });

        public void RestoreSave(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            AutobuffSave save = JsonUtility.FromJson<AutobuffSave>(json);
            expiresUtc = save.expiresUtc;
            skillIds = AutobuffRules.Sanitise(save.skills);
            if (IsSpawned && IsServer)
            {
                netExpiresUtc.Value = expiresUtc;
                if (!IsOwner) SlotsRpc(ToJson(skillIds));
            }
            Changed?.Invoke();
        }
    }
}
