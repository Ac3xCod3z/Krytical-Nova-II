using KryticalNova.Networking;
using System;
using System.Collections.Generic;
using KryticalNova.Core;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.NPC;
using KryticalNova.Persistence;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Progression
{
    /// <summary>What was done at the Herald.</summary>
    public enum NovaWorkKind : byte { Ascend, Standing }

    /// <summary>How a request at the Herald ended, as the owner is told.</summary>
    public struct NovaWorkResult : INetworkSerializable
    {
        public NovaWorkKind kind;
        /// <summary>For an Ascension attempt.</summary>
        public AscensionOutcome outcome;
        /// <summary>For a Standing bought.</summary>
        public StandingTrack track;
        /// <summary>The level (Ascension, or the track's) before and after.</summary>
        public byte before, after;
        /// <summary>The attempt reached a new Nova tier.</summary>
        public bool newTier;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref kind);
            serializer.SerializeValue(ref outcome);
            serializer.SerializeValue(ref track);
            serializer.SerializeValue(ref before);
            serializer.SerializeValue(ref after);
            serializer.SerializeValue(ref newTier);
        }
    }

    /// <summary>
    /// A character's two Renown ladders, both worked at a Herald: Ascension (a chance at a level for Renown and
    /// Creds; every level raises the four attributes, every few levels the Nova changes) and Nova Standings (levels
    /// of four tracks bought with Renown; each raises one stat, and one track's name can be worn). The client only
    /// asks; the server checks through <see cref="NovaRules"/>, takes the cost, rolls and saves. The result is
    /// replicated to everyone, and every machine works out the stat bonuses and the Nova from it, the way worn gear
    /// does. Every number is in <see cref="NovaSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNova : NetworkBehaviour, ISaveParticipant
    {
        private const float ReachPadding = 2.5f;
        private const string ReasonDefeated = "You can't do that while defeated.";
        private const string ReasonWhere = "Visit a Herald to do that.";
        private const float PendingSeconds = 1.5f; // how long a moment waits for the Nova it belongs to

        [SerializeField] private NovaSettings settings;

        private readonly NetworkVariable<NovaState> netState = new NetworkVariable<NovaState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerWallet wallet;
        private PlayerCharacterProgression progression;
        private CharacterCombatStats stats;
        private PlayerResources resources;
        private IFactionMember member;
        private ISaveScheduler saveAgent;
        private NovaHalo halo;
        private Transform head;
        private NovaState state;
        // The moment and the new state travel separately: a reaction that arrives first waits for its Nova.
        private NovaWorkResult pendingReaction;
        private float pendingUntil = -1f;

        // One stable source object so both ladders' bonuses can be replaced in one step.
        private readonly object statSource = new object();
        private readonly List<StatModifier> modifiers = new List<StatModifier>(8);

        /// <summary>GM switch: the Herald's services work anywhere until the session ends.</summary>
        public static bool GmAnywhere;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => GmAnywhere = false;

        /// <summary>The ladders of the player this machine controls.</summary>
        public static PlayerNova Local => LocalPlayer.Get<PlayerNova>();

        /// <summary>Raised on every machine when either ladder, or the Standing worn, changes.</summary>
        public event Action Changed;
        /// <summary>Raised on the owner when the server has finished a request.</summary>
        public event Action<NovaWorkResult> Finished;

        public NovaSettings Settings => settings;
        public NovaState State => state;
        public int Ascension => state.ascension;
        /// <summary>The Nova's tier: 0 with none.</summary>
        public int Tier => settings != null ? NovaRules.Tier(settings.ascension, state.ascension) : 0;
        public FactionId Faction => member != null ? member.Faction : FactionId.None;
        /// <summary>The Standing this character wears ("Vanguard Thane"), or null.</summary>
        public string WornStanding
        {
            get
            {
                if (settings == null) return null;
                // Put together once for each Standing worn: the namecard asks every frame.
                int key = state.TryGetWorn(out StandingTrack track) ? ((int)Faction << 24) | ((int)track << 16) | (state.Standing(track) & 0xFFFF) : -1;
                if (key != wornKey)
                {
                    wornKey = key;
                    wornName = key < 0 ? null : NovaRules.StandingName(settings.standings, Faction, track, state.Standing(track));
                }
                return wornName;
            }
        }

        private int wornKey = int.MinValue;
        private string wornName;
        public long Creds => wallet != null ? wallet.Creds : 0;
        public int Renown => progression != null ? progression.Renown : 0;
        /// <summary>True while a Herald is in reach (or the GM switch is on): the window closes when it stops being so.</summary>
        public bool InReach => GmAnywhere || NpcInteractable.IsOffered(NpcOffer.Herald, transform.position, ReachPadding);
        /// <summary>The Nova being worn, or null while there is none.</summary>
        public NovaHalo Halo => halo;
        /// <summary>The crown of this character's head in the world.</summary>
        public Vector3 Crown => NovaHalo.CrownOf(transform, ref head);
        /// <summary>How far above the crown the Nova reaches (metres): what a namecard has to clear. 0 with no Nova.</summary>
        public float NovaHeight => halo != null ? halo.TopAboveHead : 0f;

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            wallet = GetComponent<PlayerWallet>();
            progression = GetComponent<PlayerCharacterProgression>();
            stats = GetComponent<CharacterCombatStats>();
            resources = GetComponent<PlayerResources>();
            member = GetComponent<IFactionMember>();
            saveAgent = GetComponent<ISaveScheduler>();
        }

        private void OnEnable()
        {
            if (!IsSpawned) Show(state);
        }

        private void OnDisable()
        {
            if (stats != null) stats.SetSourceModifiers(statSource, null);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            if (IsServer) netState.Value = state;
            Show(IsServer ? state : netState.Value);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(NovaState previous, NovaState current)
        {
            if (!IsServer) Show(current);
        }

        /// <summary>Where the game is decided: keeps a change, tells every machine and has it saved.</summary>
        private void Commit(NovaState next)
        {
            state = next;
            if (IsSpawned && IsServer) netState.Value = next;
            Show(next);
            if (saveAgent != null) saveAgent.SaveSoon();
        }

        /// <summary>Every machine: the bonuses and the Nova that follow from the ladders.</summary>
        private void Show(NovaState next)
        {
            state = next;
            modifiers.Clear();
            NovaRules.AddModifiers(settings, state, statSource, modifiers);
            if (stats != null && isActiveAndEnabled) stats.SetSourceModifiers(statSource, modifiers);

            if (!Application.isBatchMode && settings != null)
            {
                // Every player is drawn after the scenery, Nova or none: that is what lets another's Nova hide them.
                KryticalNova.Rendering.ActorDrawOrder.On(gameObject);
                if (halo == null && Tier > 0) halo = NovaHalo.Create(transform);
                if (halo != null) halo.Show(settings.look, Tier);
                if (halo != null && Time.time < pendingUntil && halo.Tier == NovaRules.Tier(settings.ascension, pendingReaction.after))
                {
                    pendingUntil = -1f;
                    halo.React(pendingReaction.outcome, pendingReaction.newTier);
                }
            }
            Changed?.Invoke();
        }

        // ------------------------------------------------------------------ requests (owner)

        /// <summary>One Ascension attempt.</summary>
        public void RequestAscend()
        {
            if (IsSpawned) AscendRpc();
            else ServerAscend();
        }

        /// <summary>Buys the next level of a Standings track.</summary>
        public void RequestBuyStanding(StandingTrack track)
        {
            if (IsSpawned) BuyStandingRpc(track);
            else ServerBuyStanding(track);
        }

        /// <summary>Wears the name of a track the character holds a level on. Free, and needs no Herald.</summary>
        public void RequestWearStanding(StandingTrack track)
        {
            if (IsSpawned) WearStandingRpc(track, true);
            else ServerWearStanding(track, true);
        }

        /// <summary>Wears no Standing.</summary>
        public void RequestWearNoStanding()
        {
            if (IsSpawned) WearStandingRpc(default, false);
            else ServerWearStanding(default, false);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void AscendRpc()
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(AscendRpc))) return;
            ServerAscend();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void BuyStandingRpc(StandingTrack track)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(BuyStandingRpc))) return;
            ServerBuyStanding(track);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void WearStandingRpc(StandingTrack track, bool wear)
        {
            if (!RpcBudget.Allow(this, CallKind.Money, nameof(WearStandingRpc))) return;
            ServerWearStanding(track, wear);
        }

        // ------------------------------------------------------------------ server

        /// <summary>True when this player may use the Herald now; otherwise they are told why not.</summary>
        private bool Ready()
        {
            if (!HasAuthority) return false;
            string blocked = settings == null || progression == null || wallet == null ? NovaRules.ReasonNoSettings
                : resources != null && !resources.IsAlive ? ReasonDefeated
                : !InReach ? ReasonWhere : null;
            if (blocked == null) return true;
            Tell(blocked);
            return false;
        }

        private void ServerAscend()
        {
            if (!Ready()) return;
            AscensionSettings rules = settings.ascension;
            int before = state.ascension;
            string blocked = NovaRules.AscendBlocked(rules, before, progression.Renown, wallet.Creds);
            if (blocked != null) { Tell(blocked); return; }
            if (!progression.TrySpendRenown(rules.renownCost)) { Tell(NovaRules.ReasonNoRenown); return; }
            if (!wallet.ServerSpend(rules.credsCost, CredsSink.Herald))
            {
                progression.AddRenown(rules.renownCost); // nothing was attempted: hand the Renown back
                Tell(NovaRules.ReasonNoCreds);
                return;
            }

            AscensionOutcome outcome = NovaRules.Resolve(rules, before, UnityEngine.Random.value, out int after);
            bool newTier = NovaRules.Tier(rules, after) > NovaRules.Tier(rules, before);
            NovaState next = state;
            next.ascension = (byte)after;
            Commit(next);
            Finish(new NovaWorkResult
            {
                kind = NovaWorkKind.Ascend, outcome = outcome, before = (byte)before, after = (byte)after, newTier = newTier,
            });
        }

        private void ServerBuyStanding(StandingTrack track)
        {
            if (!Ready()) return;
            StandingSettings rules = settings.standings;
            if (!NovaRules.TryGetTrack(rules, track, out _)) return;
            int before = state.Standing(track);
            string blocked = NovaRules.StandingBlocked(rules, before, progression.Renown);
            if (blocked != null) { Tell(blocked); return; }
            if (!progression.TrySpendRenown(NovaRules.StandingCost(rules, before))) { Tell(NovaRules.ReasonNoRenown); return; }

            NovaState next = state.WithStanding(track, before + 1);
            if (!next.TryGetWorn(out _)) next = next.Wearing(track); // the first Standing earned is worn at once
            Commit(next);
            Finish(new NovaWorkResult { kind = NovaWorkKind.Standing, track = track, before = (byte)before, after = (byte)(before + 1) });
        }

        private void ServerWearStanding(StandingTrack track, bool wear)
        {
            if (!HasAuthority || settings == null) return;
            if (!wear)
            {
                if (state.worn != 0) Commit(state.WearingNone());
                return;
            }
            if (state.Standing(track) <= 0) { Tell(NovaRules.ReasonNotHeld); return; }
            NovaState next = state.Wearing(track);
            if (!next.Equals(state)) Commit(next);
        }

        // ------------------------------------------------------------------ telling the owner

        private void Finish(NovaWorkResult result)
        {
            if (IsSpawned)
            {
                FinishedRpc(result);
                ShownRpc(result);
            }
            else
            {
                KryticalNova.Diagnostics.PerformanceLog.Mark("nova.work");
                Finished?.Invoke(result);
                Show(result, true);
            }
        }

        /// <summary>The owner alone is told the outcome in words.</summary>
        [Rpc(SendTo.Owner)]
        private void FinishedRpc(NovaWorkResult result)
        {
            KryticalNova.Diagnostics.PerformanceLog.Mark("nova.work");
            Finished?.Invoke(result);
        }

        /// <summary>Everyone sees the moment on the character.</summary>
        [Rpc(SendTo.Everyone)]
        private void ShownRpc(NovaWorkResult result) => Show(result, IsOwner);

        private void Show(NovaWorkResult result, bool isLocalPlayer)
        {
            if (settings == null) return;
            bool standing = result.kind == NovaWorkKind.Standing;
            // The tier the moment belongs to: the one just reached, or the one a slip was held in.
            int tier = NovaRules.Tier(settings.ascension, standing ? state.ascension : result.after);
            if (!standing) React(result, tier);

            NovaLookSettings look = settings.look;
            ClientPresentationRegistry.Feel?.NovaWorked(new NovaFeel
            {
                Actor = transform,
                // A Standing bought reads as a success; an Ascension attempt as what it was.
                Outcome = standing ? (int)AscensionOutcome.Rise : (int)result.outcome,
                NewTier = !standing && result.newTier,
                IsStanding = standing,
                Colour = standing ? look.standingColour : look.ColourOf(tier),
                NovaPoint = Crown + Vector3.up * look.CentreAboveHead(tier),
                IsLocalPlayer = isLocalPlayer,
            });
        }

        /// <summary>The Nova itself answers the attempt, now or as soon as it shows the tier the attempt ended in.</summary>
        private void React(NovaWorkResult result, int tier)
        {
            if (tier <= 0) return;
            if (halo != null && halo.Tier == tier)
            {
                pendingUntil = -1f;
                halo.React(result.outcome, result.newTier);
                return;
            }
            pendingReaction = result;
            pendingUntil = Time.time + PendingSeconds;
        }

        private void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (IsSpawned && !IsOwnedByServer) TellRpc(text);
            else ClientPresentationRegistry.Messages?.ShowMessage(text);
        }

        [Rpc(SendTo.Owner)]
        private void TellRpc(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);

        // ------------------------------------------------------------------ GM

        /// <summary>GM: sets the Ascension level outright. Server only.</summary>
        public bool ServerSetAscension(int level)
        {
            if (!HasAuthority || settings == null) return false;
            AscensionSettings rules = settings.ascension;
            int before = state.ascension;
            NovaState next = state;
            next.ascension = (byte)Mathf.Clamp(level, 0, rules.maxLevel);
            Commit(next);
            // A rise is shown as one won at the Herald would be, so the moment of every tier can be looked at.
            if (next.ascension > before)
                Finish(new NovaWorkResult
                {
                    kind = NovaWorkKind.Ascend, outcome = AscensionOutcome.Rise, before = (byte)before, after = next.ascension,
                    newTier = NovaRules.Tier(rules, next.ascension) > NovaRules.Tier(rules, before),
                });
            return true;
        }

        /// <summary>GM: sets the level held on a Standings track outright. Server only.</summary>
        public bool ServerSetStanding(StandingTrack track, int level)
        {
            if (!HasAuthority || settings == null) return false;
            int before = state.Standing(track);
            NovaState next = state.WithStanding(track, Mathf.Clamp(level, 0, settings.standings.MaxLevel));
            if (!next.TryGetWorn(out _))
            {
                next = next.WearingNone();
                for (int i = 0; i < NovaRules.TrackCount; i++)
                {
                    if (next.Standing((StandingTrack)i) <= 0) continue;
                    next = next.Wearing((StandingTrack)i);
                    break;
                }
            }
            Commit(next);
            int after = next.Standing(track);
            if (after > before)
                Finish(new NovaWorkResult { kind = NovaWorkKind.Standing, track = track, before = (byte)before, after = (byte)after });
            return true;
        }

        /// <summary>GM: wears a track's name (the character must hold a rank on it), or none. Server only.</summary>
        public bool ServerSetWorn(StandingTrack track, bool wear)
        {
            if (!HasAuthority || settings == null) return false;
            if (wear && state.Standing(track) <= 0) return false;
            NovaState next = wear ? state.Wearing(track) : state.WearingNone();
            if (!next.Equals(state)) Commit(next);
            return true;
        }

        // ------------------------------------------------------------------ save

        [Serializable]
        private sealed class NovaSave
        {
            public int ascension;
            public int[] standings = new int[NovaRules.TrackCount];
            public int worn;
        }

        public string SaveKey => "nova";
        public SaveScope SaveScope => SaveScope.Character;

        public string CaptureSave()
        {
            var save = new NovaSave { ascension = state.ascension, worn = state.worn };
            for (int i = 0; i < NovaRules.TrackCount; i++) save.standings[i] = state.Standing((StandingTrack)i);
            return JsonUtility.ToJson(save);
        }

        /// <summary>A character saved before the ladders existed has nothing here and starts at +0 with no Standing.</summary>
        public void RestoreSave(string json)
        {
            if (!HasAuthority || string.IsNullOrEmpty(json)) return;
            NovaSave save = JsonUtility.FromJson<NovaSave>(json);
            if (save == null) return;
            int maxAscension = settings != null ? settings.ascension.maxLevel : byte.MaxValue;
            int maxStanding = settings != null ? settings.standings.MaxLevel : byte.MaxValue;
            var next = new NovaState { ascension = (byte)Mathf.Clamp(save.ascension, 0, maxAscension) };
            for (int i = 0; i < NovaRules.TrackCount && save.standings != null && i < save.standings.Length; i++)
                next = next.WithStanding((StandingTrack)i, Mathf.Clamp(save.standings[i], 0, maxStanding));
            next.worn = (byte)Mathf.Clamp(save.worn, 0, NovaRules.TrackCount);
            if (!next.TryGetWorn(out _)) next.worn = 0;

            state = next;
            if (IsSpawned && IsServer) netState.Value = next;
            Show(next);
        }
    }
}
