using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// The Serums a character has taken (Twelve Sky 2: Elixirs). Using one from the Pack raises a stat for good; a
    /// kind can be taken until the character has had the cap of it, and at the cap the server refuses the use and
    /// the item stays in the Pack. The counts are saved with the character and replicated to everyone, and every
    /// machine works the stat bonuses out from them, the way worn gear and the Nova do. The rules are
    /// <see cref="SerumRules"/>; every number is in <see cref="SerumSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSerums : NetworkBehaviour, IItemUseHandler, ISaveParticipant
    {
        [SerializeField] private SerumSettings settings;

        private readonly NetworkVariable<SerumState> netState = new NetworkVariable<SerumState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CharacterCombatStats stats;
        private ISaveScheduler saveAgent;
        private SerumState state;

        // One stable source object so every kind's bonus can be replaced in one step.
        private readonly object statSource = new object();
        private readonly List<StatModifier> modifiers = new List<StatModifier>(SerumSettings.MaxKinds * 2);

        /// <summary>The Serums of the player this machine controls.</summary>
        public static PlayerSerums Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerSerums>() : null;

        /// <summary>Raised on every machine when a count changes.</summary>
        public event Action Changed;

        public SerumSettings Settings => settings;
        public SerumState State => state;

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            stats = GetComponent<CharacterCombatStats>();
            saveAgent = GetComponent<ISaveScheduler>();
        }

        private void OnEnable()
        {
            if (!IsSpawned) Show(state);
        }

        private void OnDisable()
        {
            if (stats != null) stats.SetSourceModifiers(statSource, null);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            if (IsServer) netState.Value = state;
            Show(IsServer ? state : netState.Value);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(SerumState previous, SerumState current)
        {
            if (!IsServer) Show(current);
        }

        /// <summary>Where the game is decided: keeps a change, tells every machine and has it saved.</summary>
        private void Commit(SerumState next)
        {
            state = next;
            if (IsSpawned && IsServer) netState.Value = next;
            Show(next);
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        /// <summary>Every machine: the bonuses that follow from what was taken.</summary>
        private void Show(SerumState next)
        {
            state = next;
            modifiers.Clear();
            SerumRules.AddModifiers(settings, state, statSource, modifiers);
            if (stats != null && isActiveAndEnabled) stats.SetSourceModifiers(statSource, modifiers);
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ IItemUseHandler (server)

        public ItemUseEffect Effect => ItemUseEffect.Serum;

        public string UseBlockedReason(UsedItem item) =>
            item.IsNone ? SerumRules.ReasonNoSettings : SerumRules.UseBlockedReason(settings, SerumRules.KindOf(settings, item.Id), state);

        public void ServerUse(UsedItem item)
        {
            if (!HasAuthority || settings == null || item.IsNone) return;
            int kind = SerumRules.KindOf(settings, item.Id);
            SerumState next = SerumRules.Take(settings, kind, state);
            if (next.Equals(state)) return;
            Commit(next);
            SerumKind serum = settings.serums[kind];
            Tell($"{serum.displayName} {next.Taken(kind)}/{settings.Cap}: {SerumRules.Gains(serum, next.Taken(kind))} for good.");
        }

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwnedByServer) TellRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ GM

        /// <summary>GM: sets how many of a kind the character has taken, within the cap. Server only.</summary>
        public bool ServerSetTaken(int kind, int taken)
        {
            if (!HasAuthority || settings == null || kind < 0 || kind >= SerumRules.KindCount(settings)) return false;
            Commit(state.With(kind, SerumRules.Clamp(settings, taken)));
            return true;
        }

        // ------------------------------------------------------------------ save

        [Serializable]
        private struct SavedSerum
        {
            public int itemId;
            public int taken;
        }

        [Serializable]
        private sealed class SerumSave
        {
            public List<SavedSerum> serums = new List<SavedSerum>(SerumSettings.MaxKinds);
        }

        public string SaveKey => "serums";
        public SaveScope SaveScope => SaveScope.Character;

        /// <summary>Kept by item id, so the order of the kinds in the settings may change without mixing counts up.</summary>
        public string CaptureSave()
        {
            var save = new SerumSave();
            int count = SerumRules.KindCount(settings);
            for (int i = 0; i < count; i++)
                if (state.Taken(i) > 0) save.serums.Add(new SavedSerum { itemId = settings.serums[i].itemId, taken = state.Taken(i) });
            return JsonUtility.ToJson(save);
        }

        /// <summary>A character saved before Serums existed has nothing here and has taken none.</summary>
        public void RestoreSave(string json)
        {
            if (!HasAuthority || settings == null || string.IsNullOrEmpty(json)) return;
            SerumSave save = JsonUtility.FromJson<SerumSave>(json);
            if (save?.serums == null) return;
            var next = new SerumState();
            foreach (SavedSerum saved in save.serums)
            {
                int kind = SerumRules.KindOf(settings, (ushort)Mathf.Clamp(saved.itemId, 0, ushort.MaxValue));
                if (kind >= 0) next = next.With(kind, SerumRules.Clamp(settings, saved.taken));
            }

            state = next;
            if (IsSpawned && IsServer) netState.Value = next;
            Show(next);
        }
    }
}
