namespace KryticalNova.Items
{
    /// <summary>
    /// On a player that can be paid Creds picked up off the ground. Creds lie in the world as a drop like any
    /// other, but they never enter the Pack: whoever takes them is paid here instead. Runs where the game is
    /// decided.
    /// </summary>
    public interface ICurrencyReceiver
    {
        /// <summary>Pays what fits and returns how much that was (less than asked when the wallet is nearly full).</summary>
        long ServerReceiveCreds(long amount);
    }
}
