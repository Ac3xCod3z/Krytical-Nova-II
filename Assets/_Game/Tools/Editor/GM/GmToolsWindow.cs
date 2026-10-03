using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KryticalNova.Core;
using KryticalNova.DevTools;
using KryticalNova.Items;
using KryticalNova.Progression;
using KryticalNova.World;
using QFSW.QC;
using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools.GM
{
    /// <summary>
    /// Dockable GM / dev tools (Odin). Every action is sent as a Quantum Console command string (the same
    /// <c>gm.*</c>, <c>time.*</c>, <c>weather.*</c>, <c>item.*</c> commands you can type in-game), so the console and
    /// this window can never drift apart. Works in Play Mode as host or offline, like the commands themselves.
    /// </summary>
    public sealed class GmToolsWindow : OdinEditorWindow
    {
        private const string Tabs = "Tabs";
        private const string PlayerTab = "Player";
        private const string StatsTab = "Stats";
        private const string CheatsTab = "Cheats";
        private const string TeleportTab = "Teleport";
        private const string WorldTab = "World";
        private const string ItemsTab = "Items";
        private const int MaxLogLines = 200;

        [MenuItem("Krytical Nova/GM Tools", priority = 0)]
        [MenuItem("Window/Krytical Nova/GM Tools")]
        public static void Open()
        {
            // Opens as a tab beside the Inspector the first time; afterwards Unity remembers wherever you dock it.
            var inspectorType = Type.GetType("UnityEditor.InspectorWindow,UnityEditor");
            var window = inspectorType != null ? GetWindow<GmToolsWindow>("GM Tools", true, inspectorType) : GetWindow<GmToolsWindow>();
            window.titleContent = new GUIContent("GM Tools", EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow").image);
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        // ================================================================ state helpers

        private static PlayerInventory LocalInventory => Application.isPlaying ? PlayerInventory.Local : null;
        private static GameObject LocalPlayer => LocalInventory != null ? LocalInventory.gameObject : null;
        private static PlayerCharacterProgression Progression => LocalPlayer != null ? LocalPlayer.GetComponent<PlayerCharacterProgression>() : null;
        private static PlayerResources Resources => LocalPlayer != null ? LocalPlayer.GetComponent<PlayerResources>() : null;
        private static CharacterCombatStats CombatStats => LocalPlayer != null ? LocalPlayer.GetComponent<CharacterCombatStats>() : null;
        private static WorldEnvironmentNetwork Env => Application.isPlaying ? WorldEnvironmentNetwork.Instance : null;

        /// <summary>Play Mode, a local player exists, and we are host/offline (the commands' own authority rule).</summary>
        private static bool IsReady
        {
            get
            {
                var inv = LocalInventory;
                return inv != null && (!inv.IsSpawned || inv.IsServer);
            }
        }

        private static bool HasWorld => Env != null && Env.HasWorldAuthority;

        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Quote(string value) => "\"" + value.Replace("\"", "") + "\"";

        // ================================================================ status header

        [OnInspectorGUI, PropertyOrder(-100)]
        private void DrawStatus()
        {
            if (!Application.isPlaying)
            {
                SirenixEditorGUI.InfoMessageBox("GM tools run in Play Mode (host or offline). Changes don't persist after you stop.");
                if (GUILayout.Button("Enter Play Mode", GUILayout.Height(26))) EditorApplication.isPlaying = true;
                return;
            }
            var inv = LocalInventory;
            if (inv == null)
            {
                SirenixEditorGUI.WarningMessageBox("Waiting for the local player to spawn…");
                return;
            }
            if (!IsReady)
            {
                SirenixEditorGUI.ErrorMessageBox("Connected as a client. GM commands only work on the host or offline.");
                return;
            }

            SirenixEditorGUI.BeginBox();
            var prog = Progression;
            var res = Resources;
            var pos = LocalPlayer.transform.position;
            EditorGUILayout.LabelField(
                prog != null ? $"Level {prog.CurrentLevel} / {prog.MaxLevel}   ·   {prog.StatPoints} stat pts   ·   {prog.SkillPoints} skill pts" : "No progression",
                EditorStyles.boldLabel);
            if (res != null)
            {
                DrawBar($"HP  {res.Health:0} / {res.MaxHealth:0}", res.MaxHealth > 0f ? res.Health / res.MaxHealth : 0f);
                DrawBar($"Chi  {res.Chi:0} / {res.MaxChi:0}", res.MaxChi > 0f ? res.Chi / res.MaxChi : 0f);
            }

            var env = Env;
            string time = env != null ? FormatHours(env.TimeOfDay01 * 24f) : "--:--";
            string weather = env != null && env.ToWeather != null ? env.ToWeather.name : "none";
            EditorGUILayout.LabelField($"Pos ({pos.x:0}, {pos.y:0}, {pos.z:0})   ·   {time}   ·   {weather}", EditorStyles.miniLabel);

            var flags = new List<string>();
            if (GmState.GodMode) flags.Add("GOD");
            if (GmState.InfiniteChi) flags.Add("INF CHI");
            if (GmState.NoCooldowns) flags.Add("NO CD");
            if (GmCommands.OneShotEnabled) flags.Add("ONE-SHOT");
            if (!Mathf.Approximately(GmState.SpeedMultiplier, 1f)) flags.Add($"SPEED ×{GmState.SpeedMultiplier:0.##}");
            if (flags.Count > 0)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.35f);
                EditorGUILayout.LabelField(string.Join("   ", flags), EditorStyles.miniBoldLabel);
                GUI.color = prev;
            }
            SirenixEditorGUI.EndBox();
        }

        private static void DrawBar(string label, float fill)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 16f);
            EditorGUI.ProgressBar(rect, Mathf.Clamp01(fill), label);
        }

        private static string FormatHours(float hours)
        {
            hours = Mathf.Repeat(hours, 24f);
            int h = (int)hours;
            int m = (int)((hours - h) * 60f);
            return $"{h:00}:{m:00}";
        }

        // ================================================================ PLAYER

        [ButtonGroup(Tabs + "/" + PlayerTab + "/Level"), EnableIf(nameof(IsReady)), PropertyOrder(0)]
        private void MaxLevel() => Run("gm.maxlevel");

        [ButtonGroup(Tabs + "/" + PlayerTab + "/Level"), EnableIf(nameof(IsReady)), LabelText("Level 1"), PropertyOrder(1)]
        private void LevelOne() => Run("gm.level 1");

        [TabGroup(Tabs, PlayerTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(2)]
        private void SetLevel([MinValue(1)] int level = 10) => Run($"gm.level {level}");

        [TabGroup(Tabs, PlayerTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(3)]
        private void GrantXp([MinValue(1)] int amount = 1000) => Run($"gm.xp {amount}");

        [TabGroup(Tabs, PlayerTab), Title("Points"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(4)]
        private void AddStatPoints(int amount = 10) => Run($"gm.statpoints {amount}");

        [TabGroup(Tabs, PlayerTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(5)]
        private void AddSkillPoints(int amount = 10) => Run($"gm.skillpoints {amount}");

        [TabGroup(Tabs, PlayerTab), Title("Attributes"), ShowInInspector, DisplayAsString, LabelText("Current"), PropertyOrder(6)]
        private string AttributeSummary
        {
            get
            {
                var prog = Progression;
                var stats = CombatStats;
                if (prog == null) return "—";
                return $"STR {Attr(prog, stats, CharacterAttribute.Strength)}   VIT {Attr(prog, stats, CharacterAttribute.Vitality)}   " +
                       $"AGI {Attr(prog, stats, CharacterAttribute.Agility)}   SPI {Attr(prog, stats, CharacterAttribute.Spirit)}";
            }
        }

        private static string Attr(PlayerCharacterProgression prog, CharacterCombatStats stats, CharacterAttribute attribute)
        {
            int baseValue = prog.GetAttribute(attribute);
            int total = stats != null ? Mathf.RoundToInt(stats.GetStatValue((StatType)(int)attribute)) : baseValue;
            return total != baseValue ? $"{baseValue} (+{total - baseValue})" : baseValue.ToString();
        }

        [TabGroup(Tabs, PlayerTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(7)]
        private void SetAttribute(CharacterAttribute attribute = CharacterAttribute.Vitality, [MinValue(0)] int value = 50)
        {
            string key = attribute switch
            {
                CharacterAttribute.Strength => "str",
                CharacterAttribute.Vitality => "vit",
                CharacterAttribute.Agility => "agi",
                _ => "spi",
            };
            Run($"gm.attr {key} {value}");
        }

        [TabGroup(Tabs, PlayerTab), Button("Refund All Allocated Points"), EnableIf(nameof(IsReady)), PropertyOrder(8)]
        private void ResetAllocations() => Run("gm.resetstats");

        [ButtonGroup(Tabs + "/" + PlayerTab + "/Vitals"), EnableIf(nameof(IsReady)), PropertyOrder(9)]
        private void Heal() => Run("gm.heal");

        [ButtonGroup(Tabs + "/" + PlayerTab + "/Vitals"), EnableIf(nameof(IsReady)), PropertyOrder(10)]
        private void FillChi() => Run("gm.fillchi");

        [ButtonGroup(Tabs + "/" + PlayerTab + "/Vitals"), EnableIf(nameof(IsReady)), GUIColor(1f, 0.6f, 0.6f), PropertyOrder(11)]
        private void Kill() => Run("gm.kill");

        [TabGroup(Tabs, PlayerTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(12)]
        private void DamageSelf([MinValue(1)] float amount = 50f) => Run($"gm.damage {F(amount)}");

        [TabGroup(Tabs, PlayerTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(13)]
        private void SpendChi([MinValue(1)] float amount = 25f) => Run($"gm.spendchi {F(amount)}");

        // ================================================================ COMBAT (Cheats tab)

        [TabGroup(Tabs, CheatsTab), Title("Combat Testing"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(250)]
        [InfoBox("Targets the nearest combat dummy / NPC with a CombatEntity.")]
        private void StrikeNearestDummy([MinValue(1), MaxValue(50)] int times = 1) => Run($"gm.dummy.hit {times}");

        [ButtonGroup(Tabs + "/" + CheatsTab + "/Combat"), EnableIf(nameof(IsReady)), PropertyOrder(251)]
        private void ResetDummy() => Run("gm.dummy.reset");

        [ButtonGroup(Tabs + "/" + CheatsTab + "/Combat"), EnableIf(nameof(IsReady)), PropertyOrder(252)]
        private void UnlockDropSkills() => Run("gm.dropskills true");

        [ButtonGroup(Tabs + "/" + CheatsTab + "/Combat"), EnableIf(nameof(IsReady)), PropertyOrder(253)]
        private void LockDropSkills() => Run("gm.dropskills false");

        [TabGroup(Tabs, CheatsTab), ValueDropdown(nameof(BookSkillChoices)), EnableIf(nameof(IsReady)), LabelText("Book Skill"), PropertyOrder(254)]
        [SerializeField] private string bookSkillChoice;

        [ButtonGroup(Tabs + "/" + CheatsTab + "/BookSkill"), EnableIf(nameof(IsReady)), LabelText("Unlock Skill"), PropertyOrder(255)]
        private void UnlockBookSkill() { if (!string.IsNullOrEmpty(bookSkillChoice)) Run($"gm.unlockskill {bookSkillChoice}"); }

        [ButtonGroup(Tabs + "/" + CheatsTab + "/BookSkill"), EnableIf(nameof(IsReady)), LabelText("Lock Skill"), PropertyOrder(256)]
        private void LockBookSkill() { if (!string.IsNullOrEmpty(bookSkillChoice)) Run($"gm.lockskill {bookSkillChoice}"); }

        [ButtonGroup(Tabs + "/" + CheatsTab + "/BookSkill"), EnableIf(nameof(IsReady)), LabelText("List Skills"), PropertyOrder(257)]
        private void ListSkills() => Run("gm.skills");

        /// <summary>Level-60 drop and Wargod skills: the ones a book (or this tool) has to unlock.</summary>
        private static IEnumerable<ValueDropdownItem<string>> BookSkillChoices()
        {
            var player = LocalPlayer;
            var skills = player != null ? player.GetComponent<PlayerCombatSkills>() : null;
            if (skills == null) yield break;
            foreach (var def in skills.AvailableSkills)
            {
                if (def == null || !def.requiresSkillBook) continue;
                string state = skills.IsLocked(def) ? "locked" : "unlocked";
                yield return new ValueDropdownItem<string>($"Lvl {def.requiredCharacterLevel}/{def.displayName}  ({state})", def.skillId);
            }
        }

        // ================================================================ STATS

        [Serializable]
        private struct StatRow
        {
            [TableColumnWidth(170, Resizable = false), DisplayAsString] public string Stat;
            [DisplayAsString] public string Base;
            [DisplayAsString] public string Final;
            [DisplayAsString] public string Bonus;
        }

        [TabGroup(Tabs, StatsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(100)]
        [InfoBox("Adds a flat GM bonus on top of everything else (0 removes it). Max HP / Chi pools update on the HUD.")]
        private void SetStatBonus(StatType stat = StatType.MaxHP, float value = 500f) => Run($"gm.setstat {stat} {F(value)}");

        [ButtonGroup(Tabs + "/" + StatsTab + "/Actions"), EnableIf(nameof(IsReady)), PropertyOrder(101)]
        private void ClearGmBonuses() => Run("gm.clearstats");

        [ButtonGroup(Tabs + "/" + StatsTab + "/Actions"), EnableIf(nameof(IsReady)), PropertyOrder(102)]
        private void PrintStatsToLog() => Run("gm.stats");

        [TabGroup(Tabs, StatsTab), ShowInInspector, LabelText("Live Stats"), PropertyOrder(103)]
        [TableList(IsReadOnly = true, AlwaysExpanded = true, ShowPaging = false, HideToolbar = true)]
        private List<StatRow> LiveStats
        {
            get
            {
                var rows = new List<StatRow>();
                var stats = CombatStats;
                if (stats == null) return rows;
                foreach (StatType stat in Enum.GetValues(typeof(StatType)))
                {
                    float b = stats.GetBaseStatValue(stat);
                    float f = stats.GetStatValue(stat);
                    rows.Add(new StatRow
                    {
                        Stat = stat.ToString(),
                        Base = b.ToString("0.##"),
                        Final = f.ToString("0.##"),
                        Bonus = Mathf.Approximately(f, b) ? "" : (f - b).ToString("+0.##;-0.##"),
                    });
                }
                return rows;
            }
        }

        // ================================================================ CHEATS

        [TabGroup(Tabs, CheatsTab), Title("Toggles"), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("God Mode (no damage)"), PropertyOrder(200)]
        private bool GodMode
        {
            get => GmState.GodMode;
            set { if (value != GmState.GodMode) Run($"gm.god {(value ? "true" : "false")}"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Infinite Chi"), PropertyOrder(201)]
        private bool InfiniteChi
        {
            get => GmState.InfiniteChi;
            set { if (value != GmState.InfiniteChi) Run("gm.infchi"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("No Skill Cooldowns"), PropertyOrder(202)]
        private bool NoCooldowns
        {
            get => GmState.NoCooldowns;
            set { if (value != GmState.NoCooldowns) Run("gm.nocd"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("One-Shot (×1000 AP)"), PropertyOrder(203)]
        private bool OneShot
        {
            get => GmCommands.OneShotEnabled;
            set { if (value != GmCommands.OneShotEnabled) Run("gm.oneshot"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, PropertyRange(0.1f, 10f), EnableIf(nameof(IsReady)), LabelText("Move Speed ×"), PropertyOrder(204)]
        private float SpeedMultiplier
        {
            get => GmState.SpeedMultiplier;
            set { if (!Mathf.Approximately(value, GmState.SpeedMultiplier)) Run($"gm.speed {F(value)}", log: false); }
        }

        [TabGroup(Tabs, CheatsTab), Title("Presets"), Button(ButtonSizes.Large), GUIColor(0.55f, 0.9f, 0.75f), EnableIf(nameof(IsReady)), PropertyOrder(205)]
        [InfoBox("Max level, god mode, infinite Chi, no cooldowns, full heal.")]
        private void DevLoadout()
        {
            Run("gm.maxlevel");
            if (!GmState.GodMode) Run("gm.god true");
            if (!GmState.InfiniteChi) Run("gm.infchi");
            if (!GmState.NoCooldowns) Run("gm.nocd");
            Run("gm.heal");
        }

        [TabGroup(Tabs, CheatsTab), Button("Reset All Toggles + Stat Bonuses"), EnableIf(nameof(IsReady)), PropertyOrder(206)]
        private void ResetCheats() => Run("gm.reset");

        // ================================================================ TELEPORT

        [Serializable]
        private sealed class TeleportRow
        {
            [TableColumnWidth(160), DisplayAsString] public string Name;
            [TableColumnWidth(70, Resizable = false), DisplayAsString] public string Kind;
            [DisplayAsString] public string Position;
            [NonSerialized] public Action Go;

            [TableColumnWidth(50, Resizable = false), Button("Go")]
            private void Teleport() => Go?.Invoke();
        }

        [NonSerialized] private List<TeleportRow> teleportRows;
        [NonSerialized] private double nextTargetRefresh;

        [TabGroup(Tabs, TeleportTab), ShowInInspector, LabelText("Points of Interest"), EnableIf(nameof(IsReady)), PropertyOrder(300)]
        [TableList(IsReadOnly = true, AlwaysExpanded = true, ShowPaging = false, HideToolbar = true)]
        private List<TeleportRow> TeleportTargets
        {
            get
            {
                if (!IsReady) return teleportRows = new List<TeleportRow>();
                if (teleportRows == null || EditorApplication.timeSinceStartup >= nextTargetRefresh)
                {
                    nextTargetRefresh = EditorApplication.timeSinceStartup + 2.0;
                    teleportRows = new List<TeleportRow>();
                    foreach (var (name, kind, position) in GmCommands.GetTeleportTargets())
                    {
                        string target = name;
                        teleportRows.Add(new TeleportRow
                        {
                            Name = name,
                            Kind = kind,
                            Position = $"({position.x:0}, {position.y:0}, {position.z:0})",
                            Go = () => Run($"gm.tp {Quote(target)}"),
                        });
                    }
                }
                return teleportRows;
            }
        }

        [TabGroup(Tabs, TeleportTab), Title("Elsewhere"), Button("Teleport to Scene View Pivot", ButtonSizes.Medium), EnableIf(nameof(IsReady)), PropertyOrder(301)]
        [InfoBox("Navigate the Scene view to a spot (or select an object and press F), then teleport the player there.")]
        private void TeleportToSceneView()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null) { Log("No Scene view open."); return; }
            Vector3 p = view.pivot;
            Run($"gm.tpxyz {F(p.x)} {F(p.y)} {F(p.z)}");
        }

        [TabGroup(Tabs, TeleportTab), Button("Teleport to Selected GameObject"), EnableIf(nameof(CanTeleportToSelection)), PropertyOrder(302)]
        private void TeleportToSelection()
        {
            Vector3 p = Selection.activeTransform.position;
            Run($"gm.tpxyz {F(p.x)} {F(p.y)} {F(p.z)}");
        }

        private bool CanTeleportToSelection => IsReady && Selection.activeTransform != null && Selection.activeTransform.gameObject.scene.IsValid();

        [TabGroup(Tabs, TeleportTab), Button("Frame Player in Scene View"), EnableIf(nameof(IsReady)), PropertyOrder(303)]
        private void FramePlayer()
        {
            var view = SceneView.lastActiveSceneView;
            if (view == null || LocalPlayer == null) return;
            view.LookAt(LocalPlayer.transform.position, view.rotation, 12f);
        }

        [TabGroup(Tabs, TeleportTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(304)]
        private void TeleportToCoordinates(Vector3 position) => Run($"gm.tpxyz {F(position.x)} {F(position.y)} {F(position.z)}");

        // ================================================================ WORLD

        [TabGroup(Tabs, WorldTab), Title("Time of Day"), ShowInInspector, PropertyRange(0f, 24f), EnableIf(nameof(HasWorld)), PropertyOrder(400)]
        [LabelText("@\"Hour  (\" + CurrentTimeLabel + \")\"")]
        private float Hour
        {
            get => Env != null ? Env.TimeOfDay01 * 24f : 12f;
            set { if (Env != null && Mathf.Abs(Mathf.DeltaAngle(value * 15f, Env.TimeOfDay01 * 360f)) > 0.05f) Run($"time.set {F(value)}", log: false); }
        }

        public static string CurrentTimeLabel => Env != null ? FormatHours(Env.TimeOfDay01 * 24f) : "--:--";

        [ButtonGroup(Tabs + "/" + WorldTab + "/Presets"), EnableIf(nameof(HasWorld)), PropertyOrder(401)]
        private void Dawn() => Run("time.dawn");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Presets"), EnableIf(nameof(HasWorld)), PropertyOrder(402)]
        private void Noon() => Run("time.noon");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Presets"), EnableIf(nameof(HasWorld)), PropertyOrder(403)]
        private void Dusk() => Run("time.dusk");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Presets"), EnableIf(nameof(HasWorld)), PropertyOrder(404)]
        private void Midnight() => Run("time.midnight");

        [TabGroup(Tabs, WorldTab), ShowInInspector, ToggleLeft, EnableIf(nameof(HasWorld)), LabelText("Pause Clock"), PropertyOrder(405)]
        private bool TimePaused
        {
            get => Env != null && Env.State.TimeScale <= 0f;
            set { if (value != TimePaused) Run(value ? "time.pause" : "time.resume"); }
        }

        [TabGroup(Tabs, WorldTab), ShowInInspector, PropertyRange(0f, 200f), EnableIf(nameof(HasWorld)), LabelText("Clock Speed ×"), PropertyOrder(406)]
        [InfoBox("1 = normal, 0 = paused. Try 20-60 to watch shadows sweep across the day.")]
        private float TimeScale
        {
            get => Env != null ? Env.State.TimeScale : 1f;
            set { if (Env != null && !Mathf.Approximately(value, Env.State.TimeScale)) Run($"time.scale {F(value)}", log: false); }
        }

        [TabGroup(Tabs, WorldTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(HasWorld)), PropertyOrder(407)]
        private void SetDayLength([MinValue(10)] float realSeconds = 1440f) => Run($"time.daylength {F(realSeconds)}");

        [TabGroup(Tabs, WorldTab), Title("Weather"), ShowInInspector, DisplayAsString, LabelText("Current"), PropertyOrder(408)]
        private string CurrentWeather
        {
            get
            {
                var env = Env;
                if (env == null || env.ToWeather == null) return "—";
                return env.WeatherBlendT < 0.999f && env.FromWeather != null
                    ? $"{env.FromWeather.name} → {env.ToWeather.name} ({env.WeatherBlendT:P0})"
                    : env.ToWeather.name;
            }
        }

        [TabGroup(Tabs, WorldTab), ValueDropdown(nameof(WeatherNames)), EnableIf(nameof(HasWorld)), LabelText("Weather"), PropertyOrder(409)]
        [SerializeField] private string weatherChoice = "WP_Rain";

        [TabGroup(Tabs, WorldTab), PropertyRange(0f, 60f), EnableIf(nameof(HasWorld)), LabelText("Transition (s)"), PropertyOrder(410)]
        [SerializeField] private float weatherTransition = 3f;

        [ButtonGroup(Tabs + "/" + WorldTab + "/Weather"), EnableIf(nameof(HasWorld)), PropertyOrder(411)]
        private void ApplyWeather()
        {
            if (string.IsNullOrEmpty(weatherChoice)) return;
            Run($"weather.set {Quote(weatherChoice)} {F(weatherTransition)}");
        }

        [ButtonGroup(Tabs + "/" + WorldTab + "/Weather"), EnableIf(nameof(HasWorld)), PropertyOrder(412)]
        private void ClearSkies() => Run($"weather.clear {F(weatherTransition)}");

        [TabGroup(Tabs, WorldTab), ShowInInspector, ToggleLeft, EnableIf(nameof(HasWorld)), LabelText("Lock Weather (stop auto changes)"), PropertyOrder(413)]
        private bool WeatherLocked
        {
            get => Env != null && Env.WeatherLocked;
            set { if (value != WeatherLocked) Run($"weather.lock {(value ? "true" : "false")}"); }
        }

        // ---------------------------------------------------------------- wind

        [TabGroup(Tabs, WorldTab), Title("Wind"), ShowInInspector, DisplayAsString, LabelText("Current"), PropertyOrder(414)]
        private string CurrentWind
        {
            get
            {
                var env = Env;
                if (env == null) return "—";
                var w = env.Weather;
                float heading = Mathf.Repeat(Mathf.Atan2(w.wind.x, w.wind.y) * Mathf.Rad2Deg, 360f);
                return $"{w.wind.magnitude:0.0} m/s toward {heading:0}°  ·  gust {w.gust:0.00}";
            }
        }

        [TabGroup(Tabs, WorldTab), PropertyRange(0f, 30f), EnableIf(nameof(HasWorld)), LabelText("Speed (m/s)"), PropertyOrder(415)]
        [SerializeField] private float windSpeed = 8f;

        [TabGroup(Tabs, WorldTab), PropertyRange(0f, 360f), EnableIf(nameof(HasWorld)), LabelText("Heading (°)"), PropertyOrder(416)]
        [InfoBox("Held until the next weather change. Bends grass and trees, drives leaves, rain slant and water.")]
        [SerializeField] private float windHeading = 90f;

        [ButtonGroup(Tabs + "/" + WorldTab + "/Wind"), EnableIf(nameof(HasWorld)), LabelText("Apply Wind"), PropertyOrder(417)]
        private void ApplyWind() => Run($"weather.windset {F(windSpeed)} {F(windHeading)}");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Wind"), EnableIf(nameof(HasWorld)), LabelText("Clear Override"), PropertyOrder(418)]
        private void ClearWind() => Run("weather.windclear");

        [ButtonGroup(Tabs + "/" + WorldTab + "/WindPresets"), EnableIf(nameof(HasWorld)), PropertyOrder(419)]
        private void Calm() => SetWindPreset(0.5f);

        [ButtonGroup(Tabs + "/" + WorldTab + "/WindPresets"), EnableIf(nameof(HasWorld)), PropertyOrder(419)]
        private void Breeze() => SetWindPreset(5f);

        [ButtonGroup(Tabs + "/" + WorldTab + "/WindPresets"), EnableIf(nameof(HasWorld)), PropertyOrder(419)]
        private void Windy() => SetWindPreset(12f);

        [ButtonGroup(Tabs + "/" + WorldTab + "/WindPresets"), EnableIf(nameof(HasWorld)), PropertyOrder(419)]
        private void Gale() => SetWindPreset(22f);

        private void SetWindPreset(float speed)
        {
            windSpeed = speed;
            Run($"weather.windset {F(speed)} {F(windHeading)}");
        }

        // ---------------------------------------------------------------- sky (local presentation overrides)

        [TabGroup(Tabs, WorldTab), Title("Sky"), Button("Rainbow (sets 17:00)"), EnableIf(nameof(HasWorld)), PropertyOrder(420)]
        [InfoBox("Sky overrides only change this machine's view. Reset clears them all.")]
        private void ForceRainbow()
        {
            Run("time.set 17");
            Run("sky.rainbow 1");
        }

        [ButtonGroup(Tabs + "/" + WorldTab + "/Moon"), EnableIf(nameof(HasWorld)), LabelText("New"), PropertyOrder(421)]
        private void MoonNew() => Run("sky.moonphase new");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Moon"), EnableIf(nameof(HasWorld)), LabelText("1st Qtr"), PropertyOrder(422)]
        private void MoonFirst() => Run("sky.moonphase first");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Moon"), EnableIf(nameof(HasWorld)), LabelText("Full"), PropertyOrder(423)]
        private void MoonFull() => Run("sky.moonphase full");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Moon"), EnableIf(nameof(HasWorld)), LabelText("Last Qtr"), PropertyOrder(424)]
        private void MoonLast() => Run("sky.moonphase last");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Moon"), EnableIf(nameof(HasWorld)), LabelText("Next ▸"), PropertyOrder(425)]
        private void MoonNext() => Run("sky.moon.next");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Halo"), EnableIf(nameof(HasWorld)), LabelText("Sun Halo"), PropertyOrder(426)]
        private void SunHalo() => Run("sky.halo sun");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Halo"), EnableIf(nameof(HasWorld)), LabelText("Moon Halo"), PropertyOrder(427)]
        private void MoonHalo() => Run("sky.halo moon");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Halo"), EnableIf(nameof(HasWorld)), LabelText("Lightning"), PropertyOrder(428)]
        private void Lightning() => Run("sky.lightning");

        [TabGroup(Tabs, WorldTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(HasWorld)), PropertyOrder(429)]
        private void HeatShimmer([MinValue(0), MaxValue(5)] float multiplier = 2.5f) => Run($"sky.shimmer {F(multiplier)}");

        [TabGroup(Tabs, WorldTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(HasWorld)), PropertyOrder(430)]
        private void LensFlare([MinValue(0), MaxValue(4)] float strength = 1f) => Run($"sky.flare {F(strength)}");

        [ButtonGroup(Tabs + "/" + WorldTab + "/SkyMisc"), EnableIf(nameof(HasWorld)), LabelText("Sky Status"), PropertyOrder(431)]
        private void SkyStatus() => Run("sky.status");

        [ButtonGroup(Tabs + "/" + WorldTab + "/SkyMisc"), EnableIf(nameof(HasWorld)), LabelText("Reset Sky"), GUIColor(1f, 0.85f, 0.5f), PropertyOrder(432)]
        private void ResetSky() => Run("sky.reset");

        private static IEnumerable<string> WeatherNames()
        {
            var catalog = Env != null ? Env.Catalog : null;
            if (catalog != null)
            {
                for (int i = 0; i < catalog.Count; i++)
                {
                    var profile = catalog.Get(i);
                    if (profile != null) yield return profile.name;
                }
                yield break;
            }
            foreach (string guid in AssetDatabase.FindAssets("t:WeatherProfile"))
                yield return System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid));
        }

        // ================================================================ ITEMS

        [TabGroup(Tabs, ItemsTab), ValueDropdown(nameof(ItemChoices)), EnableIf(nameof(IsReady)), LabelText("Item"), PropertyOrder(500)]
        [SerializeField] private string itemChoice;

        [TabGroup(Tabs, ItemsTab), MinValue(1), EnableIf(nameof(IsReady)), LabelText("Count"), PropertyOrder(501)]
        [SerializeField] private int itemCount = 1;

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Give"), EnableIf(nameof(IsReady)), PropertyOrder(502)]
        private void GiveItem()
        {
            if (string.IsNullOrEmpty(itemChoice)) { Log("Pick an item first."); return; }
            Run($"item.give {Quote(itemChoice)} {itemCount}");
        }

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Give"), EnableIf(nameof(IsReady)), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(503)]
        private void ClearBag() => Run("item.clear");

        private static IEnumerable<ValueDropdownItem<string>> ItemChoices()
        {
            var inv = LocalInventory;
            if (inv == null || inv.Catalog == null) yield break;
            foreach (var def in inv.Catalog.Items)
            {
                if (def == null) continue;
                yield return new ValueDropdownItem<string>($"{def.equipSlot}/{def.displayName}  ({def.itemId})", def.itemId.ToString());
            }
        }

        // ================================================================ console + log (always visible)

        [BoxGroup("Console", Order = 1000), HorizontalGroup("Console/Input"), HideLabel]
        [SerializeField, Tooltip("Any Quantum Console command, e.g. gm.help")] private string command = "gm.help";

        [HorizontalGroup("Console/Input", Width = 60), Button("Run")]
        private void RunTyped()
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            Run(command.Trim());
        }

        [BoxGroup("Console"), OnInspectorGUI]
        private void DrawLog()
        {
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.MinHeight(110), GUILayout.MaxHeight(240));
            var style = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false, fontSize = 11 };
            EditorGUILayout.SelectableLabel(logText.Length > 0 ? logText.ToString() : "Output appears here.", style,
                GUILayout.ExpandHeight(true), GUILayout.MinHeight(style.CalcHeight(new GUIContent(logText.ToString()), position.width - 40f)));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Clear Output", EditorStyles.miniButton)) { logText.Clear(); logLines = 0; }
        }

        [NonSerialized] private readonly StringBuilder logText = new StringBuilder();
        [NonSerialized] private int logLines;
        [NonSerialized] private Vector2 logScroll;

        // ================================================================ plumbing

        private void Run(string commandString, bool log = true)
        {
            if (!Application.isPlaying) { Log("Enter Play Mode first."); return; }
            try
            {
                if (!QuantumConsoleProcessor.TableGenerated && !QuantumConsoleProcessor.TableIsGenerating)
                    QuantumConsoleProcessor.GenerateCommandTable();
                object result = QuantumConsoleProcessor.InvokeCommand(commandString);
                if (log) Log($"> {commandString}\n{result}");
            }
            catch (Exception e)
            {
                Log($"> {commandString}\n[error] {e.InnerException?.Message ?? e.Message}");
            }
            teleportRows = null;
            Repaint();
        }

        private void Log(string text)
        {
            text = text.TrimEnd();
            logText.Insert(0, text + "\n\n");
            logLines += text.Split('\n').Length + 1;
            if (logLines > MaxLogLines)
            {
                logText.Length = Mathf.Min(logText.Length, 8000);
                logLines = MaxLogLines / 2;
            }
            logScroll = Vector2.zero;
        }

        // Live readouts (HP, clock, stats) refresh ~10×/s while playing.
        private void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }
    }
}
