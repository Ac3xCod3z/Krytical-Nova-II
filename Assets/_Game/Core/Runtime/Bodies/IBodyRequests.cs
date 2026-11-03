namespace KryticalNova.Core
{
    /// <summary>What a loadout request asks the server's body to do (NGO's <c>PlayerLoadouts</c> RPCs; M8 8.1.3).</summary>
    public enum LoadoutOpKind : byte
    {
        Save = 1,
        Rename = 2,
        Delete = 3,
        Wear = 4,
    }

    /// <summary>What a Pack request asks the server's body to do (NGO's <c>PlayerInventory</c> RPCs; M8 8.1.2).</summary>
    public enum PackOpKind : byte
    {
        Move = 1,
        QuickEquip = 2,
        QuickUnequip = 3,
        Destroy = 4,
        Sort = 5,
    }

    /// <summary>
    /// Where the local view body of the entity netcode sends what only the server may do to the player (owner, 2 Nov, feel test item 5):
    /// using an item, casting a skill. The view body's rules are a stub; the server runs the real ones on its own body. Filled on the
    /// <see cref="BodyRoleTag"/> by the entity netcode (like <see cref="BodyRoleTag.Mover"/>); null everywhere else, where the body
    /// decides itself as before.
    /// </summary>
    public interface IBodyRequests
    {
        /// <summary>Asks the server to use one of the item (the first Pack cell that holds it, as NGO's hotbar does).</summary>
        void UseItem(ushort itemId);

        /// <summary>Asks the server to cast a skill on the player's own body (a buff, Heavenly Serenity).</summary>
        void CastSkill(string skillId);

        /// <summary>
        /// Asks the server to move, equip, unequip, destroy or sort in the Pack, by cell as NGO's RPCs did (a place is a container and an index:
        /// the Pack's cell, or an equipment slot); the server's body runs the game's rules and the mirror shows the result.
        /// </summary>
        void PackOp(PackOpKind kind, byte fromContainer, byte fromIndex, byte toContainer, byte toIndex);

        /// <summary>Asks the server to save, rename, delete or wear a loadout slot (the name only for save and rename).</summary>
        void LoadoutOp(LoadoutOpKind kind, byte index, string name);

        /// <summary>Asks the server to refill every Flask in the Pack at the Apothecary in reach (M8 8.1.4).</summary>
        void RefillFlask();
    }
}
