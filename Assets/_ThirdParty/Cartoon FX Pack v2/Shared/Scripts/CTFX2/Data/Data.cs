using UnityEngine;
using System;
using PSD.Creator.Utility;
using PSD.Creator.Objects;
using PSD.Creator.Data.Attributes;
using PSD.Creator.Data;
using CTFX2.Creator.Objects;

namespace CTFX2.Creator.Data
{
    [Serializable]
    public class CartoonFXBasic : Basic
    {
        [HideInInspector] public bool useCameraShake;
        [Tooltip("Shake any camera with CameraShakeController script attached to it when this effect is initialized.")]
        [Optional("useCameraShake")] public CameraShake.CameraShake cameraShake;
        public enum Alignment
        {
            SpawnFacingCamera,
            AlwaysFaceCamera,
            FixedRotation,
        }
        [Tooltip("Controls how billboard particles orient themselves relative to the camera.")]
        public Alignment alignment = Alignment.AlwaysFaceCamera;
    }

    [Serializable]
    public abstract class TextBillboard<T, TModule> : Billboard<T, TModule> where T : BillboardObject where TModule : TextBillboard<T, TModule>.TextBillboardModule
    {
        // MOVEMENT
        [Tooltip("How far from the origin particles are placed, in world units.")]
        [Range(0.01f, 20)] public float distance = 2;
        [Tooltip("How quickly particles move toward their target position. Higher values feel snappier.")]
        [Range(0, 0.75f)] public float speed = 0.75f;
        [Tooltip("Amount of random noise applied to particle movement over time.")]
        [Range(0, 5)] public float turbulence = 0.5f;
        [Range(0, 50)] public float turbulenceSpeed = 1;
        public enum EMovement { None, Flip, Rise, Slide }
        [Tooltip("Animation applied to particles as they enter the screen.")]
        public EMovement inMovement = EMovement.None;
        [Tooltip("Animation applied to particles as they leave the screen.")]
        public EMovement outMovement = EMovement.None;
        [Tooltip("Easing curve object used to shape the in and out movement animations.")]
        [Label("Easing")] public EasingObject movementEasing;
        [Tooltip("Fraction of the lifetime used for the entry movement animation.")]
        [ShowIf("inMovement", (int)EMovement.None, "outMovement", (int)EMovement.None, useOr: false, invert: true), NonNullableCheck("movementEasing"), Range(0, 0.49f), Label("Attack")]
        public float movementAttack = 0.2f;
        [Tooltip("Fraction of the lifetime used for the exit movement animation.")]
        [RequiredValue("Movement duration is longer than the lifetime.", "lifetime", RequiredValueAttribute.Operation.Add, RequiredValueAttribute.Compare.Under, "movementAttack", "movementRelease")]
        [ShowIf("inMovement", (int)EMovement.None, "outMovement", (int)EMovement.None, useOr: false, invert: true), NonNullableCheck("movementEasing"), Range(0, 0.49f), Label("Release")]
        public float movementRelease = 0.1f;
        [Tooltip("Distance particles travel during the entry or exit movement animation.")]
        [ShowIf("inMovement", "outMovement", new[] { (int)EMovement.Rise, (int)EMovement.Slide }), NonNullableCheck("movementEasing"), Label("Offset")]
        public float movementOffset = 5;

        [Serializable]
        public class TextBillboardModule : BillboardModule
        {
            [Tooltip("Randomly shifts each particle's position by up to this fraction of its size.")]
            [NonNullableCheck("Object"), Range(0, 1)] public float randomOffset = 0.1f;
        }
    }

    [Serializable]
    public abstract class TextAmountBillboard<T, TModule> : TextBillboard<T, TModule> where T : BillboardObject where TModule : TextAmountBillboard<T, TModule>.TextAmountBillboardModule
    {
        [Serializable]
        public class TextAmountBillboardModule : TextBillboardModule
        {
            [Tooltip("How many particles are spawned. A random value between X and Y is picked each time.")]
            [NonNullableCheck("Object"), MinMaxSlider(1, 200)] public Vector2Int count = new Vector2Int(5, 8);
        }
    }

