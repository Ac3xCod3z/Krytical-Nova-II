using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace KryticalNova.Items
{
    /// <summary>
    /// The real drop models, shared by every <see cref="WorldDrop"/> on this machine (owner, 14 Nov 2026). A drop takes
    /// the model its item uses and gives it back when it shows something else or goes, so many drops at once make no
    /// new objects once the pool is warm. Rarity is a small tint baked into shared material copies (one per material
    /// and rarity), so the drops stay on the SRP batcher: no property blocks, no per-frame work.
    /// A machine without graphics (a dedicated server) shows no models.
    /// </summary>
    public static class DropModelPool
    {
        /// <summary>One model in use or waiting in the pool.</summary>
        public sealed class Instance
        {
            public GameObject prefab;
            public GameObject gameObject;
            internal Renderer[] renderers;
            internal Material[][] baseMaterials;
            internal int tintKey = -1;
        }

        private static readonly Dictionary<GameObject, Stack<Instance>> Waiting = new Dictionary<GameObject, Stack<Instance>>();
        private static readonly Dictionary<(Material, int), Material> Tinted = new Dictionary<(Material, int), Material>();
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static Transform shelf;

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Waiting.Clear();
            Tinted.Clear();
            shelf = null;
        }

        /// <summary>False where nothing is drawn (a dedicated server): no models are made there.</summary>
        public static bool Shows => SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;

        /// <summary>A model of <paramref name="prefab"/> under <paramref name="parent"/>, from the pool or new.</summary>
        public static Instance Take(GameObject prefab, Transform parent)
        {
            if (prefab == null || parent == null || !Shows) return null;
            Instance instance = null;
            if (Waiting.TryGetValue(prefab, out Stack<Instance> stack))
                while (stack.Count > 0 && instance == null)
                {
                    instance = stack.Pop();
                    if (instance.gameObject == null) instance = null;   // its drop was destroyed while it was out
                }
            if (instance == null)
            {
                GameObject made = Object.Instantiate(prefab, parent, false);
                made.name = prefab.name;
                instance = new Instance { prefab = prefab, gameObject = made, renderers = made.GetComponentsInChildren<Renderer>(true) };
                instance.baseMaterials = new Material[instance.renderers.Length][];
                for (int i = 0; i < instance.renderers.Length; i++) instance.baseMaterials[i] = instance.renderers[i].sharedMaterials;
                SetLayer(made.transform, parent.gameObject.layer);
            }
            else
            {
                instance.gameObject.transform.SetParent(parent, false);
                SetLayer(instance.gameObject.transform, parent.gameObject.layer);
            }
            Transform t = instance.gameObject.transform;
            t.localPosition = prefab.transform.localPosition;
            t.localRotation = prefab.transform.localRotation;
            t.localScale = prefab.transform.localScale;
            instance.gameObject.SetActive(true);
            return instance;
        }

        /// <summary>Back to the pool, hidden.</summary>
        public static void Return(Instance instance)
        {
            if (instance == null || instance.gameObject == null) return;
            instance.gameObject.SetActive(false);
            instance.gameObject.transform.SetParent(Shelf(), false);
            if (!Waiting.TryGetValue(instance.prefab, out Stack<Instance> stack))
                Waiting[instance.prefab] = stack = new Stack<Instance>();
            stack.Push(instance);
        }

        /// <summary>Gives the model its own colours mixed toward <paramref name="tint"/> by <paramref name="amount"/>.</summary>
        public static void Tint(Instance instance, int rarity, Color tint, float amount)
        {
            if (instance == null) return;
            int key = amount <= 0.001f ? -2 : rarity;
            if (instance.tintKey == key) return;
            instance.tintKey = key;
            for (int i = 0; i < instance.renderers.Length; i++)
            {
                Material[] source = instance.baseMaterials[i];
                Material[] shown = new Material[source.Length];
                for (int m = 0; m < source.Length; m++) shown[m] = key == -2 ? source[m] : TintedCopy(source[m], key, tint, amount);
                instance.renderers[i].sharedMaterials = shown;
            }
        }

        private static Material TintedCopy(Material source, int rarity, Color tint, float amount)
        {
            if (source == null || !source.HasProperty(BaseColorId)) return source;
            if (Tinted.TryGetValue((source, rarity), out Material copy) && copy != null) return copy;
            copy = new Material(source) { name = $"{source.name} (rarity {rarity})" };
            Color own = source.GetColor(BaseColorId);
            copy.SetColor(BaseColorId, own * Color.Lerp(Color.white, tint, amount));
            Tinted[(source, rarity)] = copy;
            return copy;
        }

        private static Transform Shelf()
        {
            if (shelf != null) return shelf;
            var go = new GameObject("Drop Models (pool)") { hideFlags = HideFlags.HideInHierarchy };
            go.SetActive(false);
            Object.DontDestroyOnLoad(go);
            shelf = go.transform;
            return shelf;
        }

        private static void SetLayer(Transform root, int layer)
        {
            if (root.gameObject.layer == layer) return;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }
    }
}
