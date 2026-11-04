using UnityEngine;

namespace KryticalNova.Items
{
    /// <summary>What a pet show is (NGO's PlayerPet RPCs to other machines): a trick its player's game took, a GM's animation, an evolution.</summary>
    public enum BodyPetShowKind : byte
    {
        Trick = 1,
        Animation = 2,
        Evolved = 3,
    }

    /// <summary>
    /// A player body's pet, for the entity netcode (M8 8.4.7): the server body decides growth and hunger by NGO's PlayerPet (the pet's state is
    /// the worn item's data, which is real), every view shows the server's growth and fed from the ghost, and the tricks one game takes are
    /// played on everyone else's. Here, below Pets, as <see cref="IBodyEmotes"/>.
    /// </summary>
    public interface IBodyPet
    {
        /// <summary>The growth percent and fed the body shows (the server's own, or what a view was told).</summary>
        byte ShownGrowth { get; }
        bool ShownFed { get; }
        /// <summary>Any view: the growth and fed the server gives this player's pet (NGO's netState, read by everyone).</summary>
        void MirrorShown(byte growth, bool fed);
        /// <summary>The server body: a trick its owner's game took (NGO's ShareTrickRpc body, after the Cosmetic limit and the pet's pace).</summary>
        void RunTrick(int nameHash);
        /// <summary>Any view: a show the server passed on (NGO's OthersPlayTrickRpc, PlayAnimationRpc, EvolvedRpc).</summary>
        void PlayShown(BodyPetShowKind kind, int value);
    }

    /// <summary>Server: how a server body's pet shows reach the other machines (and, when <c>toOwner</c>, its own player's too).</summary>
    public interface IPetShowLink
    {
        void Show(Component part, BodyPetShowKind kind, int value, bool toOwner);
    }

    /// <summary>The server's <see cref="IPetShowLink"/>; null where there is no entity netcode server.</summary>
    public static class PetShowLink
    {
        public static IPetShowLink Current;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
