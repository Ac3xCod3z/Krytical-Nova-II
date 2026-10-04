using KryticalNova.CharacterCreation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Title screen: any key or click continues. A player with at least one character goes to character select
    /// (which opens on the one played last); a new player goes straight to choosing a faction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TitleScreenController : MonoBehaviour
    {
        [Tooltip("Ignore input for this long, so the key that started the game does not skip the title.")]
        [SerializeField, Min(0f)] private float inputDelay = 0.8f;

        private float readyAt;
        private bool leaving;

        private void OnEnable() => readyAt = Time.unscaledTime + inputDelay;

        private void Update()
        {
            if (leaving || Time.unscaledTime < readyAt) return;
            var transition = MenuTransition.Current;
            if (transition != null && transition.IsBusy) return;

            bool pressed = (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
                           || (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                           || (Gamepad.current != null && (Gamepad.current.buttonSouth.wasPressedThisFrame
                                                           || Gamepad.current.startButton.wasPressedThisFrame));
            if (pressed) Continue();
        }

        private void Continue()
        {
            leaving = true;
            var roster = CharacterSession.Store.Load();
            if (roster.Count > 0)
            {
                FrontEnd.HighlightSlot = roster.PreferredSlot;
                FrontEnd.Go(FrontEnd.CharacterSelectScene);
            }
            else
            {
                FrontEnd.PendingSlot = roster.FirstFree;
                FrontEnd.Go(FrontEnd.FactionSelectScene);
            }
        }
    }
}
