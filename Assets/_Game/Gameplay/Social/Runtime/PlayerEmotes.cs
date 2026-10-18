using KryticalNova.Networking;
using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Player;
using KryticalNova.Progression;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Social
{
    /// <summary>
    /// The emote a character is showing, as every machine knows it. <see cref="serial"/> goes up with every emote
    /// started or ended, so the same emote twice in a row is still a change.
    /// </summary>
    public struct EmoteNetState : INetworkSerializable, IEquatable<EmoteNetState>
    {
        /// <summary>The motion's key (<see cref="EmoteCatalog.Key"/>); empty when nothing is held.</summary>
        public FixedString64Bytes key;
        public byte serial;
        /// <summary>A stance: still going on for someone who arrives later.</summary>
        public bool stance;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref key);
            serializer.SerializeValue(ref serial);
            serializer.SerializeValue(ref stance);
        }

        public bool Equals(EmoteNetState other) => serial == other.serial && stance == other.stance && key.Equals(other.key);
        public override bool Equals(object obj) => obj is EmoteNetState other && Equals(other);
        public override int GetHashCode() => key.GetHashCode() ^ serial;
    }

    /// <summary>
    /// Emotes in the world. The owner picks one on the emote wheel and asks the server; the server checks that the
    /// character is alive, standing still and free, and tells everyone; every machine then plays the motion made for
    /// that character's body, with its prop. A stance is held until the character moves or acts, and someone who
    /// arrives meanwhile sees it held. Which motions exist and how they are sorted is <see cref="EmoteWheelSettings"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerEmotes : NetworkBehaviour
    {
        private const string ReasonDefeated = "You can't do that while defeated.";
        private const string ReasonBusy = "Stand still to do that.";
        private const float StanceCheckSeconds = 0.5f;

        [SerializeField] private EmoteWheelSettings settings;

        private readonly NetworkVariable<EmoteNetState> netState = new NetworkVariable<EmoteNetState>(default,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlayerAnimancerMotor motor;
        private PlayerIdentity identity;
        private PlayerResources resources;
        private EmoteNetState offlineState; // the unspawned avatar keeps its own
        private float nextAllowed;          // where the game is decided
        private bool starting;              // this machine is switching motions: the old one ending is not news
        private float nextStanceCheck;

        /// <summary>The emotes of the player this machine controls.</summary>
        public static PlayerEmotes Local => PlayerInventory.Local != null ? PlayerInventory.Local.GetComponent<PlayerEmotes>() : null;

        public EmoteWheelSettings Settings => settings;
        /// <summary>The body the character was made with: it only performs motions made for that body.</summary>
        public BodyType Body => identity != null ? identity.State.bodyType : BodyType.Masculine;

        private bool HasAuthority => !IsSpawned || IsServer;

        private void Awake()
        {
            motor = GetComponent<PlayerAnimancerMotor>();
            identity = GetComponent<PlayerIdentity>();
            resources = GetComponent<PlayerResources>();
        }

        private void OnEnable()
        {
            if (motor != null) motor.EmoteEnded += OnEmoteEnded;
        }

        private void OnDisable()
        {
            if (motor != null) motor.EmoteEnded -= OnEmoteEnded;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            netState.OnValueChanged += OnNetStateChanged;
            // Arriving late: an emote already played is over, a stance is still being held.
            if (!IsServer && netState.Value.stance && netState.Value.key.Length > 0) Show(netState.Value);
        }

        public override void OnNetworkDespawn()
        {
            netState.OnValueChanged -= OnNetStateChanged;
            base.OnNetworkDespawn();
        }

        private void OnNetStateChanged(EmoteNetState previous, EmoteNetState current) => Show(current);

        /// <summary>
        /// On a machine that only watches: a stance the server still holds is kept showing. A character that has
        /// just appeared here (this machine joined late) reads as moving until it has settled and drops the stance
        /// at once; so does one whose copy here misread a jitter as a step. Where the game is decided the stance
        /// ending is what ends it for everyone (<see cref="OnEmoteEnded"/>), so there is nothing to keep in line.
        /// </summary>
        private void Update()
        {
            if (HasAuthority || Time.time < nextStanceCheck) return;
            nextStanceCheck = Time.time + StanceCheckSeconds;
            EmoteNetState state = netState.Value;
            if (!state.stance || state.key.Length == 0 || motor == null || motor.IsEmoting || !motor.CanStartEmote) return;
            Show(state);
        }

        // ------------------------------------------------------------------ asking (owner)

        /// <summary>Why this character cannot start an emote right now, or null when it can. Checked again on the server.</summary>
        public string Blocked()
        {
            if (resources != null && !resources.IsAlive) return ReasonDefeated;
            if (motor == null || !motor.CanStartEmote) return ReasonBusy;
            return null;
        }

        /// <summary>Performs a motion of the emote wheel. The weapon is put away first.</summary>
        public void RequestPlay(string key)
        {
            if (string.IsNullOrEmpty(key) || settings == null) return;
            string blocked = Blocked();
            if (blocked != null)
            {
                ClientPresentationRegistry.Messages?.ShowMessage(blocked);
                return;
            }
            // Sheathing is the owner's to ask for, so every machine sees it happen the usual way.
            motor.RequestSetWeaponDraw(false);
            if (IsSpawned) PlayRpc(new FixedString64Bytes(key));
            else ServerPlay(key);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void PlayRpc(FixedString64Bytes key)
        {
            if (!RpcBudget.Allow(this, CallKind.Cosmetic, nameof(PlayRpc))) return;
            ServerPlay(key.ToString());
        }

        // ------------------------------------------------------------------ deciding (server)

        private void ServerPlay(string key)
        {
            if (!HasAuthority || settings == null || Blocked() != null) return;
            if (Time.time < nextAllowed) return;
            if (!EmoteCatalog.TryResolve(settings.library, key, out CharacterExpressionLibrary.Motion motion, out bool stance)) return;
            if (!motion.Has(Body) || !EmoteCatalog.IsOffered(settings, motion.group)) return;

            nextAllowed = Time.time + settings.cooldownSeconds;
            EmoteNetState current = IsSpawned ? netState.Value : offlineState;
            Publish(new EmoteNetState { key = new FixedString64Bytes(key), serial = (byte)(current.serial + 1), stance = stance });
        }

        private void Publish(EmoteNetState next)
        {
            if (IsSpawned) netState.Value = next; // the change reaches this machine through OnNetStateChanged too
            else
            {
                offlineState = next;
                Show(next);
            }
        }

        /// <summary>
        /// A motion ended on this machine's copy of the character. Where the game is decided that ends a held stance
        /// for everyone: late arrivals no longer see it, and machines that had not noticed let go of it.
        /// </summary>
        private void OnEmoteEnded()
        {
            if (starting || !HasAuthority) return;
            EmoteNetState current = IsSpawned ? netState.Value : offlineState;
            if (!current.stance || current.key.Length == 0) return;
            Publish(new EmoteNetState { serial = (byte)(current.serial + 1) });
        }

        // ------------------------------------------------------------------ showing (every machine)

        private void Show(EmoteNetState state)
        {
            if (motor == null || settings == null) return;
            if (state.key.Length == 0)
            {
                motor.StopEmote();
                return;
            }
            if (!EmoteCatalog.TryResolve(settings.library, state.key.ToString(), out CharacterExpressionLibrary.Motion motion, out bool stance)) return;
            BodyType body = Body;
            AnimationClip clip = motion.For(body);
            if (clip == null) return; // a body never borrows the other one's performance
            starting = true;
            motor.PlayEmote(clip, stance ? motion.EnterFor(body) : null, stance, motion.prop);
            starting = false;
        }
    }
}
