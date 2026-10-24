using System;
using System.Collections.Generic;
using System.Reflection;
using KryticalNova.Audio;
using KryticalNova.Items;
using KryticalNova.UI;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.GameFeel
{
    /// <summary>
    /// The world's light keeps the beat in a fight (owner, 24 Oct 2026): on each beat of the fight music the screen's
    /// bloom lifts a little, so every emissive thing in view (signs, the town's orb, pets' eyes and bodies, glows) flares
    /// with it; point and spot lights round the player swell; and our own glowing shaders (the Nova) read the same pulse
    /// from one global value. Out of a fight nothing moves and everything is as it was. It costs one global float, one
    /// volume value and a few lights a frame: no material is touched. Settings ▸ Audio can switch it off.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BeatGlow : MonoBehaviour
    {
        private static readonly int PulseId = Shader.PropertyToID("_KN_BeatPulse");
        // Beautify (the game's bloom) has no assembly of its own to reference: its volume component is found by name.
        private const string BloomComponent = "Beautify.Universal.Beautify";
        private const string BloomField = "bloomIntensity";

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

        private readonly List<Lit> lights = new List<Lit>();
        private float nextScan;

        private void OnEnable() => Build();

        private void OnDisable()
        {
            Rest();
            if (volume != null) volume.enabled = false;
        }

        private void OnDestroy()
        {
            if (profile != null) Destroy(profile);
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
            glowing = true;
            float pulse = CombatBeat.Pulse(settings.sharpness) * (CombatBeat.OnBar ? settings.barLift : 1f); // already scaled by live

            Shader.SetGlobalFloat(PulseId, pulse * settings.shaderBoost);
            if (volume != null && bloom != null && bloomBase >= 0f)
            {
                volume.weight = 1f;
                bloom.value = bloomBase * (1f + settings.bloomBoost * pulse);
            }
            PulseLights(pulse);
        }

        // The zone's own bloom, read while this volume adds nothing (out of a fight), so the lift is a share of it.
        private void ReadBloomBase()
        {
            if (bloomType == null || bloomField == null || VolumeManager.instance == null || VolumeManager.instance.stack == null) return;
            VolumeComponent current = VolumeManager.instance.stack.GetComponent(bloomType);
            if (current != null && bloomField.GetValue(current) is VolumeParameter<float> value) bloomBase = value.value;
        }

        // ------------------------------------------------------------------ lights round the player

        private void PulseLights(float pulse)
        {
            if (Time.unscaledTime >= nextScan) Scan();
            float factor = 1f + settings.lightBoost * pulse;
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

        // Out of a fight: every light as it was, no lift, no pulse.
        private void Rest()
        {
            glowing = false;
            Shader.SetGlobalFloat(PulseId, 0f);
            if (volume != null) volume.weight = 0f;
            foreach (Lit lit in lights)
                if (lit.light != null && Mathf.Approximately(lit.light.intensity, lit.written)) lit.light.intensity = lit.baseIntensity;
            lights.Clear();
            nextScan = 0f;
        }
    }
}
