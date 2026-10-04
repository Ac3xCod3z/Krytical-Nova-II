using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// A selectable piece of a Synty menu: a list row, a card, a tile, a swatch or a tab. The vendor Animators have
    /// Normal / Highlighted / Selected states meant for a Button's own transition, which loses the selected look as
    /// soon as something else is clicked. This drives the Animator directly so the chosen one stays lit, and still
    /// highlights on hover. Tabs keep their state in a "ToggleIsOn" bool instead of a Selected trigger; both work.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuRow : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private static readonly int NormalId = Animator.StringToHash("Normal");
        private static readonly int HighlightedId = Animator.StringToHash("Highlighted");
        private static readonly int SelectedId = Animator.StringToHash("Selected");
        private static readonly int ToggleIsOnId = Animator.StringToHash("ToggleIsOn");

        private Animator animator;
        private bool selected;
        private bool hovered;
        private bool usesToggleBool;
        private bool parametersRead;

        public event Action Clicked;
        public event Action DoubleClicked;

        public bool IsSelected => selected;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            // The Button stays for navigation and raycasts, but no longer fights us for the Animator. A Toggle would
            // also flip its own state on a click, so it is switched off: the screen decides what is selected.
            if (TryGetComponent<Selectable>(out var selectable))
            {
                selectable.transition = Selectable.Transition.None;
                if (selectable is Toggle) selectable.enabled = false;
            }
        }

        private void OnEnable() => Refresh();

        public void SetSelected(bool value)
        {
            if (selected == value) return;
            selected = value;
            Refresh();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            hovered = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            Refresh();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;
            Clicked?.Invoke();
            if (eventData.clickCount >= 2) DoubleClicked?.Invoke();
        }

        private void Refresh()
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            if (!parametersRead && animator.isInitialized)
            {
                parametersRead = true; // parameters can only be listed once the Animator is running
                foreach (var parameter in animator.parameters)
                    if (parameter.nameHash == ToggleIsOnId) usesToggleBool = true;
            }
            animator.ResetTrigger(NormalId);
            animator.ResetTrigger(HighlightedId);
            if (usesToggleBool)
            {
                animator.SetBool(ToggleIsOnId, selected);
                animator.SetTrigger(hovered ? HighlightedId : NormalId);
                return;
            }
            animator.ResetTrigger(SelectedId);
            animator.SetTrigger(selected ? SelectedId : hovered ? HighlightedId : NormalId);
        }
    }
}
