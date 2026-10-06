using System.Collections.Generic;
using KryticalNova.Economy;
using KryticalNova.Items;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Shows the economy's numbers against each other: for every level of the income target, how many hours of play
    /// each cap and price is worth. A cap that takes ten hours at the level cap and two hundred at level 30 is two
    /// different decisions, and this is where that shows before anything is built on it.
    /// </summary>
    [CustomEditor(typeof(EconomySettings))]
    public sealed class EconomySettingsEditor : Editor
    {
        private const float LabelWidth = 190f;
        private const float CellWidth = 78f;
        private static readonly Color HeaderColor = new Color(0.45f, 0.87f, 0.77f);
        private static readonly Color CheapColor = new Color(0.62f, 0.9f, 0.62f);
        private static readonly Color SteepColor = new Color(1f, 0.62f, 0.4f);
        private bool showPreview = true;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var settings = (EconomySettings)target;

            EditorGUILayout.Space(12f);
            foreach (string problem in Problems(settings)) EditorGUILayout.HelpBox(problem, MessageType.Warning);

            showPreview = EditorGUILayout.BeginFoldoutHeaderGroup(showPreview, "Hours of play to earn it (at the income target)");
            if (showPreview) DrawPreview(settings);
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        /// <summary>Every amount worth comparing with income: the caps, each rarity's top price, and the reference costs.</summary>
        public static List<(string Label, long Creds)> Amounts(EconomySettings settings)
        {
            var rows = new List<(string, long)>
            {
                ("Trade: per trade", settings.tradeCredsPerTrade),
                ("Trade: per day", settings.tradeCredsPerDay),
            };
            foreach (ItemRarity rarity in System.Enum.GetValues(typeof(ItemRarity)))
                rows.Add(($"Kiosk top price: {rarity}", settings.KioskPriceCap(rarity, false)));
            rows.Add(("Kiosk top price: stackable (each)", settings.kioskStackablePriceCap));
            rows.Add(("Kiosk top price: pet at 100%", settings.KioskPriceCap(ItemRarity.Common, false, 1f)));
            if (settings.referenceCosts != null)
                foreach (ReferenceCost cost in settings.referenceCosts)
                    if (!string.IsNullOrEmpty(cost.label)) rows.Add((cost.label, cost.creds));
            return rows;
        }

        public static List<string> Problems(EconomySettings settings)
        {
            var problems = new List<string>();
            if (settings.tradeCredsPerTrade > settings.tradeCredsPerDay)
                problems.Add("One trade may carry more Creds than a whole day allows: the per-trade cap can never be reached.");
            if (settings.tradeCredsPerDay > settings.walletCap)
                problems.Add("The daily trade allowance is larger than a wallet can hold.");
            for (int i = 1; settings.kioskPriceCapByRarity != null && i < settings.kioskPriceCapByRarity.Length; i++)
                if (settings.kioskPriceCapByRarity[i] < settings.kioskPriceCapByRarity[i - 1])
                    problems.Add($"Kiosk price caps should not fall as rarity rises ({(ItemRarity)i} is below {(ItemRarity)(i - 1)}).");
            for (int i = 1; settings.incomeTarget != null && i < settings.incomeTarget.Length; i++)
            {
                if (settings.incomeTarget[i].level <= settings.incomeTarget[i - 1].level)
                    problems.Add("Income target points must be in rising level order.");
                else if (settings.incomeTarget[i].credsPerHour < settings.incomeTarget[i - 1].credsPerHour)
                    problems.Add($"The income target falls between level {settings.incomeTarget[i - 1].level} and {settings.incomeTarget[i].level}.");
            }
            if (settings.kioskTiers == null || settings.kioskTiers.Length == 0 || !settings.kioskTiers[0].IsFree)
                problems.Add("The first Kiosk length should be the free one: it is the default every player gets.");
            return problems;
        }

        private static void DrawPreview(EconomySettings settings)
        {
            if (settings.incomeTarget == null || settings.incomeTarget.Length == 0)
            {
                EditorGUILayout.HelpBox("Add income target points to compare the numbers with.", MessageType.Info);
                return;
            }

            var header = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleRight };
            header.normal.textColor = HeaderColor;
            var cell = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Level", EditorStyles.miniBoldLabel, GUILayout.Width(LabelWidth));
                foreach (IncomePoint point in settings.incomeTarget) GUILayout.Label(point.level.ToString(), header, GUILayout.Width(CellWidth));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Creds per hour", EditorStyles.miniLabel, GUILayout.Width(LabelWidth));
                foreach (IncomePoint point in settings.incomeTarget) GUILayout.Label(Short(point.credsPerHour), cell, GUILayout.Width(CellWidth));
            }
            EditorGUILayout.Space(2f);

            foreach ((string label, long creds) in Amounts(settings))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label($"{label}  ({Short(creds)})", EditorStyles.miniLabel, GUILayout.Width(LabelWidth));
                    foreach (IncomePoint point in settings.incomeTarget)
                    {
                        double hours = settings.HoursToEarn(creds, point.level);
                        Color previous = GUI.contentColor;
                        GUI.contentColor = hours <= 1.0 ? CheapColor : hours >= 100.0 ? SteepColor : previous;
                        GUILayout.Label(Hours(hours), cell, GUILayout.Width(CellWidth));
                        GUI.contentColor = previous;
                    }
                }
            }
            EditorGUILayout.HelpBox("Green: under an hour of play. Orange: a hundred hours or more. Nothing pays Creds yet: " +
                                    "Hostile drops, Contracts and shops will be tuned to the Creds-per-hour row.", MessageType.None);
        }

        private static string Hours(double hours) =>
            hours <= 0.0 ? "-" : hours < 1.0 ? $"{hours * 60.0:0} min" : hours < 100.0 ? $"{hours:0.0} h" : $"{hours:0} h";

        private static string Short(long creds) =>
            creds >= 1_000_000_000 ? $"{creds / 1_000_000_000.0:0.##}B"
            : creds >= 1_000_000 ? $"{creds / 1_000_000.0:0.##}M"
            : creds >= 1_000 ? $"{creds / 1_000.0:0.##}k" : creds.ToString();
    }
}
