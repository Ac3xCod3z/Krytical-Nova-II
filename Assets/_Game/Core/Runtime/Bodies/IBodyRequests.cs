namespace KryticalNova.Core
{
    /// <summary>What a Forge request asks the server's body to do (NGO's <c>PlayerGearWork</c> RPCs; M8 8.3.6).</summary>
    public enum GearWorkOpKind : byte
    {
        Temper = 1,
        Fuse = 2,
        Tune = 3,
        ResetTuning = 4,
        Reforge = 5,
    }

    /// <summary>One stack put up in a Kiosk (NGO's <c>KioskOffer</c>, M7.6): the Pack cell, how many (0 = the whole stack), the price of one.</summary>
    public struct BodyKioskOffer
    {
        public short cell;
        public ushort count;
        public long unitPrice;
    }

    /// <summary>What a trade request asks the server's body to do (NGO's <c>PlayerTrade</c> RPCs; M8 8.2.3).</summary>
    public enum TradeOpKind : byte
    {
        RequestByName = 1,
        Respond = 2,
        Offer = 3,
        Withdraw = 4,
        SetCreds = 5,
        Lock = 6,
        Confirm = 7,
        Cancel = 8,
    }

    /// <summary>What a storage request asks the server's body to do (NGO's <c>PlayerStorage</c> RPCs; M8 8.2.1).</summary>
    public enum StorageOpKind : byte
    {
        Deposit = 1,
        Withdraw = 2,
        Move = 3,
        Sort = 4,
        DepositCreds = 5,
        WithdrawCreds = 6,
    }

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

        /// <summary>Asks the server to put up to <paramref name="count"/> stat points into an attribute (M8 8.3.1, NGO's allocation RPC).</summary>
        void Allocate(byte attribute, short count);

        /// <summary>Asks the server to buy up to <paramref name="count"/> ranks of a skill with skill points (M8 8.3.2, NGO's upgrade RPC).</summary>
        void UpgradeSkill(string skillId, byte count);

        /// <summary>Asks the server to buy up to <paramref name="count"/> ranks of Run or Jump (its id as a number; NGO's movement upgrade RPC).</summary>
        void UpgradeMovement(byte skill, byte count);

        /// <summary>Asks the server to pick up the drop with this server id (M7.5, NGO's pickup RPC).</summary>
        void Pickup(uint dropId);

        /// <summary>Asks the server to put an item from the Pack or a slot on the ground (NGO's drop RPC).</summary>
        void DropItem(byte container, byte index);

        /// <summary>
        /// Asks the server to deposit, withdraw, move or sort in the Vault (kind 0) or the Deep Vault (kind 1), or to move Creds in or out:
        /// <paramref name="a"/> and <paramref name="b"/> are NGO's cells, <paramref name="amount"/> the count or the Creds.
        /// </summary>
        void StorageOp(byte kind, StorageOpKind op, short a, short b, long amount);

        /// <summary>Asks the server to buy up to <paramref name="count"/> of an item from the vendor (its stock's NPC by name; M8 8.2.2, NGO's buy RPC).</summary>
        void ShopBuy(string vendor, ushort itemId, ushort count);

        /// <summary>
        /// Asks the server to sell <paramref name="count"/> from a Pack cell to the vendor in reach (NGO's sell RPC): the stack the player saw
        /// (<paramref name="itemId"/>, <paramref name="stackCount"/>, <paramref name="data"/>) and whether they confirmed the sale.
        /// </summary>
        void ShopSell(ushort cell, ushort count, ushort itemId, ushort stackCount, ulong data, bool confirmed);

        /// <summary>
        /// Asks the server to ask, answer, offer, withdraw, set Creds, lock, confirm or cancel in a trade (M8 8.2.3, NGO's trade RPCs):
        /// <paramref name="name"/> the player asked (by name, as NGO's /trade name), <paramref name="a"/> the Pack cell or the line,
        /// <paramref name="b"/> the count, <paramref name="creds"/> the Creds, <paramref name="flag"/> accept or lock.
        /// </summary>
        void TradeOp(TradeOpKind op, string name, short a, ushort b, long creds, bool flag);

        /// <summary>Asks the server to open a Kiosk where the player stands (M7.6, NGO's OpenRpc): its name, the stacks, the duration's index.</summary>
        void KioskOpen(string shopName, BodyKioskOffer[] offers, byte tier);

        /// <summary>Asks the server to close the player's Kiosk and take back what is left (NGO's CloseRpc).</summary>
        void KioskClose();

        /// <summary>Asks the server for the Kiosk's till (and of a closed Kiosk the unsold items; NGO's CollectRpc).</summary>
        void KioskCollect();

        /// <summary>
        /// Asks the server to buy <paramref name="count"/> from listing <paramref name="index"/> of the stand with this server id (NGO's BuyRpc):
        /// the listing as the player saw it (<paramref name="itemId"/>, <paramref name="stackCount"/>, <paramref name="data"/>, <paramref name="unitPrice"/>).
        /// </summary>
        void KioskBuy(uint standId, byte index, ushort itemId, ushort stackCount, ulong data, long unitPrice, ushort count);

        /// <summary>
        /// Asks the server to work a piece at the Forge (M8 8.3.6, NGO's five RPCs): <paramref name="cell"/> the piece's Pack cell,
        /// <paramref name="cell2"/> Fusion's material cell, <paramref name="alloy"/> and <paramref name="protection"/> Temper's items.
        /// </summary>
        void GearWork(GearWorkOpKind op, short cell, short cell2, ushort alloy, ushort protection);

        /// <summary>Asks the server to make a recipe <paramref name="times"/> times at the Fabricator (M8 8.3.7, NGO's CraftRpc).</summary>
        void Craft(ushort recipeId, ushort times);
    }
}
