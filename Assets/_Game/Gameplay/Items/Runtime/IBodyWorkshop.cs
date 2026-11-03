using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>
    /// A player body's Forge, for the entity netcode (M8 8.3.6): the network runs its owner's requests on the server body and shows what
    /// became of the piece on the owner's view. Here, below Forge, so the network need not know Forge (as <see cref="IBodyStorage"/>).
    /// </summary>
    public interface IBodyGearWork
    {
        /// <summary>The numbers the Forge works by (tests and the window read them).</summary>
        GearProgressionSettings GearSettings { get; }
        void RunGearWork(KryticalNova.Core.GearWorkOpKind op, int cell, int cell2, ushort alloy, ushort protection);
        /// <summary>The owner's own view: what became of the piece (NGO's FinishedRpc, with the burst for this player).</summary>
        void MirrorGearWorked(byte kind, byte outcome, ItemStack before, ItemStack after);
    }

    /// <summary>A player body's Fabricator, for the entity netcode (M8 8.3.7), as <see cref="IBodyGearWork"/>.</summary>
    public interface IBodyCrafting
    {
        void RunCraft(ushort recipeId, int times);
        /// <summary>The owner's own view: what the server made (NGO's CraftedRpc).</summary>
        void MirrorCrafted(ushort recipeId, int times);
    }

    /// <summary>Server: how a server body's Forge and Fabricator results reach its player, and the Forge's burst everyone sees.</summary>
    public interface IWorkshopOwnerLink
    {
        void GearWorked(Component part, byte kind, byte outcome, ItemStack before, ItemStack after);
        void Crafted(Component part, ushort recipeId, int times);
    }

    /// <summary>The server's <see cref="IWorkshopOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class WorkshopOwnerLink
    {
        public static IWorkshopOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
