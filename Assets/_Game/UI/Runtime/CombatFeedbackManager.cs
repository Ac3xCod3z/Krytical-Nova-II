using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>Displays animated floating combat numbers and status banners in UI Toolkit projected from world coordinates.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    // Placed on the screen after the Cinemachine brain (100) has moved the camera this frame (see NamecardOrder).
    [DefaultExecutionOrder(NamecardOrder.AfterCamera)]
    public sealed class CombatFeedbackManager : MonoBehaviour, ICombatFeedbackSink
    {
        private const float FloatDuration = 1.0f;
        private const float RiseSpeed = 1.5f;
        private const float BaseWorldYOffset = 1.8f;
        private const float RandomSpread = 0.35f;

        private class ActiveCombatLabel
        {
            public Label Label;
            public Vector3 OriginWorldPosition;
            public float ElapsedTime;
            public float Lifetime;
            public Vector3 JitterOffset;
        }

        private static CombatFeedbackManager instance;
        public static CombatFeedbackManager Instance => instance;

        private UIDocument uiDocument;
        private VisualElement rootContainer;
        private Camera targetCamera;
        private readonly List<ActiveCombatLabel> activeLabels = new List<ActiveCombatLabel>();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
            uiDocument = GetComponent<UIDocument>();
            ClientPresentationRegistry.RegisterFeedback(this);
        }

        private void OnDestroy()
        {
            ClientPresentationRegistry.UnregisterFeedback(this);
            if (instance == this) instance = null;
        }

        private void Start()
        {
            if (uiDocument != null)
            {
                rootContainer = uiDocument.rootVisualElement.Q("game-ui");
            }
            if (!IsUsable(targetCamera)) targetCamera = FindViewCamera();
        }

        private void LateUpdate()
        {
            if (activeLabels.Count == 0 || rootContainer == null || rootContainer.panel == null) return;
            if (!IsUsable(targetCamera)) targetCamera = FindViewCamera();

            float dt = Time.deltaTime;
            for (int i = activeLabels.Count - 1; i >= 0; i--)
            {
                var entry = activeLabels[i];
                // Labels age even with no camera, so a label can never be left behind on the screen (owner bug, 14 Nov:
                // "MISS / EVADE" stuck on the portrait frame while no camera was found and nothing moved or removed it).
                entry.ElapsedTime += dt;
                float progress = Mathf.Clamp01(entry.ElapsedTime / entry.Lifetime);

                if (progress >= 1f)
                {
                    entry.Label?.RemoveFromHierarchy();
                    activeLabels.RemoveAt(i);
                    continue;
                }

                Place(entry, progress);
            }
        }

        /// <summary>
        /// Puts one label over its point in the world, or hides it. A label is shown only at a real projected point:
        /// with no camera, a point that is not a number, or a point behind the camera it is hidden, never left at the
        /// panel's top-left corner (where the player's portrait frame is).
        /// </summary>
        private void Place(ActiveCombatLabel entry, float progress)
        {
            Vector3 currentWorldPos = entry.OriginWorldPosition + entry.JitterOffset + Vector3.up * (progress * RiseSpeed);
            if (rootContainer == null || rootContainer.panel == null
                || !TryProject(targetCamera, currentWorldPos, Screen.height, out Vector2 screenTopDown))
            {
                entry.Label.style.display = DisplayStyle.None;
                return;
            }

            Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(rootContainer.panel, screenTopDown);
            if (!IsFinite(panelPoint))
            {
                entry.Label.style.display = DisplayStyle.None;
                return;
            }

            entry.Label.style.display = DisplayStyle.Flex;
            // Centred by translate, so the first frame (before the label has a size) is already in the right place.
            entry.Label.style.left = panelPoint.x;
            entry.Label.style.top = panelPoint.y;
            Hud.HudChrome.HideOver(entry.Label, panelPoint, true);
            entry.Label.style.opacity = Mathf.Clamp01(1f - (progress * progress));
        }

        private void Track(Label label, Vector3 worldPosition, float lifetime, Vector3 jitter)
        {
            label.style.display = DisplayStyle.None; // shown only once it has a real point (Place)
            label.style.translate = new Translate(Length.Percent(-50f), Length.Percent(-50f));
            rootContainer.Add(label);
            var entry = new ActiveCombatLabel
            {
                Label = label,
                OriginWorldPosition = worldPosition + Vector3.up * BaseWorldYOffset,
                ElapsedTime = 0f,
                Lifetime = lifetime,
                JitterOffset = jitter
            };
            activeLabels.Add(entry);
            if (!IsUsable(targetCamera)) targetCamera = FindViewCamera();
            Place(entry, 0f);
        }

        /// <summary>A camera that can place labels this frame: it exists and is switched on.</summary>
        public static bool IsUsable(Camera camera) => camera != null && camera.isActiveAndEnabled;

        /// <summary>The camera the player looks through: the MainCamera, else the highest switched-on camera that draws to the screen.</summary>
        public static Camera FindViewCamera()
        {
            Camera main = Camera.main;
            if (IsUsable(main) && main.targetTexture == null) return main;
            Camera best = null;
            foreach (Camera camera in Camera.allCameras)
                if (IsUsable(camera) && camera.targetTexture == null && (best == null || camera.depth > best.depth)) best = camera;
            return best;
        }

        /// <summary>
        /// Projects a world point to a screen point measured from the top (as the UI panel counts). False, and nothing
        /// to show, when there is no usable camera, the point is not a real number, or it is behind the camera.
        /// </summary>
        public static bool TryProject(Camera camera, Vector3 world, float screenHeight, out Vector2 screenTopDown)
        {
            screenTopDown = default;
            if (!IsUsable(camera) || !IsFinite(world)) return false;
            Vector3 screenPoint = camera.WorldToScreenPoint(world);
            if (!IsFinite(screenPoint) || screenPoint.z <= 0f) return false;
            screenTopDown = new Vector2(screenPoint.x, screenHeight - screenPoint.y);
            return IsFinite(screenTopDown);
        }

        private static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        private static bool IsFinite(Vector2 v) => float.IsFinite(v.x) && float.IsFinite(v.y);

        /// <summary>Spawns floating combat feedback at world coordinates based on a resolved DamageResult.</summary>
        public void ShowCombatFeedback(Vector3 worldPosition, DamageResult result)
        {
            if (rootContainer == null || rootContainer.panel == null) return;
            if (!HudPreferences.DamageNumbers) return; // the player turned them off (Settings, Gameplay)

            Label label = new Label();
            label.AddToClassList("floating-combat-text");
            label.pickingMode = PickingMode.Ignore;

            if (result.IsEvaded)
            {
                label.text = "MISS / EVADE";
                label.AddToClassList("evade");
            }
            else if (result.IsReflected)
            {
                label.text = $"REFLECTED! ({Mathf.RoundToInt(result.ReflectedDamage)})";
                label.AddToClassList("reflected");
            }
            else if (result.IsCritical)
            {
                label.text = $"CRIT! {Mathf.RoundToInt(result.DamageDealt)}";
                label.AddToClassList("crit");
            }
            else
            {
                label.text = Mathf.RoundToInt(result.DamageDealt).ToString();
            }

            if (result.IsBlocked && !result.IsEvaded && !result.IsReflected)
            {
                // Build 17, item 8: the defender blocked; the hit landed for less.
                label.text = "BLOCKED " + label.text;
                label.AddToClassList("blocked");
            }

            if (result.ShieldShattered)
            {
                label.text += "\n[SHATTER]";
            }

            Track(label, worldPosition, FloatDuration, new Vector3(Random.Range(-RandomSpread, RandomSpread), 0f, RandomSpread * 0.5f));
        }

        /// <summary>Spawns floating text over a character (e.g. skill activations, buffs, level ups).</summary>
        public void ShowFloatingText(Vector3 worldPosition, string text, string styleClass = "crit")
        {
            if (rootContainer == null || rootContainer.panel == null) return;

            Label label = new Label(text);
            label.AddToClassList("floating-combat-text");
            if (!string.IsNullOrEmpty(styleClass))
            {
                label.AddToClassList(styleClass);
            }
            label.pickingMode = PickingMode.Ignore;

            Track(label, worldPosition, FloatDuration * 1.3f, new Vector3(Random.Range(-0.1f, 0.1f), 0f, 0f));
        }

        private void OnDisable()
        {
            for (int i = 0; i < activeLabels.Count; i++)
            {
                activeLabels[i].Label?.RemoveFromHierarchy();
            }
            activeLabels.Clear();
        }
    }
}
