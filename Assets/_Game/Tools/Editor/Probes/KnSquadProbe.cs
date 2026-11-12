using System;
using System.Collections;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Persistence;
using KryticalNova.Persistence.Entities;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using UnityEngine;
using UnityEngine.AI;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The port waves' Squad proof with two players (12 Nov 2026; owner's 8 Nov hand test: no Squad dots on the minimap, Follow does not copy
    /// the leader's jump; wave 3: does a Squad's kill pay both members on the entity server?). Runs in Play DOTS here with Player2 as the
    /// windowless bot (env KN_PLAYHERE_TWOBOT=1, KN_PLAYHERE_TWO=1) when KN_PLAYHERE_SQUAD=1: <see cref="KnTwoPlayerProbe"/> hands over to it.
    /// 1) the Tester invites Player2, Player2 accepts: the Squad id on both server bodies, on the own view and on the Tester's view of Player2,
    ///    and the minimap's own rule for a dot (<c>MinimapController.RefreshMarkers</c>) worked out from those views;
    /// 2) Player2 follows the Tester, who jumps twice through the real click: the server's jump plans made and the [KnFollow] lines say
    ///    whether the jump was copied;
    /// 3) a Hostile at the Squad's level is killed by the GM line: both members' XP before and after (the "same map" rule compares scenes).
    /// Test database or memory saves only; Editor only.
    /// </summary>
    public static class KnSquadProbe
    {
        public static bool On => Environment.GetEnvironmentVariable("KN_PLAYHERE_SQUAD") == "1";

        static readonly Vector3 Spot = new Vector3(150f, 0f, 146f);   // the combat probe's spot outside Concord
        const int Level = 40;

        static Vector3 Ground(Vector3 at)
        {
            if (NavMesh.SamplePosition(at, out NavMeshHit hit, 20f, NavMesh.AllAreas)) return hit.position;
            return at;
        }

        static string Alive(GameObject body) => body.TryGetComponent(out CombatEntity e) ? e.IsAlive.ToString() : "?";

        // the server body's own follow target (private: the public state is the owner's view's)
        static string FollowTarget(GameObject body)
        {
            var follow = body.GetComponent<PlayerFollow>();
            var target = follow != null ? typeof(PlayerFollow).GetField("target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(follow) as PlayerParty : null;
            return target != null ? target.MemberName : "nobody";
        }

        public static IEnumerator Run(KnPlayHere.TwoPlayerRun r, GameObject p2)
        {
            bool squadOk = false, dotsOk = false, followOk = false, killOk = false;
            try
            {
                var a = r.Body.GetComponent<PlayerParty>();
                var b = p2.GetComponent<PlayerParty>();
                if (a == null || b == null) { r.Log("squad: FAIL; a server body has no PlayerParty"); yield break; }
                string nameA = a.MemberName, nameB = b.MemberName;
                // run 1 (12 Nov): at the combat spot both players had no HP by the first jump (the zone's Hostiles), which ends a Follow;
                // the Squad and the Follow are proven where the Tester starts (the town), the kill at the combat spot afterwards
                Vector3 at = Ground(r.Body.transform.position);
                r.Put(r.Body, at);
                r.Put(p2, at + new Vector3(2f, 0f, 0f));
                yield return new WaitForSeconds(2.5f);

                // 1) the Squad
                r.Body.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.InviteByName, nameB, false);
                yield return new WaitForSeconds(1.5f);
                p2.GetComponent<IBodyParty>().RunPartyOp(PartyOpKind.Respond, "", true);
                yield return new WaitForSeconds(3f);
                squadOk = a.PartyId != 0UL && a.PartyId == b.PartyId;
                r.Log($"squad: server bodies: {nameA} id {a.PartyId}, {nameB} id {b.PartyId}: {(squadOk ? "PASS" : "FAIL")}");

                // the views on the Tester's game: its own, and the one it has of Player2 (what the minimap reads)
                var own = r.Own.GetComponent<PlayerParty>();
                List<GameObject> servers = r.ServerBodies();
                PlayerParty otherView = null;
                int views = 0;
                foreach (IPlayerBody body in PlayerBodies.All)
                {
                    Transform t = body?.Transform;
                    if (t == null || t == r.Own.transform || servers.Contains(t.gameObject)) continue;
                    views++;
                    bool party = t.TryGetComponent(out PlayerParty seen);
                    r.Log($"squad: a view of another player: '{t.name}', PlayerParty {party}" + (party ? $", name '{seen.MemberName}', id {seen.PartyId}, enabled {seen.isActiveAndEnabled}" : ""));
                    if (party && otherView == null) otherView = seen;
                }
                bool ownIn = own != null && own.InParty;
                bool sees = ownIn && otherView != null && own.IsInPartyWith(otherView);
                dotsOk = sees;
                r.Log($"squad: minimap rule: own view PlayerParty {own != null}, id {(own != null ? own.PartyId : 0UL)}, in a Squad {ownIn}; views of others {views}; " +
                      $"Player2's view id {(otherView != null ? otherView.PartyId : 0UL)}; a dot would be placed: {(dotsOk ? "PASS" : "FAIL")}");

                // 2) Follow and the jump copy
                p2.GetComponent<IBodyFollow>().RunFollow(nameA);
                yield return new WaitForSeconds(2f);
                var click = r.Own.GetComponent<PointAndClickPlayer>();
                int copied = 0;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 from = r.Body.transform.position;
                    Vector3 to = Ground(from + new Vector3(i == 0 ? 6f : -6f, 0f, 0f));
                    string why = click != null ? click.ViewJumpRefusal(to) : "no PointAndClickPlayer on the own view";
                    int planned0 = r.JumpPlans(), seen0 = r.FollowJumps();
                    r.Log($"squad: jump {i + 1}: before: Tester alive {Alive(r.Body)}, Player2 alive {Alive(p2)}, Player2's server body follows '{FollowTarget(p2)}'");
                    r.Log($"squad: jump {i + 1}: the Tester at {from:F1} clicks a jump to {to:F1}; own rules: {why ?? "allowed"}; Player2 at {p2.transform.position:F1}");
                    if (why == null) BodyClicks.Jump(to);
                    yield return new WaitForSeconds(7f);
                    int planned = r.JumpPlans() - planned0;
                    float apart = Vector3.Distance(p2.transform.position, r.Body.transform.position);
                    r.Log($"squad: jump {i + 1}: jump plans made on the server {planned} (2 = the Tester's and Player2's copy), " +
                          $"jumps reported to followers {r.FollowJumps() - seen0}; Tester at {r.Body.transform.position:F1}, Player2 at {p2.transform.position:F1} ({apart:F1} m apart)");
                    if (planned >= 2) copied++;
                }
                followOk = copied == 2;
                r.Log($"squad: Follow copies the jump: {copied} of 2: {(followOk ? "PASS" : "FAIL")} (the [KnFollow] lines say why)");

                // 3) a Squad's kill pays both
                if (!(GmRoster.Host is ulong host) || PlayerGm.CommandRunner == null) { r.Log("squad: kill: FAIL; no GM host or GM commands"); yield break; }
                string Gm(string line)
                {
                    string answer = PlayerGm.CommandRunner(host, line);
                    r.Log($"squad: '{line}' -> {answer}");
                    return answer ?? string.Empty;
                }
                var pa = r.Body.GetComponent<PlayerCharacterProgression>();
                var pb = p2.GetComponent<PlayerCharacterProgression>();
                pa.SetLevel(Level);
                pb.SetLevel(Level);
                yield return new WaitForSeconds(1f);
                Vector3 here = Ground(Spot);
                r.Put(r.Body, here);
                r.Put(p2, here + new Vector3(2f, 0f, 0f));
                yield return new WaitForSeconds(1.5f);
                int xa0 = pa.Experience, xb0 = pb.Experience, la0 = pa.CurrentLevel, lb0 = pb.CurrentLevel;
                r.Log($"squad: kill: before: {nameA} level {la0} XP {xa0} (scene '{r.Body.scene.name}'), {nameB} level {lb0} XP {xb0} (scene '{p2.scene.name}'); " +
                      $"Squad ids {a.PartyId} / {b.PartyId}");
                Gm($"gm.hostile.spawn colossus 1 {Level} 100 {here.x + 4f:F0},{here.z:F0}");
                yield return new WaitForSeconds(3f);
                Gm("gm.hostile.kill 12");
                yield return new WaitForSeconds(3f);
                bool gotA = pa.Experience != xa0 || pa.CurrentLevel != la0, gotB = pb.Experience != xb0 || pb.CurrentLevel != lb0;
                killOk = gotA && gotB;
                r.Log($"squad: kill: after: {nameA} level {pa.CurrentLevel} XP {xa0} -> {pa.Experience} ({(gotA ? "paid" : "NOT paid")}), " +
                      $"{nameB} level {pb.CurrentLevel} XP {xb0} -> {pb.Experience} ({(gotB ? "paid" : "NOT paid")}): {(killOk ? "PASS" : "FAIL")}");
                Gm("gm.hostile.clear 40");
            }
            finally
            {
                r.Log($"squad: Squad {(squadOk ? "PASS" : "FAIL")}, minimap dot rule {(dotsOk ? "PASS" : "FAIL")}, Follow jump copy {(followOk ? "PASS" : "FAIL")}, Squad kill pays both {(killOk ? "PASS" : "FAIL")}");
                r.Verdict(squadOk && dotsOk && followOk && killOk);
            }
        }
    }
}
