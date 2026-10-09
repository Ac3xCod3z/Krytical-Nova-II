using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// An invisible, screen-filling hit area behind the menu panels: dragging it turns the showroom character, the
    /// wheel zooms, a middle-drag pans and a double-click resets. The gestures and their speeds match the equipment
    /// page (GearUIController.BindViewport), scaled from its 720-pixel reference to the real screen.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ShowroomDragSurface : Graphic, IDragHandler, IScrollHandler, IPointerClickHandler
    {
        private const float RotateYawPerPixel = 0.45f;
        private const float RotatePitchPerPixel = 0.2f;
        private const float ZoomPerNotch = 0.3f;
        private const float ReferenceHeight = 720f;

        [SerializeField] private CharacterShowroom showroom;

        // Nothing to draw: this is only a raycast target.
        protected override void OnPopulateMesh(VertexHelper vh) => vh.Clear();

        public void OnDrag(PointerEventData eventData)
        {
            if (showroom == null) return;
            // The screen is as tall as the book's page, which is less than the view while the menus are a book.
            float scale = ReferenceHeight / Mathf.Max(Screen.height * MenuPage.Area.height, 1f);
            // Screen deltas have y up; the orbit expects UI pixels with y down.
            var delta = new Vector2(eventData.delta.x, -eventData.delta.y) * scale;
            if (eventData.button == PointerEventData.InputButton.Left)
                showroom.Rotate(-delta.x * RotateYawPerPixel, delta.y * RotatePitchPerPixel);
            else if (eventData.button == PointerEventData.InputButton.Middle)
                showroom.Pan(delta, ReferenceHeight);
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (showroom == null || Mathf.Approximately(eventData.scrollDelta.y, 0f)) return;
            showroom.Zoom(eventData.scrollDelta.y > 0f ? -ZoomPerNotch : ZoomPerNotch);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (showroom != null && eventData.button == PointerEventData.InputButton.Left && eventData.clickCount >= 2)
                showroom.ResetView();
        }
    }
}
