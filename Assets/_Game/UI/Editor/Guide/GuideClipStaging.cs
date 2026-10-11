#if KN_RECORDER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using KryticalNova.Combat;
using KryticalNova.Economy;
using KryticalNova.Hostiles;
using KryticalNova.Items;
using KryticalNova.NPC;
using KryticalNova.Player;
using KryticalNova.Progression;
using KryticalNova.Social;
using KryticalNova.UI;
using KryticalNova.UI.Guide;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// Stages and records the Guide's clips (menu Krytical Nova ▸ Guide ▸ Record Staged Clips, in Play as the host;
    /// <see cref="Record"/> from a script): for each topic the GM set-up, what is in the picture, what happens while
    /// it is recorded, and how long. One command re-records any clip, so a clip can follow its system when that
    /// changes. Every clip gets the same treatment: late morning and a clear sky, GM commands on this player, every
    /// window of the clip before closed, and nothing of the HUD in the picture but what the topic is about.
    ///
    /// Three kinds of clip. The world: the Game view with the HUD hidden (plates and namecards stay where they are the
    /// point). A window: recorded large with the interface at its smallest so a tall window is whole, then cut to
    /// the window. A part of the HUD: the same, cut to that part. Topics about two players need a second client in
    /// the world, running <see cref="Answer"/> so it accepts what the host asks.
    /// The log of the last run is Temp/GuideClips/staging.log.
    /// </summary>
    public static class GuideClipStaging
    {
        private const int WorldWidth = 1280, WorldHeight = 720, LargeWidth = 2560, LargeHeight = 1440;
        private const float WindowScale = 0.8f;
        // How solid a window and the panels in it are drawn for a clip. In the game a window lets the world through
        // (the player's choice, 65 % see-through as shipped); a help clip has to be read, so it is drawn nearly solid.
        private const int ClipWindowPercent = 95, ClipCardPercent = 95;
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string LogFile = GuideClipRecorder.MasterFolder + "/staging.log";
        private static readonly string[] HudParts =
        {
            "summary", "menu", "active-buffs-bar", "hotbar-dock", "network-panel", "chat-window", "minimap", "party-frames", "controls-hint", "feedback", "boost-list"
        };

        /// <summary>Every topic with a staged clip, in the order they are recorded. Those marked need a second player.</summary>
        public static readonly string[] Solo =
        {
            "moving-and-camera", "fighting", "hotbar", "hud", "chat", "town-and-map",
            "stats", "levels", "weapon-classes", "skills", "serums",
            "rarities", "pack-and-equipment", "tempering", "fusion", "tuning-and-reforge", "ascension", "nova-standings",
            "hostiles", "stims", "autopilot", "pets", "xp-boosters",
            "contacts", "kiosks", "vaults", "crews", "creds", "npc-shops", "fabricator-and-dossiers", "holoweave-and-dye",
            "day-and-weather", "settings-and-keys"
        };
        public static readonly string[] WithSecondPlayer = { "namecards-and-emotes", "squad", "trade", "callouts", "factions", "sponsor-and-recruit" };

        public static bool Running { get; private set; }
        /// <summary>The topic being recorded, then "done".</summary>
        public static string Progress { get; private set; } = string.Empty;

        // ---- the stage: what a scenario works with
        private static PlayerInventory local;
        private static PointAndClickPlayer player;
        private static HudSettingsController hud;
        private static CinemachineOrbitalFollow orbit;
        private static GuideLibrary library;
        private static string topic;
        private static readonly List<VisualElement> hidden = new List<VisualElement>();
        private static readonly List<Canvas> canvases = new List<Canvas>();
        private static readonly List<Action> closers = new List<Action>();
        private static readonly Dictionary<PanelSettings, Vector2Int> references = new Dictionary<PanelSettings, Vector2Int>();
        private static readonly List<Placed> placed = new List<Placed>();
        private static readonly List<Behaviour> readouts = new List<Behaviour>();
        private static readonly List<VisualElement> solid = new List<VisualElement>();
        private static float radiusBefore;

        private struct Placed
        {
            public VisualElement Element;
            public StyleLength Left, Top, Right, Bottom;
            public StyleTranslate Translate;
        }

        [MenuItem("Krytical Nova/Guide/Record Staged Clips (One Player)")]
        private static void RecordSolo() => Debug.Log("[GuideClip] " + Record(Solo));

        [MenuItem("Krytical Nova/Guide/Record Staged Clips (Two Players)")]
        private static void RecordPairs() => Debug.Log("[GuideClip] " + Record(WithSecondPlayer));

        /// <summary>Records these topics one after another. Returns at once; <see cref="Running"/> and the log say how it goes.</summary>
        public static string Record(params string[] topics)
        {
            if (!Application.isPlaying) return "Enter Play mode as the host first.";
            if (Running) return "Already recording " + Progress + ".";
            local = PlayerInventory.Local;
            hud = UnityEngine.Object.FindAnyObjectByType<HudSettingsController>();
            orbit = UnityEngine.Object.FindAnyObjectByType<CinemachineOrbitalFollow>();
            library = AssetDatabase.LoadAssetAtPath<GuideLibrary>(HudSetup.GuideLibraryPath);
            if (local == null || hud == null || orbit == null || library == null) return "No local player, HUD, camera or Guide library.";
            player = local.GetComponent<PointAndClickPlayer>();
            Directory.CreateDirectory(GuideClipRecorder.MasterFolder);
            File.WriteAllText(LogFile, "begin " + string.Join(",", topics) + "\n");
            Running = true;
            hud.StartCoroutine(RunAll(topics));
            return "Recording " + topics.Length + " clip(s).";
        }

        /// <summary>
        /// The second player's side, run on that client for as long as the host records: it accepts every Trade,
        /// Squad and Callout request, and plays a Trade out (an offer, lock when the host has, confirm when the host has).
        /// </summary>
        public static string Answer(float seconds = 600f)
        {
            if (!Application.isPlaying) return "Enter Play mode first.";
            PlayerTrade trade = PlayerTrade.Local;
            if (trade == null) return "No local player.";
            trade.StartCoroutine(Answering(trade, seconds));
            return "Answering for " + seconds + " s.";
        }

        private static IEnumerator Answering(PlayerTrade trade, float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            bool offered = false;
            while (Time.realtimeSinceStartup < end && trade != null)
            {
                PlayerParty party = PlayerParty.Local;
                PlayerDuel duel = PlayerDuel.Local;
                if (!trade.IsTrading)
                {
                    offered = false;
                    trade.Respond(true);
                    if (party != null && !party.InParty) party.Respond(true);
                    if (duel != null) duel.Respond(true);
                }
                else
                {
                    if (!offered) { yield return new WaitForSecondsRealtime(2f); trade.Offer(0); offered = true; }
                    if (trade.View.theirLocked && !trade.View.myLocked) { yield return new WaitForSecondsRealtime(0.9f); trade.SetLocked(true); }
                    if (trade.View.theirConfirmed && !trade.View.myConfirmed) { yield return new WaitForSecondsRealtime(0.8f); trade.Confirm(); }
                }
                yield return new WaitForSecondsRealtime(0.4f);
            }
        }

        // ------------------------------------------------------------------ the run

        private static IEnumerator RunAll(string[] topics)
        {
            foreach (string each in topics)
            {
                topic = each;
                Progress = each;
                if (library.Find(each) == null) { Log("no topic " + each); continue; }
                Log("==== " + each);
                IEnumerator scene = null;
                try { scene = Scene(each); }
                catch (Exception e) { Log("could not stage: " + e.Message); }
                if (scene != null) yield return Guarded(scene);
                float give = Time.realtimeSinceStartup + 40f;
                while (GuideClipRecorder.IsRecording && Time.realtimeSinceStartup < give) yield return null;
                yield return Wait(0.6f);
                Restore();
                Log("clip: " + GuideClipRecorder.LastResult);
                yield return Wait(0.8f);
            }
            Progress = "done";
            Running = false;
            Log("END");
        }

        // A scenario that throws must not leave the HUD hidden or the run hanging.
        private static IEnumerator Guarded(IEnumerator scene)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(scene);
            while (stack.Count > 0)
            {
                IEnumerator top = stack.Peek();
                bool more;
                try { more = top.MoveNext(); }
                catch (Exception e)
                {
                    Log("FAILED: " + (e.InnerException ?? e).Message);
                    yield break;
                }
                if (!more) { stack.Pop(); continue; }
                if (top.Current is IEnumerator inner) stack.Push(inner);
                else yield return top.Current;
            }
        }

        private static void Restore()
        {
            if (Shown("amount-dialog")) Press("amount-cancel");
            foreach (Action close in closers)
            {
                try { close(); }
                catch (Exception e) { Log("close failed: " + e.Message); }
            }
            closers.Clear();
            foreach (VisualElement element in hidden) element.style.display = StyleKeyword.Null;
            hidden.Clear();
            foreach (Canvas canvas in canvases) if (canvas != null) canvas.enabled = true;
            canvases.Clear();
            foreach (KeyValuePair<PanelSettings, Vector2Int> each in references) if (each.Key != null) each.Key.referenceResolution = each.Value;
            references.Clear();
            foreach (Placed each in placed)
            {
                each.Element.style.left = each.Left; each.Element.style.top = each.Top;
                each.Element.style.right = each.Right; each.Element.style.bottom = each.Bottom;
                each.Element.style.translate = each.Translate;
            }
            placed.Clear();
            foreach (VisualElement top in solid)
            {
                top.RemoveFromClassList(PanelOpacity.WindowClass(ClipWindowPercent));
                top.RemoveFromClassList(PanelOpacity.CardClass(ClipCardPercent));
            }
            if (solid.Count > 0) PanelOpacity.Apply();
            solid.Clear();
            foreach (Behaviour readout in readouts) if (readout != null) readout.enabled = true;
            readouts.Clear();
            if (orbit != null && radiusBefore > 0f) orbit.Radius = radiusBefore;
            radiusBefore = 0f;
        }

        /// <summary>Every clip starts from the same place.</summary>
        private static IEnumerator Begin()
        {
            Cmd("gm.target self");   // GM commands must act on this player, whoever was picked before
            Cmd("time.resume"); Cmd("time.set 11"); Cmd("time.pause"); Cmd("weather.clear");
            Cmd("gm.god on"); Cmd("gm.heal");
            GearUIController gear = GearUIController.Instance;
            if (gear != null) gear.SetInventoryOpen(false);
            Win("character-window", "open-character", false);
            Win("skills-window", "open-skills", false);
            Win("equipment-window", "open-equipment", false);
            if (Shown("amount-dialog")) Press("amount-cancel");
            Ctl<HeraldWindowController>()?.Close();
            Ctl<ContactsWindowController>()?.SetOpen(false);
            Ctl<CrewWindowController>()?.SetOpen(false);
            Ctl<AutopilotWindowController>()?.SetOpen(false);
            Ctl<SponsorWindowController>()?.SetOpen(false);
            Ctl<CraftWindowController>()?.Close();
            Ctl<WeaveWindowController>()?.Close();
            Ctl<VendorWindowController>()?.Close();
            Ctl<GearWorkWindowController>()?.Close();
            Ctl<StorageWindowController>()?.Close();
            if (Shown("settings-window")) Call(hud, "CloseSettings");
            Call(player, "ClearSelection");   // nobody selected: no target bar in the picture
            radiusBefore = orbit.Radius;
            // The performance readout is drawn over everything: it is not part of the game a player sees.
            foreach (PerformanceReadout readout in UnityEngine.Object.FindObjectsByType<PerformanceReadout>(FindObjectsSortMode.None))
                if (readout.enabled) { readout.enabled = false; readouts.Add(readout); }
            yield return Wait(0.6f);
        }

        // ------------------------------------------------------------------ the scenarios

        private static IEnumerator Scene(string id)
        {
            yield return Begin();
            switch (id)
            {
                // ---------------------------------------------------- the world
                case "moving-and-camera":
                {
                    Clean(true);
                    Cmd("gm.tpxyz 0 1 0");
                    yield return Wait(1.5f);
                    Vector3 start = player.transform.position;
                    float yaw = orbit.HorizontalAxis.Value;
                    Vector3 ahead = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, side = Quaternion.Euler(0f, yaw + 90f, 0f) * Vector3.forward;
                    Rec(10f, false);
                    yield return Wait(0.8f);
                    player.ServerMoveTo(start + ahead * 8f);
                    yield return Wait(2.4f);
                    yield return Turn(110f, 1.8f);
                    player.ServerMoveTo(start + ahead * 8f + side * 8f);
                    yield return Wait(2.2f);
                    player.ServerMoveTo(start);
                    yield return Wait(3f);
                    orbit.HorizontalAxis.Value = yaw;
                    break;
                }
                case "fighting":
                {
                    Clean(false);
                    Cmd("gm.skills.max");
                    yield return ToHostiles();
                    Rec(11f, false);
                    yield return Wait(0.8f);
                    for (int i = 0; i < 4; i++)
                    {
                        HostileNet target = Nearest();
                        if (target == null) { Log("no hostile near"); break; }
                        Log("attack " + target.name + " = " + player.CommandAttack(target.GetComponent<CombatEntity>()));
                        yield return Wait(2.5f);
                    }
                    break;
                }
                case "hostiles":
                {
                    Clean(false);
                    yield return ToHostiles();
                    Rec(9f, false);
                    yield return Turn(140f, 8f);
                    break;
                }
                case "levels":
                {
                    Clean(false, "summary");
                    Cmd("gm.tpxyz 0 1 0"); Cmd("gm.level 40");
                    yield return Wait(2f);
                    Rec(9f, false);
                    yield return Wait(1.5f);
                    Cmd("gm.xp 400000");
                    yield return Wait(3f);
                    Cmd("gm.xp 900000");
                    yield return Wait(4.5f);
                    Cmd("gm.level 112"); Cmd("gm.skills.max");
                    break;
                }
                case "pets":
                {
                    // The Tidecaller from its egg through every evolution, with the camera close.
                    Clean(true);
                    Cmd("gm.tpxyz 0 1 0"); Cmd("gm.kit pets");
                    yield return Wait(1.5f);
                    int cell = FindCell((def, stack) => def.equipSlot == EquipSlot.Pet && def.displayName.Contains("Tidecaller"));
                    Log("pet cell " + cell);
                    if (cell >= 0) local.RequestQuickEquip(cell);
                    yield return Wait(1.5f);
                    Cmd("gm.pet.hunger 100"); Cmd("gm.pet.growth 0");
                    orbit.Radius = Mathf.Max(3.2f, radiusBefore * 0.4f);
                    yield return Wait(2.5f);
                    Rec(13f, false);
                    yield return Wait(1.6f);
                    foreach (int growth in new[] { 25, 50, 75, 100 })
                    {
                        Cmd("gm.pet.growth " + growth);
                        yield return Turn(35f, 2.7f);
                    }
                    break;
                }
                case "day-and-weather":
                {
                    Clean(true);
                    Cmd("gm.tpxyz 0 1 0"); Cmd("time.set 15");
                    yield return Wait(1.5f);
                    Rec(10f, false);
                    Cmd("time.resume"); Cmd("time.scale 1500");
                    yield return Turn(60f, 9f);
                    Cmd("time.scale 1"); Cmd("time.set 11"); Cmd("time.pause");
                    break;
                }

                // ---------------------------------------------------- two players
                case "namecards-and-emotes":
                {
                    Clean(false);
                    PlayerTrade other = Other();
                    if (other == null) { Log("no second player"); break; }
                    StandBy(other.transform.position, 2.5f);
                    yield return Wait(2f);
                    Rec(10f, false);
                    yield return Wait(1f);
                    PlayerEmotes emotes = PlayerEmotes.Local;
                    emotes.RequestPlay("E/Greet/Bow");
                    yield return Turn(70f, 4f);
                    emotes.RequestPlay("E/Greet/Wave");
                    yield return Wait(5f);
                    break;
                }
                case "factions":
                {
                    // Two factions on neutral ground: each namecard in its own faction's skin.
                    Clean(false);
                    PlayerTrade other = Other();
                    if (other == null) { Log("no second player"); break; }
                    StandBy(other.transform.position, 1.7f);
                    orbit.Radius = Mathf.Max(5f, radiusBefore * 0.6f);
                    yield return Wait(2.5f);
                    Rec(9f, false);
                    // A slow quarter turn: both stay in the picture.
                    yield return Turn(-45f, 4f);
                    yield return Turn(90f, 4.5f);
                    break;
                }
                case "squad":
                {
                    Clean(false, "party-frames");
                    PlayerTrade other = Other();
                    if (other == null) { Log("no second player"); break; }
                    if (other.GetComponent<PlayerIdentity>().Faction != local.GetComponent<PlayerIdentity>().Faction)
                    {
                        Log("a Squad is of one faction: the second player is of another. Not recorded.");
                        break;
                    }
                    PlayerParty party = PlayerParty.Local;
                    if (party.InParty) party.Leave();
                    StandBy(other.transform.position, 2.5f);
                    yield return Wait(2f);
                    Rec(11f, false);
                    yield return Wait(1f);
                    party.Invite(other.TraderName);
                    yield return Wait(8.5f);
                    Log("in a Squad: " + party.InParty);
                    party.Leave();
                    break;
                }
                case "callouts":
                {
                    Clean(false);
                    PlayerTrade other = Other();
                    if (other == null) { Log("no second player"); break; }
                    Cmd("gm.god off");
                    StandBy(other.transform.position, 3f);
                    yield return Wait(2f);
                    Rec(13f, false);
                    yield return Wait(1f);
                    PlayerDuel.Local.Request(other.GetComponent<PlayerDuel>());
                    yield return Wait(6.5f);   // accepted by the other side, then the count
                    Log("attack = " + player.CommandAttack(other.GetComponent<CombatEntity>()));
                    yield return Wait(5.5f);
                    Cmd("gm.duel.end"); Cmd("gm.heal");
                    break;
                }
                case "trade":
                {
                    Small();
                    Clean(true);
                    PlayerTrade me = PlayerTrade.Local, other = Other();
                    if (other == null) { Log("no second player"); break; }
                    StandBy(other.transform.position, 2.2f);
                    Cmd("gm.creds 750000");
                    if (FindCell((def, stack) => def.IsLadderGear) < 0) Cmd("gm.kit gear 60 Rare");
                    yield return Wait(2.5f);
                    Rec(15f, true);
                    yield return Wait(1f);
                    me.Request(other);
                    float give = Time.realtimeSinceStartup + 6f;
                    while (!me.IsTrading && Time.realtimeSinceStartup < give) yield return null;
                    yield return Wait(0.8f);
                    Beside("trade-window", "inventory-window");
                    yield return Wait(0.4f);
                    Focus(10f, "trade-window", "inventory-window");
                    for (int i = 0; i < 3; i++)
                    {
                        int cell = FindCell((def, stack) => def.IsLadderGear, i);
                        if (cell >= 0) me.Offer(cell);
                        yield return Wait(0.8f);
                    }
                    me.SetCreds(250000);
                    yield return Wait(2.2f);
                    me.SetLocked(true);
                    give = Time.realtimeSinceStartup + 5f;
                    while (!(me.View.myLocked && me.View.theirLocked) && Time.realtimeSinceStartup < give) yield return null;
                    yield return Wait(1.5f);
                    me.Confirm();
                    yield return Wait(3.5f);
                    Log("trading=" + me.IsTrading + " notice=" + me.LastNotice);
                    closers.Add(() => { if (GearUIController.Instance != null) GearUIController.Instance.SetInventoryOpen(false); });
                    break;
                }
                case "sponsor-and-recruit":
                {
                    Small();
                    Clean(true);
                    Cmd("gm.level 112");
                    Log(Cmd("gm.players"));
                    Cmd("gm.sponsor 0 1");
                    yield return Wait(1.5f);
                    Ctl<SponsorWindowController>().SetOpen(true);
                    closers.Add(() => Ctl<SponsorWindowController>().SetOpen(false));
                    yield return Window(9f, null, "sponsor-window");
                    break;
                }

                // ---------------------------------------------------- parts of the HUD
                case "hotbar":
                {
                    Clean(false, "hotbar-dock");
                    Cmd("gm.skills.max");
                    yield return ToHostiles();
                    Rec(10f, true);
                    yield return Wait(1.2f);
                    Focus(90f, "hotbar-dock");
                    var bar = local.GetComponent<PlayerSkillHotbar>();
                    HostileNet target = Nearest();
                    if (target != null) player.CommandAttack(target.GetComponent<CombatEntity>());
                    for (int i = 2; i < 7; i++) { Log("slot " + i + " = " + bar.ActivateSlot(i)); yield return Wait(1.5f); }
                    break;
                }
                case "hud":
                {
                    Cmd("gm.tpxyz 0 1 0");
                    yield return Wait(1.5f);
                    Rec(11f, true);
                    yield return Wait(2f);
                    Call(hud, "BeginLayoutEdit");
                    closers.Add(() => Call(hud, "EndLayoutEdit"));
                    yield return Wait(8f);
                    break;
                }
                case "chat":
                {
                    Clean(true, "chat-window");
                    Rec(10f, true);
                    yield return Wait(1.2f);
                    Focus(16f, "chat-window");
                    Chat("Anyone hunting the Gargoyles tonight?");
                    yield return Wait(2.2f);
                    Chat("/f Selling a Rare Monoblade, level 60. Kiosk in the plaza.");
                    yield return Wait(2.2f);
                    Chat("/s On my way.");
                    yield return Wait(4.5f);
                    break;
                }
                case "xp-boosters":
                {
                    // The boosters are used from the Pack; what is running is the list at the side of the screen.
                    Small();
                    Clean(true, "boost-list");
                    Cmd("gm.boost.clear"); Cmd("gm.boosters 3");
                    Win("inventory-window", "open-inventory", true);
                    closers.Add(() => Win("inventory-window", "open-inventory", false));
                    closers.Add(() => Cmd("gm.boost.clear"));
                    yield return Wait(1.5f);
                    Beside("inventory-window", "boost-list");
                    Rec(11f, true);
                    yield return Wait(1.3f);
                    Focus(14f, "inventory-window");
                    for (int i = 0; i < 3; i++)
                    {
                        yield return Wait(1.6f);
                        // One of each: a stack that is used up moves the next one into its place.
                        int cell = FindCell((def, stack) => def.useEffect == ItemUseEffect.ExperienceBoost, i);
                        if (cell >= 0) local.RequestUse(cell);
                        Log("booster cell " + cell);
                        yield return Wait(0.5f);
                        // The list has no size until something runs: the cut takes it in once it has.
                        Focus(14f, "inventory-window", "boost-list");
                    }
                    yield return Wait(3f);
                    break;
                }

                // ---------------------------------------------------- windows
                case "town-and-map":
                    Small(); Clean(true);
                    Ctl<WorldMapController>().Open();
                    closers.Add(() => Call(Ctl<WorldMapController>(), "SetOpen", false));
                    yield return Window(9f, null, "world-map");
                    break;
                case "stats":
                    Small(); Clean(true);
                    Win("character-window", "open-character", true);
                    closers.Add(() => Win("character-window", "open-character", false));
                    yield return Window(8f, null, "character-window");
                    break;
                case "serums":
                    Small(); Clean(true);
                    Cmd("gm.serums 20");
                    Win("character-window", "open-character", true);
                    closers.Add(() => Win("character-window", "open-character", false));
                    yield return Window(9f, () => ScrollDown("character-window"), "character-window");
                    break;
                case "skills":
                    Small(); Clean(true);
                    Cmd("gm.skillpoints 40");
                    Win("skills-window", "open-skills", true);
                    closers.Add(() => Win("skills-window", "open-skills", false));
                    yield return Window(11f, SkillTree, "skills-window");
                    break;
                case "weapon-classes":
                    Small(); Clean(true);
                    Cmd("gm.kit gear 112 Epic");
                    Win("inventory-window", "open-inventory", true); Win("equipment-window", "open-equipment", true);
                    closers.Add(() => { Win("inventory-window", "open-inventory", false); Win("equipment-window", "open-equipment", false); });
                    yield return Window(10f, SwapWeapons, "inventory-window", "equipment-window");
                    break;
                case "rarities":
                case "stims":
                case "creds":
                    Small(); Clean(true);
                    Cmd("gm.kit gear 20 Common"); Cmd("gm.kit gear 30 Uncommon"); Cmd("gm.kit gear 112 Epic");
                    Win("inventory-window", "open-inventory", true);
                    closers.Add(() => Win("inventory-window", "open-inventory", false));
                    yield return Window(9f, id == "creds" ? Creds : (Func<IEnumerator>)null, "inventory-window");
                    break;
                case "pack-and-equipment":
                    Small(); Clean(true);
                    Win("inventory-window", "open-inventory", true); Win("equipment-window", "open-equipment", true);
                    closers.Add(() => { Win("inventory-window", "open-inventory", false); Win("equipment-window", "open-equipment", false); });
                    yield return Window(9f, null, "inventory-window", "equipment-window");
                    break;
                case "autopilot":
                    Small(); Clean(true);
                    Ctl<AutopilotWindowController>().SetOpen(true);
                    closers.Add(() => Ctl<AutopilotWindowController>().SetOpen(false));
                    yield return Window(9f, () => ScrollDown("autopilot-window"), "autopilot-window");
                    break;
                case "contacts":
                    Small(); Clean(true);
                    Ctl<ContactsWindowController>().SetOpen(true);
                    closers.Add(() => Ctl<ContactsWindowController>().SetOpen(false));
                    yield return Window(8f, null, "contacts-window");
                    break;
                case "crews":
                    Small(); Clean(true);
                    Cmd("gm.guild Nightwatch");
                    yield return Wait(1.5f);
                    Ctl<CrewWindowController>().SetOpen(true);
                    closers.Add(() => Ctl<CrewWindowController>().SetOpen(false));
                    yield return Window(9f, null, "crew-window");
                    break;
                case "kiosks":
                {
                    Small(); Clean(true);
                    Cmd("gm.tpxyz 0 1 0");
                    if (FindCell((def, stack) => def.IsLadderGear) < 0) Cmd("gm.kit gear 60 Rare");
                    var kiosk = Ctl<KioskWindowController>();
                    // A Kiosk that has closed is collected first, so the window shows a shop being set up.
                    Call(kiosk, "SetOwnOpen", true);
                    yield return Wait(1f);
                    Press("kiosk-collect");
                    yield return Wait(2f);
                    Call(kiosk, "SetOwnOpen", true);
                    if (GearUIController.Instance != null) GearUIController.Instance.SetInventoryOpen(true);
                    closers.Add(() => Call(kiosk, "SetOwnOpen", false));
                    closers.Add(() => { if (GearUIController.Instance != null) GearUIController.Instance.SetInventoryOpen(false); });
                    yield return Window(10f, Stock, "kiosk-window", "inventory-window");
                    break;
                }
                case "vaults":
                {
                    Small(); Clean(true);
                    if (FindCell((def, stack) => def.IsLadderGear) < 0) Cmd("gm.kit gear 60 Rare");
                    Cmd("gm.tp NPC_GuildDirector");
                    yield return Wait(2.5f);
                    Ctl<StorageWindowController>().Open(StorageKind.Vault);
                    closers.Add(() => Ctl<StorageWindowController>().Close());
                    closers.Add(() => { if (GearUIController.Instance != null) GearUIController.Instance.SetInventoryOpen(false); });
                    yield return Window(9f, Deposit, "storage-window", "inventory-window");
                    break;
                }
                case "ascension":
                case "nova-standings":
                    Small(); Clean(true);
                    Cmd("gm.renown 200000"); Cmd("gm.creds 50000000"); Cmd("gm.ascension 14"); Cmd("gm.herald");
                    closers.Add(() => Ctl<HeraldWindowController>().Close());
                    yield return Wait(1f);
                    Press(id == "nova-standings" ? "herald-tab-standings" : "herald-tab-ascension");
                    yield return Window(11f, id == "ascension" ? Ascend : (Func<IEnumerator>)null, "herald-window");
                    break;
                case "settings-and-keys":
                    Small(); Clean(true);
                    hud.OpenSettings();
                    ((SettingsPages)typeof(HudSettingsController).GetField("pages", Any).GetValue(hud)).Show("controls");
                    closers.Add(() => Call(hud, "CloseSettings"));
                    yield return Window(8f, null, "settings-window");
                    break;
                case "tempering":
                    Small(); Clean(true);
                    yield return AtForge("temper");
                    yield return Window(12f, Temper, "forge-window", "inventory-window");
                    break;
                case "fusion":
                    Small(); Clean(true);
                    Cmd("item.clear");
                    yield return Wait(1f);
                    yield return AtForge("temper");
                    yield return FusePrepare();
                    yield return Window(13f, Fuse, "forge-window", "inventory-window");
                    break;
                case "tuning-and-reforge":
                    Small(); Clean(true);
                    yield return AtForge("tuning");
                    yield return Window(13f, Tune, "forge-window", "inventory-window");
                    break;
                case "npc-shops":
                {
                    Small(); Clean(true);
                    Cmd("gm.creds 500000");
                    Cmd("gm.tp NPC_HerbMaster");
                    yield return Wait(2.5f);
                    Ctl<VendorWindowController>().Open();
                    closers.Add(() => Ctl<VendorWindowController>().Close());
                    closers.Add(() => { if (GearUIController.Instance != null) GearUIController.Instance.SetInventoryOpen(false); });
                    yield return Window(9f, null, "vendor-window", "inventory-window");
                    break;
                }
                case "fabricator-and-dossiers":
                    Small(); Clean(true);
                    Cmd("gm.craftkit 4"); Cmd("gm.creds 5000000");
                    Cmd("gm.craft");
                    closers.Add(() => Ctl<CraftWindowController>().Close());
                    yield return Window(10f, Craft, "craft-window");
                    break;
                case "holoweave-and-dye":
                    Small(); Clean(true);
                    Cmd("gm.kit gear 112 Epic"); Cmd("gm.creds 5000000");
                    Cmd("gm.weave");
                    closers.Add(() => Ctl<WeaveWindowController>().Close());
                    yield return Window(11f, Weave, "weave-window");
                    break;
                default:
                    Log("no scenario for " + id);
                    break;
            }
        }

        /// <summary>A window clip: recorded large, cut to these windows, with something happening in them meanwhile.</summary>
        private static IEnumerator Window(float seconds, Func<IEnumerator> act, params string[] show)
        {
            yield return Wait(1f);
            if (show.Length > 1) Beside(show);
            yield return Wait(0.5f);
            Rec(seconds, true);
            yield return Wait(1.3f);
            Focus(10f, show);
            float began = Time.realtimeSinceStartup;
            if (act != null) yield return act();
            while (Time.realtimeSinceStartup - began < seconds) yield return null;
        }

        // ------------------------------------------------------------------ what happens in a window

        private static IEnumerator ScrollDown(string window)
        {
            ScrollView scroll = Find(window)?.Q<ScrollView>();
            if (scroll == null) { Log("no scroll view in " + window); yield break; }
            yield return Wait(1.5f);
            float end = Mathf.Max(0f, scroll.contentContainer.layout.height - scroll.contentViewport.layout.height);
            for (float t = 0f; t < 2.5f; t += Time.unscaledDeltaTime)
            {
                scroll.scrollOffset = new Vector2(0f, end * Mathf.SmoothStep(0f, 1f, t / 2.5f));
                yield return null;
            }
            scroll.scrollOffset = new Vector2(0f, end);
        }

        // One tile of the tree after another is chosen, so its card is read beside the tree.
        private static IEnumerator SkillTree()
        {
            var progression = UnityEngine.Object.FindAnyObjectByType<ProgressionHUD>();
            object tree = progression != null ? typeof(ProgressionHUD).GetField("skillTree", Any)?.GetValue(progression) : null;
            if (tree == null) { Log("no skill tree"); yield break; }
            var parts = tree.GetType().GetField("parts", Any)?.GetValue(tree) as IDictionary;
            var keys = new List<string>();
            if (parts != null) foreach (object key in parts.Keys) keys.Add((string)key);
            Log("tiles: " + keys.Count);
            for (int i = 0; i < keys.Count && i < 16; i += 3)
            {
                yield return Wait(1.4f);
                tree.GetType().GetMethod("Select", Any).Invoke(tree, new object[] { keys[i] });
            }
        }

        // The class is the weapon in hand: one weapon of the faction after another is put on.
        private static IEnumerator SwapWeapons()
        {
            for (int i = 0; i < 3; i++)
            {
                yield return Wait(2.2f);
                int cell = FindCell((def, stack) => def.IsLadderGear && def.equipSlot == EquipSlot.Weapon && def.rarity == ItemRarity.Epic, i);
                if (cell >= 0) local.RequestQuickEquip(cell);
                Log("weapon cell " + cell);
            }
        }

        private static IEnumerator Creds()
        {
            yield return Wait(2f); Cmd("gm.creds 125000");
            yield return Wait(2.5f); Cmd("gm.creds 480000");
        }

        private static IEnumerator Stock()
        {
            var kiosk = Ctl<KioskWindowController>();
            for (int i = 0; i < 3; i++)
            {
                yield return Wait(1.6f);
                if (Shown("amount-dialog")) Press("amount-ok");
                int cell = FindCell((def, stack) => def.IsLadderGear, i);
                if (cell >= 0) Log("stock " + cell + " = " + kiosk.TryQuick(ItemLocation.Bag(cell)));
            }
            yield return Wait(1.2f);
            if (Shown("amount-dialog")) Press("amount-ok");
        }

        private static IEnumerator Deposit()
        {
            var storage = Ctl<StorageWindowController>();
            for (int i = 0; i < 4; i++)
            {
                yield return Wait(1.5f);
                int cell = FindCell((def, stack) => def.IsLadderGear);
                if (cell >= 0) Log("deposit " + cell + " = " + storage.TryQuick(ItemLocation.Bag(cell)));
            }
        }

        private static IEnumerator Ascend()
        {
            for (int i = 0; i < 4; i++) { yield return Wait(2.2f); Press("herald-ascend"); }
        }

        private static IEnumerator Craft()
        {
            // One recipe after another is read, and the first is made.
            VisualElement list = Find("craft-list");
            List<VisualElement> rows = list != null ? list.Query(className: "craft-row").ToList() : new List<VisualElement>();
            Log("recipes: " + rows.Count);
            // An Alloy, the Chroma Cell, a Dossier; then an Alloy is made.
            foreach (int row in new[] { 1, 3, rows.Count - 1, 0 })
            {
                yield return Wait(1.4f);
                if (row >= 0 && row < rows.Count) Click(rows[row]);
            }
            yield return Wait(1.2f);
            Press("craft-act");
            yield return Wait(1.5f);
            Press("craft-act");
        }

        private static IEnumerator Weave()
        {
            // A few looks are tried on, one is put on.
            Press("weave-tab-armor");
            yield return Wait(1f);
            VisualElement grid = Find("weave-grid");
            List<VisualElement> looks = grid != null ? grid.Query(className: "weave-cell").ToList() : new List<VisualElement>();
            Log("looks: " + looks.Count);
            for (int i = 0; i < looks.Count && i < 4; i++)
            {
                yield return Wait(1.5f);
                Click(looks[i]);
            }
            yield return Wait(1.2f);
            Press("weave-act");
        }

        private static IEnumerator AtForge(string tab)
        {
            Cmd("gm.kit temper"); Cmd("gm.forgekit"); Cmd("gm.renown 200000"); Cmd("gm.creds 50000000");
            // The forge only stays open beside the Armorer.
            Cmd("gm.tp NPC_Blacksmith");
            yield return Wait(2.5f);
            var forge = Ctl<GearWorkWindowController>();
            forge.Open();
            closers.Add(() => forge.Close());
            closers.Add(() => { if (GearUIController.Instance != null) GearUIController.Instance.SetInventoryOpen(false); });
            yield return Wait(1.2f);
            if (tab != null) Press("forge-tab-" + tab);
            yield return Wait(0.6f);
        }

        private static int Untouched(EquipSlot slot, ItemRarity rarity, int skip = 0)
            => FindCell((def, stack) => def.IsLadderGear && def.equipSlot == slot && def.rarity == rarity && stack.data == 0, skip);

        private static IEnumerator Temper()
        {
            var forge = Ctl<GearWorkWindowController>();
            if (Untouched(EquipSlot.Weapon, ItemRarity.Rare) < 0) { Cmd("gm.kit gear 60 Rare"); yield return Wait(1.5f); }
            int cell = Untouched(EquipSlot.Weapon, ItemRarity.Rare);
            Log("piece " + cell + " placed=" + (cell >= 0 && forge.TryQuick(ItemLocation.Bag(cell))));
            for (int i = 0; i < 3; i++) { yield return Wait(2.6f); Press("forge-act"); }
        }

        private static int fusePiece = -1;

        // Two untouched Rares of one kind: one is tempered to what Fusion asks before the clip, the other is melted into it.
        private static IEnumerator FusePrepare()
        {
            var forge = Ctl<GearWorkWindowController>();
            Cmd("gm.kit gear 60 Rare"); Cmd("gm.kit gear 60 Rare");
            yield return Wait(1.5f);
            fusePiece = Untouched(EquipSlot.Armor, ItemRarity.Rare);
            Log("piece " + fusePiece + " placed=" + (fusePiece >= 0 && forge.TryQuick(ItemLocation.Bag(fusePiece))));
            yield return Wait(1f);
            for (int i = 0; i < 6; i++) { Press("forge-act"); yield return Wait(1.3f); }
        }

        private static IEnumerator Fuse()
        {
            var forge = Ctl<GearWorkWindowController>();
            yield return Wait(1.2f);
            Press("forge-tab-fusion");
            yield return Wait(1.8f);
            int material = Untouched(EquipSlot.Armor, ItemRarity.Rare);
            Log("material " + material + " placed=" + (material >= 0 && forge.TryQuick(ItemLocation.Bag(material))));
            yield return Wait(2.2f);
            Press("forge-act");
            yield return Wait(3f);
            Press("forge-act");
        }

        private static IEnumerator Tune()
        {
            var forge = Ctl<GearWorkWindowController>();
            if (Untouched(EquipSlot.Gloves, ItemRarity.Rare) < 0) { Cmd("gm.kit gear 60 Rare"); yield return Wait(1.5f); }
            int cell = Untouched(EquipSlot.Gloves, ItemRarity.Rare);
            Log("piece " + cell + " placed=" + (cell >= 0 && forge.TryQuick(ItemLocation.Bag(cell))));
            for (int i = 0; i < 2; i++) { yield return Wait(2.2f); Press("forge-act"); }
            yield return Wait(2f);
            Press("forge-tab-reforge");
            yield return Wait(1f);
            int spare = Untouched(EquipSlot.Boots, ItemRarity.Rare);
            Log("reforge piece " + spare + " placed=" + (spare >= 0 && forge.TryQuick(ItemLocation.Bag(spare))));
            yield return Wait(1.5f);
            Press("forge-act");
        }

        // ------------------------------------------------------------------ helpers

        private static void Log(string line)
        {
            try { File.AppendAllText(LogFile, line + "\n"); }
            catch (IOException) { }
        }

        private static WaitForSecondsRealtime Wait(float seconds) => new WaitForSecondsRealtime(seconds);
        private static T Ctl<T>() where T : UnityEngine.Object => UnityEngine.Object.FindAnyObjectByType<T>();
        private static void Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Any).Invoke(target, args);

        private static Type console;

        private static string Cmd(string command)
        {
            // The console is reached by name: this assembly does not reference it.
            console ??= AppDomain.CurrentDomain.GetAssemblies().Select(each => each.GetType("QFSW.QC.QuantumConsoleProcessor")).FirstOrDefault(found => found != null);
            if (console == null) { Log("> " + command + " : no console"); return string.Empty; }
            bool ready = (bool)console.GetProperty("TableGenerated").GetValue(null) || (bool)console.GetProperty("TableIsGenerating").GetValue(null);
            if (!ready) console.GetMethod("GenerateCommandTable").Invoke(null, new object[] { false, false });
            object result = console.GetMethod("InvokeCommand", new[] { typeof(string) }).Invoke(null, new object[] { command });
            Log("> " + command + " : " + result);
            return result != null ? result.ToString() : string.Empty;
        }

        private static VisualElement Find(string name)
        {
            foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                VisualElement found = document != null && document.rootVisualElement != null ? document.rootVisualElement.Q(name) : null;
                if (found != null) return found;
            }
            return null;
        }

        private static bool Shown(string name)
        {
            VisualElement element = Find(name);
            return element != null && element.resolvedStyle.display != DisplayStyle.None;
        }

        private static void Submit(VisualElement button)
        {
            using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
            {
                submit.target = button;
                button.SendEvent(submit);
            }
        }

        // A row or a cell that listens for the pointer going down on it, as a click would.
        private static void Click(VisualElement element)
        {
            var down = new Event { type = EventType.MouseDown, button = 0, clickCount = 1, mousePosition = element.worldBound.center };
            using (PointerDownEvent press = PointerDownEvent.GetPooled(down))
            {
                press.target = element;
                element.SendEvent(press);
            }
        }

        /// <summary>
        /// Stands these windows side by side from the top left, so the cut holds them and nothing between. Where
        /// the player keeps them (a saved layout) is not touched: each goes back when the clip is done.
        /// </summary>
        private static void Beside(params string[] names)
        {
            float x = 24f;
            foreach (string name in names)
            {
                VisualElement element = Find(name);
                if (element == null || element.resolvedStyle.display == DisplayStyle.None) { Log("beside: nothing shown named " + name); continue; }
                placed.Add(new Placed
                {
                    Element = element, Left = element.style.left, Top = element.style.top, Right = element.style.right,
                    Bottom = element.style.bottom, Translate = element.style.translate
                });
                element.style.left = x;
                element.style.top = 34f;
                element.style.right = StyleKeyword.Auto;
                element.style.bottom = StyleKeyword.Auto;
                element.style.translate = new Translate(0f, 0f);
                // As drawn: a window may be scaled (the forge is), and its own width says nothing of that.
                float width = element.worldBound.width;
                x += (float.IsNaN(width) || width < 2f ? 300f : width) + 16f;
            }
        }

        private static bool Press(string name)
        {
            if (!(Find(name) is Button button)) { Log("no button " + name); return false; }
            Submit(button);
            return true;
        }

        // A menu button toggles its window: it is pressed only when the window is not already as wanted.
        private static void Win(string window, string button, bool on)
        {
            if (Shown(window) != on) Press(button);
        }

        private static void Chat(string line)
        {
            var chat = Ctl<ChatWindowController>();
            var input = (TextField)typeof(ChatWindowController).GetField("input", Any).GetValue(chat);
            input.SetValueWithoutNotify(line);
            Call(chat, "Submit");
            Log("chat: " + line);
        }

        /// <summary>Hides the HUD but the parts named; with the canvases too, the plates and namecards over heads go as well.</summary>
        private static void Clean(bool canvasesOff, params string[] keep)
        {
            foreach (string name in HudParts)
            {
                if (Array.IndexOf(keep, name) >= 0) continue;
                VisualElement element = Find(name);
                if (element == null) continue;
                element.style.display = DisplayStyle.None;
                hidden.Add(element);
            }
            if (!canvasesOff) return;
            foreach (Canvas canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.enabled && canvas.isRootCanvas) { canvas.enabled = false; canvases.Add(canvas); }
        }

        /// <summary>
        /// The interface at its smallest, so a tall window is whole on the screen. Done on the panels themselves and
        /// put back after the clip: the player's own interface size (a saved preference) is not touched.
        /// </summary>
        private static void Small()
        {
            foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                VisualElement top = document != null ? document.rootVisualElement : null;
                if (top == null || solid.Contains(top)) continue;
                var steps = new List<string>();
                foreach (string name in top.GetClasses())
                    if (name.StartsWith("kn-win-") || name.StartsWith("kn-card-")) steps.Add(name);
                foreach (string name in steps) top.RemoveFromClassList(name);
                top.AddToClassList(PanelOpacity.WindowClass(ClipWindowPercent));
                top.AddToClassList(PanelOpacity.CardClass(ClipCardPercent));
                solid.Add(top);
            }
            float current = HudPreferences.InterfaceScale;
            foreach (UIDocument document in UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                PanelSettings panel = document != null ? document.panelSettings : null;
                if (panel == null || references.ContainsKey(panel)) continue;
                Vector2Int now = panel.referenceResolution;
                references[panel] = now;
                panel.referenceResolution = new Vector2Int(Mathf.RoundToInt(now.x * current / WindowScale), Mathf.RoundToInt(now.y * current / WindowScale));
            }
        }

        /// <summary>The clip keeps the part of the picture these elements take up, with a little air round them.</summary>
        private static void Focus(float pad, params string[] names)
        {
            Rect panel = hud.GetComponent<UIDocument>().rootVisualElement.worldBound;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (string name in names)
            {
                VisualElement element = Find(name);
                if (element == null || element.resolvedStyle.display == DisplayStyle.None) { Log("focus: nothing shown named " + name); continue; }
                Rect bound = element.worldBound;
                if (bound.width < 2f) continue;
                x0 = Mathf.Min(x0, bound.xMin); y0 = Mathf.Min(y0, bound.yMin);
                x1 = Mathf.Max(x1, bound.xMax); y1 = Mathf.Max(y1, bound.yMax);
            }
            if (x1 < x0) { Log("focus: nothing to cut to"); return; }
            // The close button and a frame's crown stand a little outside the window's own box.
            x0 = Mathf.Max(0f, x0 - pad); y0 = Mathf.Max(0f, y0 - pad - 12f);
            x1 = Mathf.Min(panel.width, x1 + pad + 8f); y1 = Mathf.Min(panel.height, y1 + pad);
            GuideClipRecorder.Crop = new Rect(x0 / panel.width, y0 / panel.height, (x1 - x0) / panel.width, (y1 - y0) / panel.height);
            Log("focus " + GuideClipRecorder.Crop);
        }

        private static bool Rec(float seconds, bool large)
        {
            bool ok = GuideClipRecorder.Record(topic, seconds, true, large ? LargeWidth : WorldWidth, large ? LargeHeight : WorldHeight);
            Log("recording=" + ok + " " + seconds + " s large=" + large);
            return ok;
        }

        private static IEnumerator Turn(float degrees, float seconds)
        {
            float from = orbit.HorizontalAxis.Value;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                orbit.HorizontalAxis.Value = from + degrees * Mathf.SmoothStep(0f, 1f, t / seconds);
                yield return null;
            }
        }

        private static int FindCell(Func<ItemDefinition, ItemStack, bool> wanted, int skip = 0)
        {
            for (int i = 0; i < PlayerInventory.Capacity; i++)
            {
                ItemDefinition def = local.GetDefinition(i);
                if (def == null || !wanted(def, local.Get(i))) continue;
                if (skip-- > 0) continue;
                return i;
            }
            return -1;
        }

        private static PlayerTrade Other()
        {
            PlayerTrade me = PlayerTrade.Local;
            foreach (PlayerTrade each in UnityEngine.Object.FindObjectsByType<PlayerTrade>(FindObjectsSortMode.None))
                if (each != me) return each;
            return null;
        }

        /// <summary>To a spot this far to the side of the point as the camera sees it, so both are in the picture.</summary>
        private static void StandBy(Vector3 point, float aside)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            Vector3 right = Quaternion.Euler(0f, orbit.HorizontalAxis.Value, 0f) * Vector3.right;
            Vector3 spot = point + right * aside;
            Cmd("gm.tpxyz " + spot.x.ToString("0.0", c) + " " + point.y.ToString("0.0", c) + " " + spot.z.ToString("0.0", c));
        }

        private static HostileNet Nearest()
        {
            HostileNet best = null;
            float bestDistance = 40f;
            foreach (HostileNet each in UnityEngine.Object.FindObjectsByType<HostileNet>(FindObjectsSortMode.None))
            {
                var entity = each.GetComponent<CombatEntity>();
                if (entity == null || !each.gameObject.activeInHierarchy || !entity.IsAlive) continue;
                float distance = Vector3.Distance(each.transform.position, player.transform.position);
                if (distance < bestDistance) { bestDistance = distance; best = each; }
            }
            return best;
        }

        // True where the sun reaches: a clip in a tower's shadow reads as night.
        private static bool Sunlit(Vector3 point)
        {
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (Light each in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (each.type == LightType.Directional && each.isActiveAndEnabled && (sun == null || each.intensity > sun.intensity)) sun = each;
            return sun == null || !Physics.Raycast(point + Vector3.up * 1.5f, -sun.transform.forward, 500f, ~0, QueryTriggerInteraction.Ignore);
        }

        private static bool SunlitRound(Vector3 point, float metres)
        {
            if (!Sunlit(point)) return false;
            for (int i = 0; i < 8; i++)
                if (!Sunlit(point + Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward * metres)) return false;
            return true;
        }

        /// <summary>To the nearest Hostile with the sun on it and on the ground round it; failing that, to the nearest Hostile Zone.</summary>
        private static IEnumerator ToHostiles()
        {
            HostileZone zone = null;
            foreach (HostileZone each in UnityEngine.Object.FindObjectsByType<HostileZone>(FindObjectsSortMode.None))
                if (zone == null || Vector3.Distance(each.transform.position, player.transform.position) < Vector3.Distance(zone.transform.position, player.transform.position)) zone = each;
            if (zone == null) { Log("no hostile zone"); yield break; }
            Log("zone " + zone.name + " at " + zone.transform.position);
            StandBy(zone.transform.position, 0f);
            yield return Wait(4f);
            HostileNet lit = null;
            float nearest = float.MaxValue;
            foreach (HostileNet each in UnityEngine.Object.FindObjectsByType<HostileNet>(FindObjectsSortMode.None))
            {
                var entity = each.GetComponent<CombatEntity>();
                if (entity == null || !each.gameObject.activeInHierarchy || !entity.IsAlive || !SunlitRound(each.transform.position, 7f)) continue;
                float distance = Vector3.Distance(each.transform.position, player.transform.position);
                if (distance < nearest) { nearest = distance; lit = each; }
            }
            Log(lit != null ? "sunlit hostile " + lit.name + " at " + lit.transform.position + ", " + nearest.ToString("0") + " m" : "no hostile in the sun");
            if (lit != null) StandBy(lit.transform.position, 4f);
            yield return Wait(3f);
        }
    }
}
#endif
