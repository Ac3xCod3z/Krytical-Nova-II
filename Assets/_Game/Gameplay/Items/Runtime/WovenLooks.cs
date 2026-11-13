using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// One Weave a character keeps (build 17 item 6, owner: "the Weave always holds"): a weapon Weave belongs to a weapon CLASS, an outfit
    /// Weave to the gear LINE of the piece it was put on. Switching weapons or sets never loses one and never spreads one to another
    /// class or set; the slot shows the Weave of what is worn now. Kept on the server body only (the ghost carries what is shown).
    /// </summary>
    [Serializable]
    public struct WovenLook
    {
        public const byte KindClass = 1, KindLine = 2, KindItem = 3;
        /// <summary><see cref="KindClass"/> (key = the weapon class), <see cref="KindLine"/> (key = the gear line) or, for a piece with
        /// neither (lineId 0, no class: about 221 pieces today), <see cref="KindItem"/> (key = the piece's own item id), so its Weave is
        /// never dropped (Master, 13 Nov 2026).</summary>
        public byte kind;
        public ushort key;
        /// <summary>The item that stands for the look shown.</summary>
        public ushort item;
    }

    /// <summary>A Weave from a save made before Weaves had keys (version 1): it is bound to a key once the worn pieces are known.</summary>
    [Serializable]
    public struct UnboundWeave
    {
        public EquipSlot slot;
        public ushort item;
    }

    /// <summary>The pure rules of kept Weaves (build 17 item 6).</summary>
    public static class WovenLooks
    {
        /// <summary>The key a worn piece's Weave is kept under: its weapon class, its gear line, or else its own item id. False when it
        /// takes no Weave.</summary>
        public static bool TryKeyFor(ItemDefinition worn, out byte kind, out ushort key)
        {
            kind = 0;
            key = 0;
            if (worn == null || !WeaveRules.IsWeaveSlot(worn.equipSlot)) return false;
            if (worn.equipSlot == EquipSlot.Weapon)
            {
                if (worn.weaponClass == 0) return ByItem(worn, out kind, out key);
                kind = WovenLook.KindClass;
                key = (ushort)worn.weaponClass;
                return true;
            }
            if (worn.lineId == 0) return ByItem(worn, out kind, out key);
            kind = WovenLook.KindLine;
            key = worn.lineId;
            return true;
        }

        // The fallback key: the exact piece (it has no class and no line to share a Weave with).
        private static bool ByItem(ItemDefinition worn, out byte kind, out ushort key)
        {
            kind = WovenLook.KindItem;
            key = worn.itemId;
            return key != 0;
        }

        /// <summary>The look item kept for what is worn, or 0.</summary>
        public static ushort WeaveFor(IReadOnlyList<WovenLook> woven, ItemDefinition worn)
        {
            if (woven == null || !TryKeyFor(worn, out byte kind, out ushort key)) return 0;
            for (int i = 0; i < woven.Count; i++)
                if (woven[i].kind == kind && woven[i].key == key) return woven[i].item;
            return 0;
        }

        /// <summary>Keeps <paramref name="item"/> under a key (0 takes it off). True when the list changed.</summary>
        public static bool Set(List<WovenLook> woven, byte kind, ushort key, ushort item)
        {
            for (int i = 0; i < woven.Count; i++)
            {
                if (woven[i].kind != kind || woven[i].key != key) continue;
                if (woven[i].item == item) return false;
                if (item == 0) woven.RemoveAt(i);
                else woven[i] = new WovenLook { kind = kind, key = key, item = item };
                return true;
            }
            if (item == 0) return false;
            woven.Add(new WovenLook { kind = kind, key = key, item = item });
            return true;
        }

        /// <summary>
        /// Binds old slot Weaves (decision 6-B a): a weapon Weave to its own look item's class (it could only ever be put on that class); an
        /// outfit Weave to the line of the piece worn in its slot now, or it waits when the slot is empty. <paramref name="wornIn"/> gives
        /// the piece in a slot (null = empty), <paramref name="lookOf"/> the definition of a look item. True when anything was bound.
        /// </summary>
        public static bool Bind(List<UnboundWeave> unbound, List<WovenLook> woven, Func<EquipSlot, ItemDefinition> wornIn, Func<ushort, ItemDefinition> lookOf)
        {
            bool changed = false;
            for (int i = unbound.Count - 1; i >= 0; i--)
            {
                UnboundWeave old = unbound[i];
                ItemDefinition look = lookOf(old.item);
                if (look == null || look.equipSlot != old.slot || look.lineId == 0) { unbound.RemoveAt(i); changed = true; continue; }
                ItemDefinition keyPiece = old.slot == EquipSlot.Weapon ? look : wornIn(old.slot);
                if (keyPiece == null || keyPiece.equipSlot != old.slot) continue;   // the slot is empty: it waits
                if (TryKeyFor(keyPiece, out byte kind, out ushort key) && WeaveFor(woven, keyPiece) == 0) Set(woven, kind, key, old.item);
                unbound.RemoveAt(i);
                changed = true;
            }
            return changed;
        }
    }

    /// <summary>
    /// The save steps of the "weave" part (build 17 item 6, the game's first real <c>IMigratingSave</c>). Plain C#: old text in, new text out.
    /// Version 1 kept one Weave per slot; version 2 keeps them by key (<see cref="WovenLook"/>) and carries the old ones as
    /// <see cref="UnboundWeave"/> until the worn pieces are known. Nothing is charged, nothing dropped; unreadable text throws, so the load is
    /// refused and the save kept (never reset).
    /// </summary>
    public static class WeaveSaveSteps
    {
        public const int Version = 2;

        [Serializable]
        public sealed class V1
        {
            public EquipmentLooks looks;
            public bool collectedOwned;
        }

        [Serializable]
        public sealed class V2
        {
            /// <summary>Morph and the helmet / cape switches; its five Weave numbers are not read (the server works them out).</summary>
            public EquipmentLooks looks;
            public List<WovenLook> woven = new List<WovenLook>();
            public List<UnboundWeave> unbound = new List<UnboundWeave>();
            public bool collectedOwned;
        }

        public static string Migrate(string json, int from)
        {
            if (from == 1) return OneToTwo(json);
            throw new InvalidOperationException("weave save: no step from version " + from);
        }

        public static string OneToTwo(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.TrimStart()[0] != '{') throw new FormatException("weave save: not a document");
            V1 one = JsonUtility.FromJson<V1>(json) ?? throw new FormatException("weave save: unreadable");
            var two = new V2 { looks = one.looks, collectedOwned = one.collectedOwned };
            foreach (EquipSlot slot in WeaveRules.WeaveSlots)
            {
                ushort item = one.looks.GetWeave(slot);
                if (item != 0) two.unbound.Add(new UnboundWeave { slot = slot, item = item });
                two.looks.SetWeave(slot, 0);
            }
            return JsonUtility.ToJson(two);
        }
    }
}
