using System.Collections.Generic;
using System.Text;
using KryticalNova.Combat;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Networking;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// A player's voice and ears in chat. The owner asks the server to send; the server applies the channel rules
    /// (<see cref="ChatRouting"/>) and hands each line to the players who should read it, on their own player object,
    /// so a far-away speaker that is not replicated to them still reaches them. The same component carries this
    /// player's combat log: what they dealt and what they took, sent to them and to nobody else.
    /// Offline (the unspawned scene avatar) everything is decided and shown locally.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerChat : NetworkBehaviour
    {
        private const byte CriticalBit = 1;
        private const byte EvadedBit = 2;
        private const byte ReflectedBit = 4;
        private const byte ShatteredBit = 8;

        [Tooltip("How far a General message carries, in metres.")]
        [SerializeField, Min(1f)] private float shoutRadius = 40f;
        [Tooltip("At most this many messages...")]
        [SerializeField, Min(1)] private int rateLimitMessages = 5;
        [Tooltip("...in this many seconds. Further messages are refused.")]
        [SerializeField, Min(0.5f)] private float rateLimitSeconds = 5f;

        private readonly NetworkVariable<FixedString32Bytes> netGuild = new NetworkVariable<FixedString32Bytes>(
            writePerm: NetworkVariableWritePermission.Server);

        // Reused on the server for every message: chat is handled one message at a time on the main thread.
        private static readonly List<ChatParticipant> Everyone = new List<ChatParticipant>(16);
        private static readonly List<PlayerChat> EveryoneChat = new List<PlayerChat>(16);
        private static readonly List<int> Picked = new List<int>(16);

        private readonly Queue<float> recentSends = new Queue<float>(8);
        // Death lines wait for the end of the frame, so the hit that killed is logged before them.
        private readonly List<string> deferredCombat = new List<string>(4);
        private PlayerIdentity identity;
        private CombatEntity combatEntity;
        private PlayerMeleeCombat meleeCombat;
        private PlayerDeathMotor deathMotor;
        private PlayerDuel duel;
        private PlayerParty party;
        private PlayerGm gm;
        private string offlineGuild = string.Empty;

        /// <summary>This machine's own player, or null (dedicated server, no player yet).</summary>
        public static PlayerChat Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerChat>() : null;

        public string Guild => IsSpawned ? netGuild.Value.ToString() : offlineGuild;
        public string SpeakerName => identity != null ? identity.CharacterName : name;

        /// <summary>Only a GM (<see cref="PlayerGm"/>: the host's own player, a joined player who typed a GM password, or the offline player) may speak on the GM channel.</summary>
        public bool IsGameMaster => !IsSpawned || OwnerClientId == NetworkManager.ServerClientId || (gm != null && gm.IsGm);

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            combatEntity = GetComponent<CombatEntity>();
            meleeCombat = GetComponent<PlayerMeleeCombat>();
            deathMotor = GetComponent<PlayerDeathMotor>();
            duel = GetComponent<PlayerDuel>();
            party = GetComponent<PlayerParty>();
            gm = GetComponent<PlayerGm>();
        }

        private void OnEnable()
        {
            // Both are raised where combat is resolved (server or offline), which is where the log is written.
            if (combatEntity != null) combatEntity.Damaged += OnDamaged;
            if (meleeCombat != null) meleeCombat.HitResolved += OnHitResolved;
            if (deathMotor != null)
            {
                deathMotor.Died += OnDied;
                deathMotor.RespawnCountdown += OnRespawnCountdown;
                deathMotor.Respawned += OnRespawned;
            }
            if (duel != null)
            {
                duel.Notice += ServerSystem;
                duel.Announcement += ServerAnnounceNearby;
            }
        }

        private void OnDisable()
        {
            if (combatEntity != null) combatEntity.Damaged -= OnDamaged;
            if (meleeCombat != null) meleeCombat.HitResolved -= OnHitResolved;
            if (deathMotor != null)
            {
                deathMotor.Died -= OnDied;
                deathMotor.RespawnCountdown -= OnRespawnCountdown;
                deathMotor.Respawned -= OnRespawned;
            }
            if (duel != null)
            {
                duel.Notice -= ServerSystem;
                duel.Announcement -= ServerAnnounceNearby;
            }
        }

        // ------------------------------------------------------------------ sending (owner)

        /// <summary>Say something. Call on the machine that owns this player; the server decides who hears it.</summary>
        public void Send(ChatChannel channel, string text, string whisperTarget = null)
        {
            text = ChatRouting.Clean(text);
            if (text.Length == 0) return;
            if (!IsSpawned) ServerSend(channel, text, whisperTarget);
            else if (IsOwner) SendRpc(channel, text, whisperTarget ?? string.Empty);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SendRpc(ChatChannel channel, string text, string whisperTarget) => ServerSend(channel, text, whisperTarget);

        // ------------------------------------------------------------------ routing (server / offline)

        private void ServerSend(ChatChannel channel, string text, string whisperTarget)
        {
            if (!HasAuthority) return;
            text = ChatRouting.Clean(text); // never trust the length or content a client sent
            if (text.Length == 0) return;
            if (!WithinRateLimit())
            {
                ServerSystem("You are sending messages too quickly.");
                return;
            }

            Gather();
            ChatParticipant me = Describe();
            switch (ChatRouting.Recipients(channel, me, Everyone, whisperTarget, shoutRadius, Picked))
            {
                case ChatRoute.Delivered:
                    if (channel == ChatChannel.Whisper)
                    {
                        string refusal = SocialGate.Refusal(this, EveryoneChat[Picked[0]]);
                        if (refusal != null) { ServerSystem(refusal); break; }
                        EveryoneChat[Picked[0]].Deliver(ChatChannel.Whisper, me.Name, text, false);
                        Deliver(ChatChannel.Whisper, Everyone[Picked[0]].Name, text, true);
                    }
                    else if (channel == ChatChannel.Gm)
                    {
                        // The GM's own "/gm": the same announcement as gm.say, toast included. The channel says GM already.
                        for (int i = 0; i < Picked.Count; i++) EveryoneChat[Picked[i]].DeliverAnnounced(AnnouncementKind.Gm, channel, me.Name, text, me.Name);
                    }
                    else
                    {
                        // The [GM] tag goes on the other channels' lines: a whisper's sender is the name /r answers to.
                        string label = PlayerGm.Label(this, me.Name);
                        for (int i = 0; i < Picked.Count; i++) EveryoneChat[Picked[i]].Deliver(channel, label, text, false);
                    }
                    break;
                case ChatRoute.NotInGuild:
                    ServerSystem("You are not in a crew.");
                    break;
                case ChatRoute.NotInParty:
                    ServerSystem("You are not in a squad. Invite a player with /invite <name>.");
                    break;
                case ChatRoute.NoSuchPlayer:
                    ServerSystem($"No player named '{whisperTarget}' is online.");
                    break;
                case ChatRoute.AmbiguousPlayer:
                    var names = new StringBuilder("Several players share that name. Whisper one of: ");
                    for (int i = 0; i < Picked.Count; i++)
                    {
                        if (i > 0) names.Append(", ");
                        names.Append(Everyone[Picked[i]].Name).Append(ChatRouting.IdSeparator).Append(Everyone[Picked[i]].ClientId);
                    }
                    ServerSystem(names.ToString());
                    break;
                case ChatRoute.WhisperToSelf:
                    ServerSystem("You cannot whisper to yourself.");
                    break;
                case ChatRoute.NotGameMaster:
                    ServerSystem("Only a GM can use this channel.");
                    break;
            }
        }

        /// <summary>
        /// Server (or offline) only: a GM announcement to every player of every faction, wherever they are. For the
        /// GM tools; a player's own "/gm" goes through <see cref="Send"/> and is refused unless they are a GM (<see cref="IsGameMaster"/>).
        /// </summary>
        public static int ServerBroadcastGm(string text, string senderName) =>
            ServerAnnounce(AnnouncementKind.Gm, ChatChannel.Gm, senderName, text);

        /// <summary>
        /// Server (or offline) only: news for every player on the server, wherever they are. Each player gets it as
        /// a line on <paramref name="channel"/> and as a toast, sent together in one message so the two always say
        /// the same. Returns how many players were told.
        /// </summary>
        /// <param name="highlight">The names the news is about ("|" between them), lit on the ticker; or null.</param>
        /// <param name="rarity">The rarity of the item the news is about (an <c>ItemRarity</c> as a byte), which colours the ticker line; 0 for none.</param>
        public static int ServerAnnounce(AnnouncementKind kind, ChatChannel channel, string senderName, string text, string highlight = null, byte rarity = 0)
        {
            text = ChatRouting.Clean(text);
            if (text.Length == 0) return 0;
            highlight = highlight != null ? ChatRouting.Clean(highlight) : string.Empty;

            var network = NetworkManager.Singleton;
            if (network != null && network.IsListening)
            {
                if (!network.IsServer) return 0;
                int reached = 0;
                foreach (var client in network.ConnectedClientsList)
                {
                    if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerChat chat)) continue;
                    chat.DeliverAnnounced(kind, channel, senderName, text, highlight, rarity);
                    reached++;
                }
                return reached;
            }

            var local = Local;
            if (local == null) return 0;
            local.DeliverAnnounced(kind, channel, senderName, text, highlight, rarity);
            return 1;
        }

        /// <summary>Server (or offline) only: a notice from the game to this player.</summary>
        public void ServerSystem(string text)
        {
            if (!HasAuthority || string.IsNullOrEmpty(text)) return;
            Deliver(ChatChannel.System, string.Empty, text, false);
        }

        /// <summary>Server (or offline) only: news for every player within shouting distance of this one (this one included).</summary>
        public void ServerAnnounceNearby(string text)
        {
            if (!HasAuthority || string.IsNullOrEmpty(text)) return;
            Gather();
            float reach = shoutRadius * shoutRadius;
            for (int i = 0; i < EveryoneChat.Count; i++)
                if ((Everyone[i].Position - transform.position).sqrMagnitude <= reach) EveryoneChat[i].Deliver(ChatChannel.System, string.Empty, text, false);
        }

        /// <summary>Server (or offline) only: a line in this player's combat log (deaths, respawn countdowns).</summary>
        public void ServerCombatNotice(string text)
        {
            if (!HasAuthority || string.IsNullOrEmpty(text)) return;
            Deliver(ChatChannel.Combat, string.Empty, text, false);
        }

        /// <summary>Server (or offline) only: put this player in a guild, or take them out with an empty name.</summary>
        public void ServerSetGuild(string guild)
        {
            if (!HasAuthority) return;
            guild = string.IsNullOrWhiteSpace(guild) ? string.Empty : guild.Trim();
            var value = new FixedString32Bytes();
            value.CopyFromTruncated(guild);
            if (IsSpawned) netGuild.Value = value;
            else offlineGuild = value.ToString();
        }

        private bool WithinRateLimit()
        {
            float now = Time.unscaledTime;
            while (recentSends.Count > 0 && now - recentSends.Peek() > rateLimitSeconds) recentSends.Dequeue();
            if (recentSends.Count >= rateLimitMessages) return false;
            recentSends.Enqueue(now);
            return true;
        }

        private ChatParticipant Describe() => new ChatParticipant
        {
            ClientId = IsSpawned ? OwnerClientId : 0,
            Name = SpeakerName,
            Faction = identity != null ? identity.Faction : default,
            Guild = Guild,
            Party = party != null ? party.PartyId : 0,
            Position = transform.position,
            IsGameMaster = IsGameMaster,
        };

        private void Gather()
        {
            Everyone.Clear();
            EveryoneChat.Clear();
            if (!IsSpawned)
            {
                Everyone.Add(Describe());
                EveryoneChat.Add(this);
                return;
            }
            foreach (var client in NetworkManager.ConnectedClientsList)
            {
                if (client.PlayerObject == null || !client.PlayerObject.TryGetComponent(out PlayerChat chat)) continue;
                Everyone.Add(chat.Describe());
                EveryoneChat.Add(chat);
            }
        }

        // ------------------------------------------------------------------ receiving (this player's owner)

        private void Deliver(ChatChannel channel, string sender, string text, bool outgoing)
        {
            if (!IsSpawned) Show(channel, sender, text, outgoing);
            else ReceiveRpc(channel, sender ?? string.Empty, text, outgoing);
        }

        // Only the server may put a line in a player's window: nobody can forge a GM or system message.
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ReceiveRpc(ChatChannel channel, string sender, string text, bool outgoing) => Show(channel, sender, text, outgoing);

        private void DeliverAnnounced(AnnouncementKind kind, ChatChannel channel, string sender, string text, string highlight = null, byte rarity = 0)
        {
            if (!IsSpawned) ShowAnnounced(kind, channel, sender ?? string.Empty, text, highlight ?? string.Empty, rarity);
            else AnnounceRpc(kind, channel, sender ?? string.Empty, text, highlight ?? string.Empty, rarity);
        }

        // The chat line and the ticker line of an announcement travel as one message.
        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void AnnounceRpc(AnnouncementKind kind, ChatChannel channel, string sender, string text, string highlight, byte rarity) =>
            ShowAnnounced(kind, channel, sender, text, highlight, rarity);

        private static void ShowAnnounced(AnnouncementKind kind, ChatChannel channel, string sender, string text, string highlight, byte rarity)
        {
            Show(channel, sender, text, false);
            ClientPresentationRegistry.Announcements?.Announce(kind, sender, text, string.IsNullOrEmpty(highlight) ? null : highlight, rarity);
        }

        /// <summary>Raised on this machine for every line its player is shown (the Contacts window notes who whispered).</summary>
        public static event System.Action<ChatLine> LineShown;

        private static void Show(ChatChannel channel, string sender, string text, bool outgoing)
        {
            var line = new ChatLine { Channel = channel, Sender = sender, Text = text, Outgoing = outgoing };
            ClientPresentationRegistry.Chat?.Add(line);
            LineShown?.Invoke(line);
        }

        // ------------------------------------------------------------------ combat log (server / offline -> owner)

        private void OnHitResolved(IDamageable target, DamageResult result)
        {
            if (target == null) return;
            SendCombatLine(true, target.EntityName, result);
        }

        private void OnDamaged(DamageResult result, CharacterCombatStats attacker)
        {
            string other = "Something";
            if (attacker != null)
            {
                var entity = attacker.GetComponent<IDamageable>();
                other = entity != null ? entity.EntityName : attacker.name;
            }
            SendCombatLine(false, other, result);
        }

        // Death and the way back are told in the combat log too: there is no death screen, only the countdown here.
        private void OnDied(KillingBlow blow)
        {
            if (blow.Attacker == null)
            {
                deferredCombat.Add("You have been defeated.");
                return;
            }
            var killer = blow.Attacker.GetComponent<IDamageable>();
            string killerName = killer != null ? killer.EntityName : blow.Attacker.name;
            deferredCombat.Add(blow.IsCritical ? $"{killerName} defeated you with a critical blow." : $"{killerName} defeated you.");
            if (blow.Attacker.TryGetComponent(out PlayerChat killerChat) && killerChat != this)
                killerChat.deferredCombat.Add(blow.IsCritical ? $"You defeated {SpeakerName} with a critical blow!" : $"You defeated {SpeakerName}.");
        }

        private void OnRespawnCountdown(int seconds) =>
            deferredCombat.Add(seconds == 1 ? "Respawning in 1 second." : $"Respawning in {seconds} seconds.");

        private void LateUpdate()
        {
            if (deferredCombat.Count == 0) return;
            for (int i = 0; i < deferredCombat.Count; i++) ServerCombatNotice(deferredCombat[i]);
            deferredCombat.Clear();
        }

        private void OnRespawned() => ServerCombatNotice("You are back on your feet.");

        private void SendCombatLine(bool outgoing, string other, DamageResult result)
        {
            if (!HasAuthority) return;
            byte flags = (byte)((result.IsCritical ? CriticalBit : 0) | (result.IsEvaded ? EvadedBit : 0)
                | (result.IsReflected ? ReflectedBit : 0) | (result.ShieldShattered ? ShatteredBit : 0));
            if (!IsSpawned) ShowCombatLine(outgoing, other, result.DamageDealt, result.ReflectedDamage, flags);
            else CombatLineRpc(outgoing, other ?? string.Empty, result.DamageDealt, result.ReflectedDamage, flags);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void CombatLineRpc(bool outgoing, string other, float damage, float reflected, byte flags) =>
            ShowCombatLine(outgoing, other, damage, reflected, flags);

        // A miss and an evade are the same roll: the attacker reads it as a miss, the defender as an evade.
        private static void ShowCombatLine(bool outgoing, string other, float damage, float reflected, byte flags)
        {
            bool critical = (flags & CriticalBit) != 0;
            string text;
            if ((flags & EvadedBit) != 0)
                text = outgoing ? $"Your attack missed {other}." : $"You evaded {other}'s attack.";
            else if ((flags & ReflectedBit) != 0)
                text = outgoing ? $"{other} reflected your attack: {reflected:0} damage back to you." : $"You reflected {other}'s attack.";
            else if (outgoing)
                text = critical ? $"You critically hit {other} for {damage:0}." : $"You hit {other} for {damage:0}.";
            else
                text = critical ? $"{other} critically hits you for {damage:0}." : $"{other} hits you for {damage:0}.";

            if ((flags & ShatteredBit) != 0) text += outgoing ? " Their shield shatters." : " Your shield shatters.";
            Show(ChatChannel.Combat, string.Empty, text, false);
        }
    }
}
