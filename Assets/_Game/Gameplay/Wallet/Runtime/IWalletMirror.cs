namespace KryticalNova.Wallet
{
    /// <summary>
    /// The owner's own view of a wallet on a client of the entity netcode (M7/M8 system 0, owner Q1, 2 Nov 2026): it shows the balance the
    /// server sent, as NGO's owner-only network variable did, and never decides one.
    /// </summary>
    public interface IWalletMirror
    {
        void MirrorCreds(long creds);
    }
}
