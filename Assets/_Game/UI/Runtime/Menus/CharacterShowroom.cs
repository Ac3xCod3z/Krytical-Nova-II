using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items.Visuals;
using UnityEngine;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The live character shown in the creator and on character select: a display doll on a small lit stage, viewed
    /// with the same damped orbit as the equipment page (<see cref="PreviewOrbit"/>). The lighting takes its rim and
    /// fill colours from the character's faction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterShowroom : MonoBehaviour
    {
        [SerializeField] private CharacterCreationDatabase database;
        [Tooltip("The player's Sidekick model prefab (Player_01).")]
        [SerializeField] private GameObject modelPrefab;
        [SerializeField] private AnimationClip idleClip;
        [SerializeField] private Camera viewCamera;
        [Tooltip("Where the character stands. It faces the stage's -Z.")]
        [SerializeField] private Transform stage;
        [Tooltip("Where the character sits on screen: 0 centre, +1 right edge, -1 left edge. Leaves room for the menu panels.")]
        [SerializeField, Range(-1f, 1f)] private float screenShiftX;
        [Tooltip("Camera distance of the full-body view. Further out than the equipment page, whose viewport is small.")]
        [SerializeField, Min(2f)] private float fullBodyDistance = 5.2f;
        [Tooltip("Height the full-body view is centred on. A little above the character's middle, so they stand clear of the footer.")]
        [SerializeField] private float fullBodyPivotHeight = 1.02f;
        [SerializeField] private Light rimLight;
        [SerializeField] private Light fillLight;
        [Tooltip("Backdrop renderers tinted with a dark shade of the faction colour.")]
        [SerializeField] private Renderer[] backdrop = System.Array.Empty<Renderer>();
        [SerializeField] private Color neutralRim = new Color(0.6f, 0.85f, 1f);
        [SerializeField] private Color neutralFill = new Color(0.55f, 0.6f, 0.75f);
        [Tooltip("Brightness of the backdrop away from the character, and of the burst behind them.")]
        [SerializeField, Range(0f, 1f)] private float backdropEdgeValue = 0.09f;
        [SerializeField, Range(0f, 1f)] private float backdropGlowValue = 0.34f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

        private readonly PreviewOrbit orbit = new PreviewOrbit();
        private CharacterDoll doll;
        private MaterialPropertyBlock block;

        public CharacterCreationDatabase Database => database;
        public bool IsShowing => doll != null && doll.gameObject.activeSelf;

        public float ScreenShiftX
        {
            get => screenShiftX;
            set => screenShiftX = Mathf.Clamp(value, -1f, 1f);
        }

        private void Awake()
        {
            orbit.maxDistance = fullBodyDistance;
            orbit.farPivotHeight = fullBodyPivotHeight;
            orbit.Snap();
            if (viewCamera != null) viewCamera.fieldOfView = orbit.fieldOfView;
            ApplyFaction(FactionId.None);
        }

        /// <summary>Show (or restyle) the character. Returns false when the recipe cannot be built.</summary>
        public bool Show(in CharacterRecipe recipe)
        {
            if (doll == null)
            {
                doll = CharacterDoll.Create(modelPrefab, stage != null ? stage : transform, idleClip);
                if (doll == null) return false;
            }
            doll.gameObject.SetActive(true);
            ApplyFaction(recipe.faction);
            return doll.Apply(recipe, database);
        }

        public void Hide()
        {
            if (doll != null) doll.gameObject.SetActive(false);
            ApplyFaction(FactionId.None);
        }

        // ---------------- View control (same gestures as the equipment page) ----------------

        public void Rotate(float deltaYaw, float deltaPitch) => orbit.Rotate(deltaYaw, deltaPitch);
        public void Zoom(float delta) => orbit.Zoom(delta);
        public void Pan(Vector2 pixelDelta, float viewportHeightPixels) => orbit.Pan(pixelDelta, viewportHeightPixels);
        public void ResetView() => orbit.ResetView();
        /// <summary>0 frames the whole body, 1 the face.</summary>
        public void Focus(float zoom01) => orbit.Focus(zoom01);

        private void LateUpdate()
        {
            if (viewCamera == null) return;
            orbit.Tick(Time.unscaledDeltaTime);
            var origin = stage != null ? stage : transform;
            orbit.Evaluate(origin.position, origin.rotation, out var dollRotation, out var cameraPosition, out var cameraRotation);
            if (doll != null) doll.transform.localRotation = dollRotation;

            // Slide the camera sideways (parallel to the picture) so the character stands off-centre without turning.
            float halfHeight = orbit.Distance * Mathf.Tan(orbit.fieldOfView * 0.5f * Mathf.Deg2Rad);
            cameraPosition -= cameraRotation * Vector3.right * (screenShiftX * halfHeight * viewCamera.aspect);
            viewCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        private void ApplyFaction(FactionId id)
        {
            var faction = database != null && database.Factions != null ? database.Factions.Get(id) : null;
            Color rim = faction != null ? faction.accentColor : neutralRim;
            Color fill = faction != null ? Color.Lerp(faction.secondaryColor, Color.white, 0.35f) : neutralFill;
            if (rimLight != null) rimLight.color = rim;
            if (fillLight != null) fillLight.color = fill;

            if (backdrop.Length == 0) return;
            if (block == null) block = new MaterialPropertyBlock();
            // The burst behind the character: a dark shade of the faction colour, printed with dots of a brighter one.
            Color.RGBToHSV(rim, out float h, out float s, out _);
            Color edge = Color.HSVToRGB(h, Mathf.Min(s, 0.6f), backdropEdgeValue);
            Color glow = Color.HSVToRGB(h, Mathf.Min(s, 0.7f), backdropGlowValue);
            foreach (var r in backdrop)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColorId, edge);
                block.SetColor(GlowColorId, glow);
                r.SetPropertyBlock(block);
            }
        }
    }
}
