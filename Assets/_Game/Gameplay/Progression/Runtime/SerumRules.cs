using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>
    /// How many of each kind of Serum a character has taken: one count per kind, in the order of
    /// <see cref="SerumSettings.serums"/>. Small and fixed so it replicates to everyone, who each work the stat
    /// bonuses out from it. (The save keeps the counts by item id instead, so the order may change.)
    /// </summary>
    public struct SerumState : INetworkSerializable, IEquatable<SerumState>
    {
        private ushort k0, k1, k2, k3, k4, k5, k6, k7;

        public int Taken(int kind)
        {
            switch (kind)
            {
                case 0: return k0;
                case 1: return k1;
                case 2: return k2;
                case 3: return k3;
                case 4: return k4;
                case 5: return k5;
                case 6: return k6;
                case 7: return k7;
                default: return 0;
            }
        }

        public SerumState With(int kind, int taken)
        {
            SerumState next = this;
            ushort value = (ushort)Mathf.Clamp(taken, 0, ushort.MaxValue);
            switch (kind)
            {
                case 0: next.k0 = value; break;
                case 1: next.k1 = value; break;
                case 2: next.k2 = value; break;
                case 3: next.k3 = value; break;
                case 4: next.k4 = value; break;
                case 5: next.k5 = value; break;
                case 6: next.k6 = value; break;
                case 7: next.k7 = value; break;
            }
            return next;
        }

        public bool Any => (k0 | k1 | k2 | k3 | k4 | k5 | k6 | k7) != 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref k0);
            serializer.SerializeValue(ref k1);
            serializer.SerializeValue(ref k2);
            serializer.SerializeValue(ref k3);
            serializer.SerializeValue(ref k4);
            serializer.SerializeValue(ref k5);
            serializer.SerializeValue(ref k6);
            serializer.SerializeValue(ref k7);
        }

        public bool Equals(SerumState other) =>
            k0 == other.k0 && k1 == other.k1 && k2 == other.k2 && k3 == other.k3 && k4 == other.k4 && k5 == other.k5 && k6 == other.k6 && k7 == other.k7;

        public override bool Equals(object other) => other is SerumState state && Equals(state);
        public override int GetHashCode() => HashCode.Combine(k0, k1, k2, k3, k4, k5, k6, k7);
    }

    /// <summary>
    /// The rules of Serums: a kind can be taken until the character has had the cap of it, and every one taken adds
    /// the kind's gains for good. Pure rules; <see cref="PlayerSerums"/> applies them where the game is decided.
    /// </summary>
    public static class SerumRules
    {
        public const string ReasonNoSettings = "Serums are not set up.";

        /// <summary>How many kinds the settings name (never more than a record holds).</summary>
        public static int KindCount(SerumSettings settings) =>
            settings?.serums == null ? 0 : Mathf.Min(settings.serums.Length, SerumSettings.MaxKinds);

        /// <summary>Which kind an item is, or -1 when it is no Serum.</summary>
        public static int KindOf(SerumSettings settings, ushort itemId)
        {
            int count = KindCount(settings);
            for (int i = 0; i < count; i++)
                if (settings.serums[i].itemId == itemId && itemId != 0) return i;
            return -1;
        }

        /// <summary>Why one more of a kind cannot be taken (shown to the player), or null when it can.</summary>
        public static string UseBlockedReason(SerumSettings settings, int kind, in SerumState state)
        {
            if (settings == null) return ReasonNoSettings;
            if (kind < 0 || kind >= KindCount(settings)) return "That is not a Serum.";
            int cap = settings.Cap;
            return state.Taken(kind) >= cap ? $"You have taken all {cap} {settings.serums[kind].displayName}s a body can hold." : null;
        }

        /// <summary>The state after one more of a kind. Unchanged when the cap is reached: check <see cref="UseBlockedReason"/> first.</summary>
        public static SerumState Take(SerumSettings settings, int kind, in SerumState state)
        {
            if (UseBlockedReason(settings, kind, state) != null) return state;
            return state.With(kind, state.Taken(kind) + 1);
        }

        /// <summary>A count as it may stand: never below zero or above the cap in force.</summary>
        public static int Clamp(SerumSettings settings, int taken) => Mathf.Clamp(taken, 0, settings != null ? settings.Cap : 0);

        /// <summary>What a character has from one gain of a kind, having taken <paramref name="taken"/> of it.</summary>
        public static float Total(in SerumGain gain, int taken) => gain.perUse * Mathf.Max(0, taken);

        /// <summary>The stat bonuses of everything taken, as flat modifiers.</summary>
        public static void AddModifiers(SerumSettings settings, in SerumState state, object source, List<StatModifier> results)
        {
            if (settings == null || results == null) return;
            int count = KindCount(settings);
            for (int i = 0; i < count; i++)
            {
                int taken = Mathf.Min(state.Taken(i), settings.Cap);
                if (taken <= 0 || settings.serums[i].gains == null) continue;
                foreach (SerumGain gain in settings.serums[i].gains)
                    if (gain.perUse > 0f) results.Add(new StatModifier(gain.stat, ModifierType.Flat, Total(gain, taken), source));
            }
        }

        /// <summary>What a kind gives for <paramref name="taken"/> of it: "+3 Attack Power", "+2 Hit Rate, +2 Evasion".</summary>
        public static string Gains(in SerumKind kind, int taken)
        {
            if (kind.gains == null) return string.Empty;
            var text = new StringBuilder();
            foreach (SerumGain gain in kind.gains)
            {
                if (text.Length > 0) text.Append(", ");
                text.Append(StatDisplay.FormatModifier(gain.stat, ModifierType.Flat, Total(gain, taken)));
            }
            return text.ToString();
        }

        /// <summary>The Character Sheet's line for a kind: "Strength Serum (37/100) +111 Attack Power".</summary>
        public static string SheetLine(SerumSettings settings, int kind, in SerumState state)
        {
            SerumKind serum = settings.serums[kind];
            int taken = state.Taken(kind);
            return $"{serum.displayName} ({taken}/{settings.Cap}) {Gains(serum, taken)}";
        }
    }
}
