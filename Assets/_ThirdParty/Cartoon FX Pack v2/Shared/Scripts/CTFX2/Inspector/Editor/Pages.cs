using System.Collections.Generic;
using UnityEditor;
using PSD.Creator.Objects;
using PSD.Creator.Inspector;
using System.Linq;
using PSD.Creator.Editor.Inspector.Editors;
using CTFX2.Creator.Objects;
using PSD.Creator.Data;

namespace CTFX2.Creator.Editor.Inspector.Editors
{
    public class CartoonFXBasicSettingsPage : BasicSettingsPage
    {
        public override string Category => "General";

        public override void DrawContent(SerializedObject target)
        {
            DrawProperty(target, "alignment");
            DrawProperty(target, "cameraShake");
            base.DrawContent(target);
        }
    }

    public class CartoonFXRandomizationPage : RandomizationPage
    {
        protected override RandomizationBuilder Configure(RandomizationBuilder builder) => builder
            .Add(Randomization.ETargets.Background, "background", r => r.backgrounds)
            .Add(Randomization.ETargets.Halftone, "halftone", r => r.halftones)
            .Add(Randomization.ETargets.Text, "text", r => r.texts)
            .Add(Randomization.ETargets.Impact, "impact", r => r.impacts)
            .Add(Randomization.ETargets.Effects, "effects", r => r.effects)
            .Add(Randomization.ETargets.Smoke, "smoke", r => r.smokes)
            .Add(Randomization.ETargets.Debris, "debris", r => r.debris);
    }

    public abstract class TextBillboardPage : BillboardPage
    {
        protected override Layout Layout => new Layout()
            .Header("Particle", "Appearance and duration of each particle")
            .Property("lifetime")

            .Header("Overlay", "Halftone texture layered on top of the particle")
            .Property("overlay", "overlayColor", "overlayRotation", "overlayTiling")

            .Header("Modifiers", "Optional effects applied when particles spawn")
            .Property("delay", "randomDelay")

            .Header("Position", "How particles are placed and move")
            .Property("distance", "speed")
            .SubHeader("Movement")
            .Property(
                "turbulence", "turbulenceSpeed", "inMovement", "outMovement",
                "movementEasing", "movementAttack", "movementRelease", "movementOffset")

            .Separate()
            .Property("billboards");
    }

    public abstract class TextAmountBillboardPage : TextBillboardPage { }

    public class BackgroundPage : TextBillboardPage
    {
        public override string Category => "Background";
        protected override ReferencesObject.System GetReference(ReferencesObject references) => references.background;
        protected override string Name => "Background";
        protected override string Settings => "background";
        public override string Subtitle => "A full-screen sprite rendered behind all other layers";
    }

    public class HalftonePage : TextBillboardPage
    {
        public override string Category => "Background";
        protected override ReferencesObject.System GetReference(ReferencesObject references) => references.halftone;
        protected override string Name => "Halftone";
        protected override string Settings => "halftone";
        public override string Subtitle => "A dot-grid texture overlaid behind the text for a comic-print feel";
    }

    public class TextPage : TextBillboardPage
    {
        public override string Category => "Foreground";
        protected override ReferencesObject.System GetReference(ReferencesObject references) => references.text;
        protected override string Name => "Text";
        protected override string Settings => "text";
        public override string Subtitle => "The words and characters displayed in your effect";

        protected override List<(string name, bool valid)> GetValidationStates(SerializedObject target)
        {
            var states = base.GetValidationStates(target);

            var textProperty = GetProperty(target, "text");
            var billboards = GetProperty(target, "billboards");

            if (textProperty != null && billboards != null)
            {
                for (int i = 0; i < billboards.arraySize; i++)
                {
                    var billboard = billboards.GetArrayElementAtIndex(i);
                    var obj = billboard.FindPropertyRelative("Object");
                    if (obj?.objectReferenceValue is not TextObject textObject) continue;

                    for (int line = 0; line < textProperty.arraySize; line++)
                    {
                        string line_text = textProperty.GetArrayElementAtIndex(line).stringValue;
                        foreach (char c in line_text.Where(c => c != ' '))
                            if (!textObject.HasCharacter(c))
                                states.Add(($"Billboard {i + 1}: missing character '{c}'", false));
                    }
                }
            }

            return states;
        }

        protected override Layout Layout => new Layout()
            .Header("Particle", "Appearance and duration of each particle")
            .Property("lifetime")

            .Header("Text", "What is displayed and how it is arranged")
            .Property("text", "alignment")

            .Header("Overlay", "Halftone texture layered on top of the particle")
            .Property("overlay", "overlayColor", "overlayRotation", "overlayTiling")

            .Header("Modifiers", "Optional effects applied when particles spawn")
            .Property("delay", "randomDelay")

            .Header("Position", "How particles are placed and move")
            .Property("distance", "speed", "flatten", "spacing", "lineHeight", "shape", "sizeOverText")
            .SubHeader("Movement")
            .Property(
                "turbulence", "turbulenceSpeed", "inMovement", "outMovement",
                "movementEasing", "movementAttack", "movementRelease", "movementOffset")

            .Separate()
            .Property("billboards");
    }

    public class ImpactPage : TextBillboardPage
    {
        public override string Category => "Foreground";
        protected override ReferencesObject.System GetReference(ReferencesObject references) => references.impact;
        protected override string Name => "Impact";
        protected override string Settings => "impact";
        public override string Subtitle => "A burst of sprites that plays on spawn to punch up the opening frame";
    }

    public class EffectsPage : TextAmountBillboardPage
    {
        public override string Category => "Foreground";
        protected override ReferencesObject.System GetReference(ReferencesObject references) => references.effect;
        protected override string Name => "Effects";
        protected override string Settings => "effects";
        public override string Subtitle => "Decorative sprite particles scattered around the effect for added flair";
    }

    public class SmokePage : AmountBillboardPage
    {
        public override string Category => "3D";
        protected override ReferencesObject.System GetReference(ReferencesObject references) => references.smoke;
        protected override string Name => "Smoke";
        protected override string Settings => "smoke";
        public override string Subtitle => "Soft smoke puffs that billow and fade around the effect";
    }

    public class DebrisPage : AmountBillboardPage
    {
        public override string Category => "3D";
        protected override ReferencesObject.System GetReference(ReferencesObject references) => references.debris;
        protected override string Name => "Debris";
        protected override string Settings => "debris";
        public override string Subtitle => "Small fragments that eject outward from the impact point with physics";

        protected override Layout Layout => new Layout()
            .Header("Particle", "Appearance and physical behaviour of each piece")
            .Property("lifetime", "collision", "gravity")

            .Header("Overlay", "Halftone texture layered on top of the particle")
            .Property("overlay", "overlayColor", "overlayRotation", "overlayTiling")

            .Header("Modifiers", "Optional effects applied when particles spawn")
            .Property("delay", "randomDelay")

            .Separate()
            .Property("billboards");
    }
}