namespace KryticalNova.Items
{
    /// <summary>
    /// A storage of a player's body (the Vault, the Deep Vault), for the entity netcode (M8 8.2.1): the network reads its cells and Creds on
    /// the server body, runs its owner's requests there, and shows them on the owner's own view. Here, below Economy, so the network need not
    /// know Economy (Economy already uses the save system).
    /// </summary>
    public interface IBodyStorage
    {
        /// <summary>0 = the Vault, 1 = the Deep Vault (Economy's StorageKind).</summary>
        byte StorageKind { get; }
        int CellCount { get; }
        ItemStack Get(int cell);
        long Creds { get; }
        void RunStorageOp(KryticalNova.Core.StorageOpKind op, int a, int b, long amount);
        void MirrorCell(int cell, ItemStack stack);
        void MirrorCreds(long creds);
    }
}
