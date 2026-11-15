using Animancer;
using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Mounts.Entities
{
    /// <summary>
    /// One Mount Companion as a view draws it (day list item 19): the stage model of its <see cref="MountDefinition"/> with the mount's own
    /// idle / walk / run clips at the ghost's speed, its feet snapped to the real ground under the interpolated place (a downward ray, so it
    /// never floats or sinks on a slope), and the mount's appear / vanish look (<see cref="MountMaterialiser"/>) when it is called, warps,
    /// goes home or is knocked out. Presentation only: no rule runs here; made and driven by <see cref="CompanionClientSystem"/>.
    /// </summary>
    public sealed class CompanionPresenter : MonoBehaviour
    {
        MountDefinition mount;
        MountSettings settings;
        int stage = -1, groundMask;
        GameObject model;
        AnimancerComponent animancer;
        MountMaterialiser materialiser;
        AnimationClip playing;
        AnimancerState current;
        byte warpsSeen, strikesSeen, pulsesSeen;
        bool leaving;
        Transform owner;
        bool ownerIsLocal;
        float lungeStart = -1f;
        float tauntStart = -1f;
        byte tauntsSeen;
        // phase 4: a TANK taunt is a rear-up of the model (it rises and tips back, then drops), with the swing feel
        const float TauntSeconds = 0.5f, TauntMetres = 0.45f, TauntDegrees = 18f;
        // phase 3: a DPS strike is a quick lunge of the model toward the Hostile and back (procedural: the mounts have no attack clip)
        const float LungeSeconds = 0.35f, LungeMetres = 0.6f;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public bool Leaving => leaving;
        public int ShownStage => stage;
        public AnimationClip Playing => playing;

        /// <summary>Phase 3 (tests): strikes and heal pulses this view has played.</summary>
        public int StrikesPlayed { get; private set; }
        public int PulsesPlayed { get; private set; }
        /// <summary>Phase 4 (tests): taunts this view has played.</summary>
        public int TauntsPlayed { get; private set; }

        public static CompanionPresenter Make(MountDefinition mount, MountSettings settings, int stage, int ignoreLayer, Vector3 at, Quaternion rotation,
            in CompanionView view, Transform owner, bool ownerIsLocal)
        {
            var go = new GameObject($"Companion ({(mount != null ? mount.displayName : "mount")})");
            var p = go.AddComponent<CompanionPresenter>();
            p.mount = mount;
            p.settings = settings;
            p.groundMask = Physics.DefaultRaycastLayers & ~(1 << ignoreLayer);
            p.warpsSeen = view.Warps;
            p.strikesSeen = view.Strikes;   // a view that first meets it replays no old strike or pulse
            p.pulsesSeen = view.Pulses;
            p.tauntsSeen = view.Taunts;
            p.owner = owner;
            p.ownerIsLocal = ownerIsLocal;
            go.transform.SetPositionAndRotation(p.Grounded(at), rotation);
            p.Build(stage);
            p.Appear();
            return p;
        }

        public void Drive(Vector3 at, Quaternion rotation, in CompanionView view)
        {
            if (leaving) return;
            if (view.Stage != stage) { Build(view.Stage); Appear(); }   // it evolved while out
            transform.SetPositionAndRotation(Grounded(at), rotation);
            if (view.Warps != warpsSeen) { warpsSeen = view.Warps; Appear(); }   // it vanished there and forms here
            var state = (CompanionState)view.State;
            if (state == CompanionState.KnockedOut || state == CompanionState.Leaving) { Vanish(); return; }
            Animate(view.SpeedStep * CompanionView.SpeedStepMetres);
            Fight(view);
        }

        // Phase 3, from the replicated counters only (so every view plays each once, the owner's included): a DPS strike lunges the model
        // and plays the swing's feel (its sound, item 20's grass push); a HEALER pulse plays the heal's feel on the owner; a TANK taunt (phase 4)
        // rears the model up with the swing's feel.
        void Fight(in CompanionView view)
        {
            IGameFeelSink feel = ClientPresentationRegistry.Feel;
            if (view.Strikes != strikesSeen)
            {
                strikesSeen = view.Strikes;
                StrikesPlayed++;
                lungeStart = Time.time;
                feel?.Swing(transform, 3, ownerIsLocal, LungeSeconds);   // the whoosh on the owner's screen, as their own swings
            }
            if (view.Pulses != pulsesSeen)
            {
                pulsesSeen = view.Pulses;
                PulsesPlayed++;
                if (owner != null) feel?.Consumed(owner, true, false);
            }
            if (view.Taunts != tauntsSeen)
            {
                tauntsSeen = view.Taunts;
                TauntsPlayed++;
                tauntStart = Time.time;
                feel?.Swing(transform, 3, ownerIsLocal, TauntSeconds);
            }
            if (model == null) return;
            float t = lungeStart < 0f ? 1f : (Time.time - lungeStart) / LungeSeconds;
            float out01 = t >= 1f ? 0f : Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);   // out and back
            float r = tauntStart < 0f ? 1f : (Time.time - tauntStart) / TauntSeconds;
            float rear01 = r >= 1f ? 0f : Mathf.Sin(Mathf.Clamp01(r) * Mathf.PI);   // up and down
            model.transform.localPosition = Vector3.forward * (out01 * LungeMetres) + Vector3.up * (rear01 * TauntMetres);
            model.transform.localRotation = Quaternion.Euler(-rear01 * TauntDegrees, 0f, 0f);
            if (t >= 1f) lungeStart = -1f;
            if (r >= 1f) tauntStart = -1f;
        }

        /// <summary>The ghost is gone: finish vanishing where it stands, then destroy everything.</summary>
        public void Release()
        {
            if (model != null && materialiser != null && mount != null && mount.materialise != null && !materialiser.IsGone)
            {
                model.transform.SetParent(null, true);
                materialiser.VanishAndDestroy(mount.materialise, VanishSeconds);
                model = null;
            }
            Destroy(gameObject);
        }

        float VanishSeconds => settings != null ? Mathf.Max(0.05f, settings.companionVanishSeconds) : 0.6f;

        void Vanish()
        {
            leaving = true;
            if (animancer != null && mount != null && mount.Stage(stage).idle != null) animancer.Play(mount.Stage(stage).idle, 0.2f);
            if (materialiser != null && mount != null && mount.materialise != null) materialiser.Vanish(mount.materialise, 0f, VanishSeconds, false);
        }

        void Appear()
        {
            if (materialiser != null && mount != null && mount.materialise != null) materialiser.Appear(mount.materialise, 0f, VanishSeconds);
        }

        void Build(int next)
        {
            if (model != null) Destroy(model);
            model = null;
            animancer = null;
            materialiser = null;
            playing = null;
            current = null;
            stage = next;
            if (mount == null) return;
            MountStageLook look = mount.Stage(next);
            if (look.model == null) return;
            model = Instantiate(look.model, transform);
            model.name = $"Mount ({mount.displayName}, {mount.StageName(next)})";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            materialiser = model.AddComponent<MountMaterialiser>();
            Animator animator = model.GetComponentInChildren<Animator>();
            if (animator == null) return;
            animator.applyRootMotion = false;
            animancer = animator.GetComponent<AnimancerComponent>();
            if (animancer == null) animancer = animator.gameObject.AddComponent<AnimancerComponent>();
            animancer.Animator = animator;
        }

        void Animate(float speed)
        {
            if (animancer == null || mount == null) return;
            MountStageLook look = mount.Stage(stage);
            float runAbove = settings != null ? settings.companionRunAbove : 7f;
            AnimationClip clip = speed < 0.1f ? look.idle : speed < runAbove ? look.walk : look.run;
            if (clip == null) clip = look.idle;
            if (clip == null) return;
            if (clip != playing || current == null) current = animancer.Play(clip, 0.25f);
            playing = clip;
            AnimancerState state = current;
            if (state == null) return;
            Vector2 stride = look.clipMetresPerSecond != Vector2.zero ? look.clipMetresPerSecond : settings != null ? settings.clipMetresPerSecond : new Vector2(4f, 14f);
            Vector2 range = settings != null ? settings.clipPlaybackRange : new Vector2(0.7f, 2.4f);
            float perOne = clip == look.walk ? stride.x : clip == look.run ? stride.y : 0f;
            state.Speed = perOne > 0f ? Mathf.Clamp(speed / perOne, range.x, range.y) : 1f;
        }

        // the feet on the real ground under the interpolated place (owner rule: the character is always drawn at the real ground height)
        Vector3 Grounded(Vector3 at)
        {
            int count = Physics.RaycastNonAlloc(at + Vector3.up * 2f, Vector3.down, hits, 6f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                if (model != null && hits[i].collider.transform.IsChildOf(transform)) continue;
                if (hits[i].point.y > best) best = hits[i].point.y;
            }
            return count > 0 && best > float.NegativeInfinity ? new Vector3(at.x, best, at.z) : at;
        }
    }
}
