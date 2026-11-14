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
    /// Lives on the vendor's Sidekick body, beside its Animator (Unity sends OnAnimatorIK there); ticked by
    /// <see cref="VendorLooksClientSystem"/>, no Update of its own.
    /// </summary>
    public sealed class VendorPresenter : MonoBehaviour
    {
        static readonly int GestureId = Animator.StringToHash("Gesture");
        static readonly int GestureKindId = Animator.StringToHash("GestureKind");
        static float s_NextAnyWave, s_NextAnyCallout;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { s_NextAnyWave = 0f; s_NextAnyCallout = 0f; }

        public NpcInteractable Npc;
        public Animator Animator;
        public uint Seed;
        public VendorManner Manner;
        public Transform Listener;
        int m_LastCallout = -1, m_LastWelcome = -1;
        float m_NextCallout;
        TownsfolkSettings m_Settings;
        float m_LookWeight, m_MaxWeight = 1f;
        Vector3 m_LookAt;
        float m_NextWave, m_NextShow;
        bool m_WasInReach;

        public void Tick(bool hasPlayer, Vector3 player, Vector3 playerForward, TownsfolkSettings s, float now, float dt)
        {
            m_Settings = s;
            Vector3 at = transform.position;
            float d = hasPlayer ? Vector3.Distance(at, player) : float.MaxValue;
            m_MaxWeight = s.vendorHeadWeight;
            m_LookAt = player + Vector3.up * 1.55f;
            m_LookWeight = Mathf.MoveTowards(m_LookWeight, d <= s.vendorHeadFollowRadius ? 1f : 0f, dt * 2f);
            if (!hasPlayer || Animator == null || Npc == null) return;

            bool inReach = Npc.InReachOf(player);
            if (inReach && !m_WasInReach && now >= m_NextShow) { Gesture(TownGesture.ShowGoods); m_NextShow = now + s.vendorShowGoodsCooldown; }
            m_WasInReach = inReach;
            if (inReach || d > s.vendorWaveRadius || now < m_NextWave || now < s_NextAnyWave) return;
            Vector3 toVendor = at - player;
            toVendor.y = 0f;
            playerForward.y = 0f;
            if (toVendor.sqrMagnitude < 0.01f || playerForward.sqrMagnitude < 0.01f) return;
            if (Vector3.Dot(playerForward.normalized, toVendor.normalized) < 0.7f) return;   // only when the player looks this way
            TownGesture greet = Manner != null && Manner.greet != TownGesture.None ? Manner.greet
                : (Seed & 1) == 0 ? TownGesture.Wave : TownGesture.Beckon;
            Gesture(greet);
            m_NextWave = now + s.vendorWaveCooldown;
            s_NextAnyWave = now + s.vendorAnyWaveGap;
            // a call-out with the wave, on its own longer cooldown (never a chorus of vendors)
            if (d <= s.vendorCalloutRadius && now >= m_NextCallout && now >= s_NextAnyCallout && Say(Manner != null ? Manner.callouts : null, ref m_LastCallout))
            {
                m_NextCallout = now + s.vendorCalloutCooldown;
                s_NextAnyCallout = now + s.vendorAnyCalloutGap;
            }
        }

        public void Welcome()
        {
            Gesture(TownGesture.Welcome);
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

        void Gesture(TownGesture gesture)
        {
            if (Animator == null || !Animator.isActiveAndEnabled) return;
            Animator.SetInteger(GestureKindId, (int)gesture);
            Animator.SetTrigger(GestureId);
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
            look_.Dressed = cast.dressed;
            if (!look_.Apply(recipe, m_Assets.database)) { Object.Destroy(go); return null; }
            appearance.RebuildNow();

            // the old model is hidden, not removed
            foreach (Renderer r in npc.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            foreach (Animator a in npc.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            go.transform.SetParent(npc.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            CharacterLod.Ensure(go);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            animator.runtimeAnimatorController = bodyType == BodyType.Feminine && m_Assets.feminine != null ? m_Assets.feminine : m_Assets.masculine;
            uint seed = StableHash(key);
            animator.SetInteger(TownsfolkBody.ActivityId, (int)TownActivity.Stand);
            animator.SetFloat(TownsfolkBody.OffsetId, (seed >> 8 & 0xFF) / 255f);
            var presenter = animator.gameObject.AddComponent<VendorPresenter>();
            presenter.Npc = npc;
            presenter.Animator = animator;
            presenter.Seed = seed;
            presenter.Manner = m_Assets.MannerOf(key);
            return presenter;
        }
    }
}
