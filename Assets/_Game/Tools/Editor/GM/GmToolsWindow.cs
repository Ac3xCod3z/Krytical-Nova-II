using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using KryticalNova.Core;
using KryticalNova.DevTools;
using KryticalNova.Factions;
using KryticalNova.Forge;
using KryticalNova.Items;
using KryticalNova.Player;
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
    /// this window can never drift apart. Works in Play Mode as host, server or offline, like the commands themselves,
    /// and acts on your own player or on the connected player picked at the top (<c>gm.target</c>).
    /// The search box at the top finds any command by name or description, whichever tab its button lives on.
    /// </summary>
    public sealed class GmToolsWindow : OdinEditorWindow
    {
        private const string Tabs = "Tabs";
        private const string PlayerTab = "Player";
        private const string StatsTab = "Stats";
        private const string SkillsTab = "Skills";
        private const string CheatsTab = "Cheats";
        private const string TeleportTab = "Teleport";
        private const string WorldTab = "World";
        private const string ItemsTab = "Items";
        private const string EconomyTab = "Economy";
        private const string SocialTab = "Social";
        private const string PetsTab = "Pets";
        private const string RenownTab = "Renown";
        private const string HostilesTab = "Hostiles";
        private const string CadenceTab = "Cadence";
        private const int MaxLogLines = 200;
        private const int MaxSearchResults = 40;

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

        // "Local" = the player the commands act on: your own, or the connected player picked with gm.target.
        // Found once a frame: every button's enable check asks for it, many times a draw, and a joined GM's lookup scans
        // every player in the scene (with the window open in Play that alone took the Editor down to two frames a second).
        private static GameObject LocalPlayer
        {
            get
            {
                if (!Application.isPlaying) return null;
                if (localPlayerFrame == Time.frameCount) return localPlayer;
                localPlayerFrame = Time.frameCount;
                localPlayer = GmRemoteLines.ForwardsToServer ? RemoteTargetPlayer()
                    : GmTarget.TryGetPlayer(out var player, out _) ? player : null;
                return localPlayer;
            }
        }
        private static GameObject localPlayer;
        private static int localPlayerFrame = -1;

        // A joined GM: the picked player as this game sees them (shown here; the commands themselves run on the server).
        private static GameObject RemoteTargetPlayer()
        {
            var own = KryticalNova.Networking.PlayerGm.Local;
            ulong id = GmRemoteLines.RemoteTarget ?? own.OwnerClientId;
            foreach (var gm in FindObjectsByType<KryticalNova.Networking.PlayerGm>())
                if (gm.IsSpawned && gm.OwnerClientId == id) return gm.gameObject;
            return own.gameObject;
        }
        private static PlayerInventory LocalInventory => LocalPlayer != null ? LocalPlayer.GetComponent<PlayerInventory>() : null;
        private static PlayerGmState TargetGm => LocalPlayer != null ? LocalPlayer.GetComponent<PlayerGmState>() : null;
        private static PlayerCharacterProgression Progression => LocalPlayer != null ? LocalPlayer.GetComponent<PlayerCharacterProgression>() : null;
        private static PlayerResources Resources => LocalPlayer != null ? LocalPlayer.GetComponent<PlayerResources>() : null;
        private static CharacterCombatStats CombatStats => LocalPlayer != null ? LocalPlayer.GetComponent<CharacterCombatStats>() : null;
        private static WorldEnvironmentNetwork Env => Application.isPlaying ? WorldEnvironmentNetwork.Instance : null;

        /// <summary>Play Mode, a player to act on exists, and we are host/server/offline (the commands' own authority rule).</summary>
        private static bool IsReady => LocalPlayer != null;

        private static bool HasWorld => Env != null && Env.HasWorldAuthority;

        private static string F(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Quote(string value) => "\"" + value.Replace("\"", "") + "\"";

        // The comic banner over the window (Tools/Editor/Skin): the same look as our inspectors.
        private KryticalNova.EditorTools.Skin.KnSkinScope skinScope;
        protected override void OnBeginDrawEditors()
        {
            KryticalNova.EditorTools.Skin.KnSkinScope.Backdrop(new Rect(0f, 0f, position.width, position.height));
            skinScope = new KryticalNova.EditorTools.Skin.KnSkinScope();
            KryticalNova.EditorTools.Skin.KnEditorSkin.Banner("GM Tools", "host only · every command is gm.*");
        }
        protected override void OnEndDrawEditors()
        {
            skinScope?.Dispose();
            skinScope = null;
        }

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
            DrawTargetPicker();
            if (LocalPlayer == null)
            {
                GmTarget.TryGetPlayer(out _, out string unavailable);
                SirenixEditorGUI.WarningMessageBox(unavailable);
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
                DrawBar($"Flux  {res.Chi:0} / {res.MaxChi:0}", res.MaxChi > 0f ? res.Chi / res.MaxChi : 0f);
            }

            var env = Env;
            string time = env != null ? FormatHours(env.TimeOfDay01 * 24f) : "--:--";
            string weather = env != null && env.ToWeather != null ? env.ToWeather.name : "none";
            EditorGUILayout.LabelField($"Pos ({pos.x:0}, {pos.y:0}, {pos.z:0})   ·   {time}   ·   {weather}", EditorStyles.miniLabel);

            var flags = new List<string>();
            var gm = TargetGm;
            if (gm != null && gm.GodMode) flags.Add("GOD");
            if (gm != null && gm.InfiniteChi) flags.Add("INF FLUX");
            if (gm != null && gm.NoCooldowns) flags.Add("NO CD");
            if (gm != null && gm.OneShot) flags.Add("ONE-SHOT");
            if (!Mathf.Approximately(Time.timeScale, 1f)) flags.Add($"GAME ×{Time.timeScale:0.##}");
            if (gm != null && !Mathf.Approximately(gm.SpeedMultiplier, 1f)) flags.Add($"SPEED ×{gm.SpeedMultiplier:0.##}");
            if (flags.Count > 0)
            {
                var prev = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.35f);
                EditorGUILayout.LabelField(string.Join("   ", flags), EditorStyles.miniBoldLabel);
                GUI.color = prev;
            }
            SirenixEditorGUI.EndBox();
        }

        /// <summary>Hosting a session: choose which connected player every button below acts on.</summary>
        private void DrawTargetPicker()
        {
            if (GmRemoteLines.ForwardsToServer)
            {
                DrawRemoteTargetPicker();
                return;
            }
            var players = GmTarget.ConnectedPlayers();
            if (players.Count == 0) return;

            var local = PlayerInventory.Local;
            ulong? target = GmTarget.ClientId;
            var labels = new string[players.Count];
            int selected = -1;
            for (int i = 0; i < players.Count; i++)
            {
                bool isLocal = local != null && local.gameObject == players[i].Player;
                labels[i] = isLocal ? $"Client {players[i].ClientId} (you)" : $"Client {players[i].ClientId}";
                if (target.HasValue ? target.Value == players[i].ClientId : isLocal) selected = i;
            }

            int picked = EditorGUILayout.Popup("Target player", selected, labels);
            if (picked != selected && picked >= 0) Run($"gm.target {players[picked].ClientId}");
        }

        /// <summary>
        /// A joined GM: the same choice among the players this game sees; the buttons below then run on the server and
        /// act on the one picked (gm.target for this GM only). Players too far away to be seen here are reached with
        /// gm.players and gm.target in the GM console.
        /// </summary>
        private void DrawRemoteTargetPicker()
        {
            ListenForServerAnswers();
            var players = new List<KryticalNova.Networking.PlayerGm>();
            foreach (var gm in FindObjectsByType<KryticalNova.Networking.PlayerGm>())
                if (gm.IsSpawned) players.Add(gm);
            players.Sort((a, b) => a.OwnerClientId.CompareTo(b.OwnerClientId));
            if (players.Count == 0) return;

            ulong own = KryticalNova.Networking.PlayerGm.Local.OwnerClientId;
            ulong target = GmRemoteLines.RemoteTarget ?? own;
            var labels = new string[players.Count];
            int selected = -1;
            for (int i = 0; i < players.Count; i++)
            {
                ulong id = players[i].OwnerClientId;
                string name = players[i].TryGetComponent(out PlayerIdentity identity) ? identity.CharacterName : players[i].name;
                labels[i] = $"Client {id}  {name}" + (id == own ? " (you)" : string.Empty);
                if (id == target) selected = i;
            }

            int picked = EditorGUILayout.Popup("Target player", selected, labels);
            if (picked != selected && picked >= 0)
                Run(players[picked].OwnerClientId == own ? "gm.target self" : $"gm.target {players[picked].OwnerClientId}");
            EditorGUILayout.LabelField("You are GM on the server: every button runs there, on the player picked.", EditorStyles.miniLabel);
        }

        // A joined GM's buttons run on the server; its answers come back here as well as to the GM console.
        [NonSerialized] private bool listeningForServer;

        private void ListenForServerAnswers()
        {
            if (listeningForServer) return;
            listeningForServer = true;
            KryticalNova.Networking.PlayerGm.Reply += text =>
            {
                if (this == null) return; // the window was closed
                Log(text);
                Repaint();
            };
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

        // ================================================================ search (always visible)

        [NonSerialized] private string search = string.Empty;
        [NonSerialized] private List<CommandData> searchable;
        [NonSerialized] private Vector2 searchScroll;

        [OnInspectorGUI, PropertyOrder(-90)]
        private void DrawSearch()
        {
            EditorGUILayout.BeginHorizontal();
            search = EditorGUILayout.TextField(search ?? string.Empty, EditorStyles.toolbarSearchField);
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(46)))
            {
                search = string.Empty;
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();

            if (string.IsNullOrWhiteSpace(search))
            {
                EditorGUILayout.LabelField("Search every GM command by name or description: skill, moon, teleport, speed…", EditorStyles.miniLabel);
                return;
            }

            List<CommandData> commands = Searchable();
            if (commands == null)
            {
                EditorGUILayout.LabelField("Loading the command list…", EditorStyles.miniLabel);
                return;
            }

            string[] words = search.Trim().ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var signature = new GUIStyle(EditorStyles.boldLabel) { wordWrap = true };
            var description = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true };
            int shown = 0, matches = 0;
            searchScroll = EditorGUILayout.BeginScrollView(searchScroll, GUILayout.MaxHeight(260));
            foreach (CommandData found in commands)
            {
                if (!Matches(found, words)) continue;
                matches++;
                if (shown >= MaxSearchResults) continue;
                shown++;

                EditorGUILayout.BeginHorizontal();
                bool needsValues = found.ParamCount > 0;
                if (GUILayout.Button(needsValues ? "Use" : "Run", GUILayout.Width(40)))
                {
                    if (needsValues)
                    {
                        // Needs values: hand it to the console line below, ready to complete and run.
                        command = found.CommandName + " ";
                        GUI.FocusControl(null);
                        Log($"'{found.CommandName}{found.ParameterSignature}' is in the console line below: add its values and press Run.");
                    }
                    else Run(found.CommandName);
                }
                EditorGUILayout.BeginVertical();
                EditorGUILayout.LabelField((found.CommandName + " " + found.ParameterSignature).Trim(), signature);
                if (found.HasDescription) EditorGUILayout.LabelField(found.CommandDescription, description);
                EditorGUILayout.EndVertical();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (matches == 0) EditorGUILayout.LabelField("No command matches.", EditorStyles.miniLabel);
            else if (matches > shown) EditorGUILayout.LabelField($"{matches - shown} more: type more of the name.", EditorStyles.miniLabel);
        }

        private static bool Matches(CommandData found, string[] words)
        {
            string name = found.CommandName.ToLowerInvariant();
            string text = found.HasDescription ? found.CommandDescription.ToLowerInvariant() : string.Empty;
            foreach (string word in words)
                if (!name.Contains(word) && !text.Contains(word)) return false;
            return true;
        }

        /// <summary>Every console command, sorted by name; null while Quantum Console is still building its table.</summary>
        private List<CommandData> Searchable()
        {
            if (searchable != null) return searchable;
            if (!QuantumConsoleProcessor.TableGenerated)
            {
                if (!QuantumConsoleProcessor.TableIsGenerating) QuantumConsoleProcessor.GenerateCommandTable();
                if (!QuantumConsoleProcessor.TableGenerated) return null;
            }
            searchable = new List<CommandData>(QuantumConsoleProcessor.GetAllCommands());
            searchable.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.CommandName, b.CommandName);
                return byName != 0 ? byName : a.ParamCount.CompareTo(b.ParamCount);
            });
            return searchable;
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

        [TabGroup(Tabs, CheatsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(252)]
        private void SetDummyHP([MinValue(1)] float health = 2500f) => Run($"gm.dummy.hp {F(health)}");

        [TabGroup(Tabs, CheatsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(253)]
        [InfoBox("Refills the dummy. 0 returns it to its base max HP.")]
        private void SetDummyMaxHP([MinValue(0)] float maxHealth = 5000f) => Run($"gm.dummy.maxhp {F(maxHealth)}");

        [TabGroup(Tabs, CheatsTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Infinite Dummy HP (refills instead of dying)"), PropertyOrder(254)]
        private bool InfiniteDummyHP
        {
            get => GmCommands.DummyInfiniteHealthEnabled;
            set { if (value != GmCommands.DummyInfiniteHealthEnabled) Run($"gm.dummy.inf {(value ? "true" : "false")}"); }
        }

        // ================================================================ SKILLS

        private enum HitStyle { Normal, Heavy, Crit }

        [TabGroup(Tabs, SkillsTab), Title("Skill Points"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(150)]
        private void AddSkillPoints(int amount = 10) => Run($"gm.skillpoints {amount}");

        [TabGroup(Tabs, SkillsTab), Button("Unlock + Max All Skills", ButtonSizes.Large), GUIColor(0.55f, 0.9f, 0.75f), EnableIf(nameof(IsReady)), PropertyOrder(151)]
        [InfoBox("Every combat, buff and movement skill at max rank, book skills unlocked. Ignores level and skill points.")]
        private void MaxAllSkills() => Run("gm.skills.max");

        [ButtonGroup(Tabs + "/" + SkillsTab + "/Books"), EnableIf(nameof(IsReady)), PropertyOrder(152)]
        private void UnlockDropSkills() => Run("gm.dropskills true");

        [ButtonGroup(Tabs + "/" + SkillsTab + "/Books"), EnableIf(nameof(IsReady)), PropertyOrder(153)]
        private void LockDropSkills() => Run("gm.dropskills false");

        [TabGroup(Tabs, SkillsTab), ValueDropdown(nameof(BookSkillChoices)), EnableIf(nameof(IsReady)), LabelText("Book Skill"), PropertyOrder(154)]
        [SerializeField] private string bookSkillChoice;

        [ButtonGroup(Tabs + "/" + SkillsTab + "/BookSkill"), EnableIf(nameof(IsReady)), LabelText("Unlock Skill"), PropertyOrder(155)]
        private void UnlockBookSkill() { if (!string.IsNullOrEmpty(bookSkillChoice)) Run($"gm.unlockskill {bookSkillChoice}"); }

        [ButtonGroup(Tabs + "/" + SkillsTab + "/BookSkill"), EnableIf(nameof(IsReady)), LabelText("Lock Skill"), PropertyOrder(156)]
        private void LockBookSkill() { if (!string.IsNullOrEmpty(bookSkillChoice)) Run($"gm.lockskill {bookSkillChoice}"); }

        [ButtonGroup(Tabs + "/" + SkillsTab + "/BookSkill"), EnableIf(nameof(IsReady)), LabelText("List Skills"), PropertyOrder(157)]
        private void ListSkills() => Run("gm.skills");

        [TabGroup(Tabs, SkillsTab), Title("Hit Preview"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(160)]
        [InfoBox("Hits you with the player's own impact effect. Amount 0 = 10% of max HP. God mode keeps the HP.")]
        private void HitMe([MinValue(0)] float amount = 0f, HitStyle style = HitStyle.Normal, HitSide from = HitSide.Front) =>
            Run($"gm.hitme {F(amount)} {style.ToString().ToLowerInvariant()} {from.ToString().ToLowerInvariant()}");

        private enum HitSide { Front, Back, Left, Right }

        [TabGroup(Tabs, SkillsTab), Title("Active Skills"), ValueDropdown(nameof(ActiveSkillChoices)), EnableIf(nameof(IsReady)), LabelText("Active Skill"), PropertyOrder(170)]
        [InfoBox("Skills (buffs) currently running on you. Removing one takes down its stats, aura and buff-bar entry.")]
        [SerializeField] private string activeSkillChoice;

        [ButtonGroup(Tabs + "/" + SkillsTab + "/ActiveSkill"), EnableIf(nameof(IsReady)), LabelText("Remove Active Skill"), PropertyOrder(171)]
        private void RemoveActiveSkill() { if (!string.IsNullOrEmpty(activeSkillChoice)) Run($"gm.buff.remove {activeSkillChoice}"); }

        [ButtonGroup(Tabs + "/" + SkillsTab + "/ActiveSkill"), EnableIf(nameof(IsReady)), LabelText("Remove All Active Skills"), GUIColor(1f, 0.6f, 0.6f), PropertyOrder(172)]
        private void RemoveAllActiveSkills() => Run("gm.buff.clear");

        private static IEnumerable<ValueDropdownItem<string>> ActiveSkillChoices()
        {
            var player = LocalPlayer;
            var buffs = player != null ? player.GetComponent<CharacterBuffManager>() : null;
            if (buffs == null) yield break;
            foreach (var buff in buffs.ActiveBuffs)
            {
                if (buff?.Skill == null) continue;
                yield return new ValueDropdownItem<string>($"{buff.Skill.displayName}  ({buff.RemainingDuration:0}s left)", buff.Skill.skillId);
            }
        }

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
        [InfoBox("Adds a flat GM bonus on top of everything else (0 removes it). Max HP / Flux pools update on the HUD.")]
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
            get => TargetGm != null && TargetGm.GodMode;
            set { if (value != GodMode) Run($"gm.god {(value ? "true" : "false")}"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Infinite Flux"), PropertyOrder(201)]
        private bool InfiniteChi
        {
            get => TargetGm != null && TargetGm.InfiniteChi;
            set { if (value != InfiniteChi) Run("gm.infchi"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("No Skill Cooldowns"), PropertyOrder(202)]
        private bool NoCooldowns
        {
            get => TargetGm != null && TargetGm.NoCooldowns;
            set { if (value != NoCooldowns) Run("gm.nocd"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("One-Shot (×1000 AP)"), PropertyOrder(203)]
        private bool OneShot
        {
            get => GmCommands.OneShotEnabled;
            set { if (value != GmCommands.OneShotEnabled) Run("gm.oneshot"); }
        }

        [TabGroup(Tabs, CheatsTab), Title("PvP"), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Factions fight each other (everyone)"), PropertyOrder(205.7f)]
        private bool CrossFactionPvp
        {
            get => FactionRules.CrossFactionPvp;
            set { if (value != CrossFactionPvp) Run($"gm.pvp {(value ? "true" : "false")}"); }
        }

        [TabGroup(Tabs, CheatsTab), ShowInInspector, PropertyRange(0.1f, 10f), EnableIf(nameof(IsReady)), LabelText("Move Speed ×"), PropertyOrder(204)]
        private float SpeedMultiplier
        {
            get => TargetGm != null ? TargetGm.SpeedMultiplier : 1f;
            set { if (!Mathf.Approximately(value, SpeedMultiplier)) Run($"gm.speed {F(value)}", log: false); }
        }

        [TabGroup(Tabs, CheatsTab), Title("Game Speed"), ShowInInspector, PropertyRange(0.05f, 2f), EnableIf(nameof(IsReady)), LabelText("Game Speed ×"), PropertyOrder(204.1f)]
        [InfoBox("Slows the whole game (animation, movement, effects) for a close look. 1 = normal.")]
        private float GameSpeed
        {
            get => Application.isPlaying ? Time.timeScale : 1f;
            set { if (!Mathf.Approximately(value, Time.timeScale)) Run($"gm.slowmo {F(value)}", log: false); }
        }

        [ButtonGroup(Tabs + "/" + CheatsTab + "/Speed"), EnableIf(nameof(IsReady)), LabelText("×0.1"), PropertyOrder(204.2f)]
        private void TenthSpeed() => Run("gm.slowmo 0.1");

        [ButtonGroup(Tabs + "/" + CheatsTab + "/Speed"), EnableIf(nameof(IsReady)), LabelText("×0.25"), PropertyOrder(204.3f)]
        private void QuarterSpeed() => Run("gm.slowmo 0.25");

        [ButtonGroup(Tabs + "/" + CheatsTab + "/Speed"), EnableIf(nameof(IsReady)), LabelText("×0.5"), PropertyOrder(204.4f)]
        private void HalfSpeed() => Run("gm.slowmo 0.5");

        [ButtonGroup(Tabs + "/" + CheatsTab + "/Speed"), EnableIf(nameof(IsReady)), LabelText("Normal"), PropertyOrder(204.5f)]
        private void NormalSpeed() => Run("gm.slowmo 1");

        [TabGroup(Tabs, CheatsTab), Title("Presets"), Button(ButtonSizes.Large), GUIColor(0.55f, 0.9f, 0.75f), EnableIf(nameof(IsReady)), PropertyOrder(205)]
        [InfoBox("Max level, god mode, infinite Flux, no cooldowns, full heal.")]
        private void DevLoadout()
        {
            Run("gm.maxlevel");
            if (!GodMode) Run("gm.god true");
            if (!InfiniteChi) Run("gm.infchi");
            if (!NoCooldowns) Run("gm.nocd");
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

        // Travel between maps (gm.travel): the host takes the whole session.
        private const string ZoneCatalogPath = "Assets/_Game/World/Data/Assets/ZoneCatalog.asset";
        [NonSerialized] private string travelZone = "";

        private IEnumerable<ValueDropdownItem<string>> TravelChoices()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<KryticalNova.World.ZoneCatalog>(ZoneCatalogPath);
            if (catalog == null) yield break;
            foreach (var zone in catalog.zones)
                if (zone != null) yield return new ValueDropdownItem<string>($"{zone.kind}/{zone.displayName}  ({zone.LevelBand})", zone.sceneName);
        }

        [TabGroup(Tabs, TeleportTab), Title("Travel to Another Map"), ShowInInspector, ValueDropdown(nameof(TravelChoices)), EnableIf(nameof(IsReady)), LabelText("Map"), PropertyOrder(305)]
        [InfoBox("@\"Now in: \" + CurrentMapLabel + \". In a session the host takes everyone; the travel screen shows the region's concept art and lore.\"")]
        private string TravelZone { get => travelZone; set => travelZone = value; }

        private static string CurrentMapLabel
        {
            get
            {
                var rules = KryticalNova.World.ZoneRules.Current;
                return rules == null ? "(no map)" : rules.Entry != null ? $"{rules.Entry.displayName} ({rules.Entry.LevelBand}, {rules.Entry.pvp})" : rules.gameObject.scene.name;
            }
        }

        [ButtonGroup(Tabs + "/" + TeleportTab + "/Travel"), EnableIf("@IsReady && !string.IsNullOrEmpty(travelZone)"), LabelText("Travel"), PropertyOrder(306)]
        private void TravelGo() => Run($"gm.travel {travelZone}");

        [ButtonGroup(Tabs + "/" + TeleportTab + "/Travel"), EnableIf(nameof(IsReady)), LabelText("Back to Zone01"), PropertyOrder(307)]
        private void TravelHome() => Run("gm.travel Zone01_StarterTown");

        [ButtonGroup(Tabs + "/" + TeleportTab + "/Travel"), EnableIf(nameof(IsReady)), LabelText("List Maps"), PropertyOrder(308)]
        private void TravelList() => Run("gm.zones");

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
        private void SetDayLength([MinValue(10)] float realSeconds = 3600f) => Run($"time.daylength {F(realSeconds)}");

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

        // ---- ladder gear: the same lookups as gm.gear / gm.loadout

        private enum GearSlotChoice { Weapon, Armor, Gloves, Boots, Ring, Amulet, Cape }
        private enum GearRarityChoice { Common, Uncommon, Rare, Epic }
        private enum GearClassChoice
        {
            Current, Monoblade, Glaive, Carbine, Harvester, ServoClaws, ArcCaster, Longaxe, Claymore, Warbow
        }

        [TabGroup(Tabs, ItemsTab), Title("Ladder Gear"), EnableIf(nameof(IsReady)), LabelText("Slot"), PropertyOrder(510)]
        [SerializeField] private GearSlotChoice gearSlot = GearSlotChoice.Weapon;

        [TabGroup(Tabs, ItemsTab), EnableIf(nameof(IsReady)), LabelText("Rarity"), PropertyOrder(511)]
        [SerializeField] private GearRarityChoice gearRarity = GearRarityChoice.Common;

        [TabGroup(Tabs, ItemsTab), ValueDropdown(nameof(GearLevelChoices)), EnableIf(nameof(IsReady)), LabelText("Level"), PropertyOrder(512)]
        [SerializeField] private int gearLevel = 1;

        [TabGroup(Tabs, ItemsTab), EnableIf(nameof(IsReady)), LabelText("Class"), PropertyOrder(513)]
        [InfoBox("Current = the class the player is using. Another class only matters for weapons and loadouts.")]
        [SerializeField] private GearClassChoice gearClass = GearClassChoice.Current;

        private string GearClassArgument => gearClass == GearClassChoice.Current ? string.Empty : " " + gearClass.ToString().ToLowerInvariant();

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Gear"), EnableIf(nameof(IsReady)), PropertyOrder(514)]
        private void GiveGear() =>
            Run($"gm.gear {gearSlot.ToString().ToLowerInvariant()} {gearLevel} {gearRarity.ToString().ToLowerInvariant()}{GearClassArgument}");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Gear"), EnableIf(nameof(IsReady)), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(515)]
        private void StarterLoadout() => Run($"gm.loadout{GearClassArgument}");

        /// <summary>The tiers that exist for the chosen slot and rarity, so the level can only be a real band step.</summary>
        private IEnumerable<ValueDropdownItem<int>> GearLevelChoices()
        {
            var inv = LocalInventory;
            if (inv == null || inv.Gear == null) yield break;
            var seen = new SortedSet<int>();
            string slotName = gearSlot.ToString(), rarityName = gearRarity.ToString();
            foreach (var entry in inv.Gear.Entries)
                if (entry.slot.ToString() == slotName && entry.rarity.ToString() == rarityName) seen.Add(entry.itemLevel);
            foreach (int level in seen) yield return new ValueDropdownItem<int>($"Level {level}", level);
        }

        // ---- a whole set: the same lookup as gm.gearset

        [TabGroup(Tabs, ItemsTab), Title("Whole Gear Set"), ValueDropdown(nameof(GearSetChoices)), EnableIf(nameof(IsReady)), LabelText("Set"), PropertyOrder(520)]
        [InfoBox("The sets this character can wear. One click gives Armor, Gloves, Boots, Ring, Amulet, the Cape when the set has one, " +
                 "and the weapon of the class in use, all at the Temper % below. A character below the set's level gets it in the " +
                 "Pack instead of on: raise its level first (Player tab).")]
        [SerializeField] private int gearSetLine;

        [TabGroup(Tabs, ItemsTab), ValueDropdown(nameof(GearSetLevelChoices)), EnableIf(nameof(IsReady)), LabelText("Level"), PropertyOrder(521)]
        [SerializeField] private int gearSetLevel = 112;

        [TabGroup(Tabs, ItemsTab), PropertyRange(0, nameof(TemperCap)), EnableIf(nameof(IsReady)), LabelText("Temper %"), PropertyOrder(522)]
        [SerializeField] private int gearTemper;

        [TabGroup(Tabs, ItemsTab), ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Put it on (what is worn goes to the Pack)"), PropertyOrder(523)]
        [SerializeField] private bool gearSetWear = true;

        [TabGroup(Tabs, ItemsTab), Button("Give Whole Set", ButtonSizes.Large), GUIColor(0.55f, 0.9f, 0.75f), EnableIf(nameof(IsReady)), PropertyOrder(524)]
        private void GiveGearSet()
        {
            var inv = LocalInventory;
            if (inv == null || inv.Gear == null || !inv.Gear.TryGetLine((ushort)gearSetLine, out var line) || !FitsTarget(line))
            {
                Log("Pick a set first.");
                return;
            }
            int level = gearSetLevel;
            if (!LineHasLevel(inv.Gear, line.lineId, level)) level = LowestLevel(inv.Gear, line.lineId);
            Run($"gm.gearset {level} {line.rarity.ToString().ToLowerInvariant()} {gearTemper}{(gearSetWear ? " wear" : string.Empty)}");
        }

        // ---- Temper on gear the player already has: gm.temper.all

        [TabGroup(Tabs, ItemsTab), Title("Temper"), ShowInInspector, DisplayAsString, LabelText("Worn Now"), PropertyOrder(530)]
        [InfoBox("Sets the Temper % above on gear the player already has. A Temper of 0 clears it.")]
        private string WornTemper
        {
            get
            {
                var inv = LocalInventory;
                if (inv == null || inv.Catalog == null) return "(enter Play Mode)";
                var sb = new StringBuilder();
                foreach (EquipSlot slot in EquipSlots.All)
                {
                    ItemStack stack = inv.GetEquipped(slot);
                    ItemDefinition item = stack.IsEmpty ? null : inv.Catalog.Get(stack.itemId);
                    if (item == null || !item.IsLadderGear) continue;
                    if (sb.Length > 0) sb.Append("  ·  ");
                    sb.Append(slot).Append(" +").Append(GearItemState.Of(stack).Temper).Append('%');
                }
                return sb.Length > 0 ? sb.ToString() : "no gear worn";
            }
        }

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Temper"), EnableIf(nameof(IsReady)), LabelText("Set On Worn Gear"), PropertyOrder(531)]
        private void TemperWorn() => Run($"gm.temper.all {gearTemper} worn");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Temper"), EnableIf(nameof(IsReady)), LabelText("Set On Pack Gear"), PropertyOrder(532)]
        private void TemperPack() => Run($"gm.temper.all {gearTemper} pack");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Temper"), EnableIf(nameof(IsReady)), LabelText("Set On Everything"), PropertyOrder(533)]
        private void TemperEverything() => Run($"gm.temper.all {gearTemper} all");

        /// <summary>How far Temper goes today (the forge's cap), so the slider cannot ask for more.</summary>
        private static int TemperCap
        {
            get
            {
                var work = LocalPlayer != null ? LocalPlayer.GetComponent<PlayerGearWork>() : null;
                return work != null && work.Settings != null ? work.Settings.temper.hardCap : 120;
            }
        }

        /// <summary>True when a gear line is one the targeted character wears: its own outfit group, faction and body.</summary>
        private static bool FitsTarget(GearDictionary.Line line)
        {
            var wearer = LocalPlayer != null ? LocalPlayer.GetComponent<IGearWearer>() : null;
            if (wearer == null) return true;
            if (wearer.Faction != FactionId.None && !FactionRules.Includes(line.factions, wearer.Faction)) return false;
            if (wearer.Body != BodyFit.None && (line.bodies & wearer.Body) == 0) return false;
            return string.IsNullOrEmpty(wearer.GearPack) || string.IsNullOrEmpty(line.pack) || line.pack == wearer.GearPack;
        }

        private static bool LineHasLevel(GearDictionary gear, ushort lineId, int level)
        {
            foreach (var entry in gear.Entries)
                if (entry.lineId == lineId && entry.itemLevel == level) return true;
            return false;
        }

        private static int LowestLevel(GearDictionary gear, ushort lineId)
        {
            int lowest = int.MaxValue;
            foreach (var entry in gear.Entries)
                if (entry.lineId == lineId && entry.itemLevel < lowest) lowest = entry.itemLevel;
            return lowest == int.MaxValue ? 1 : lowest;
        }

        /// <summary>One choice per set this character can wear, named as the game names it: "Sunlord  (Epic 100–112)".</summary>
        private static IEnumerable<ValueDropdownItem<int>> GearSetChoices()
        {
            var inv = LocalInventory;
            if (inv == null || inv.Gear == null) yield break;
            foreach (var line in inv.Gear.Lines)
            {
                if (line.slot != EquipSlot.Armor || !FitsTarget(line)) continue;
                int lowest = int.MaxValue, highest = int.MinValue;
                foreach (var entry in inv.Gear.Entries)
                {
                    if (entry.lineId != line.lineId) continue;
                    lowest = Mathf.Min(lowest, entry.itemLevel);
                    highest = Mathf.Max(highest, entry.itemLevel);
                }
                if (lowest == int.MaxValue) continue;
                string name = string.IsNullOrEmpty(line.set) ? line.displayName : line.set;
                yield return new ValueDropdownItem<int>($"{name}  ({line.rarity} {lowest}–{highest})", line.lineId);
            }
        }

        /// <summary>The level tiers of the chosen set.</summary>
        private IEnumerable<ValueDropdownItem<int>> GearSetLevelChoices()
        {
            var inv = LocalInventory;
            if (inv == null || inv.Gear == null) yield break;
            var seen = new SortedSet<int>();
            foreach (var entry in inv.Gear.Entries)
                if (entry.lineId == gearSetLine) seen.Add(entry.itemLevel);
            foreach (int level in seen) yield return new ValueDropdownItem<int>($"Level {level}", level);
        }

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

        // ================================================================ ECONOMY (Creds, storage, trade, Kiosks, saving)

        [TabGroup(Tabs, EconomyTab), Title("Creds"), MinValue(0), EnableIf(nameof(IsReady)), LabelText("Amount"), PropertyOrder(600)]
        [InfoBox("Until Hostile drops, Contracts and shops exist, this is the only way Creds enter the game.")]
        [SerializeField] private long credsAmount = 1_000_000;

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Creds"), EnableIf(nameof(IsReady)), LabelText("Give"), PropertyOrder(601)]
        private void GiveCreds() => Run($"gm.creds {credsAmount}");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Creds"), EnableIf(nameof(IsReady)), LabelText("Take"), PropertyOrder(602)]
        private void TakeCreds() => Run($"gm.creds -{credsAmount}");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Creds"), EnableIf(nameof(IsReady)), LabelText("Set Balance"), PropertyOrder(603)]
        private void SetCreds() => Run($"gm.creds.set {credsAmount}");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Creds"), EnableIf(nameof(IsReady)), LabelText("Ledger"), PropertyOrder(604)]
        private void CredsReport() => Run("gm.creds.report");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Storage"), EnableIf(nameof(IsReady)), LabelText("Open Vault Here"), PropertyOrder(610)]
        private void OpenVault() => Run("gm.vault");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Storage"), EnableIf(nameof(IsReady)), LabelText("Open Deep Vault Here"), PropertyOrder(611)]
        private void OpenDeepVault() => Run("gm.vault deep");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Storage"), EnableIf(nameof(IsReady)), LabelText("Storage Needs Its NPC Again"), PropertyOrder(612)]
        private void VaultRuleBack() => Run("gm.vault off");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Trade"), EnableIf(nameof(IsReady)), LabelText("Reset Daily Trade Limit"), PropertyOrder(620)]
        private void ResetTradeLimit() => Run("gm.trade.reset");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Trade"), EnableIf(nameof(IsReady)), LabelText("List Kiosks"), PropertyOrder(621)]
        private void ListKiosks() => Run("gm.kiosk");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Trade"), EnableIf(nameof(IsReady)), LabelText("Expire Player's Kiosk"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(622)]
        private void ExpireKiosk() => Run("gm.kiosk.expire");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Save"), EnableIf(nameof(IsReady)), LabelText("Save Everyone Now"), PropertyOrder(630)]
        private void SaveNow() => Run("gm.save");

        [ButtonGroup(Tabs + "/" + EconomyTab + "/Save"), EnableIf(nameof(IsReady)), LabelText("Where Are The Saves?"), PropertyOrder(631)]
        private void SaveWhere() => Run("gm.save.where");

        // ================================================================ SOCIAL (Crews, Contacts)

        [TabGroup(Tabs, SocialTab), Title("Sponsor / Recruit"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(690)]
        [InfoBox("Links two connected players at once, whatever their levels: the higher sponsors. Client ids are in the player list at the top.")]
        private void LinkSponsor([MinValue(0)] int clientIdA = 0, [MinValue(0)] int clientIdB = 1) => Run($"gm.sponsor {clientIdA} {clientIdB}");

        [ButtonGroup(Tabs + "/" + SocialTab + "/Sponsor"), EnableIf(nameof(IsReady)), LabelText("Player's Links"), PropertyOrder(691)]
        private void ShowSponsor() => Run("gm.sponsor");

        [ButtonGroup(Tabs + "/" + SocialTab + "/Sponsor"), EnableIf(nameof(IsReady)), LabelText("Open The Window"), PropertyOrder(692)]
        private void OpenSponsorWindow() => Run("gm.sponsor.window");

        [ButtonGroup(Tabs + "/" + SocialTab + "/Sponsor"), EnableIf(nameof(IsReady)), LabelText("End Every Link"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(693)]
        private void EndSponsorLinks() => Run("gm.sponsor.end");

        [TabGroup(Tabs, SocialTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(694)]
        private void SetSponsorPoints([MinValue(0)] int points = 1000) => Run($"gm.sponsor.points {points}");

        [TabGroup(Tabs, SocialTab), Title("Crew"), EnableIf(nameof(IsReady)), LabelText("Crew Name"), PropertyOrder(700)]
        [InfoBox("Puts the targeted player in that Crew, founding it when there is none: no fee, level or Vaultkeeper.")]
        [SerializeField] private string crewName = "Test Crew";

        [ButtonGroup(Tabs + "/" + SocialTab + "/Crew"), EnableIf(nameof(IsReady)), LabelText("Join / Found"), PropertyOrder(701)]
        private void JoinCrew()
        {
            if (string.IsNullOrWhiteSpace(crewName)) { Log("Type a Crew name first."); return; }
            Run($"gm.guild {Quote(crewName)}");
        }

        [ButtonGroup(Tabs + "/" + SocialTab + "/Crew"), EnableIf(nameof(IsReady)), LabelText("Take Out Of Crew"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(702)]
        private void LeaveCrew() => Run("gm.guild none");

        [ButtonGroup(Tabs + "/" + SocialTab + "/Crew"), EnableIf(nameof(IsReady)), LabelText("Player's Crew"), PropertyOrder(703)]
        private void ShowCrew() => Run("gm.guild");

        [ButtonGroup(Tabs + "/" + SocialTab + "/Crew"), EnableIf(nameof(IsReady)), LabelText("List Crews"), PropertyOrder(704)]
        private void ListCrews() => Run("gm.guild.list");

        [TabGroup(Tabs, SocialTab), Title("Neutral Ground"), ShowInInspector, DisplayAsString, LabelText("Player Stands On"), PropertyOrder(710)]
        [InfoBox("On neutral ground (a Trade Zone with Neutral Ground ticked) players of different factions may trade, buy from each " +
                 "other's Kiosks and add each other as contacts, and fight each other only in a Callout, whatever Cross-Faction PvP " +
                 "says. A Squad and a Crew are one faction everywhere.")]
        private string NeutralGround => LocalPlayer == null ? "(enter Play Mode)"
            : KryticalNova.Economy.TradeZone.CrossFactionAt(LocalPlayer.transform.position) ? "neutral ground" : "faction ground";

        // ================================================================ PETS

        [TabGroup(Tabs, PetsTab), Title("The Pet That Is Out"), PropertyRange(0f, 100f), EnableIf(nameof(IsReady)), LabelText("Growth %"), PropertyOrder(800)]
        [InfoBox("Evolutions happen at 25, 50, 75 and 100. Growth and hunger travel with the pet item.")]
        [SerializeField] private float petGrowth = 25f;

        [ButtonGroup(Tabs + "/" + PetsTab + "/Growth"), EnableIf(nameof(IsReady)), LabelText("Set Growth"), PropertyOrder(801)]
        private void SetPetGrowth() => Run($"gm.pet.growth {petGrowth.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        [ButtonGroup(Tabs + "/" + PetsTab + "/Growth"), EnableIf(nameof(IsReady)), LabelText("Status"), PropertyOrder(802)]
        private void PetStatus() => Run("gm.pet");

        [TabGroup(Tabs, PetsTab), PropertyRange(0f, 100f), EnableIf(nameof(IsReady)), LabelText("Hunger %"), PropertyOrder(810)]
        [SerializeField] private float petHunger = 100f;

        [ButtonGroup(Tabs + "/" + PetsTab + "/Hunger"), EnableIf(nameof(IsReady)), LabelText("Set Hunger"), PropertyOrder(811)]
        private void SetPetHunger() => Run($"gm.pet.hunger {petHunger.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        [ButtonGroup(Tabs + "/" + PetsTab + "/Hunger"), EnableIf(nameof(IsReady)), LabelText("Feed To Full"), PropertyOrder(812)]
        private void FeedPet() => Run("gm.pet.feed");

        [ButtonGroup(Tabs + "/" + PetsTab + "/Hunger"), EnableIf(nameof(IsReady)), LabelText("Give 5 Pet Rations"), PropertyOrder(813)]
        private void GivePetRations() => Run("item.give Pet_Ration 5");

        [ButtonGroup(Tabs + "/" + PetsTab + "/Event"), EnableIf(nameof(IsReady)), LabelText("PvP Event Win (50 XP)"), PropertyOrder(820)]
        private void PetEventWin() => Run("gm.pet.event win");

        [ButtonGroup(Tabs + "/" + PetsTab + "/Event"), EnableIf(nameof(IsReady)), LabelText("PvP Event Loss (25 XP)"), PropertyOrder(821)]
        private void PetEventLoss() => Run("gm.pet.event loss");

        [TabGroup(Tabs, PetsTab), Title("Animations"), ValueDropdown(nameof(PetAnimationChoices)), EnableIf(nameof(IsReady)), LabelText("Animation"), PropertyOrder(830)]
        [InfoBox("The animations of the pet that is out. A pet plays them in turn by itself (Pet Settings, Animation Cycle Seconds) " +
                 "and rests in between; playing one here shows it to everyone at once.")]
        [SerializeField] private string petAnimation = "";

        private static IEnumerable<string> PetAnimationChoices()
        {
            var pet = LocalPlayer != null ? LocalPlayer.GetComponent<KryticalNova.Pets.PlayerPet>() : null;
            return pet != null ? pet.AnimationNames : System.Array.Empty<string>();
        }

        [ButtonGroup(Tabs + "/" + PetsTab + "/Animation"), EnableIf(nameof(IsReady)), LabelText("Play"), PropertyOrder(831)]
        private void PlayPetAnimation()
        {
            if (string.IsNullOrWhiteSpace(petAnimation)) { Log("Pick an animation first."); return; }
            Run($"gm.pet.anim {petAnimation}");
        }

        [ButtonGroup(Tabs + "/" + PetsTab + "/Animation"), EnableIf(nameof(IsReady)), LabelText("List"), PropertyOrder(832)]
        private void ListPetAnimations() => Run("gm.pet.anims");

        // ================================================================ RENOWN (Renown, Ascension, Nova Standings)

        private static PlayerNova TargetNova => LocalPlayer != null ? LocalPlayer.GetComponent<PlayerNova>() : null;

        [TabGroup(Tabs, RenownTab), Title("Renown"), ShowInInspector, DisplayAsString, LabelText("Held"), PropertyOrder(900)]
        [InfoBox("Nothing in the game pays Renown yet, so this is the only way to get it. It is spent on Tuning at the forge, " +
                 "and on Ascension and Nova Standings at the Herald.")]
        private string RenownHeld => Progression != null ? Progression.Renown.ToString("N0") : "(enter Play Mode)";

        [TabGroup(Tabs, RenownTab), MinValue(0), EnableIf(nameof(IsReady)), LabelText("Amount"), PropertyOrder(901)]
        [SerializeField] private int renownAmount = 10_000;

        [ButtonGroup(Tabs + "/" + RenownTab + "/Renown"), EnableIf(nameof(IsReady)), LabelText("Give"), PropertyOrder(902)]
        private void GiveRenown() => Run($"gm.renown {renownAmount}");

        [ButtonGroup(Tabs + "/" + RenownTab + "/Renown"), EnableIf(nameof(IsReady)), LabelText("Take"), PropertyOrder(903)]
        private void TakeRenown() => Run($"gm.renown -{renownAmount}");

        [ButtonGroup(Tabs + "/" + RenownTab + "/Renown"), EnableIf(nameof(IsReady)), LabelText("Set Balance"), PropertyOrder(904)]
        private void SetRenown() => Run($"gm.renown.set {renownAmount}");

        // ---- Ascension

        [TabGroup(Tabs, RenownTab), Title("Ascension"), ShowInInspector, DisplayAsString, LabelText("Now"), PropertyOrder(910)]
        private string AscensionNow
        {
            get
            {
                PlayerNova nova = TargetNova;
                if (nova == null || nova.Settings == null) return "(enter Play Mode)";
                return NovaRules.AscensionLine(nova.Settings.ascension, nova.Ascension);
            }
        }

        [TabGroup(Tabs, RenownTab), PropertyRange(0, nameof(AscensionCap)), EnableIf(nameof(IsReady)), LabelText("Level +"), PropertyOrder(911)]
        [InfoBox("Each level is +1 Strength, Agility, Vitality and Spirit. Every 8 levels is the next Nova.")]
        [SerializeField] private int ascensionLevel = 8;

        [ButtonGroup(Tabs + "/" + RenownTab + "/Ascension"), EnableIf(nameof(IsReady)), LabelText("Set Level"), PropertyOrder(912)]
        private void SetAscension() => Run($"gm.ascension {ascensionLevel}");

        [ButtonGroup(Tabs + "/" + RenownTab + "/Ascension"), EnableIf(nameof(IsReady)), LabelText("None (+0)"), PropertyOrder(913)]
        private void ClearAscension() => Run("gm.ascension 0");

        [ButtonGroup(Tabs + "/" + RenownTab + "/Ascension"), EnableIf(nameof(IsReady)), LabelText("Max"), PropertyOrder(914)]
        private void MaxAscension() => Run($"gm.ascension {AscensionCap}");

        [TabGroup(Tabs, RenownTab), PropertyRange(1, nameof(NovaTierCount)), EnableIf(nameof(IsReady)), LabelText("Nova Tier"), PropertyOrder(915)]
        [SerializeField] private int novaTier = 1;

        [TabGroup(Tabs, RenownTab), Button("Set To The First Level Of That Nova"), EnableIf(nameof(IsReady)), PropertyOrder(916)]
        private void SetNovaTier()
        {
            PlayerNova nova = TargetNova;
            int perTier = nova != null && nova.Settings != null ? nova.Settings.ascension.levelsPerTier : 8;
            Run($"gm.ascension {(Mathf.Max(1, novaTier) - 1) * perTier + 1}");
        }

        private static int AscensionCap => TargetNova != null && TargetNova.Settings != null ? TargetNova.Settings.ascension.maxLevel : 96;
        private static int NovaTierCount => TargetNova != null && TargetNova.Settings != null ? NovaRules.TierCount(TargetNova.Settings.ascension) : 12;

        // ---- Nova Standings

        private enum StandingChoice { All, Vanguard, Bulwark, Ironblood, Mindwell }

        [TabGroup(Tabs, RenownTab), Title("Nova Standings"), ShowInInspector, DisplayAsString, LabelText("Now"), PropertyOrder(920)]
        private string StandingsNow
        {
            get
            {
                PlayerNova nova = TargetNova;
                if (nova == null || nova.Settings == null) return "(enter Play Mode)";
                NovaState state = nova.State;
                var sb = new StringBuilder();
                foreach (StandingTrackInfo info in nova.Settings.standings.tracks)
                    sb.Append(info.name).Append(' ').Append(state.Standing(info.track)).Append("  ·  ");
                return sb.Append("worn: ").Append(nova.WornStanding ?? "none").ToString();
            }
        }

        [TabGroup(Tabs, RenownTab), EnableIf(nameof(IsReady)), LabelText("Track"), PropertyOrder(921)]
        [InfoBox("Every track the player holds a rank on raises its stat by 1 % a rank, whichever one is worn.")]
        [SerializeField] private StandingChoice standingTrack = StandingChoice.All;

        [TabGroup(Tabs, RenownTab), PropertyRange(0, nameof(StandingCap)), EnableIf(nameof(IsReady)), LabelText("Rank"), PropertyOrder(922)]
        [SerializeField] private int standingRank = 12;

        [ButtonGroup(Tabs + "/" + RenownTab + "/Standing"), EnableIf(nameof(IsReady)), LabelText("Set Rank"), PropertyOrder(923)]
        private void SetStanding() => Run($"gm.standing {standingTrack.ToString().ToLowerInvariant()} {standingRank}");

        [ButtonGroup(Tabs + "/" + RenownTab + "/Standing"), EnableIf(nameof(CanWearStanding)), LabelText("Wear This Track"), PropertyOrder(924)]
        private void WearStanding() => Run($"gm.standing.wear {standingTrack.ToString().ToLowerInvariant()}");

        [ButtonGroup(Tabs + "/" + RenownTab + "/Standing"), EnableIf(nameof(IsReady)), LabelText("Wear None"), PropertyOrder(925)]
        private void WearNoStanding() => Run("gm.standing.wear none");

        private bool CanWearStanding => IsReady && standingTrack != StandingChoice.All;
        private static int StandingCap => TargetNova != null && TargetNova.Settings != null ? TargetNova.Settings.standings.MaxLevel : 12;

        // ---- both at once, and the Herald

        [ButtonGroup(Tabs + "/" + RenownTab + "/Both"), EnableIf(nameof(IsReady)), LabelText("Max Both Ladders"), GUIColor(0.55f, 0.9f, 0.75f), PropertyOrder(930)]
        private void MaxLadders()
        {
            Run($"gm.ascension {AscensionCap}");
            Run($"gm.standing all {StandingCap}");
        }

        [ButtonGroup(Tabs + "/" + RenownTab + "/Both"), EnableIf(nameof(IsReady)), LabelText("Clear Both Ladders"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(931)]
        private void ClearLadders()
        {
            Run("gm.ascension 0");
            Run("gm.standing all 0");
        }

        [ButtonGroup(Tabs + "/" + RenownTab + "/Herald"), EnableIf(nameof(IsReady)), LabelText("Open Herald Here"), PropertyOrder(940)]
        private void OpenHerald() => Run("gm.herald");

        [ButtonGroup(Tabs + "/" + RenownTab + "/Herald"), EnableIf(nameof(IsReady)), LabelText("Herald Needs Its NPC Again"), PropertyOrder(941)]
        private void HeraldRuleBack() => Run("gm.herald off");

        // ================================================================ buttons for the commands that had none
        // Every console command has a button somewhere in this window. These sit at the end of the tab they belong to.

        [TabGroup(Tabs, PlayerTab), Title("Respawn and Who Is Here"), Button("Respawn Now"), EnableIf(nameof(IsReady)), PropertyOrder(14)]
        private void RespawnNow() => Run("gm.respawn");

        [TabGroup(Tabs, PlayerTab), Button("List Connected Players"), EnableIf(nameof(IsReady)), PropertyOrder(15)]
        private void ListPlayers() => Run("gm.players");

        [TabGroup(Tabs, SkillsTab), Button("List Active Skills"), EnableIf(nameof(IsReady)), PropertyOrder(173)]
        private void ListActiveSkills() => Run("gm.buffs");

        private enum FxKind { Normal, Elemental, Crit, Shatter, Backlash }
        private enum FxWeight { Light, Heavy }
        private enum PvpCritChoice { Splash, Frame, Faction, Default }

        [TabGroup(Tabs, SkillsTab), Title("Effect Previews"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(174)]
        [InfoBox("Shows what a hit of yours looks like on the nearest target, with the weapon you hold. Nothing is hit.")]
        private void PreviewHit(FxKind kind = FxKind.Normal, FxWeight weight = FxWeight.Heavy) =>
            Run($"gm.fx.hit {kind.ToString().ToLowerInvariant()} {weight.ToString().ToLowerInvariant()}");

        [TabGroup(Tabs, SkillsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(175)]
        [InfoBox("The look of a critical hit on another player, on this screen until the game is closed.")]
        private void PvpCritLook(PvpCritChoice look = PvpCritChoice.Default) => Run($"gm.fx.pvpcrit {look.ToString().ToLowerInvariant()}");

        [TabGroup(Tabs, CheatsTab), Title("Callouts"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(255)]
        [InfoBox("Starts a Callout between two connected players at once, without a request. The ids are in List Connected Players (Player tab).")]
        private void StartCallout([MinValue(0)] int clientIdA = 0, [MinValue(0)] int clientIdB = 1) => Run($"gm.duel {clientIdA} {clientIdB}");

        [TabGroup(Tabs, CheatsTab), Button("End The Player's Callout"), EnableIf(nameof(IsReady)), PropertyOrder(256)]
        private void EndCallout() => Run("gm.duel.end");

        [TabGroup(Tabs, TeleportTab), Button("List Teleport Targets"), EnableIf(nameof(IsReady)), PropertyOrder(305)]
        private void ListPointsOfInterest() => Run("gm.poi");

        [TabGroup(Tabs, TeleportTab), Button("Teleport to Mouse Cursor"), EnableIf(nameof(IsReady)), PropertyOrder(306)]
        [InfoBox("Uses the ground under the mouse in the Game view. Pressed here the mouse is over this window, so it only finds " +
                 "a place when this window overlaps the Game view; typed in the console it works anywhere.")]
        private void TeleportToCursor() => Run("gm.tpcursor");

        [TabGroup(Tabs, WorldTab), Button("Print Wind"), EnableIf(nameof(IsReady)), PropertyOrder(432.8f)]
        private void PrintWind() => Run("weather.wind");

        [TabGroup(Tabs, WorldTab), Button("Grass: Only What The Camera Sees (toggle)"), EnableIf(nameof(IsReady)), PropertyOrder(432.9f)]
        private void ToggleGrassView() => Run("grass.view");

        [TabGroup(Tabs, WorldTab), Title("Lists"), Button("Print Time and Weather"), EnableIf(nameof(IsReady)), PropertyOrder(433)]
        private void PrintTime() => Run("time.get");

        [TabGroup(Tabs, WorldTab), Button("List Weather Profiles"), EnableIf(nameof(IsReady)), PropertyOrder(434)]
        private void ListWeather() => Run("weather.list");

        private enum GrassDebugView { Off = 0, Placement = 1, LodBands = 2, Biomes = 3 }

        [ButtonGroup(Tabs + "/" + WorldTab + "/Grass"), Title("Grass"), EnableIf(nameof(IsReady)), LabelText("Grass On"), PropertyOrder(435)]
        private void GrassOn() => Run("grass.toggle true");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Grass"), EnableIf(nameof(IsReady)), LabelText("Grass Off"), PropertyOrder(436)]
        private void GrassOff() => Run("grass.toggle false");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Grass"), EnableIf(nameof(IsReady)), LabelText("Fill Tufts On"), PropertyOrder(437)]
        private void GrassFillOn() => Run("grass.fill true");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Grass"), EnableIf(nameof(IsReady)), LabelText("Fill Tufts Off"), PropertyOrder(438)]
        private void GrassFillOff() => Run("grass.fill false");

        [ButtonGroup(Tabs + "/" + WorldTab + "/Grass"), EnableIf(nameof(IsReady)), LabelText("Grass Stats"), PropertyOrder(439)]
        private void GrassStats() => Run("grass.stats");

        [TabGroup(Tabs, WorldTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(440)]
        private void GrassDensity([MinValue(0)] float scale = 1f) => Run($"grass.density {F(scale)}");

        [TabGroup(Tabs, WorldTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(441)]
        private void GrassDistance([MinValue(20), MaxValue(150)] float metres = 80f) => Run($"grass.distance {F(metres)}");

        [TabGroup(Tabs, WorldTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(442)]
        private void GrassDebug(GrassDebugView view = GrassDebugView.Off) => Run($"grass.debug {(int)view}");

        private enum KitRarity { Common, Uncommon, Rare, Epic }
        private enum ForgeSlot { FirstInPack, Weapon, Armor, Gloves, Boots, Ring, Amulet, Cape }
        private enum SerumKind { Strength, Vitality, Agility, Spirit, Strike, Ward }

        private static string SlotArgument(ForgeSlot slot) => slot == ForgeSlot.FirstInPack ? string.Empty : " " + slot.ToString().ToLowerInvariant();

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Kits"), Title("Kits"), EnableIf(nameof(IsReady)), LabelText("Everything (gear, Temper items, pets)"), PropertyOrder(534)]
        private void KitAll() => Run("gm.kit all");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Kits"), EnableIf(nameof(IsReady)), LabelText("Every Pet"), PropertyOrder(535)]
        private void KitPets() => Run("gm.kit pets");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(536)]
        [InfoBox("Every piece the character can wear at that tier, with the weapon of every class of its faction.")]
        private void KitGear([MinValue(1)] int level = 112, KitRarity rarity = KitRarity.Epic) => Run($"gm.kit gear {level} {rarity.ToString().ToLowerInvariant()}");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(537)]
        private void KitTemper([MinValue(1)] int countOfEach = 20) => Run($"gm.kit temper {countOfEach}");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Forge"), Title("Forge"), EnableIf(nameof(IsReady)), LabelText("Open Forge Here"), PropertyOrder(538)]
        private void OpenForge() => Run("gm.forge");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Forge"), EnableIf(nameof(IsReady)), LabelText("Forge Rule Back"), PropertyOrder(539)]
        private void ForgeRuleBack() => Run("gm.forge off");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(540)]
        [InfoBox("Alloys, Failsafes, the Fusion Catalyst and the Tuning chips.")]
        private void ForgeKit([MinValue(1)] int countOfEach = 20) => Run($"gm.forgekit {countOfEach}");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(541)]
        private void SetFusion([MinValue(0), MaxValue(12)] int stage = 1, ForgeSlot slot = ForgeSlot.FirstInPack) => Run($"gm.fuse {stage}{SlotArgument(slot)}");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(542)]
        private void SetTuning([MinValue(0), MaxValue(25)] int stage = 1, ForgeSlot slot = ForgeSlot.FirstInPack) => Run($"gm.tune {stage}{SlotArgument(slot)}");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(542.1f)]
        [InfoBox("Sets the Temper % of the first piece of gear in the Pack (or the first of that slot), up to the cap.")]
        private void SetTemperOfOne([MinValue(0), MaxValue(120)] int percent = 60, ForgeSlot slot = ForgeSlot.FirstInPack) => Run($"gm.temper {percent}{SlotArgument(slot)}");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(542.2f)]
        [InfoBox("Puts an item on the ground in front of the player, free for anyone to take: an item id, asset name or display name.")]
        private void DropOnGround(string item = "Pill_Vitality", [MinValue(1)] int count = 1) { if (!string.IsNullOrWhiteSpace(item)) Run($"gm.drop {Quote(item.Trim())} {count}"); }

        [TabGroup(Tabs, ItemsTab), Title("Serums"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(543)]
        private void GiveSerums([MinValue(1)] int countOfEach = 10) => Run($"gm.serums {countOfEach}");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(544)]
        [InfoBox("Sets how many of that kind the player has taken. It stops at the cap.")]
        private void SetSerumsTaken(SerumKind kind = SerumKind.Strength, [MinValue(0)] int taken = 0) => Run($"gm.serum {kind.ToString().ToLowerInvariant()} {taken}");

        [TabGroup(Tabs, ItemsTab), Button("List Serums Taken"), EnableIf(nameof(IsReady)), PropertyOrder(545)]
        private void ListSerums() => Run("gm.serum");

        [TabGroup(Tabs, ItemsTab), Title("Appearance: Holoweave, dye, hair, face, build"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(545.1f)]
        [InfoBox("Chroma Cells (one dyes one outfit piece), Prism Cells (one dyes one weapon), Restyle Passes (Stylist, Reshaper) and Training Passes (Coach). The two Passes have no other source until the Nova Store exists.")]
        private void GivePasses([MinValue(1)] int countOfEach = 10) => Run($"gm.passes {countOfEach}");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Appearance"), EnableIf(nameof(IsReady)), LabelText("Holoweaver Here"), PropertyOrder(545.2f)]
        private void OpenHoloweaver() => Run("gm.weave");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Appearance"), EnableIf(nameof(IsReady)), LabelText("Outfitter (Dye) Here"), PropertyOrder(545.21f)]
        private void OpenOutfitter() => Run("gm.dye");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Appearance"), EnableIf(nameof(IsReady)), LabelText("Unlock Every Weave"), PropertyOrder(545.22f)]
        private void UnlockEveryWeave() => Run("gm.weave.unlock all");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Appearance"), EnableIf(nameof(IsReady)), LabelText("Unlock Every Morph"), PropertyOrder(545.225f)]
        private void UnlockEveryMorph() => Run("gm.weave.unlock morphs");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Appearance"), EnableIf(nameof(IsReady)), LabelText("What It Shows"), PropertyOrder(545.23f)]
        private void ListWeaves() => Run("gm.weave.list");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Appearance"), EnableIf(nameof(IsReady)), LabelText("Empty Weave Library"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(545.24f)]
        private void ClearWeaves() => Run("gm.weave.clear");

        [TabGroup(Tabs, ItemsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(545.25f)]
        [InfoBox("Adds the look of one item to the Weave Library: an item id, asset name or display name.")]
        private void UnlockWeaveOf(string item = "") { if (!string.IsNullOrWhiteSpace(item)) Run($"gm.weave.unlock {Quote(item.Trim())}"); }

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Restyle"), EnableIf(nameof(IsReady)), LabelText("Stylist Here"), PropertyOrder(545.3f)]
        private void OpenStylist() => Run("gm.restyle stylist");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Restyle"), EnableIf(nameof(IsReady)), LabelText("Reshaper Here"), PropertyOrder(545.31f)]
        private void OpenReshaper() => Run("gm.restyle reshaper");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Restyle"), EnableIf(nameof(IsReady)), LabelText("Coach Here"), PropertyOrder(545.32f)]
        private void OpenCoach() => Run("gm.restyle coach");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Restyle"), EnableIf(nameof(IsReady)), LabelText("NPCs Needed Again"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(545.33f)]
        private void AppearanceNeedsNpcs()
        {
            Run("gm.weave off");
            Run("gm.restyle off");
        }

        [TabGroup(Tabs, ItemsTab), Title("Crafting (the Fabricator)"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(545.4f)]
        [InfoBox("What every recipe uses up, that many times over: the four colour cells, every Dossier Page, and Alumite, Iridion and Orichalc by the four.")]
        private void GiveCraftKit([MinValue(1)] int times = 4) => Run($"gm.craftkit {times}");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Crafting"), EnableIf(nameof(IsReady)), LabelText("Fabricator Here"), PropertyOrder(545.5f)]
        private void OpenFabricator() => Run("gm.craft");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Crafting"), EnableIf(nameof(IsReady)), LabelText("Give The 3 Dossiers"), PropertyOrder(545.51f)]
        private void GiveDossiers() => Run("gm.dossier");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Crafting"), EnableIf(nameof(IsReady)), LabelText("Give Zenith Dossiers"), PropertyOrder(545.515f)]
        private void GiveZenithDossiers() => Run("gm.zenith");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Crafting"), EnableIf(nameof(IsReady)), LabelText("Give Skill Chips"), PropertyOrder(545.52f)]
        private void GiveSkillChips() => Run("gm.chips");

        [TabGroup(Tabs, ItemsTab), Title("XP Boosters (Nova Store stock)"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(545.6f)]
        [InfoBox("That many of each: Field (character XP x1.5), Companion (pet XP x2), Tandem (both x1.5), an hour each. They add up, and using one again adds its hour. Use them from the Pack.")]
        private void GiveXpBoosters([MinValue(1)] int ofEach = 1) => Run($"gm.boosters {ofEach}");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Boosters"), EnableIf(nameof(IsReady)), LabelText("What Is Running"), PropertyOrder(545.61f)]
        private void ShowXpBoosts() => Run("gm.boost");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Boosters"), EnableIf(nameof(IsReady)), LabelText("End Every Boost"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(545.62f)]
        private void ClearXpBoosts() => Run("gm.boost.clear");

        [ButtonGroup(Tabs + "/" + ItemsTab + "/Crafting"), EnableIf(nameof(IsReady)), LabelText("Fabricator Needed Again"), GUIColor(1f, 0.75f, 0.6f), PropertyOrder(545.52f)]
        private void CraftingNeedsNpc() => Run("gm.craft off");

        [TabGroup(Tabs, ItemsTab), Title("Catalog"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(546)]
        [InfoBox("Every item id. A slot narrows it: Weapon, Armor, Gloves, Boots, Ring, Amulet, Cape, Pet. Empty lists all.")]
        private void ListItems(string slot = "") => Run(string.IsNullOrWhiteSpace(slot) ? "item.list" : $"item.list {slot.Trim()}");

        [TabGroup(Tabs, EconomyTab), Title("Danger"), Button("Wipe Every Save..."), GUIColor(1f, 0.5f, 0.5f), EnableIf(nameof(IsReady)), PropertyOrder(632)]
        [InfoBox("Deletes all accounts and characters, Crews, Kiosks and Contacts. Asks first.", InfoMessageType.Warning)]
        private void WipeAllSaves()
        {
            if (EditorUtility.DisplayDialog("Wipe Every Save",
                    "This deletes EVERY save: all accounts and characters, Crews, Kiosks and Contacts. It cannot be undone.", "Delete Everything", "Cancel"))
                Run("gm.save.wipe confirm");
        }

        [TabGroup(Tabs, SocialTab), Title("GM Announcement"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(711)]
        [InfoBox("Every player of every faction reads it, wherever they are.")]
        private void Announce(string text = "")
        {
            if (string.IsNullOrWhiteSpace(text)) { Log("Type the announcement first."); return; }
            Run($"gm.say {Quote(text)}");
        }

        [TabGroup(Tabs, SocialTab), Title("Server"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(712)]
        [InfoBox("Sends a player away (a client id or character name; gm.players lists them). They can join again.")]
        private void KickPlayer(string who = "", string reason = "")
        {
            if (string.IsNullOrWhiteSpace(who)) { Log("Type who first."); return; }
            Run($"gm.kick {Quote(who.Trim())} {Quote(reason)}");
        }

        [TabGroup(Tabs, SocialTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(713)]
        [InfoBox("A dedicated server only: counts down with the players told, saves everyone, backs up, then stops.")]
        private void ShutdownServer([MinValue(0)] int seconds = 60, string reason = "") => Run($"gm.shutdown {seconds} {Quote(reason)}");

        [TabGroup(Tabs, SocialTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(713.5f)]
        [InfoBox("A dedicated server only: like Shutdown, then it starts again (through Start Server.bat).")]
        private void RestartServer([MinValue(0)] int seconds = 60, string reason = "") => Run($"gm.restart {seconds} {Quote(reason)}");

        [ButtonGroup(Tabs + "/" + SocialTab + "/Server"), EnableIf(nameof(IsReady)), LabelText("Cancel Shutdown"), PropertyOrder(714)]
        private void CancelShutdown() => Run("gm.shutdown cancel");

        [ButtonGroup(Tabs + "/" + SocialTab + "/Server"), EnableIf(nameof(IsReady)), LabelText("Back Up Saves Now"), PropertyOrder(715)]
        private void BackupNow() => Run("gm.backup");

        private enum PetMoment { Died, Drop, Level, Bruiser, Crit, LowHp, Idle }

        [TabGroup(Tabs, PetsTab), Title("XP and Chatter"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(833)]
        [InfoBox("100,000 fills the growth meter; a Hostile kill is worth 0.04.")]
        private void GivePetXp([MinValue(0)] float amount = 1000f) => Run($"gm.pet.xp {F(amount)}");

        [TabGroup(Tabs, PetsTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(834)]
        [InfoBox("Makes your own pet react as if that moment had just happened. It still waits out its quiet time and stays silent with Pet chatter off.")]
        private void PetSay(PetMoment moment = PetMoment.Crit) => Run($"gm.pet.say {moment.ToString().ToLowerInvariant()}");

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
                bool remote = GmRemoteLines.ForwardsToServer;
                if (remote) ListenForServerAnswers();
                object result = QuantumConsoleProcessor.InvokeCommand(commandString);
                if (log) Log(remote ? $"> {commandString}\n(sent to the server)" : $"> {commandString}\n{result}");
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

        // ================================================================ HOSTILES

        [TabGroup(Tabs, HostilesTab), Title("Species"), ShowInInspector, ValueDropdown(nameof(HostileChoices)), EnableIf(nameof(IsReady)), LabelText("Species"), PropertyOrder(1000)]
        private string hostileSpecies = string.Empty;

        private IEnumerable<ValueDropdownItem<string>> HostileChoices()
        {
            if (!Application.isPlaying) yield break;
            foreach (KryticalNova.Hostiles.HostileNet species in KryticalNova.Hostiles.HostileSpawner.Species())
                yield return new ValueDropdownItem<string>($"{species.Definition.displayName}  (Lv {species.Definition.level}, {species.Definition.tier})", species.Definition.id.ToString());
        }

        private string ChosenHostile => string.IsNullOrEmpty(hostileSpecies) ? "0" : hostileSpecies;

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1001)]
        private void SpawnHostiles([MinValue(1)] int count = 1, [MinValue(0), LabelText("Level (0 = its own)")] int level = 0,
            [MinValue(0), LabelText("Health (0 = its own)")] float health = 0f, [LabelText("Cannot die")] bool cannotDie = false) =>
            Run($"gm.hostile.spawn {ChosenHostile} {count} {level}{(cannotDie ? " inf" : health >= 1f ? " " + F(health) : string.Empty)}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Species"), EnableIf(nameof(IsReady)), LabelText("List"), PropertyOrder(1002)]
        private void ListHostiles() => Run("gm.hostile.list");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Species"), EnableIf(nameof(IsReady)), LabelText("Hits To Kill"), PropertyOrder(1003)]
        private void HostileTimeToKill() => Run($"gm.hostile.ttk {ChosenHostile}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Species"), EnableIf(nameof(IsReady)), LabelText("Simulate 1000 Kills"), PropertyOrder(1004)]
        private void HostileLoot() => Run($"gm.hostile.loot {ChosenHostile} 1000");

        [TabGroup(Tabs, HostilesTab), Title("The target (under the ring, or the nearest)"), Button("Info"), EnableIf(nameof(IsReady)), PropertyOrder(1010)]
        private void HostileInfo() => Run("gm.hostile.info");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Target"), EnableIf(nameof(IsReady)), LabelText("Kill Target"), PropertyOrder(1011)]
        private void KillHostile() => Run("gm.hostile.kill");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Target"), EnableIf(nameof(IsReady)), LabelText("HP 25%"), PropertyOrder(1012)]
        private void HostileQuarterHealth() => Run("gm.hostile.hp 25%");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Target"), EnableIf(nameof(IsReady)), LabelText("HP 100%"), PropertyOrder(1013)]
        private void HostileFullHealth() => Run("gm.hostile.hp 100%");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1014)]
        private void KillHostilesWithin([MinValue(1)] float metres = 15f) => Run($"gm.hostile.kill {metres}");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1015)]
        private void ClearGmSpawned([MinValue(0), LabelText("Metres (0 = everywhere)")] float metres = 0f) => Run($"gm.hostile.clear {metres}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/All"), EnableIf(nameof(IsReady)), LabelText("Freeze All"), PropertyOrder(1020)]
        private void FreezeHostiles() => Run("gm.hostile.freeze on");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/All"), EnableIf(nameof(IsReady)), LabelText("Release All"), PropertyOrder(1021)]
        private void ReleaseHostiles() => Run("gm.hostile.freeze off");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/All"), EnableIf(nameof(IsReady)), LabelText("Ignore Me"), PropertyOrder(1022)]
        private void HostilesIgnore() => Run("gm.hostile.ignore on");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/All"), EnableIf(nameof(IsReady)), LabelText("Notice Me"), PropertyOrder(1023)]
        private void HostilesNotice() => Run("gm.hostile.ignore off");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/All"), EnableIf(nameof(IsReady)), LabelText("Counts"), PropertyOrder(1024)]
        private void HostileStats() => Run("gm.hostile.stats");

        [TabGroup(Tabs, HostilesTab), Title("Hostile Zones"), ShowInInspector, ValueDropdown(nameof(ZoneChoices)), EnableIf(nameof(IsReady)), LabelText("Zone"), PropertyOrder(1030)]
        private string hostileZone = string.Empty;

        private IEnumerable<ValueDropdownItem<string>> ZoneChoices()
        {
            if (!Application.isPlaying) yield break;
            foreach (KryticalNova.Hostiles.HostileZone zone in KryticalNova.Hostiles.HostileZone.All)
                if (zone.Definition != null) yield return new ValueDropdownItem<string>(zone.Definition.displayName, zone.Definition.id);
        }

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Zone"), EnableIf(nameof(IsReady)), LabelText("List Zones"), PropertyOrder(1031)]
        private void ListZones() => Run("gm.zone.list");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Zone"), EnableIf(nameof(IsReady)), LabelText("Go There"), PropertyOrder(1032)]
        private void GoToZone() => Run($"gm.tp {hostileZone}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Zone"), EnableIf(nameof(IsReady)), LabelText("Fill"), PropertyOrder(1033)]
        private void FillZone() => Run($"gm.zone.fill {hostileZone}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Zone"), EnableIf(nameof(IsReady)), LabelText("Empty"), PropertyOrder(1034)]
        private void EmptyZone() => Run($"gm.zone.clear {hostileZone}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Zone"), EnableIf(nameof(IsReady)), LabelText("Bruiser Now"), PropertyOrder(1035)]
        private void ZoneBruiser() => Run($"gm.zone.bruiser {hostileZone}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Sleep"), EnableIf(nameof(IsReady)), LabelText("Hold Asleep"), PropertyOrder(1036)]
        private void ZoneAsleep() => Run($"gm.zone.sleep {hostileZone} on");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Sleep"), EnableIf(nameof(IsReady)), LabelText("Hold Awake"), PropertyOrder(1037)]
        private void ZoneAwake() => Run($"gm.zone.sleep {hostileZone} off");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Sleep"), EnableIf(nameof(IsReady)), LabelText("By Itself"), PropertyOrder(1038)]
        private void ZoneAuto() => Run($"gm.zone.sleep {hostileZone} auto");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1039)]
        private void SetRespawnSeconds([MinValue(0)] float seconds = 2f) => Run($"gm.zone.respawn {seconds}");

        [TabGroup(Tabs, HostilesTab), Button("Respawn Times Back To Normal"), EnableIf(nameof(IsReady)), PropertyOrder(1040)]
        private void DefaultRespawn() => Run("gm.zone.respawn default");

        [TabGroup(Tabs, HostilesTab), Title("Level gap and rates"), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1050)]
        private void GapAgainstLevel([MinValue(1)] int hostileLevel = 10) => Run($"gm.gap {hostileLevel}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Gap"), EnableIf(nameof(IsReady)), LabelText("Gap Table"), PropertyOrder(1051)]
        private void GapTable() => Run("gm.gap.table");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Gap"), EnableIf(nameof(IsReady)), LabelText("Death Penalty On"), PropertyOrder(1052)]
        private void DeathPenaltyOn() => Run("gm.deathpenalty on");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Gap"), EnableIf(nameof(IsReady)), LabelText("Death Penalty Off"), PropertyOrder(1053)]
        private void DeathPenaltyOff() => Run("gm.deathpenalty off");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1054)]
        private void SetXpRate([MinValue(0)] float multiplier = 1f) => Run($"gm.xprate {multiplier}");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1055)]
        private void SetDropRate([MinValue(0)] float multiplier = 1f) => Run($"gm.droprate {multiplier}");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Autopilot"), Title("Autopilot and Magnet"), EnableIf(nameof(IsReady)), LabelText("State"), PropertyOrder(1060)]
        private void AutopilotState() => Run("gm.autopilot");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Autopilot"), EnableIf(nameof(IsReady)), LabelText("On (free)"), PropertyOrder(1061)]
        private void AutopilotOn() => Run("gm.autopilot on");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Autopilot"), EnableIf(nameof(IsReady)), LabelText("Off"), PropertyOrder(1062)]
        private void AutopilotOff() => Run("gm.autopilot off");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Autopilot"), EnableIf(nameof(IsReady)), LabelText("Log Turns"), PropertyOrder(1063)]
        private void AutopilotLog() => Run("gm.autopilot.log");

        [ButtonGroup(Tabs + "/" + HostilesTab + "/Autopilot"), EnableIf(nameof(IsReady)), LabelText("Magnet Test"), PropertyOrder(1064)]
        private void MagnetTest() => Run("gm.magnet.test");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1065)]
        private void SetAutopilotMinutes([MinValue(0)] float minutes = 300f) => Run($"gm.autopilot.time {minutes}");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1066)]
        private void GiveAutopilotChips([MinValue(1)] int count = 1) => Run($"item.give \"Autopilot Chip\" {count}");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1067)]
        private void SetAutobuffDays([MinValue(0)] int days = 3) => Run($"gm.autobuff.days {days}");

        [TabGroup(Tabs, HostilesTab), Button(ButtonStyle.CompactBox, Expanded = true), EnableIf(nameof(IsReady)), PropertyOrder(1068)]
        private void GiveAutobuffPasses([MinValue(1)] int count = 1) => Run($"item.give \"Autobuff Pass (3 Days)\" {count}");

        // ================================================================ CADENCE (design 17): the fight, its music, the beat

        private static KryticalNova.Combat.PlayerCombatState FightState => LocalPlayer != null ? LocalPlayer.GetComponent<KryticalNova.Combat.PlayerCombatState>() : null;
        private static KryticalNova.Combat.PlayerCadence Cadence => LocalPlayer != null ? LocalPlayer.GetComponent<KryticalNova.Combat.PlayerCadence>() : null;
        private static KryticalNova.Audio.MusicConductor Conductor => Application.isPlaying ? KryticalNova.Audio.MusicConductor.Instance : null;
        private static bool HasMusic => IsReady && Conductor != null && Conductor.Music != null;

        [TabGroup(Tabs, CadenceTab), Title("In A Fight Or Not"), ShowInInspector, DisplayAsString, LabelText("Now"), PropertyOrder(1100)]
        private string FightNow => FightState == null ? "-" : FightState.InCombat ? $"In a fight: {FightState.Reasons}" : "Not in a fight";

        [TabGroup(Tabs, CadenceTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Hold in a fight (as a war or The Scar will)"), PropertyOrder(1101)]
        private bool HeldInFight
        {
            get => FightState != null && (FightState.Reasons & KryticalNova.Combat.CombatReasons.Held) != 0;
            set { if (value != HeldInFight) Run($"gm.combat hold {(value ? "on" : "off")}"); }
        }

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Fight"), EnableIf(nameof(IsReady)), LabelText("Print"), PropertyOrder(1102)]
        private void PrintFight() => Run("gm.combat");

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Fight"), EnableIf(nameof(IsReady)), LabelText("Log Starts And Ends"), PropertyOrder(1103)]
        private void WatchFight() => Run("gm.combat watch on");

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Fight"), EnableIf(nameof(IsReady)), LabelText("Stop Logging"), PropertyOrder(1104)]
        private void UnwatchFight() => Run("gm.combat watch off");

        [TabGroup(Tabs, CadenceTab), Title("Fight Music (heard only in a fight)"), ShowInInspector, DisplayAsString(false), LabelText("This Machine"), PropertyOrder(1110)]
        private string MusicNow => !Application.isPlaying ? "-" : Conductor == null ? "This scene has no fight music." : KryticalNova.Audio.CombatMusicPlayer.Report;

        [TabGroup(Tabs, CadenceTab), ShowInInspector, ValueDropdown(nameof(TrackChoices)), EnableIf(nameof(HasMusic)), LabelText("Track"), PropertyOrder(1111)]
        private int musicTrack = 1;

        private IEnumerable<ValueDropdownItem<int>> TrackChoices()
        {
            if (!HasMusic) yield break;
            KryticalNova.Audio.MusicTrack[] tracks = Conductor.Music.combat;
            for (int i = 0; i < tracks.Length; i++)
                if (tracks[i] != null) yield return new ValueDropdownItem<int>($"{i + 1}  {tracks[i].Title}  ({tracks[i].beatsPerMinute:0.##} a minute)", i + 1);
        }

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Music"), EnableIf(nameof(HasMusic)), LabelText("Play This Track Over And Over"), PropertyOrder(1112)]
        private void PlayTrack() => Run($"gm.music.track {musicTrack}");

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Music"), EnableIf(nameof(HasMusic)), LabelText("Tracks In Turn"), PropertyOrder(1113)]
        private void PlayRound() => Run("gm.music.track round");

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Music"), EnableIf(nameof(HasMusic)), LabelText("Print"), PropertyOrder(1114)]
        private void PrintMusic() => Run("gm.music");

        [TabGroup(Tabs, CadenceTab), ShowInInspector, ToggleLeft, EnableIf(nameof(HasMusic)), LabelText("Click on every beat (this machine only)"), PropertyOrder(1115)]
        private bool BeatClick
        {
            get => Application.isPlaying && KryticalNova.Audio.CombatMusicPlayer.Click;
            set { if (value != BeatClick) Run($"gm.music.click {(value ? "on" : "off")}"); }
        }

        [TabGroup(Tabs, CadenceTab), ShowInInspector, PropertyRange(-200f, 200f), EnableIf(nameof(HasMusic)), LabelText("Nudge Every Beat (ms)"), PropertyOrder(1116)]
        [InfoBox("For finding a track's right value by ear with the click on. It is not kept: tell Claude the value and it is baked into the track.", InfoMessageType.None)]
        private float BeatNudge
        {
            get => Conductor != null ? Mathf.Round(Conductor.State.Nudge * 1000f) : 0f;
            set { if (Conductor != null && !Mathf.Approximately(Mathf.Round(value), BeatNudge)) Run($"gm.music.nudge {F(Mathf.Round(value))}", log: false); }
        }

        [TabGroup(Tabs, CadenceTab), Title("Cadence (a skill key pressed on the beat)"), ShowInInspector, DisplayAsString(false), LabelText("Last Key"), PropertyOrder(1120)]
        private string CadenceLast => Cadence != null ? Cadence.LastPress : "-";

        [TabGroup(Tabs, CadenceTab), ShowInInspector, DisplayAsString, LabelText("Counts"), PropertyOrder(1121)]
        private string CadenceCounts
        {
            get
            {
                KryticalNova.Combat.PlayerCadence cadence = Cadence;
                if (cadence == null) return "-";
                int judged = cadence.PressesOn + cadence.PressesOff;
                string timing = judged > 0 ? $" · average {Mathf.Abs(cadence.AverageErrorMs):0} ms {(cadence.AverageErrorMs < 0f ? "early" : "late")}, spread {cadence.SpreadMs:0} ms" : string.Empty;
                return $"on beat {cadence.PressesOn} · off beat {cadence.PressesOff} · casts boosted {cadence.CastsOnCadence}{timing}";
            }
        }

        [TabGroup(Tabs, CadenceTab), ShowInInspector, DisplayAsString, LabelText("Rules"), PropertyOrder(1122)]
        private string CadenceRulesNow
        {
            get
            {
                KryticalNova.Combat.CadenceSettings settings = Cadence != null ? Cadence.Settings : null;
                return settings == null ? "-" : $"{(settings.enabled ? "on" : "OFF")} · within {settings.windowMs:0} ms of a beat · +{settings.bonusPercent:0.#} % damage · kept {settings.keepSeconds:0.#} s";
            }
        }

        [TabGroup(Tabs, CadenceTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Readout on the game screen (beats coming, hits on and off the beat)"), PropertyOrder(1123)]
        private bool CadenceReadout
        {
            get => KryticalNova.Combat.PlayerCadence.ReadoutShown;
            set { if (value != CadenceReadout) Run($"gm.cadence window {(value ? "on" : "off")}"); }
        }

        [TabGroup(Tabs, CadenceTab), ShowInInspector, ToggleLeft, EnableIf(nameof(IsReady)), LabelText("Print every press of every player to the log"), PropertyOrder(1124)]
        private bool CadenceLog
        {
            get => Application.isPlaying && KryticalNova.Combat.PlayerCadence.Logging;
            set { if (value != CadenceLog) Run($"gm.cadence log {(value ? "on" : "off")}"); }
        }

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Cadence"), EnableIf(nameof(IsReady)), LabelText("Start Counts Again"), PropertyOrder(1125)]
        private void ResetCadence() => Run("gm.cadence reset");

        [ButtonGroup(Tabs + "/" + CadenceTab + "/Cadence"), EnableIf(nameof(IsReady)), LabelText("Print"), PropertyOrder(1126)]
        private void PrintCadence() => Run("gm.cadence");

        // Live readouts (HP, clock, stats) refresh ~10×/s while playing.
        private void OnInspectorUpdate()
        {
            if (Application.isPlaying) Repaint();
        }
    }
}
