using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KryticalNova.Audio;
using KryticalNova.Items;
using KryticalNova.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The world's light keeps the beat in a fight (owner, 24 Oct 2026): everything glowing breathes with the fight
    /// music, settling a little lower between beats and rising well above itself on each beat, more on the first of a
    /// bar. Four things move together: the glowing surfaces of the map (every emissive material: signs, the town's orb,
    /// neon, lit windows), the screen's bloom round them, point and spot lights near the player, and our own glowing
    /// shaders (the Nova) through one global value. Out of a fight everything is exactly as it was, and every material
    /// is put back when the fight ends, the map changes or Play stops. A few dozen material colours and lights a frame:
    /// about a tenth of a millisecond. Settings ▸ Audio can switch it off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatGlow : MonoBehaviour
    {
        private static readonly int PulseId = Shader.PropertyToID("_KN_BeatPulse");
        // Beautify (the game's bloom) has no assembly of its own to reference: its volume component is found by name.
        private const string BloomComponent = "Beautify.Universal.Beautify";
        private const string BloomField = "bloomIntensity";
        // Emission as URP Lit has it, and as Synty's shaders have it.
        private static readonly int UrpEmission = Shader.PropertyToID("_EmissionColor");
        private static readonly int SyntyEmission = Shader.PropertyToID("_Emission_Color");
        private static readonly int SyntyEmissionOn = Shader.PropertyToID("_Enable_Emission");
        private const int RenderersPerFrame = 400; // the look for glowing materials is spread over frames

        [SerializeField] private BeatGlowSettings settings;

        private Volume volume;
        private VolumeProfile profile;
        private VolumeParameter<float> bloom;
        private Type bloomType;
        private FieldInfo bloomField;
        private float bloomBase = -1f;
        private bool glowing;

        private struct Lit
        {
            public Light light;
            public float baseIntensity, written;
        }

        private struct Glowing
        {
            public Material material;
            public int property;
            public Color baseColour;
        }

        private readonly List<Lit> lights = new List<Lit>();
        private readonly List<Glowing> surfaces = new List<Glowing>();
        private float nextScan;
        private Scene surfacesOf;
        private bool surfacesFound, surfacesLooking;

        private void OnEnable()
        {
            Build();
            SceneManager.activeSceneChanged += OnSceneChanged;
            Application.quitting += Rest;
        }

        private void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnSceneChanged;
            Application.quitting -= Rest;
            Rest();
            if (volume != null) volume.enabled = false;
        }

        private void OnDestroy()
        {
            Rest();
            if (profile != null) Destroy(profile);
        }

        // A new map: its materials are its own; the old ones are put back first.
        private void OnSceneChanged(Scene from, Scene to)
        {
            Rest();
            surfaces.Clear();
            surfacesFound = false;
        }

        private void Build()
        {
            if (volume != null) { volume.enabled = true; return; }
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                bloomType = assembly.GetType(BloomComponent, false);
                if (bloomType != null) break;
            }
            if (bloomType == null) return;
            bloomField = bloomType.GetField(BloomField, BindingFlags.Public | BindingFlags.Instance);
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "Beat Glow";
            var component = profile.Add(bloomType, false);
            bloom = bloomField != null ? bloomField.GetValue(component) as VolumeParameter<float> : null;
            if (bloom != null) bloom.overrideState = true;
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 1000f; // over every zone's own volume, but only while it has weight
            volume.weight = 0f;
            volume.sharedProfile = profile;
        }

        private void LateUpdate()
        {
            if (settings == null) return;
            float live = AudioPreferences.BeatLights ? CombatBeat.Live01 : 0f;
            if (live <= 0.01f || !CombatBeat.HasBeat)
            {
                if (glowing) Rest();
                ReadBloomBase();
                return;
            }
            if (!glowing) Begin();
            glowing = true;
            // 0 between beats, 1 on one (more on a bar's first), already faded in and out with the fight.
            // Settings ▸ Accessibility scales the whole swing (flashing effects), down to nothing.
            float safe = KryticalNova.Core.Accessibility.FlashScale;
            float pulse = CombatBeat.Pulse(settings.sharpness) * (CombatBeat.OnBar ? settings.barLift : 1f) * safe;
            float dip = settings.dip * live * safe;

            Shader.SetGlobalFloat(PulseId, pulse * settings.shaderBoost);
            if (volume != null && bloom != null && bloomBase >= 0f)
            {
                volume.weight = 1f;
                bloom.value = bloomBase * Swing(dip, settings.bloomBoost, pulse);
            }
            PulseLights(Swing(dip, settings.lightBoost, pulse));
            PulseSurfaces(Swing(dip, settings.emissionBoost, pulse));
        }

        // Between beats a share lower (the dip), on the beat that much higher (the boost): 1 is as the map has it.
        private static float Swing(float dip, float boost, float pulse) => Mathf.Max(0f, 1f - dip + (dip + boost) * pulse);

        private void Begin()
        {
            if (!surfacesFound || surfacesOf != SceneManager.GetActiveScene())
            {
                if (!surfacesLooking) StartCoroutine(FindSurfaces());
            }
        }

        // The zone's own bloom, read while this volume adds nothing (out of a fight), so the swing is a share of it.
        private void ReadBloomBase()
        {
            if (bloomType == null || bloomField == null || VolumeManager.instance == null || VolumeManager.instance.stack == null) return;
            VolumeComponent current = VolumeManager.instance.stack.GetComponent(bloomType);
            if (current != null && bloomField.GetValue(current) is VolumeParameter<float> value) bloomBase = value.value;
        }

        // ------------------------------------------------------------------ glowing surfaces

        // Every material in the map that glows, once a map, a few hundred renderers a frame so no frame hitches.
        private IEnumerator FindSurfaces()
        {
            surfacesLooking = true;
            Scene scene = SceneManager.GetActiveScene();
            var seen = new HashSet<Material>();
            foreach (Glowing each in surfaces) seen.Add(each.material);
            Renderer[] renderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (i > 0 && i % RenderersPerFrame == 0) yield return null;
                Renderer renderer = renderers[i];
                if (renderer == null || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null || !seen.Add(material)) continue;
                    if (TryGlow(material, out Glowing glow)) surfaces.Add(glow);
                }
            }
            surfacesOf = scene;
            surfacesFound = true;
            surfacesLooking = false;
        }

        private static bool TryGlow(Material material, out Glowing glow)
        {
            glow = default;
            if (material.HasProperty(SyntyEmission) && (!material.HasProperty(SyntyEmissionOn) || material.GetFloat(SyntyEmissionOn) > 0.5f))
                glow = new Glowing { material = material, property = SyntyEmission, baseColour = material.GetColor(SyntyEmission) };
            else if (material.HasProperty(UrpEmission) && material.IsKeywordEnabled("_EMISSION"))
                glow = new Glowing { material = material, property = UrpEmission, baseColour = material.GetColor(UrpEmission) };
            else return false;
            return glow.baseColour.maxColorComponent > 0.01f;
        }

        private void PulseSurfaces(float factor)
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                Glowing glow = surfaces[i];
                if (glow.material == null) continue;
                Color colour = glow.baseColour * factor;
                colour.a = glow.baseColour.a;
                glow.material.SetColor(glow.property, colour);
            }
        }

        private void RestoreSurfaces()
        {
            foreach (Glowing glow in surfaces)
            {
                if (glow.material == null) continue;
                glow.material.SetColor(glow.property, glow.baseColour);
#if UNITY_EDITOR
                // Back as it was: the Editor must not save the map's materials (vendor assets) because a fight pulsed them.
                UnityEditor.EditorUtility.ClearDirty(glow.material);
#endif
            }
        }

        // ------------------------------------------------------------------ lights round the player

        private void PulseLights(float factor)
        {
            if (Time.unscaledTime >= nextScan) Scan();
            for (int i = 0; i < lights.Count; i++)
            {
                Lit lit = lights[i];
                if (lit.light == null) continue;
                // Something else set the light since (the day's streetlights): that is its intensity now.
                if (!Mathf.Approximately(lit.light.intensity, lit.written)) lit.baseIntensity = lit.light.intensity;
                lit.written = lit.baseIntensity * factor;
                lit.light.intensity = lit.written;
                lights[i] = lit;
            }
        }

        private void Scan()
        {
            nextScan = Time.unscaledTime + settings.lightRescanSeconds;
            Transform player = PlayerInventory.Local != null ? PlayerInventory.Local.transform : null;
            if (player == null) return;
            float reach = settings.lightRadius * settings.lightRadius;
            var kept = new HashSet<Light>();
            for (int i = lights.Count - 1; i >= 0; i--)
            {
                Lit lit = lights[i];
                bool keep = lit.light != null && lit.light.isActiveAndEnabled && (lit.light.transform.position - player.position).sqrMagnitude <= reach;
                if (keep) { kept.Add(lit.light); continue; }
                if (lit.light != null && Mathf.Approximately(lit.light.intensity, lit.written)) lit.light.intensity = lit.baseIntensity;
                lights.RemoveAt(i);
            }
            foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (kept.Contains(light) || light.type == LightType.Directional || !light.enabled) continue;
                if ((light.transform.position - player.position).sqrMagnitude > reach) continue;
                lights.Add(new Lit { light = light, baseIntensity = light.intensity, written = light.intensity });
            }
        }

        // Out of a fight: every light, surface and the bloom as they were, no pulse.
        private void Rest()
        {
            glowing = false;
            Shader.SetGlobalFloat(PulseId, 0f);
            if (volume != null) volume.weight = 0f;
            foreach (Lit lit in lights)
                if (lit.light != null && Mathf.Approximately(lit.light.intensity, lit.written)) lit.light.intensity = lit.baseIntensity;
            lights.Clear();
            nextScan = 0f;
            RestoreSurfaces();
        }
    }
}
