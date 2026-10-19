using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using System.Text;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Economy
{
    /// <summary>What the owner is told about the trade it is in, beside the two offers.</summary>
    public struct TradeHeader : INetworkSerializable
    {
        public bool active;
        public FixedString64Bytes partnerName;
        public ulong partnerObjectId;
        public long myCreds, theirCreds;
        public bool myLocked, theirLocked, myConfirmed, theirConfirmed;
        /// <summary>The most Creds one trade moves each way.</summary>
        public long perTrade;
        /// <summary>What this account may still send in trades today.</summary>
        public long remainingToday;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref active);
            serializer.SerializeValue(ref partnerName);
            serializer.SerializeValue(ref partnerObjectId);
            serializer.SerializeValue(ref myCreds);
            serializer.SerializeValue(ref theirCreds);
            serializer.SerializeValue(ref myLocked);
            serializer.SerializeValue(ref theirLocked);
            serializer.SerializeValue(ref myConfirmed);
            serializer.SerializeValue(ref theirConfirmed);
            serializer.SerializeValue(ref perTrade);
            serializer.SerializeValue(ref remainingToday);
        }
    }

    /// <summary>
    /// Trading with another player: ask, accept, each lays out items and Creds, both lock, both confirm, and the
    /// server swaps everything at once (<see cref="TradeRules"/>). Offered items stay in the Pack until then; the
    /// server only trusts what the Packs hold at the moment of the swap. Any change to an offer unlocks both sides,
    /// so nobody confirms something other than what they saw. Walking apart, dying or leaving cancels.
    /// The Creds an account sends in trades are capped per trade and per day; the day's total is saved with the
    /// account, so switching character does not reset it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInventory))]
    public sealed class PlayerTrade : NetworkBehaviour, ISaveParticipant
    {
        private const string PromptId = "trade";
        // Walking a little past trading distance does not end a trade at once.
        private const float LeashPadding = 2f;

        /// <summary>Server only: the two players of one trade.</summary>
        private sealed class Session
        {
            public PlayerTrade A, B;
        }

        [SerializeField] private EconomySettings settings;

        // Reused on the server for requests by name: they are handled one at a time on the main thread.
        private static readonly List<ChatParticipant> Online = new List<ChatParticipant>(16);
        private static readonly List<PlayerTrade> OnlineTraders = new List<PlayerTrade>(16);
        private static readonly List<int> Found = new List<int>(4);

        private PlayerInventory inventory;
        private PlayerWallet wallet;
        private PlayerResources resources;
        private PlayerIdentity identity;
        private PlayerChat chat;
        private PlayerSaveAgent saveAgent;

        // Server only.
        private Session session;
        private readonly List<TradeLine> lines = new List<TradeLine>(12);
        private long offeredCreds;
        private bool locked, confirmed;
        private PlayerTrade requestedBy;
        private float requestExpires;
        private long sentDay, sentAmount;
        private bool leaving;

        // Owner only: what the server sent last.
        private TradeHeader view;
        private TradeLine[] mine = Array.Empty<TradeLine>();
        private TradeLine[] theirs = Array.Empty<TradeLine>();

        /// <summary>Raised on the owner's machine when its trade changes (opens, an offer changes, closes).</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerTrade Local => LocalPlayer.Get<PlayerTrade>();

        public string TraderName => identity != null ? identity.CharacterName : name;
        public bool IsTrading => view.active;
        public TradeHeader View => view;
        /// <summary>Who the owner is trading with; empty when not trading.</summary>
        public string PartnerName => view.active ? view.partnerName.ToString() : string.Empty;
        /// <summary>The owner's offer, with the Pack cell each line comes from.</summary>
        public IReadOnlyList<TradeLine> Mine => mine;
        public IReadOnlyList<TradeLine> Theirs => theirs;
        /// <summary>The last thing the server said about this trade ("Vex's Pack has no room"); empty when nothing.</summary>
        public string LastNotice { get; private set; } = string.Empty;

        private bool HasAuthority => !IsSpawned || IsServer;
        private int MaxLines => settings != null ? settings.tradeSlots : 12;
        private float Range => settings != null ? settings.tradeRange : 6f;
        private long PerTrade => settings != null ? settings.tradeCredsPerTrade : long.MaxValue;
        private long PerDay => settings != null ? settings.tradeCredsPerDay : long.MaxValue;

        private void Awake()
        {
            inventory = GetComponent<PlayerInventory>();
            wallet = GetComponent<PlayerWallet>();
            resources = GetComponent<PlayerResources>();
            identity = GetComponent<PlayerIdentity>();
            chat = GetComponent<PlayerChat>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && !NetworkManager.ShutdownInProgress)
            {
                leaving = true;
                requestedBy = null;
                if (session != null) EndSession(session, null, $"{TraderName} left the game. The trade was cancelled.");
            }
            view = default;
            base.OnNetworkDespawn();
        }

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Ask the player <paramref name="target"/> (the one selected in the world) to trade.</summary>
        public void Request(PlayerTrade target)
        {
            if (target == null) return;
            if (!IsSpawned) ServerRequest(target);
            else if (IsOwner && target.IsSpawned) RequestRpc(target.NetworkObject);
        }

        /// <summary>Ask the player called <paramref name="playerName"/> to trade.</summary>
        public void Request(string playerName)
        {
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(playerName)) RequestByNameRpc(playerName.Trim());
        }

        /// <summary>Answer the request this player was sent.</summary>
        public void Respond(bool accept)
        {
            if (IsSpawned) { if (IsOwner) RespondRpc(accept); } else ServerRespond(accept);
        }

        /// <summary>Put a Pack cell in the offer. <paramref name="count"/> 0 = the whole stack.</summary>
        public void Offer(int packCell, int count = 0)
        {
            if (IsSpawned) { if (IsOwner) OfferRpc((short)packCell, (ushort)Mathf.Clamp(count, 0, ushort.MaxValue)); }
            else ServerOffer(packCell, count);
        }

        /// <summary>Take a line of the owner's offer back.</summary>
        public void Withdraw(int lineIndex)
        {
            if (IsSpawned) { if (IsOwner) WithdrawRpc((byte)Mathf.Clamp(lineIndex, 0, 255)); } else ServerWithdraw(lineIndex);
        }

        public void SetCreds(long amount)
        {
            if (IsSpawned) { if (IsOwner) SetCredsRpc(amount); } else ServerSetCreds(amount);
        }

        /// <summary>Lock the offer (nothing more changes), or unlock it again.</summary>
        public void SetLocked(bool on)
        {
            if (IsSpawned) { if (IsOwner) LockRpc(on); } else ServerLock(on);
        }

        /// <summary>Agree to the trade as both sides have locked it. It happens when both have confirmed.</summary>
        public void Confirm()
        {
            if (IsSpawned) { if (IsOwner) ConfirmRpc(); } else ServerConfirm();
        }

        public void Cancel()
        {
            if (IsSpawned) { if (IsOwner) CancelRpc(); } else ServerCancel();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRpc(NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(RequestRpc))) return;
            ServerRequest(targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerTrade>() : null);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestByNameRpc(string playerName)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(RequestByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(RequestByNameRpc), playerName)) return;
            Online.Clear();
            OnlineTraders.Clear();
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerTrade other)) continue;
                Online.Add(new ChatParticipant { ClientId = client.ClientId, Name = other.TraderName });
                OnlineTraders.Add(other);
            }

            ChatRouting.FindByName(playerName, Online, Found);
            if (Found.Count == 0) { Tell($"No player named '{ChatRouting.Clean(playerName)}' is online."); return; }
            if (Found.Count > 1)
            {
                var names = new StringBuilder("Several players share that name. Trade with one of: ");
                for (int i = 0; i < Found.Count; i++)
                {
                    if (i > 0) names.Append(", ");
                    names.Append(Online[Found[i]].Name).Append(ChatRouting.IdSeparator).Append(Online[Found[i]].ClientId);
                }
                Tell(names.ToString());
                return;
            }
            ServerRequest(OnlineTraders[Found[0]]);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(bool accept)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(RespondRpc))) return;
            ServerRespond(accept);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void OfferRpc(short packCell, ushort count)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(OfferRpc))) return;
            ServerOffer(packCell, count);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WithdrawRpc(byte lineIndex)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(WithdrawRpc))) return;
            ServerWithdraw(lineIndex);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SetCredsRpc(long amount)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(SetCredsRpc))) return;
            ServerSetCreds(amount);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void LockRpc(bool on)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(LockRpc))) return;
            ServerLock(on);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void ConfirmRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(ConfirmRpc))) return;
            ServerConfirm();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void CancelRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(CancelRpc))) return;
            ServerCancel();
        }

        // ------------------------------------------------------------------ asking and answering (server)

        private bool IsAlive => resources == null || resources.IsAlive;
        private FactionId Faction => identity != null ? identity.Faction : FactionId.None;

        private float DistanceTo(PlayerTrade other)
        {
            Vector3 offset = other.transform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>Why this player and <paramref name="other"/> may not trade right now (told to this player), or null.</summary>
        private string Refusal(PlayerTrade other)
        {
            if (!IsAlive) return "You can't trade while defeated.";
            if (!other.IsAlive) return $"{other.TraderName} is defeated.";
            bool crossFaction = TradeZone.CrossFactionAt(transform.position) && TradeZone.CrossFactionAt(other.transform.position);
            if (!TradeRules.CanTrade(Faction, other.Faction, crossFaction)) return $"{other.TraderName} is not of your faction: you can't trade here.";
            if (DistanceTo(other) > Range) return $"{other.TraderName} is too far away to trade.";
            return SocialGate.Refusal(this, other);
        }

        private void ServerRequest(PlayerTrade target)
        {
            if (!HasAuthority) return;
            if (target == null || target == this) { Tell("Trade with another player."); return; }
            if (session != null) { Tell("You are already trading."); return; }
            if (target.session != null) { Tell($"{target.TraderName} is trading with someone else."); return; }
            if (target.requestedBy != null && target.requestedBy != this && Time.time <= target.requestExpires)
            { Tell($"{target.TraderName} is answering another trade request."); return; }
            string refusal = Refusal(target);
            if (refusal != null) { Tell(refusal); return; }

            // Two players asking each other is an agreement.
            if (requestedBy == target && Time.time <= requestExpires)
            {
                ServerRespond(true);
                return;
            }

            float seconds = settings != null ? settings.tradeRequestSeconds : 30f;
            target.requestedBy = this;
            target.requestExpires = Time.time + seconds;
            Tell($"You asked {target.TraderName} to trade.");
            target.Tell($"{TraderName} wants to trade with you. Accept with /trade accept, refuse with /trade decline.");
            target.ShowPrompt(TraderName, seconds);
        }

        private void ServerRespond(bool accept)
        {
            if (!HasAuthority) return;
            PlayerTrade from = requestedBy;
            bool live = from != null && !from.leaving && from.isActiveAndEnabled && Time.time <= requestExpires;
            requestedBy = null;
            HidePrompt();
            if (!live) { Tell("There is no trade request to answer."); return; }

            if (!accept)
            {
                Tell($"You declined to trade with {from.TraderName}.");
                from.Tell($"{TraderName} declined to trade.");
                return;
            }
            if (session != null) { Tell("You are already trading."); return; }
            if (from.session != null) { Tell($"{from.TraderName} is trading with someone else."); return; }
            string refusal = Refusal(from);
            if (refusal != null)
            {
                Tell(refusal);
                from.Tell($"The trade with {TraderName} could not start.");
                return;
            }

            var started = new Session { A = from, B = this };
            from.Begin(started);
            Begin(started);
            Publish(started);
        }

        private void Begin(Session started)
        {
            session = started;
            lines.Clear();
            offeredCreds = 0;
            locked = confirmed = false;
            requestedBy = null;
            SetNotice(string.Empty);
        }

        private static PlayerTrade Other(Session s, PlayerTrade one) => s.A == one ? s.B : s.A;

        // ------------------------------------------------------------------ the offer (server)

        private bool InSession()
        {
            if (!HasAuthority) return false;
            if (session != null) return true;
            Tell("You are not trading.");
            return false;
        }

        private void ServerOffer(int packCell, int count)
        {
            if (!InSession()) return;
            var pack = new ItemStack[PlayerInventory.Capacity];
            inventory.CopyPack(pack);
            if (count <= 0 && packCell >= 0 && packCell < pack.Length) count = pack[packCell].count;
            string blocked = TradeRules.AddBlockedReason(lines, pack, packCell, count, MaxLines);
            if (blocked != null) { Notice(blocked); return; }
            // A faction's own item (a furnishing) is never handed to a character of another faction.
            if (!ItemFactionRules.MayHave(inventory.Catalog != null ? inventory.Catalog.Get(pack[packCell].itemId) : null, Other(session, this).Faction))
            { Notice(ItemFactionRules.ReasonTheirFaction); return; }
            lines.Add(new TradeLine(packCell, pack[packCell].WithCount(count)));
            OfferChanged();
        }

        private void ServerWithdraw(int lineIndex)
        {
            if (!InSession() || lineIndex < 0 || lineIndex >= lines.Count) return;
            lines.RemoveAt(lineIndex);
            OfferChanged();
        }

        private void ServerSetCreds(long amount)
        {
            if (!InSession() || wallet == null) return;
            amount = Math.Max(0, amount);
            TradeFailure failure = TradeRules.CredsFailure(amount, wallet.Creds, SentToday(), PerTrade, PerDay);
            if (failure != TradeFailure.None) { Notice(OwnFault(failure)); return; }
            if (amount == offeredCreds) return;
            offeredCreds = amount;
            OfferChanged();
        }

        /// <summary>Whatever either side agreed to, it was to the offers as they were.</summary>
        private void OfferChanged()
        {
            session.A.locked = session.A.confirmed = false;
            session.B.locked = session.B.confirmed = false;
            SetNotice(string.Empty);
            Other(session, this).SetNotice(string.Empty);
            Publish(session);
        }

        private void ServerLock(bool on)
        {
            if (!InSession() || locked == on) return;
            locked = on;
            // Unlocking takes back any confirmation, on both sides.
            if (!on) session.A.confirmed = session.B.confirmed = false;
            Publish(session);
        }

        private void ServerConfirm()
        {
            if (!InSession()) return;
            PlayerTrade other = Other(session, this);
            if (!locked || !other.locked) { Notice("Both offers must be locked first."); return; }
            if (confirmed) return;
            confirmed = true;
            if (other.confirmed) Commit(session);
            else Publish(session);
        }

        private void ServerCancel()
        {
            if (!HasAuthority) return;
            if (session == null)
            {
                // Nothing running: take back a request this player sent, if there is one.
                foreach (PlayerTrade asked in FindObjectsByType<PlayerTrade>(FindObjectsInactive.Exclude))
                {
                    if (asked.requestedBy != this) continue;
                    asked.requestedBy = null;
                    asked.HidePrompt();
                    asked.Tell($"{TraderName} withdrew the trade request.");
                    Tell("Trade request withdrawn.");
                    return;
                }
                Tell("You are not trading.");
                return;
            }
            EndSession(session, "You cancelled the trade.", $"{TraderName} cancelled the trade.", this);
        }

        private void Update()
        {
            // One of the two watches over the trade.
            if (session == null || session.A != this || !HasAuthority) return;
            PlayerTrade other = session.B;
            if (other == null) { EndSession(session, "The trade was cancelled.", null, this); return; }
            if (!IsAlive || !other.IsAlive)
            {
                PlayerTrade fallen = IsAlive ? other : this;
                EndSession(session, "You were defeated. The trade was cancelled.", $"{fallen.TraderName} was defeated. The trade was cancelled.", fallen);
            }
            else if (DistanceTo(other) > Range + LeashPadding)
                EndSession(session, "You moved too far apart. The trade was cancelled.", "You moved too far apart. The trade was cancelled.", this);
        }

        // ------------------------------------------------------------------ the swap (server)

        private long SentToday()
        {
            long today = WalletRules.DayOf(DateTime.UtcNow);
            long sent = TradeRules.SentOn(today, sentDay, sentAmount);
            sentDay = today;
            sentAmount = sent;
            return sent;
        }

        private TradeSide Side()
        {
            var pack = new ItemStack[PlayerInventory.Capacity];
            inventory.CopyPack(pack);
            return new TradeSide { pack = pack, lines = lines, creds = offeredCreds, wallet = wallet != null ? wallet.Creds : 0, sentToday = SentToday() };
        }

        private static void Commit(Session s)
        {
            PlayerTrade a = s.A, b = s.B;
            TradeSide sideA = a.Side(), sideB = b.Side();
            long cap = a.wallet != null ? a.wallet.Cap : long.MaxValue;
            if (!TradeRules.TryCommit(ref sideA, ref sideB, a.inventory.Catalog, a.PerTrade, a.PerDay, cap, out TradeFailure failure, out bool failedOnA))
            {
                PlayerTrade culprit = failedOnA ? a : b, other = failedOnA ? b : a;
                a.locked = a.confirmed = b.locked = b.confirmed = false;
                culprit.SetNotice(culprit.OwnFault(failure));
                other.SetNotice(TheirFault(failure, culprit.TraderName));
                Publish(s);
                return;
            }

            ItemStack[] packA = sideA.pack, packB = sideB.pack;
            a.inventory.ServerChangePack(pack => { Array.Copy(packA, pack, pack.Length); return true; });
            b.inventory.ServerChangePack(pack => { Array.Copy(packB, pack, pack.Length); return true; });
            Move(a, b, a.offeredCreds);
            Move(b, a, b.offeredCreds);
            a.sentAmount = sideA.sentToday;
            b.sentAmount = sideB.sentToday;

            string gaveA = Describe(a), gaveB = Describe(b);
            ServerRecords.Write(RecordKind.Economy, $"Trade: {ServerRecords.Who(a)} gave {Recorded(a)}; {ServerRecords.Who(b)} gave {Recorded(b)}.");
            KeepLedger(a, b);
            a.Tell($"Trade with {b.TraderName} complete. You gave {gaveA} and received {gaveB}.");
            b.Tell($"Trade with {a.TraderName} complete. You gave {gaveB} and received {gaveA}.");
            EndSession(s, null, null);
            // Both players, and the ledger lines of both, in one change (online phase 4, milestone 3): a crash keeps the
            // trade whole or not at all, never an item on both sides or on neither.
            ServerSave.SaveTogether(new[] { a.saveAgent, b.saveAgent });
        }

        // Each side's line: what it gave up and what it received (its Creds are tallied by the wallets).
        private static void KeepLedger(PlayerTrade a, PlayerTrade b)
        {
            ItemCatalog catalog = a.inventory != null ? a.inventory.Catalog : null;
            LedgerEntry lineA = EconomyLedger.Line(a, LedgerKinds.Trade, b, CredsNote(a.offeredCreds, b.offeredCreds));
            LedgerEntry lineB = EconomyLedger.Line(b, LedgerKinds.Trade, a, CredsNote(b.offeredCreds, a.offeredCreds));
            foreach (TradeLine line in a.lines) { lineA.Item(catalog, line.stack, -1); lineB.Item(catalog, line.stack, 1); }
            foreach (TradeLine line in b.lines) { lineB.Item(catalog, line.stack, -1); lineA.Item(catalog, line.stack, 1); }
            EconomyLedger.Keep(lineA);
            EconomyLedger.Keep(lineB);
        }

        private static string CredsNote(long gave, long got) =>
            gave == 0 && got == 0 ? null : $"gave {gave} Creds, received {got} Creds";

        private static void Move(PlayerTrade from, PlayerTrade to, long creds)
        {
            if (creds <= 0 || from.wallet == null || to.wallet == null) return;
            if (from.wallet.ServerSpend(creds, CredsSink.Trade)) to.wallet.ServerEarn(creds, CredsSource.Trade);
        }

        // Every item by name, id and count, and the Creds, for the economy record.
        private static string Recorded(PlayerTrade side)
        {
            var parts = new List<string>();
            foreach (TradeLine line in side.lines)
            {
                ItemDefinition item = side.inventory != null && side.inventory.Catalog != null ? side.inventory.Catalog.Get(line.stack.itemId) : null;
                parts.Add($"{line.stack.count}x {(item != null ? item.displayName : "item")} (item {line.stack.itemId})");
            }
            if (side.offeredCreds > 0) parts.Add(side.offeredCreds + " Creds");
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        private static string Describe(PlayerTrade side)
        {
            int items = 0;
            foreach (TradeLine line in side.lines) items += line.stack.count;
            if (items == 0 && side.offeredCreds == 0) return "nothing";
            string things = items == 0 ? string.Empty : items == 1 ? "1 item" : $"{items} items";
            string creds = side.offeredCreds > 0 ? WalletRules.FormatCreds(side.offeredCreds) : string.Empty;
            return things.Length > 0 && creds.Length > 0 ? $"{things} and {creds}" : things + creds;
        }

        /// <summary>Ends a trade. <paramref name="toActor"/> is told to <paramref name="actor"/>, <paramref name="toOther"/> to the other one (to both when there is no actor).</summary>
        private static void EndSession(Session s, string toActor, string toOther, PlayerTrade actor = null)
        {
            foreach (PlayerTrade side in new[] { s.A, s.B })
            {
                if (side == null) continue;
                side.session = null;
                side.lines.Clear();
                side.offeredCreds = 0;
                side.locked = side.confirmed = false;
                side.Tell(actor == null ? toOther : side == actor ? toActor : toOther);
                side.SendView(default, Array.Empty<TradeLine>(), Array.Empty<TradeLine>());
            }
        }

        private string OwnFault(TradeFailure failure)
        {
            switch (failure)
            {
                case TradeFailure.OfferChanged: return "Your Pack no longer holds what you offered. Both offers were unlocked.";
                case TradeFailure.NoRoom: return "Your Pack has no room for what you would receive.";
                case TradeFailure.NotEnoughCreds: return "You don't carry that many Creds.";
                case TradeFailure.OverTradeCap: return $"A trade moves at most {WalletRules.FormatCreds(PerTrade)} each way.";
                case TradeFailure.OverDailyCap:
                    return $"You can send {WalletRules.FormatCreds(TradeRules.RemainingToday(SentToday(), PerDay))} more in trades today (the limit is {WalletRules.Format(PerDay)} a day).";
                case TradeFailure.WalletFull: return "Your wallet can't hold that many Creds.";
                default: return string.Empty;
            }
        }

        private static string TheirFault(TradeFailure failure, string who)
        {
            switch (failure)
            {
                case TradeFailure.OfferChanged: return $"{who}'s Pack no longer holds what was offered. Both offers were unlocked.";
                case TradeFailure.NoRoom: return $"{who}'s Pack has no room for your items.";
                case TradeFailure.NotEnoughCreds: return $"{who} doesn't carry the Creds that were offered.";
                case TradeFailure.OverTradeCap: return $"{who} offered more Creds than one trade moves.";
                case TradeFailure.OverDailyCap: return $"{who} has reached today's limit for Creds sent in trades.";
                case TradeFailure.WalletFull: return $"{who}'s wallet can't hold that many Creds.";
                default: return string.Empty;
            }
        }

        // ------------------------------------------------------------------ server -> owner

        private static void Publish(Session s)
        {
            s.A.SendViewOf(s.B);
            s.B.SendViewOf(s.A);
        }

        private void SendViewOf(PlayerTrade other)
        {
            var header = new TradeHeader
            {
                active = true,
                partnerObjectId = other.IsSpawned ? other.NetworkObjectId : 0,
                myCreds = offeredCreds, theirCreds = other.offeredCreds,
                myLocked = locked, theirLocked = other.locked,
                myConfirmed = confirmed, theirConfirmed = other.confirmed,
                perTrade = PerTrade,
                remainingToday = TradeRules.RemainingToday(SentToday(), PerDay)
            };
            header.partnerName.CopyFromTruncated(other.TraderName);
            var shown = new TradeLine[other.lines.Count];
            for (int i = 0; i < shown.Length; i++) shown[i] = new TradeLine(-1, other.lines[i].stack);
            SendView(header, lines.ToArray(), shown);
        }

        private void SendView(TradeHeader header, TradeLine[] own, TradeLine[] partner)
        {
            if (leaving) return;
            if (IsSpawned) ViewRpc(header, own, partner);
            else ApplyView(header, own, partner);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ViewRpc(TradeHeader header, TradeLine[] own, TradeLine[] partner) => ApplyView(header, own, partner);

        private void ApplyView(TradeHeader header, TradeLine[] own, TradeLine[] partner)
        {
            view = header;
            mine = own ?? Array.Empty<TradeLine>();
            theirs = partner ?? Array.Empty<TradeLine>();
            if (!header.active) LastNotice = string.Empty;
            Changed?.Invoke();
        }

        /// <summary>A refusal the player should see in the trade window, and in the chat log.</summary>
        private void Notice(string text)
        {
            SetNotice(text);
            Tell(text);
        }

        private void SetNotice(string text)
        {
            if (leaving) return;
            text ??= string.Empty;
            if (IsSpawned) NoticeRpc(text);
            else ApplyNotice(text);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void NoticeRpc(string text) => ApplyNotice(text);

        private void ApplyNotice(string text)
        {
            LastNotice = text ?? string.Empty;
            Changed?.Invoke();
        }

        private void Tell(string text)
        {
            if (leaving || string.IsNullOrEmpty(text)) return;
            if (chat != null && chat.isActiveAndEnabled) chat.ServerSystem(text);
            else LastNotice = text;
        }

        private void ShowPrompt(string fromName, float seconds)
        {
            if (leaving) return;
            if (IsSpawned) PromptRpc(fromName, seconds);
        }

        private void HidePrompt()
        {
            if (!leaving && IsSpawned) PromptClosedRpc();
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptRpc(string fromName, float seconds) =>
            ClientPresentationRegistry.Prompts?.Show(PromptId, "TRADE", $"{fromName} wants to trade with you.", seconds, Respond);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptClosedRpc() => ClientPresentationRegistry.Prompts?.Hide(PromptId);

        // ------------------------------------------------------------------ saving (the account's day total)

        [Serializable]
        private struct TradeSave
        {
            public long day;
            public long sent;
        }

        public string SaveKey => "trade";
        public SaveScope SaveScope => SaveScope.Account;

        public string CaptureSave() => JsonUtility.ToJson(new TradeSave { day = sentDay, sent = sentAmount });

        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            var save = JsonUtility.FromJson<TradeSave>(json);
            sentDay = save.day;
            sentAmount = Math.Max(0, save.sent);
        }

        /// <summary>GM: forget what this account has sent in trades today.</summary>
        public void ServerResetDailyAllowance()
        {
            if (!HasAuthority) return;
            sentAmount = 0;
            if (session != null) Publish(session);
        }

        /// <summary>Server only: what this account may still send in trades today.</summary>
        public long ServerRemainingToday() => TradeRules.RemainingToday(SentToday(), PerDay);
    }
}
