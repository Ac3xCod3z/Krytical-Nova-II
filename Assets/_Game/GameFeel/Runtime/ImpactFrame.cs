using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>The two looks of the Callout crit impact frame (build 17 item 10.3); the owner picks one from GIFs.</summary>
    public enum ImpactFrameStyle : byte
    {
        Off = 0,
        /// <summary>A: an inked frame: the screen goes near-black with white radial speed lines.</summary>
        Ink = 1,
        /// <summary>B: a flash frame: the screen goes white with dark radial speed lines.</summary>
        Flash = 2,
    }

    /// <summary>
    /// The anime impact frame of a crit in a Callout (build 17 item 10.3): a full-screen frame with radial speed lines held for a few
    /// frames, on this machine only. Presentation only: it decides nothing and never touches the game clock. Built on first use: drawn
    /// with IMGUI over everything (no canvas, no asset), one picture made in code.
    /// </summary>
    public sealed class ImpactFrame : MonoBehaviour
    {
        private const int TextureSize = 512, Lines = 56;
        private static ImpactFrame instance;
        private Texture2D lineTexture;
        private Color fillColour, lineColour;
        private float lineAngle;
        private int framesLeft;

        /// <summary>The share of frames left a frame is for (tests), 0 when nothing shows.</summary>
        public static int FramesLeft => instance != null ? instance.framesLeft : 0;

        /// <summary>Shows the frame for <paramref name="frames"/> rendered frames at <paramref name="strength"/> (1 = own hit, less = the opponent's).</summary>
        public static void Show(ImpactFrameStyle style, int frames, float strength)
        {
            if (style == ImpactFrameStyle.Off || frames <= 0 || strength <= 0f || Application.isBatchMode) return;
            if (instance == null) instance = Build();
            instance.Begin(style, frames, Mathf.Clamp01(strength));
        }

        private static ImpactFrame Build()
        {
            var go = new GameObject("[ImpactFrame]");
            DontDestroyOnLoad(go);
            var frame = go.AddComponent<ImpactFrame>();
            frame.lineTexture = SpeedLines();
            frame.enabled = false;
            return frame;
        }

        /// <summary>White radial wedges on clear, fading out toward the middle (the focus stays readable).</summary>
        private static Texture2D SpeedLines()
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "ImpactSpeedLines" };
            var pixels = new Color32[TextureSize * TextureSize];
            float half = TextureSize * 0.5f;
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f) * Lines;
                    float wedge = Mathf.Abs(angle - Mathf.Round(angle));           // 0 on a line, 0.5 between
                    float jitter = Mathf.Repeat(Mathf.Round(angle) * 0.618f, 1f);   // each line its own thickness and start
                    float on = wedge < 0.08f + 0.1f * jitter && radius > 0.35f + 0.3f * jitter ? 1f : 0f;
                    byte a = (byte)(255f * on * Mathf.Clamp01((radius - 0.3f) * 2f));
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, a);
                }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void Begin(ImpactFrameStyle style, int frames, float strength)
        {
            bool ink = style == ImpactFrameStyle.Ink;
            fillColour = ink ? new Color(0.03f, 0.03f, 0.05f, 0.85f * strength) : new Color(1f, 1f, 1f, 0.8f * strength);
            lineColour = ink ? new Color(1f, 1f, 1f, strength) : new Color(0.05f, 0.05f, 0.08f, strength);
            lineAngle = Random.Range(0f, 360f);
            framesLeft = Mathf.Max(framesLeft, frames);
            enabled = true;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            var screen = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.depth = -10000;
            Color was = GUI.color;
            GUI.color = fillColour;
            GUI.DrawTexture(screen, Texture2D.whiteTexture);
            GUI.color = lineColour;
            float side = Mathf.Max(Screen.width, Screen.height) * 1.5f;
            var square = new Rect((Screen.width - side) * 0.5f, (Screen.height - side) * 0.5f, side, side);
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(lineAngle, screen.center);
            GUI.DrawTexture(square, lineTexture);
            GUI.matrix = matrix;
            GUI.color = was;
        }

        private void LateUpdate()
        {
            if (framesLeft-- > 0) return;   // drawn this frame; gone once the count is spent
            framesLeft = 0;
            enabled = false;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
            if (lineTexture != null) Destroy(lineTexture);
        }
    }
}
