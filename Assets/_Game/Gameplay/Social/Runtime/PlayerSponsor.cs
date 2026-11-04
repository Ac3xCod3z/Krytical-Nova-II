using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Persistence;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// A player's side of Sponsor / Recruit: asking, answering and ending a link, the XP bonus a Recruit hunts with
    /// while its Sponsor is near, and the Sponsor points a Sponsor earns for being there. Everything is a request;
    /// <see cref="SponsorService"/> on the server keeps the links. The owner is sent its own links whenever they
    /// change, so the window only shows. Every number is in <see cref="SponsorSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerSponsor : NetworkBehaviour, ISaveParticipant, IBodySponsor, IServerLeaving
    {
        public const string SponsorSaveKey = "sponsor";
        private const string PromptId = "sponsor";
        public const string PromptHeading = "SPONSOR REQUEST";

        [SerializeField] private SponsorSettings settings;

        private readonly NetworkVariable<bool> netBoostOn = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        private PlayerIdentity identity;
        private PlayerCharacterProgression progression;
        private PlayerResources resources;
        private PlayerChat chat;
        private PlayerSaveAgent saveAgent;
        private bool arrived;
        private float nextCheck;
        private int nearMask;

        // Server: the request this player is being asked to answer.
        private PlayerSponsor pendingFrom;
        private bool pendingFromSponsors;
        private float pendingExpires;

        // Saved: what a Sponsor has earned, and the time near a Recruit not yet paid for.
        private int points;
        private float nearSeconds;

        // Owner only: what the server sent last.
        private SponsorLinkView[] lines = Array.Empty<SponsorLinkView>();
        private int shownPoints;

        // The entity netcode (M8 8.4.4): its bodies are never network-spawned. The server body keeps the bonus in a field (NGO's netBoostOn,
        // read by the owner only); the owner's view shows what the server sent.
        private bool serverBoost;
        private bool shownBoost;
        private bool EntityView => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ClientView };
        private bool EntityServer => BodyRoleTag.Of(this) is { Kind: NetRoleKind.ServerBody };
        private bool Decides => (IsSpawned && IsServer) || EntityServer;
        private IBodyRequests Requests => BodyRoleTag.RequestsOf(this) is IBodyRequests requests ? requests : null;
        // NGO's IsSpawned on another player meant "still in the game"; a server body of the entity netcode is that while it is awake.
        private static bool Present(PlayerSponsor other) =>
            other != null && (other.IsSpawned || BodyRoleTag.Of(other) is { Kind: NetRoleKind.ServerBody } && other.isActiveAndEnabled);
        private bool BoostState => EntityServer ? serverBoost : netBoostOn.Value;
        private static readonly List<SponsorLink> recruitScratch = new List<SponsorLink>(4);
        private static readonly List<SponsorLink> togetherScratch = new List<SponsorLink>(4);

        /// <summary>Raised on the owner's machine when its links, its points or its bonus change.</summary>
        public event Action Changed;

        /// <summary>This machine's own player, or null.</summary>
        public static PlayerSponsor Local => LocalPlayer.Get<PlayerSponsor>();

        public SponsorSettings Settings => settings;
        public string CharacterId => identity != null ? identity.CharacterId : string.Empty;
        public string SponsorName => identity != null ? identity.CharacterName : name;
        public FactionId Faction => identity != null ? identity.Faction : FactionId.None;
        public int Level => progression != null ? progression.CurrentLevel : 1;
        public bool IsAlive => resources == null || resources.IsAlive;

        /// <summary>The owner's Sponsor and Recruits, as last told by the server.</summary>
        public IReadOnlyList<SponsorLinkView> Lines => lines;
        /// <summary>The owner's Sponsor points, as last told by the server.</summary>
        public int Points => EntityView || (IsSpawned && !IsServer) ? shownPoints : points;
        /// <summary>True on the owner while its Sponsor is near: kills pay the bonus.</summary>
        public bool BoostOn => EntityView ? shownBoost : BoostState;
        /// <summary>Server: true while a request is waiting for this player's answer.</summary>
        public bool HasPendingRequest => pendingFrom != null && Time.time < pendingExpires;

        private void Awake()
        {
            identity = GetComponent<PlayerIdentity>();
            progression = GetComponent<PlayerCharacterProgression>();
            resources = GetComponent<PlayerResources>();
            chat = GetComponent<PlayerChat>();
            saveAgent = GetComponent<PlayerSaveAgent>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netBoostOn.OnValueChanged += OnBoostChanged;
            if (!IsServer || saveAgent == null) return;
            saveAgent.Loaded += OnCharacterLoaded;
            if (saveAgent.IsLoaded) OnCharacterLoaded();
        }

        public override void OnNetworkDespawn()
        {
            netBoostOn.OnValueChanged -= OnBoostChanged;
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            if (IsServer && arrived)
            {
                arrived = false;
                SponsorService.ServerLeft(this);
            }
            lines = Array.Empty<SponsorLinkView>();
            base.OnNetworkDespawn();
        }

        private void OnCharacterLoaded()
        {
            if (arrived) return;
            arrived = true;
            SponsorService.ServerArrived(this);
        }

        private void OnBoostChanged(bool previous, bool next) => Changed?.Invoke();

        // ------------------------------------------------------------------ the entity netcode (M8 8.4.4)

        // The server body arrives on load and leaves before its save, as NGO's server did on spawn and despawn.
        private void Start()
        {
            if (!EntityServer || saveAgent == null) return;
            saveAgent.Loaded += OnCharacterLoaded;
            if (saveAgent.IsLoaded) OnCharacterLoaded();
        }

        public void OnServerLeaving()
        {
            if (!EntityServer || !arrived) return;
            arrived = false;
            SponsorService.ServerLeft(this);
        }

        public override void OnDestroy()
        {
            if (saveAgent != null) saveAgent.Loaded -= OnCharacterLoaded;
            base.OnDestroy();
        }

        /// <summary>A server body of the entity netcode runs its owner's request (NGO's RPC bodies, after the Social limit).</summary>
        public void RunSponsorOp(SponsorOpKind op, string a, bool flag)
        {
            if (!EntityServer) return;
            switch (op)
            {
                case SponsorOpKind.RequestByName: if (!string.IsNullOrWhiteSpace(a)) ServerRequestByName(a.Trim()); break;
                case SponsorOpKind.Respond: ServerRespond(flag); break;
                case SponsorOpKind.End: if (!string.IsNullOrEmpty(a)) ServerEndLink(a); break;
            }
        }

        /// <summary>The entity netcode's own view: the links, points and bonus the server sent (NGO's owner ViewRpc and netBoostOn).</summary>
        public void MirrorView(string viewText, int sponsorPoints, bool boostOn)
        {
            if (!BodyRoleTag.IsLocalClientView(this) || !SponsorViewText.TryDecode(viewText, out SponsorLinkView[] view)) return;
            lines = view;
            shownPoints = sponsorPoints;
            shownBoost = boostOn;
            Changed?.Invoke();
        }

        public void MirrorPrompt(string fromName, bool theyWouldSponsor, float seconds)
        {
            if (BodyRoleTag.IsLocalClientView(this)) ShowPrompt(fromName, theyWouldSponsor, seconds);
        }

        public void MirrorClosePrompt()
        {
            if (BodyRoleTag.IsLocalClientView(this)) ClientPresentationRegistry.Prompts?.Hide(PromptId);
        }

        // ------------------------------------------------------------------ owner -> server

        /// <summary>Ask the player <paramref name="target"/> to be this player's Sponsor or Recruit (their levels decide which).</summary>
        public void Request(PlayerSponsor target)
        {
            if (target != null && Requests is { } requests) { requests.SponsorOp(SponsorOpKind.RequestByName, target.SponsorName, false); return; }
            if (IsSpawned && IsOwner && target != null && target.IsSpawned) RequestRpc(target.NetworkObject);
        }

        /// <summary>The same, of the online character called <paramref name="characterName"/>.</summary>
        public void Request(string characterName)
        {
            if (!string.IsNullOrWhiteSpace(characterName) && Requests is { } requests) { requests.SponsorOp(SponsorOpKind.RequestByName, characterName.Trim(), false); return; }
            if (IsSpawned && IsOwner && !string.IsNullOrWhiteSpace(characterName)) RequestByNameRpc(characterName.Trim());
        }

        public void Respond(bool accept)
        {
            if (Requests is { } requests) { requests.SponsorOp(SponsorOpKind.Respond, string.Empty, accept); return; }
            if (IsSpawned && IsOwner) RespondRpc(accept);
        }

        /// <summary>End the link with the character of this id.</summary>
        public void End(string characterId)
        {
            if (!string.IsNullOrEmpty(characterId) && Requests is { } requests) { requests.SponsorOp(SponsorOpKind.End, characterId, false); return; }
            if (IsSpawned && IsOwner && !string.IsNullOrEmpty(characterId)) EndRpc(characterId);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRpc(NetworkObjectReference targetReference)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RequestRpc))) return;
            PlayerSponsor target = targetReference.TryGet(out NetworkObject targetObject) ? targetObject.GetComponent<PlayerSponsor>() : null;
            ServerRequest(target);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestByNameRpc(string characterName)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RequestByNameRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(RequestByNameRpc), characterName)) return;
            ServerRequestByName(characterName);
        }

        private void ServerRequestByName(string characterName)
        {
            PlayerSponsor target = null;
            if (EntityServer)
            {
                // the entity netcode's server bodies (NGO's ConnectedClientsList never fills there)
                foreach (BodyRoleTag tag in BodyRoleTag.ServerBodies)
                {
                    var candidate = tag != null ? tag.GetComponent<PlayerSponsor>() : null;
                    if (candidate == null || !string.Equals(candidate.SponsorName, characterName, StringComparison.OrdinalIgnoreCase)) continue;
                    target = candidate;
                    break;
                }
            }
            else
                foreach (NetworkClient client in NetworkManager.ConnectedClientsList)
                {
                    var candidate = client.PlayerObject != null ? client.PlayerObject.GetComponent<PlayerSponsor>() : null;
                    if (candidate == null || !string.Equals(candidate.SponsorName, characterName, StringComparison.OrdinalIgnoreCase)) continue;
                    target = candidate;
                    break;
                }
            if (target == null) { Notify($"{characterName} is not online."); return; }
            ServerRequest(target);
        }

        private void ServerRequest(PlayerSponsor target)
        {
            if (target == null) { Notify("That player can't be sponsored."); return; }
            string refusal = SponsorService.ServerRequest(this, target, settings);
            if (refusal != null) Notify(refusal);
            else Tell($"Sponsor request sent to {target.SponsorName}.");
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RespondRpc(bool accept)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(RespondRpc))) return;
            ServerRespond(accept);
        }

        private void ServerRespond(bool accept)
        {
            PlayerSponsor from = pendingFrom;
            bool open = HasPendingRequest && Present(from);
            bool fromSponsors = pendingFromSponsors;
            pendingFrom = null;
            if (EntityServer) SponsorOwnerLink.Current?.ClosePrompt(this);   // its player's prompt (M8 8.4.4)
            else ClosePromptRpc();
            if (!open) { Notify("That request is no longer open."); return; }
            if (!accept)
            {
                from.Tell($"{SponsorName} declined your Sponsor request.");
                return;
            }
            string refusal = SponsorService.ServerAccept(from, this, fromSponsors, settings);
            if (refusal == null) return;
            Notify(refusal);
            from.Notify(refusal);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void EndRpc(string characterId)
        {
            if (!RpcBudget.Allow(this, CallKind.Social, nameof(EndRpc))) return;
            if (!RpcBudget.TextFits(this, nameof(EndRpc), characterId)) return;
            ServerEndLink(characterId);
        }

        private void ServerEndLink(string characterId)
        {
            string refusal = SponsorService.ServerEnd(this, characterId);
            if (refusal != null) Notify(refusal);
        }

        // ------------------------------------------------------------------ server

        /// <summary>Server: puts a request in front of this player.</summary>
        public void ServerOfferRequest(PlayerSponsor from, bool fromSponsors, float seconds)
        {
            if (!Decides) return;
            pendingFrom = from;
            pendingFromSponsors = fromSponsors;
            pendingExpires = Time.time + seconds;
            if (EntityServer) SponsorOwnerLink.Current?.Prompt(this, from.SponsorName, fromSponsors, seconds);
            else PromptRpc(from.SponsorName, fromSponsors, seconds);
        }

        /// <summary>True when the two are close enough for the bonus: the same map, both alive, within range.</summary>
        public bool IsNear(PlayerSponsor other, float rangeMetres)
        {
            if (other == null || other == this || !IsAlive || !other.IsAlive || other.gameObject.scene != gameObject.scene) return false;
            return (other.transform.position - transform.position).sqrMagnitude <= rangeMetres * rangeMetres;
        }

        /// <summary>Server: true while this player is the Sponsor of <paramref name="other"/>.</summary>
        public bool ServerSponsors(PlayerSponsor other) =>
            other != null && other != this && SponsorService.Sponsors(CharacterId, other.CharacterId);

        /// <summary>
        /// Server: the extra share of kill XP this player earns right now for hunting near its Sponsor
        /// (<see cref="SponsorSettings.experienceBonusSteps"/>), or 0. In the Sponsor's Squad it grows with every
        /// other Recruit of that Sponsor who is in the Squad and near the Sponsor too, and
        /// <paramref name="inSponsorSquad"/> says so. Worked out afresh, so a kill never pays on stale news.
        /// </summary>
        public float ServerExperienceBonus(out bool inSponsorSquad)
        {
            inSponsorSquad = false;
            if (!Decides || settings == null) return 0f;
            SponsorLink link = SponsorService.SponsorOf(CharacterId);
            if (link == null) return 0f;
            PlayerSponsor sponsor = SponsorService.Online(link.sponsorId);
            if (sponsor == null || SponsorRules.Outgrown(sponsor.Level, Level, settings.levelGap)) return 0f;
            if (!IsNear(sponsor, settings.rangeMetres)) return 0f;

            int together = 1;
            var squad = GetComponent<PlayerParty>();
            var sponsorSquad = sponsor.GetComponent<PlayerParty>();
            if (squad != null && squad.IsInPartyWith(sponsorSquad))
            {
                inSponsorSquad = true;
                SponsorService.RecruitsOf(link.sponsorId, togetherScratch);
                foreach (SponsorLink other in togetherScratch)
                {
                    PlayerSponsor recruit = SponsorService.Online(other.recruitId);
                    if (recruit == null || recruit == this || !recruit.IsNear(sponsor, settings.rangeMetres)) continue;
                    if (recruit.TryGetComponent(out PlayerParty theirs) && theirs.IsInPartyWith(sponsorSquad)) together++;
                }
            }
            return SponsorRules.Bonus(settings.experienceBonusSteps, together);
        }

        private void Update()
        {
            if (!Decides || !arrived || settings == null || Time.time < nextCheck) return;
            float elapsed = Mathf.Max(1f, settings.checkSeconds);
            nextCheck = Time.time + elapsed;
            ServerCheck(elapsed);
        }

        /// <summary>Who is near whom: the Recruit's bonus goes on and off, a Recruit who caught up is let go, and a Sponsor near a Recruit earns its points.</summary>
        private void ServerCheck(float elapsed)
        {
            string id = CharacterId;
            if (string.IsNullOrEmpty(id)) return;
            int mask = 0;

            // As a Recruit.
            bool boost = false;
            SponsorLink mine = SponsorService.SponsorOf(id);
            if (mine != null)
            {
                PlayerSponsor sponsor = SponsorService.Online(mine.sponsorId);
                mine.recruitLevel = Level;
                if (SponsorRules.Outgrown(sponsor != null ? sponsor.Level : mine.sponsorLevel, Level, settings.levelGap))
                    SponsorService.ServerGraduate(mine, settings.levelGap);
                else
                    boost = sponsor != null && IsNear(sponsor, settings.rangeMetres);
            }
            if (boost) mask |= 1;
            if (boost != BoostState)
            {
                if (EntityServer) serverBoost = boost;   // reaches the owner with the view the mask change sends below
                else netBoostOn.Value = boost;
                Tell(boost
                    ? $"Your Sponsor is near: +{Mathf.RoundToInt(ServerExperienceBonus(out _) * 100f)}% kill XP."
                    : "Your Sponsor is out of reach: the XP bonus is off.");
            }

            // As a Sponsor.
            SponsorService.RecruitsOf(id, recruitScratch);
            bool anyNear = false;
            for (int i = 0; i < recruitScratch.Count; i++)
            {
                PlayerSponsor recruit = SponsorService.Online(recruitScratch[i].recruitId);
                if (recruit == null || !IsNear(recruit, settings.rangeMetres)) continue;
                anyNear = true;
                mask |= 2 << i;
            }
            if (anyNear)
            {
                nearSeconds += elapsed;
                int due = SponsorRules.PointsDue(ref nearSeconds, settings.pointsPerMinute);
                if (due > 0)
                {
                    points = (int)Math.Min(int.MaxValue, (long)points + due);
                    if (saveAgent != null) saveAgent.SaveSoon();
                    mask ^= 1 << 30; // the points changed: the window is told
                }
            }

            if (mask == nearMask) return;
            nearMask = mask & ~(1 << 30);
            ServerPushView();
        }

        /// <summary>GM: sets the Sponsor points. Server only.</summary>
        public void ServerSetPoints(int value)
        {
            if (!Decides) return;
            points = Mathf.Max(0, value);
            if (saveAgent != null) saveAgent.SaveSoon();
            ServerPushView();
        }

        // ------------------------------------------------------------------ server -> owner (used by SponsorService)

        /// <summary>Server: a line in this player's chat log.</summary>
        public void Tell(string text)
        {
            if (chat != null && Decides && !string.IsNullOrEmpty(text)) chat.ServerSystem(text);
        }

        /// <summary>Server: a refusal, shown to this player as a notice.</summary>
        public void Notify(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (EntityServer) { OwnerMessages.TryTell(this, text); return; }   // its player's message line, as NGO's NotifyRpc
            if (!IsSpawned || !IsServer) return;
            if (IsOwnedByServer) ClientPresentationRegistry.Messages?.ShowMessage(text);
            else NotifyRpc(text);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void NotifyRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        /// <summary>Server: sends the owner its links and points as they stand now.</summary>
        public void ServerPushView()
        {
            if (EntityServer && settings != null)
            {
                SponsorOwnerLink.Current?.View(this, SponsorViewText.Encode(SponsorService.ServerView(this, settings.rangeMetres)), points, serverBoost);
                return;
            }
            if (IsSpawned && IsServer && settings != null) ViewRpc(SponsorService.ServerView(this, settings.rangeMetres), points);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ViewRpc(SponsorLinkView[] view, int sponsorPoints)
        {
            lines = view ?? Array.Empty<SponsorLinkView>();
            shownPoints = sponsorPoints;
            Changed?.Invoke();
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void PromptRpc(string fromName, bool theyWouldSponsor, float seconds) => ShowPrompt(fromName, theyWouldSponsor, seconds);

        private void ShowPrompt(string fromName, bool theyWouldSponsor, float seconds) =>
            ClientPresentationRegistry.Prompts?.Show(PromptId, PromptHeading,
                theyWouldSponsor ? $"{fromName} offers to be your Sponsor." : $"{fromName} asks you to be their Sponsor.", seconds, Respond);

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ClosePromptRpc() => ClientPresentationRegistry.Prompts?.Hide(PromptId);

        // ------------------------------------------------------------------ saving

        [Serializable]
        private sealed class SponsorSave
        {
            public int points;
            public float nearSeconds;
        }

        public string SaveKey => SponsorSaveKey;
        public SaveScope SaveScope => SaveScope.Character;

        // The points only: the seconds towards the next point (under a minute) are not kept, so standing near a sponsee does
        // not rewrite this part every minute (online phase 4: byte-sized saves). An older save's seconds are still read.
        public string CaptureSave() => JsonUtility.ToJson(new SponsorSave { points = points });

        public void RestoreSave(string json)
        {
            if ((IsSpawned && !IsServer) || EntityView || string.IsNullOrEmpty(json)) return;   // a view of the entity netcode never loads a save
            var save = JsonUtility.FromJson<SponsorSave>(json);
            if (save == null) return;
            points = Mathf.Max(0, save.points);
            nearSeconds = Mathf.Clamp(save.nearSeconds, 0f, 60f);
        }
    }
}
