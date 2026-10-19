namespace KryticalNova.Wallet
{
    /// <summary>
    /// A player's Creds as the systems below Economy reach them (Nova's Ascension, the Flask's refill, founding a Crew):
    /// how many there are, whether a price can be paid, and paying it. <c>PlayerWallet</c> (Economy) is the one there
    /// is, and every Cred still goes through it (owner, 18 Oct 2026: a new source or sink is a new
    /// <see cref="CredsSource"/> / <see cref="CredsSink"/>, never a side door).
    /// </summary>
    public interface IWallet
    {
        long Creds { get; }

        /// <summary>True when the wallet holds at least <paramref name="amount"/>.</summary>
        bool CanAfford(long amount);

        /// <summary>Server: takes <paramref name="amount"/> for <paramref name="sink"/>; false when it is not there.</summary>
        bool ServerSpend(long amount, CredsSink sink);
    }
}
