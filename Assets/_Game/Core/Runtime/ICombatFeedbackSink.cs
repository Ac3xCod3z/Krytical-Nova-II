using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>Client-side presentation of combat results (floating numbers, banners). Implemented by the UI assembly.</summary>
    public interface ICombatFeedbackSink
    {
        void ShowCombatFeedback(Vector3 worldPosition, DamageResult result);
        void ShowFloatingText(Vector3 worldPosition, string text, string styleClass = "crit");
    }
}
