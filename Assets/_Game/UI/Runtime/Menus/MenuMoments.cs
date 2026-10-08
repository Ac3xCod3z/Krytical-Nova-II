using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI.Menus
{
    /// <summary>What a player just did in a menu or in Settings, as far as the feel of it goes.</summary>
    public enum MenuMoment
    {
        /// <summary>The pointer came onto something that can be used.</summary>
        Hover = 0,
        /// <summary>It was pressed (the mouse went down on it, or its key did).</summary>
        Press = 1,
        /// <summary>A choice was made and kept: enter the game, create, apply, accept.</summary>
        Confirm = 2,
        /// <summary>A step back: a screen left the way it was come to, a window closed, changes put back.</summary>
        Back = 3,
        /// <summary>What was asked cannot be done: a name that is taken, a key that is in use.</summary>
        Error = 4,
        /// <summary>A page of the menus' comic book turns to the next screen.</summary>
        PageForward = 5,
        /// <summary>A page turns back to the screen before.</summary>
        PageBack = 6,
        /// <summary>A switch was flipped or one of several values stepped to.</summary>
        Toggle = 7,
        /// <summary>A slider was moved.</summary>
        Slide = 8,
        /// <summary>A window or a popup opened.</summary>
        Open = 9,
        /// <summary>Another tab of a window was picked.</summary>
        Tab = 10,
    }

    /// <summary>
    /// Where the menus and Settings say what happened, and the game feel listens (MenuFeelPresenter plays a Feel
    /// player for each moment). The interface knows nothing of how a moment looks or sounds, and works the same
    /// when nobody listens. A moment never waits for its feedback: it is raised after the thing was done.
    /// </summary>
    public static class MenuMoments
    {
        /// <summary>The moment, and what it happened on: a menu element, an element of a UI Toolkit window, or neither.</summary>
        public static event Action<MenuMoment, Transform, VisualElement> Happened;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Happened = null;

        public static void Raise(MenuMoment moment) => Happened?.Invoke(moment, null, null);

        public static void Raise(MenuMoment moment, Transform on) => Happened?.Invoke(moment, on, null);

        public static void Raise(MenuMoment moment, VisualElement on) => Happened?.Invoke(moment, null, on);
    }
}
