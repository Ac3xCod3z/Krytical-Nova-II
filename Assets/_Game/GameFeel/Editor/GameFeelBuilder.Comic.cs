using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KryticalNova.GameFeel;
using KryticalNova.Progression;
using MoreMountains.Feedbacks;
using MoreMountains.FeedbacksForThirdParty;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The comic layer of the game feel, made from the Cartoon FX pack: the mark each weapon class's hits leave, the
    /// words they hit with, the look kept for a critical hit on another player, and the words of the moments that
    /// have one of their own (a death, the forge, a level gained, a rare find, a pet's evolution). A mark is a copy
    /// of one of the pack's impacts. A word is made with the pack's own text tool, driven from here on a copy of
    /// ours: the tool is given the word and the lettering and rebuilds the effect, letter by letter. Every copy is
    /// stripped of the pack's authoring scripts and gets a <see cref="ComicFx"/> that says which of its layers
    /// takes which colour, so one pooled effect can be shown in the colours of any kind of hit.
    /// The pack is only read: its prefabs are instantiated, its fonts and textures referenced, and every material a
    /// word needs is a copy in <see cref="ComicMaterialFolder"/> (the tool writes to, renames and deletes the
    /// materials of what it rebuilds). The pack's scripts are not in an assembly this one can name, so the tool is
    /// reached by reflection.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string ComicFolder = VfxFolder + "/Comic";
        public const string ComicMaterialFolder = MaterialFolder + "/Comic";
        public const string HitLookSettingsPath = "Assets/_Game/GameFeel/Data/Assets/HitLookSettings.asset";
        private const string CartoonPack = "Assets/_ThirdParty/Cartoon FX Pack v2/";
        private const string CartoonFonts = CartoonPack + "Assets/Data/Components/Fonts/";
        private const string CartoonTool = "CTFX2.Creator.Tool.CartoonFX";

        private static string ComicPath(string name) => $"{ComicFolder}/{name}.prefab";

        // ---------------------------------------------------------------- tables

        /// <summary>A hit mark: a copy of one of the pack's impacts.</summary>
        private readonly struct Mark
        {
            public readonly string Name, Source;
            public readonly float Scale;
            public Mark(string name, string source, float scale) { Name = name; Source = source; Scale = scale; }
        }

        /// <summary>A word: one of the pack's text effects, given another word and lettering by the pack's own tool.</summary>
        private readonly struct Word
        {
            public readonly string Name, Source, Font;
            public readonly string[] Lines;
            /// <summary>How tall its capital letters are, in metres: the pack's effects come in sizes from a hand's width to a house, and its fonts fill their squares very differently.</summary>
            public readonly float Size;
            /// <summary>Its letters are together no wider than this many metres: a long word is made smaller to stay that short.</summary>
            public readonly float Widest;
            public readonly float Spacing;
            /// <summary>The colours it is built in. Null: the colours of the effect it is made from.</summary>
            public readonly ComicRamp? Ramp;
            public Word(string name, string source, string font, float size, float widest, string line, float spacing = 1f, ComicRamp? ramp = null)
            {
                Name = name; Source = source; Font = font; Size = size; Widest = widest; Lines = line != null ? new[] { line } : null;
                Spacing = spacing; Ramp = ramp;
            }
        }

        /// <summary>How one weapon class's hits look: its mark (in a light and a heavy form), its lettering and its three words.</summary>
        private readonly struct ClassComic
        {
            public readonly WeaponClassId Class;
            public readonly string LightMark, HeavyMark, Font;
            public readonly (string word, string source) Heavy, Critical, Skill;
            public readonly Color Swing;
            /// <summary>Its letters are drawn this much bigger than the rest: a narrow font needs it to be read as easily.</summary>
            public readonly float Letters;
            /// <summary>Its marks are drawn this much bigger than the rest: a shape of thin lines (a cut, a claw's rake) needs it to be seen as easily as a cloud.</summary>
            public readonly float Marks;
            public ClassComic(WeaponClassId id, string lightMark, string heavyMark, string font, Color swing,
                (string, string) heavy, (string, string) critical, (string, string) skill, float letters = 1f, float marks = 1f)
            {
                Class = id; LightMark = lightMark; HeavyMark = heavyMark; Font = font; Swing = swing; Heavy = heavy; Critical = critical; Skill = skill;
                Letters = letters; Marks = marks;
            }
        }

        // A mark is this many metres across at its widest (the pack's impacts come in all sizes), and is on screen
        // at least this long: the pack's lightest impacts are gone in a tenth of a second, before their shape is seen.
        private const float LightMarkSize = 0.9f, HeavyMarkSize = 1.35f;
        private const float ShortestMarkSeconds = 0.18f;
        // A word: how tall its capital letters are (x) and how wide its letters may be together (y), in metres. A
        // character is 1.8 m tall. Short words reach the height; long ones are held to the width. The look of a
        // critical hit on a player is half as large again as any other word of a hit.
        private static readonly Vector2 HeavyWordSize = new Vector2(0.26f, 1.35f), SkillWordSize = new Vector2(0.29f, 1.5f),
            CriticalWordSize = new Vector2(0.33f, 1.7f), PvpPanelSize = new Vector2(0.5f, 2.6f);
        // What is drawn behind a word (its burst, its dots) may be this much wider than the word may be: some of the
        // pack's bursts are very wide for their letters, and a hit's word must not hide the fight.
        private const float BurstOverWord = 1.45f;
        // How much of a letter's own width the next letter starts after it: under 1 they overlap a little, as comic
        // lettering does.
        private const float LetterPitch = 0.94f;
        // No letter of a word is drawn smaller than this share of its largest one: some of the pack's effects let
        // their word trail away to nothing, and such a word is not read to its end.
        private const float SmallestLetter = 0.55f;
        // The moments of an effect's life it is measured at, as shares of it (a burst is widest early), and the one
        // at which its letters are: half way, when they have flown in and not yet begun to leave.
        private static readonly float[] MeasuredAt = { 0.12f, 0.3f, 0.5f, 0.72f };
        private const float LettersMeasuredAt = 0.5f;
        // A word and the burst behind it stay at least this long: some of the pack's are gone in a third of a second.
        private const float ShortestWordSeconds = 0.55f;

        // Each class owns one shape of the pack's impacts: the plain form for a light hit, the form with the coloured
        // fringe for a heavy one. Its lettering is one of the pack's fonts, in its faction's family: Hegemony's are
        // clean and military, Null's are broken and eerie, Covenant's are carved and old.
        private static readonly ClassComic[] ClassComics =
        {
            new ClassComic(WeaponClassId.Monoblade, "Impact 009", "Impact 023", "Shojumaru", Hex("4F9BF0"),
                ("SHING!", "Texts/Text 030"), ("SLASH!", "Texts/Text 020"), ("SLICE!", "Texts/Text 023"), marks: 1.4f),      // a crossed cut
            new ClassComic(WeaponClassId.Glaive, "Impact 012", "Impact 026", "Black Ops One", Hex("E04FB8"),
                ("SWISH!", "Texts/Text 023"), ("THWACK!", "Texts/Text 019"), ("WHOOSH!", "Texts/Text 010"), marks: 1.4f),    // a sweep
            new ClassComic(WeaponClassId.Carbine, "Impact 010", "Impact 024", "Bungee", Hex("4FE3F0"),
                ("BANG!", "Texts/Text 018"), ("BLAM!", "Texts/Text 022"), ("RATATAT!", "Texts/Text 024"), marks: 1.25f),     // a muzzle star
            new ClassComic(WeaponClassId.Harvester, "Impact 001", "Impact 015", "Creepster", Hex("F03A2E"),
                ("SHRED!", "Texts/Text 014"), ("REAP!", "Texts/Text 017"), ("CULL!", "Texts/Text 013"), 1.2f, 1.5f),         // streaks of a scythe
            new ClassComic(WeaponClassId.ServoClaws, "Impact 011", "Impact 025", "Rubik Glitch", Hex("39D1B4"),
                ("RAKE!", "Texts/Text 026"), ("REND!", "Explosions/Explosion 044"), ("RIP!", "Texts/Text 030"), marks: 1.4f), // a claw's rake
            new ClassComic(WeaponClassId.ArcCaster, "Impact 013", "Impact 027", "Rubik Wet Paint", Hex("9B6BFF"),
                ("ZAP!", "Texts/Text 021"), ("KRAKL!", "Explosions/Explosion 047"), ("BZZZT!", "Texts/Text 004"), marks: 1.6f), // rays
            new ClassComic(WeaponClassId.Longaxe, "Impact 007", "Impact 021", "Caesar Dressing", Hex("FF7A3C"),
                ("CHOP!", "Texts/Text 018"), ("KRAK!", "Texts/Text 019"), ("THOOM!", "Texts/Text 011"), 1.12f),              // a star
            new ClassComic(WeaponClassId.Claymore, "Impact 002", "Impact 016", "Berkshire Swash", Hex("FFC93C"),
                ("KLANG!", "Texts/Text 023"), ("BASH!", "Texts/Text 020"), ("KRASH!", "Texts/Text 022")),                    // a cloud
            new ClassComic(WeaponClassId.Warbow, "Impact 006", "Impact 020", "Jolly Lodger", Hex("6FD06A"),
                ("THWIP!", "Texts/Text 030"), ("THUNK!", "Explosions/Explosion 041"), ("TWANG!", "Texts/Text 013"), 1.22f),  // a ring
        };

        /// <summary>Marks that belong to no weapon class.</summary>
        private static readonly Mark[] OtherMarks =
        {
            new Mark("PFX_Mark_Unarmed", "Impact 014", LightMarkSize),         // a creature, a GM command
            new Mark("PFX_MarkHeavy_Unarmed", "Impact 028", HeavyMarkSize),
            new Mark("PFX_Mark_Pickup", "Impact 005", 1f),                     // a drop taken off the ground
            new Mark("PFX_Mark_Death", "Explosion 022", 2.2f),                 // the ink cloud of a death
            new Mark("PFX_Mark_LevelUp", "Explosion 006", 2.4f),               // a ring of puffs round a character that gained a level
            new Mark("PFX_Mark_PvpLines", "Impact 013", 4.5f),                 // lines of speed through a critical hit on a player
        };

        private static readonly ComicRamp InkOnPaper = new ComicRamp(Hex("F03A2E"), Hex("F4EEDF"), Hex("14122B"), Hex("14122B"));
        private static readonly ComicRamp Gold = new ComicRamp(Color.white, Hex("FFC93C"), Hex("FF9E66"), Hex("14122B"));
        private static readonly ComicRamp Broken = new ComicRamp(Color.white, Hex("F03A2E"), Hex("8C8A94"), Hex("14122B"));
        private static readonly ComicRamp Saved = new ComicRamp(Color.white, Hex("4FE3F0"), Hex("1F6ECC"), Hex("14122B"));
        private static readonly ComicRamp DustRamp = new ComicRamp(Color.white, Hex("F4EEDF"), Hex("8C8A94"), Hex("14122B"));
        private static readonly ComicRamp CritDeath = new ComicRamp(Hex("FFC93C"), Hex("4FE3F0"), Hex("F03A2E"), Hex("14122B"));
        private static readonly ComicRamp RareFind = new ComicRamp(Color.white, Hex("FFC93C"), Hex("FF9E66"), Hex("14122B"));
        private static readonly ComicRamp EpicFind = new ComicRamp(Color.white, Hex("B06BFF"), Hex("7A4FE0"), Hex("14122B"));
        // The impact frame: white letters on a burst of ink, lines of magenta through it. Nothing else in the game
        // is drawn without colour, which is what makes it the one hit that cannot be mistaken.
        private static readonly ComicRamp ImpactFrame = new ComicRamp(Color.white, Color.white, Hex("14122B"), Hex("14122B"));
        private static readonly Color ImpactFrameLines = Hex("E04FB8");
        private static readonly ComicRamp SplashPanel = new ComicRamp(Color.white, Hex("FFC93C"), Hex("F03A2E"), Hex("14122B"));

        /// <summary>Words that belong to one moment each, and to nothing else.</summary>
        private static readonly Word[] OtherWords =
        {
            new Word("PFX_Word_Backlash", "Texts/Text 013", "Knewave", 0.28f, 1.9f, "BACKLASH!"),     // a blow thrown back
            new Word("PFX_Word_Shatter", "Texts/Text 003", "Frijole", 0.3f, 1.9f, "SHATTER!"),        // a barrier broken
            new Word("PFX_Word_KO", "Explosions/Explosion 040", "Luckiest Guy", 0.36f, 1.4f, "K.O.!", 1f, InkOnPaper),   // a death
            new Word("PFX_Word_Wham", "Texts/Text 022", "Bangers", 0.42f, 1.8f, "WHAM!", 1f, CritDeath),          // a critical death
            new Word("PFX_Word_Thud", "Texts/Text 016", "Titan One", 0.3f, 1.4f, "THUD!", 1f, DustRamp),          // a thrown body comes down
            new Word("PFX_Word_Forged", "Texts/Text 029", "Alfa Slab One", 0.3f, 1.7f, "FORGED!", 1f, Gold),      // the forge: it worked
            new Word("PFX_Word_Crack", "Texts/Text 012", "Frijole", 0.3f, 1.6f, "CRACK!", 1f, Broken),            // the forge: the piece broke
            new Word("PFX_Word_Saved", "Texts/Text 027", "Carter One", 0.3f, 1.6f, "SAVED!", 1f, Saved),          // the forge: a Failsafe held
            new Word("PFX_Word_LevelUp", "Texts/Text 011", "Bungee Shade", 0.34f, 2.3f, "LEVEL UP!"), // in the faction's colours at run time
            new Word("PFX_Word_Evolved", "Texts/Text 001", "Slackey", 0.26f, 1.5f, "EVOLVED!"),       // in the pet's colour at run time
            new Word("PFX_Word_RareFind", "Texts/Text 033", null, 0.55f, 1.2f, null, 1f, RareFind),   // "!!" as the pack made it, in Rare's yellow
            new Word("PFX_Word_EpicFind", "Texts/Text 034", null, 0.55f, 1.5f, null, 1f, EpicFind),   // "!!!", in Epic's purple
            // A critical hit on another player: three looks to choose from (HitLookSettings.pvpCritStyle).
            new Word("PFX_PvpCrit_Splash", "Explosions/Explosion 055", "Bangers", PvpPanelSize.x, PvpPanelSize.y, "KABLAM!", 1f, SplashPanel),
            new Word("PFX_PvpCrit_Frame", "Explosions/Explosion 043", "Permanent Marker", PvpPanelSize.x, PvpPanelSize.y, "CRIT!", 1f, ImpactFrame),
            new Word("PFX_PvpCrit_Hegemony", "Explosions/Explosion 042", "Black Ops One", PvpPanelSize.x, PvpPanelSize.y, "DENIED!"),
            new Word("PFX_PvpCrit_Null", "Explosions/Explosion 042", "Rubik Glitch", PvpPanelSize.x, PvpPanelSize.y, "ERASED!"),
            new Word("PFX_PvpCrit_Covenant", "Explosions/Explosion 042", "Caesar Dressing", PvpPanelSize.x, PvpPanelSize.y, "JUDGED!"),
        };

        private static Color Hex(string rgb) => ColorUtility.TryParseHtmlString("#" + rgb, out Color colour) ? colour : Color.white;

        private static string ClassName(WeaponClassId id) => id.ToString();

        // ---------------------------------------------------------------- menus

        /// <summary>
        /// Builds every comic mark and word, the hit look settings that hand them out, and the players of the game
        /// feel prefab that show them. Takes a few minutes: each word is rebuilt by the pack's tool.
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Build Comic Effects")]
        public static void BuildComicEffects()
        {
            try
            {
                var words = AllWords();
                int done = 0;
                BuildMarks();
                foreach (Word word in words)
                {
                    EditorUtility.DisplayProgressBar("Comic effects", word.Name, (float)done++ / words.Count);
                    BuildWord(word);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            BuildHitLookSettings();
            RebuildComicFeel();
            Debug.Log($"[GameFeel] Comic marks and words built in {ComicFolder}; hit looks in {HitLookSettingsPath}.");
        }

        /// <summary>Every word there is: three per weapon class, and the ones of the single moments.</summary>
        private static List<Word> AllWords()
        {
            var words = new List<Word>();
            foreach (ClassComic comic in ClassComics)
            {
                string name = ClassName(comic.Class);
                words.Add(new Word($"PFX_Word_{name}_Heavy", comic.Heavy.source, comic.Font, HeavyWordSize.x * comic.Letters, HeavyWordSize.y,
                    comic.Heavy.word));
                words.Add(new Word($"PFX_Word_{name}_Critical", comic.Critical.source, comic.Font, CriticalWordSize.x * comic.Letters,
                    CriticalWordSize.y, comic.Critical.word));
                words.Add(new Word($"PFX_Word_{name}_Skill", comic.Skill.source, comic.Font, SkillWordSize.x * comic.Letters, SkillWordSize.y,
                    comic.Skill.word));
            }
            words.AddRange(OtherWords);
            return words;
        }

        /// <summary>Editor scripting: the names of every word, and one word built by name (for a run spread over frames).</summary>
        public static List<string> ComicWordNames()
        {
            var names = new List<string>();
            foreach (Word word in AllWords()) names.Add(word.Name);
            return names;
        }

        public static bool BuildComicWord(string name)
        {
            foreach (Word word in AllWords())
                if (word.Name == name) return BuildWord(word);
            Debug.LogError($"[GameFeel] No comic word named '{name}'.");
            return false;
        }

        // ---------------------------------------------------------------- marks

        public static void BuildMarks()
        {
            WeatherVfxBuilder.EnsureFolder(ComicFolder);
            foreach (ClassComic comic in ClassComics)
            {
                string name = ClassName(comic.Class);
                BuildMark(new Mark($"PFX_Mark_{name}", comic.LightMark, LightMarkSize * comic.Marks));
                BuildMark(new Mark($"PFX_MarkHeavy_{name}", comic.HeavyMark, HeavyMarkSize * comic.Marks));
            }
            foreach (Mark mark in OtherMarks) BuildMark(mark);
            AssetDatabase.SaveAssets();
        }

        private static void BuildMark(in Mark mark)
        {
            string folder = mark.Source.StartsWith("Explosion", StringComparison.Ordinal) ? "Explosions/" : "Impacts/";
            GameObject source = Load(CartoonFx + folder + mark.Source + ".prefab");
            if (source == null)
            {
                Debug.LogError($"[GameFeel] The Cartoon FX pack has no '{mark.Source}': {mark.Name} was not built.");
                return;
            }
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = mark.Name;
                go.transform.position = Vector3.zero;
                StayToBeSeen(go);
                Finish(go, 1f, null);
                // The mark's size is what the table says, whatever size the pack made the impact it comes from.
                Extent extent = Measure(go, null, null);
                float across = Mathf.Max(extent.Width, extent.Height);
                if (across > 0.001f) go.transform.localScale = Vector3.one * (mark.Scale / across);
                PrefabUtility.SaveAsPrefabAsset(go, ComicPath(mark.Name));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Gives a mark at least <see cref="ShortestMarkSeconds"/>: every layer's life and delay are stretched by
        /// the same amount, so its shape plays out as the pack drew it, only slower.
        /// </summary>
        private static void StayToBeSeen(GameObject go)
        {
            float longest = 0f;
            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
                if (system.gameObject != go) longest = Mathf.Max(longest, system.main.startDelay.constantMax + system.main.startLifetime.constantMax);
            if (longest <= 0f || longest >= ShortestMarkSeconds) return;
            float stretch = ShortestMarkSeconds / longest;
            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.gameObject == go) continue;
                // The pack's tool leaves the effect playing, and a system that plays refuses new timings.
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.startLifetimeMultiplier *= stretch;
                main.startDelayMultiplier *= stretch;
            }
        }

        // ---------------------------------------------------------------- words

        private static bool BuildWord(in Word word)
        {
            WeatherVfxBuilder.EnsureFolder(ComicFolder);
            GameObject source = Load(CartoonFx + word.Source + ".prefab");
            if (source == null)
            {
                Debug.LogError($"[GameFeel] The Cartoon FX pack has no '{word.Source}': {word.Name} was not built.");
                return false;
            }
            string materials = $"{ComicMaterialFolder}/{word.Name}";
            WeatherVfxBuilder.EnsureFolder(materials);

            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = word.Name;
                go.transform.position = Vector3.zero;
                MonoBehaviour tool = Tool(go);
                if (tool == null)
                {
                    Debug.LogError($"[GameFeel] '{word.Source}' has no Cartoon FX tool on it: {word.Name} was not built.");
                    return false;
                }

                // Our own copy of every material first: from here on nothing the tool does can reach the pack's.
                foreach (MonoBehaviour component in Components(tool))
                {
                    var theirs = Get(component, "Material") as Material;
                    if (theirs == null) continue;
                    Give(component, OwnMaterial(materials, component, theirs));
                }

                // The word and its lettering, then the tool rebuilds the effect.
                object text = Get(tool, "text");
                var modules = Get(text, "billboards") as Array;
                if (word.Lines != null) Set(text, "text", word.Lines);
                if (word.Font != null && modules != null)
                {
                    foreach (object module in modules)
                    {
                        var font = Get(module, "Object") as Object;
                        string now = font != null ? font.name : string.Empty;
                        // The pack's fonts come in two cuts; a layer keeps the cut it had.
                        string wanted = now.EndsWith(" Thinn", StringComparison.Ordinal) ? word.Font + " Thinn" : word.Font;
                        var replacement = AssetDatabase.LoadAssetAtPath<ScriptableObject>(CartoonFonts + wanted + ".asset");
                        if (replacement == null)
                        {
                            Debug.LogError($"[GameFeel] The Cartoon FX pack has no font '{wanted}': {word.Name} keeps '{now}'.");
                            continue;
                        }
                        Set(module, "Object", replacement);
                    }
                }
                if (word.Lines != null && modules != null && modules.Length > 0) SpaceLetters(text, modules, word);
                StayToBeRead(tool);
                KeepLettersReadable(text);

                tool.enabled = false; // the tool only rebuilds the selected object, unless it is switched off
                Call(tool, "ResetSystem");

                // Letters the word has more of than the effect it was made from come without a material: the tool's
                // template for a letter, copied to ours. Then once more, so the new letters are set up too.
                bool added = false;
                foreach (MonoBehaviour component in Missing(tool))
                {
                    var template = Get(Get(component, "Reference"), "material") as Material;
                    if (template == null) continue;
                    Give(component, OwnMaterial(materials, component, template));
                    added = true;
                }
                if (added) Call(tool, "ResetSystem");
                foreach (MonoBehaviour component in Missing(tool))
                {
                    Debug.LogError($"[GameFeel] {word.Name}: '{component.name}' has no material. The word was not built.");
                    return false;
                }

                if (word.Lines != null && modules != null) Kern(tool, text, modules, word);
                // The word's letters are as tall as the table says, whatever size the pack made the effect it comes
                // from; a long word is made smaller, so its letters stay within the width the table gives it, and
                // so is one whose burst is far wider than its letters.
                float tall = modules != null ? LetterHeight(tool, modules) : 0f;
                var letters = new HashSet<ParticleSystem>();
                var loose = new HashSet<ParticleSystem>();
                Sort(go, letters, loose);
                Finish(go, 1f, word.Ramp);
                Extent extent = Measure(go, letters, loose);
                float scale = tall > 0.0001f ? word.Size / tall : 1f;
                if (word.Widest > 0f)
                {
                    if (extent.WordWidth * scale > word.Widest) scale = word.Widest / extent.WordWidth;
                    if (extent.Width * scale > word.Widest * BurstOverWord) scale = word.Widest * BurstOverWord / extent.Width;
                }
                go.transform.localScale = Vector3.one * scale;
                go.GetComponent<ComicFx>().ApplyAuthored(); // the measuring played it: it is saved wearing its own colours
                PrefabUtility.SaveAsPrefabAsset(go, ComicPath(word.Name));
                RemoveUnused(materials, go);
                return true;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Gives the word, and what is drawn behind it, at least <see cref="ShortestWordSeconds"/>: every layer that
        /// lasts as long as the letters is stretched by the same amount, so they still go together. The flash of an
        /// impact, smoke and debris keep their own time.
        /// </summary>
        private static void StayToBeRead(MonoBehaviour tool)
        {
            object text = Get(tool, "text");
            float seconds = (float)Get(text, "lifetime");
            if (seconds <= 0f || seconds >= ShortestWordSeconds) return;
            float stretch = ShortestWordSeconds / seconds;
            foreach (string layer in new[] { "text", "background", "halftone", "effects" })
            {
                object settings = Get(tool, layer);
                if (settings != null) Set(settings, "lifetime", (float)Get(settings, "lifetime") * stretch);
            }
        }

        /// <summary>
        /// Holds the smallest letter of a word to <see cref="SmallestLetter"/> of its largest: the curve by which
        /// an effect grows or shrinks its letters along the word keeps its shape, over a smaller range.
        /// </summary>
        private static void KeepLettersReadable(object text)
        {
            if (!(Get(text, "sizeOverText") is AnimationCurve curve) || curve.length == 0) return;
            float low = float.MaxValue, high = 0f;
            for (int i = 0; i <= 32; i++)
            {
                float value = curve.Evaluate(i / 32f);
                low = Mathf.Min(low, value);
                high = Mathf.Max(high, value);
            }
            float floor = high * SmallestLetter;
            if (high <= 0f || low >= floor) return;
            float squeeze = (high - floor) / (high - low);
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value = floor + (keys[i].value - low) * squeeze;
                keys[i].inTangent *= squeeze;
                keys[i].outTangent *= squeeze;
            }
            Set(text, "sizeOverText", new AnimationCurve(keys));
        }

        /// <summary>
        /// Sets how far apart the tool puts the letters, from how wide this word's letters really are in its font
        /// (measured on the font's sheet): the tool spaces every letter alike, and an effect of the pack is spaced
        /// for the font and the word it came with.
        /// </summary>
        private static void SpaceLetters(object text, Array modules, in Word word)
        {
            float size = 0f, widest = 0f, mean = 0f;
            int counted = 0;
            foreach (object module in modules)
            {
                var range = (Vector2)Get(module, "size");
                size = Mathf.Max(size, range.y);
                if (!(Get(module, "Object") is ScriptableObject font)) continue;
                float[] widths = GlyphBoxes(font)?.widths;
                if (widths == null) continue;
                foreach (string line in word.Lines)
                    foreach (char letter in line)
                    {
                        if (letter == ' ') continue;
                        int index = (int)font.GetType().GetMethod("GetIndex").Invoke(font, new object[] { letter });
                        if (index < 0 || index >= widths.Length) continue;
                        widest = Mathf.Max(widest, widths[index]);
                        mean += widths[index];
                        counted++;
                    }
            }
            if (counted == 0 || size <= 0f) return;
            mean /= counted;
            // Between the average letter and the widest: the tool cannot give an I less room than an M.
            Set(text, "spacing", size * Mathf.Lerp(mean, widest, 0.4f) * LetterPitch * word.Spacing);
        }

        /// <summary>
        /// Moves each letter to where a letter of its width belongs. The tool puts letters at even steps, which
        /// leaves holes round an I and crowds an M; here every letter gets the room its own glyph needs, at the size
        /// that letter has (some effects grow their letters along the word). Where a letter comes to rest is where
        /// it starts plus how far its own motion carries it, whichever way the effect brings its letters in (flying
        /// out from the middle, sliding in from the side): so a letter is moved by moving where it starts.
        /// </summary>
        private static void Kern(MonoBehaviour tool, object text, Array modules, in Word word)
        {
            ScriptableObject font = null;
            foreach (object module in modules)
                if (Get(module, "Object") is ScriptableObject each) font = each;
            float[] widths = font != null ? GlyphBoxes(font)?.widths : null;
            if (widths == null) return;
            MethodInfo indexOf = font.GetType().GetMethod("GetIndex");

            for (int l = 0; l < word.Lines.Length; l++)
            {
                string line = word.Lines[l];
                int count = line.Length;
                if (count < 2) continue;

                // Each letter's layers, how big the letter is, and where the tool has it come to rest.
                var layers = new List<ParticleSystem>[count];
                var size = new float[count];
                var rest = new float[count];
                var known = new bool[count];
                foreach (MonoBehaviour letter in Components(tool))
                {
                    if (letter.GetType().Name != "TextComponent" || (int)Get(letter, "Line") != l) continue;
                    int j = (int)Get(letter, "Letter");
                    if (j < 0 || j >= count || !letter.TryGetComponent(out ParticleSystem system)) continue;
                    (layers[j] ??= new List<ParticleSystem>()).Add(system);
                    size[j] = Mathf.Max(size[j], system.main.startSize.constantMax);
                    if (known[j]) continue;
                    rest[j] = system.shape.position.x + Travel(system);
                    known[j] = true;
                }
                if (!known[0] || !known[count - 1]) continue;

                // The room each letter takes, and from that where its middle belongs, the line centred on 0.
                var room = new float[count];
                float total = 0f;
                for (int j = 0; j < count; j++)
                {
                    int index = line[j] == ' ' ? -1 : (int)indexOf.Invoke(font, new object[] { line[j] });
                    float glyph = index >= 0 && index < widths.Length ? widths[index] : 0.45f; // a space is a narrow letter
                    float letterSize = size[j] > 0f ? size[j] : size[Mathf.Max(0, j - 1)];
                    room[j] = letterSize * Mathf.Max(0.22f, glyph) * LetterPitch * word.Spacing;
                    total += room[j];
                }
                // The word keeps its middle and the way it reads (an effect faces the camera, so its first letter
                // is the one furthest along one side).
                float middle = (rest[0] + rest[count - 1]) * 0.5f;
                float direction = rest[count - 1] >= rest[0] ? 1f : -1f;
                float at = -total * 0.5f;
                for (int j = 0; j < count; j++)
                {
                    float wanted = middle + direction * (at + room[j] * 0.5f);
                    at += room[j];
                    if (layers[j] == null || !known[j]) continue;
                    float move = wanted - rest[j];
                    foreach (ParticleSystem system in layers[j])
                    {
                        var shape = system.shape;
                        shape.position += new Vector3(move, 0f, 0f);
                    }
                }
            }
        }

        /// <summary>How far along x a system's own motion carries its particle over its life.</summary>
        private static float Travel(ParticleSystem system)
        {
            var velocity = system.velocityOverLifetime;
            if (!velocity.enabled) return 0f;
            float life = system.main.startLifetime.constantMax;
            ParticleSystem.MinMaxCurve x = velocity.x;
            switch (x.mode)
            {
                case ParticleSystemCurveMode.Constant: return x.constant * life;
                case ParticleSystemCurveMode.TwoConstants: return x.constantMax * life;
                default:
                    AnimationCurve curve = x.mode == ParticleSystemCurveMode.TwoCurves ? x.curveMax : x.curve;
                    if (curve == null) return 0f;
                    const int Steps = 240;
                    float sum = 0f;
                    for (int i = 0; i < Steps; i++) sum += curve.Evaluate((i + 0.5f) / Steps);
                    return sum / Steps * x.curveMultiplier * life;
            }
        }

        /// <summary>
        /// How tall the word's letters are, in metres at scale 1: the size each letter is drawn at times how much of
        /// its square its glyph fills, averaged over the letters (some effects grow their letters along the word).
        /// </summary>
        private static float LetterHeight(MonoBehaviour tool, Array modules)
        {
            var tallest = new Dictionary<int, float>(); // per letter (line * 1000 + letter): its tallest layer
            foreach (MonoBehaviour letter in Components(tool))
            {
                if (letter.GetType().Name != "TextComponent" || !letter.TryGetComponent(out ParticleSystem system)) continue;
                char character = (char)Get(letter, "Character");
                if (character == ' ') continue;
                int layer = Mathf.Clamp((int)Get(letter, "Id"), 0, modules.Length - 1);
                if (!(Get(modules.GetValue(layer), "Object") is ScriptableObject font)) continue;
                float[] heights = GlyphBoxes(font)?.heights;
                if (heights == null) continue;
                int index = (int)font.GetType().GetMethod("GetIndex").Invoke(font, new object[] { character });
                if (index < 0 || index >= heights.Length || heights[index] <= 0f) continue;
                int key = (int)Get(letter, "Line") * 1000 + (int)Get(letter, "Letter");
                float height = system.main.startSize.constantMax * heights[index];
                tallest[key] = tallest.TryGetValue(key, out float had) ? Mathf.Max(had, height) : height;
            }
            if (tallest.Count == 0) return 0f;
            float sum = 0f;
            foreach (float each in tallest.Values) sum += each;
            return sum / tallest.Count;
        }

        private sealed class Glyphs
        {
            public float[] widths, heights;
        }

        private static readonly Dictionary<Object, Glyphs> glyphBoxes = new Dictionary<Object, Glyphs>();

        /// <summary>How wide and how tall each glyph of a font is, as a share of its cell on the font's sheet. Measured once a font.</summary>
        private static Glyphs GlyphBoxes(ScriptableObject font)
        {
            if (glyphBoxes.TryGetValue(font, out Glyphs known)) return known;
            Glyphs glyphs = null;
            if (Get(font, "albedo") is Texture sheet)
            {
                var grid = (Vector2)Get(font, "flipbookSize");
                int columns = Mathf.Max(1, Mathf.RoundToInt(grid.x)), rows = Mathf.Max(1, Mathf.RoundToInt(grid.y));
                const int Size = 1024; // enough to tell a letter's size to a hundredth of its cell
                var target = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RenderTexture before = RenderTexture.active;
                Graphics.Blit(sheet, target);
                RenderTexture.active = target;
                var read = new Texture2D(Size, Size, TextureFormat.RGBA32, false, true);
                read.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                read.Apply();
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                Color32[] pixels = read.GetPixels32();
                Object.DestroyImmediate(read);

                glyphs = new Glyphs { widths = new float[columns * rows], heights = new float[columns * rows] };
                float cellWidth = (float)Size / columns, cellHeight = (float)Size / rows;
                for (int index = 0; index < glyphs.widths.Length; index++)
                {
                    // Cells count from the sheet's top row; a texture's rows count from the bottom.
                    int x0 = Mathf.RoundToInt(index % columns * cellWidth), x1 = Mathf.RoundToInt((index % columns + 1) * cellWidth);
                    int row = rows - 1 - index / columns;
                    int y0 = Mathf.RoundToInt(row * cellHeight), y1 = Mathf.RoundToInt((row + 1) * cellHeight);
                    int left = int.MaxValue, right = -1, bottom = int.MaxValue, top = -1;
                    for (int y = y0; y < y1; y++)
                        for (int x = x0; x < x1; x++)
                        {
                            if (pixels[y * Size + x].a < 40) continue;
                            if (x < left) left = x;
                            if (x > right) right = x;
                            if (y < bottom) bottom = y;
                            if (y > top) top = y;
                        }
                    glyphs.widths[index] = right >= left ? (right - left + 1) / cellWidth : 0f;
                    glyphs.heights[index] = top >= bottom ? (top - bottom + 1) / cellHeight : 0f;
                }
            }
            glyphBoxes[font] = glyphs;
            return glyphs;
        }

        /// <summary>What an effect measures when it plays, in metres at scale 1.</summary>
        private struct Extent
        {
            /// <summary>The whole effect at its widest and its tallest.</summary>
            public float Width, Height;
            /// <summary>Its letters alone, from the first one's edge to the last one's.</summary>
            public float WordWidth;
        }

        private static readonly ParticleSystem.Particle[] measured = new ParticleSystem.Particle[512];

        /// <summary>
        /// Which of an effect's layers are its letters, and which fly loose of it (debris, smoke and their trails):
        /// noted while the pack's scripts are still on the layers to say what each one is.
        /// </summary>
        private static void Sort(GameObject go, HashSet<ParticleSystem> letters, HashSet<ParticleSystem> loose)
        {
            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                string type = LayerType(system);
                if (type == "Text") letters.Add(system);
                else if (type == "Debris" || type == "Smoke" || (type != null && type.EndsWith("Trail", StringComparison.Ordinal))) loose.Add(system);
            }
        }

        /// <summary>
        /// Measures an effect on its particles, played to a few moments of its life at scale 1. (A particle
        /// renderer's bounds will not do: they are padded for a billboard that may turn, and are those of the
        /// frame before, which had the pack's words measured three times too wide and its bursts too narrow.)
        /// The layers in <paramref name="loose"/> are left out of the whole; with no <paramref name="letters"/>
        /// there is no word to measure.
        /// </summary>
        private static Extent Measure(GameObject go, HashSet<ParticleSystem> letters, HashSet<ParticleSystem> loose)
        {
            var extent = new Extent();
            if (!go.TryGetComponent(out ParticleSystem root)) return extent;
            ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>(true);
            float longest = 0f;
            foreach (ParticleSystem system in systems)
                if (system.gameObject != go) longest = Mathf.Max(longest, system.main.startDelay.constantMax + system.main.startLifetime.constantMax);
            if (longest <= 0f) return extent;

            float left = float.MaxValue, right = float.MinValue, bottom = float.MaxValue, top = float.MinValue;
            float wordLeft = float.MaxValue, wordRight = float.MinValue;
            foreach (float share in MeasuredAt)
            {
                root.Simulate(longest * share, true, true);
                bool lettersAtRest = Mathf.Approximately(share, LettersMeasuredAt);
                foreach (ParticleSystem system in systems)
                {
                    if (system.gameObject == go || (loose != null && loose.Contains(system))) continue;
                    if (!system.TryGetComponent(out ParticleSystemRenderer renderer) || !renderer.enabled) continue;
                    bool letter = letters != null && letters.Contains(system);
                    if (letter && !lettersAtRest) continue;
                    ParticleSystem.MainModule main = system.main;
                    bool local = main.simulationSpace == ParticleSystemSimulationSpace.Local;
                    Vector3 scale = system.transform.lossyScale;
                    int count = system.GetParticles(measured);
                    for (int i = 0; i < count; i++)
                    {
                        Vector3 size = Vector3.Scale(measured[i].GetCurrentSize3D(system), scale);
                        Vector3 at = local ? system.transform.TransformPoint(measured[i].position) : measured[i].position;
                        // An effect is flat and faces the camera: across it is its own x, up it its own y.
                        at = go.transform.InverseTransformPoint(at);
                        float halfWidth = Mathf.Abs(size.x) * 0.5f, halfHeight = Mathf.Abs(size.y) * 0.5f;
                        left = Mathf.Min(left, at.x - halfWidth);
                        right = Mathf.Max(right, at.x + halfWidth);
                        bottom = Mathf.Min(bottom, at.y - halfHeight);
                        top = Mathf.Max(top, at.y + halfHeight);
                        if (!letter) continue;
                        wordLeft = Mathf.Min(wordLeft, at.x - halfWidth);
                        wordRight = Mathf.Max(wordRight, at.x + halfWidth);
                    }
                }
            }
            root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (right > left) extent.Width = right - left;
            if (top > bottom) extent.Height = top - bottom;
            if (wordRight > wordLeft) extent.WordWidth = wordRight - wordLeft;
            return extent;
        }

        /// <summary>A material of ours for one layer of a word, under a name that stays the same from build to build.</summary>
        private static Material OwnMaterial(string folder, MonoBehaviour component, Material source)
        {
            string path = $"{folder}/{LayerKey(component)}.mat";
            var own = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (own == null)
            {
                own = new Material(source);
                AssetDatabase.CreateAsset(own, path);
            }
            else
            {
                own.shader = source.shader;
                own.CopyPropertiesFromMaterial(source);
                EditorUtility.SetDirty(own);
            }
            return own;
        }

        private static void Give(MonoBehaviour component, Material material)
        {
            Set(component, "Material", material);
            if (component.TryGetComponent(out ParticleSystemRenderer renderer)) renderer.sharedMaterial = material;
        }

        /// <summary>"Text_0_2_1" (line, letter, layer), "Background_0", or a trail's "SmokeTrail_of_Debris_1".</summary>
        private static string LayerKey(MonoBehaviour component)
        {
            string type = component.GetType().Name.Replace("Component", string.Empty);
            if (type == "Text") return $"Text_{Get(component, "Line")}_{Get(component, "Letter")}_{Get(component, "Id")}";
            if (Has(component, "Id")) return $"{type}_{Get(component, "Id")}";
            // A trail belongs to the layer it hangs under.
            Transform parent = component.transform.parent;
            if (parent != null)
                foreach (MonoBehaviour owner in parent.GetComponents<MonoBehaviour>())
                    if (owner != null && owner != component && Has(owner, "Id")) return $"{type}_of_{LayerKey(owner)}";
            return type;
        }

        /// <summary>Materials left in a word's folder that the finished word does not use (it lost letters since it was last built).</summary>
        private static void RemoveUnused(string folder, GameObject word)
        {
            var used = new HashSet<string>();
            foreach (var renderer in word.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (renderer.sharedMaterial != null) used.Add(AssetDatabase.GetAssetPath(renderer.sharedMaterial));
                if (renderer.trailMaterial != null) used.Add(AssetDatabase.GetAssetPath(renderer.trailMaterial));
            }
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!used.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        // ---------------------------------------------------------------- from the pack's effect to a pooled one of ours

        /// <summary>
        /// The last steps for a mark and a word alike: note which layer takes which colour (while the pack's scripts
        /// are still there to say what each layer is), take the scripts off, size it, make it a pooled one-shot that
        /// lasts as long as its particles and no longer, and put the <see cref="ComicFx"/> on it.
        /// </summary>
        private static void Finish(GameObject go, float scale, ComicRamp? ramp)
        {
            ComicFx.Part[] parts = Parts(go, out ComicRamp builtIn);
            StripAuthoring(go);
            // Lettering is laid over the inked frame, not inked with it: its own layer, its own pass.
            int lettering = PostProcessSetup.EnsureComicLettering();
            if (lettering >= 0)
                foreach (Transform each in go.GetComponentsInChildren<Transform>(true)) each.gameObject.layer = lettering;
            // The pack's tool leaves the effect playing, and a system that plays refuses a new duration.
            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            OneShot(go, scale, null);

            float longest = 0f;
            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var collision = system.collision;
                collision.enabled = false; // debris bounced off the world: a physics query a particle, for nothing
                if (system.gameObject == go) continue;
                var main = system.main;
                // The pack leaves every system ten seconds long; all of them burst at once, so their own particles'
                // lives are all the time they need. The pool returns an effect when its systems are done.
                float seconds = main.startDelay.constantMax + main.startLifetime.constantMax;
                main.duration = Mathf.Max(0.05f, seconds);
                longest = Mathf.Max(longest, seconds);
            }
            if (go.TryGetComponent(out ParticleSystem root))
            {
                var main = root.main;
                main.duration = Mathf.Max(0.05f, longest);
                main.startLifetime = 0.05f;
                var emission = root.emission;
                emission.enabled = false; // the root only starts the others
                if (root.TryGetComponent(out ParticleSystemRenderer renderer)) renderer.enabled = false;
            }

            var comic = go.GetComponent<ComicFx>();
            if (comic == null) comic = go.AddComponent<ComicFx>();
            comic.Set(parts, ramp ?? builtIn);
            if (ramp.HasValue) comic.ApplyAuthored(); // a word with colours of its own is saved wearing them
        }

        /// <summary>
        /// Which colour of a ramp each layer takes, worked out from what the layer is and the colour the pack gave
        /// it: dark layers are ink, a word's letters are the fill and its outline the back colour, a burst behind a
        /// word is the back colour, white flashes are the core. Debris keeps its own colours.
        /// </summary>
        private static ComicFx.Part[] Parts(GameObject go, out ComicRamp builtIn)
        {
            MonoBehaviour tool = Tool(go);
            // A letter is a stack of layers, drawn in order: the last is its face, the ones under it back it.
            int face = 0;
            if (tool != null && Get(Get(tool, "text"), "billboards") is Array modules) face = modules.Length - 1;

            var parts = new List<ComicFx.Part>();
            var first = new Dictionary<ComicRole, Color>();
            int marks = 0, backs = 0, coloured = 0;
            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
                if (system.gameObject != go && LayerType(system) == "Impact") marks++;

            foreach (var system in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (system.gameObject == go) continue;
                string type = LayerType(system);
                var data = system.customData;
                if (type == null || !data.enabled || data.GetMode(ParticleSystemCustomData.Custom1) != ParticleSystemCustomDataMode.Color) continue;
                Color colour = Solid(data.GetColor(ParticleSystemCustomData.Custom1));
                Color.RGBToHSV(colour, out _, out float saturation, out float value);
                bool dark = value < 0.3f, white = saturation < 0.2f && value > 0.8f;
                ComicRole role;
                switch (type)
                {
                    case "Text":
                        MonoBehaviour letter = Layer(system);
                        role = dark ? ComicRole.Ink : (int)Get(letter, "Id") == face ? ComicRole.Fill : ComicRole.Back;
                        break;
                    case "Background":
                        // The first burst is the back colour; one laid over it is the bright one, so letters in the
                        // fill colour stand clear of both.
                        role = dark ? ComicRole.Ink : backs++ == 0 ? ComicRole.Back : ComicRole.Core;
                        break;
                    case "Halftone":
                        role = dark ? ComicRole.Ink : white ? ComicRole.Core : ComicRole.Back;
                        break;
                    case "Effects":
                    case "EffectTrail":
                        role = dark ? ComicRole.Ink : white ? ComicRole.Core : ComicRole.Fill;
                        break;
                    case "Impact":
                        // One layer: the mark itself. Three: two coloured fringes and a white middle.
                        role = marks == 1 ? ComicRole.Fill : white ? ComicRole.Core : coloured++ == 0 ? ComicRole.Back : ComicRole.Fill;
                        break;
                    case "Smoke":
                    case "SmokeTrail":
                        role = dark ? ComicRole.Ink : white ? ComicRole.Core : ComicRole.Back;
                        break;
                    default:
                        role = ComicRole.Keep; // debris: rocks stay rocks
                        break;
                }
                if (role == ComicRole.Keep) continue;
                if (!first.ContainsKey(role)) first[role] = colour;

                // A letter's edge is drawn by its material, in a colour of the pack's that no ramp would change.
                // Round the letter itself it becomes ink, whatever it was: letters in the fill colour then stand
                // clear of any burst. Round the layer that backs the letter, a coloured edge takes the back colour
                // and a white one stays white (a rim of light round the whole word).
                ComicRole edge = ComicRole.Keep;
                var renderer = system.GetComponent<ParticleSystemRenderer>();
                if (type == "Text" && renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_OutlineColor"))
                {
                    Color outlineColour = renderer.sharedMaterial.GetColor("_OutlineColor");
                    Color.RGBToHSV(outlineColour, out _, out float edgeSaturation, out float edgeValue);
                    bool edgeDark = edgeValue < 0.3f, edgeWhite = edgeSaturation < 0.2f && edgeValue > 0.8f;
                    if (outlineColour.a > 0.05f && !edgeDark)
                        edge = role == ComicRole.Fill ? ComicRole.Ink : role == ComicRole.Back && !edgeWhite ? ComicRole.Back : ComicRole.Keep;
                }
                parts.Add(new ComicFx.Part
                {
                    system = system, role = role, alpha = colour.a,
                    twoColours = data.GetMode(ParticleSystemCustomData.Custom2) == ParticleSystemCustomDataMode.Color,
                    outline = edge, renderer = edge != ComicRole.Keep ? renderer : null,
                });
            }
            builtIn = new ComicRamp(
                first.TryGetValue(ComicRole.Core, out Color core) ? Picked(core) : Color.white,
                first.TryGetValue(ComicRole.Fill, out Color fill) ? Picked(fill) : Hex("FFC93C"),
                first.TryGetValue(ComicRole.Back, out Color back) ? Picked(back) : Hex("F03A2E"),
                first.TryGetValue(ComicRole.Ink, out Color ink) ? Picked(ink) : Hex("14122B"));
            return parts.ToArray();
        }

        /// <summary>
        /// A colour read off one of the pack's layers, as a ramp holds colours: opaque, and as it would be picked
        /// (the layer holds what the shader is given, which in a linear project is the linear value).
        /// </summary>
        private static Color Picked(Color colour)
        {
            if (QualitySettings.activeColorSpace == ColorSpace.Linear) colour = colour.gamma;
            return new Color(colour.r, colour.g, colour.b, 1f);
        }

        private static Color Solid(ParticleSystem.MinMaxGradient colour)
        {
            switch (colour.mode)
            {
                case ParticleSystemGradientMode.Color: return colour.color;
                case ParticleSystemGradientMode.TwoColors: return colour.colorMax;
                case ParticleSystemGradientMode.Gradient: return colour.gradient != null ? colour.gradient.Evaluate(0.5f) : Color.white;
                case ParticleSystemGradientMode.TwoGradients: return colour.gradientMax != null ? colour.gradientMax.Evaluate(0.5f) : Color.white;
                default: return Color.white;
            }
        }

        // ---------------------------------------------------------------- the pack's tool, by reflection

        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static MonoBehaviour Tool(GameObject go)
        {
            foreach (MonoBehaviour behaviour in go.GetComponents<MonoBehaviour>())
                if (behaviour != null && behaviour.GetType().FullName == CartoonTool) return behaviour;
            return null;
        }

        /// <summary>The pack's script on a layer (its TextComponent, BackgroundComponent, ...), or null.</summary>
        private static MonoBehaviour Layer(Component on)
        {
            foreach (MonoBehaviour behaviour in on.GetComponents<MonoBehaviour>())
                if (behaviour != null && (behaviour.GetType().Namespace ?? string.Empty).StartsWith("CTFX2.Creator.Components", StringComparison.Ordinal))
                    return behaviour;
            return null;
        }

        private static string LayerType(Component on)
        {
            MonoBehaviour layer = Layer(on);
            return layer != null ? layer.GetType().Name.Replace("Component", string.Empty) : null;
        }

        private static IEnumerable<MonoBehaviour> Components(MonoBehaviour tool)
        {
            if (Call(tool, "GetComponents") is IEnumerable all)
                foreach (object each in all)
                    if (each is MonoBehaviour behaviour && behaviour != null) yield return behaviour;
        }

        private static List<MonoBehaviour> Missing(MonoBehaviour tool)
        {
            var missing = new List<MonoBehaviour>();
            if (Call(tool, "FindMissingMaterials") is IEnumerable all)
                foreach (object each in all)
                    if (each is MonoBehaviour behaviour && behaviour != null) missing.Add(behaviour);
            return missing;
        }

        private static bool Has(object target, string member) => Member(target.GetType(), member) != null;

        private static MemberInfo Member(Type type, string name)
        {
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                FieldInfo field = t.GetField(name, Members | BindingFlags.DeclaredOnly);
                if (field != null) return field;
                PropertyInfo property = t.GetProperty(name, Members | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        private static object Get(object target, string name)
        {
            if (target == null) return null;
            MemberInfo member = Member(target.GetType(), name);
            if (member is FieldInfo field) return field.GetValue(target);
            if (member is PropertyInfo property) return property.GetValue(target);
            throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static void Set(object target, string name, object value)
        {
            MemberInfo member = Member(target.GetType(), name);
            if (member is FieldInfo field) field.SetValue(target, value);
            else if (member is PropertyInfo property) property.SetValue(target, value);
            else throw new MissingMemberException(target.GetType().FullName, name);
        }

        private static object Call(object target, string name)
        {
            for (Type t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                MethodInfo method = t.GetMethod(name, Members | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (method != null) return method.Invoke(target, null);
            }
            throw new MissingMethodException(target.GetType().FullName, name);
        }

        // ---------------------------------------------------------------- the settings that hand the looks out

        /// <summary>
        /// Writes which mark and which words each weapon class has into the hit look settings. An asset that is
        /// already there keeps every number and colour on it: only the effects and the class list are set.
        /// </summary>
        public static HitLookSettings BuildHitLookSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<HitLookSettings>(HitLookSettingsPath);
            if (settings == null)
            {
                WeatherVfxBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(HitLookSettingsPath).Replace('\\', '/'));
                settings = ScriptableObject.CreateInstance<HitLookSettings>();
                AssetDatabase.CreateAsset(settings, HitLookSettingsPath);
            }

            var classes = new List<WeaponHitLook>();
            foreach (ClassComic comic in ClassComics)
            {
                string name = ClassName(comic.Class);
                classes.Add(new WeaponHitLook
                {
                    weaponClass = comic.Class,
                    lightImpact = Load(ComicPath($"PFX_Mark_{name}")),
                    heavyImpact = Load(ComicPath($"PFX_MarkHeavy_{name}")),
                    heavyWord = Load(ComicPath($"PFX_Word_{name}_Heavy")),
                    criticalWord = Load(ComicPath($"PFX_Word_{name}_Critical")),
                    skillWord = Load(ComicPath($"PFX_Word_{name}_Skill")),
                    swing = comic.Swing,
                });
            }
            settings.classes = classes.ToArray();
            settings.unarmed = new WeaponHitLook
            {
                weaponClass = WeaponClassId.None,
                lightImpact = Load(ComicPath("PFX_Mark_Unarmed")),
                heavyImpact = Load(ComicPath("PFX_MarkHeavy_Unarmed")),
                swing = new Color(1f, 1f, 1f, 0f), // no class: the blade trail keeps its own colours
            };
            settings.reflectedWord = Load(ComicPath("PFX_Word_Backlash"));
            settings.shatteredWord = Load(ComicPath("PFX_Word_Shatter"));

            GameObject lines = Load(ComicPath("PFX_Mark_PvpLines"));
            settings.pvpCrit = new[]
            {
                new PvpCritLook { style = PvpCritStyle.SplashPanel, panels = new[] { Load(ComicPath("PFX_PvpCrit_Splash")) }, ramp = SplashPanel, scale = 1f },
                new PvpCritLook
                {
                    style = PvpCritStyle.ImpactFrame, panels = new[] { Load(ComicPath("PFX_PvpCrit_Frame")) }, lines = lines,
                    linesColour = ImpactFrameLines, ramp = ImpactFrame, scale = 1f,
                },
                new PvpCritLook
                {
                    style = PvpCritStyle.FactionStrike, factionColours = true, scale = 1f,
                    panels = new[] { Load(ComicPath("PFX_PvpCrit_Hegemony")), Load(ComicPath("PFX_PvpCrit_Null")), Load(ComicPath("PFX_PvpCrit_Covenant")) },
                },
            };
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssetIfDirty(settings);
            return settings;
        }

        // ---------------------------------------------------------------- the players that show them

        private static readonly string[] ComicCameraPlayers = { "PvP Critical", "Level Up", "Pick Up" };
        private static readonly string[] ComicWorldPlayers = { "Hit", "Heavy Hit", "Critical", "Skill Hit", "Level Up", "Pick Up", "Pick Up Rare", "Pick Up Epic" };

        /// <summary>
        /// Puts the comic layer on the game feel prefab without regenerating the rest of it: the hit players lose the
        /// marks and words the hit looks now hand out, the skills' hit bursts lose theirs, the death, forge and pet
        /// moments get words of their own, and the new moments (a critical hit on a player, a level gained, a drop
        /// taken) get their players.
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Comic Feel")]
        public static void RebuildComicFeel()
        {
            HitLookSettings settings = BuildHitLookSettings();
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                GameObject root = scope.prefabContentsRoot;
                var presenter = root.GetComponent<GameFeelPresenter>();
                Transform world = root.transform.Find("World");
                if (presenter == null || world == null)
                {
                    Debug.LogError($"[GameFeel] {PrefabPath} is not a built game feel prefab. Run Build Game Feel first.");
                    return;
                }
                Remove(root.transform, ComicCameraPlayers);
                Remove(world, ComicWorldPlayers);
                Remove(root.transform, DeathCameraPlayers);
                Remove(world, DeathWorldPlayers);
                Remove(root.transform, ForgeCameraPlayers);
                Remove(world, ForgeWorldPlayers);
                Remove(root.transform, PetCameraPlayers);
                Remove(world, PetWorldPlayers);

                BuildComicVfx();
                var so = new SerializedObject(presenter);
                AddHitFeel(root, world.gameObject, so);
                AddDeathFeel(root, world.gameObject, so);
                AddForgeFeel(root, world.gameObject, so);
                AddPetFeel(root, world.gameObject, so);
                so.FindProperty("looks").objectReferenceValue = settings;
                so.ApplyModifiedPropertiesWithoutUndo();

                // The skills' own hit bursts, from the table (their marks and words are the hit looks' now).
                Transform skills = root.transform.Find("Skills");
                var catalog = root.GetComponent<SkillFeelCatalog>();
                if (skills != null && catalog != null)
                {
                    Object.DestroyImmediate(skills.gameObject);
                    BuildCatalog(root, catalog);
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Comic feel rebuilt on {PrefabPath}. Assign sounds on the new MMF_Sound slots.");
        }

        /// <summary>
        /// The hit players and the moments the comic layer added. The world hit players keep only what is not comic
        /// (sparks): the mark and the word of a hit come from <see cref="HitLookSettings"/> at run time.
        /// </summary>
        private static void AddHitFeel(GameObject root, GameObject world, SerializedObject so)
        {
            // ---- camera / screen / sound
            so.FindProperty("pvpCritFeel").objectReferenceValue = Player(root, "PvP Critical", p =>
            {
                Sound(p, "PvP Critical");
                Impulse(p, new Vector3(0.7f, 0.45f, 0f), 0.34f);
                Chromatic(p, "Chromatic Slam", 0.3f, 1f);
                var fov = p.AddFeedback(typeof(MMF_CameraFieldOfView)) as MMF_CameraFieldOfView;
                fov.Label = "FOV Punch";
                fov.Duration = 0.4f;
                fov.RelativeFieldOfView = true;
                fov.RemapFieldOfViewZero = 0f;
                fov.RemapFieldOfViewOne = 5f;
                fov.ShakeFieldOfView = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.12f, 1f), new Keyframe(1f, 0f));
                var lens = p.AddFeedback(typeof(MMF_LensDistortion_URP)) as MMF_LensDistortion_URP;
                lens.Label = "Lens Punch";
                lens.Duration = 0.26f;
                lens.RelativeIntensity = false;
                lens.RemapIntensityZero = 0f;
                lens.RemapIntensityOne = 0.24f;
                lens.Intensity = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f));
                // The flash: paper white in from the corners with comic dots, gone in a sixth of a second.
                Wash(p, "Impact Flash (Beautify)", 0.16f, 0.42f, new Color(0.96f, 0.93f, 0.87f, 0.34f), 0.6f);
            });
            so.FindProperty("levelUpFeel").objectReferenceValue = Player(root, "Level Up", p =>
            {
                Sound(p, "Level Up");
                Impulse(p, new Vector3(0f, 0.18f, 0f), 0.26f);
                Chromatic(p, "Chromatic Shimmer", 0.5f, 0.4f);
            });
            so.FindProperty("pickupFeel").objectReferenceValue = Player(root, "Pick Up", p => Sound(p, "Rare Find"));

            // ---- world particles: every client
            so.FindProperty("hitWorldFeel").objectReferenceValue = World(world, "Hit", Warm(new Fx("PFX_Hit_Sparks"), 16));
            so.FindProperty("heavyHitWorldFeel").objectReferenceValue = World(world, "Heavy Hit", Warm(new Fx("PFX_Hit_Heavy"), 8));
            so.FindProperty("critWorldFeel").objectReferenceValue = null; // a critical hit's word is its weapon class's
            so.FindProperty("skillHitWorldFeel").objectReferenceValue = World(world, "Skill Hit", Warm(new Fx("PFX_Hit_Sparks"), 16));
            so.FindProperty("levelUpWorldFeel").objectReferenceValue = World(world, "Level Up",
                AtFeet("PFX_LevelUp"), ComicAt("PFX_Mark_LevelUp", 0.2f, ramped: true), ComicAt("PFX_Word_LevelUp", 2.35f, ramped: true));
            so.FindProperty("pickupWorldFeel").objectReferenceValue = World(world, "Pick Up", ComicAt("PFX_Mark_Pickup", 0.25f));
            // Over the finder's head (the presenter plays these at their feet, and the puff at the drop).
            so.FindProperty("rarePickupWorldFeel").objectReferenceValue = World(world, "Pick Up Rare", ComicAt("PFX_Word_RareFind", 2.3f));
            so.FindProperty("epicPickupWorldFeel").objectReferenceValue = World(world, "Pick Up Epic", ComicAt("PFX_Word_EpicFind", 2.3f));
        }
    }
}