    [Serializable]
    public class Background : TextBillboard<BackgroundObject, Background.BackgroundModule>
    {
        [Serializable]
        public class BackgroundModule : TextBillboardModule
        {
            [Tooltip("Randomly rotates each background sprite by up to this many degrees.")]
            [NonNullableCheck("Object"), Range(0, 360)] public float randomRotation = 5;
        }
    }

    [Serializable]
    public class Halftone : TextBillboard<HalftoneObject, Halftone.HalftoneModule>
    {
        [Serializable]
        public class HalftoneModule : TextBillboardModule
        {
            [Tooltip("Randomly rotates each halftone sprite by up to this many degrees.")]
            [NonNullableCheck("Object"), Range(0, 360)] public float randomRotation = 5;
        }
    }

    [Serializable]
    public class Text : TextBillboard<TextObject, Text.TextModule>
    {
        // TEXT
        [Tooltip("The lines of text to display. Each entry in the array is a new line.")]
        public string[] text = { "KA-", "BOOM!!" };
        public enum EAlignment { Left, Middle, Right }
        [Tooltip("Horizontal alignment of each line of text relative to the longest line.")]
        [ShowIf("text", count: 1)] public EAlignment alignment = EAlignment.Middle;

        // POSITIONING
        [Tooltip("Blends between circular arc layout (0) and flat horizontal layout (1).")]
        [Range(0, 1)] public float flatten = 1;
        [Tooltip("Horizontal gap between characters, in world units.")]
        [Range(0, 20)] public float spacing = 1.5f;
        [Tooltip("Vertical gap between lines of text, in world units.")]
        [Range(0, 10), ShowIf("text", count: 1)] public float lineHeight = 3f;
        [Tooltip("Vertical offset applied per character across the text, evaluated left to right.")]
        public AnimationCurve shape = AnimationUtility.DefaultConstantZero;
        [Tooltip("Scales each character's size based on its horizontal position across the text.")]
        public AnimationCurve sizeOverText = AnimationUtility.DefaultConstantOne;

        [Serializable]
        public class TextModule : TextBillboardModule
        {
            [Tooltip("Randomly rotates each character sprite by up to this many degrees.")]
            [NonNullableCheck("Object"), Range(0, 360)] public float randomRotation = 5;
            public enum EColorSample { ColorOverLife, ColorOverText }
            [Tooltip("Whether the color gradient is sampled over the particle's lifetime or across the width of the text.")]
            [NonNullableCheck("Object"), ShowIf("colorIsGradient", "|", "(", "secondColorIsGradient", "&", "useSecondColor", ")")] public EColorSample colorSampling;
            [HideInInspector] public bool useSecondColor = false;
            [HideInInspector] public bool secondColorIsGradient = false;
            [HideInInspector] public Color secondColorSolid = Color.white;
            [Tooltip("A second color or gradient blended with the primary color for richer visuals.")]
            [NonNullableCheck("Object"), GradientUsage(true), Optional("useSecondColor"), ColorMode("secondColorIsGradient", "secondColorSolid"), Label("2nd Color")] public Gradient secondColorGradient = GradientUtility.DefaultGradient;
            [HideInInspector] public bool useOutline = true;
            [Tooltip("Color of the outline drawn around each character. Supports HDR for emissive outlines.")]
            [NonNullableCheck("Object"), ColorUsage(true, true), Optional("useOutline")] public Color outlineColor = Color.black;
        }
    }

    [Serializable]
    public class Impact : TextBillboard<ImpactObject, Impact.ImpactModule>
    {
        [Serializable]
        public class ImpactModule : TextBillboardModule
        {
            [Tooltip("Randomly rotates each impact sprite by up to this many degrees.")]
            [NonNullableCheck("Object"), Range(0, 360)] public float randomRotation = 5;
        }
    }

