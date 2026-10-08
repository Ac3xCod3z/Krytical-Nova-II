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
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void LateUpdate()
        {
            if (activeLabels.Count == 0 || rootContainer == null || rootContainer.panel == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            float dt = Time.deltaTime;
            for (int i = activeLabels.Count - 1; i >= 0; i--)
            {
                var entry = activeLabels[i];
                entry.ElapsedTime += dt;
                float progress = Mathf.Clamp01(entry.ElapsedTime / entry.Lifetime);

                if (progress >= 1f)
                {
                    entry.Label?.RemoveFromHierarchy();
                    activeLabels.RemoveAt(i);
                    continue;
                }

                Vector3 currentWorldPos = entry.OriginWorldPosition + entry.JitterOffset + Vector3.up * (progress * RiseSpeed);
                Vector3 screenPoint = targetCamera.WorldToScreenPoint(currentWorldPos);

                // If behind camera, hide
                if (screenPoint.z <= 0f)
                {
                    entry.Label.style.display = DisplayStyle.None;
                    continue;
                }

                entry.Label.style.display = DisplayStyle.Flex;
                Vector2 panelPoint = RuntimePanelUtils.ScreenToPanel(
                    rootContainer.panel,
                    new Vector2(screenPoint.x, Screen.height - screenPoint.y));

                entry.Label.style.left = panelPoint.x - (entry.Label.resolvedStyle.width * 0.5f);
                entry.Label.style.top = panelPoint.y - (entry.Label.resolvedStyle.height * 0.5f);
                entry.Label.style.opacity = Mathf.Clamp01(1f - (progress * progress));
            }
        }

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

            if (result.ShieldShattered)
            {
                label.text += "\n[SHATTER]";
            }

            rootContainer.Add(label);

            var entry = new ActiveCombatLabel
            {
                Label = label,
                OriginWorldPosition = worldPosition + Vector3.up * BaseWorldYOffset,
                ElapsedTime = 0f,
                Lifetime = FloatDuration,
                JitterOffset = new Vector3(Random.Range(-RandomSpread, RandomSpread), 0f, RandomSpread * 0.5f)
            };
            activeLabels.Add(entry);
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

            rootContainer.Add(label);

            var entry = new ActiveCombatLabel
            {
                Label = label,
                OriginWorldPosition = worldPosition + Vector3.up * BaseWorldYOffset,
                ElapsedTime = 0f,
                Lifetime = FloatDuration * 1.3f,
                JitterOffset = new Vector3(Random.Range(-0.1f, 0.1f), 0f, 0f)
            };
            activeLabels.Add(entry);
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
