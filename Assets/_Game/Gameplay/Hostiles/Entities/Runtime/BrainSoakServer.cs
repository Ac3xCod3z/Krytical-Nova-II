#if KN_TEST_LANE
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using KryticalNova.Brains;
using KryticalNova.Networking.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace KryticalNova.Hostiles.Entities
{
    /// <summary>
    /// The synthetic brain soak on a test-lane server (wave 4 stage S3, 12 Nov 2026; only with <c>KN_TEST_LANE</c>): <c>-knbrainsoak &lt;count&gt;
    /// [-knbrainsoakat x,z]</c> spawns <see cref="SoakLayout"/>'s grid through the normal spawn path (<see cref="HostileSpawner.Spawn"/>, the
    /// NavMesh finds the ground; species take turns), once the map and the entity brains are in, then writes a <c>[KnSoak]</c> line with the
    /// mind tick's p50 / p99 / max every <see cref="ReportSeconds"/> s (<see cref="BrainTickSamples"/>). The centre comes from the planned
    /// spot (Krytical Nova ▸ Online ▸ Entities ▸ Soak ▸ 2 Plan Soak Spot writes it for the soak script); without one, <see cref="DefaultCentre"/>.
    /// Every species is aggressive in the game (HostileAssetBuilder), so the layout's half / half applies to the EditMode soak only.
    /// </summary>
    public static class BrainSoakServer
    {
        public const string CountFlag = "-knbrainsoak", AtFlag = "-knbrainsoakat";
        public const float ReportSeconds = 10f, SettleSeconds = 10f;
        public static readonly float2 DefaultCentre = new float2(150f, 150f);

        /// <summary>The count and centre asked for on <paramref name="args"/>; false when there is no <see cref="CountFlag"/> (or it is bad, with <paramref name="error"/>).</summary>
        public static bool TryParse(string[] args, out int count, out float2 centre, out string error)
        {
            count = 0;
            centre = DefaultCentre;
            error = null;
            if (args == null) return false;
            bool asked = false;
            for (int i = 0; i < args.Length; i++)
            {
                string value = i + 1 < args.Length ? args[i + 1] : null;
                if (string.Equals(args[i], CountFlag, StringComparison.OrdinalIgnoreCase))
                {
                    asked = true;
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count < 1 || count > 10000) error = CountFlag + " needs a count from 1 to 10000";
                }
                else if (string.Equals(args[i], AtFlag, StringComparison.OrdinalIgnoreCase))
                {
                    string[] xz = (value ?? string.Empty).Split(',');
                    if (xz.Length != 2 || !float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                        || !float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) error = AtFlag + " wants x,z in metres";
                    else centre = new float2(x, z);
                }
            }
            return asked;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        static void Hook()
        {
            KnWorldHooks.ServerWorldMade -= OnServerWorld;
            if (!TryParse(Environment.GetCommandLineArgs(), out _, out _, out _)) return;
            KnWorldHooks.ServerWorldMade += OnServerWorld;
        }

        static void OnServerWorld(Unity.Entities.World server)
        {
            KnWorldHooks.ServerWorldMade -= OnServerWorld;   // once
            TryParse(Environment.GetCommandLineArgs(), out int count, out float2 centre, out string error);
            if (error != null) { Debug.LogError("[KnSoak] " + error + ": no brain soak."); return; }
            var host = new GameObject("BrainSoakServer");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.AddComponent<Runner>().Begin(count, centre);
        }

        sealed class Runner : MonoBehaviour
        {
            int m_Count;
            float2 m_Centre;

            public void Begin(int count, float2 centre) { m_Count = count; m_Centre = centre; StartCoroutine(Run()); }

            IEnumerator Run()
            {
                // the map loads and the brains install after the server world is made: wait for the species and the authority
                float waited = 0f;
                while ((HostileSpawner.Species().Count == 0 || !HostileSpawner.HasAuthority) && waited < 120f) { waited += Time.unscaledDeltaTime; yield return null; }
                List<HostileNet> species = HostileSpawner.Species();
                if (species.Count == 0) { Debug.LogError("[KnSoak] No Hostile species after 120 s: no brain soak."); yield break; }
                yield return new WaitForSecondsRealtime(SettleSeconds);

                List<SoakLayout.Point> points = SoakLayout.Build(m_Centre, m_Count, SoakLayout.DefaultSpacing, species.Count);
                int made = 0, offNav = 0;
                foreach (SoakLayout.Point p in points)
                {
                    HostileNet h = HostileSpawner.Spawn(species[p.Species], new Vector3(p.Position.x, GroundY(p.Position), p.Position.z), 0f, 0, gmSpawned: true);
                    if (h != null) made++; else offNav++;
                    if ((made + offNav) % 200 == 0) yield return null;   // spread the spawn over frames
                }
                Debug.Log($"[KnSoak] brain soak: {made} of {m_Count} Hostiles at {m_Centre.x:F0},{m_Centre.y:F0} ({offNav} off the NavMesh), {species.Count} species.");
                BrainTickSamples.On = true;
                while (true)
                {
                    yield return new WaitForSecondsRealtime(ReportSeconds);
                    if (BrainTickSamples.TakeSummary(out double p50, out double p99, out double max, out int ticks))
                        Debug.Log($"[KnSoak] {HostileNet.Active.Count} brains, {ticks} ticks: mind tick p50 {p50:F3} ms, p99 {p99:F3} ms, max {max:F3} ms");
                    else Debug.Log("[KnSoak] no brain ticks in the last " + ReportSeconds + " s");
                }
            }

            static float GroundY(float3 p) =>
                Physics.Raycast(new Vector3(p.x, 500f, p.z), Vector3.down, out RaycastHit hit, 1000f) ? hit.point.y : 0f;
        }
    }
}
#endif
