using System;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace KryticalNova.GameFeel
{
    /// <summary>Where a buff's looping aura sits on the character.</summary>
    public enum AuraAnchor { Feet, Chest, Head }

    /// <summary>
    /// How one skill looks: its cast burst, its hit burst, (for buffs) the aura that lasts as long as the buff and
    /// (for charged skills) what builds up while the key is held.
    /// </summary>
    [Serializable]
    public sealed class SkillFeel
    {
        public string skillId;
        [Tooltip("Played at the caster on every client when the skill is cast.")]
        public MMF_Player cast;
        [Tooltip("Played on every target the skill damages.")]
        public MMF_Player hit;
        [Tooltip("Looping effect that rides on the character for the buff's whole duration.")]
        public GameObject aura;
        public AuraAnchor auraAnchor = AuraAnchor.Feet;
        [Tooltip("Charged skills: looping effect at the chest while the key is held. It swells as the charge fills.")]
        public GameObject charge;
        [Tooltip("Charged skills: looping effect at the feet while the key is held.")]
        public GameObject chargeGround;
    }

    /// <summary>
    /// Maps skill ids to their effects, so gameplay data never references VFX. Lives on the GameFeel prefab next to
    /// <see cref="GameFeelPresenter"/>; built by GameFeelBuilder and tuned on the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkillFeelCatalog : MonoBehaviour
    {
        [SerializeField] private List<SkillFeel> skills = new List<SkillFeel>();

        private Dictionary<string, SkillFeel> byId;

        public SkillFeel Find(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return null;
            if (byId == null)
            {
                byId = new Dictionary<string, SkillFeel>(skills.Count);
                foreach (var skill in skills)
                    if (skill != null && !string.IsNullOrEmpty(skill.skillId)) byId[skill.skillId] = skill;
            }
            return byId.TryGetValue(skillId, out var found) ? found : null;
        }
    }
}
