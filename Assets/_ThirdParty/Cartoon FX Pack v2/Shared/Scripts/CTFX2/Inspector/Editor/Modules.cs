using UnityEditor;
using PSD.Creator.Inspector;
using PSD.Creator.Inspector.Utility;
using CTFX2.Creator.Data;
using PSD.Creator.Utility.Inspector.Editors;

namespace CTFX2.Creator.Utility.Inspector.Editors
{
    [CustomPropertyDrawer(typeof(SmokeTrail))]
    public class SmokeTrailDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Smoke", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("overlay", "overlayColor", "overlayRotation", "overlayTiling")
            .Property("size", PropertyOverride.WithMinMaxRange(min: 0.01f, max: 5f))
            .Header("Color")
            .Property("colorGradient")
            .Header("Spawning")
            .Property("delay", "randomDelay")
            .Property("density")
            .Property("lifetime")
            .Property("probability");
    }

    [CustomPropertyDrawer(typeof(EffectTrail))]
    public class EffectTrailDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Effect", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("overlay", "overlayColor", "overlayRotation", "overlayTiling")
            .Property("size", PropertyOverride.WithMinMaxRange(min: 0.01f, max: 15f))
            .Header("Color")
            .Property("colorGradient")
            .Property("secondColorGradient")
            .Header("Spawning")
            .Property("delay", "randomDelay")
            .Property("count")
            .Property("probability");
    }

    [CustomPropertyDrawer(typeof(Background.BackgroundModule))]
    public class BackgroundModuleDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Background", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("moduleUseOverlay")
            .Property("size")
            .Header("Color")
            .Property("colorGradient")
            .Header("Randomization")
            .Property("randomOffset")
            .Property("randomRotation");
    }

    [CustomPropertyDrawer(typeof(Halftone.HalftoneModule))]
    public class HalftoneModuleDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Halftone", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("moduleUseOverlay")
            .Property("size")
            .Header("Color")
            .Property("colorGradient")
            .Header("Randomization")
            .Property("randomOffset")
            .Property("randomRotation");
    }

    [CustomPropertyDrawer(typeof(Text.TextModule))]
    public class TextModuleDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Font", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("moduleUseOverlay")
            .Property("size", PropertyOverride.WithMinMaxRange(min: 0.01f, max: 10f))
            .Header("Color")
            .Property("colorSampling")
            .Property("colorGradient")
            .Property("secondColorGradient")
            .Property("outlineColor")
            .Header("Randomization")
            .Property("randomOffset")
            .Property("randomRotation");
    }

    [CustomPropertyDrawer(typeof(Impact.ImpactModule))]
    public class ImpactModuleDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Impact", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("moduleUseOverlay")
            .Property("size")
            .Header("Color")
            .Property("colorGradient")
            .Header("Randomization")
            .Property("randomOffset")
            .Property("randomRotation");
    }

    [CustomPropertyDrawer(typeof(Effects.EffectsModule))]
    public class EffectsModuleDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Effect", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("moduleUseOverlay")
            .Property("size", PropertyOverride.WithMinMaxRange(min: 0.01f, max: 15f))
            .Header("Color")
            .Property("colorGradient")
            .Property("secondColorGradient")
            .Header("Spawning")
            .Property("count", PropertyOverride.WithMinMaxRange(min: 1, max: 20))
            .Header("Positioning")
            .Property("pivot")
            .Property("spread")
            .Header("Randomization")
            .Property("randomOffset");
    }

    [CustomPropertyDrawer(typeof(Smoke.SmokeModule))]
    public class SmokeModuleDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Smoke", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("moduleUseOverlay")
            .Property("size", PropertyOverride.WithMinMaxRange(min: 0.01f, max: 5f))
            .Header("Color")
            .Property("colorGradient")
            .Header("Spawning")
            .Property("count")
            .Header("Shape")
            .Property("shape")
            .Property("donutYOffset")
            .Property("angle")
            .Header("Movement")
            .Property("force")
            .Property("drag")
            .Property("spread")
            .Separate()
            .BeginBox("Trail")
            .Property("enableTrail")
            .Property("trail")
            .EndBox();
    }

    [CustomPropertyDrawer(typeof(Debris.DebrisModule))]
    public class DebrisModuleDrawer : ModuleDrawer
    {
        protected override Layout GetLayout() => new Layout()
            .Header("Particle")
            .Property("Object", "Debris", validate: new NonNullValidation("Select an object for the billboard to render."))
            .Property("moduleUseOverlay")
            .Property("size", PropertyOverride.WithMinMaxRange(min: 0.01f, max: 5f))
            .SubHeader("Spawning")
            .Property("count")
            .Header("Interactions")
            .Property("collision")
            .Header("Color")
            .Property("colorGradient")
            .Header("Movement")
            .Property("force")
            .Property("upForce")
            .Property("spread")
            .Property("angularVelocity")
            .Separate()
            .BeginBox("Trail")
            .Property("enableTrail")
            .Property("trail")
            .EndBox();
    }
}