    [Serializable]
    public class Effects : TextAmountBillboard<EffectsObject, Effects.EffectsModule>
    {
        [Serializable]
        public class EffectsModule : TextAmountBillboardModule
        {
            [HideInInspector] public bool useSecondColor = false;
            [HideInInspector] public bool secondColorIsGradient = false;
            [HideInInspector] public Color secondColorSolid = Color.white;
            [Tooltip("A second color or gradient blended with the primary color for richer visuals.")]
            [NonNullableCheck("Object"), GradientUsage(true), Optional("useSecondColor"), ColorMode("secondColorIsGradient", "secondColorSolid"), Label("2nd Color")] public Gradient secondColorGradient = GradientUtility.DefaultGradient;
            [Tooltip("Distance from the effect origin that particles are placed at spawn.")]
            [NonNullableCheck("Object"), Range(0, 20)] public float pivot = 5;
            [Tooltip("How widely particles are scattered around the pivot point. 0 is a tight ring, 1 is fully random.")]
            [NonNullableCheck("Object"), Range(0, 1)] public float spread = 0.1f;
        }
    }

    [Serializable]
    public class Smoke : AmountBillboard<SmokeObject, Smoke.SmokeModule>
    {
        [Serializable]
        public class SmokeModule : AmountBillboardModule
        {
            public enum Shape
            {
                Volume,
                Donut,
                Mushroom,
                Hat
            }
            [Tooltip("The volumetric shape particles are spawned within.")]
            [NonNullableCheck("Object")] public Shape shape = Shape.Volume;
            [Tooltip("Shifts the donut shape up or down along the Y axis.")]
            [NonNullableCheck("Object"), Range(-10, 10), ShowIf("shape", value: (int)Shape.Donut, false)] public float donutYOffset = -2;
            [Tooltip("Opening angle of the hat shape in degrees. Smaller values make a tighter cone.")]
            [NonNullableCheck("Object"), Range(0, 90), ShowIf("shape", value: (int)Shape.Hat, false)] public float angle = 20;

            [Tooltip("Outward velocity applied to particles on spawn.")]
            [NonNullableCheck("Object"), Range(0, 200)] public float force = 30;
            [Tooltip("How quickly particles slow down after spawning. Higher values stop them faster.")]
            [NonNullableCheck("Object"), Range(0, 200)] public float drag = 30f;
            [Tooltip("How randomly particles are scattered from their spawn direction. 0 is straight, 1 is fully random.")]
            [NonNullableCheck("Object"), Range(0, 1)] public float spread = 0.25f;

            [Tooltip("Spawns a trail of effect particles behind each smoke particle.")]
            [NonNullableCheck("Object")] public bool enableTrail = false;
            [Tooltip("Settings for the effect trail spawned behind each smoke particle.")]
            [NonNullableCheck("Object"), ShowIf("enableTrail")] public EffectTrail trail;
        }
    }

    [Serializable]
    public class Debris : AmountBillboard<DebrisObject, Debris.DebrisModule>
    {
        [Tooltip("Quality level of physics collision detection for debris particles.")]
        public CollisionQuality collision = CollisionQuality.Medium;
        [Tooltip("Downward acceleration applied to debris particles, simulating gravity.")]
        [Range(0, 20)] public float gravity = 7;

        [Serializable]
        public class DebrisModule : AmountBillboardModule
        {
            [Tooltip("Spawns a smoke trail behind each debris particle as it flies.")]
            [NonNullableCheck("Object")] public bool enableTrail = false;
            [Tooltip("Settings for the smoke trail spawned behind each debris particle.")]
            [NonNullableCheck("Object"), ShowIf("enableTrail")] public SmokeTrail trail;

