using KryticalNova.Wallet;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// A Crew founding (Social, below Economy) writes its ledger line through <see cref="LedgerNote"/>, which Economy's
    /// ledger fills when the game loads (architecture stage 4). Empty, the line would not be kept: the kept ledger
    /// must keep every economy action (owner, 18 Oct 2026).
    /// </summary>
    public sealed class LedgerNoteTests
    {
        [Test]
        public void TheLedgerWritesNotesFromBelowEconomy() =>
            Assert.IsNotNull(LedgerNote.Writer, "Economy's ledger fills LedgerNote when the game loads");
    }
}
