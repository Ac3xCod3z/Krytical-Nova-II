using KryticalNova.UI.Menus;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The feel of the menus and of Settings. It listens to what the interface says happened
    /// (<see cref="MenuMoments"/>) and plays the Feel player of that moment on the thing it happened on: a hover, a
    /// press, a confirm, a step back, an error, a page of the comic book turning, a switch, a slider, a window
    /// opening, a tab. Each player is tuned on the prefab like any other of the game's feel; a sound goes in its
    /// sound slot.
    ///
    /// One stands in every menu scene and one in the world (inside the game feel prefab), so Settings feels the
    /// same from character select and in the game. Nothing here waits: a moment is raised after the thing was done.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MenuFeelPresenter : MonoBehaviour
    {
        [Header("One player per moment (MenuMoment)")]
        [SerializeField] private MMF_Player hover;
        [SerializeField] private MMF_Player press;
        [SerializeField] private MMF_Player confirm;
        [SerializeField] private MMF_Player back;
        [SerializeField] private MMF_Player error;
        [SerializeField] private MMF_Player pageForward;
        [SerializeField] private MMF_Player pageBack;
        [SerializeField] private MMF_Player toggle;
        [SerializeField] private MMF_Player slide;
        [SerializeField] private MMF_Player open;
        [SerializeField] private MMF_Player tab;

        [Header("Pace")]
        [Tooltip("A slider that is dragged says it moved many times a second: its feel plays at most this often (seconds).")]
        [SerializeField, Min(0f)] private float slideGap = 0.07f;
        [Tooltip("The same thing hovered again within this time does not answer twice (seconds): a pointer resting on an edge.")]
        [SerializeField, Min(0f)] private float hoverGap = 0.08f;

        [SerializeField] private MenuPunchDriver driver;

        private float lastSlide = -10f, lastHover = -10f;
        private Transform lastHoverBody;
        private VisualElement lastHoverElement;

        /// <summary>How many moments were answered since this presenter woke (for checks).</summary>
        public int Answered { get; private set; }
        public MenuPunchDriver Driver => driver;

        private void Awake()
        {
            if (driver == null && !TryGetComponent(out driver)) driver = gameObject.AddComponent<MenuPunchDriver>();
        }

        private void OnEnable() => MenuMoments.Happened += Answer;

        private void OnDisable() => MenuMoments.Happened -= Answer;

        /// <summary>The player of a moment, or null when it has none.</summary>
        public MMF_Player PlayerOf(MenuMoment moment)
        {
            switch (moment)
            {
                case MenuMoment.Hover: return hover;
                case MenuMoment.Press: return press;
                case MenuMoment.Confirm: return confirm;
                case MenuMoment.Back: return back;
                case MenuMoment.Error: return error;
                case MenuMoment.PageForward: return pageForward;
                case MenuMoment.PageBack: return pageBack;
                case MenuMoment.Toggle: return toggle;
                case MenuMoment.Slide: return slide;
                case MenuMoment.Open: return open;
                case MenuMoment.Tab: return tab;
                default: return null;
            }
        }

        private void Answer(MenuMoment moment, Transform body, VisualElement element)
        {
            MMF_Player player = PlayerOf(moment);
            if (player == null || !isActiveAndEnabled) return;
            float now = Time.unscaledTime;
            if (moment == MenuMoment.Slide)
            {
                if (now - lastSlide < slideGap) return;
                lastSlide = now;
            }
            else if (moment == MenuMoment.Hover)
            {
                if (now - lastHover < hoverGap && body == lastHoverBody && element == lastHoverElement) return;
                lastHover = now;
                lastHoverBody = body;
                lastHoverElement = element;
            }

            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                if (!(feedback is MMF_MenuPunch punch)) continue;
                punch.Target = body;
                punch.Element = element;
                punch.Driver = driver;
            }
            player.PlayFeedbacks();
            Answered++;
        }
    }
}
