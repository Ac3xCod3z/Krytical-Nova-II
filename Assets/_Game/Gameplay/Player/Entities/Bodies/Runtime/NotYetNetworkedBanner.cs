using UnityEngine;

namespace KryticalNova.Player.Entities.Bodies
{
    /// <summary>
    /// The prototype banner of the entity netcode client (M3a-2b decision D2): only walking and looks are shared with the server yet;
    /// gear, skills, the wallet and the rest run on this PC only and are never saved. Put on the local ClientView by
    /// <see cref="ClientBodyFactory"/>; it goes with the body. A thin strip at the top of the screen, no input.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NotYetNetworkedBanner : MonoBehaviour
    {
        public const string Text = "Prototype server: walking and looks are shared. Gear, skills and the wallet are not networked yet and are not saved.";

        GUIStyle m_Style;

        void OnGUI()
        {
            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13, wordWrap = true };
                m_Style.normal.textColor = new Color(1f, 0.85f, 0.4f);
            }
            float width = Mathf.Min(900f, Screen.width - 32f);
            GUI.Box(new Rect((Screen.width - width) * 0.5f, 8f, width, 26f), Text, m_Style);
        }
    }
}
