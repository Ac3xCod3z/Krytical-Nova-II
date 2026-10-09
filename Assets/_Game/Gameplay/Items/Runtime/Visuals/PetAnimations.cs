using System;
using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// The animations of a pet model that has an Animator. The controller loops its resting state by itself; every
    /// trigger it has is one animation that plays once and blends back, named after the trigger. Nothing here is
    /// written per pet: a model with other triggers has other animations. They are played in turn, one every so many
    /// seconds, and any of them can be played on demand.
    /// </summary>
    public sealed class PetAnimations
    {
        private readonly Animator animator;
        private readonly List<string> names = new List<string>();
        private readonly List<int> hashes = new List<int>();
        private bool read;
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

        /// <summary>Once a frame: plays the next animation in turn every <paramref name="cycleSeconds"/> (0 = never).</summary>
        public void Tick(float cycleSeconds)
        {
            if (cycleSeconds <= 0f || animator == null || !animator.isActiveAndEnabled) return;
            Read();
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
