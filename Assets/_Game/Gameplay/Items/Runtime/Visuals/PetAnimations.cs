using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>How a pet is getting along beside its owner.</summary>
    public enum PetGait
    {
        Rest,
        Walk,
        Run,
    }

    /// <summary>
    /// The animations of a pet model that has an Animator. The controller loops its resting state by itself; every
    /// trigger it has is one animation that plays once and blends back, named after the trigger. Nothing here is
    /// written per pet: a model with other triggers has other animations. They are played in turn, one every so many
    /// seconds, and any of them can be played on demand. A controller with a true/false parameter called Walk or Run
    /// also has that gait: a loop on the spot, held for as long as the parameter is true. A model with a single
    /// movement loop instead (<see cref="SetMoveLoop"/>) holds that one whenever it moves, played slower at a walk
    /// than at a run. A model that should not sit in its default state while it waits (a bird that would be perched
    /// in mid-air) can name a loop to rest in (<see cref="SetRestLoop"/>). While its owner is in a fight a pet is
    /// kept calm (<see cref="SetCalm"/>): it rests and keeps its gait, and plays nothing else.
    /// </summary>
    public sealed class PetAnimations
    {
        private static readonly int WalkParameter = Animator.StringToHash("Walk");
        private static readonly int RunParameter = Animator.StringToHash("Run");

        private readonly Animator animator;
        private readonly List<string> names = new List<string>();
        private readonly List<int> hashes = new List<int>();
        // The animations that take a turn: all of them but those only reached through another.
        private readonly List<int> turns = new List<int>();
        private IReadOnlyList<FollowOn> followOns;
        private int followedTimes;
        private int followedState;
        private bool read;
        private bool hasWalk;
        private bool hasRun;
        private readonly List<int> bools = new List<int>();
        private int moveLoop;
        private float moveLoopWalkSpeed = 1f;
        private float moveLoopRunSpeed = 1f;
        // The loop held at rest, and the animation played once that it is making way for.
        private const float OneShotStartSeconds = 1f;
        private int restLoop;
        private int defaultState;
        private bool oneShot;
        private bool oneShotLeftDefault;
        private float oneShotSince;
        private PetGait gait;
        private int next;
        private float nextAt = -1f;
        // Calm: nothing is played once, by turn or on demand.
        private const float CalmBlendSeconds = 0.2f;
        private bool calm;

        private PetAnimations(Animator animator) => this.animator = animator;

        /// <summary>The animations of a pet model, or null for a model that has no Animator (the drones).</summary>
        public static PetAnimations For(GameObject pet)
        {
            var animator = pet != null ? pet.GetComponentInChildren<Animator>(true) : null;
            if (animator == null || animator.runtimeAnimatorController == null) return null;
            animator.applyRootMotion = false; // where the pet is belongs to its follower
            return new PetAnimations(animator);
        }

        /// <summary>An animation followed by another: the hashes of the two states' names, and how many times the first is played through before the second starts.</summary>
        public readonly struct FollowOn
        {
            public readonly int From, Then, Times;

            public FollowOn(string from, string then, int times)
            {
                From = Animator.StringToHash(from ?? "");
                Then = Animator.StringToHash(then ?? "");
                Times = Mathf.Max(1, times);
            }
        }

        /// <summary>
        /// Puts a model in the first frame of one of its animations, for a model whose file holds some other pose
        /// than the one it should rest in. Done before its Animator has started, that pose is what the Animator
        /// goes back to whenever nothing is playing. False when the model has no such animation.
        /// </summary>
        public static bool Pose(GameObject pet, string animation)
        {
            if (pet == null || string.IsNullOrEmpty(animation)) return false;
            var animator = pet.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                // A clip is named after its state, or after the model's file and its state ("Armature|EyeLook").
                if (clip == null || !(clip.name == animation || clip.name.EndsWith("|" + animation, StringComparison.Ordinal))) continue;
                clip.SampleAnimation(animator.gameObject, 0f);
                return true;
            }
            return false;
        }

        /// <summary>The animations that are followed by another instead of going back to rest. Those that follow take no turn of their own.</summary>
        public void SetFollowOns(IReadOnlyList<FollowOn> list)
        {
            followOns = list;
            ReadTurns();
        }

        private void ReadTurns()
        {
            turns.Clear();
            foreach (int hash in hashes)
            {
                bool follows = false;
                if (followOns != null)
                    foreach (FollowOn each in followOns) follows |= each.Then == hash;
                if (!follows) turns.Add(hash);
            }
        }

        /// <summary>
        /// Once a frame: an animation that is followed by another hands over to it as it ends, instead of going back
        /// to rest. One that is played more than once starts again until it has been through that many times.
        /// </summary>
        public void Continue()
        {
            if (followOns == null || followOns.Count == 0 || calm || animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return;
            AnimatorStateInfo now = animator.GetCurrentAnimatorStateInfo(0);
            bool leaving = animator.IsInTransition(0);
            // Left for anything but rest (the pet moved off, another animation was asked for): the chain is dropped.
            if (leaving && animator.GetNextAnimatorStateInfo(0).shortNameHash != defaultState) return;
            foreach (FollowOn each in followOns)
            {
                if (each.From != now.shortNameHash) continue;
                if (followedState != each.From)
                {
                    followedState = each.From;
                    followedTimes = 0;
                }
                float oneFrame = now.length > 0f ? Time.deltaTime * Mathf.Abs(animator.speed) / now.length : 0f;
                if (!leaving && now.normalizedTime < 1f - oneFrame) return;
                followedTimes++;
                if (followedTimes < each.Times) animator.Play(each.From, 0, 0f);
                else
                {
                    followedState = 0;
                    animator.Play(each.Then, 0, 0f);
                }
                if (HasRestLoop)
                {
                    oneShot = true;
                    oneShotLeftDefault = true;
                    oneShotSince = Time.time;
                }
                nextAt = -1f; // the turn-taking waits a full interval after the whole chain
                return;
            }
            followedState = 0;
        }

        /// <summary>What can be played, by name. Empty until the Animator has started.</summary>
        public IReadOnlyList<string> Names
        {
            get
            {
                Read();
                return names;
            }
        }

        // An Animator only knows its parameters once it has started, which a model that came out hidden has not.
        private void Read()
        {
            if (read || animator == null || !animator.isInitialized) return;
            read = true;
            defaultState = animator.GetCurrentAnimatorStateInfo(0).shortNameHash; // nothing has been asked of it yet
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Bool)
                {
                    hasWalk |= parameter.nameHash == WalkParameter;
                    hasRun |= parameter.nameHash == RunParameter;
                    bools.Add(parameter.nameHash);
                }
                if (parameter.type != AnimatorControllerParameterType.Trigger) continue;
                names.Add(parameter.name);
                hashes.Add(parameter.nameHash);
            }
            ReadTurns();
        }

        /// <summary>
        /// The state the Animator is in (Animator.StringToHash of its name) and how far through it, 0 to 1 (a loop
        /// starts again from 0). While it blends from one state to the next it is the one it is going to. False
        /// while the Animator is not running.
        /// </summary>
        public bool TryGetPlaying(out int state, out float share)
        {
            state = 0;
            share = 0f;
            if (animator == null || !animator.isActiveAndEnabled || !animator.isInitialized) return false;
            AnimatorStateInfo info = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            state = info.shortNameHash;
            share = info.loop ? Mathf.Repeat(info.normalizedTime, 1f) : Mathf.Clamp01(info.normalizedTime);
            return true;
        }

        /// <summary>The animation of that name, whatever its capitals; -1 when the pet has none.</summary>
        public int IndexOf(string name)
        {
            Read();
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>
        /// Calm or not. A calm pet keeps to its resting state and its gait: it takes no turns and refuses an
        /// animation asked of it, and one already under way is cut short. Out of it, the turn-taking starts again
        /// from a full interval.
        /// </summary>
        public void SetCalm(bool on)
        {
            if (calm == on) return;
            calm = on;
            nextAt = -1f;
            if (!on || animator == null || !animator.isActiveAndEnabled) return;
            Read();
            if (!read) return;
            foreach (int hash in hashes) animator.ResetTrigger(hash);
            // Each animation played once is a state named after its trigger: in one of those, or on the way into
            // one, the pet goes back to the state it rests in.
            AnimatorStateInfo now = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
            if (hashes.Contains(now.shortNameHash)) animator.CrossFadeInFixedTime(defaultState, CalmBlendSeconds, 0);
            oneShot = false;
        }

        /// <summary>Plays one animation now (<paramref name="hash"/> = Animator.StringToHash of its name). The turn-taking waits a full interval after it.</summary>
        public bool Play(int hash)
        {
            if (calm) return false;
            Read();
            int index = hashes.IndexOf(hash);
            if (index < 0 || !animator.isActiveAndEnabled) return false;
            foreach (int other in hashes) animator.ResetTrigger(other);
            if (HasRestLoop)
            {
                // The rest loop would take the Animator straight back: it stands down until this one has played.
                animator.SetBool(restLoop, false);
                oneShot = true;
                oneShotLeftDefault = false;
                oneShotSince = Time.time;
            }
            animator.SetTrigger(hash);
            nextAt = -1f;
            return true;
        }

        /// <summary>
        /// The one loop this model moves in, for a model that has no walk and run of its own: the true/false
        /// parameter that holds it (empty = none) and how fast the whole model is played while its owner walks and
        /// while its owner runs (1 = as it was made).
        /// </summary>
        public void SetMoveLoop(string parameter, float walkSpeed, float runSpeed)
        {
            moveLoop = string.IsNullOrEmpty(parameter) ? 0 : Animator.StringToHash(parameter);
            moveLoopWalkSpeed = Mathf.Max(0.01f, walkSpeed);
            moveLoopRunSpeed = Mathf.Max(0.01f, runSpeed);
        }

        /// <summary>
        /// The loop this model waits in instead of its default state: the true/false parameter that holds it
        /// (empty = none, it waits in its default state). It lets go for the movement loop and for an animation
        /// played once, and takes over again when that one is back in the default state.
        /// </summary>
        public void SetRestLoop(string parameter) => restLoop = string.IsNullOrEmpty(parameter) ? 0 : Animator.StringToHash(parameter);

        private bool HasRestLoop => restLoop != 0 && bools.Contains(restLoop);

        /// <summary>Holds the rest loop while the pet is at rest and no animation played once is still on its way through.</summary>
        private void HoldRestLoop()
        {
            if (!HasRestLoop) return;
            if (oneShot)
            {
                bool inDefault = !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == defaultState;
                if (!inDefault) oneShotLeftDefault = true;
                else if (oneShotLeftDefault || Time.time - oneShotSince > OneShotStartSeconds) oneShot = false;
            }
            animator.SetBool(restLoop, gait == PetGait.Rest && !oneShot);
        }

        /// <summary>
        /// Once a frame: the gait the pet should be in. A model without a run walks instead, and one without a walk
        /// rests. Told every frame, because an Animator forgets its parameters whenever the pet is hidden.
        /// </summary>
        public void SetGait(PetGait wanted)
        {
            if (animator == null || !animator.isActiveAndEnabled) return;
            Read();
            if (!read) return;
            if (moveLoop != 0 && bools.Contains(moveLoop))
            {
                gait = wanted;
                animator.SetBool(moveLoop, gait != PetGait.Rest);
                // The speed is the whole Animator's: at rest it goes back to 1, so the other animations play as made.
                animator.speed = gait == PetGait.Walk ? moveLoopWalkSpeed : gait == PetGait.Run ? moveLoopRunSpeed : 1f;
                HoldRestLoop();
                return;
            }
            if (wanted == PetGait.Run && !hasRun) wanted = PetGait.Walk;
            if (wanted == PetGait.Walk && !hasWalk) wanted = PetGait.Rest;
            gait = wanted;
            if (hasWalk) animator.SetBool(WalkParameter, gait == PetGait.Walk);
            if (hasRun) animator.SetBool(RunParameter, gait == PetGait.Run);
            HoldRestLoop();
        }

        /// <summary>
        /// Once a frame: plays the next animation in turn every <paramref name="cycleSeconds"/> (0 = never). Only a
        /// pet at rest takes its turn; one that has been walking waits a full interval after it stops.
        /// </summary>
        public void Tick(float cycleSeconds)
        {
            if (calm || cycleSeconds <= 0f || animator == null || !animator.isActiveAndEnabled) return;
            Read();
            if (gait != PetGait.Rest)
            {
                nextAt = -1f;
                return;
            }
            if (turns.Count == 0) return;
            if (nextAt < 0f)
            {
                nextAt = Time.time + cycleSeconds;
                return;
            }
            if (Time.time < nextAt) return;
            int index = next % turns.Count;
            next = index + 1;
            Play(turns[index]);
        }
    }
}
