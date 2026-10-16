using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VolumetricLights;

namespace KryticalNova.World
{
    /// <summary>The player's fog and mist quality (Settings, Graphics). Off removes the volumetric fog, the mist and the lamps' light cones.</summary>
    public enum VolumetricQuality { Off, Low, Medium, High, Ultra }

    /// <summary>
    /// What each fog and mist quality does, in one place (Rendering Bible, section 3b): how finely the mist is
    /// raymarched and at what resolution, and the same for the lamps' light cones, with how far away they still show.
    /// High is the look the game is authored at, except that the cones are drawn at half resolution with an
    /// edge-preserving blur, like the mist. <see cref="VolumetricAtmosphereDriver"/> and
    /// <see cref="StreetLampController"/> follow <see cref="Version"/>; the cones' render feature is set here and is
    /// shared project data, so <see cref="RestoreAuthored"/> puts it back when the session ends.
    /// </summary>
    public static class VolumetricQualityRuntime
    {
        public readonly struct Tier
        {
            /// <summary>Mist: raymarch samples, its shortest step as a share of the authored one, and the resolution divider.</summary>
            public readonly int FogRaymarch;
            public readonly float FogStepScale, FogDownscaling;
            /// <summary>Lamp cones: raymarch samples, shortest step (m), jitter, the distance they start to fade and are gone, and the resolution divider.</summary>
            public readonly int ConeRaymarch;
            public readonly float ConeMinStep, ConeJitter, ConeFadeFrom, ConeGoneAt, ConeDownscaling;

            public Tier(int fogRaymarch, float fogStepScale, float fogDownscaling, int coneRaymarch, float coneMinStep, float coneJitter,
                float coneFadeFrom, float coneGoneAt, float coneDownscaling)
            {
                FogRaymarch = fogRaymarch; FogStepScale = fogStepScale; FogDownscaling = fogDownscaling;
                ConeRaymarch = coneRaymarch; ConeMinStep = coneMinStep; ConeJitter = coneJitter;
                ConeFadeFrom = coneFadeFrom; ConeGoneAt = coneGoneAt; ConeDownscaling = coneDownscaling;
            }
        }

        private static readonly Tier[] Tiers =
        {
            new Tier(3, 2f, 3f, 2, 1f, 4f, 25f, 40f, 2f),       // Off: nothing is drawn; Low's numbers stand ready
            new Tier(3, 2f, 3f, 2, 1f, 4f, 25f, 40f, 2f),       // Low
            new Tier(4, 1.5f, 2f, 4, 0.25f, 1f, 35f, 50f, 2f),  // Medium: cones sampled like High (coarser ones blotch), seen less far
            new Tier(6, 1f, 2f, 4, 0.2f, 1f, 45f, 65f, 2f),     // High: as authored, cones at half resolution
            new Tier(8, 0.75f, 1.5f, 8, 0.1f, 0.5f, 60f, 85f, 1f), // Ultra
        };

        /// <summary>The quality in force. High until the player's settings say otherwise.</summary>
        public static VolumetricQuality Quality { get; private set; } = VolumetricQuality.High;
        /// <summary>Goes up at each change, for the things that follow the quality.</summary>
        public static int Version { get; private set; }
        public static Tier Current => Tiers[(int)Quality];

        private static VolumetricLightsRenderFeature coneFeature;
        private static bool remembered;
        private static float authoredConeDownscaling;
        private static bool authoredConeEdgePreserve;

        public static void Set(VolumetricQuality quality)
        {
            if (Quality == quality && remembered) return;
            Quality = quality;
            Version++;
            VolumetricLightsRenderFeature feature = ConeFeature();
            if (feature == null) return;
            if (!remembered)
            {
                remembered = true;
                authoredConeDownscaling = feature.downscaling;
                authoredConeEdgePreserve = feature.blurEdgePreserve;
            }
            feature.downscaling = Current.ConeDownscaling;
            // A cone drawn smaller is blurred back up without bleeding over the edges of what stands in front of it.
            feature.blurEdgePreserve = Current.ConeDownscaling > 1f || authoredConeEdgePreserve;
        }

        /// <summary>The cones' render feature as it was authored, and the quality back to High.</summary>
        public static void RestoreAuthored()
        {
            if (remembered && coneFeature != null)
            {
                coneFeature.downscaling = authoredConeDownscaling;
                coneFeature.blurEdgePreserve = authoredConeEdgePreserve;
            }
            remembered = false;
            Quality = VolumetricQuality.High;
            Version++;
        }

        private static VolumetricLightsRenderFeature ConeFeature()
        {
            if (coneFeature != null) return coneFeature;
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset pipeline)) return null;
            foreach (ScriptableRendererData data in pipeline.rendererDataList)
            {
                if (data == null) continue;
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                    if (feature is VolumetricLightsRenderFeature cones) return coneFeature = cones;
            }
            return null;
        }
    }
}
