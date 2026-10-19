using System;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// The XP boosters a character has running (the Field, Companion and Tandem Boosters of the Nova Store: which
    /// items, how much and for how long is <see cref="EconomySettings.boosters"/>). Using one adds its time to its
    /// own clock; while a clock has time left its share is added to the XP of every Hostile kill, the character's,
    /// the pet's or both. Boosters that run together add up, and they add to the Squad and Sponsor boosts.
    /// The server keeps the clocks and saves them with the character; the owner is told the time left, for the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerExperienceBoosts : NetworkBehaviour, ISaveParticipant, IItemUseHandler
    {
        private const float TickSeconds = 1f;
        private const float SendSeconds = 15f;

        [SerializeField] private EconomySettings settings;

        // One clock a booster, in the order of the settings' list. The owner's copy is exact when a booster is used
        // or runs out, and at most SendSeconds behind in between: the HUD shows minutes.
        private NetworkList<float> netSeconds;
        private float[] seconds = Array.Empty<float>();
        private float lastTick, nextSend;

        /// <summary>Raised when a clock changes by more than its own running (a booster used, run out, restored).</summary>
        public event Action Changed;

        public EconomySettings Settings => settings;
        private bool Decides => !IsSpawned || IsServer;
        private ExperienceBooster[] Boosters => settings != null && settings.boosters != null ? settings.boosters : Array.Empty<ExperienceBooster>();

        /// <summary>How many boosters there are (running or not).</summary>
        public int Count => Boosters.Length;
        public ExperienceBooster BoosterAt(int index) => Boosters[index];

        /// <summary>Seconds a booster has left: the server's own figure where the game is decided, what it last told the owner elsewhere.</summary>
        public float SecondsLeft(int index)
        {
            if (Decides) return index >= 0 && index < seconds.Length ? seconds[index] : 0f;
            return netSeconds != null && index >= 0 && index < netSeconds.Count ? netSeconds[index] : 0f;
        }

        /// <summary>Server: the share added to this character's kill XP right now (0.5 = +50 %).</summary>
        public float ServerCharacterBonus => Decides ? ExperienceBoostRules.CharacterBonus(Boosters, seconds) : 0f;
        /// <summary>Server: the share added to the kill XP of this character's pet right now.</summary>
        public float ServerPetBonus => Decides ? ExperienceBoostRules.PetBonus(Boosters, seconds) : 0f;

        private void Awake()
        {
            netSeconds = new NetworkList<float>(null, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);
            Fit();
        }

        private void OnEnable() => lastTick = Time.time;

        // The clocks follow the settings' list: a booster added to it later starts at nothing.
        private void Fit()
        {
            int count = Boosters.Length;
            if (seconds.Length != count) Array.Resize(ref seconds, count);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netSeconds.OnListChanged += OnNetChanged;
            if (IsServer) Send();
        }

        public override void OnNetworkDespawn()
        {
            netSeconds.OnListChanged -= OnNetChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetChanged(NetworkListEvent<float> change)
        {
            if (!IsServer) Changed?.Invoke();
        }

        private void Send()
        {
            if (!IsSpawned || !IsServer) return;
            nextSend = Time.time + SendSeconds;
            while (netSeconds.Count > seconds.Length) netSeconds.RemoveAt(netSeconds.Count - 1);
            for (int i = 0; i < seconds.Length; i++)
            {
                if (i >= netSeconds.Count) netSeconds.Add(seconds[i]);
                else if (!Mathf.Approximately(netSeconds[i], seconds[i])) netSeconds[i] = seconds[i];
            }
        }

        private void Update()
        {
            if (!Decides || seconds.Length == 0 || Time.time < lastTick + TickSeconds) return;
            // Only the time since the last tick: a character that enters late is not charged for the session's age.
            float elapsed = Time.time - lastTick;
            lastTick = Time.time;

            bool any = false, ended = false;
            for (int i = 0; i < seconds.Length; i++)
            {
                if (seconds[i] <= 0f) continue;
                any = true;
                seconds[i] = Mathf.Max(0f, seconds[i] - elapsed);
                if (seconds[i] > 0f) continue;
                ended = true;
                Tell($"{NameOf(i)} has run out.");
            }
            if (ended)
            {
                Send();
                Changed?.Invoke();
            }
            else if (any && Time.time >= nextSend) Send();
        }

        private string NameOf(int index)
        {
            ItemDefinition item = TryGetComponent(out PlayerInventory inventory) && inventory.Catalog != null ? inventory.Catalog.Get(Boosters[index].itemId) : null;
            return item != null ? item.displayName : "An XP booster";
        }

        private int IndexOf(ItemDefinition item)
        {
            if (item == null) return -1;
            ExperienceBooster[] boosters = Boosters;
            for (int i = 0; i < boosters.Length; i++)
                if (boosters[i].itemId == item.itemId) return i;
            return -1;
        }

        private void Tell(string text)
        {
            if (IsSpawned && !IsOwner) TellRpc(text);
            else if (!IsSpawned || IsOwner) ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ a booster is used (server)

        public ItemUseEffect Effect => ItemUseEffect.ExperienceBoost;

        public string UseBlockedReason(ItemDefinition item)
        {
            int index = IndexOf(item);
            if (index < 0) return "It does nothing here.";
            Fit();
            ExperienceBooster booster = Boosters[index];
            return ExperienceBoostRules.WouldWaste(seconds[index], booster.minutes, settings.boosterMaxHours)
                ? $"You cannot hold more than {settings.boosterMaxHours} hours of it." : null;
        }

        public void ServerUse(ItemDefinition item)
        {
            int index = IndexOf(item);
            if (!Decides || index < 0) return;
            Fit();
            ExperienceBooster booster = Boosters[index];
            seconds[index] = ExperienceBoostRules.AfterUse(seconds[index], booster.minutes, settings.boosterMaxHours);
            Send();
            Changed?.Invoke();
            Tell($"{item.displayName}: {Describe(booster)} for {FormatTime(seconds[index])}.");
        }

        /// <summary>Server (GM): every clock back to nothing.</summary>
        public void ServerClear()
        {
            if (!Decides) return;
            for (int i = 0; i < seconds.Length; i++) seconds[i] = 0f;
            Send();
            Changed?.Invoke();
        }

        /// <summary>
        /// What an item's tooltip says of it when it is a booster: what one adds and for how long, and the time this
        /// character has of it running. Null for any other item.
        /// </summary>
        public string TooltipLine(ItemDefinition item)
        {
            int index = IndexOf(item);
            if (index < 0) return null;
            ExperienceBooster booster = Boosters[index];
            float left = SecondsLeft(index);
            return $"{Describe(booster)} for {booster.minutes} min." + (left > 0f ? $" Running: {FormatTime(left)} left." : string.Empty);
        }

        /// <summary>What a booster adds, as a player reads it: "+50% character XP, +50% pet XP".</summary>
        public static string Describe(in ExperienceBooster booster)
        {
            if (booster.characterPercent > 0 && booster.petPercent > 0)
                return $"+{booster.characterPercent}% character XP, +{booster.petPercent}% pet XP";
            return booster.petPercent > 0 ? $"+{booster.petPercent}% pet XP" : $"+{booster.characterPercent}% character XP";
        }

        /// <summary>Time as "1 h 05 min" or "12 min".</summary>
        public static string FormatTime(float totalSeconds)
        {
            int minutes = Mathf.Max(0, Mathf.CeilToInt(totalSeconds / 60f));
            return minutes >= 60 ? $"{minutes / 60} h {minutes % 60:00} min" : $"{minutes} min";
        }

        // ------------------------------------------------------------------ saving

        [Serializable]
        private struct BoostClock
        {
            public ushort itemId;
            public float seconds;
        }

        [Serializable]
        private sealed class BoostSave
        {
            public BoostClock[] clocks;
            /// <summary>When it was saved (UTC ticks): for clocks that run while the character is away.</summary>
            public long savedAt;
        }

        public string SaveKey => "xpboosts";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave()
        {
            ExperienceBooster[] boosters = Boosters;
            // Whole seconds, and the time of saving only while a clock runs: with no boost running the text stays the same
            // from save to save, so a save writes nothing for it (online phase 4: byte-sized saves).
            var save = new BoostSave { clocks = new BoostClock[boosters.Length] };
            bool running = false;
            for (int i = 0; i < boosters.Length; i++)
            {
                float left = i < seconds.Length ? Mathf.Max(0f, Mathf.Round(seconds[i])) : 0f;
                running |= left > 0f;
                save.clocks[i] = new BoostClock { itemId = boosters[i].itemId, seconds = left };
            }
            if (running) save.savedAt = DateTime.UtcNow.Ticks;
            return JsonUtility.ToJson(save);
        }

        public void RestoreSave(string json)
        {
            Fit();
            for (int i = 0; i < seconds.Length; i++) seconds[i] = 0f;
            BoostSave save = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<BoostSave>(json);
            if (save != null && save.clocks != null && settings != null)
            {
                // Clocks are kept by item, so a list in another order (or shorter) loses nothing it still has.
                float away = settings.boosterTimeRunsOnlyOnline ? 0f
                    : (float)Math.Max(0.0, new TimeSpan(DateTime.UtcNow.Ticks - save.savedAt).TotalSeconds);
                ExperienceBooster[] boosters = Boosters;
                foreach (BoostClock clock in save.clocks)
                    for (int i = 0; i < boosters.Length; i++)
                        if (boosters[i].itemId == clock.itemId)
                            seconds[i] = Mathf.Clamp(clock.seconds - away, 0f, settings.boosterMaxHours * 3600f);
            }
            lastTick = Time.time;
            Send();
            Changed?.Invoke();
        }
    }
}
