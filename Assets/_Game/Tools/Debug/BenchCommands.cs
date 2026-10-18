using System.Collections;
using System.Collections.Generic;
using KryticalNova.Diagnostics;
using KryticalNova.Hostiles;
using KryticalNova.Player;
using KryticalNova.Progression;
using QFSW.QC;
using Unity.Cinemachine;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// The performance benchmark: the same three stretches of play every time, so one build is measured against
    /// another and one PC against another. A turn of the camera where the character stands (everything round it is
    /// drawn in turn), the fight at the nearest training dummy with every learned skill on rotation (the owner's
    /// test of 10 Oct 2026), and a run towards the Hostile Zones (the world waking ahead of the character). Each
    /// stretch is a section of the performance log, which counts every frame of it: read with
    /// <c>Tools/Editor/Build/ReadPerformanceLog.py</c>. Like every GM command it runs where the game is decided.
    /// <para>It changes nothing that is saved: the character fights as it is, takes no damage and spends no Flux
    /// while the benchmark runs, and is put back where it stood.</para>
    /// <para><c>gm.sweep</c> is the other tool: it stands the character on every walkable spot of a grid over the
    /// map, looks four ways from each, and writes where the frame cost most to draw. The benchmark runs one road;
    /// the North Gate, where three faded wall pieces took the graphics card from 5 ms to 19, was found only because
    /// that road went through it. The sweep is how the next such place is found before a player finds it.</para>
    /// </summary>
    public static class BenchCommands
    {
        public const string TownSection = "bench.town", FightSection = "bench.fight", RunSection = "bench.run";
        private const float SettleSeconds = 3f;         // after a move of the character, before frames are counted
        private const float TurnSeconds = 20f, Turns = 2f;
        private const float FightSeconds = 45f, CastEvery = 0.15f, StandOff = 2.5f;
        private const float RunSeconds = 60f, Arrived = 6f, LegSeconds = 25f;
        private const float SlowMs = 20f;
        public const string SweepSection = "sweep";
        private const float SweepSettleSeconds = 0.35f;   // after each move, for what streams in and wakes
        private const int SweepSettleFrames = 6, SweepFrames = 6, SweepWorst = 40;
        // The costliest views are looked at a second time, for longer: a place that costs is costly both times,
        // while a view that was only caught as something loaded is not.
        private const int SweepAgain = 12, SweepAgainFrames = 30;
        private const float SweepAgainSettleSeconds = 1f;
        private const float SweepReach = 6f;              // how far from a grid point walkable ground may be
        private static readonly float[] SweepYaws = { 0f, 90f, 180f, 270f };

        private static Runner runner;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => runner = null;

        [Command("gm.bench", "Run the performance benchmark, about two and a half minutes: a turn of the camera, the fight at the training " +
                             "dummy with every skill firing, a run towards the Hostile Zones. Start it standing in town, hands off. " +
                             "The frames go to the performance log, a section each. gm.bench stop ends it early.")]
        private static string Bench(string what = "")
        {
            if (what == "stop")
            {
                if (runner == null || !runner.Running) return "No benchmark is running.";
                runner.Stop();
                return "Benchmark stopped.";
            }
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return error;
            if (runner != null && runner.Running) return "The benchmark is already running (gm.bench stop ends it).";
            if (runner == null)
            {
                var host = new GameObject("Benchmark") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(host);
                runner = host.AddComponent<Runner>();
            }
            runner.Begin(player);
            return "Benchmark started: camera turn, dummy fight, run. Leave the keys and the mouse alone until it says it is done."
                + (PerformanceLog.IsRecording ? string.Empty : " The performance log is NOT recording (in the Editor: Krytical Nova > Build > Record Performance Log In Play), so only the short summary will be shown.");
        }

        [Command("gm.sweep", "Find the places that cost most to draw: stands the character on a grid over the whole map (a spot every " +
                             "so many metres, 50 unless said), looks four ways from each, and writes the worst to a sweep_ file beside the " +
                             "performance logs. Several minutes; hands off. gm.bench stop ends it early.")]
        private static string Sweep(float metres = 50f)
        {
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return error;
            if (runner != null && runner.Running) return "The benchmark or a sweep is already running (gm.bench stop ends it).";
            if (runner == null)
            {
                var host = new GameObject("Benchmark") { hideFlags = HideFlags.HideAndDontSave };
                Object.DontDestroyOnLoad(host);
                runner = host.AddComponent<Runner>();
            }
            int spots = runner.BeginSweep(player, Mathf.Clamp(metres, 10f, 200f));
            return spots == 0 ? "No walkable ground was found to sweep."
                : $"Sweep started: {spots} spots, four views each, about {spots * 4 * 0.2f + spots * SweepSettleSeconds:0} seconds. Leave the keys and the mouse alone.";
        }

        private sealed class Runner : MonoBehaviour
        {
            public bool Running { get; private set; }

            private struct View
            {
                public Vector3 At;
                public float Yaw, GpuMs, FrameMs;
                public long SetPass, Batches, Triangles;
                public int Faded;
            }

            private readonly List<Vector3> spots = new List<Vector3>(512);
            private readonly List<View> views = new List<View>(2048);
            private readonly List<View> again = new List<View>(32);
            private readonly FrameTiming[] timing = new FrameTiming[1];

            /// <summary>Lays the grid over the walkable ground and starts walking it. Returns how many spots it has.</summary>
            public int BeginSweep(GameObject target, float metres)
            {
                spots.Clear();
                views.Clear();
                again.Clear();
                // The ground the map has: every terrain's square, walked in rows, each point moved onto the NavMesh.
                foreach (Terrain terrain in Terrain.activeTerrains)
                {
                    Vector3 corner = terrain.transform.position, size = terrain.terrainData.size;
                    for (float z = metres * 0.5f; z < size.z; z += metres)
                        for (float x = metres * 0.5f; x < size.x; x += metres)
                        {
                            var point = new Vector3(corner.x + x, 0f, corner.z + z);
                            point.y = terrain.SampleHeight(point) + corner.y;
                            if (UnityEngine.AI.NavMesh.SamplePosition(point, out UnityEngine.AI.NavMeshHit hit, SweepReach, UnityEngine.AI.NavMesh.AllAreas))
                                spots.Add(hit.position);
                        }
                }
                if (spots.Count == 0) return 0;
                player = target;
                Running = true;
                StartCoroutine(RunSweep());
                return spots.Count;
            }

            private IEnumerator RunSweep()
            {
                summary.Clear();
                startedAt = player.transform.position;
                gm = player.GetComponent<PlayerGmState>();
                if (gm != null)
                {
                    godWas = gm.GodMode;
                    fluxWas = gm.InfiniteChi;
                    gm.SetGodMode(true);
                }
                restore = true;
                var orbit = FindAnyObjectByType<CinemachineOrbitalFollow>();
                float yawWas = orbit != null ? orbit.HorizontalAxis.Value : 0f;
                var setPass = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "SetPass Calls Count");
                var triangles = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Triangles Count");
                var batches = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, "Batches Count");
                Open(SweepSection);
                // One look: the worst frame, the most calls and the mean time of the graphics card over a few frames.
                IEnumerator Look(Vector3 at, float yaw, int frames, List<View> into)
                {
                    if (orbit != null) orbit.HorizontalAxis.Value = orbit.HorizontalAxis.ClampValue(yaw);
                    for (int f = 0; f < SweepSettleFrames; f++) yield return null;
                    var view = new View { At = at, Yaw = yaw };
                    double gpu = 0;
                    int gpuFrames = 0;
                    for (int f = 0; f < frames; f++)
                    {
                        yield return null;
                        FrameTimingManager.CaptureFrameTimings();
                        if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0 && timing[0].gpuFrameTime < 2000)
                        {
                            gpu += timing[0].gpuFrameTime;
                            gpuFrames++;
                        }
                        view.FrameMs = Mathf.Max(view.FrameMs, Time.unscaledDeltaTime * 1000f);
                        view.SetPass = System.Math.Max(view.SetPass, setPass.Valid ? setPass.LastValue : 0);
                        view.Triangles = System.Math.Max(view.Triangles, triangles.Valid ? triangles.LastValue : 0);
                        view.Batches = System.Math.Max(view.Batches, batches.Valid ? batches.LastValue : 0);
                    }
                    view.GpuMs = gpuFrames > 0 ? (float)(gpu / gpuFrames) : -1f;
                    view.Faded = KryticalNova.Rendering.OccluderFader.Faded.Count;
                    into.Add(view);
                }

                for (int s = 0; s < spots.Count; s++)
                {
                    Place(spots[s]);
                    yield return new WaitForSecondsRealtime(SweepSettleSeconds);
                    foreach (float yaw in SweepYaws) yield return Look(spots[s], yaw, SweepFrames, views);
                }

                // The costliest again, by the graphics card and by set-pass calls.
                var costly = new List<View>(views);
                costly.Sort((a, b) => b.GpuMs.CompareTo(a.GpuMs));
                var busiest = new List<View>(views);
                busiest.Sort((a, b) => b.SetPass.CompareTo(a.SetPass));
                var revisit = new List<View>(SweepAgain * 2);
                for (int i = 0; i < SweepAgain && i < costly.Count; i++) revisit.Add(costly[i]);
                for (int i = 0; i < SweepAgain && i < busiest.Count; i++)
                {
                    View candidate = busiest[i];
                    if (!revisit.Exists(v => v.At == candidate.At && v.Yaw == candidate.Yaw)) revisit.Add(candidate);
                }
                foreach (View view in revisit)
                {
                    Place(view.At);
                    yield return new WaitForSecondsRealtime(SweepAgainSettleSeconds);
                    yield return Look(view.At, view.Yaw, SweepAgainFrames, again);
                }
                setPass.Dispose();
                triangles.Dispose();
                batches.Dispose();
                if (orbit != null) orbit.HorizontalAxis.Value = yawWas;
                Close(SweepSection);
                summary.Append(WriteSweep());
                Finish("sweep done");
            }

            // The file: what is usual, the views that cost most (by the graphics card, then by set-pass calls), then every view.
            private string WriteSweep()
            {
                if (views.Count == 0) return "  nothing was measured" + System.Environment.NewLine;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                var byGpu = new List<View>(views);
                byGpu.Sort((a, b) => b.GpuMs.CompareTo(a.GpuMs));
                var byPass = new List<View>(views);
                byPass.Sort((a, b) => b.SetPass.CompareTo(a.SetPass));
                float usualGpu = byGpu[byGpu.Count / 2].GpuMs;
                long usualPass = byPass[byPass.Count / 2].SetPass;

                var text = new System.Text.StringBuilder(views.Count * 64 + 4096);
                text.Append("Krytical Nova II sweep  ").Append(System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv)).Append("  ")
                    .Append(SystemInfo.graphicsDeviceName).Append("  ").Append(Screen.width).Append(" x ").Append(Screen.height)
                    .Append(Application.isEditor ? "  EDITOR" : string.Empty).Append('\n');
                text.Append(spots.Count).Append(" spots, ").Append(views.Count).Append(" views. Usual view: graphics card ")
                    .Append(usualGpu.ToString("0.0", inv)).Append(" ms, ").Append(usualPass).Append(" set-pass calls.\n\n");
                void Row(in View v) => text.Append("  x ").Append(v.At.x.ToString("0", inv).PadLeft(5)).Append("  z ").Append(v.At.z.ToString("0", inv).PadLeft(5))
                    .Append("  looking ").Append(v.Yaw.ToString("0", inv).PadLeft(3)).Append("   gpu ").Append(v.GpuMs.ToString("0.0", inv).PadLeft(5))
                    .Append(" ms   frame ").Append(v.FrameMs.ToString("0.0", inv).PadLeft(5)).Append(" ms   set-pass ").Append(v.SetPass.ToString(inv).PadLeft(5)).Append("   batches ").Append(v.Batches.ToString(inv).PadLeft(5))
                    .Append("   triangles ").Append(v.Triangles.ToString(inv).PadLeft(8)).Append("   faded ").Append(v.Faded).Append('\n');
                if (again.Count > 0)
                {
                    again.Sort((a, b) => b.GpuMs.CompareTo(a.GpuMs));
                    text.Append("THE COSTLIEST, LOOKED AT AGAIN (a second of standing there first, then ").Append(SweepAgainFrames)
                        .Append(" frames: what is still costly here is costly; gm.tpxyz x 1 z goes there)\n");
                    foreach (View v in again) Row(v);
                    text.Append('\n');
                }
                text.Append("COSTLIEST FOR THE GRAPHICS CARD ON THE FIRST PASS (a view can be caught while something loads)\n");
                for (int i = 0; i < byGpu.Count && i < SweepWorst; i++) Row(byGpu[i]);
                text.Append("\nMOST SET-PASS CALLS\n");
                for (int i = 0; i < byPass.Count && i < SweepWorst; i++) Row(byPass[i]);
                text.Append("\nEVERY VIEW\n");
                foreach (View v in views) Row(v);

                string folder = PerformanceLog.IsRecording && !string.IsNullOrEmpty(PerformanceLog.FilePath)
                    ? System.IO.Path.GetDirectoryName(PerformanceLog.FilePath) : PerformanceLogRecorder.Folder();
                string path = System.IO.Path.Combine(folder, "sweep_" + System.DateTime.Now.ToString("yyyy-MM-dd_HHmmss", inv) + ".txt");
                try
                {
                    System.IO.Directory.CreateDirectory(folder);
                    System.IO.File.WriteAllText(path, text.ToString());
                }
                catch (System.Exception e) { return "  the sweep could not be written: " + e.Message + "\n"; }

                View worst = again.Count > 0 ? again[0] : byGpu[0];
                return "  usual view " + usualGpu.ToString("0.0", inv) + " ms of the graphics card and " + usualPass + " set-pass calls; costliest "
                    + worst.GpuMs.ToString("0.0", inv) + " ms at x " + worst.At.x.ToString("0", inv) + " z " + worst.At.z.ToString("0", inv)
                    + " looking " + worst.Yaw.ToString("0", inv) + "; most set-pass " + byPass[0].SetPass + "\n  written to " + path + "\n";
            }

            private GameObject player;
            private PlayerGmState gm;
            private CombatEntity dummy;
            private bool godWas, fluxWas, dummyWas, restore;
            private Vector3 startedAt;
            private readonly List<SkillDefinitionSO> skills = new List<SkillDefinitionSO>(24);
            private readonly List<HostileZone> zones = new List<HostileZone>(16);
            private readonly System.Text.StringBuilder summary = new System.Text.StringBuilder(256);
            // The stretch being counted.
            private int frames, slow;
            private float seconds, slowest;

            public void Begin(GameObject target)
            {
                player = target;
                Running = true;
                StartCoroutine(Run());
            }

            public void Stop()
            {
                StopAllCoroutines();
                Finish("stopped early");
            }

            private void Update()
            {
                if (!Running) return;
                if (player == null) { Stop(); return; }
                float frame = Time.unscaledDeltaTime;
                frames++;
                seconds += frame;
                if (frame > slowest) slowest = frame;
                if (frame * 1000f > SlowMs) slow++;
            }

            private IEnumerator Run()
            {
                summary.Clear();
                startedAt = player.transform.position;
                gm = player.GetComponent<PlayerGmState>();
                if (gm != null)
                {
                    godWas = gm.GodMode;
                    fluxWas = gm.InfiniteChi;
                    gm.SetGodMode(true);
                    gm.SetInfiniteChi(true);
                }
                restore = true;
                PerformanceLog.Mark("bench.begin");
                yield return new WaitForSecondsRealtime(SettleSeconds);

                // ---- a turn of the camera where the character stands
                var orbit = FindAnyObjectByType<CinemachineOrbitalFollow>();
                if (orbit != null)
                {
                    Open(TownSection);
                    float first = orbit.HorizontalAxis.Value;
                    for (float t = 0f; t < TurnSeconds; t += Time.unscaledDeltaTime)
                    {
                        orbit.HorizontalAxis.Value = orbit.HorizontalAxis.ClampValue(first + 360f * Turns * t / TurnSeconds);
                        yield return null;
                    }
                    orbit.HorizontalAxis.Value = first;
                    Close(TownSection);
                }

                // ---- the fight at the training dummy
                dummy = NearestDummy();
                var pointer = player.GetComponent<PointAndClickPlayer>();
                var combat = player.GetComponent<PlayerCombatSkills>();
                if (dummy != null && pointer != null && combat != null)
                {
                    dummyWas = dummy.InfiniteHealth;
                    dummy.SetInfiniteHealth(true);
                    Vector3 away = player.transform.position - dummy.transform.position;
                    away.y = 0f;
                    Place(dummy.transform.position + (away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward) * StandOff);
                    yield return new WaitForSecondsRealtime(SettleSeconds);

                    skills.Clear();
                    IReadOnlyList<SkillDefinitionSO> known = combat.AvailableSkills;
                    for (int i = 0; i < known.Count; i++)
                        if (known[i] != null && combat.GetRank(known[i]) > 0 && combat.IsInActiveKit(known[i])) skills.Add(known[i]);

                    Open(FightSection);
                    pointer.CommandAttack(dummy);
                    int next = 0;
                    float nextCast = 0f;
                    for (float t = 0f; t < FightSeconds; t += Time.unscaledDeltaTime)
                    {
                        if (skills.Count > 0 && t >= nextCast)
                        {
                            nextCast = t + CastEvery;
                            // The target can be lost (a skill moves the character): it is taken again.
                            if (pointer.SelectedTarget != dummy.transform) pointer.CommandAttack(dummy);
                            combat.TryCastSkill(skills[next++ % skills.Count], out _, quiet: true);
                        }
                        yield return null;
                    }
                    Close(FightSection);
                    dummy.SetInfiniteHealth(dummyWas);
                    dummy = null;
                }
                else summary.Append("  no fight: there is no training dummy in this scene\n");

                // ---- a run towards the Hostile Zones, nearest first
                zones.Clear();
                IReadOnlyList<HostileZone> all = HostileZone.All;
                for (int i = 0; i < all.Count; i++)
                    if (all[i] != null) zones.Add(all[i]);
                if (zones.Count > 0 && pointer != null)
                {
                    Open(RunSection);
                    float left = RunSeconds;
                    Vector3 from = player.transform.position;
                    while (left > 0f && zones.Count > 0)
                    {
                        int nearest = 0;
                        for (int i = 1; i < zones.Count; i++)
                            if ((zones[i].Centre - from).sqrMagnitude < (zones[nearest].Centre - from).sqrMagnitude) nearest = i;
                        Vector3 goal = zones[nearest].Centre;
                        zones.RemoveAt(nearest);
                        pointer.ServerMoveTo(goal);
                        float leg = Mathf.Min(left, LegSeconds), again = 2f;
                        while (leg > 0f && Flat(player.transform.position - goal).sqrMagnitude > Arrived * Arrived)
                        {
                            // An order can be refused for a moment (mid-vault) or run out at something in the way.
                            again -= Time.unscaledDeltaTime;
                            if (again <= 0f) { again = 2f; pointer.ServerMoveTo(goal); }
                            leg -= Time.unscaledDeltaTime;
                            left -= Time.unscaledDeltaTime;
                            yield return null;
                        }
                        from = player.transform.position;
                    }
                    Close(RunSection);
                }
                else summary.Append("  no run: this scene has no Hostile Zones\n");

                Finish("done");
            }

            private void Open(string section)
            {
                PerformanceLog.Section(section);
                frames = slow = 0;
                seconds = slowest = 0f;
            }

            private void Close(string section)
            {
                summary.Append("  ").Append(section.PadRight(12)).Append(frames).Append(" frames in ").Append(seconds.ToString("0")).Append(" s, ")
                    .Append(seconds > 0f ? (frames / seconds).ToString("0") : "-").Append(" a second, slowest ")
                    .Append((slowest * 1000f).ToString("0.0")).Append(" ms, ").Append(slow).Append(" over 20 ms\n");
                PerformanceLog.Section(null);
            }

            // Everything put back as it was found, however the benchmark ended.
            private void Finish(string how)
            {
                Running = false;
                PerformanceLog.Section(null);
                if (!restore) return;
                restore = false;
                if (dummy != null) dummy.SetInfiniteHealth(dummyWas);
                dummy = null;
                if (gm != null)
                {
                    gm.SetGodMode(godWas);
                    gm.SetInfiniteChi(fluxWas);
                }
                if (player != null) Place(startedAt);
                PerformanceLog.Mark("bench.end", how);
                string where = PerformanceLog.IsRecording ? "\nEvery frame of it is in the performance log: " + PerformanceLog.FilePath : string.Empty;
                Debug.Log("[Benchmark] " + how + "\n" + summary + where);
            }

            private void Place(Vector3 position)
            {
                if (UnityEngine.AI.NavMesh.SamplePosition(position, out UnityEngine.AI.NavMeshHit hit, 6f, UnityEngine.AI.NavMesh.AllAreas)) position = hit.position;
                if (player.TryGetComponent(out UnityEngine.AI.NavMeshAgent agent))
                {
                    position += Vector3.up * agent.baseOffset;
                    if (agent.isActiveAndEnabled && agent.isOnNavMesh) agent.ResetPath();
                }
                KryticalNova.Networking.NetworkPlacement.Place(player.transform, position, player.transform.rotation);
            }

            // A dummy is something that can be struck and is neither a player nor a Hostile.
            private CombatEntity NearestDummy()
            {
                CombatEntity best = null;
                float bestSqr = float.MaxValue;
                foreach (CombatEntity entity in FindObjectsByType<CombatEntity>(FindObjectsInactive.Exclude))
                {
                    if (entity.gameObject == player || entity.GetComponent<PlayerResources>() != null || entity.GetComponent<HostileNet>() != null) continue;
                    float sqr = (entity.transform.position - player.transform.position).sqrMagnitude;
                    if (sqr < bestSqr) { bestSqr = sqr; best = entity; }
                }
                return best;
            }

            private static Vector3 Flat(Vector3 offset) => new Vector3(offset.x, 0f, offset.z);
        }
    }
}
