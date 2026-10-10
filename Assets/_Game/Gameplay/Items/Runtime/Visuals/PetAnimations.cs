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
    /// than at a run.
    /// </summary>
    public sealed class PetAnimations
    {
        private static readonly int WalkParameter = Animator.StringToHash("Walk");
        private static readonly int RunParameter = Animator.StringToHash("Run");

        private readonly Animator animator;
        private readonly List<string> names = new List<string>();
        private readonly List<int> hashes = new List<int>();
        private bool read;
        private bool hasWalk;
        private bool hasRun;
        private readonly List<int> bools = new List<int>();
        private int moveLoop;
        private float moveLoopWalkSpeed = 1f;
        private float moveLoopRunSpeed = 1f;
        private PetGait gait;
        private int next;
        private float nextAt = -1f;

        private PetAnimations(Animator animator) => this.animator = animator;

        /// <summary>The animations of a pet model, or null for a model that has no Animator (the drones).</summary>
        public static PetAnimations For(GameObject pet)
        {
            var animator = pet != null ? pet.GetComponentInChildren<Animator>(true) : null;
            if (animator == null || animator.runtimeAnimatorController == null) return null;
            animator.applyRootMotion = false; // where the pet is belongs to its follower
            return new PetAnimations(animator);
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
        }

        /// <summary>The animation of that name, whatever its capitals; -1 when the pet has none.</summary>
        public int IndexOf(string name)
        {
            Read();
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>Plays one animation now (<paramref name="hash"/> = Animator.StringToHash of its name). The turn-taking waits a full interval after it.</summary>
        public bool Play(int hash)
        {
            Read();
            int index = hashes.IndexOf(hash);
            if (index < 0 || !animator.isActiveAndEnabled) return false;
            foreach (int other in hashes) animator.ResetTrigger(other);
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
                return;
            }
            if (wanted == PetGait.Run && !hasRun) wanted = PetGait.Walk;
            if (wanted == PetGait.Walk && !hasWalk) wanted = PetGait.Rest;
            gait = wanted;
            if (hasWalk) animator.SetBool(WalkParameter, gait == PetGait.Walk);
            if (hasRun) animator.SetBool(RunParameter, gait == PetGait.Run);
        }

        /// <summary>
        /// Once a frame: plays the next animation in turn every <paramref name="cycleSeconds"/> (0 = never). Only a
        /// pet at rest takes its turn; one that has been walking waits a full interval after it stops.
        /// </summary>
        public void Tick(float cycleSeconds)
        {
            if (cycleSeconds <= 0f || animator == null || !animator.isActiveAndEnabled) return;
            Read();
            if (gait != PetGait.Rest)
            {
                nextAt = -1f;
                return;
            }
            if (hashes.Count == 0) return;
            if (nextAt < 0f)
            {
                nextAt = Time.time + cycleSeconds;
                return;
            }
            if (Time.time < nextAt) return;
            int index = next % hashes.Count;
            next = index + 1;
            Play(hashes[index]);
        }
    }
}
