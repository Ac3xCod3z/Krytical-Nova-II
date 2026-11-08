using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    public static partial class GameFeelBuilder
    {
        public const string GroundScuffVfx = "PFX_Ground_Scuff";
        /// <summary>Seconds a scuff mark lies on the ground before it has faded (owner VFX pick c: 1-2 s).</summary>
        public const float GroundScuffSeconds = 1.6f;

        /// <summary>
        /// Ground scuffs and slide marks (owner, 7 Nov 2026 VFX pick c): a few dark streaks lying flat on the ground along
        /// the way the body travels, fading out in <see cref="GroundScuffSeconds"/>. A pooled one-shot like the other ride
        /// puffs (capped by the pool and by its own particle count), played at a getting-off's touchdown and along its
        /// roll and slide.
        /// </summary>
        private static void BuildGroundScuff()
        {
            WeatherVfxBuilder.EnsureFolder(VfxFolder);
            var go = new GameObject(GroundScuffVfx);
            try
            {
                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 0.1f;
                main.loop = false;
                main.playOnAwake = true;
                main.startLifetime = GroundScuffSeconds;
                main.startSpeed = 0f;
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(0.22f, 0.35f);  // across the travel
                main.startSizeY = new ParticleSystem.MinMaxCurve(0.8f, 1.3f);    // along it
                main.startSizeZ = 1f;
                main.startRotation = new ParticleSystem.MinMaxCurve(-0.12f, 0.12f);
                main.startColor = new Color(0.10f, 0.08f, 0.06f, 1f);
                main.maxParticles = 6;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.stopAction = ParticleSystemStopAction.None;

                var emission = ps.emission;
                emission.rateOverTime = 0f;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.45f, 0.01f, 0.25f);
                shape.position = new Vector3(0f, 0.02f, 0f); // just above the ground: no fighting with it

                var colour = ps.colorOverLifetime;
                colour.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.04f), new GradientAlphaKey(0.45f, 0.55f), new GradientAlphaKey(0f, 1f) });
                colour.color = gradient;

                var renderer = go.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                renderer.alignment = ParticleSystemRenderSpace.Local; // the streaks lie along the way the body faces
                renderer.sharedMaterial = WeatherVfxBuilder.ParticleMaterial("M_GroundScuff", SwipeTexture, additive: false);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                PrefabUtility.SaveAsPrefabAsset(go, Vfx(GroundScuffVfx));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
