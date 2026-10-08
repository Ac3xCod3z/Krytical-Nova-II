using System;
using KryticalNova.Factions;
using UnityEngine;

namespace KryticalNova.UI
{
    /// <summary>The Menu Theme setting: which faction's skin the menus wear.</summary>
    public enum MenuTheme
    {
        /// <summary>One of the three, picked at random each time the game is started.</summary>
        Rotate = 0,
        Hegemony = 1,
        Null = 2,
        Covenant = 3
    }

    /// <summary>
    /// The skin of the menus (the title flow, and Settings when it is opened from them). With Menu Theme on Rotate it
    /// is one of the three factions', picked at random once each time the game is started and kept for that whole
    /// run, also after a character logs out and the menus come back. A faction chosen in Menu Theme locks the menus to
    /// that skin at once; back on Rotate they show this run's pick again and a new one is made the next time the
    /// game is started. The in-game UI never asks this: it wears the character's own faction (<see cref="FactionSkin"/>).
    /// </summary>
    public static class MenuSkin
    {
        private static FactionId rolled;
        private static FactionId shown;

        /// <summary>Raised when the skin the menus wear changes (the Menu Theme setting was changed).</summary>
        public static event Action Changed;

        /// <summary>The faction whose skin the menus wear now.</summary>
        public static FactionId Current
        {
            get
            {
                if (shown == FactionId.None) shown = Resolve(HudPreferences.MenuTheme, Rolled);
                return shown;
            }
        }

        /// <summary>This run's pick, made the first time anything asks.</summary>
        public static FactionId Rolled
        {
            get
            {
                if (rolled == FactionId.None) rolled = Roll(new System.Random(Guid.NewGuid().GetHashCode()));
                return rolled;
            }
        }

        // Enter Play Mode runs without a domain reload: a new Play is a new start of the game, with a new pick.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialise()
        {
            rolled = FactionId.None;
            shown = FactionId.None;
            Changed = null;
            HudPreferences.Changed -= OnPreferencesChanged;
            HudPreferences.Changed += OnPreferencesChanged;
        }

        private static void OnPreferencesChanged()
        {
            FactionId now = Resolve(HudPreferences.MenuTheme, Rolled);
            if (now == shown) return;
            shown = now;
            Changed?.Invoke();
        }

        /// <summary>The skin a theme shows: the faction it names, or the run's pick when it rotates.</summary>
        public static FactionId Resolve(MenuTheme theme, FactionId rolledThisRun)
        {
            switch (theme)
            {
                case MenuTheme.Hegemony: return FactionId.Hegemony;
                case MenuTheme.Null: return FactionId.Null;
                case MenuTheme.Covenant: return FactionId.Covenant;
                default: return rolledThisRun;
            }
        }

        /// <summary>One of the three factions, each as likely as the others.</summary>
        public static FactionId Roll(System.Random random)
        {
            switch (random.Next(3))
            {
                case 0: return FactionId.Hegemony;
                case 1: return FactionId.Null;
                default: return FactionId.Covenant;
            }
        }
    }
}
