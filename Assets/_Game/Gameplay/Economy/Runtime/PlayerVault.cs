using KryticalNova.Core;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>The Vault: this character's own storage, opened at a Vaultkeeper. Saved with the character.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerVault : PlayerStorage
    {
        public override StorageKind Kind => StorageKind.Vault;
        public override string DisplayName => "Vault";
        public override NpcOffer Offer => NpcOffer.Vault;
        protected override string WhereReason => "Visit a Vaultkeeper to use your Vault.";
        public override string SaveKey => "vault";
        public override SaveScope SaveScope => SaveScope.Character;
    }
}
