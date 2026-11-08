using System;
using System.Collections;
using System.Reflection;
using KryticalNova.Combat;
using KryticalNova.Combat.Entities;
using KryticalNova.Player.Entities;
using KryticalNova.Player.Entities.Presentation;
using KryticalNova.Progression;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using World = Unity.Entities.World;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// The Cadence probe (env KN_PLAYHERE_CADENCE=1; owner's hand test 7 Nov 2026, item x6: Cadence fails for both clients on the Strike
    /// Dummy). Logs every link of the chain, NGO PlayerCadence.OnSkillKey -> PressRpc -> ServerNotePress as ported: the server body's
    /// fight, the ghost's CombatFlag on both worlds, the own view's mirrored fight, LocalPlayer, the beat clock (HasBeat, Live01: the fight
    /// music on this machine), the press sent, the server's verdict. The known case first: before any strike every "in a fight" link
    /// reads false and a press is never sent; then the dummy is struck by the owner's double-click and the same links are read again.
    /// Diagnostics only (a GUI Play here run): the fight music is switched off in a batch Editor.
    /// </summary>
    static class KnCadenceProbe
    {
        public const string Variable = "KN_PLAYHERE_CADENCE";
        public static bool On => Environment.GetEnvironmentVariable(Variable) == "1";

        static readonly MethodInfo s_PressSlot = typeof(PlayerSkillHotbar).GetMethod("PressSlot", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>Runs the probe; <paramref name="click"/> is the owner's real click at a world point (the runner's PressAt).</summary>
        public static IEnumerator Run(World server, Entity ghost, GameObject body, GameObject own, Action<string> log,
            Func<Vector3, string> click, Action<Vector3> teleport, Action<bool> verdict)
        {
            World client = null;
            foreach (World w in World.All) if (w.IsClient() && !w.IsThinClient()) { client = w; break; }
            CombatEntity dummy = NearestDummy(client, body.transform.position, out string picked);
            if (client == null || dummy == null || own == null) { log($"cadence: FAIL; client world {client != null}, dummy {dummy != null}, own view {own != null}"); verdict(false); yield break; }
            log($"cadence: the dummy '{dummy.name}' at {dummy.transform.position} ({picked}); its ghost id for a click {CombatTargetRegistry.GhostIdOf(dummy.transform)}; server body {body.transform.position}");
            PlayerCadence.Logging = true;
            int localPresses = 0;
            Action<double, bool> heard = (e, on) => { localPresses++; log($"cadence: LocalPress on this machine: {e * 1000.0:+0;-0} ms from the beat, on {on}"); };
            PlayerCadence.LocalPress += heard;
            try
            {
                // stand 6 m short of the dummy along the camera's own heading, so the following camera looks straight at it and the
                // owner's real double-click lands on it (runs 1-2, 7 Nov: from the body's side the dummy was behind the camera, "off screen")
                Camera view = Camera.main;
                Vector3 heading = view != null ? Vector3.ProjectOnPlane(view.transform.forward, Vector3.up).normalized : Vector3.zero;
                if (heading.sqrMagnitude < 0.5f) heading = (dummy.transform.position - body.transform.position).normalized;
                Vector3 near = dummy.transform.position - heading * 6f;
                if (UnityEngine.AI.NavMesh.SamplePosition(near, out UnityEngine.AI.NavMeshHit hit, 4f, UnityEngine.AI.NavMesh.AllAreas)) near = hit.position;
                // walk there with Run, as the owner does (runs 1-3 teleported and no attack after it ever struck): the order a ground click makes
                PlayerEntitiesSetup.IssueOrder(server.EntityManager, ghost,
                    new Unity.Mathematics.int3(Mathf.RoundToInt(near.x * 1000f), Mathf.RoundToInt(near.y * 1000f), Mathf.RoundToInt(near.z * 1000f)), runPaid: true);
                float walked = 0f;
                while (walked < 15f && Vector3.Distance(Flat(body.transform.position), Flat(near)) > 1.5f) { walked += Time.deltaTime; yield return null; }
                bool arrived = Vector3.Distance(Flat(body.transform.position), Flat(near)) <= 1.5f;
                log($"cadence: walked {(arrived ? "there" : "NOT there")} in {walked:F1} s");
                if (!arrived) teleport(near);
                yield return new WaitForSeconds(2f);
                log($"cadence: at {near}: server body {body.transform.position} ({Vector3.Distance(body.transform.position, dummy.transform.position):F1} m from the dummy), own view {own.transform.position} ({Vector3.Distance(own.transform.position, dummy.transform.position):F1} m)");

                int slot = AttackSlot(own, log);
                // 1) the known case: no strike yet, so no fight anywhere, no beat heard here, and a press is never sent
                log("cadence: KNOWN CASE (no strike yet): " + Snapshot(server, client, ghost, body, own));
                int sent0 = Pressed(client);
                Press(own, slot, log);
                yield return new WaitForSeconds(0.6f);
                bool knownOk = Pressed(client) == sent0 && !InFight(own);
                log($"cadence: known case {(knownOk ? "READ AS EXPECTED" : "UNEXPECTED")}: presses sent {Pressed(client) - sent0}, own view in a fight {InFight(own)}");

                // 2) the owner's double-click on the dummy (select, then attack), through the real click path
                log("cadence: click 1 (select): " + click(Centre(dummy)));
                yield return new WaitForSeconds(0.3f);
                string second = click(Centre(dummy));
                log("cadence: click 2 (attack): " + second);
                if (second.StartsWith("off screen") || second.StartsWith("no "))
                {
                    // the camera faces away after the teleport: the second click's own order (NGO StartAttackTarget), no selection
                    KryticalNova.Core.BodyClicks.Attack(dummy.transform);
                    log("cadence: the dummy was off screen: attacked through BodyClicks.Attack instead (the dummy is NOT selected; within 30 m it holds)");
                }
                yield return new WaitForSeconds(0.2f);
                log($"cadence: selected after the double-click: own view '{Selected(own)}', server body '{Selected(body)}'; the click's hostility rule " +
                    $"{(KryticalNova.Player.HostilityCheck.Rule != null ? "set" : "NOT SET")}, the dummy hostile to the own view {KryticalNova.Player.HostilityCheck.IsHostile(own.transform, dummy)}, " +
                    $"to the server body {KryticalNova.Player.HostilityCheck.IsHostile(body.transform, dummy)}; " + Melee(server, ghost));
                int hits0 = Hits(server);
                // x5 (owner 8 Nov 2026): the camera must not swing round during the walk-in and the strikes; its yaw is read every frame
                Camera cam = Camera.main;
                float lastYaw = cam != null ? cam.transform.eulerAngles.y : 0f, worstYawStep = 0f, nextLog = 1f, watched = 0f;
                int worstFrame = 0;
                while (watched < 6f)
                {
                    yield return null;
                    watched += Time.deltaTime;
                    if (cam != null)
                    {
                        float yaw = cam.transform.eulerAngles.y, step = Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw));
                        if (step > worstYawStep) { worstYawStep = step; worstFrame = Time.frameCount; }
                        lastYaw = yaw;
                    }
                    if (watched >= nextLog)
                    {
                        nextLog += 1f;
                        log($"cadence: t {watched:F1} s after the attack click, strikes landed {Hits(server) - hits0}, {Melee(server, ghost)}: " + Snapshot(server, client, ghost, body, own));
                    }
                }
                bool cameraSteady = worstYawStep < 20f;
                log($"camera: {(cameraSteady ? "PASS" : "FAIL")}: the largest yaw step in one frame during the walk-in and the strikes was {worstYawStep:F1} degrees (frame {worstFrame}); [KnCam] lines show any re-point");
                log("gm target: " + GmTargetCheck());
                log("test monsters: " + MobsCheck());

                // 3) presses on the beat as this machine hears it (if it hears one), else plain presses
                for (int p = 0; p < 3; p++)
                {
                    IBeatClock clock = BeatClock.Current;
                    float waited = 0f;
                    while (clock != null && clock.HasBeat && waited < 2f)
                    {
                        if (clock.TryBeatAt(clock.Now - clock.ClockLagSeconds, out _, out double e, out _) && Math.Abs(e) < 0.03) break;
                        waited += Time.unscaledDeltaTime;
                        yield return null;
                    }
                    int sent = Pressed(client), judged = Judged(server);
                    Press(own, slot, log);
                    yield return new WaitForSeconds(0.7f);
                    var judge = body.GetComponent<PlayerCadence>();
                    log($"cadence: press {p + 1}: sent {Pressed(client) - sent}, judged on the server {Judged(server) - judged}, server says '{judge?.LastPress}' " +
                        $"(on {judge?.PressesOn}, off {judge?.PressesOff}, refused {judge?.PressesRefused}, casts on Cadence {judge?.CastsOnCadence}); words shown here {Shown(client)}; " +
                        Snapshot(server, client, ghost, body, own));
                    yield return new WaitForSeconds(1.2f);
                }
                var cadence = body.GetComponent<PlayerCadence>();
                bool chain = cadence != null && cadence.PressesOn + cadence.PressesOff > 0;
                log($"cadence: {(chain ? "PASS" : "FAIL")}: presses judged on the beat or off it on the server {(cadence != null ? cadence.PressesOn + cadence.PressesOff : 0)}, " +
                    $"LocalPress here {localPresses}; first broken link: {FirstBroken(server, client, ghost, body, own)}");
                verdict(chain);
            }
            finally
            {
                PlayerCadence.LocalPress -= heard;
                PlayerCadence.Logging = false;
            }
        }

        /// <summary>The fight music's own line (CombatMusicPlayer.Report, Presentation: read by name, never referenced from here).</summary>
        static string MusicReport()
        {
            Type t = Type.GetType("KryticalNova.Audio.CombatMusicPlayer, KryticalNova.Audio");
            return t?.GetProperty("Report", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as string ?? "(no CombatMusicPlayer)";
        }

        /// <summary>The GM Tools window's player with no pick (GmTarget.TryGetPlayer, Tooling: read by name).</summary>
        static string GmTargetCheck()
        {
            Type t = Type.GetType("KryticalNova.DevTools.GmTarget, KryticalNova.DevTools") ?? Type.GetType("KryticalNova.GmTarget, KryticalNova.DevTools");
            MethodInfo m = t?.GetMethod("TryGetPlayer", BindingFlags.Public | BindingFlags.Static);
            if (m == null) return "FAIL: GmTarget.TryGetPlayer not found";
            object[] args = { null, null };
            bool ok = (bool)m.Invoke(null, args);
            var player = args[0] as GameObject;
            return ok ? $"PASS: with no pick the buttons act on '{player?.name}' ({player?.GetComponent<KryticalNova.Core.BodyRoleTag>()?.Kind})" : "FAIL: " + args[1];
        }

        static string MobsCheck()
        {
            string answers = KnPlayHere.TestMobAnswersNow;
            bool on = Environment.GetEnvironmentVariable("KN_PLAYHERE_TEST_MOBS") == "1";
            if (!on) return "not asked (KN_PLAYHERE_TEST_MOBS off)";
            return answers.Contains("Spawned 5") && answers.Contains("Spawned 1") ? "PASS: " + answers : "FAIL: " + answers;
        }

        static CombatEntity NearestDummy(World client, Vector3 from, out string picked)
        {
            CombatEntity best = null;
            float bestD = float.MaxValue;
            // the dummy the client sees and the owner clicks: a registered attackable ghost body of the client world
            foreach (CombatTargetEntry t in CombatTargetRegistry.Entries)
            {
                if (!ReferenceEquals(t.World, client) || t.Body == null) continue;
                CombatEntity ce = t.Body.GetComponentInChildren<CombatEntity>();
                if (ce == null) continue;
                float d = Vector3.Distance(from, ce.transform.position);
                if (d < bestD) { bestD = d; best = ce; }
            }
            if (best != null) { picked = "a registered client target"; return best; }
            picked = "a scene dummy (no registered client target)";
            foreach (CombatEntity e in UnityEngine.Object.FindObjectsByType<CombatEntity>(FindObjectsSortMode.None))
            {
                if (!StrikeDummies.IsSceneDummy(e)) continue;
                float d = Vector3.Distance(from, e.transform.position);
                if (d < bestD) { bestD = d; best = e; }
            }
            return best;
        }

        static Vector3 Centre(CombatEntity dummy) =>
            dummy.TryGetComponent(out Collider c) ? c.bounds.center : dummy.transform.position + Vector3.up;

        /// <summary>A hotbar slot holding an attack skill that is not charged (one is put on slot 9 when none is).</summary>
        static int AttackSlot(GameObject own, Action<string> log)
        {
            var bar = own.GetComponent<PlayerSkillHotbar>();
            if (bar == null) { log("cadence: the own view has no hotbar"); return -1; }
            for (int i = 0; i < PlayerSkillHotbar.SlotCount; i++)
            {
                HotbarSlotEntry e = bar.GetSlotEntry(i);
                if (e.itemType == HotbarItemType.CombatSkill && e.combatSkill != null && e.combatSkill.IsAttack && !e.combatSkill.IsCharged)
                { log($"cadence: hotbar slot {i} holds the attack '{e.combatSkill.displayName}'"); return i; }
            }
            var skills = own.GetComponent<PlayerCombatSkills>();
            if (skills != null)
                foreach (SkillDefinitionSO s in skills.AvailableSkills)
                    if (s != null && s.IsAttack && !s.IsCharged && bar.AssignSkill(8, s)) { log($"cadence: put the attack '{s.displayName}' on slot 8"); return 8; }
            log("cadence: no attack skill for the hotbar");
            return -1;
        }

        /// <summary>The key press as the hotbar's own key handler makes it (PressSlot: SkillKeyPressed, then the cast).</summary>
        static void Press(GameObject own, int slot, Action<string> log)
        {
            var bar = own.GetComponent<PlayerSkillHotbar>();
            if (bar == null || slot < 0 || s_PressSlot == null) { log($"cadence: cannot press (hotbar {bar != null}, slot {slot}, PressSlot found {s_PressSlot != null})"); return; }
            s_PressSlot.Invoke(bar, new object[] { slot });
        }

        /// <summary>The server's melee state of the body (CombatState: the target ghost the attack is on, the step, the weapon).</summary>
        static string Melee(World server, Entity ghost)
        {
            var em = server.EntityManager;
            if (!em.Exists(ghost) || !em.HasComponent<CombatState>(ghost)) return "server melee state: none";
            CombatState c = em.GetComponentData<CombatState>(ghost);
            return $"server melee state: target ghost {c.TargetGhostId}, step {c.Step}, weapon drawn {c.WeaponDrawn}";
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static string Selected(GameObject b)
        {
            Transform t = b != null && b.TryGetComponent(out KryticalNova.Player.PointAndClickPlayer p) ? p.SelectedTarget : null;
            return t != null ? t.name : "nothing";
        }

        static bool InFight(GameObject b) => b != null && b.TryGetComponent(out PlayerCombatState s) && s.InCombat;

        static int Pressed(World client) => client.GetExistingSystemManaged<CadenceClientSystem>()?.Pressed ?? -1;
        static int Shown(World client) => client.GetExistingSystemManaged<CadenceClientSystem>()?.Shown ?? -1;
        static int Judged(World server) => server.GetExistingSystemManaged<CadenceServerSystem>()?.Judged ?? -1;
        static int Hits(World server) => server.GetExistingSystemManaged<MeleeResolveServerSystem>()?.Hits ?? -1;

        static string GhostFlag(World w, GameObject b)
        {
            foreach (GhostBodyEntry e in GhostBodyRegistry.Entries)
                if (ReferenceEquals(e.World, w) && e.Body == b)
                    return w.EntityManager.Exists(e.Ghost) && w.EntityManager.HasComponent<CombatFlag>(e.Ghost)
                        ? w.EntityManager.GetComponentData<CombatFlag>(e.Ghost).InCombat.ToString() : "no flag";
            return "no entry";
        }

        static string Snapshot(World server, World client, Entity ghost, GameObject body, GameObject own)
        {
            var fight = body.GetComponent<PlayerCombatState>();
            var mine = own.GetComponent<PlayerCombatState>();
            IBeatClock clock = BeatClock.Current;
            string beat = clock == null ? "no beat clock" :
                $"clock {clock.GetType().Name}: HasBeat {clock.HasBeat}, Live01 {clock.Live01:F2}, now {clock.Now:F2}, lag {clock.ClockLagSeconds * 1000.0:F0} ms, callouts only {clock.CalloutsOnly}";
            var bar = own.GetComponent<PlayerSkillHotbar>();
            return $"music '{MusicReport()}'; server body in a fight {fight?.InCombat} ({fight?.Reasons}); ghost flag server {GhostFlag(server, body)}, client {GhostFlag(client, own)}; " +
                   $"own view in a fight {mine?.InCombat}; LocalPlayer is the own view {ReferenceEquals(KryticalNova.Core.LocalPlayer.Object, own)} " +
                   $"('{(KryticalNova.Core.LocalPlayer.Object != null ? KryticalNova.Core.LocalPlayer.Object.name : "none")}'); {beat}; " +
                   $"selections sent {client.GetExistingSystemManaged<CombatFlagClientSystem>()?.SelectionsSent}; hotbar input suppressed {bar?.InputSuppressed}";
        }

        static string FirstBroken(World server, World client, Entity ghost, GameObject body, GameObject own)
        {
            var fight = body.GetComponent<PlayerCombatState>();
            if (fight == null || !fight.InCombat) return "the server body is not in a fight (strike or dummy rule)";
            if (GhostFlag(server, body) != "1") return "the server ghost's CombatFlag is not set";
            if (GhostFlag(client, own) != "1") return "the client ghost's CombatFlag is not set (replication)";
            if (!InFight(own)) return "the own view did not mirror the fight";
            if (!ReferenceEquals(KryticalNova.Core.LocalPlayer.Object, own)) return "LocalPlayer is not the own view (the fight music reads LocalPlayer)";
            IBeatClock clock = BeatClock.Current;
            if (clock == null) return "no beat clock registered";
            if (!clock.HasBeat || clock.Live01 < 0.5f) return $"no beat heard here (HasBeat {clock.HasBeat}, Live01 {clock.Live01:F2}: fight music)";
            var judge = body.GetComponent<PlayerCadence>();
            if (Judged(server) <= 0) return "no press reached the server";
            return judge != null && judge.PressesOn > 0 ? "none (an on-beat press was judged)" : "presses judged, none on the beat";
        }
    }
}
