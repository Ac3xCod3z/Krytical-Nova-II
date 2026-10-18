using System.Collections.Generic;
using System.Linq;
using System.Text;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Every class's attack of the same level and kind (single hit, two-hit, three-hit, area) hits at the same moments
    /// and locks the caster for the same time, so no class has an edge from its animations (owner, 17 Oct 2026). Each
    /// group takes the whole timing of the class in its middle (by the moment of its last hit). Every other class's
    /// clip is made to fit it: its speed from the spacing of its first and last impact frames, kept between 0.7x and
    /// 1.5x (owner's pick: trim + speed); what speed cannot do, a trimmed wind-up does for a clip too slow and a short
    /// hold in the ready pose does for one too quick. The hits are then placed on the shared moments exactly. Run again
    /// after the skills are rebuilt (Build Class Skills, Build Two-Hit Skills); a second run changes nothing.
    /// </summary>
    public static class StrikeTimingEqualizer
    {
        private const float MinSpeed = 0.7f;
        private const float MaxSpeed = 1.5f;

        [MenuItem("Krytical Nova/Progression/Equalize Strike Timings")]
        public static void EqualizeMenu() => Debug.Log("[Timing] " + Equalize());

        /// <summary>Applies the shared timings to every class attack; returns a table of the result.</summary>
        public static string Equalize()
        {
            var skills = new List<SkillDefinitionSO>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(SkillDefinitionSO)))
            {
                var skill = AssetDatabase.LoadAssetAtPath<SkillDefinitionSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (skill != null && skill.IsClassSkill && skill.IsAttack && skill.castClip != null) skills.Add(skill);
            }

            var report = new StringBuilder();
            foreach (var group in skills.GroupBy(s => (s.requiredCharacterLevel, SkillKinds.Of(s))).OrderBy(g => g.Key.requiredCharacterLevel).ThenBy(g => g.Key.Item2))
            {
                // The class in the middle of the group, by when its last hit lands, gives the group its timing.
                List<SkillDefinitionSO> sorted = group.OrderBy(s => s.GetHitDelay(s.hitCount - 1)).ThenBy(s => s.skillId).ToList();
                SkillDefinitionSO reference = sorted[sorted.Count / 2];
                float[] target = Enumerable.Range(0, reference.hitCount).Select(reference.GetHitDelay).ToArray();
                float lockSeconds = reference.CastLockDuration;

                foreach (SkillDefinitionSO skill in sorted)
                {
                    if (skill.hitCount != target.Length)
                    {
                        Debug.LogWarning($"[Timing] {skill.skillId} has {skill.hitCount} hits, its group {target.Length}: left as it is.", skill);
                        continue;
                    }
                    Fit(skill, target, lockSeconds);
                }
                report.Append($"\nL{group.Key.requiredCharacterLevel} {group.Key.Item2}: hits {string.Join("/", target.Select(t => t.ToString("0.00")))} s, lock {lockSeconds:0.00} s (from {reference.skillId})");
            }
            AssetDatabase.SaveAssets();
            return $"{skills.Count} class attacks fitted to their group's timing:" + report;
        }

        private static void Fit(SkillDefinitionSO skill, float[] target, float lockSeconds)
        {
            float length = skill.castClip.length;
            int last = target.Length - 1;
            // Where each hit lands in the clip's own time: the impact frames the hit times were set on.
            float[] inClip = new float[target.Length];
            for (int i = 0; i < target.Length; i++)
                inClip[i] = HitFraction(skill, i) * length;

            float speed = last > 0 && target[last] > target[0] + 0.01f
                ? (inClip[last] - inClip[0]) / (target[last] - target[0])
                : inClip[last] / Mathf.Max(0.01f, target[last]);
            speed = Mathf.Clamp(speed, MinSpeed, MaxSpeed);

            float trim = inClip[last] - target[last] * speed;
            float delay = 0f;
            if (trim < 0f)
            {
                delay = -trim / speed;
                trim = 0f;
            }

            var fractions = new float[target.Length];
            for (int i = 0; i < target.Length; i++)
                fractions[i] = Mathf.Clamp01((trim + Mathf.Max(0f, target[i] - delay) * speed) / length);

            Undo.RecordObject(skill, "Equalize strike timings");
            skill.castClipSpeed = speed;
            skill.castClipTrim = trim;
            skill.castClipDelay = delay;
            skill.hitTimesNormalized = fractions;
            skill.castLockSeconds = lockSeconds;
            EditorUtility.SetDirty(skill);
        }

        private static float HitFraction(SkillDefinitionSO skill, int index) =>
            skill.hitTimesNormalized != null && index < skill.hitTimesNormalized.Length
                ? Mathf.Clamp01(skill.hitTimesNormalized[index])
                : (index + 1f) / (Mathf.Max(1, skill.hitCount) + 1f);
    }
}
