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
    /// </summary>
    public static class BenchCommands
    {
        public const string TownSection = "bench.town", FightSection = "bench.fight", RunSection = "bench.run";
        private const float SettleSeconds = 3f;         // after a move of the character, before frames are counted
        private const float TurnSeconds = 20f, Turns = 2f;
        private const float FightSeconds = 45f, CastEvery = 0.15f, StandOff = 2.5f;
        private const float RunSeconds = 60f, Arrived = 6f, LegSeconds = 25f;
        private const float SlowMs = 20f;

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

        private sealed class Runner : MonoBehaviour
        {
            public bool Running { get; private set; }

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