            [Tooltip("Outward velocity applied to debris particles on spawn.")]
            [NonNullableCheck("Object"), Range(0, 200)] public float force = 20;
            [Tooltip("How randomly debris is scattered from its spawn direction. 0 is straight, 1 is fully random.")]
            [NonNullableCheck("Object"), Range(0, 1)] public float spread = 0.5f;
            [Tooltip("Additional upward velocity applied on spawn to make debris arc into the air.")]
            [NonNullableCheck("Object"), Range(0, 50)] public float upForce = 5f;
            [Tooltip("How fast debris spins as it flies through the air.")]
            [NonNullableCheck("Object"), Range(0, 30)] public float angularVelocity = 5;

            [HideInInspector] public bool overrideCollision = false;
            [Tooltip("Overrides the parent layer's collision quality for this debris particle specifically.")]
            [NonNullableCheck("Object"), Optional("overrideCollision")] public CollisionQuality collision = CollisionQuality.Medium;
        }
    }

    [Serializable]
    public class SmokeTrail : Billboard<SmokeObject, SmokeTrail.SmokeTrailModule>
    {
        [Tooltip("The smoke object asset used to render each trail particle.")]
        public SmokeObject Object;

        [HideInInspector] public bool colorIsGradient = false;
        [HideInInspector] public Color colorSolid = Color.white;
        [Tooltip("Color or gradient applied to each trail particle over its lifetime.")]
        [NonNullableCheck("Object"), GradientUsage(true), ColorMode("colorIsGradient", "colorSolid"), Label("Color")] public Gradient colorGradient = GradientUtility.DefaultGradient;
        [Tooltip("Size range of each trail particle. A random value between X and Y is picked at spawn.")]
        [NonNullableCheck("Object"), MinMaxSlider(0.01f, 10f)] public Vector2 size = new Vector2(1.7f, 2);
        [Tooltip("How many trail particles are spawned per unit of distance traveled by the parent particle.")]
        [NonNullableCheck("Object"), Range(0f, 30f)] public float density = 1f;
        [Tooltip("Chance of a trail particle spawning at each spawn opportunity. 1 always spawns, 0 never spawns.")]
        [NonNullableCheck("Object"), Range(0, 1)] public float probability = 1;

        [Serializable]
        public class SmokeTrailModule : BillboardModule { }
    }

    [Serializable]
    public class EffectTrail : Billboard<EffectsObject, EffectTrail.EffectTrailModule>
    {
        [Tooltip("The effect object asset used to render each trail particle.")]
        public EffectsObject Object;

        [HideInInspector] public bool colorIsGradient = false;
        [HideInInspector] public Color colorSolid = Color.white;
        [Tooltip("Primary color or gradient applied to each trail particle over its lifetime.")]
        [NonNullableCheck("Object"), GradientUsage(true), ColorMode("colorIsGradient", "colorSolid"), Label("Color")] public Gradient colorGradient = GradientUtility.DefaultGradient;
        [HideInInspector] public bool useSecondColor = false;
        [HideInInspector] public bool secondColorIsGradient = false;
        [HideInInspector] public Color secondColorSolid = Color.white;
        [Tooltip("A second color or gradient blended with the primary color for richer trail visuals.")]
        [NonNullableCheck("Object"), GradientUsage(true), Optional("useSecondColor"), ColorMode("secondColorIsGradient", "secondColorSolid"), Label("2nd Color")] public Gradient secondColorGradient = GradientUtility.DefaultGradient;
        [Tooltip("Size range of each trail particle. A random value between X and Y is picked at spawn.")]
        [NonNullableCheck("Object"), MinMaxSlider(0.01f, 10f)] public Vector2 size = new Vector2(2, 3);
        [Tooltip("How many trail particles spawn per trigger. A random value between X and Y is picked each time.")]
        [NonNullableCheck("Object"), MinMaxSlider(0, 10)] public Vector2Int count = new Vector2Int(2, 4);
        [Tooltip("Chance of the trail spawning at each opportunity. 1 always spawns, 0 never spawns.")]
        [NonNullableCheck("Object"), Range(0, 1)] public float probability = 0.5f;

        [Serializable]
        public class EffectTrailModule : BillboardModule { }
    }
}