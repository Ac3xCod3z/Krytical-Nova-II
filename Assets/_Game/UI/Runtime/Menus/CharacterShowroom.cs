using System.Collections.Generic;
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
        [Tooltip("Faces, idles and emotes the character can show (the creator's Preview tab). Optional.")]
        [SerializeField] private CharacterExpressionLibrary expressions;
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
        private BodyType bodyType;
        private int stance; // index into the library's stances; 0 is the relaxed default

        // Ambient life (character select): the character shifts, looks about, and now and then waves or changes
        // stance, so the screen is not a statue. Calm motions only, and only those performed for its body type.
        private static readonly string[] AmbientStanceGroups = { "Base", "Arms Folded", "Hands On Hips", "Posture", "Thoughtful" };
        private static readonly string[] AmbientGestureGroups = { "Bored", "Head Nod", "Inspect", "Look", "Play With Hair", "Stretch", "Thoughtful", "Weight Shift", "Yawn" };
        private static readonly string[] AmbientEmoteGroups = { "Greet", "Happy", "Celebrate", "Affection" };
        // Motions of those groups that do not suit a character at rest (a frightened look over the shoulder).
        private static readonly string[] AmbientSkippedWords = { "Scared" };
        private const float AmbientGestureChance = 0.55f;
        private const float AmbientEmoteChance = 0.25f; // the rest is a change of stance
        private static readonly Vector2 AmbientPause = new Vector2(3.5f, 7f);
        private static readonly Vector2 AmbientFirstPause = new Vector2(1.2f, 2.5f);
        private readonly List<int> ambientPicks = new List<int>(32);
        private bool ambient;
        private float ambientWait;
        private int lastAmbientEmote = -1;

        /// <summary>
        /// On: the shown character moves by itself between calm stances, small gestures and the odd emote (the
        /// select screen). Off: it holds whatever stance it is given (the creator, where the player picks).
        /// </summary>
        public bool Ambient
        {
            get => ambient;
            set
            {
                if (ambient == value) return;
                ambient = value;
                ambientWait = Random.Range(AmbientFirstPause.x, AmbientFirstPause.y);
                if (!value) ResetPreview();
            }
        }

        public CharacterCreationDatabase Database => database;
        public CharacterExpressionLibrary Expressions => expressions;
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
            bool created = doll == null;
            if (created)
            {
                // The library's relaxed stance when there is one; the player's own (sword-ready) idle otherwise.
                AnimationClip standing = HasStances ? expressions.Idles[0].For(recipe.bodyType) : idleClip;
                doll = CharacterDoll.Create(modelPrefab, stage != null ? stage : transform, standing);
                if (doll == null) return false;
                if (expressions != null) doll.EnableExpressions(expressions);
                stance = 0;
            }
            doll.gameObject.SetActive(true);
            ApplyFaction(recipe.faction);

            // Each motion has a masculine and a feminine performance: a change of body type swaps the stance (or goes
            // back to the relaxed one, which both have, when the stance was made for the other body only).
            bool bodyChanged = bodyType != recipe.bodyType;
            bodyType = recipe.bodyType;
            if (bodyChanged && !created && HasStances)
            {
                if (!expressions.Idles[stance].Has(bodyType)) stance = 0;
                doll.PlayIdle(expressions.Idles[stance].For(bodyType));
            }
            // Another character stepped up: it starts relaxed and takes a moment before it moves.
            if (ambient)
            {
                if (HasStances && stance != 0 && !bodyChanged)
                {
                    stance = 0;
                    doll.PlayIdle(expressions.Idles[0].For(bodyType));
                }
                ambientWait = Random.Range(AmbientFirstPause.x, AmbientFirstPause.y);
            }
            return doll.Apply(recipe, database);
        }

        private bool HasStances => expressions != null && expressions.Idles.Count > 0;

        public void Hide()
        {
            if (doll != null) doll.gameObject.SetActive(false);
            ApplyFaction(FactionId.None);
        }

        // ---------------- Expressions (the creator's Preview tab) ----------------

        public void SetFace(int index)
        {
            if (doll != null) doll.SetFace(index);
        }

        public void PlayIdle(int index)
        {
            if (doll == null || expressions == null || index < 0 || index >= expressions.Idles.Count) return;
            var motion = expressions.Idles[index];
            if (!motion.Has(bodyType)) return; // not performed for this body type
            stance = index;
            doll.PlayIdle(motion.For(bodyType), motion.EnterFor(bodyType));
        }

        public void PlayEmote(int index)
        {
            if (doll == null || expressions == null || index < 0 || index >= expressions.Emotes.Count) return;
            var motion = expressions.Emotes[index];
            if (motion.Has(bodyType)) doll.PlayEmote(motion.For(bodyType)); // never the other body's performance
        }

        /// <summary>Back to the neutral face and the relaxed stance (the preview is not part of the character).</summary>
        public void ResetPreview()
        {
            if (doll == null) return;
            doll.SetFace(0);
            if (HasStances && stance != 0)
            {
                stance = 0;
                doll.PlayIdle(expressions.Idles[0].For(bodyType));
            }
        }

        // ---------------- View control (same gestures as the equipment page) ----------------

        public void Rotate(float deltaYaw, float deltaPitch) => orbit.Rotate(deltaYaw, deltaPitch);
        public void Zoom(float delta) => orbit.Zoom(delta);
        public void Pan(Vector2 pixelDelta, float viewportHeightPixels) => orbit.Pan(pixelDelta, viewportHeightPixels);
        public void ResetView() => orbit.ResetView();
        /// <summary>0 frames the whole body, 1 the face.</summary>
        public void Focus(float zoom01) => orbit.Focus(zoom01);

        private void Update()
        {
            if (!ambient || expressions == null || doll == null || !doll.isActiveAndEnabled || doll.IsBusy) return;
            ambientWait -= Time.unscaledDeltaTime;
            if (ambientWait > 0f) return;
            ambientWait = Random.Range(AmbientPause.x, AmbientPause.y);

            float roll = Random.value;
            if (roll < AmbientGestureChance) PlayAmbientEmote(AmbientGestureGroups);
            else if (roll < AmbientGestureChance + AmbientEmoteChance) PlayAmbientEmote(AmbientEmoteGroups);
            else PlayAmbientStance();
        }

        private void PlayAmbientEmote(string[] groups)
        {
            Pick(expressions.Emotes, groups, lastAmbientEmote);
            if (ambientPicks.Count == 0) return;
            lastAmbientEmote = ambientPicks[Random.Range(0, ambientPicks.Count)];
            PlayEmote(lastAmbientEmote);
        }

        private void PlayAmbientStance()
        {
            Pick(expressions.Idles, AmbientStanceGroups, stance);
            if (ambientPicks.Count > 0) PlayIdle(ambientPicks[Random.Range(0, ambientPicks.Count)]);
        }

        /// <summary>The motions of the given groups that were performed for this character's body type, except the last one used.</summary>
        private void Pick(IReadOnlyList<CharacterExpressionLibrary.Motion> motions, string[] groups, int except)
        {
            ambientPicks.Clear();
            for (int i = 0; i < motions.Count; i++)
            {
                if (i == except || !motions[i].Has(bodyType)) continue;
                if (System.Array.IndexOf(groups, motions[i].group) < 0) continue;
                bool skipped = false;
                foreach (string word in AmbientSkippedWords) skipped |= motions[i].name.Contains(word);
                if (!skipped) ambientPicks.Add(i);
            }
        }

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
