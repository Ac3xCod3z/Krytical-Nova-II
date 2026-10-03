using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class TextComponent : TextBillboardComponent<Text, TextObject, Text.TextModule>
    {
        public override Text Settings => Creator.text;
        public override ReferencesObject.System Reference => Creator.reference.references.text;

        protected override Curve Rotation => (module.randomRotation * Mathf.Deg2Rad, -1);

        [SerializeField] private int letter;
        [SerializeField] private char character;
        [SerializeField] private int line;
        [SerializeField] private int lines;
        [SerializeField] private int maxLetters;

        public int Letter { get => letter; set => letter = value; }
        public char Character { get => character; set => character = value; }
        public int Line { get => line; set => line = value; }
        public int Lines { get => lines; set => lines = value; }
        public int MaxLetters { get => maxLetters; set => maxLetters = value; }

        // protected override int GetOrder(int v) => v + (Count - 1 - 2 * Letter) * Max; // Reversed
        protected override int GetOrder(int v) => v + (Letter % 2 == 0 ? 1 : 0) * Max + (Count - 1 - Letter);

        protected override int Seed => Letter + Line * MaxLetters;

        private int Count => Settings.text[Line].Length;

        private float NormalizedT => Count > 1 ? (float)Letter / (Count - 1) : 0.5f;

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Main.SetSize(Curve.TwoConstants(module.size.x, module.size.y) * Settings.sizeOverText.Evaluate(NormalizedT));

            Gradient color, secondColor;
            if (module.useSecondColor)
            {
                color = (module.colorIsGradient, module.colorGradient, module.colorSolid);
                secondColor = (module.secondColorIsGradient, module.secondColorGradient, module.secondColorSolid);
            }
            else
            {
                color = (module.colorIsGradient, module.colorGradient, module.colorSolid);
                secondColor = (module.colorIsGradient, module.colorGradient, module.colorSolid);
            }

            if (module.colorSampling == Text.TextModule.EColorSample.ColorOverText)
            {
                color = color.Value.Evaluate(NormalizedT);
                secondColor = secondColor.Value.Evaluate(NormalizedT);
            }

            System.CustomData.Set(ParticleSystemCustomData.Custom1, color);
            System.CustomData.Set(ParticleSystemCustomData.Custom2, secondColor);

            System.SetColor("_OutlineColor", module.useOutline ? module.outlineColor : Color.clear);
            System.SetFloat("_Index", Object.GetIndex(Character));
        }

        protected override void Move()
        {
            float globalCenterOffset = (MaxLetters - 1) * 0.5f;

            float alignmentOffset = Settings.alignment switch
            {
                Text.EAlignment.Left => 0,
                Text.EAlignment.Middle => (MaxLetters - Count) * 0.5f,
                Text.EAlignment.Right => MaxLetters - Count,
                _ => 0
            };

            float offsetFromCenter = Letter + alignmentOffset - globalCenterOffset;

            float spacing = Settings.spacing * Mathf.Pow(Settings.sizeOverText.Evaluate(NormalizedT), 0.5f); ;

            float angleStep = spacing / Settings.distance;
            float angle = -offsetFromCenter * angleStep;

            Vector3 circularPos = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * Settings.distance;
            Vector3 flatPos = new Vector3(-offsetFromCenter * spacing, 0, Settings.distance);
            Vector3 targetPos = Vector3.Lerp(circularPos, flatPos, Settings.flatten);

            float lineCenter = (Lines - 1) * 0.5f;
            float lineOffset = (Line - lineCenter) * -Settings.lineHeight;

            targetPos.y += Settings.shape.Evaluate(NormalizedT) + lineOffset;

            MoveTo(targetPos);
        }
    }
}