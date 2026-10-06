using KryticalNova.Core;
using KryticalNova.NPC;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>
    /// The Deep Vault: storage shared by every character of the account, opened at a Wayfinder. Saved with the
    /// account, so what one character puts in the next one finds. Only one character of an account is in the world
    /// at a time, so two never open it at once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerDeepVault : PlayerStorage
    {
        public override StorageKind Kind => StorageKind.DeepVault;
        public override string DisplayName => "Deep Vault";
        public override NpcOffer Offer => NpcOffer.DeepVault;
        protected override string WhereReason => "Visit a Wayfinder to use your Deep Vault.";
        public override string SaveKey => "deepvault";
        public override SaveScope SaveScope => SaveScope.Account;
    }
}
