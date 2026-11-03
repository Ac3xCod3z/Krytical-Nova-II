using System.Collections.Generic;

namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's Weaves and dyes, for the entity netcode (M8 8.3.9): the network runs its owner's requests on the server body. Here,
    /// below Restyle, so the network need not know Restyle (as <see cref="IBodyStorage"/>).
    /// </summary>
    public interface IBodyWeaves
    {
        long WeaveFee { get; }
        void RunWeaveOp(KryticalNova.Core.WeaveOpKind op, byte slot, ushort id, byte stage);
    }

    /// <summary>A player body's Weave library (account-wide; M8 8.3.10): the server's copy goes to its owner's view in pages.</summary>
    public interface IBodyWeaveLibrary
    {
        /// <summary>Goes up whenever a look is added or the library is replaced (the server's and an offline one).</summary>
        int Version { get; }
        IReadOnlyCollection<ushort> Lines { get; }
        IReadOnlyCollection<uint> Morphs { get; }
        /// <summary>The owner's own view: the server's library.</summary>
        void MirrorLibrary(ushort[] lines, uint[] morphs);
    }
}
