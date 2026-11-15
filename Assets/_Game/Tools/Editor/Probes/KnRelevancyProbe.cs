using System;
using System.Collections;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Persistence.Entities;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The relevancy two-player proof (perf round 3, R3-3b; lead review condition 4 and the Master's 15 Nov ask): in Play DOTS here with
    /// Player2 as the windowless bot (KN_PLAYHERE_TWOBOT=1, KN_PLAYHERE_TWO=1) when KN_PLAYHERE_RELEVANCY=1, <see cref="KnTwoPlayerProbe"/>
    /// hands over to it. Player2's server body walks away from the Tester in 5 m steps and back: the Tester's game must keep its view of
    /// Player2 to 150 m + the 16 m margin, lose it past that, and get it back inside 150 m; a teleport from out of range to 20 m must
    /// show Player2 at once. Test database or memory saves only; Editor only.
    /// </summary>
    public static class KnRelevancyProbe
    {
        public static bool On => Environment.GetEnvironmentVariable("KN_PLAYHERE_RELEVANCY") == "1";

        const float Radius = 150f, Margin = 16f, Step = 5f, StepSeconds = 0.4f;

        static Vector3 Ground(Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out NavMeshHit hit, 30f, NavMesh.AllAreas)) return hit.position;
            return at;
        }

        static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        // the Tester's game: its views of other players (not its own view, not a server body)
        static int OtherViews(KnPlayHere.TwoPlayerRun r)
        {
            List<GameObject> servers = r.ServerBodies();
            int views = 0;
            foreach (IPlayerBody body in PlayerBodies.All)
            {
                Transform t = body?.Transform;
                if (t == null || t == r.Own.transform || servers.Contains(t.gameObject)) continue;
                views++;
            }
            return views;
        }

        public static IEnumerator Run(KnPlayHere.TwoPlayerRun r, GameObject p2)
        {
            bool nearOk = false, keptOk = false, goneOk = false, backOk = false, teleportOk = false;
            float lostAt = -1f, foundAt = -1f, teleportSeconds = -1f;
            try
            {
                Vector3 home = Ground(r.Body.transform.position);
                r.Put(r.Body, home);
                r.Put(p2, Ground(home + new Vector3(2f, 0f, 0f)));
                yield return new WaitForSeconds(3f);
                nearOk = OtherViews(r) == 1;
                r.Log($"relevancy: near (2 m): the Tester's game has {OtherViews(r)} view(s) of others: {(nearOk ? "PASS" : "FAIL")}");

                // walk away along +x (5 m steps), then back
                Vector3 dir = Vector3.right;
                bool keptAll = true;
                for (float d = 120f; d <= 200f; d += Step)
                {
                    r.Put(p2, Ground(home + dir * d));
                    yield return new WaitForSeconds(StepSeconds);
                    float apart = Flat(p2.transform.position, r.Body.transform.position);
                    int views = OtherViews(r);
                    if (views == 0 && lostAt < 0f) lostAt = apart;
                    if (apart <= Radius + Margin - 1f && views == 0) keptAll = false;
                }
                keptOk = keptAll;
                goneOk = lostAt >= Radius + Margin - 1f && OtherViews(r) == 0;
                r.Log($"relevancy: walk away: view kept to the edge {(keptOk ? "PASS" : "FAIL")}; lost at {lostAt:F1} m flat " +
                      $"(expected past {Radius + Margin:0} m, one step later at most): {(goneOk ? "PASS" : "FAIL")}");
                for (float d = 200f; d >= 120f; d -= Step)
                {
                    r.Put(p2, Ground(home + dir * d));
                    yield return new WaitForSeconds(StepSeconds);
                    float apart = Flat(p2.transform.position, r.Body.transform.position);
                    if (OtherViews(r) == 1 && foundAt < 0f) foundAt = apart;
                }
                backOk = foundAt > 0f && foundAt <= Radius + 1f;
                r.Log($"relevancy: walk back: view back at {foundAt:F1} m flat (expected inside {Radius:0} m): {(backOk ? "PASS" : "FAIL")}");

                // a teleport from out of range lands beside the Tester: shown at once (the nudged fill), not at the next timed fill
                r.Put(p2, Ground(home + dir * 220f));
                yield return new WaitForSeconds(2f);
                int before = OtherViews(r);
                float t0 = Time.realtimeSinceStartup;
                r.Put(p2, Ground(home + new Vector3(0f, 0f, 20f)));
                while (OtherViews(r) == 0 && Time.realtimeSinceStartup - t0 < 5f) yield return null;
                teleportSeconds = OtherViews(r) == 1 ? Time.realtimeSinceStartup - t0 : -1f;
                teleportOk = before == 0 && teleportSeconds >= 0f && teleportSeconds < 1f;
                r.Log($"relevancy: teleport in: views before {before}; shown after {teleportSeconds:F2} s (a body spawn included; under 1 s): {(teleportOk ? "PASS" : "FAIL")}");
            }
            finally
            {
                r.Log($"relevancy: near {(nearOk ? "PASS" : "FAIL")}, kept to the edge {(keptOk ? "PASS" : "FAIL")}, gone past it {(goneOk ? "PASS" : "FAIL")}, " +
                      $"back inside {(backOk ? "PASS" : "FAIL")}, teleport shown at once {(teleportOk ? "PASS" : "FAIL")}");
                r.Verdict(nearOk && keptOk && goneOk && backOk && teleportOk);
            }
        }
    }
}
