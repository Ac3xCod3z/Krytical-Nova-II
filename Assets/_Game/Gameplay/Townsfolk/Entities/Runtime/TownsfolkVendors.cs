using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Items.Visuals;
using KryticalNova.NPC;
using KryticalNova.Player.Entities;
using KryticalNova.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace KryticalNova.Townsfolk
{
    /// <summary>
    /// A vendor noticing the player (plan 3.6, step 7; client only, each player's own game): the head follows the player within a few
    /// metres, a wave or beckon when the player is near and looks its way (a cooldown per vendor and a gap between any two), a show-goods
    /// gesture when the player steps into reach, a welcome when the player opens the shop (<see cref="NpcInteractable.Interacted"/>).
    /// No two vendors move in step (owner, 14 Nov 2026): each has its own dice from its seed; its standing animation starts somewhere
    /// else in its cycle and runs a little faster or slower, every cooldown is rolled a little longer or shorter, a gesture starts a moment
    /// late, and two vendors never start one on the same frame (the numbers are <see cref="TownsfolkSettings"/>' "out of step" rows).
    /// Lives on the vendor's Sidekick body, beside its Animator (Unity sends OnAnimatorIK there); ticked by
    /// <see cref="VendorLooksClientSystem"/>, no Update of its own.
    /// </summary>
    public sealed class VendorPresenter : MonoBehaviour
    {
        static readonly int GestureId = Animator.StringToHash("Gesture");
        static readonly int GestureKindId = Animator.StringToHash("GestureKind");
        static float s_NextAnyWave, s_NextAnyCallout;
        static int s_GestureFrame = -1;
        /// <summary>Gestures started since Play began (a check's count).</summary>
        public static int GesturesStarted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_NextAnyWave = 0f; s_NextAnyCallout = 0f; s_GestureFrame = -1; GesturesStarted = 0; }

        public NpcInteractable Npc;
        public Animator Animator;
        public uint Seed;
        /// <summary>
        /// This vendor's own number: its kind's <see cref="Seed"/> with the place it stands. Two vendors of one kind (Concord has
        /// several of each) share the look, never the timing: with the kind's seed alone they moved in exact step (14 Nov 2026).
        /// </summary>
        public uint OwnSeed;
        public VendorManner Manner;
        public Transform Listener;
        int m_LastCallout = -1, m_LastWelcome = -1;
        float m_NextCallout;
        TownsfolkSettings m_Settings;
        float m_LookWeight, m_MaxWeight = 1f;
        Vector3 m_LookAt;
        float m_NextWave, m_NextShow;
        bool m_WasInReach;
        Unity.Mathematics.Random m_Dice;
        TownGesture m_Queued;
        float m_QueuedAt;

        /// <summary>A cooldown or gap, a little longer or shorter each time (this vendor's own dice).</summary>
        float Rolled(float seconds, TownsfolkSettings s) =>
            seconds * (1f + Mathf.Clamp(s.vendorCooldownJitter, 0f, 0.9f) * m_Dice.NextFloat(-1f, 1f));

        public void Tick(bool hasPlayer, Vector3 player, Vector3 playerForward, TownsfolkSettings s, float now, float dt)
        {
            m_Settings = s;
            Vector3 at = transform.position;
            float d = hasPlayer ? Vector3.Distance(at, player) : float.MaxValue;
            m_MaxWeight = s.vendorHeadWeight;
            m_LookAt = player + Vector3.up * 1.55f;
            m_LookWeight = Mathf.MoveTowards(m_LookWeight, d <= s.vendorHeadFollowRadius ? 1f : 0f, dt * 2f);
            if (m_Queued != TownGesture.None && now >= m_QueuedAt && Gesture(m_Queued)) m_Queued = TownGesture.None;
            if (!hasPlayer || Animator == null || Npc == null) return;

            bool inReach = Npc.InReachOf(player);
            if (inReach && !m_WasInReach && now >= m_NextShow) { Queue(TownGesture.ShowGoods, now, s); m_NextShow = now + Rolled(s.vendorShowGoodsCooldown, s); }
            m_WasInReach = inReach;
            if (inReach || d > s.vendorWaveRadius || now < m_NextWave || now < s_NextAnyWave) return;
            Vector3 toVendor = at - player;
            toVendor.y = 0f;
            playerForward.y = 0f;
            if (toVendor.sqrMagnitude < 0.01f || playerForward.sqrMagnitude < 0.01f) return;
            if (Vector3.Dot(playerForward.normalized, toVendor.normalized) < 0.7f) return;   // only when the player looks this way
            TownGesture greet = Manner != null && Manner.greet != TownGesture.None ? Manner.greet
                : (Seed & 1) == 0 ? TownGesture.Wave : TownGesture.Beckon;
            Queue(greet, now, s);
            m_NextWave = now + Rolled(s.vendorWaveCooldown, s);
            s_NextAnyWave = now + Rolled(s.vendorAnyWaveGap, s);
            // a call-out with the wave, on its own longer cooldown (never a chorus of vendors)
            if (d <= s.vendorCalloutRadius && now >= m_NextCallout && now >= s_NextAnyCallout && Say(Manner != null ? Manner.callouts : null, ref m_LastCallout))
            {
                m_NextCallout = now + Rolled(s.vendorCalloutCooldown, s);
                s_NextAnyCallout = now + Rolled(s.vendorAnyCalloutGap, s);
            }
        }

        CharacterAppearance m_Appearance;
        RuntimeAnimatorController m_Pending;
        float m_Offset;

        /// <summary>
        /// The controller is handed over only when the look is merged (its Rebuilt), so the animator binds once, to the merged mesh
        /// (see <see cref="TownsfolkBody.PendingController"/>; 14 Nov 2026).
        /// </summary>
        public void Bind(CharacterAppearance appearance, RuntimeAnimatorController controller, float offset, TownsfolkSettings settings)
        {
            m_Settings = settings;
            m_Dice = new Unity.Mathematics.Random((OwnSeed != 0 ? OwnSeed : Seed) | 1u);
            m_Appearance = appearance;
            m_Pending = controller;
            m_Offset = offset;
            if (Animator != null) Animator.runtimeAnimatorController = null;
            if (appearance != null) appearance.Rebuilt += Rebuilt;
        }

        void OnDestroy()
        {
            if (m_Appearance != null) m_Appearance.Rebuilt -= Rebuilt;
        }

        void Rebuilt()
        {
            if (Animator == null || m_Pending == null) return;
            Animator.runtimeAnimatorController = m_Pending;
            m_Pending = null;
            Animator.SetInteger(TownsfolkBody.ActivityId, (int)TownActivity.Stand);
            Animator.SetFloat(TownsfolkBody.OffsetId, m_Offset);
            if (m_Settings == null || !Animator.isActiveAndEnabled) return;
            // out of step from the first frame: its own speed, and its standing animation already somewhere in its cycle (a start
            // in seconds would not do: a clip that does not loop restarts when it ends, and all of those would be in step again)
            Animator.speed = 1f + Mathf.Clamp(m_Settings.vendorSpeedJitter, 0f, 0.3f) * m_Dice.NextFloat(-1f, 1f);
            Animator.Update(0f);
            int stand = Animator.IsInTransition(0) ? Animator.GetNextAnimatorStateInfo(0).fullPathHash : Animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
            Animator.Play(stand, 0, m_Dice.NextFloat() * Mathf.Clamp01(m_Settings.vendorStartSpread));
            Animator.Update(0f);
        }

        /// <summary>The gesture starts a moment late (rolled); a newer one replaces one still waiting.</summary>
        void Queue(TownGesture gesture, float now, TownsfolkSettings s)
        {
            m_Queued = gesture;
            m_QueuedAt = now + m_Dice.NextFloat() * Mathf.Max(0f, s.vendorGestureDelay);
        }

        public void Welcome()
        {
            // the answer to the player's own click: at once, or on the next frame when another vendor's gesture took this one
            if (!Gesture(TownGesture.Welcome)) { m_Queued = TownGesture.Welcome; m_QueuedAt = 0f; }
            Say(Manner != null ? Manner.welcome : null, ref m_LastWelcome);
        }

        /// <summary>One of the lines (never the last one again) in a speech bubble beside this vendor; false when it has none or no HUD.</summary>
        bool Say(KryticalNova.NPC.NpcLine[] lines, ref int last)
        {
            KryticalNova.Core.ISpeechBubbleSink speech = KryticalNova.Core.ClientPresentationRegistry.Speech;
            if (speech == null || lines == null || lines.Length == 0 || m_Settings == null) return false;
            int pick = KryticalNova.NPC.NpcLinePicker.Next(lines.Length, last, max => UnityEngine.Random.Range(0, max));
            if (pick < 0 || string.IsNullOrEmpty(lines[pick].text)) return false;
            last = pick;
            string text = lines[pick].text;
            speech.Speak(Npc != null ? Npc.transform : transform, Listener, text, m_Settings.vendorLineSeconds + text.Length * m_Settings.vendorLineSecondsPerChar);
            return true;
        }

        /// <summary>Starts the gesture; false when another vendor's started on this frame (it waits for the next one).</summary>
        bool Gesture(TownGesture gesture)
        {
            if (Animator == null || !Animator.isActiveAndEnabled) return true;   // nothing to wait for
            int frame = Time.frameCount;
            if (frame == s_GestureFrame) return false;
            s_GestureFrame = frame;
            GesturesStarted++;
            Animator.SetInteger(GestureKindId, (int)gesture);
            Animator.SetTrigger(GestureId);
            return true;
        }

        void OnAnimatorIK(int layerIndex)
        {
            if (Animator == null || layerIndex != 0) return;
            Animator.SetLookAtWeight(m_LookWeight * m_MaxWeight, 0.15f, 1f, 0.6f, 0.5f);
            Animator.SetLookAtPosition(m_LookAt);
        }
    }

    /// <summary>
    /// Today's vendors dressed with Sidekick like the crowd (owner 18.1 A, 13 Nov 2026), on each client: every vendor (an
    /// <see cref="NpcInteractable"/>) gets one fixed look, the same on every machine: its definition's name picks a look of the scene's
    /// cast (a faction's home city: only its people, by <see cref="TownsfolkViewAssets.vendorCasts"/>; elsewhere the first cast, Sky
    /// Concord's, which mixes every faction). Its old model is hidden, never removed (the server and the scene keep it as it was: the
    /// black-sky lesson), and a <see cref="VendorPresenter"/> is added. The vendors' own interaction, turning and lines are unchanged.
    /// </summary>
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class VendorLooksClientSystem : SystemBase
    {
        // shared by every client world of the process (an Editor with thin clients): a vendor is dressed and ticked once
        static readonly Dictionary<NpcInteractable, VendorPresenter> s_Dressed = new();
        static readonly List<NpcInteractable> s_Gone = new();
        static int s_TickedFrame = -1;
        static Transform s_Hidden;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Dressed.Clear();
            s_TickedFrame = -1;
            s_Hidden = null;
        }

        TownsfolkViewAssets m_Assets;
        bool m_Looked;
        EntityQuery m_LocalPlayer;
        float m_NextSweep;

        protected override void OnCreate()
        {
            m_LocalPlayer = GetEntityQuery(ComponentType.ReadOnly<PlayerGhostTag>(), ComponentType.ReadOnly<GhostOwnerIsLocal>(), ComponentType.ReadOnly<LocalTransform>());
            NpcInteractable.Interacted += OnInteracted;
        }

        protected override void OnDestroy() => NpcInteractable.Interacted -= OnInteracted;

        static void OnInteracted(NpcInteractable npc, GameObject player)
        {
            if (npc != null && s_Dressed.TryGetValue(npc, out VendorPresenter p) && p != null) p.Welcome();
        }

        protected override void OnUpdate()
        {
            IReadOnlyList<NpcInteractable> npcs = NpcInteractable.Active;
            if (npcs.Count == 0 || s_TickedFrame == UnityEngine.Time.frameCount) return;
            s_TickedFrame = UnityEngine.Time.frameCount;
            if (!m_Looked)
            {
                m_Looked = true;
                m_Assets = Resources.Load<TownsfolkViewAssets>(TownsfolkViewAssets.ResourcePath);
            }
            if (m_Assets == null || m_Assets.body == null || m_Assets.settings == null || m_Assets.database == null) return;

            bool hasPlayer = false;
            Vector3 player = default, forward = Vector3.forward;
            if (!m_LocalPlayer.IsEmpty)
            {
                using NativeArray<LocalTransform> places = m_LocalPlayer.ToComponentDataArray<LocalTransform>(Allocator.Temp);
                if (places.Length > 0)
                {
                    hasPlayer = true;
                    player = places[0].Position;
                    forward = math.mul(places[0].Rotation, new float3(0f, 0f, 1f));
                }
            }

            float now = UnityEngine.Time.time, dt = UnityEngine.Time.deltaTime;
            for (int i = 0; i < npcs.Count; i++)
            {
                NpcInteractable npc = npcs[i];
                if (npc == null) continue;
                if (!s_Dressed.TryGetValue(npc, out VendorPresenter p))
                {
                    p = Dress(npc);
                    s_Dressed[npc] = p;   // null: it keeps its old model (no look could be made), never tried again
                }
                if (p != null) p.Tick(hasPlayer, player, forward, m_Assets.settings, now, dt);
            }

            if (now < m_NextSweep) return;
            m_NextSweep = now + 2f;
            s_Gone.Clear();
            foreach (KeyValuePair<NpcInteractable, VendorPresenter> pair in s_Dressed) if (pair.Key == null) s_Gone.Add(pair.Key);
            foreach (NpcInteractable gone in s_Gone) s_Dressed.Remove(gone);
        }

        /// <summary>The vendor's fixed look number: from its definition's name (stable on every machine), apart from the crowd's looks.</summary>
        public static int LookOf(string vendorName, int lookBase) =>
            lookBase + (int)(math.hash(new uint2(StableHash(vendorName), 0x9E3779B9u)) % 997u);

        /// <summary>A vendor's own number from its kind's seed and where it stands (to the decimetre: the same on every machine).</summary>
        public static uint OwnSeedOf(uint kindSeed, Vector3 at) =>
            math.hash(new uint2(kindSeed, math.hash(new int3(math.round((float3)at * 10f)))));

        static uint StableHash(string text)
        {
            uint h = 2166136261u;   // FNV-1a: string.GetHashCode differs between runs
            if (text != null) foreach (char c in text) { h ^= c; h *= 16777619u; }
            return h;
        }

        VendorPresenter Dress(NpcInteractable npc)
        {
            string key = npc.Definition != null ? npc.Definition.name : npc.name;
            TownsfolkCast cast = m_Assets.VendorCastOf(npc.gameObject.scene.name);
            int look = LookOf(key, m_Assets.settings.vendorLookBase);
            if (!TownsfolkLooks.Recipe(cast, m_Assets.database, look, out CharacterRecipe recipe, out BodyType bodyType, out _))
            {
                Debug.LogWarning($"[Townsfolk] Vendor {key}: no Sidekick look (cast {(cast != null ? cast.name : "none")}); it keeps its old model.");
                return null;
            }
            if (s_Hidden == null)
            {
                var holder = new GameObject("Vendor looks (unpacking)");
                Object.DontDestroyOnLoad(holder);
                holder.SetActive(false);
                s_Hidden = holder.transform;
            }
            // made under an inactive parent so the player model's own parts never wake up before they are taken off
            GameObject go = Object.Instantiate(m_Assets.body, s_Hidden);
            go.name = "Sidekick look";
            TownsfolkViewClientSystem.Strip(go);
            var look_ = go.GetComponent<CharacterLook>();
            var appearance = go.GetComponent<CharacterAppearance>();
            Animator animator = go.GetComponentInChildren<Animator>(true);
            if (look_ == null || appearance == null || animator == null) { Object.Destroy(go); return null; }
            animator.runtimeAnimatorController = null;   // no binding before the look is merged

            // the old model is hidden, not removed; the new body wakes under the vendor (its merger exists from here) and is dressed there
            foreach (Renderer r in npc.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Animator a in npc.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            go.transform.SetParent(npc.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            CharacterLod.Ensure(go);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            uint seed = StableHash(key);
            var presenter = animator.gameObject.AddComponent<VendorPresenter>();
            presenter.Npc = npc;
            presenter.Animator = animator;
            presenter.Seed = seed;
            uint own = OwnSeedOf(seed, npc.transform.position);
            presenter.OwnSeed = own;
            presenter.Manner = m_Assets.MannerOf(key);
            presenter.Bind(appearance, bodyType == BodyType.Feminine && m_Assets.feminine != null ? m_Assets.feminine : m_Assets.masculine,
                (own >> 8 & 0xFF) / 255f, m_Assets.settings);
            look_.Dressed = cast.dressed;
            if (!look_.Apply(recipe, m_Assets.database)) { Object.Destroy(go); return null; }
            appearance.RebuildNow();   // merged: its Rebuilt hands the animator its controller
            return presenter;
        }
    }
}
