using Animancer;
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
        byte warpsSeen;
        bool leaving;
        readonly RaycastHit[] hits = new RaycastHit[8];

        public bool Leaving => leaving;
        public int ShownStage => stage;
        public AnimationClip Playing => playing;

        public static CompanionPresenter Make(MountDefinition mount, MountSettings settings, int stage, int ignoreLayer, Vector3 at, Quaternion rotation, byte warps)
        {
            var go = new GameObject($"Companion ({(mount != null ? mount.displayName : "mount")})");
            var p = go.AddComponent<CompanionPresenter>();
            p.mount = mount;
            p.settings = settings;
            p.groundMask = Physics.DefaultRaycastLayers & ~(1 << ignoreLayer);
            p.warpsSeen = warps;
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
            stage = next;
            if (mount == null) return;
            MountStageLook look = mount.Stage(next);
            if (look == null || look.model == null) return;
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
            AnimancerState state = clip != playing ? animancer.Play(clip, 0.25f) : animancer.States.Current;
            playing = clip;
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
