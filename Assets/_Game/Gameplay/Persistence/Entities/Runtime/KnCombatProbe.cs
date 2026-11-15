#if UNITY_EDITOR   // a Play DOTS here probe: KnPlayHere is Editor-only
using System;
using System.Collections;
using KryticalNova.Player.Entities;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using World = Unity.Entities.World;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The cutover's combat proof (env KN_PLAYHERE_COMBAT=1 with KN_DOTS_HOSTILES=1; 12 Nov 2026, owner's D5: "no monsters, gm.hostile.spawn
    /// says No walkable ground there"): the Tester at level 40 with god mode, one killable Colossus spawned through the GM line outside
    /// Concord's neutral ground (Zone01's mob-ne zone, as the test monsters), attacked from the own view the way a click does
    /// (<see cref="KryticalNova.Core.BodyClicks.Attack"/>) until it dies. The [KnPlayHere] "combat:" lines show the spawn answer, the XP
    /// before and after and the drops at the corpse. The drop rate is raised for the run only (gm.droprate 20) so a drop is seen in one kill.
    /// </summary>
    public static class KnCombatProbe
    {
        public const string Variable = "KN_PLAYHERE_COMBAT";
        public static bool On => Environment.GetEnvironmentVariable(Variable) == "1";

        static readonly Vector3 Spot = new Vector3(150f, 0f, 146f);   // gm.tpxyz 150 0 146, KnPlayHere.TestMobLines
        const string SpawnLine = "gm.hostile.spawn colossus 1 40 100 150,152";
        const float FightSeconds = 40f;

        public static IEnumerator Run(GameObject body, GameObject own, Action<string> log, Action<Vector3> teleport, Action<bool> verdict)
        {
            if (!(GmRoster.Host is ulong host) || PlayerGm.CommandRunner == null || body == null || own == null)
            {
                log("combat: FAIL; no GM host, GM commands or bodies");
                verdict(false);
                yield break;
            }
            string Gm(string line)
            {
                string answer = PlayerGm.CommandRunner(host, line);
                log($"combat: '{line}' -> {answer}");
                return answer ?? string.Empty;
            }
            var progression = body.GetComponent<PlayerCharacterProgression>();
            if (progression == null) { log("combat: FAIL; the server body has no progression"); verdict(false); yield break; }

            Gm("gm.level 40");
            Gm("gm.god true");
            Gm("gm.droprate 20");
            yield return new WaitForSeconds(1f);
            Vector3 at = Spot;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out UnityEngine.AI.NavMeshHit ground, 20f, UnityEngine.AI.NavMesh.AllAreas)) at = ground.position;
            teleport(at);
            yield return new WaitForSeconds(2f);

            int xp0 = progression.Experience, level0 = progression.CurrentLevel, drops0 = WorldDrop.Active.Count;
            log($"combat: before the fight: level {level0}, XP {xp0}/{progression.ExperienceRequired}, drops in the world {drops0}, Tester at {body.transform.position}");
            bool spawned = Gm(SpawnLine).StartsWith("Spawned", StringComparison.Ordinal);
            yield return new WaitForSeconds(2f);

            float start = Time.realtimeSinceStartup;
            bool killed = false, attacked = false;
            while (Time.realtimeSinceStartup - start < FightSeconds)
            {
                if (progression.Experience != xp0 || progression.CurrentLevel != level0) { killed = true; break; }
                GameObject view = NearestHostileView(own.transform.position, "Colossus");   // run 1: the zone's level-13 Wraiths were nearer
                if (view != null) { KryticalNova.Core.BodyClicks.Attack(view.transform); attacked = true; }
                log($"combat: t {Time.realtimeSinceStartup - start:0} s: monster view {(view != null ? view.name + " " + Vector3.Distance(view.transform.position, own.transform.position).ToString("0.0") + " m" : "none")}, " +
                    $"XP {progression.Experience}, Tester HP {CurrentHealth(body)}; Colossus " + ColossusHealth(view));
                yield return new WaitForSeconds(2f);
            }
            bool killedByGm = false;
            if (!killed)
            {
                // option A (cutover lead, 12 Nov 2026): the real kill -> reward path, so XP and the drop are seen live even if strikes did not finish it
                Gm("gm.hostile.kill 8");
                yield return new WaitForSeconds(1f);
                killedByGm = progression.Experience != xp0 || progression.CurrentLevel != level0;
                log("combat: after the GM kill: Colossus " + ColossusHealth(NearestHostileView(own.transform.position, "Colossus")));
            }
            yield return new WaitForSeconds(2f);   // the drops fall and reach the views
            int drops = WorldDrop.Active.Count - drops0;
            int gained = progression.CurrentLevel == level0 ? progression.Experience - xp0 : -1;
            log($"combat: after {Time.realtimeSinceStartup - start:0} s: attacked {attacked}, killed by strikes {killed}, by the GM kill {killedByGm}, XP {xp0} -> {progression.Experience} " +
                $"(level {level0} -> {progression.CurrentLevel}{(gained >= 0 ? ", +" + gained : "")}), new drops {drops}");
            foreach (WorldDrop d in WorldDrop.Active)
                if (d != null && Vector3.Distance(d.transform.position, at) < 30f) log($"combat: drop {d.name} at {d.transform.position}");

            Gm("gm.droprate 1");
            Gm("gm.god false");
            Gm("gm.hostile.clear 40");
            bool pass = spawned && (killed || killedByGm) && drops > 0;
            log($"combat: spawn {(spawned ? "PASS" : "FAIL")}, kill by strikes {(killed ? "PASS" : "FAIL")}, kill reward XP {(killed || killedByGm ? "PASS" : "FAIL")}, drop {(drops > 0 ? "PASS" : "FAIL")}");
            verdict(pass);
        }

        // The Colossus' health three ways: the server body's own (HostileNet.Active by reflection: Hostiles is not referenced here), the
        // replicated ghost's CombatTarget (what the bars read), and the client view's CombatEntity (what the hit line reads: "HP 515/515").
        static string ColossusHealth(GameObject view)
        {
            string server = "none";
            Type net = Type.GetType("KryticalNova.Hostiles.HostileNet, KryticalNova.Hostiles");
            if (net?.GetProperty("Active", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null) is IEnumerable all)
                foreach (object o in all)
                    if (o is Component c && c != null && c.name.IndexOf("Colossus", StringComparison.OrdinalIgnoreCase) >= 0
                        && c.TryGetComponent(out CombatEntity ce) && !ReferenceEquals(c.gameObject, view))
                        server = $"{ce.CurrentHealth:0}/{ce.MaxHealth:0}{(ce.InfiniteHealth ? " inf" : "")}";
            string ghost = "none", shown = "none";
            if (view != null)
            {
                foreach (World w in World.All)
                {
                    if (!w.IsClient() || w.IsThinClient()) continue;
                    CombatTargetEntry e = CombatTargetRegistry.Of(w, view);
                    if (e != null && w.EntityManager.Exists(e.Ghost) && w.EntityManager.HasComponent<CombatTarget>(e.Ghost))
                    {
                        CombatTarget t = w.EntityManager.GetComponentData<CombatTarget>(e.Ghost);
                        ghost = $"{t.HealthMilli / 1000}/{t.MaxHealthMilli / 1000}";
                    }
                }
                if (view.TryGetComponent(out CombatEntity v)) shown = $"{v.CurrentHealth:0}/{v.MaxHealth:0}";
            }
            return $"server {server}, ghost {ghost}, view {shown}";
        }

        static string CurrentHealth(GameObject body) =>
            body.TryGetComponent(out CombatEntity combat) ? combat.CurrentHealth.ToString("0") : "?";

        internal static GameObject NearestHostileView(Vector3 from, string named)
        {
            World client = null;
            foreach (World w in World.All) if (w.IsClient() && !w.IsThinClient()) { client = w; break; }
            if (client == null) return null;
            EntityManager em = client.EntityManager;
            GameObject best = null;
            float bestD = 40f;
            foreach (CombatTargetEntry e in CombatTargetRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, client) || e.Body == null || !em.Exists(e.Ghost) || !em.HasComponent<CombatTarget>(e.Ghost)) continue;
                if (em.GetComponentData<CombatTarget>(e.Ghost).Kind != CombatTargetKinds.Hostile || !e.Body.activeInHierarchy) continue;
                if (named != null && e.Body.name.IndexOf(named, StringComparison.OrdinalIgnoreCase) < 0) continue;
                float dist = Vector3.Distance(e.Body.transform.position, from);
                if (dist < bestD) { best = e.Body; bestD = dist; }
            }
            return best;
        }
    }
}
#endif
