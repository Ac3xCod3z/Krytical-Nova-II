using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// Loads Sidekick parts on demand. The part meshes already sit in Sidekick's Resources folder, so a part is
    /// loaded by path the first time a character or a gear piece needs it and kept for the session; nothing
    /// references all 1,100 meshes up front.
    /// </summary>
    public static class PartLibrary
    {
        private static readonly Dictionary<ushort, SidekickPartRef> Cache = new Dictionary<ushort, SidekickPartRef>(256);

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Cache.Clear();

        /// <summary>Forget loaded parts (after the catalog is re-baked in the Editor).</summary>
        public static void Clear() => Cache.Clear();

        public static bool TryLoad(SidekickPartCatalog catalog, ushort id, out SidekickPartRef part)
        {
            if (Cache.TryGetValue(id, out part)) return part.IsValid;

            part = default;
            if (catalog != null && catalog.TryGet(id, out var entry))
            {
                var model = Resources.Load<GameObject>(entry.resourcePath);
                var smr = model != null ? model.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                if (smr != null)
                {
                    part = SidekickPartRefs.FromRenderer(smr, entry.region);
                    part.usesWrap = entry.usesWrap;
                }
                else Debug.LogWarning($"[PartLibrary] Part '{entry.name}' could not be loaded from Resources/{entry.resourcePath}.");
            }
            Cache[id] = part; // failures are cached too, so a missing part warns once
            return part.IsValid;
        }

        /// <summary>Loads several parts into <paramref name="results"/>, skipping any that fail.</summary>
        public static void LoadAll(SidekickPartCatalog catalog, IReadOnlyList<ushort> ids, List<SidekickPartRef> results)
        {
            results.Clear();
            if (ids == null) return;
            for (int i = 0; i < ids.Count; i++)
                if (TryLoad(catalog, ids[i], out var part)) results.Add(part);
        }
    }
}
