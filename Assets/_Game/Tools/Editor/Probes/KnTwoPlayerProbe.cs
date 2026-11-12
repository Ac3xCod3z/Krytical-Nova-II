using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using KryticalNova.Persistence.Entities;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The cutover's two-player proof (12 Nov 2026, owner's D5: Trade did nothing, vendors sold nothing, GM lines went to Player2): Play DOTS
    /// here with Player2 joined from the Multiplayer Play Mode clone (<see cref="KnJoinHere.MppmJoinPref"/>, no sign-in). The own view asks
    /// Player2 to trade through the real client path ([KnTrade] lines); Player2's side is answered on its server body. Then the shelf of every
    /// vendor seen from the own view, one buy, and a GM line's default target with two players in. Tooling: it needs Economy and NPC, which
    /// Persistence.Entities may not use, so it fills <see cref="KnPlayHere.TwoPlayerProbe"/>.
    /// <c>Unity.exe -projectPath ... -executeMethod KryticalNova.EditorTools.KnTwoPlayerProbe.Batch</c> with KN_PLAYHERE_TWO=1.
    /// </summary>
    [InitializeOnLoad]
    public static class KnTwoPlayerProbe
    {
        static KnTwoPlayerProbe() => KnPlayHere.TwoPlayerProbe = Run;

        const float CloneBootSeconds = 300f, PlayerTwoWaitSeconds = 150f;
        static double s_Started;
        static int s_UnityAtStart;

        /// <summary>Batch entry: the clone may join, Player 2 is switched on, and once its Editor is up Play DOTS here starts (it quits by itself).</summary>
        public static void Batch()
        {
            EditorPrefs.SetBool(KnJoinHere.MppmJoinPref, true);
            s_UnityAtStart = UnityProcesses();
            string activated = PlayerTwo("Activate");
            Debug.Log("[KnTwo] MPPM Player 2: " + activated + $"; Unity processes before: {s_UnityAtStart}");
            s_Started = EditorApplication.timeSinceStartup;
            EditorApplication.update += WaitForClone;
        }

        static void WaitForClone()
        {
            double waited = EditorApplication.timeSinceStartup - s_Started;
            bool up = UnityWindows() >= 2;
            if (!up && waited < CloneBootSeconds) return;
            EditorApplication.update -= WaitForClone;
            Debug.Log($"[KnTwo] the clone's Editor window {(up ? "is up" : "is NOT up")} after {waited:0} s; Play DOTS here starts");
            if (up) EditorApplication.delayCall += () => EditorApplication.delayCall += StartPlay;
            else StartPlay();
        }

        static void StartPlay()
        {
            Type t = typeof(KnPlayHere);
            t.GetMethod("Batch", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }

        static int UnityProcesses() => System.Diagnostics.Process.GetProcessesByName("Unity").Length;

        static int UnityWindows()
        {
            int n = 0;
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("Unity"))
                try { if (!string.IsNullOrEmpty(p.MainWindowTitle)) n++; } catch (Exception) { }
            return n;
        }

        /// <summary>MPPM's Player 2 by reflection (UnityEditor.MultiplayerModule, memory mppm-clone-testing 22 Oct): Activate / Deactivate.</summary>
        static string PlayerTwo(string verb)
        {
            Type mp = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode", false))
                .FirstOrDefault(x => x != null);
            if (mp == null) return "no MultiplayerPlaymode type";
            object two = (object)mp.GetProperty("PlayerTwo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null)
                ?? mp.GetField("PlayerTwo", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (two == null) return "no PlayerTwo";
            MethodInfo m = two.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(x => x.Name == verb);
            if (m == null) return "no " + verb;
            object[] args = new object[m.GetParameters().Length];
            object result = m.Invoke(two, args);
            return $"{verb} -> {result} {string.Join(" ", args.Where(a => a != null))}".Trim();
        }

        // ------------------------------------------------------------------ in Play

        /// <summary>The Pack and worn items kept in the store for <paramref name="body"/>'s character equal its live inventory.</summary>
        static bool SavedPackMatches(GameObject body, PlayerInventory inv, Action<string> log, string who)
        {
            var agent = body.GetComponent<PlayerSaveAgent>();
            if (agent == null || string.IsNullOrEmpty(agent.AccountId)) { log($"two: save: {who}: no account bound (not saved)"); return false; }
            AccountSave kept = ServerSave.ReadAccountFrom(ServerSave.Store, agent.AccountId);
            CharacterSave character = kept?.Find(agent.CharacterId);
            string json = character?.Get(inv.SaveKey);
            string live = inv.CaptureSave();
            bool same = json != null && json == live;
            log($"two: save: {who} account {agent.AccountId.Substring(0, Math.Min(8, agent.AccountId.Length))}.. character '{character?.name}' " +
                $"saved {(character != null ? new DateTime(character.savedUtc, DateTimeKind.Utc).ToString("HH:mm:ss") : "-")}: Pack {(json == null ? "NOT FOUND" : same ? "equals the live Pack" : "DIFFERS from the live Pack")}");
            return same;
        }

        static IEnumerator Run(KnPlayHere.TwoPlayerRun r)
        {
            bool tradeOk = false, vendorsOk = false, buyOk = false, gmOk = false, squad = false;
            try
            {
                GameObject p2 = null;
                float start = Time.realtimeSinceStartup, nextLog = 0f;
                while (p2 == null && Time.realtimeSinceStartup - start < PlayerTwoWaitSeconds)
                {
                    foreach (GameObject b in r.ServerBodies()) if (b != null && b != r.Body) p2 = b;
                    if (Time.realtimeSinceStartup - start >= nextLog) { r.Log($"two: waiting for Player2: {r.ServerBodies().Count} server bodies"); nextLog += 10f; }
                    if (p2 == null) yield return new WaitForSeconds(1f);
                }
                if (p2 == null) { r.Log("two: FAIL; Player2 never joined (is the MPPM clone up?)"); yield break; }
                var hostTrade = r.Body.GetComponent<PlayerTrade>();
                var p2Trade = p2.GetComponent<PlayerTrade>();
                r.Log($"two: Player2 joined: '{p2Trade?.TraderName}' ({p2.name}); the Tester '{hostTrade?.TraderName}'");
                // the port waves' Squad proof instead (KN_PLAYHERE_SQUAD=1): Squad ids and the minimap rule, Follow's jump copy, a Squad's kill
                if (KnSquadProbe.On) { squad = true; yield return KnSquadProbe.Run(r, p2); yield break; }
                r.Put(p2, r.Body.transform.position + r.Body.transform.right * 2f);
                yield return new WaitForSeconds(2f);

                // 1) a GM line's default target with two players in: the GM's own body, never Player2
                var hostWallet = r.Body.GetComponent<PlayerWallet>();
                var p2Wallet = p2.GetComponent<PlayerWallet>();
                long h0 = hostWallet.Creds, q0 = p2Wallet.Creds;
                if (PlayerGm.Remote == null) r.Log("two: GM: FAIL; no GM link on the own view");
                else
                {
                    PlayerGm.Remote.Send("gm.creds 7");
                    yield return new WaitForSeconds(2f);
                    gmOk = hostWallet.Creds == h0 + 7 && p2Wallet.Creds == q0;
                    r.Log($"two: GM 'gm.creds 7' with no pick: Tester {h0} -> {hostWallet.Creds}, Player2 {q0} -> {p2Wallet.Creds}: {(gmOk ? "PASS (own body)" : "FAIL")}");
                    PlayerGm.Remote.Send("gm.creds 5000");   // for the buy below
                    yield return new WaitForSeconds(1.5f);
                }

                // 2) Trade: the own view asks through the real client path; Player2's side is answered on its server body
                var ownTrade = r.Own.GetComponent<PlayerTrade>();
                var hostInv = r.Body.GetComponent<PlayerInventory>();
                var p2Inv = p2.GetComponent<PlayerInventory>();
                int cell = -1;
                for (int i = 0; i < PlayerInventory.Capacity && cell < 0; i++) if (!hostInv.Get(i).IsEmpty && hostInv.Get(i).itemId != LootRules.CredsItemId) cell = i;
                if (ownTrade == null || p2Trade == null || cell < 0) r.Log($"two: trade: FAIL; own view trade {ownTrade != null}, Player2 trade {p2Trade != null}, an item in the Pack {cell >= 0}");
                else
                {
                    ItemStack item = hostInv.Get(cell);
                    int hostHad = hostInv.CountItem(item.itemId), p2Had = p2Inv.CountItem(item.itemId);
                    r.Log($"two: trade: the Tester offers cell {cell} (item {item.itemId} x{item.count}); Player2 has {p2Had}");
                    ownTrade.Request(p2Trade.TraderName);
                    yield return new WaitForSeconds(1.5f);
                    r.Log($"two: trade: after the ask: Tester trading {hostTrade.IsTrading}, Player2 trading {p2Trade.IsTrading}");
                    p2Trade.RunTradeOp(TradeOpKind.Respond, null, 0, 0, 0, true);
                    yield return new WaitForSeconds(1.5f);
                    r.Log($"two: trade: after Player2 accepts: Tester with '{hostTrade.PartnerName}', own view with '{ownTrade.PartnerName}', Player2 with '{p2Trade.PartnerName}'");
                    ownTrade.Offer(cell, item.count);
                    yield return new WaitForSeconds(1.5f);
                    ownTrade.SetLocked(true);
                    p2Trade.RunTradeOp(TradeOpKind.Lock, null, 0, 0, 0, true);
                    yield return new WaitForSeconds(1.5f);
                    ownTrade.Confirm();
                    p2Trade.RunTradeOp(TradeOpKind.Confirm, null, 0, 0, 0, false);
                    yield return new WaitForSeconds(2.5f);
                    int hostNow = hostInv.CountItem(item.itemId), p2Now = p2Inv.CountItem(item.itemId);
                    tradeOk = p2Now == p2Had + item.count && hostNow == hostHad - item.count;
                    r.Log($"two: trade: item {item.itemId}: Tester {hostHad} -> {hostNow}, Player2 {p2Had} -> {p2Now}, still trading {hostTrade.IsTrading}: {(tradeOk ? "PASS" : "FAIL")}");

                    // the other way (cutover, 12 Nov 2026): Player2 asks the Tester by name and gives the same item back; the own view accepts
                    int back = -1;
                    for (int i = 0; i < PlayerInventory.Capacity && back < 0; i++) if (p2Inv.Get(i).itemId == item.itemId && !p2Inv.Get(i).IsEmpty) back = i;
                    if (!tradeOk || back < 0) { r.Log($"two: trade back: FAIL; the first trade {(tradeOk ? "passed" : "failed")}, Player2's cell {back}"); tradeOk = false; }
                    else
                    {
                        int backCount = p2Inv.Get(back).count;
                        int hostBefore = hostInv.CountItem(item.itemId), p2Before = p2Inv.CountItem(item.itemId);
                        p2Trade.RunTradeOp(TradeOpKind.RequestByName, hostTrade.TraderName, 0, 0, 0, false);
                        yield return new WaitForSeconds(1.5f);
                        r.Log($"two: trade back: after Player2's ask: Tester trading {hostTrade.IsTrading}, Player2 trading {p2Trade.IsTrading}");
                        ownTrade.Respond(true);
                        yield return new WaitForSeconds(1.5f);
                        p2Trade.RunTradeOp(TradeOpKind.Offer, null, (short)back, (ushort)backCount, 0, false);
                        yield return new WaitForSeconds(1.5f);
                        p2Trade.RunTradeOp(TradeOpKind.Lock, null, 0, 0, 0, true);
                        ownTrade.SetLocked(true);
                        yield return new WaitForSeconds(1.5f);
                        p2Trade.RunTradeOp(TradeOpKind.Confirm, null, 0, 0, 0, false);
                        ownTrade.Confirm();
                        yield return new WaitForSeconds(2.5f);
                        int hostAfter = hostInv.CountItem(item.itemId), p2After = p2Inv.CountItem(item.itemId);
                        bool backOk = hostAfter == hostBefore + backCount && p2After == p2Before - backCount;
                        r.Log($"two: trade back: item {item.itemId} x{backCount}: Tester {hostBefore} -> {hostAfter}, Player2 {p2Before} -> {p2After}, still trading {hostTrade.IsTrading}: {(backOk ? "PASS" : "FAIL")}");
                        tradeOk = backOk;
                    }

                    // both saves after the trades (a trade saves both players in one change: ServerSave.SaveTogether)
                    yield return new WaitForSeconds(2f);
                    bool hostSaved = SavedPackMatches(r.Body, hostInv, r.Log, "Tester");
                    bool p2Saved = SavedPackMatches(p2, p2Inv, r.Log, "Player2");
                    r.Log($"two: saves after the trades ({ServerSave.Store?.GetType().Name}): Tester {(hostSaved ? "PASS" : "FAIL")}, Player2 {(p2Saved ? "PASS" : "FAIL")}");
                    tradeOk = tradeOk && hostSaved && p2Saved;
                }

                // 3) the vendors' shelves from the own view, then one buy at the nearest
                var ownShop = r.Own.GetComponent<PlayerShopping>();
                var offers = new List<VendorOffer>();
                int shops = 0, stocked = 0;
                NpcInteractable nearest = null;
                VendorStock nearestStock = null;
                foreach (NpcInteractable npc in NpcInteractable.Active)
                {
                    if (npc == null || (npc.Offers & NpcOffer.Shop) == 0) continue;
                    shops++;
                    VendorStock stock = ownShop != null ? ownShop.StockOf(npc) : null;
                    offers.Clear();
                    if (stock != null) { ownShop.List(stock, offers); stocked++; }
                    r.Log($"two: vendor '{npc.name}' ({npc.Definition?.name}): {(stock != null ? offers.Count + " for sale" : "NOTHING (no shelf)")}");
                    if (stock != null && (nearest == null || Vector3.Distance(npc.transform.position, r.Body.transform.position) < Vector3.Distance(nearest.transform.position, r.Body.transform.position)))
                    { nearest = npc; nearestStock = stock; }
                }
                vendorsOk = shops > 0 && stocked == shops;
                r.Log($"two: vendors: {stocked} of {shops} shelves seen: {(vendorsOk ? "PASS" : "FAIL")}");
                if (nearest != null)
                {
                    r.Put(r.Body, nearest.transform.position + nearest.transform.forward * 2f);
                    yield return new WaitForSeconds(2.5f);
                    offers.Clear();
                    ownShop.List(nearestStock, offers);
                    VendorOffer pick = offers.Where(o => o.Item != null && o.Price <= hostWallet.Creds).OrderBy(o => o.Price).FirstOrDefault();
                    if (pick.Item == null) r.Log("two: buy: FAIL; nothing affordable");
                    else
                    {
                        int had = hostInv.CountItem(pick.Item.itemId);
                        long creds = hostWallet.Creds;
                        ownShop.RequestBuy(nearestStock, pick.Item, 1);
                        yield return new WaitForSeconds(2.5f);
                        buyOk = hostInv.CountItem(pick.Item.itemId) == had + 1 && hostWallet.Creds == creds - pick.Price;
                        r.Log($"two: buy '{pick.Item.displayName}' for {pick.Price} at '{nearest.name}': Pack {had} -> {hostInv.CountItem(pick.Item.itemId)}, Creds {creds} -> {hostWallet.Creds}: {(buyOk ? "PASS" : "FAIL")}");
                    }
                }
            }
            finally
            {
                EditorPrefs.SetBool(KnJoinHere.MppmJoinPref, false);
                if (!squad) r.Log($"two: GM default target {(gmOk ? "PASS" : "FAIL")}, trade {(tradeOk ? "PASS" : "FAIL")}, vendors {(vendorsOk ? "PASS" : "FAIL")}, buy {(buyOk ? "PASS" : "FAIL")}");
                if (!squad) r.Verdict(gmOk && tradeOk && vendorsOk && buyOk);
            }
        }
    }
}
