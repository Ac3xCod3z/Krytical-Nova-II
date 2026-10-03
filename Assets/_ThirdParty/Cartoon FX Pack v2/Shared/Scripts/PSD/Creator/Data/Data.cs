using UnityEngine;
using System;
using PSD.Creator.Utility;
using PSD.Creator.Objects;
using PSD.Creator.Data.Attributes;

namespace PSD.Creator.Data
{
    [Serializable]
    public abstract class Settings
    {
        public enum CollisionQuality { Off, Low, Medium, High }

        public bool enabled = true;
    }
    [Serializable]
    public abstract class ComponentSettings<T> : Settings where T : ComponentObject { }

    [Serializable]
    public abstract class Basic : Settings
    {
        [Tooltip("What happens to the GameObject when the particle system stops playing.")]
        public ParticleSystemStopAction stopAction = ParticleSystemStopAction.Destroy;
        [HideInInspector] public bool useDitherFade = true;
        [Optional("useDitherFade"), Range(0, 1)] public float ditherFade = 1;
    }

    [Serializable]
    public class Reference : Settings
    {
        [Tooltip("The references object containing all particle systems and materials used by this effect.")]
        public ReferencesObject references;
    }

    [Serializable]
    public class Randomization : Settings
    {
        [Tooltip("The randomization object containing all objects that should be randomized.")]
        public RandomizationObject randomization;

        [Flags]
        public enum ETargets
        {
            None = 0,
            Background = 1 << 0,
            Halftone = 1 << 1,
            Text = 1 << 2,
            Impact = 1 << 3,
            Effects = 1 << 4,
            Smoke = 1 << 5,
            Debris = 1 << 6,
            All = ~0
        }
        [Tooltip("Which properties should be randomized when the Randomize button is pressed.")]
        public ETargets targets = ETargets.All;
    }

    [Serializable]
    public abstract class Particle<T> : ComponentSettings<T> where T : ParticleObject { }

    [Serializable]
    public abstract class Billboard<T, TModule> : Particle<T> where T : BillboardObject where TModule : Billboard<T, TModule>.BillboardModule
    {
        // PARTICLE
        [Tooltip("How long each particle stays visible, in seconds.")]
        [Range(0.01f, 10f)] public float lifetime = 1.25f;

        // OVERLAY
        [HideInInspector] public bool useOverlay = true;
        [Tooltip("The overlay texture object applied on top of each particle.")]
        [Optional("useOverlay")] public OverlayObject overlay;
        [Tooltip("Tint color of the overlay texture. Supports HDR for emissive effects.")]
        [ColorUsage(true, true), ShowIf("useOverlay"), NonNullableCheck("overlay")] public Color overlayColor = Color.black;
        [Tooltip("Rotates the overlay texture by this many degrees.")]
        [Range(0, 360), ShowIf("useOverlay"), NonNullableCheck("overlay")] public float overlayRotation = 0;
        [Range(0.01f, 2), ShowIf("useOverlay"), NonNullableCheck("overlay")] public float overlayTiling = 1;

        // MODIFIERS
        [HideInInspector] public bool useDelay = false;
        [Tooltip("Fixed delay in seconds before this layer starts spawning particles.")]
        [Optional("useDelay"), Range(0, 5f)] public float delay = 0.05f;
        [HideInInspector] public bool useRandomDelay = false;
        [Tooltip("Adds a random extra delay on top of the fixed delay, in seconds.")]
        [Optional("useRandomDelay"), Range(0.01f, 5f)] public float randomDelay = 0.25f;

        [Serializable]
        public class BillboardModule
        {
            [HideInInspector] public bool enabled = true;
            [Tooltip("The billboard object asset that defines the sprite and material for this particle.")]
            public T Object;

            [Tooltip("Whether this billboard uses the shared overlay texture defined on the parent layer.")]
            [ShowIf("useOverlay"), NonNullableCheck("overlay", "Object"), Label("Use Overlay")] public bool moduleUseOverlay = true;

            [HideInInspector] public bool colorIsGradient = false;
            [HideInInspector] public Color colorSolid = Color.white;
            [Tooltip("Color or gradient applied to the particle over its lifetime.")]
            [NonNullableCheck("Object"), GradientUsage(true), ColorMode("colorIsGradient", "colorSolid"), Label("Color")] public Gradient colorGradient = GradientUtility.DefaultGradient;
            [Tooltip("Size range of each particle. A random value between X and Y is picked at spawn.")]
            [NonNullableCheck("Object"), MinMaxSlider(0.01f, 30f)] public Vector2 size = new Vector2(4, 5);
        }
        public TModule[] billboards;
    }

    [Serializable]
    public abstract class AmountBillboard<T, TModule> : Billboard<T, TModule> where T : BillboardObject where TModule : AmountBillboard<T, TModule>.AmountBillboardModule
    {
        [Serializable]
        public class AmountBillboardModule : BillboardModule
        {
            [Tooltip("How many particles are spawned. A random value between X and Y is picked each time.")]
            [NonNullableCheck("Object"), MinMaxSlider(1, 200)] public Vector2Int count = new Vector2Int(5, 8);
        }
    }
}