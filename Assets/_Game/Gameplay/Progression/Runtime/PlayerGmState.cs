using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// Development / GM toggles and stat bonuses for one player. They are decided where the player is simulated
    /// (the server, or offline) and the toggles are replicated, so the owning client's own checks agree with it.
    /// Set from the Quantum Console (KryticalNova.DevTools.GmCommands), which only the host or an offline player may use.
    /// TODO(GM): permission-check the setters when the GM mod gets real accounts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGmState : NetworkBehaviour
    {
        private const byte GodBit = 1;
        private const byte InfiniteChiBit = 2;
        private const byte NoCooldownsBit = 4;
        private const float OneShotPercent = 100000f;

        private bool godMode;
        private bool infiniteChi;
        private bool noCooldowns;
        private float speedMultiplier = 1f;

        // Stat bonuses change what the server computes; their results (damage, max HP) reach clients on their own.
        private readonly Dictionary<StatType, float> flatStats = new Dictionary<StatType, float>();
        private readonly object statSource = new object();
        private bool oneShot;

        private readonly NetworkVariable<byte> netToggles = new NetworkVariable<byte>(
            writePerm: NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<float> netSpeedMultiplier = new NetworkVariable<float>(
            1f, writePerm: NetworkVariableWritePermission.Server);

        /// <summary>The player takes no damage.</summary>
        public bool GodMode => godMode;
        /// <summary>Chi spends always succeed and never drain the pool.</summary>
        public bool InfiniteChi => infiniteChi;
        /// <summary>Combat skills ignore cooldowns.</summary>
        public bool NoCooldowns => noCooldowns;
        /// <summary>Multiplier applied to the player's walk/run speed.</summary>
        public float SpeedMultiplier => speedMultiplier;
        /// <summary>×1000 Attack Power.</summary>
        public bool OneShot => oneShot;
        public IReadOnlyDictionary<StatType, float> FlatStats => flatStats;

        public bool HasAuthority => !IsSpawned || IsServer;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netToggles.OnValueChanged += OnNetTogglesChanged;
            netSpeedMultiplier.OnValueChanged += OnNetSpeedChanged;
            if (IsServer)
            {
                Push();
            }
            else
            {
                ReadToggles(netToggles.Value);
                speedMultiplier = netSpeedMultiplier.Value;
            }
        }

        public override void OnNetworkDespawn()
        {
            netToggles.OnValueChanged -= OnNetTogglesChanged;
            netSpeedMultiplier.OnValueChanged -= OnNetSpeedChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetTogglesChanged(byte previous, byte current)
        {
            if (!IsServer) ReadToggles(current);
        }

        private void OnNetSpeedChanged(float previous, float current)
        {
            if (!IsServer) speedMultiplier = current;
        }

        private void ReadToggles(byte bits)
        {
            godMode = (bits & GodBit) != 0;
            infiniteChi = (bits & InfiniteChiBit) != 0;
            noCooldowns = (bits & NoCooldownsBit) != 0;
        }

        private void Push()
        {
            if (!IsSpawned || !IsServer) return;
            netToggles.Value = (byte)((godMode ? GodBit : 0) | (infiniteChi ? InfiniteChiBit : 0) | (noCooldowns ? NoCooldownsBit : 0));
            netSpeedMultiplier.Value = speedMultiplier;
        }

        public void SetGodMode(bool on)
        {
            if (!HasAuthority) return;
            godMode = on;
            Push();
        }

        public void SetInfiniteChi(bool on)
        {
            if (!HasAuthority) return;
            infiniteChi = on;
            Push();
        }

        public void SetNoCooldowns(bool on)
        {
            if (!HasAuthority) return;
            noCooldowns = on;
            Push();
        }

        public void SetSpeedMultiplier(float multiplier)
        {
            if (!HasAuthority) return;
            speedMultiplier = Mathf.Clamp(multiplier, 0.1f, 20f);
            Push();
        }

        /// <summary>Adds a flat bonus to a stat; 0 removes it.</summary>
        public void SetFlatStat(StatType stat, float value)
        {
            if (!HasAuthority) return;
            if (Mathf.Approximately(value, 0f)) flatStats.Remove(stat);
            else flatStats[stat] = value;
            ApplyStatModifiers();
        }

        public void SetOneShot(bool on)
        {
            if (!HasAuthority) return;
            oneShot = on;
            ApplyStatModifiers();
        }

        public void ClearStatBonuses()
        {
            if (!HasAuthority) return;
            flatStats.Clear();
            oneShot = false;
            ApplyStatModifiers();
        }

        /// <summary>Turns off every toggle and stat bonus.</summary>
        public void ResetAll()
        {
            if (!HasAuthority) return;
            godMode = false;
            infiniteChi = false;
            noCooldowns = false;
            speedMultiplier = 1f;
            Push();
            ClearStatBonuses();
        }

        private void ApplyStatModifiers()
        {
            if (!TryGetComponent<CharacterCombatStats>(out var stats)) return;
            var mods = new List<StatModifier>(flatStats.Count + 1);
            foreach (var kv in flatStats)
                mods.Add(new StatModifier(kv.Key, ModifierType.Flat, kv.Value, statSource));
            if (oneShot)
                mods.Add(new StatModifier(StatType.AttackPower, ModifierType.PercentMult, OneShotPercent, statSource));
            stats.SetSourceModifiers(statSource, mods);
        }
    }
}
