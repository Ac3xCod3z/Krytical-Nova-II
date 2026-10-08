using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Says when the pointer comes onto a menu element and when it is pressed (<see cref="MenuMoments"/>), so that
    /// everything in the menus that can be clicked answers the hand the same way. <see cref="MenuUi.MakeClickable"/>
    /// puts one on whatever it makes clickable; the rows and arrows of the creator ask for theirs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuTouch : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
    {
        [Tooltip("What answers the touch. Empty: this object.")]
        [SerializeField] private Transform body;

        private Selectable selectable;

        private void Awake() => selectable = GetComponent<Selectable>();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (Usable) MenuMoments.Raise(MenuMoment.Hover, body != null ? body : transform);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !Usable) return;
            MenuMoments.Raise(MenuMoment.Press, body != null ? body : transform);
        }

        // A button that is switched off (FINALISE before there is a name) stays still.
        private bool Usable => selectable == null || selectable.IsInteractable();

        /// <summary>The touch of <paramref name="target"/>, added if it has none yet.</summary>
        public static MenuTouch On(GameObject target)
        {
            if (target == null) return null;
            return target.TryGetComponent<MenuTouch>(out var touch) ? touch : target.AddComponent<MenuTouch>();
        }
    }
}
