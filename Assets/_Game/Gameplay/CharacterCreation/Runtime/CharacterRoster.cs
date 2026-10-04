using System;

namespace KryticalNova.CharacterCreation
{
    /// <summary>The player's saved characters: a fixed number of slots and which one was played last.</summary>
    [Serializable]
    public sealed class CharacterRoster
    {
        public const int SlotCount = 3;
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public CharacterProfile[] slots = new CharacterProfile[SlotCount];
        public int lastPlayedSlot = -1;

        public int Count
        {
            get
            {
                int count = 0;
                for (int i = 0; i < SlotCount; i++) if (IsUsed(i)) count++;
                return count;
            }
        }

        public bool IsUsed(int slot) => slot >= 0 && slot < SlotCount && slots[slot] != null && slots[slot].IsUsed;

        public CharacterProfile Get(int slot) => IsUsed(slot) ? slots[slot] : null;

        /// <summary>The first empty slot, or -1 when all are used.</summary>
        public int FirstFree
        {
            get
            {
                for (int i = 0; i < SlotCount; i++) if (!IsUsed(i)) return i;
                return -1;
            }
        }

        /// <summary>The character to preselect: the last one played, else the first one that exists.</summary>
        public int PreferredSlot
        {
            get
            {
                if (IsUsed(lastPlayedSlot)) return lastPlayedSlot;
                for (int i = 0; i < SlotCount; i++) if (IsUsed(i)) return i;
                return -1;
            }
        }

        public bool Set(int slot, CharacterProfile profile)
        {
            if (slot < 0 || slot >= SlotCount || profile == null || !profile.IsUsed) return false;
            slots[slot] = profile;
            return true;
        }

        public void Clear(int slot)
        {
            if (slot < 0 || slot >= SlotCount) return;
            slots[slot] = null;
            if (lastPlayedSlot == slot) lastPlayedSlot = -1;
        }

        /// <summary>Stamps the slot as played now and remembers it for the next launch.</summary>
        public void MarkPlayed(int slot)
        {
            if (!IsUsed(slot)) return;
            slots[slot].lastPlayed = DateTime.UtcNow.Ticks;
            lastPlayedSlot = slot;
        }

        /// <summary>True when another of this player's own characters already uses the name (case-insensitive).</summary>
        public bool IsNameTaken(string characterName, int exceptSlot = -1)
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (i == exceptSlot || !IsUsed(i)) continue;
                if (string.Equals(slots[i].name, characterName, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>JSON round trips turn a missing slot into an empty profile; make the array the right length again.</summary>
        public void Normalize()
        {
            if (slots == null || slots.Length != SlotCount)
            {
                var fixedSlots = new CharacterProfile[SlotCount];
                if (slots != null) Array.Copy(slots, fixedSlots, Math.Min(slots.Length, SlotCount));
                slots = fixedSlots;
            }
            if (!IsUsed(lastPlayedSlot)) lastPlayedSlot = -1;
        }
    }
}
