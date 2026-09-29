using System;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    internal static class BalancePreview
    {
        private const float GraphHeight = 90f;
        private const float GraphInset = 7f;
        private const float MinimumRange = 0.001f;

        internal static void DrawGraph(int count, Func<int, float> valueAt, Color color)
        {
            if (count < 2) return;
            Rect rect = GUILayoutUtility.GetRect(0f, GraphHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.1f, 0.13f, 0.16f));
            float maximum = MinimumRange;
            for (int index = 0; index < count; index++) maximum = Mathf.Max(maximum, valueAt(index));
            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = color;
            Vector3 previous = default;
            for (int index = 0; index < count; index++)
            {
                var point = new Vector3(rect.x + GraphInset + (rect.width - GraphInset * 2f) * index / (count - 1f),
                    rect.yMax - GraphInset - (rect.height - GraphInset * 2f) * valueAt(index) / maximum);
                if (index > 0) Handles.DrawLine(previous, point);
                previous = point;
            }
            Handles.color = previousColor;
            Handles.EndGUI();
        }

        internal static bool ConfirmReplacement(string label)
        {
            return EditorUtility.DisplayDialog("Replace level table?", $"Generate a new {label} table? Existing rows will be replaced. This change supports Undo.", "Generate", "Cancel");
        }
    }

    [CustomEditor(typeof(CharacterProgressionData))]
    public sealed class CharacterProgressionDataEditor : Editor
    {
        private const int DefaultMaximumLevel = 100;
        private const int DefaultStartingExperience = 100;
        private const float DefaultExponent = 1.5f;
        private const int DefaultStatReward = 5;
        private const int DefaultSkillReward = 1;
        private const int MaximumGeneratedLevels = 1000;
        private const float TableHeight = 300f;
        private int maximumLevel = DefaultMaximumLevel;
        private int baseExperience = DefaultStartingExperience;
        private float exponent = DefaultExponent;
        private int statReward = DefaultStatReward;
        private int skillReward = DefaultSkillReward;
        private bool showTable;
        private bool showGenerator;
        private Vector2 tableScroll;

        /// <summary>Draws the XP/reward table, a curve preview, and an undoable table generator.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var data = (CharacterProgressionData)target;
            EditorGUILayout.HelpBox("Shared balance data only. Each row advances the character FROM that level and grants its rewards upon arrival. Player state is not stored here.", MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startingStatPoints"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("startingSkillPoints"));
            EditorGUILayout.LabelField("Maximum character level", data.MaxLevel.ToString());
            BalancePreview.DrawGraph(data.levels?.Length ?? 0, index => data.levels[index].experienceRequired, Color.cyan);
            if (data.levels == null || data.levels.Length == 0)
                EditorGUILayout.HelpBox("No level transitions: characters are capped at level 1.", MessageType.Warning);
            showTable = EditorGUILayout.Foldout(showTable, "XP and reward table", true);
            if (showTable)
            {
                SerializedProperty levels = serializedObject.FindProperty("levels");
                levels.arraySize = Mathf.Clamp(EditorGUILayout.IntField("Transition rows", levels.arraySize), 0, MaximumGeneratedLevels - 1);
                EditorGUILayout.LabelField("From → To        XP required         Stat points         Skill points", EditorStyles.miniBoldLabel);
                tableScroll = EditorGUILayout.BeginScrollView(tableScroll, GUILayout.MaxHeight(TableHeight));
                for (int index = 0; index < levels.arraySize; index++)
                {
                    SerializedProperty row = levels.GetArrayElementAtIndex(index);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"{index + 1} → {index + 2}", GUILayout.Width(65f));
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("experienceRequired"), GUIContent.none);
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("statPointReward"), GUIContent.none);
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("skillPointReward"), GUIContent.none);
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();
            }
            serializedObject.ApplyModifiedProperties();
            showGenerator = EditorGUILayout.Foldout(showGenerator, "Generate / replace table", true);
            if (!showGenerator) return;
            maximumLevel = Mathf.Clamp(EditorGUILayout.IntField("Maximum level", maximumLevel), 1, MaximumGeneratedLevels);
            baseExperience = Mathf.Max(1, EditorGUILayout.IntField("Base XP", baseExperience));
            exponent = Mathf.Max(0f, EditorGUILayout.FloatField("Power exponent", exponent));
            statReward = Mathf.Max(0, EditorGUILayout.IntField("Stat points / level", statReward));
            skillReward = Mathf.Max(0, EditorGUILayout.IntField("Skill points / level", skillReward));
            EditorGUILayout.HelpBox("XP = base XP × current level ^ exponent, clamped to integer limits. Generated rows remain individually editable.", MessageType.None);
            if (!GUILayout.Button("Generate level table") || !BalancePreview.ConfirmReplacement("character progression")) return;
            Undo.RecordObject(data, "Generate progression table");
            data.levels = new CharacterLevelStep[maximumLevel - 1];
            for (int index = 0; index < data.levels.Length; index++)
                data.levels[index] = new CharacterLevelStep
                {
                    experienceRequired = (int)Math.Min(int.MaxValue, Math.Max(1d, Math.Round(baseExperience * Math.Pow(index + 1d, exponent)))),
                    statPointReward = statReward,
                    skillPointReward = skillReward
                };
            EditorUtility.SetDirty(data);
        }
    }

    [CustomEditor(typeof(MovementSkillData))]
    public sealed class MovementSkillDataEditor : Editor
    {
        private const int DefaultRankCount = 20;
        private const int MaximumGeneratedRanks = 200;
        private const float DefaultRunSpeed = 7f;
        private const float DefaultSpeedIncrement = 0.5f;
        private const float DefaultHeight = 1.5f;
        private const float DefaultHeightIncrement = 0.2f;
        private const float DefaultDistance = 2f;
        private const float DefaultDistanceIncrement = 0.3f;
        private const float TableHeight = 320f;
        private int rankCount = DefaultRankCount;
        private int levelStep = 2;
        private int pointCost = 1;
        private float chiCost = 5f;
        private float startingSpeed = DefaultRunSpeed;
        private float speedIncrement = DefaultSpeedIncrement;
        private float startingHeight = DefaultHeight;
        private float heightIncrement = DefaultHeightIncrement;
        private float startingDistance = DefaultDistance;
        private float distanceIncrement = DefaultDistanceIncrement;
        private bool showGenerator;
        private Vector2 tableScroll;

        /// <summary>Draws skill rank columns, validation warnings, graphs, and an undoable generator.</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var data = (MovementSkillData)target;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("skillId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("description"));
            bool isRun = data.skillId == MovementSkillId.Run;
            EditorGUILayout.HelpBox("Chi is charged per accepted destination. Skill selection is free; invalid targets never spend Chi. Jump height sets the jump apex height and maximum landing elevation difference.", MessageType.Info);
            EditorGUILayout.LabelField("Maximum rank", data.MaxRank.ToString());
            BalancePreview.DrawGraph(data.MaxRank, index => isRun ? data.ranks[index].runSpeed : data.ranks[index].jumpDistance, Color.green);
            if (data.MaxRank == 0) EditorGUILayout.HelpBox("This skill has no purchasable ranks.", MessageType.Warning);
            for (int index = 1; index < data.MaxRank; index++)
            {
                var previous = data.ranks[index - 1];
                var current = data.ranks[index];
                if (current.requiredCharacterLevel < previous.requiredCharacterLevel
                    || (isRun ? current.runSpeed < previous.runSpeed
                        : current.jumpHeight < previous.jumpHeight || current.jumpDistance < previous.jumpDistance))
                {
                    EditorGUILayout.HelpBox($"Rank {index + 1} decreases an effect or character-level requirement. Verify that this is intentional.", MessageType.Warning);
                    break;
                }
            }
            SerializedProperty ranks = serializedObject.FindProperty("ranks");
            ranks.arraySize = Mathf.Clamp(EditorGUILayout.IntField("Rank count", ranks.arraySize), 0, MaximumGeneratedRanks);
            EditorGUILayout.LabelField(isRun ? "Rank     Level req.    SP cost    Chi/click    Speed" : "Rank    Level req.    SP cost    Chi/jump    Height    Distance", EditorStyles.miniBoldLabel);
            tableScroll = EditorGUILayout.BeginScrollView(tableScroll, GUILayout.MaxHeight(TableHeight));
            for (int index = 0; index < ranks.arraySize; index++)
            {
                SerializedProperty row = ranks.GetArrayElementAtIndex(index);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField((index + 1).ToString(), GUILayout.Width(32f));
                EditorGUILayout.PropertyField(row.FindPropertyRelative("requiredCharacterLevel"), GUIContent.none);
                EditorGUILayout.PropertyField(row.FindPropertyRelative("skillPointCost"), GUIContent.none);
                EditorGUILayout.PropertyField(row.FindPropertyRelative("chiCost"), GUIContent.none);
                if (isRun) EditorGUILayout.PropertyField(row.FindPropertyRelative("runSpeed"), GUIContent.none);
                else
                {
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("jumpHeight"), GUIContent.none);
                    EditorGUILayout.PropertyField(row.FindPropertyRelative("jumpDistance"), GUIContent.none);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            serializedObject.ApplyModifiedProperties();
            showGenerator = EditorGUILayout.Foldout(showGenerator, "Generate / replace ranks", true);
            if (!showGenerator) return;
            rankCount = Mathf.Clamp(EditorGUILayout.IntField("Ranks", rankCount), 1, MaximumGeneratedRanks);
            levelStep = Mathf.Max(0, EditorGUILayout.IntField("Character levels per rank", levelStep));
            pointCost = Mathf.Max(1, EditorGUILayout.IntField("Skill-point cost", pointCost));
            chiCost = Mathf.Max(0f, EditorGUILayout.FloatField("Chi per destination", chiCost));
            if (isRun)
            {
                startingSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("Initial run speed", startingSpeed));
                speedIncrement = Mathf.Max(0f, EditorGUILayout.FloatField("Speed per rank", speedIncrement));
            }
            else
            {
                startingHeight = Mathf.Max(0f, EditorGUILayout.FloatField("Initial height", startingHeight));
                heightIncrement = Mathf.Max(0f, EditorGUILayout.FloatField("Height per rank", heightIncrement));
                startingDistance = Mathf.Max(0f, EditorGUILayout.FloatField("Initial distance", startingDistance));
                distanceIncrement = Mathf.Max(0f, EditorGUILayout.FloatField("Distance per rank", distanceIncrement));
            }
            if (!GUILayout.Button("Generate rank table") || !BalancePreview.ConfirmReplacement("skill rank")) return;
            Undo.RecordObject(data, "Generate skill ranks");
            data.ranks = new MovementSkillRank[rankCount];
            for (int index = 0; index < rankCount; index++)
                data.ranks[index] = new MovementSkillRank
                {
                    requiredCharacterLevel = (int)Math.Min(int.MaxValue, 1L + (long)index * levelStep),
                    skillPointCost = pointCost,
                    chiCost = chiCost,
                    runSpeed = isRun ? startingSpeed + index * speedIncrement : 0f,
                    jumpHeight = isRun ? 0f : startingHeight + index * heightIncrement,
                    jumpDistance = isRun ? 0f : startingDistance + index * distanceIncrement
                };
            EditorUtility.SetDirty(data);
        }
    }

    [CustomEditor(typeof(PlayerCharacterProgression))]
    public sealed class PlayerCharacterProgressionEditor : Editor
    {
        private const int DefaultTestExperience = 100;
        private int testExperience = DefaultTestExperience;

        /// <summary>Shows runtime-only progression state and explicit XP testing controls.</summary>
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Progress and hotbar assignments are session-only. Enter Play Mode to inspect and test transactions.", MessageType.Info);
                return;
            }
            var player = (PlayerCharacterProgression)target;
            EditorGUILayout.LabelField("Level", $"{player.CurrentLevel} / {player.MaxLevel}");
            EditorGUILayout.LabelField("XP", $"{player.Experience} / {player.ExperienceRequired}");
            EditorGUILayout.LabelField("Available points", $"Stat: {player.StatPoints} / Skill: {player.SkillPoints}");
            testExperience = Mathf.Max(1, EditorGUILayout.IntField("Test XP amount", testExperience));
            using (new EditorGUI.DisabledScope(player.IsAtLevelCap))
            {
                if (GUILayout.Button("Grant test XP")) player.GrantExperience(testExperience);
                if (GUILayout.Button("Grant XP to next level")) player.GrantExperience(player.ExperienceRequired - player.Experience);
            }
        }
    }
}
