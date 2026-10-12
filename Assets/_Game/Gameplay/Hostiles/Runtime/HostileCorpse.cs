using System.Collections;
using System.Collections.Generic;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Hostiles
{
    /// <summary>
    /// What a dead Hostile looks like on this screen: it lies for a moment, then burns away. Looks only, and each
    /// peer plays it for itself from the death it is told of; the server takes the Hostile out of the world when
    /// the same time has passed (<see cref="HostileNet"/>). The model's own materials come back whenever the
    /// Hostile is put away, so one taken from the pool again is whole.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CombatEntity))]
    public sealed class HostileCorpse : MonoBehaviour
    {
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int FadeTex = Shader.PropertyToID("_FadeTex");
        private static readonly int FadeAmount = Shader.PropertyToID("_FadeAmount");
        private static readonly int FadeBurnWidth = Shader.PropertyToID("_FadeBurnWidth");
        private static readonly int FadeBurnColor = Shader.PropertyToID("_FadeBurnColor");
        private static readonly int FadeTiling = Shader.PropertyToID("_FadeTiling");

        // What the packs' shaders call the same things.
        private static readonly string[] AlbedoNames = { "_BaseMap", "_Albedo_Map", "_MainTex" };
        private static readonly string[] EmissionNames = { "_EmissionMap", "_Emission_Map" };
        private static readonly string[] ColorNames = { "_BaseColor", "_Color" };
        private static readonly string[] EmissionColorNames = { "_EmissionColor", "_Emission_Color" };

        [SerializeField] private HostileSettings settings;

        private CombatEntity entity;
        private Renderer[] renderers;
        private Material[][] ownMaterials;
        private readonly List<Material> burning = new List<Material>();
        private Coroutine running;

        /// <summary>0 while whole, 1 when nothing is left. For tests and tools.</summary>
        public float Dissolved { get; private set; }

        private void Awake()
        {
            entity = GetComponent<CombatEntity>();
            renderers = GetComponentsInChildren<Renderer>(true);
            ownMaterials = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++) ownMaterials[i] = renderers[i].sharedMaterials;
        }

        private void OnEnable() => entity.Died += OnDied;

        private void OnDisable()
        {
            entity.Died -= OnDied;
            MakeWhole();
        }

        private void OnDestroy() => ReleaseMaterials();

        private void OnDied()
        {
            if (!Application.isBatchMode) KryticalNova.Core.SpiritMoments.Raise(transform); // its spirit leaves it
            // A server with no screen has nobody to show it to.
            if (settings == null || settings.dissolveShader == null || Application.isBatchMode || !isActiveAndEnabled) return;
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(LieThenBurn());
        }

        private IEnumerator LieThenBurn()
        {
            yield return new WaitForSeconds(settings.corpseSeconds);
            if (entity.IsAlive) { running = null; yield break; } // brought back by a tool meanwhile

            WearBurningMaterials();
            float duration = Mathf.Max(0.1f, settings.dissolveSeconds);
            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                SetDissolved(t / duration);
                yield return null;
            }
            SetDissolved(1f);
            foreach (Renderer r in renderers) r.enabled = false; // gone until the pool hands it out again
            running = null;
        }

        private void WearBurningMaterials()
        {
            ReleaseMaterials();
            var made = new Dictionary<Material, Material>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Material[] own = ownMaterials[i];
                var worn = new Material[own.Length];
                for (int m = 0; m < own.Length; m++)
                {
                    if (own[m] == null) continue;
                    if (!made.TryGetValue(own[m], out Material burn))
                    {
                        burn = BurningCopyOf(own[m]);
                        made[own[m]] = burn;
                        burning.Add(burn);
                    }
                    worn[m] = burn;
                }
                renderers[i].sharedMaterials = worn;
            }
        }

        private Material BurningCopyOf(Material own)
        {
            var burn = new Material(settings.dissolveShader) { name = own.name + " (burning)", hideFlags = HideFlags.HideAndDontSave };
            Texture albedo = TextureOf(own, AlbedoNames);
            if (albedo != null) burn.SetTexture(BaseMap, albedo);
            Texture emission = TextureOf(own, EmissionNames);
            if (emission != null) burn.SetTexture(EmissionMap, emission);
            burn.SetColor(BaseColor, ColorOf(own, ColorNames, Color.white));
            burn.SetColor(EmissionColor, emission != null ? ColorOf(own, EmissionColorNames, Color.white) : Color.black);
            if (settings.dissolveNoise != null) burn.SetTexture(FadeTex, settings.dissolveNoise);
            burn.SetColor(FadeBurnColor, settings.burnColor);
            burn.SetFloat(FadeBurnWidth, settings.burnWidth);
            burn.SetFloat(FadeTiling, settings.noiseTiling);
            burn.SetFloat(FadeAmount, 0f);
            return burn;
        }

        private static Texture TextureOf(Material material, string[] names)
        {
            foreach (string name in names)
                if (material.HasTexture(name) && material.GetTexture(name) != null) return material.GetTexture(name);
            return null;
        }

        private static Color ColorOf(Material material, string[] names, Color fallback)
        {
            foreach (string name in names)
                if (material.HasColor(name)) return material.GetColor(name);
            return fallback;
        }

        private void SetDissolved(float amount)
        {
            Dissolved = Mathf.Clamp01(amount);
            foreach (Material burn in burning) burn.SetFloat(FadeAmount, Dissolved);
        }

        /// <summary>Stops any burning and gives the model its own materials back.</summary>
        public void MakeWhole()
        {
            if (running != null) { StopCoroutine(running); running = null; }
            Dissolved = 0f;
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                renderers[i].sharedMaterials = ownMaterials[i];
                renderers[i].enabled = true;
            }
            ReleaseMaterials();
        }

        private void ReleaseMaterials()
        {
            foreach (Material burn in burning)
                if (burn != null) Destroy(burn);
            burning.Clear();
        }
    }
}
