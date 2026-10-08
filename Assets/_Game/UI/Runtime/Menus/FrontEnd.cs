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

        /// <summary>Closes the game. In the Editor it leaves Play mode.</summary>
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Where a scene is in the menus' comic book. Going to a later page turns the page forward, to an earlier
        /// one turns it back. Anything that is not a menu (the world) comes after the last page.
        /// </summary>
        public static int PageOf(string scene)
        {
            switch (scene)
            {
                case TitleScene: return 0;
                case CharacterSelectScene: return 1;
                case FactionSelectScene: return 2;
                case CharacterCreatorScene: return 3;
                case LoadingScene: return 4;
                default: return 5;
            }
        }

        /// <summary>Go to another front-end scene by a turn of the page.</summary>
        public static void Go(string scene)
        {
            var transition = MenuTransition.Current;
            if (transition != null) transition.LoadScene(scene);
            else SceneManager.LoadScene(scene);
        }
    }
}
