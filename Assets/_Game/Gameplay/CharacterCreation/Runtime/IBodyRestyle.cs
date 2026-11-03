using UnityEngine;

namespace KryticalNova.CharacterCreation
{
    /// <summary>
    /// Where a client view of the entity netcode sends a restyle (M8 8.3.8): the look is larger than one message, so the network sends it in
    /// pages. Implemented by the same object as the view's <c>IBodyRequests</c>. Here because the look (<see cref="CharacterRecipe"/>) lives here.
    /// </summary>
    public interface IRestyleRequests
    {
        void Restyle(byte service, in CharacterRecipe proposed);
    }

    /// <summary>A player body's restyle for the entity netcode: the server body runs it, the owner's view is told it is done.</summary>
    public interface IBodyRestyle
    {
        /// <summary>The item one change at a service uses up (a Restyle Pass, a Training Pass), or 0 when it is free.</summary>
        ushort PassFor(byte service);
        void RunRestyle(byte service, in CharacterRecipe proposed);
        void MirrorRestyled();
    }

    /// <summary>Server: how a server body's finished restyle reaches its player (NGO's RestyledRpc).</summary>
    public interface IRestyleOwnerLink
    {
        void Restyled(Component part);
    }

    /// <summary>The server's <see cref="IRestyleOwnerLink"/>; null where there is no entity netcode server.</summary>
    public static class RestyleOwnerLink
    {
        public static IRestyleOwnerLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
