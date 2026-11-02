namespace KryticalNova.Core
{
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
    }
}
