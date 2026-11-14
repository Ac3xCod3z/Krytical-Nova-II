#if UNITY_EDITOR   // a Play DOTS here probe: KnPlayHere is Editor-only
using System;
using System.Collections;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The Mount Companion's phase 3 proof (env KN_PLAYHERE_COMPANION_FIGHT=1 with KN_DOTS_HOSTILES=1; day list item 19): the Tester at level 40
    /// in god mode outside Concord (Zone01's mob-ne zone, as <see cref="KnCombatProbe"/>), the Brambleback called out, one unkillable
    /// Colossus beside them. DPS: the Tester attacks it as a click does and the companion must strike it too (its strike count rises, its
    /// drawn model stands by the Colossus). HEALER: the Tester's HP is taken to half (god mode off for one hit), the fight goes on, and the
    /// companion must heal (its pulse count rises, the HP climbs) and never strike. The "[KnPlayHere] companion fight:" lines say what was seen.
    /// </summary>
    public static class KnCompanionFightProbe
    {
        public const string Variable = "KN_PLAYHERE_COMPANION_FIGHT";
        public static bool On => Environment.GetEnvironmentVariable(Variable) == "1";

        static readonly Vector3 Spot = new Vector3(150f, 0f, 146f);
        const string SpawnLine = "gm.hostile.spawn colossus 1 40 inf 150,151";
        const float StanceSeconds = 16f;

        public static IEnumerator Run(GameObject body, GameObject own, Action<string> log, Action<Vector3> teleport, Action<bool> verdict)
        {
            if (!(GmRoster.Host is ulong host) || PlayerGm.CommandRunner == null || body == null || own == null
                || !body.TryGetComponent(out IBodyMounts mounts) || !body.TryGetComponent(out IBodyMountCompanion companion)
                || !body.TryGetComponent(out PlayerInventory inventory) || !own.TryGetComponent(out PlayerInventory ownPack)
                || !body.TryGetComponent(out PlayerResources hp))
            {
                log("companion fight: FAIL; no GM host, GM commands, bodies, mounts or Pack");
                verdict(false);
                yield break;
            }
            string Gm(string line)
            {
                string answer = PlayerGm.CommandRunner(host, line);
                log($"companion fight: '{line}' -> {answer}");
                return answer ?? string.Empty;
            }
            Transform Drawn()
            {
                foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                    if (t.parent == null && t.name.StartsWith("Companion (", StringComparison.Ordinal) && t.gameObject.activeInHierarchy) return t;
                return null;
            }

            Gm("gm.level 40");
            Gm("gm.god true");
            yield return new WaitForSeconds(1f);
            Vector3 at = Spot;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out UnityEngine.AI.NavMeshHit ground, 20f, UnityEngine.AI.NavMesh.AllAreas)) at = ground.position;
            teleport(at);
            yield return new WaitForSeconds(2f);

            // the Brambleback (box 1): learned from its item, selected, called out
            var before = new System.Collections.Generic.HashSet<ushort>();
            for (int i = 0; i < PlayerInventory.Capacity; i++) before.Add(inventory.Get(i).itemId);
            Gm("gm.mount.give 1");
            yield return new WaitForSeconds(1.2f);
            for (int i = 0; i < PlayerInventory.Capacity; i++)
                if (!before.Contains(inventory.Get(i).itemId)) { ownPack.UseItem(inventory.Get(i).itemId); break; }
            yield return new WaitForSeconds(1.5f);
            mounts.RunMountOp(MountOpKind.Select, 1);
            yield return new WaitForSeconds(1f);
            if (!companion.CompanionOut) mounts.RunMountOp(MountOpKind.Companion, 0);
            yield return new WaitForSeconds(3f);
            log($"companion fight: out={companion.CompanionOut}, drawn={(Drawn() != null ? Drawn().name : "NONE")}");

            bool spawned = Gm(SpawnLine).StartsWith("Spawned", StringComparison.Ordinal);
            yield return new WaitForSeconds(2f);

            // DPS: the Tester fights; the companion joins
            mounts.RunMountOp(MountOpKind.Stance, (int)CompanionStanceByte.Dps);
            yield return new WaitForSeconds(0.5f);
            log($"companion fight: stance now {companion.CompanionStance} (2 = DPS)");
            int strikes0 = companion.CompanionStrikes, pulses0 = companion.CompanionPulses;
            float nearest = float.MaxValue;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < StanceSeconds)
            {
                GameObject view = KnCombatProbe.NearestHostileView(own.transform.position, "Colossus");
                if (view != null) BodyClicks.Attack(view.transform);
                Transform drawn = Drawn();
                if (view != null && drawn != null) nearest = Mathf.Min(nearest, Flat(drawn.position - view.transform.position));
                yield return new WaitForSeconds(1f);
            }
            int dpsStrikes = companion.CompanionStrikes - strikes0, dpsPulses = companion.CompanionPulses - pulses0;
            bool dps = spawned && dpsStrikes >= 3 && dpsPulses == 0 && nearest < 6f;
            log($"companion fight: DPS {(dps ? "PASS" : "FAIL")}; {dpsStrikes} strikes, {dpsPulses} heal pulses in {StanceSeconds:0} s; " +
                $"drawn companion came within {nearest:F1} m of the Colossus");

            // HEALER: half the Tester's HP gone (god mode off for that one hit), the fight goes on, the companion heals and never strikes
            mounts.RunMountOp(MountOpKind.Stance, (int)CompanionStanceByte.Healer);
            yield return new WaitForSeconds(0.5f);
            log($"companion fight: stance now {companion.CompanionStance} (3 = HEALER)");
            Gm("gm.god false");
            Gm("gm.damage " + Mathf.RoundToInt(hp.MaxHealth * 0.5f));
            Gm("gm.god true");
            float hpLow = hp.Health;
            strikes0 = companion.CompanionStrikes;
            pulses0 = companion.CompanionPulses;
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < StanceSeconds)
            {
                GameObject view = KnCombatProbe.NearestHostileView(own.transform.position, "Colossus");
                if (view != null) BodyClicks.Attack(view.transform);
                yield return new WaitForSeconds(1f);
            }
            int healStrikes = companion.CompanionStrikes - strikes0, healPulses = companion.CompanionPulses - pulses0;
            bool healer = healPulses >= 3 && healStrikes == 0 && hp.Health > hpLow;
            log($"companion fight: HEALER {(healer ? "PASS" : "FAIL")}; {healPulses} heal pulses, {healStrikes} strikes in {StanceSeconds:0} s; " +
                $"Tester HP {hpLow:0} -> {hp.Health:0} of {hp.MaxHealth:0}");

            mounts.RunMountOp(MountOpKind.Stance, (int)CompanionStanceByte.Free);
            yield return new WaitForSeconds(0.5f);
            mounts.RunMountOp(MountOpKind.Companion, 0);   // home
            Gm("gm.hostile.clear 40");
            Gm("gm.god false");
            yield return new WaitForSeconds(2f);
            verdict(dps && healer);
        }

        static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        // CompanionStance lives in Mounts (not referenced here): its byte values
        static class CompanionStanceByte
        {
            public const int Free = 0, Dps = 2, Healer = 3;
        }
    }
}
#endif
