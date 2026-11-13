#if UNITY_EDITOR   // a Play DOTS here probe: KnPlayHere is Editor-only (the server build failed on it)
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

                StrikePresentSystem.Trace = true;   // [KnStrike] frame lines for the rest of this run
                PlayerMeleeCombat.TraceSlash = true;   // [KnSlash]: the frame of each slash and what the body plays then
                KryticalNova.Player.PlayerAnimancerMotor.TraceCalls = true;   // [KnPlayCall]: each clip the body is told to play (strike 3 hunt)
                int slot = AttackSlot(own, log);
                // 1) the known case: no strike yet, so no fight anywhere, no beat heard here, and a press is never sent
                log("cadence: KNOWN CASE (no strike yet): " + Snapshot(server, client, ghost, body, own));
                int sent0 = Pressed(client);
                Press(own, slot, log);
                yield return new WaitForSeconds(0.6f);
                bool knownOk = Pressed(client) == sent0 && !InFight(own);
                log($"cadence: known case {(knownOk ? "READ AS EXPECTED" : "UNEXPECTED")}: presses sent {Pressed(client) - sent0}, own view in a fight {InFight(own)}");

                // 2) the owner's double-click on the dummy (select, then attack), through the real click path
                log($"cadence: click 1 (select) at frame {Time.frameCount}: " + click(Centre(dummy)));
                yield return new WaitForSeconds(0.3f);
                string second = click(Centre(dummy));
                log($"cadence: click 2 (attack) at frame {Time.frameCount}: " + second);
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
                // item 4 (owner 8 Nov 2026): the test monsters outside Concord fight back and crowd the NGO way; one Prowler takes a click
                if (Environment.GetEnvironmentVariable("KN_PLAYHERE_TEST_MOBS") == "1")
                {
                    IEnumerator mobs = Mobs(server, client, ghost, body, own, log, click, teleport);
                    while (mobs.MoveNext()) yield return mobs.Current;
                }
            }
            finally
            {
                PlayerCadence.LocalPress -= heard;
                PlayerCadence.Logging = false;
            }
        }

        /// <summary>
        /// The test monsters at mob-ne (150, 150), outside Concord: the Tester stands 10 m away; each second the Hostiles' distances to
        /// the body, how many stand within 4 m, the closest two (no stacking) and the body's HP (they hit back); then ONE click on one
        /// Prowler view (it must select it) and the attack click (strikes must land).
        /// </summary>
        // Probe only (owner, 8 Nov 2026): the Tester cannot die in the mobs part, so the click and the slash sheet are never cut short;
        // every blow it takes is still counted. Its server body is topped up whenever it falls under 60 %. Never in the game.
        static CombatEntity s_Life;
        static float s_Taken;
        static int s_Blows;
        static void OnTesterHit(KryticalNova.Core.DamageResult r, CharacterCombatStats by) { if (!r.IsEvaded) { s_Taken += r.DamageDealt; s_Blows++; } }

        static IEnumerator Hold(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                if (s_Life != null && s_Life.IsAlive && s_Life.CurrentHealth < s_Life.MaxHealth * 0.6f) s_Life.ResetHealth();
                yield return null;
            }
        }

        static IEnumerator Mobs(World server, World client, Entity ghost, GameObject body, GameObject own, Action<string> log,
            Func<Vector3, string> click, Action<Vector3> teleport)
        {
            EntityManager em = server.EntityManager;
            s_Life = body.GetComponent<CombatEntity>();
            s_Taken = 0f; s_Blows = 0;
            if (s_Life != null) { s_Life.Damaged -= OnTesterHit; s_Life.Damaged += OnTesterHit; s_Life.ResetHealth(); }
            log($"mobs: the Tester is kept alive for the probe (topped up under 60 %; blows still counted): {(s_Life != null ? "on" : "NO CombatEntity on the server body")}");
            Vector3 spot = new Vector3(150f, 0f, 140f);
            if (UnityEngine.AI.NavMesh.SamplePosition(spot, out UnityEngine.AI.NavMeshHit nh, 20f, UnityEngine.AI.NavMesh.AllAreas)) spot = nh.position;
            teleport(spot);
            // probe 8: the body still had the dummy as its attack target and ran back to it (to x -72) at once: the attack is dropped and
            // the body told to stand where it landed, as a player who clicks the ground there
            if (em.HasComponent<CombatState>(ghost))
            {
                CombatState c = em.GetComponentData<CombatState>(ghost);
                c.TargetGhostId = 0;
                c.Step = 0;
                em.SetComponentData(ghost, c);
            }
            PlayerEntitiesSetup.IssueOrder(em, ghost,
                new Unity.Mathematics.int3(Mathf.RoundToInt(spot.x * 1000f), Mathf.RoundToInt(spot.y * 1000f), Mathf.RoundToInt(spot.z * 1000f)));
            { IEnumerator hold = Hold(1f); while (hold.MoveNext()) yield return hold.Current; }
            int hp0 = Hp(em, ghost), lowest = hp0;
            log($"mobs: at {body.transform.position} (outside Concord), HP {hp0}; " + MobRing(em, ghost));
            log("mobs: bodies: " + HostileBodies(server));
            // probe 13: clicked after the 8 s the Tester was nearly dead and the camera elsewhere ("off screen"): the click comes first now,
            // with where the camera, the own view and the Prowler stand
            {
                GameObject prowler = NearestView(client, body.transform.position);
                if (prowler == null) log("mobs: click FAIL: no Hostile view near the body on the client");
                else
                {
                    Camera cam = Camera.main;
                    log($"mobs: click setup: camera {(cam != null ? cam.transform.position.ToString() : "none")}, own view {own.transform.position}, server body {body.transform.position}, " +
                        $"'{prowler.name}' at {prowler.transform.position}");
                    // probe 17 (real fps): the camera still trailed the teleport and the click went off screen (y -35): wait, up to 5 s, until the
                    // Prowler is inside the picture (the shot needs it there anyway)
                    for (float waited = 0f; waited < 5f && cam != null; waited += 0.2f)
                    {
                        Vector3 sp = cam.WorldToScreenPoint(prowler.transform.position + Vector3.up);
                        if (sp.z > 0f && sp.x > 100f && sp.x < cam.pixelWidth - 100f && sp.y > 100f && sp.y < cam.pixelHeight - 100f) break;
                        IEnumerator wait = Hold(0.2f); while (wait.MoveNext()) yield return wait.Current;
                    }
                    int hits0 = Hits(server);
                    log($"mobs: click 1 on one Prowler at frame {Time.frameCount}: " + click(prowler.transform.position + Vector3.up));
                    { IEnumerator hold = Hold(0.3f); while (hold.MoveNext()) yield return hold.Current; }
                    string picked = Selected(own);
                    log($"mobs: select {(picked == prowler.name ? "PASS" : "FAIL")}: own view selected '{picked}' (clicked '{prowler.name}')");
                    log($"mobs: click 2 (attack) at frame {Time.frameCount}: " + click(prowler.transform.position + Vector3.up));
                    { IEnumerator hold = Hold(3f); while (hold.MoveNext()) yield return hold.Current; }
                    log($"mobs: strike {(Hits(server) > hits0 ? "PASS" : "FAIL")}: strikes landed {Hits(server) - hits0}; {Melee(server, ghost)}");
                }
            }
            for (int s = 1; s <= 8; s++)
            {
                { IEnumerator hold = Hold(1f); while (hold.MoveNext()) yield return hold.Current; }
                lowest = Math.Min(lowest, Hp(em, ghost));
                log($"mobs: t {s} s, HP {Hp(em, ghost)}, blows taken {s_Blows} ({s_Taken:F0} damage), me {body.transform.position}; " + MobRing(em, ghost));
                log($"mobs: t {s} s brains: " + BrainTrace(server));
                log($"mobs: t {s} s stacking: " + Stacking(server, body));
            }
            log("mobs: bodies after 8 s: " + HostileBodies(server));
            log($"mobs: {(s_Blows > 0 ? "PASS" : "FAIL")}: they fought back ({s_Blows} blows, {s_Taken:F0} damage; HP {hp0} -> lowest {lowest}, kept alive)");
            // the owner's "why is the fps still so low" (8 Nov 2026): three 6 s stretches standing still, frames counted in real time
            log($"fps: settings: targetFrameRate {Application.targetFrameRate}, vSyncCount {QualitySettings.vSyncCount}, runInBackground {Application.runInBackground}, " +
                $"focused {Application.isFocused}, screen {Screen.width}x{Screen.height}, GPU '{SystemInfo.graphicsDeviceName}' ({SystemInfo.graphicsDeviceType}), " +
                $"worlds {World.All.Count}, fixed dt {Time.fixedDeltaTime * 1000f:F1} ms, quality '{QualitySettings.names[QualitySettings.GetQualityLevel()]}'");
            for (int w = 0; w < 3; w++)
            {
                bool cap = w >= 1, traces = w >= 2;
                KnPlayHere.CaptureEnabled = cap;
                StrikePresentSystem.Trace = traces; PlayerMeleeCombat.TraceSlash = traces; KryticalNova.Player.PlayerAnimancerMotor.TraceCalls = traces;
                yield return null;
                int f0 = Time.frameCount; float t0 = Time.realtimeSinceStartup, worst = 0f;
                while (Time.realtimeSinceStartup - t0 < 6f) { worst = Mathf.Max(worst, Time.unscaledDeltaTime); { IEnumerator hold = Hold(0.0f); while (hold.MoveNext()) { } } yield return null; }
                float dt = Time.realtimeSinceStartup - t0;
                log($"fps: window {w + 1}: capture {(cap ? "on" : "OFF")}, traces {(traces ? "on" : "OFF")}: {(Time.frameCount - f0) / dt:F1} fps over {dt:F1} s, worst frame {worst * 1000f:F0} ms");
            }
            KnPlayHere.CaptureEnabled = true;
            // window 4 (run 6): the Editor brought to the front if Windows allows it, then 6 s profiled: frame timing (main, render, GPU) and
            // the busiest markers of the player loop, summed per frame, each as milliseconds per frame
            TryFocusEditor(log);
            { float until = Time.realtimeSinceStartup + 1.5f; while (Time.realtimeSinceStartup < until) yield return null; }
            log($"fps: focused now {Application.isFocused}");
            IEnumerator profile = ProfileWindow(log);
            while (profile.MoveNext()) yield return profile.Current;
            // stacking (run 6): the Tester walks to the biggest cluster of monsters and stands there; every second the closest pairs
            IEnumerator stack = StackWindow(server, body, log, teleport);
            while (stack.MoveNext()) yield return stack.Current;
            IEnumerator whatIfs = WhatIfs(server, ghost, body, log, teleport);
            while (whatIfs.MoveNext()) yield return whatIfs.Current;
        }

        /// <summary>
        /// Probe 12 (owner, 8 Nov 2026: several what-ifs in one run): (1) the NavMesh under the Tester and brains #1-#3 (SamplePosition hit and
        /// distance) and whether the straight line to each brain's goal is blocked on it (the move system's own test); (2) the GM Prowlers'
        /// server bodies: NavMeshAgent / CharacterController / Rigidbody / root motion; (3) a second spot outside Concord where the NavMesh
        /// is found, the Tester there and 3 Prowlers 8 m away, their distances each second. (The "forced straight-line move" is read from
        /// the move system's inputs, speed, path status and the NavMesh raycast: the decide step rewrites a brain's goal every tick.)
        /// </summary>
        static IEnumerator WhatIfs(World server, Entity ghost, GameObject body, Action<string> log, Action<Vector3> teleport)
        {
            EntityManager em = server.EntityManager;
            log("whatif nav: Tester " + NavAt(body.transform.position));
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<KryticalNova.Brains.BrainClock>(), ComponentType.ReadOnly<KryticalNova.Brains.BrainMover>(),
                       ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()))
            using (var clocks = q.ToComponentDataArray<KryticalNova.Brains.BrainClock>(Unity.Collections.Allocator.Temp))
            using (var movers = q.ToComponentDataArray<KryticalNova.Brains.BrainMover>(Unity.Collections.Allocator.Temp))
            using (var places = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp))
                for (int i = 0; i < clocks.Length; i++)
                {
                    if (clocks[i].StableId < 1 || clocks[i].StableId > 3) continue;
                    Vector3 at = places[i].Position, dest = movers[i].Dest;
                    bool blocked = UnityEngine.AI.NavMesh.Raycast(at, dest, out UnityEngine.AI.NavMeshHit rh, UnityEngine.AI.NavMesh.AllAreas);
                    log($"whatif nav: brain #{clocks[i].StableId} {NavAt(at)}; straight line to its goal {dest} {(blocked ? $"BLOCKED at {rh.position}" : "clear")} on the NavMesh");
                }

            // (2) the GM Prowlers' server bodies (read by name: Persistence does not reference Hostiles)
            Type net = Type.GetType("KryticalNova.Hostiles.HostileNet, KryticalNova.Hostiles");
            if (net?.GetProperty("Active", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is IList list)
            {
                int shown = 0;
                foreach (object o in list)
                {
                    if (!(o is Component c) || !(net.GetProperty("GmSpawned")?.GetValue(c) is bool gm) || !gm || shown++ >= 2) continue;
                    var agent = c.GetComponent<UnityEngine.AI.NavMeshAgent>();
                    var cc = c.GetComponent<CharacterController>();
                    var rb = c.GetComponent<Rigidbody>();
                    var anim = c.GetComponent<Animator>();
                    log($"whatif body: '{c.name}' at {c.transform.position}: NavMeshAgent {(agent == null ? "none" : $"enabled {agent.enabled}, on mesh {agent.isOnNavMesh}, velocity {agent.velocity}")}, " +
                        $"CharacterController {(cc == null ? "none" : $"enabled {cc.enabled}")}, Rigidbody {(rb == null ? "none" : $"kinematic {rb.isKinematic}, velocity {rb.linearVelocity}")}, " +
                        $"Animator {(anim == null ? "none" : $"enabled {anim.enabled}, root motion {anim.applyRootMotion}")}");
                }
            }

            // (3) a second spot outside Concord (|x| or |z| beyond 110) with NavMesh under it: the first that answers
            Vector3[] candidates = { new Vector3(-150f, 0f, 140f), new Vector3(140f, 0f, -150f), new Vector3(-150f, 0f, -150f), new Vector3(0f, 0f, 180f),
                                     new Vector3(0f, 0f, -180f), new Vector3(180f, 0f, 0f), new Vector3(-180f, 0f, 0f) };
            Vector3 alt = Vector3.zero;
            bool found = false;
            foreach (Vector3 cand in candidates)
                if (UnityEngine.AI.NavMesh.SamplePosition(cand + Vector3.up * 2f, out UnityEngine.AI.NavMeshHit h, 4f, UnityEngine.AI.NavMesh.AllAreas)
                    && UnityEngine.AI.NavMesh.SamplePosition(cand + new Vector3(0f, 2f, 8f), out _, 4f, UnityEngine.AI.NavMesh.AllAreas))
                { alt = h.position; found = true; break; }
            if (!found) { log("whatif spot 2: no candidate had NavMesh under it"); yield break; }
            teleport(alt);
            if (em.HasComponent<CombatState>(ghost))
            {
                CombatState c = em.GetComponentData<CombatState>(ghost);
                c.TargetGhostId = 0;
                c.Step = 0;
                em.SetComponentData(ghost, c);
            }
            PlayerEntitiesSetup.IssueOrder(em, ghost, new Unity.Mathematics.int3(Mathf.RoundToInt(alt.x * 1000f), Mathf.RoundToInt(alt.y * 1000f), Mathf.RoundToInt(alt.z * 1000f)));
            { IEnumerator hold = Hold(0.5f); while (hold.MoveNext()) yield return hold.Current; }
            string answer = KryticalNova.Networking.GmRoster.Host is ulong host && KryticalNova.Networking.PlayerGm.CommandRunner != null
                ? KryticalNova.Networking.PlayerGm.CommandRunner(host, $"gm.hostile.spawn prowler 3 0 inf {alt.x.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)},{(alt.z + 8f).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)}")
                : "no GM command runner";
            log($"whatif spot 2: Tester at {body.transform.position} ({NavAt(alt)}); 3 Prowlers 8 m north: {answer}");
            int hp0 = Hp(em, ghost), lowest = hp0;
            for (int s = 1; s <= 6; s++)
            {
                { IEnumerator hold = Hold(1f); while (hold.MoveNext()) yield return hold.Current; }
                lowest = Math.Min(lowest, Hp(em, ghost));
                log($"whatif spot 2: t {s} s, HP {Hp(em, ghost)}, blows taken {s_Blows} ({s_Taken:F0} damage); " + MobRing(em, ghost));
            }
            log($"whatif spot 2: {(lowest < hp0 ? "PASS" : "FAIL")}: they fought back there (HP {hp0} -> lowest {lowest}); brains: " + BrainTrace(server));
        }

        static string NavAt(Vector3 p)
        {
            if (!UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit h, 5f, UnityEngine.AI.NavMesh.AllAreas)) return $"{p}: NO NavMesh within 5 m";
            return $"{p}: NavMesh at {h.position}, {Vector3.Distance(p, h.position):F2} m away (vertical {h.position.y - p.y:F2})";
        }

        /// <summary>
        /// Probe 5 (8 Nov 2026): the GM spawn said 6 were made, the server held 0 Hostile targets. Where the bodies are: HostileNet.Active
        /// (each one's name, active, server body / view, has the entity brain, place), whether the server world holds the ghost prefab
        /// and brain config singletons the link system needs. Read by name: Persistence does not reference Hostiles.
        /// </summary>
        static string HostileBodies(World server)
        {
            var sb = new System.Text.StringBuilder();
            Type net = Type.GetType("KryticalNova.Hostiles.HostileNet, KryticalNova.Hostiles");
            Type brain = Type.GetType("KryticalNova.Hostiles.Entities.EcsHostileBrain, KryticalNova.Hostiles.Entities");
            if (net?.GetProperty("Active", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is IList list)
            {
                sb.Append($"HostileNet.Active {list.Count}: ");
                foreach (object o in list)
                    if (o is Component c)
                        sb.Append($"[{c.name} on {c.gameObject.activeInHierarchy} serverBody {net.GetProperty("IsServerBody")?.GetValue(c)} view " +
                                  $"{net.GetProperty("IsView")?.GetValue(c)} brain {(brain != null && c.GetComponent(brain) != null)} at {c.transform.position}] ");
            }
            else sb.Append("HostileNet.Active not readable; ");
            foreach (string name in new[] { "KryticalNova.Hostiles.Entities.HostileGhostPrefab", "KryticalNova.Brains.BrainConfig" })
            {
                Type t = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { t = a.GetType(name); if (t != null) break; }
                if (t == null) { sb.Append($"{name}: type not found; "); continue; }
                using (EntityQuery q = server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly(t)))
                    sb.Append($"{t.Name} in {server.Name}: {q.CalculateEntityCount()}; ");
            }
            // the link system itself: there, on, would run, how often it ran, links made and undone, linked now
            Type linkType = Type.GetType("KryticalNova.Hostiles.Entities.HostileBodyLinkServerSystem, KryticalNova.Hostiles.Entities");
            SystemHandle handle = linkType != null ? server.GetExistingSystem(linkType) : default;
            if (linkType == null) sb.Append("link system: type not found; ");
            else if (handle == SystemHandle.Null) sb.Append("link system: NOT in the server world; ");
            else
            {
                object link = server.GetExistingSystemManaged(linkType);
                var sys = (ComponentSystemBase)link;
                object Read(string p) => linkType.GetProperty(p)?.GetValue(link);
                int linkedNow = Read("Linked") is ICollection c2 ? c2.Count : -1;
                sb.Append($"link system: Enabled {sys.Enabled}, would run {sys.ShouldRunSystem()}, updates {Read("Updates")}, links {Read("Links")}, " +
                          $"unlinks {Read("Unlinks")}, linked now {linkedNow}; ");
            }
            // probe 9: the linked brains never moved: how many are switched on (BrainActive is enableable: a plain query counts the on ones)
            Type active = null;
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies()) { active = a.GetType("KryticalNova.Brains.BrainActive"); if (active != null) break; }
            if (active != null)
            {
                using EntityQuery on = server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly(active));
                var types = new Unity.Collections.FixedList32Bytes<ComponentType> { ComponentType.ReadOnly(active) };
                using EntityQuery any = new EntityQueryBuilder(Unity.Collections.Allocator.Temp).WithAll(ref types)
                    .WithOptions(EntityQueryOptions.IgnoreComponentEnabledState).Build(server.EntityManager);
                sb.Append($"brains awake {on.CalculateEntityCount()} of {any.CalculateEntityCount()}; ");
            }
            // what the brains sense of the players this tick (HostileSenseFlagsServerSystem: Neutral on cross-faction ground, Ignored by a GM)
            using (EntityQuery sq = server.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<KryticalNova.Brains.SenseWorld>()))
                if (sq.TryGetSingleton(out KryticalNova.Brains.SenseWorld sense) && sense.Players.IsCreated)
                {
                    sb.Append($"sensed players {sense.Players.Length}:");
                    foreach (KryticalNova.Brains.SensedPlayer p in sense.Players)
                        sb.Append($" [ghost id {p.GhostId} feet {p.Feet} flags {p.Flags}" +
                                  $"{((p.Flags & KryticalNova.Brains.SensedFlags.Alive) != 0 ? " alive" : "")}" +
                                  $"{((p.Flags & KryticalNova.Brains.SensedFlags.Neutral) != 0 ? " NEUTRAL" : "")}" +
                                  $"{((p.Flags & KryticalNova.Brains.SensedFlags.Ignored) != 0 ? " IGNORED" : "")}" +
                                  $"{((p.Flags & KryticalNova.Brains.SensedFlags.Dead) != 0 ? " DEAD" : "")}]");
                    sb.Append("; ");
                }
                else sb.Append("no SenseWorld; ");
            // every CombatTarget, the disabled and prefab ones too, by kind
            using (EntityQuery all = new EntityQueryBuilder(Unity.Collections.Allocator.Temp).WithAll<CombatTarget>()
                       .WithOptions(EntityQueryOptions.IncludeDisabledEntities | EntityQueryOptions.IncludePrefab).Build(server.EntityManager))
            using (var targets = all.ToComponentDataArray<CombatTarget>(Unity.Collections.Allocator.Temp))
            {
                var kinds = new System.Collections.Generic.SortedDictionary<int, int>();
                foreach (CombatTarget ct in targets) kinds[(int)ct.Kind] = kinds.TryGetValue((int)ct.Kind, out int n) ? n + 1 : 1;
                sb.Append($"CombatTargets incl. disabled/prefab {targets.Length} by kind:");
                foreach (var kv in kinds) sb.Append($" {kv.Key}={kv.Value}");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Probe 11 (8 Nov 2026): after the first notice at tick 779 no brain changed again. Per second: the brain tick and the netcode's,
        /// how often the decide system and the brain group ran (their LastSystemVersion), and brains #1-#3 (mode, phase, notice end, phase
        /// end, think tier / next think tick, mover flags, place).
        /// </summary>
        static string BrainTrace(World server)
        {
            var sb = new System.Text.StringBuilder();
            EntityManager em = server.EntityManager;
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<KryticalNova.Brains.BrainTick>()))
                sb.Append($"BrainTick {(q.TryGetSingleton(out KryticalNova.Brains.BrainTick bt) ? bt.Tick.ToString() : "none")}; ");
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<NetworkTime>()))
                sb.Append($"ServerTick {(q.TryGetSingleton(out NetworkTime nt) && nt.ServerTick.IsValid ? nt.ServerTick.TickIndexForValidTick.ToString() : "invalid")}; ");
            SystemHandle decide = server.GetExistingSystem<KryticalNova.Hostiles.Entities.HostileDecideSystem>();
            SystemHandle group = server.GetExistingSystem<KryticalNova.Brains.BrainSimulationGroup>();
            sb.Append($"decide ran v{(decide != SystemHandle.Null ? server.Unmanaged.ResolveSystemStateRef(decide).LastSystemVersion.ToString() : "missing")}, ");
            sb.Append($"brain group v{(group != SystemHandle.Null ? server.Unmanaged.ResolveSystemStateRef(group).LastSystemVersion.ToString() : "missing")}; ");
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<KryticalNova.Hostiles.Entities.HostileBrain>(),
                       ComponentType.ReadOnly<KryticalNova.Brains.BrainClock>(), ComponentType.ReadOnly<KryticalNova.Brains.BrainMover>(),
                       ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()))
            using (var brains = q.ToComponentDataArray<KryticalNova.Hostiles.Entities.HostileBrain>(Unity.Collections.Allocator.Temp))
            using (var clocks = q.ToComponentDataArray<KryticalNova.Brains.BrainClock>(Unity.Collections.Allocator.Temp))
            using (var movers = q.ToComponentDataArray<KryticalNova.Brains.BrainMover>(Unity.Collections.Allocator.Temp))
            using (var places = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp))
            using (var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                var tiers = new int[4];
                for (int i = 0; i < clocks.Length; i++) tiers[Mathf.Min(3, (int)clocks[i].Tier)]++;
                sb.Append($"tiers 0:{tiers[0]} 1:{tiers[1]} 2:{tiers[2]} 3:{tiers[3]}; ");
                for (int i = 0; i < brains.Length; i++)
                {
                    if (clocks[i].StableId < 1 || clocks[i].StableId > 3) continue;
                    var b = brains[i];
                    sb.Append($"#{clocks[i].StableId} mode {b.Mode} phase {b.Phase} noticeUntil {b.NoticeUntilTick} phaseEnd {b.PhaseEndTick} " +
                              $"tier {clocks[i].Tier} nextThink {clocks[i].NextThinkTick} mover flags {movers[i].Flags} speed {movers[i].Speed:F2} " +
                              $"dest {movers[i].Dest} at {places[i].Position}");
                    // probe 12: Engage, Run and a goal 7 m away, yet no step: its speeds, its path and the NavMesh under it
                    Entity e = ents[i];
                    if (em.HasComponent<KryticalNova.Hostiles.Entities.HostileLife>(e))
                    {
                        var life = em.GetComponentData<KryticalNova.Hostiles.Entities.HostileLife>(e);
                        sb.Append($" life walk {life.Walk:F2} run {life.Run:F2}");
                    }
                    if (em.HasComponent<KryticalNova.Brains.BrainPath>(e))
                        sb.Append($" path {em.GetComponentData<KryticalNova.Brains.BrainPath>(e).Status}");
                    else sb.Append(" NO BrainPath");
                    sb.Append(em.HasBuffer<KryticalNova.Brains.BrainWaypoint>(e) ? $" waypoints {em.GetBuffer<KryticalNova.Brains.BrainWaypoint>(e).Length}" : " NO waypoints");
                    sb.Append($" active {em.IsComponentEnabled<KryticalNova.Brains.BrainActive>(e)}; ");
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Stacking probe (owner, 8 Nov 2026: four Bonewalkers inside each other on the player): per Hostile within 12 m of the body, the species radius,
        /// its slot (Placed/Held), the brain's goal and flags, the simulated position and the drawn body's, the closest pair of simulated and of drawn.
        /// </summary>
        static string Stacking(World server, GameObject body)
        {
            var sb = new System.Text.StringBuilder();
            EntityManager em = server.EntityManager;
            Vector3 me = body.transform.position;
            var views = new System.Collections.Generic.List<Component>();
            Type net = Type.GetType("KryticalNova.Hostiles.HostileNet, KryticalNova.Hostiles");
            if (net?.GetProperty("Active", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is IList list)
                foreach (object o in list)
                    if (o is Component c && net.GetProperty("IsView")?.GetValue(c) is bool v && v) views.Add(c);
            var sim = new System.Collections.Generic.List<Vector3>();
            var drawn = new System.Collections.Generic.List<Vector3>();
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<KryticalNova.Hostiles.Entities.HostileLife>(), ComponentType.ReadOnly<KryticalNova.Brains.BrainMover>(),
                       ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()))
            using (var lives = q.ToComponentDataArray<KryticalNova.Hostiles.Entities.HostileLife>(Unity.Collections.Allocator.Temp))
            using (var movers = q.ToComponentDataArray<KryticalNova.Brains.BrainMover>(Unity.Collections.Allocator.Temp))
            using (var places = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp))
            using (var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp))
            {
                sb.Append($"{ents.Length} hostiles with life+mover; ");
                for (int i = 0; i < ents.Length; i++)
                {
                    Vector3 at = places[i].Position;
                    if (Vector3.Distance(at, me) > 12f) continue;
                    string slot = em.HasComponent<KryticalNova.Hostiles.Entities.HostileSlot>(ents[i])
                        ? $"slot placed {em.GetComponentData<KryticalNova.Hostiles.Entities.HostileSlot>(ents[i]).Placed} held {em.GetComponentData<KryticalNova.Hostiles.Entities.HostileSlot>(ents[i]).Held}" : "NO HostileSlot";
                    Component view = null; float best = 3f;
                    foreach (Component c in views) { float d = Vector3.Distance(c.transform.position, at); if (d < best) { best = d; view = c; } }
                    sim.Add(at);
                    if (view != null) drawn.Add(view.transform.position);
                    sb.Append($"[species {lives[i].Species} radius {lives[i].RadiusMm} mm, {slot}, {(em.HasComponent<KryticalNova.Brains.BrainTag>(ents[i]) ? "tag" : "NO BrainTag")}, " +
                              $"goal {movers[i].Dest} flags {movers[i].Flags}, sim {at}, drawn {(view != null ? view.transform.position.ToString() : "none within 3 m")}, " +
                              $"{Vector3.Distance(at, me):F2} m from me] ");
                }
            }
            float Closest(System.Collections.Generic.List<Vector3> pts)
            {
                float m = float.MaxValue;
                for (int i = 0; i < pts.Count; i++) for (int j = i + 1; j < pts.Count; j++) m = Mathf.Min(m, Vector3.Distance(pts[i], pts[j]));
                return m == float.MaxValue ? -1f : m;
            }
            sb.Append($"| near me {sim.Count}, closest pair simulated {Closest(sim):F2} m, drawn {Closest(drawn):F2} m");
            return sb.ToString();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);

        static void TryFocusEditor(Action<string> log)
        {
            try
            {
                var p = System.Diagnostics.Process.GetCurrentProcess();
                bool ok = SetForegroundWindow(p.MainWindowHandle);
                log($"fps: asked Windows to bring the Editor to the front: {ok} (focused before {Application.isFocused})");
            }
            catch (Exception e) { log("fps: could not focus the Editor: " + e.Message); }
        }

        static IEnumerator ProfileWindow(Action<string> log)
        {
            var recorders = new System.Collections.Generic.List<(string name, Unity.Profiling.ProfilerRecorder rec)>();
            var handles = new System.Collections.Generic.List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var h in handles)
            {
                var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != Unity.Profiling.ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                recorders.Add((d.Name, Unity.Profiling.ProfilerRecorder.StartNew(d.Category, d.Name, 1, Unity.Profiling.ProfilerRecorderOptions.Default | Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame)));
            }
            var sums = new double[recorders.Count];
            var timings = new FrameTiming[1];
            double cpu = 0, main = 0, render = 0, gpu = 0; int tn = 0, frames = 0;
            float t0 = Time.realtimeSinceStartup;
            yield return null;
            while (Time.realtimeSinceStartup - t0 < 6f)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                { cpu += timings[0].cpuFrameTime; main += timings[0].cpuMainThreadFrameTime; render += timings[0].cpuRenderThreadFrameTime; gpu += timings[0].gpuFrameTime; tn++; }
                for (int i = 0; i < recorders.Count; i++) if (recorders[i].rec.Valid) sums[i] += recorders[i].rec.LastValue;
                frames++;
                yield return null;
            }
            float dt = Time.realtimeSinceStartup - t0;
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < recorders.Count; i++) order.Add(i);
            order.Sort((x, y) => sums[y].CompareTo(sums[x]));
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < Math.Min(30, order.Count); k++) sb.Append($"{recorders[order[k]].name} {sums[order[k]] / frames / 1e6:F2} ms; ");
            foreach (var r in recorders) r.rec.Dispose();
            log($"fps: window 4 (profiled, focused {Application.isFocused}): {frames / dt:F1} fps; frame timing over {tn} frames: cpu {(tn > 0 ? cpu / tn : 0):F1} ms, main thread {(tn > 0 ? main / tn : 0):F1} ms, render thread {(tn > 0 ? render / tn : 0):F1} ms, GPU {(tn > 0 ? gpu / tn : 0):F1} ms");
            log("fps: busiest markers per frame (nested ones overlap): " + sb);
        }

        static IEnumerator StackWindow(World server, GameObject body, Action<string> log, Action<Vector3> teleport)
        {
            EntityManager em = server.EntityManager;
            // the monster with the most others within 8 m, and where those stand on average
            Vector3 best = Vector3.zero; int bestN = -1;
            using (EntityQuery q = em.CreateEntityQuery(ComponentType.ReadOnly<KryticalNova.Hostiles.Entities.HostileLife>(), ComponentType.ReadOnly<Unity.Transforms.LocalTransform>()))
            using (var places = q.ToComponentDataArray<Unity.Transforms.LocalTransform>(Unity.Collections.Allocator.Temp))
            {
                for (int i = 0; i < places.Length; i++)
                {
                    Vector3 c = Vector3.zero; int n = 0;
                    for (int j = 0; j < places.Length; j++) if (Vector3.Distance(places[i].Position, places[j].Position) < 8f) { c += (Vector3)places[j].Position; n++; }
                    if (n > bestN) { bestN = n; best = c / n; }
                }
            }
            Vector3 spot = best + new Vector3(7f, 0f, 0f);   // 7 m from the middle of the cluster: inside aggro, outside the pile
            if (UnityEngine.AI.NavMesh.SamplePosition(spot, out UnityEngine.AI.NavMeshHit nh, 10f, UnityEngine.AI.NavMesh.AllAreas)) spot = nh.position;
            log($"stack: cluster of {bestN} monsters around {best}; the Tester stands at {spot}");
            teleport(spot);
            for (int s = 1; s <= 10; s++)
            {
                { IEnumerator hold = Hold(1f); while (hold.MoveNext()) yield return hold.Current; }
                log($"stack: t {s} s: " + Stacking(server, body));
            }
        }

        static int Hp(EntityManager em, Entity ghost) => em.HasComponent<PlayerVitals>(ghost) ? em.GetComponentData<PlayerVitals>(ghost).HealthMilli / 1000 : -1;

        static string MobRing(EntityManager em, Entity ghost)
        {
            Vector3 me = (Vector3)(Unity.Mathematics.float3)em.GetComponentData<PlayerPose>(ghost).Mm * 0.001f;
            var mobs = new System.Collections.Generic.List<Vector3>();
            using (var q = em.CreateEntityQuery(typeof(CombatTarget)))
            using (var all = q.ToComponentDataArray<CombatTarget>(Unity.Collections.Allocator.Temp))
                foreach (CombatTarget c in all)
                    if (c.Kind == CombatTargetKinds.Hostile) mobs.Add((Vector3)(Unity.Mathematics.float3)c.FeetMm * 0.001f);
            float closestPair = float.MaxValue;
            int near = 0;
            var d = new System.Text.StringBuilder();
            for (int i = 0; i < mobs.Count; i++)
            {
                float m = Vector3.Distance(Flat(mobs[i]), Flat(me));
                if (m <= 4f) near++;
                d.Append(m.ToString("F1")).Append(' ');
                for (int j = i + 1; j < mobs.Count; j++) closestPair = Mathf.Min(closestPair, Vector3.Distance(Flat(mobs[i]), Flat(mobs[j])));
            }
            return $"{mobs.Count} Hostiles, {near} within 4 m, closest two {(mobs.Count > 1 ? closestPair.ToString("F2") : "-")} m apart; distances {d}";
        }

        static GameObject NearestView(World client, Vector3 from)
        {
            if (client == null) return null;
            EntityManager em = client.EntityManager;
            GameObject best = null;
            float bestD = 40f;
            foreach (CombatTargetEntry e in CombatTargetRegistry.Entries)
            {
                if (!ReferenceEquals(e.World, client) || e.Body == null || !em.Exists(e.Ghost) || !em.HasComponent<CombatTarget>(e.Ghost)) continue;
                if (em.GetComponentData<CombatTarget>(e.Ghost).Kind != CombatTargetKinds.Hostile) continue;
                float dist = Vector3.Distance(e.Body.transform.position, from);
                if (dist < bestD) { best = e.Body; bestD = dist; }
            }
            return best;
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
            log($"cadence: skill press on slot {slot} at frame {Time.frameCount}");   // stray hit: which VFX is whose
        }

        /// <summary>The server's melee state of the body (CombatState: the target ghost the attack is on, the step, the weapon).</summary>
        static string Melee(World server, Entity ghost)
        {
            var em = server.EntityManager;
            if (!em.Exists(ghost) || !em.HasComponent<CombatState>(ghost)) return "server melee state: none";
            CombatState c = em.GetComponentData<CombatState>(ghost);
            string where = "";
            if (em.HasComponent<MeleeTimingData>(ghost) && em.HasComponent<PlayerPose>(ghost))
            {
                MeleeTiming t = em.GetComponentData<MeleeTimingData>(ghost).Value;
                var me = em.GetComponentData<PlayerPose>(ghost).Mm;
                string them = "target ghost not found on the server";
                using (var q = em.CreateEntityQuery(typeof(GhostInstance)))
                using (var all = q.ToEntityArray(Unity.Collections.Allocator.Temp))
                    foreach (Entity e in all)
                    {
                        if (em.GetComponentData<GhostInstance>(e).ghostId != c.TargetGhostId) continue;
                        bool isTarget = em.HasComponent<CombatTarget>(e), isPlayer = em.HasComponent<PlayerPose>(e);
                        var feet = isTarget ? em.GetComponentData<CombatTarget>(e).FeetMm : isPlayer ? em.GetComponentData<PlayerPose>(e).Mm : default;
                        string kind = isTarget ? $"CombatTarget kind {em.GetComponentData<CombatTarget>(e).Kind}" : isPlayer ? "player" : "other";
                        them = $"target {kind} at {feet} mm, in striking range {MeleeRules.InStrikingRange(t, me, feet)}";
                        break;
                    }
                var rs = server.GetExistingSystemManaged<MeleeResolveServerSystem>();
                where = $"; reach {t.ReachMm} mm, me {me} mm, {them}; resolve: hits {rs?.Hits}, refused {rs?.Refused}, unresolved {rs?.Unresolved}, " +
                        $"missed {rs?.Missed}, not applied {rs?.NotApplied}; last: {rs?.LastVerdict}";
            }
            return $"server melee state: target ghost {c.TargetGhostId}, step {c.Step}, weapon drawn {c.WeaponDrawn}{where}";
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
            if (GhostFlag(server, body) is "0" or "no flag") return "the server ghost's CombatFlag is not set";
            if (GhostFlag(client, own) is "0" or "no flag") return "the client ghost's CombatFlag is not set (replication)";
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
#endif
