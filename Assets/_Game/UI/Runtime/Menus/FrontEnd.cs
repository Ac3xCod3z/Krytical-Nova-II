using KryticalNova.Factions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The front-end flow. Each step is its own scene:
    ///   first launch  Title -> FactionSelect -> CharacterCreator -> CharacterSelect -> Loading -> world
    ///   returning     Title -> CharacterSelect (last played preselected) -> Loading -> world
    /// Picking an empty slot on CharacterSelect runs FactionSelect -> CharacterCreator for that exact slot.
    /// What is being created travels between scenes in the static fields below.
    /// </summary>
    public static class FrontEnd
    {
        public const string TitleScene = "Title";
        public const string FactionSelectScene = "FactionSelect";
        public const string CharacterCreatorScene = "CharacterCreator";
        public const string CharacterSelectScene = "CharacterSelect";
        public const string LoadingScene = "Loading";
        public const string WorldScene = "Zone01_StarterTown";

        /// <summary>The slot the new character is being made for; -1 uses the first free slot.</summary>
        public static int PendingSlot { get; set; } = -1;
        /// <summary>The faction picked on the faction screen, on its way to the creator.</summary>
        public static FactionId PendingFaction { get; set; }
        /// <summary>The slot character select should open on; -1 uses the last-played character.</summary>
        public static int HighlightSlot { get; set; } = -1;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            PendingSlot = -1;
            PendingFaction = FactionId.None;
            HighlightSlot = -1;
        }

        /// <summary>Go to another front-end scene through the halftone wipe.</summary>
        public static void Go(string scene)
        {
            var transition = MenuTransition.Current;
            if (transition != null) transition.LoadScene(scene);
            else SceneManager.LoadScene(scene);
        }
    }
}
