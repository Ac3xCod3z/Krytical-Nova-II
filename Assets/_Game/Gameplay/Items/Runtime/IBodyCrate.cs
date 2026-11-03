using System.Collections.Generic;

namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's Furnishing Crate, for the entity netcode (M8 group 4): the server body runs its owner's store and withdraw and sends the
    /// owner the Crate; the owner's view shows it. Here, below Housing, as <see cref="IBodyStorage"/>.
    /// </summary>
    public interface IBodyCrate
    {
        /// <summary>Goes up whenever what is in the Crate changes (the server's and an offline one).</summary>
        int Version { get; }
        /// <summary>True when this furnishing may go in this character's Crate (NGO's catalog and faction rules).</summary>
        bool MayStore(ushort itemId);
        int Count(ushort itemId);
        void ReadEntries(List<(ushort itemId, int count)> into);
        void RunStore(int packCell, int count);
        void RunWithdraw(ushort itemId, int count);
        void MirrorEntries(IReadOnlyList<(ushort itemId, int count)> shown);
    }
}
