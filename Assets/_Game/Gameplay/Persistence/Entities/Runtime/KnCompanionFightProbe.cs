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
    /// The Mount Companion's phase 3-4 proof (env KN_PLAYHERE_COMPANION_FIGHT=1 with KN_DOTS_HOSTILES=1; day list item 19): the Tester maxed
    /// (Max Everything, its Epic set worn, its points spent) in god mode outside Concord (Zone01's mob-ne zone, as <see cref="KnCombatProbe"/>), the Brambleback called out, one unkillable
    /// Colossus beside them. DPS: the Tester attacks it as a click does and the companion must strike it too (its strike count rises, its
    /// drawn model stands by the Colossus). HEALER: the Tester's HP is taken to half (god mode off for one hit), the fight goes on, and the
    /// companion must heal (its pulse count rises, the HP climbs) and never strike. TANK (phase 4): its taunts turn the Colossus on it, so its
    /// own HP falls (or it is knocked out), and it never strikes or heals. The "[KnPlayHere] companion fight:" lines say what was seen.
    /// </summary>
    public static class KnCompanionFightProbe
    {
        public const string Variable = "KN_PLAYHERE_COMPANION_FIGHT";
        public static bool On => Environment.GetEnvironmentVariable(Variable) == "1";

        static readonly Vector3 Spot = new Vector3(150f, 0f, 146f);
        // the Colossus at the Tester's own level, unkillable (a same-level Bruiser, as the numbers are made for)
        const string SpawnLine = "gm.hostile.spawn colossus 1 {0} inf 150,151";
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

            // owner rule: test players enter maxed. Max Everything (top level, every mount at 100 %), its own Epic set worn at Temper 60 and
            // every stat point spent in the split the Hostiles' numbers are made for (HostileStatCurve: 50 % Strength, 20 % Vitality and
            // Agility, 10 % Spirit), so the fight's numbers are real play's
            Gm("gm.maxall confirm");
            yield return new WaitForSeconds(1.5f);
            int level = body.TryGetComponent(out PlayerCharacterProgression progression) ? progression.CurrentLevel : 40;
            Gm($"gm.gearset {level} epic 60 wear");
            if (progression != null)
            {
                int points = progression.StatPoints;
                progression.RunAllocate(CharacterAttribute.Strength, points / 2);
                progression.RunAllocate(CharacterAttribute.Vitality, points / 5);
                progression.RunAllocate(CharacterAttribute.Agility, points / 5);
                progression.RunAllocate(CharacterAttribute.Spirit, progression.StatPoints);
                log($"companion fight: level {level}, {points} stat points spent, {progression.StatPoints} left");
            }
            Gm("gm.god true");
            Gm("time.noon");   // daylight for the clip (owner, 14 Nov: the first one was too dark to read)
            yield return new WaitForSeconds(1f);
            Vector3 at = Spot;
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out UnityEngine.AI.NavMeshHit ground, 20f, UnityEngine.AI.NavMesh.AllAreas)) at = ground.position;
            teleport(at);
            yield return new WaitForSeconds(2f);
            log($"companion fight: camera pulled out to {Wider(WideRadius):0.#} m for the clip");

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

            bool spawned = Gm(string.Format(SpawnLine, level)).StartsWith("Spawned", StringComparison.Ordinal);
            yield return new WaitForSeconds(2f);

            // DPS: the Tester fights; the companion joins
            mounts.RunMountOp(MountOpKind.Stance, (int)CompanionStanceByte.Dps);
            yield return new WaitForSeconds(0.5f);
            log($"companion fight: stance now {companion.CompanionStance} (2 = DPS)");
            int strikes0 = Count("StrikesDealt"), pulses0 = Count("PulsesGiven");
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
            int dpsStrikes = Count("StrikesDealt") - strikes0, dpsPulses = Count("PulsesGiven") - pulses0;
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
            strikes0 = Count("StrikesDealt");
            pulses0 = Count("PulsesGiven");
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < StanceSeconds)
            {
                GameObject view = KnCombatProbe.NearestHostileView(own.transform.position, "Colossus");
                if (view != null) BodyClicks.Attack(view.transform);
                yield return new WaitForSeconds(1f);
            }
            int healStrikes = Count("StrikesDealt") - strikes0, healPulses = Count("PulsesGiven") - pulses0;
            bool healer = healPulses >= 3 && healStrikes == 0 && hp.Health > hpLow;
            log($"companion fight: HEALER {(healer ? "PASS" : "FAIL")}; {healPulses} heal pulses, {healStrikes} strikes in {StanceSeconds:0} s; " +
                $"Tester HP {hpLow:0} -> {hp.Health:0} of {hp.MaxHealth:0}");

            // TANK (phase 4): the companion taunts the Colossus so it turns from the Tester to the companion, and its body takes the blows
            // (the Tester stays in god mode); it never strikes and never heals. Counted from before the switch: the first taunt comes at once.
            strikes0 = Count("StrikesDealt");
            pulses0 = Count("PulsesGiven");
            int taunts0 = Count("TauntsWritten"), knockOuts0 = companion.CompanionKnockOuts, hp0 = companion.CompanionHpPermille, hpMin = hp0;
            mounts.RunMountOp(MountOpKind.Stance, (int)CompanionStanceByte.Tank);
            yield return new WaitForSeconds(0.5f);
            log($"companion fight: stance now {companion.CompanionStance} (1 = TANK)");
            start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < StanceSeconds)
            {
                GameObject view = KnCombatProbe.NearestHostileView(own.transform.position, "Colossus");
                if (view != null) BodyClicks.Attack(view.transform);
                if (companion.CompanionOut) hpMin = Mathf.Min(hpMin, companion.CompanionHpPermille);
                yield return new WaitForSeconds(1f);
            }
            int tankTaunts = Count("TauntsWritten") - taunts0, tankStrikes = Count("StrikesDealt") - strikes0, tankPulses = Count("PulsesGiven") - pulses0;
            bool knockedOut = companion.CompanionKnockOuts != knockOuts0;
            bool tank = tankTaunts >= 1 && tankStrikes == 0 && tankPulses == 0 && (hpMin < hp0 || knockedOut);
            log($"companion fight: TANK {(tank ? "PASS" : "FAIL")}; {tankTaunts} taunts, {tankStrikes} strikes, {tankPulses} heal pulses in {StanceSeconds:0} s; " +
                $"companion HP {hp0} -> lowest {hpMin} permille{(knockedOut ? ", knocked out by the Colossus" : string.Empty)}");

            mounts.RunMountOp(MountOpKind.Stance, (int)CompanionStanceByte.Free);
            yield return new WaitForSeconds(0.5f);
            if (companion.CompanionOut) mounts.RunMountOp(MountOpKind.Companion, 0);   // home
            Gm("gm.hostile.clear 40");
            Gm("gm.god false");
            yield return new WaitForSeconds(2f);
            verdict(dps && healer && tank);
        }

        static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        // A wider shot for the clip (owner, 14 Nov: the first was too close to read): the follow camera's orbit radius, as a mouse-wheel zoom
        // out would set it (Cinemachine is not referenced here, so by name). Returns the radius set, 0 when no orbit camera was found.
        const float WideRadius = 14f;

        static float Wider(float radius)
        {
            foreach (Behaviour b in UnityEngine.Object.FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
            {
                if (b == null || b.GetType().Name != "CinemachineOrbitalFollow" || !b.isActiveAndEnabled) continue;
                var p = b.GetType().GetProperty("Radius");
                if (p != null && p.CanWrite) { p.SetValue(b, radius); return radius; }
                var f = b.GetType().GetField("Radius");
                if (f != null) { f.SetValue(b, radius); return radius; }
            }
            return 0f;
        }

        // The companion's server system keeps its own counters (strikes dealt, heal pulses given, taunts written); this assembly does not
        // reference Mounts, so the probe reads them from the server world by name (one player in the probe: the counts are the Tester's).
        const string ServerSystem = "KryticalNova.Mounts.Entities.CompanionServerSystem, KryticalNova.Mounts.Entities";

        static int Count(string property)
        {
            Type type = Type.GetType(ServerSystem);
            if (type == null) return -1;
            foreach (Unity.Entities.World world in Unity.Entities.World.All)
            {
                if ((world.Flags & Unity.Entities.WorldFlags.GameServer) == 0) continue;
                object system = world.GetExistingSystemManaged(type);
                if (system != null && type.GetProperty(property)?.GetValue(system) is int n) return n;
            }
            return -1;
        }

        // CompanionStance lives in Mounts (not referenced here): its byte values
        static class CompanionStanceByte
        {
            public const int Free = 0, Tank = 1, Dps = 2, Healer = 3;
        }
    }
}
#endif
