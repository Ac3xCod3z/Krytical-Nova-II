using System;
using System.Collections;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items;
using KryticalNova.Progression;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// The character creator. The faction was chosen on the screen before; here the player picks a lineage and body
    /// type (Origin), shapes the body, chooses the head and its colours, and names the character. The body's outfit
    /// is not chosen: every new character wears its lineage's basic outfit and earns gear in the game.
    /// The live character on the right is the same doll, orbit and lighting as character select.
    /// Built on Synty's Fantasy Screens character creator; the scene builder prepares that screen and wires the
    /// pieces below.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterCreatorController : MonoBehaviour
    {
        private enum Tab
        {
            Origin,
            Discipline,
            Body,
            Head,
            Preview,
            Colour,
            Finalise
        }

        // Edits that arrive as a stream (a slider drag) are one undo step; everything else is one step per click.
        private const int DiscreteEdit = 0, BuildSlider = 1, MuscleSlider = 2, BustSlider = 3, ButtSlider = 4, DefinedSlider = 5, HeightSlider = 6;
        private const int MaxHistory = 40;

        [Tooltip("The character creator screen instance (Screen_FantasyScreens_CharacterCreator_01).")]
        [SerializeField] private Transform screen;
        [SerializeField] private CharacterCreationDatabase database;
        [SerializeField] private CharacterShowroom showroom;
        [SerializeField] private PortraitStudio portraits;
        [SerializeField] private MenuHotkeys hotkeys;

        [Header("Tabs, in order: Origin, Discipline, Body, Head, Preview, Colour, Finalise")]
        [SerializeField] private MenuRow[] tabButtons = Array.Empty<MenuRow>();
        [SerializeField] private GameObject originSection;
        [Tooltip("The list of option rows shared by the Body, Head and Preview tabs.")]
        [SerializeField] private GameObject optionsSection;
        [SerializeField] private GameObject colourSection;
        [SerializeField] private GameObject finaliseSection;

        [Header("Discipline: the faction's three weapon classes, shown on the Origin tiles")]
        [SerializeField] private WeaponClassCatalog classes;
        [Tooltip("Looks up the weapon a class starts with.")]
        [SerializeField] private GearDictionary gear;
        [SerializeField] private ItemCatalog items;

        [Header("Origin: one tile per lineage and body type")]
        [SerializeField] private MenuRow[] originTiles = Array.Empty<MenuRow>();
        [SerializeField] private RawImage[] originPortraits = Array.Empty<RawImage>();

        [Header("Body and Head")]
        [SerializeField] private MenuOptionRow[] optionRows = Array.Empty<MenuOptionRow>();
        [SerializeField] private MenuSliderRow[] sliderRows = Array.Empty<MenuSliderRow>();

        [Header("Colour")]
        [SerializeField] private MenuSwatchGroup[] swatchGroups = Array.Empty<MenuSwatchGroup>();
        [Tooltip("The right eye's own colour, shown under the eye colour while Heterochromia is on.")]
        [SerializeField] private MenuSwatchGroup rightEyeGroup;
        [Tooltip("The eyes' switches: Glowing Eyes, then Heterochromia.")]
        [SerializeField] private MenuOptionRow[] colourRows = Array.Empty<MenuOptionRow>();

        [Header("Finalise")]
        [SerializeField] private TMP_InputField nameInput;
        [SerializeField] private MenuOptionRow nameSuggestion;
        [SerializeField] private TMP_Text nameMessage;
        [SerializeField] private Button finaliseButton;
        [SerializeField] private Color nameOkColor = new Color(0.56f, 0.89f, 0.62f);
        [SerializeField] private Color nameProblemColor = new Color(1f, 0.79f, 0.24f);

        private struct Origin
        {
            public LineageDefinition lineage;
            public BodyType bodyType;
        }

        private readonly List<Origin> origins = new List<Origin>();
        private readonly List<WeaponClassCatalog.Entry> disciplines = new List<WeaponClassCatalog.Entry>();
        private FactionId factionId;
        private WeaponClassId discipline;
        private readonly List<CharacterRecipe> history = new List<CharacterRecipe>();
        private readonly List<Color32> skinSwatches = new List<Color32>();
        private readonly System.Random random = new System.Random();
        private CharacterRoster roster;
        private FactionDefinition faction;
        private CharacterRecipe recipe;
        private LineageDefinition lineage;
        private LineageDefinition.Setup setup;
        private Transform nextPrompt;
        private Tab tab;
        private int slot = -1;
        private int lastEditSource;
        // Preview tab: stances and emotes are browsed by group (one row picks the group, the next the motion in it).
        private int previewFace, stanceGroup, stanceInGroup, emoteGroup;
        // Choosing teeth or a tongue opens the mouth, so the choice can be seen (owner, 22 Oct 2026).
        private bool mouthShown;
        private int emoteInGroup = -1; // none played yet
        private readonly List<string> stanceGroups = new List<string>(), emoteGroups = new List<string>();
        private readonly List<List<int>> stanceMembers = new List<List<int>>(), emoteMembers = new List<List<int>>();
        private string suggestion;
        private string pendingName;
        private bool nameValid;
        private bool leaving;

        private void Start()
        {
            // Pressing Play in this scene (no faction chosen) builds a Hegemony character, for quick iteration.
            factionId = FrontEnd.PendingFaction != FactionId.None ? FrontEnd.PendingFaction : FactionId.Hegemony;
            discipline = WeaponClassRules.DefaultFor(factionId);
            if (classes != null) classes.GetClasses(factionId, disciplines);
            faction = database != null && database.Factions != null ? database.Factions.Get(factionId) : null;
            roster = CharacterSession.Store.Load();
            slot = FrontEnd.PendingSlot >= 0 && !roster.IsUsed(FrontEnd.PendingSlot) ? FrontEnd.PendingSlot : roster.FirstFree;

            var lineages = new List<LineageDefinition>();
            if (database != null) database.GetLineages(factionId, lineages);
            foreach (var l in lineages)
            {
                origins.Add(new Origin { lineage = l, bodyType = BodyType.Masculine });
                origins.Add(new Origin { lineage = l, bodyType = BodyType.Feminine });
            }
            if (origins.Count == 0 || slot < 0)
            {
                Debug.LogWarning(slot < 0 ? "[Menus] Every character slot is used; nothing to create." : $"[Menus] No lineages for {factionId}.", this);
                leaving = true;
                FrontEnd.Go(FrontEnd.CharacterSelectScene);
                return;
            }

            BindTabs();
            BindOrigin();
            BindFinalise();
            BindPrompts();
            Apply(RecipeEdit.Default(origins[0].lineage, origins[0].bodyType));
            ShowTab(Tab.Origin);
            RenderPortraits();
        }

        // ---------------- Tabs ----------------

        private void BindTabs()
        {
            for (int i = 0; i < tabButtons.Length; i++)
            {
                if (tabButtons[i] == null) continue;
                var target = TabOfButton(i);
                tabButtons[i].Clicked += () => ShowTab(target);
            }

            // The Discipline tab is the pack's spare button, which sits further along the bar. Children of the screen
            // prefab cannot be reordered in the saved scene, so it is moved next to Origin here.
            if (tabButtons.Length > (int)Tab.Finalise && tabButtons[(int)Tab.Origin] != null && tabButtons[(int)Tab.Discipline] != null)
                tabButtons[(int)Tab.Discipline].transform.SetSiblingIndex(tabButtons[(int)Tab.Origin].transform.GetSiblingIndex() + 1);
        }

        /// <summary>
        /// One button per tab, in tab order. A screen built before the Discipline tab existed has one button fewer:
        /// its buttons skip that tab, which stays reachable with Next / Back.
        /// </summary>
        private Tab TabOfButton(int index)
        {
            bool hasDisciplineButton = tabButtons.Length > (int)Tab.Finalise;
            return hasDisciplineButton || index == 0 ? (Tab)index : (Tab)(index + 1);
        }

        private void ShowTab(Tab next)
        {
            // The preview is not saved with the character: leaving the tab puts the face and stance back.
            if (tab == Tab.Preview && next != Tab.Preview) ResetPreview();
            if (tab == Tab.Head && next != Tab.Head) ShowMouth(false);
            tab = next;
            // The Body tab shows the body bare or clothed, as the player switches it (owner, 24 Oct 2026: a toggle, so they
            // see themselves either way); every other tab shows the outfit. The ruler is the Body tab's.
            if (showroom != null)
            {
                bool bare = next == Tab.Body && bodyBare;
                if (!bare) showroom.HideHeightRuler();
                if (showroom.Bare != bare)
                {
                    showroom.Bare = bare;
                    Preview();
                }
            }
            for (int i = 0; i < tabButtons.Length; i++)
                if (tabButtons[i] != null) tabButtons[i].SetSelected(TabOfButton(i) == next);
            SetActive(originSection, next == Tab.Origin || next == Tab.Discipline);
            SetActive(optionsSection, next == Tab.Body || next == Tab.Head || next == Tab.Preview);
            SetActive(colourSection, next == Tab.Colour);
            SetActive(finaliseSection, next == Tab.Finalise);
            RefreshTab();
            if (showroom != null) showroom.Focus(next == Tab.Head ? 0.8f : next == Tab.Colour ? 0.45f : next == Tab.Preview ? 0.3f : 0f);
            if (nextPrompt != null) MenuUi.SetText(nextPrompt, "Label_Action", next == Tab.Finalise ? "Create Character" : "Next");
            if (next == Tab.Finalise && nameInput != null)
            {
                nameInput.Select();
                nameInput.ActivateInputField();
            }
        }

        /// <summary>Rebuild the visible tab from the recipe (after any change, undo or randomise).</summary>
        private void RefreshTab()
        {
            switch (tab)
            {
                case Tab.Origin: RefreshOrigin(); break;
                case Tab.Discipline: RefreshDiscipline(); break;
                case Tab.Body: ShowBody(); break;
                case Tab.Head: ShowHead(); break;
                case Tab.Preview: ShowPreview(); break;
                case Tab.Colour: ShowColours(); break;
                case Tab.Finalise: RefreshName(); break;
            }
        }

        private void StepTab(int direction)
        {
            int next = Mathf.Clamp((int)tab + direction, 0, (int)Tab.Finalise);
            if (next == (int)tab) return;
            ShowTab((Tab)next);
            MenuMoments.Raise(MenuMoment.Tab);
        }

        // ---------------- Origin ----------------

        private void BindOrigin()
        {
            for (int i = 0; i < originTiles.Length; i++)
            {
                var tile = originTiles[i];
                if (tile == null) continue;
                int index = i;
                // The same tiles are the lineage choices on the Origin tab and the class cards on the Discipline tab.
                tile.Clicked += () =>
                {
                    if (tab == Tab.Discipline) ChooseDiscipline(index);
                    else if (index < origins.Count) ChooseOrigin(index);
                };
            }
        }

        private void ChooseOrigin(int index)
        {
            var origin = origins[index];
            if (origin.lineage == lineage && origin.bodyType == recipe.bodyType) return;
            Record(DiscreteEdit);
            var next = RecipeEdit.Default(origin.lineage, origin.bodyType);
            next.CopyBodyFrom(recipe); // the body shape carries over to the new origin (the sliders its body type has)
            Apply(next);
        }

        private void RefreshOrigin()
        {
            int current = origins.FindIndex(o => o.lineage == lineage && o.bodyType == recipe.bodyType);
            for (int i = 0; i < originTiles.Length; i++)
            {
                if (originTiles[i] == null) continue;
                originTiles[i].gameObject.SetActive(i < origins.Count);
                originTiles[i].SetSelected(i == current);
                SetTilePicture(i, null);
            }
            if (originSection == null || lineage == null) return;
            MenuUi.SetText(originSection.transform, "Label_Header", faction != null ? $"{faction.displayName} Origins" : "Origin");

            var info = MenuUi.Find(originSection.transform, "Description");
            MenuUi.SetText(info, "Label_Header", $"{lineage.displayName}, {(recipe.bodyType == BodyType.Feminine ? "Feminine" : "Masculine")}");
            MenuUi.SetText(info, "Label_Description", lineage.description);
            for (int i = 0; i < 3; i++)
            {
                var chip = MenuUi.Find(originSection.transform, $"Trait_0{i + 1}");
                if (chip == null) continue;
                bool used = lineage.traits != null && i < lineage.traits.Length;
                chip.gameObject.SetActive(used);
                if (used) MenuUi.SetText(chip, "Label_Stat", lineage.traits[i]);
            }
        }

        // ---------------- Discipline ----------------

        private void ChooseDiscipline(int index)
        {
            if (index < 0 || index >= disciplines.Count || disciplines[index].id == discipline) return;
            Record(DiscreteEdit);
            discipline = disciplines[index].id;
            recipe.weaponClass = discipline;
            RefreshDiscipline();
        }

        /// <summary>The Origin tiles as class cards: the weapon each class starts with, and what the class is.</summary>
        private void RefreshDiscipline()
        {
            int current = disciplines.FindIndex(d => d.id == discipline);
            for (int i = 0; i < originTiles.Length; i++)
            {
                if (originTiles[i] == null) continue;
                bool used = i < disciplines.Count;
                originTiles[i].gameObject.SetActive(used);
                originTiles[i].SetSelected(i == current);
                SetTilePicture(i, used ? StarterWeapon(disciplines[i].id) : null);
            }
            if (originSection == null) return;
            MenuUi.SetText(originSection.transform, "Label_Header", "Choose a Discipline");
            var info = MenuUi.Find(originSection.transform, "Description");
            if (current < 0)
            {
                MenuUi.SetText(info, "Label_Header", WeaponClasses.DisplayName(discipline));
                MenuUi.SetText(info, "Label_Description", "The weapon you start with. You can take up your faction's other weapons at any time.");
                return;
            }

            var entry = disciplines[current];
            var weapon = StarterWeapon(entry.id);
            MenuUi.SetText(info, "Label_Header", $"{entry.displayName}, {WeaponClasses.RoleName(entry.role)}");
            MenuUi.SetText(info, "Label_Description",
                $"{entry.summary}\nYou can take up your faction's other weapons at any time."); // two short lines: all the panel has room for
            string[] chips = { WeaponClasses.RoleName(entry.role), entry.stanceSummary, weapon != null ? $"Starts with: {weapon.displayName}" : null };
            for (int i = 0; i < 3; i++)
            {
                var chip = MenuUi.Find(originSection.transform, $"Trait_0{i + 1}");
                if (chip == null) continue;
                bool used = !string.IsNullOrEmpty(chips[i]);
                chip.gameObject.SetActive(used);
                if (used) MenuUi.SetText(chip, "Label_Stat", chips[i]);
            }
        }

        /// <summary>The level-1 weapon a class starts with, or null when the gear ladder has none for it.</summary>
        private ItemDefinition StarterWeapon(WeaponClassId weaponClass)
        {
            if (gear == null || items == null) return null;
            return gear.TryFindStarter(EquipSlot.Weapon, factionId, BodyFit.None, null, weaponClass, out var entry) ? items.Get(entry.itemId) : null;
        }

        /// <summary>A tile shows a lineage portrait (Origin) or, given an item, that item's icon (Discipline).</summary>
        private void SetTilePicture(int index, ItemDefinition item)
        {
            if (index < originPortraits.Length && originPortraits[index] != null)
                originPortraits[index].gameObject.SetActive(item == null);
            var icon = originTiles[index] != null ? MenuUi.Find<Image>(originTiles[index].transform, "ICON") : null;
            if (icon == null) return;
            icon.gameObject.SetActive(item != null && item.icon != null);
            if (item != null && item.icon != null)
            {
                icon.sprite = item.icon;
                icon.preserveAspect = true;
                icon.color = Color.white;
            }
        }

        private void RenderPortraits()
        {
            if (portraits == null) return;
            var looks = new List<CharacterRecipe>(origins.Count);
            foreach (var origin in origins) looks.Add(RecipeEdit.Default(origin.lineage, origin.bodyType));
            Color accent = faction != null ? faction.accentColor : Color.gray;
            Color.RGBToHSV(accent, out float h, out float s, out _);
            portraits.Render(looks, originPortraits, Color.HSVToRGB(h, Mathf.Min(s, 0.6f), 0.22f));
        }

        // ---------------- Body ----------------

        private void ShowBody()
        {
            SetListTitle("Body");
            int row = 0;
            if (setup.builds.Count > 0 && row < optionRows.Length) optionRows[row++].Show("Build", BuildName, StepBuild);
            // Bare or clothed while the proportions are set (owner, 24 Oct 2026); starts bare, the choice is kept.
            if (row < optionRows.Length) optionRows[row++].Show("Outfit", () => bodyBare ? "Bare" : "Clothed", _ =>
            {
                bodyBare = !bodyBare;
                if (showroom != null && tab == Tab.Body)
                {
                    showroom.Bare = bodyBare;
                    Preview();
                }
                RefreshOptionRows();
            });
            HideOptionRows(row);
            if (sliderRows.Length > 0)
                sliderRows[0].Show("Body Size", "Slim", "Heavy", -100f, 100f, recipe.build, v => ShapeChanged(BuildSlider, (sbyte)v, recipe.muscle));
            if (sliderRows.Length > 1)
                sliderRows[1].Show("Musculature", "Lean", "Muscular", -100f, 100f, recipe.muscle, v => ShapeChanged(MuscleSlider, recipe.build, (sbyte)v));
            // The body sliders (22 Oct 2026): bust and butt on a feminine body, definition on a masculine one, height on every one.
            // A lineage whose bare body has no such key (robot, skeleton) gets height only.
            int slider = 2;
            if (recipe.bodyType == BodyType.Feminine)
            {
                if (BodyHas("kn_bust")) ShowSlider(slider++, "Bust", "Natural", "Full", recipe.bust, BustSlider, v => recipe.bust = v);
                if (BodyHas("kn_butt")) ShowSlider(slider++, "Butt", "Natural", "Full", recipe.butt, ButtSlider, v => recipe.butt = v);
            }
            else if (BodyHas("kn_defined"))
                ShowSlider(slider++, "Definition", "Smooth", "Defined", recipe.defined, DefinedSlider, v => recipe.defined = v);
            // While the height changes, a ruler with a tree for scale stands beside the character (owner, 24 Oct 2026).
            ShowSlider(slider++, "Height", "Short", "Tall", recipe.Height, HeightSlider, v =>
            {
                recipe.height = v;
                if (showroom != null) showroom.ShowHeightRuler();
            });
            for (int i = slider; i < sliderRows.Length; i++) sliderRows[i].Hide();
        }

        // The Body tab's Outfit switch: the body shown bare (true) or in the basic outfit.
        private bool bodyBare = true;

        private readonly Dictionary<(byte, string), bool> bodyKeys = new Dictionary<(byte, string), bool>();

        private bool BodyHas(string key)
        {
            if (lineage == null) return false;
            if (!bodyKeys.TryGetValue((lineage.id, key), out bool has))
                bodyKeys[(lineage.id, key)] = has = RecipeResolver.BodyHasKey(lineage, database, key);
            return has;
        }

        private void ShowSlider(int index, string label, string low, string high, byte value, int source, Action<byte> set)
        {
            if (index >= sliderRows.Length || sliderRows[index] == null) return;
            sliderRows[index].Show(label, low, high, 0f, 100f, value, v =>
            {
                Record(source);
                set((byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 100));
                Preview();
            });
        }

        private int BuildIndex()
        {
            for (int i = 0; i < setup.builds.Count; i++)
                if (setup.builds[i].build == recipe.build && setup.builds[i].muscle == recipe.muscle) return i;
            return -1;
        }

        private string BuildName()
        {
            int index = BuildIndex();
            return index >= 0 ? setup.builds[index].name : "Custom";
        }

        private void StepBuild(int direction)
        {
            int count = setup.builds.Count;
            int index = BuildIndex();
            // From a custom shape the arrows go to the first or last preset.
            index = index < 0 ? (direction > 0 ? 0 : count - 1) : ((index + direction) % count + count) % count;
            Record(DiscreteEdit);
            recipe.build = setup.builds[index].build;
            recipe.muscle = setup.builds[index].muscle;
            if (sliderRows.Length > 0) sliderRows[0].SetValue(recipe.build);
            if (sliderRows.Length > 1) sliderRows[1].SetValue(recipe.muscle);
            Preview();
        }

        private void ShapeChanged(int source, sbyte build, sbyte muscle)
        {
            Record(source);
            recipe.build = build;
            recipe.muscle = muscle;
            Preview();
            RefreshOptionRows(); // the Build row may now read "Custom"
        }

        // ---------------- Head ----------------

        private void ShowHead()
        {
            SetListTitle("Head");
            int row = 0;
            if (setup.presets.Count > 1 && row < optionRows.Length) optionRows[row++].Show("Look", LookName, StepLook);
            foreach (var options in setup.regions)
            {
                if (row >= optionRows.Length) break;
                if (!RecipeEdit.HasChoice(options)) continue;
                var region = options;
                optionRows[row++].Show(RegionName(region.region), () => OptionName(region), direction => StepRegion(region, direction));
            }
            HideOptionRows(row);
            foreach (var slider in sliderRows) slider.Hide();
        }

        private string LookName()
        {
            int index = RecipeEdit.MatchingPreset(setup, recipe);
            return index >= 0 ? $"{index + 1} / {setup.presets.Count}" : "Custom";
        }

        private void StepLook(int direction)
        {
            int count = setup.presets.Count;
            int index = RecipeEdit.MatchingPreset(setup, recipe);
            index = index < 0 ? (direction > 0 ? 0 : count - 1) : ((index + direction) % count + count) % count;
            Record(DiscreteEdit);
            recipe = RecipeEdit.FromPreset(lineage, recipe.bodyType, index, recipe);
            Preview();
            RefreshOptionRows();
        }

        private string OptionName(LineageDefinition.RegionOptions options)
        {
            int index = RecipeEdit.IndexOf(recipe, database.Parts, options);
            return index < 0 ? "None" : $"{index + 1} / {options.partIds.Length}";
        }

        private void StepRegion(LineageDefinition.RegionOptions options, int direction)
        {
            Record(DiscreteEdit);
            RecipeEdit.Step(ref recipe, database.Parts, setup, options, direction);
            Preview();
            ShowMouth(options.region == BodyRegion.Teeth || options.region == BodyRegion.Tongue);
            RefreshOptionRows(); // the Look row may now read "Custom"
        }

        // The face that bares the teeth while they are chosen (a wide smile, the jaw a little open), and the neutral one after.
        private void ShowMouth(bool open)
        {
            if (open == mouthShown || showroom == null) return;
            mouthShown = open;
            showroom.SetFace(open ? MouthFace() : 0);
        }

        private int MouthFace()
        {
            var library = showroom.Expressions;
            if (library == null) return 0;
            foreach (string wanted in new[] { "Delighted", "Happy", "Surprised" })
                for (int i = 0; i < library.Faces.Count; i++)
                    if (library.Faces[i].name == wanted) return i;
            return 0;
        }

        private static string RegionName(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return "Head";
                case BodyRegion.Hair: return "Hair";
                case BodyRegion.EyebrowLeft: return "Eyebrows";
                case BodyRegion.EyeLeft:
                case BodyRegion.EyeRight: return "Eyes";
                case BodyRegion.EarLeft: return "Ears";
                case BodyRegion.FacialHair: return "Facial Hair";
                case BodyRegion.Nose: return "Nose";
                case BodyRegion.Teeth: return "Teeth";
                case BodyRegion.Tongue: return "Tongue";
                default: return region.ToString();
            }
        }

        // ---------------- Preview (faces, idles, emotes) ----------------

        // Nothing here is saved with the character: it shows the player how their character looks alive.
        private void ShowPreview()
        {
            SetListTitle("Preview");
            var library = showroom != null ? showroom.Expressions : null;
            int row = 0;
            if (library != null && library.Faces.Count > 0 && row < optionRows.Length)
                optionRows[row++].Show("Expression", () => library.Faces[previewFace].name, direction =>
                {
                    previewFace = Wrap(previewFace + direction, library.Faces.Count);
                    showroom.SetFace(previewFace);
                });
            if (library != null && library.Idles.Count > 0 && row + 1 < optionRows.Length)
            {
                CharacterExpressionLibrary.Group(library.Idles, recipe.bodyType, stanceGroups, stanceMembers);
                stanceGroup = Mathf.Clamp(stanceGroup, 0, stanceGroups.Count - 1);
                stanceInGroup = Mathf.Clamp(stanceInGroup, 0, stanceMembers[stanceGroup].Count - 1);
                optionRows[row++].Show("Stance Group", () => Counted(stanceGroups[stanceGroup], stanceGroup, stanceGroups.Count), direction =>
                {
                    stanceGroup = Wrap(stanceGroup + direction, stanceGroups.Count);
                    stanceInGroup = 0;
                    showroom.PlayIdle(stanceMembers[stanceGroup][0]);
                    RefreshOptionRows();
                });
                optionRows[row++].Show("Stance", () => Counted(library.Idles[stanceMembers[stanceGroup][stanceInGroup]].name,
                    stanceInGroup, stanceMembers[stanceGroup].Count), direction =>
                {
                    stanceInGroup = Wrap(stanceInGroup + direction, stanceMembers[stanceGroup].Count);
                    showroom.PlayIdle(stanceMembers[stanceGroup][stanceInGroup]);
                });
            }
            if (library != null && library.Emotes.Count > 0 && row + 1 < optionRows.Length)
            {
                CharacterExpressionLibrary.Group(library.Emotes, recipe.bodyType, emoteGroups, emoteMembers);
                emoteGroup = Mathf.Clamp(emoteGroup, 0, emoteGroups.Count - 1);
                emoteInGroup = Mathf.Min(emoteInGroup, emoteMembers[emoteGroup].Count - 1);
                optionRows[row++].Show("Emote Group", () => Counted(emoteGroups[emoteGroup], emoteGroup, emoteGroups.Count), direction =>
                {
                    emoteGroup = Wrap(emoteGroup + direction, emoteGroups.Count);
                    emoteInGroup = 0;
                    showroom.PlayEmote(emoteMembers[emoteGroup][0]);
                    RefreshOptionRows();
                });
                optionRows[row++].Show("Emote", () => emoteInGroup >= 0
                    ? Counted(library.Emotes[emoteMembers[emoteGroup][emoteInGroup]].name, emoteInGroup, emoteMembers[emoteGroup].Count)
                    : "Use the arrows", direction =>
                {
                    int count = emoteMembers[emoteGroup].Count;
                    // Nothing played yet: right starts at the first, left at the last.
                    emoteInGroup = emoteInGroup < 0 ? (direction > 0 ? 0 : count - 1) : Wrap(emoteInGroup + direction, count);
                    showroom.PlayEmote(emoteMembers[emoteGroup][emoteInGroup]);
                });
            }
            HideOptionRows(row);
            foreach (var slider in sliderRows) slider.Hide();
        }

        private void ResetPreview()
        {
            previewFace = 0;
            stanceGroup = stanceInGroup = 0;
            emoteInGroup = -1;
            if (showroom != null) showroom.ResetPreview();
        }

        private static string Counted(string name, int index, int count) => $"{name}  ({index + 1}/{count})";

        private static int Wrap(int index, int count) => ((index % count) + count) % count;

        // ---------------- Colour ----------------

        private void ShowColours()
        {
            int group = 0;
            if (setup.skinTones.Count > 0 && group < swatchGroups.Length)
            {
                skinSwatches.Clear();
                foreach (var tone in setup.skinTones) skinSwatches.Add(tone.swatch);
                swatchGroups[group++].Show("Skin", skinSwatches, recipe.skinTone - 1, PickSkin);
            }
            bool twoEyes = setup.OffersChannel(CharacterColorChannels.Eyes) && RecipeEdit.HasHeterochromia(recipe);
            foreach (byte id in setup.channels)
            {
                if (group >= swatchGroups.Length) break;
                if (!database.TryGetChannel(id, out var channel) || channel.swatches == null) continue;
                var picked = channel;
                string label = twoEyes && id == CharacterColorChannels.Eyes ? "Left Eye" : channel.displayName;
                swatchGroups[group++].Show(label, channel.swatches, RecipeEdit.SwatchIndex(recipe, channel), index => PickColor(picked, index));
            }
            for (; group < swatchGroups.Length; group++) swatchGroups[group].Hide();
            ShowEyeOptions(twoEyes);
        }

        /// <summary>
        /// Glowing Eyes for every lineage; Heterochromia, with the right eye's own swatches, wherever eye colour is
        /// offered (owner, 24 Oct 2026). Both are On/Off arrow rows, as the Body tab's Outfit row is.
        /// </summary>
        private void ShowEyeOptions(bool twoEyes)
        {
            if (rightEyeGroup != null)
            {
                if (twoEyes && database.TryGetChannel(CharacterColorChannels.EyeRight, out var right) && right.swatches != null)
                    rightEyeGroup.Show(right.displayName, right.swatches, RecipeEdit.SwatchIndex(recipe, right), index => PickColor(right, index));
                else rightEyeGroup.Hide();
            }
            int row = 0;
            if (row < colourRows.Length)
                colourRows[row++].Show("Glowing Eyes", () => recipe.eyeGlow ? "On" : "Off", _ =>
                {
                    Record(DiscreteEdit);
                    recipe.eyeGlow = !recipe.eyeGlow;
                    Preview();
                    ShowColours();
                });
            if (row < colourRows.Length && setup.OffersChannel(CharacterColorChannels.Eyes))
                colourRows[row++].Show("Heterochromia", () => RecipeEdit.HasHeterochromia(recipe) ? "On" : "Off", _ =>
                {
                    Record(DiscreteEdit);
                    Color32 fallback = database.TryGetChannel(CharacterColorChannels.Eyes, out var eyes) && eyes.swatches != null
                        && eyes.swatches.Length > 0 ? eyes.swatches[0] : new Color32(107, 74, 39, 255);
                    RecipeEdit.SetHeterochromia(ref recipe, !RecipeEdit.HasHeterochromia(recipe), fallback);
                    Preview();
                    ShowColours();
                });
            for (; row < colourRows.Length; row++) if (colourRows[row] != null) colourRows[row].Hide();
        }

        private void PickSkin(int index)
        {
            Record(DiscreteEdit);
            recipe.skinTone = (byte)(index + 1);
            Preview();
        }

        private void PickColor(CharacterCreationDatabase.ColorChannel channel, int index)
        {
            if (index < 0 || index >= channel.swatches.Length) return;
            Record(DiscreteEdit);
            RecipeEdit.SetColor(ref recipe, channel.id, channel.swatches[index]);
            Preview();
        }

        // ---------------- Finalise ----------------

        private void BindFinalise()
        {
            if (finaliseSection != null) MenuUi.SetText(finaliseSection.transform, "Label_Header", "Name Your Character");
            if (nameInput != null)
            {
                nameInput.characterLimit = CharacterNameRules.MaxLength;
                nameInput.richText = false;
                nameInput.text = string.Empty;
                if (nameInput.placeholder is TMP_Text placeholder) placeholder.text = "Type a name";
                nameInput.onValueChanged.AddListener(_ => RefreshName());
                // The vendor field ships with raycasts off, so it cannot be clicked into.
                if (nameInput.TryGetComponent<Graphic>(out var hit)) hit.raycastTarget = true;
            }
            if (nameSuggestion != null) nameSuggestion.Show("Suggest a Name", () => suggestion ?? "Use the arrows", _ => Suggest());
            if (finaliseButton != null)
            {
                MenuUi.SetAllTexts(finaliseButton.transform, "Create Character");
                finaliseButton.onClick.AddListener(Finalise);
            }
        }

        private void Suggest()
        {
            // The generator can land on a name this player already uses; a few tries are plenty.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string candidate = CharacterNameGenerator.Generate(recipe.lineage, recipe.bodyType, random);
                if (!CharacterNameRules.Validate(candidate, out string normalized, out _) || roster.IsNameTaken(normalized)) continue;
                suggestion = normalized;
                break;
            }
            if (nameInput != null && suggestion != null) nameInput.text = suggestion;
            if (nameSuggestion != null) nameSuggestion.Refresh();
        }

        private void RefreshName()
        {
            string raw = nameInput != null ? nameInput.text : string.Empty;
            nameValid = CharacterNameRules.Validate(raw, out pendingName, out string reason);
            if (nameValid && roster.IsNameTaken(pendingName))
            {
                nameValid = false;
                reason = CharacterNameRules.ReasonTaken;
            }
            if (nameMessage != null)
            {
                string home = faction != null ? faction.displayName : "the world";
                nameMessage.text = nameValid
                    ? $"{pendingName}, {lineage.displayName} of the {home}. Ready."
                    : reason;
                nameMessage.color = nameValid ? nameOkColor : nameProblemColor;
            }
            if (finaliseButton != null) finaliseButton.interactable = nameValid;
        }

        private void Finalise()
        {
            if (leaving) return;
            RefreshName();
            if (!nameValid)
            {
                // The line under the name says why; it is what shakes.
                MenuMoments.Raise(MenuMoment.Error, nameMessage != null ? nameMessage.transform : null);
                return;
            }
            if (!CharacterRecipeValidator.Validate(recipe, database, out string problem))
            {
                Debug.LogError($"[Menus] The creator built a look the rules reject: {problem}", this);
                if (nameMessage != null) nameMessage.text = problem;
                MenuMoments.Raise(MenuMoment.Error, nameMessage != null ? nameMessage.transform : null);
                return;
            }

            MenuMoments.Raise(MenuMoment.Confirm);
            // On a server the server makes the character (online phase 4, milestone 5): it checks the name against every
            // character in the game and the look, and says why not.
            if (MenuRoster.OnServer)
            {
                StartCoroutine(CreateOnServer());
                return;
            }
            roster.Set(slot, CharacterProfile.Create(pendingName, recipe));
            CharacterSession.Store.Save(roster);
            Done(slot);
        }

        private IEnumerator CreateOnServer()
        {
            leaving = true; // no second press while the server decides
            if (finaliseButton != null) finaliseButton.interactable = false;
            if (nameMessage != null)
            {
                nameMessage.text = $"Asking {MenuRoster.ServerName} to make {pendingName}...";
                nameMessage.color = nameOkColor;
            }
            RosterReply answer = null;
            string problem = null;
            yield return MenuRoster.Create(pendingName, recipe, slot, (reply, why) => { answer = reply; problem = why; });
            if (problem != null || answer == null)
            {
                leaving = false;
                if (nameMessage != null)
                {
                    nameMessage.text = problem ?? "The server did not make the character. Try again.";
                    nameMessage.color = nameProblemColor;
                }
                if (finaliseButton != null) finaliseButton.interactable = true;
                MenuMoments.Raise(MenuMoment.Error, nameMessage != null ? nameMessage.transform : null);
                yield break;
            }
            int made = slot;
            foreach (RosterCharacter character in answer.characters)
                if (character != null && character.id == answer.characterId) made = character.slot;
            Done(made);
        }

        private void Done(int madeSlot)
        {
            leaving = true;
            FrontEnd.HighlightSlot = madeSlot;
            FrontEnd.PendingSlot = -1;
            FrontEnd.PendingFaction = FactionId.None;
            FrontEnd.Go(FrontEnd.CharacterSelectScene);
        }

        // ---------------- Prompts and shortcuts ----------------

        private void BindPrompts()
        {
            var turn = MenuUi.Require(screen, "Prompt_Turn", this);
            if (turn != null)
            {
                MenuUi.SetText(turn, "Label_Action", "Turn   Scroll: Zoom");
                if (hotkeys != null) hotkeys.ShowMouse(turn, KeyGlyphSet.MouseGlyph.LeftButton);
            }
            Prompt("Prompt_Randomise", Key.R, "Randomise", Randomise);
            Prompt("Prompt_Undo", Key.Z, "Undo", Undo);
            Prompt("Prompt_Back", Key.Escape, "Back", Back);
            nextPrompt = Prompt("Prompt_Next", Key.Enter, "Next", Next);

            if (hotkeys == null) return;
            hotkeys.Bind(Key.NumpadEnter, Next);
            hotkeys.Bind(Key.Q, () => StepTab(-1));
            hotkeys.Bind(Key.E, () => StepTab(1));
            MakeTabKey("Input_Key_Left", -1);
            MakeTabKey("Input_Key_Right", 1);
        }

        private Transform Prompt(string promptName, Key key, string label, Action action)
        {
            var prompt = MenuUi.Require(screen, promptName, this);
            if (prompt == null) return null;
            MenuUi.SetText(prompt, "Label_Action", label);
            MenuUi.MakeClickable(prompt.gameObject, () => action());
            if (hotkeys != null) hotkeys.Bind(key, action, prompt);
            return prompt;
        }

        // The Q and E keycaps either side of the tabs are also buttons.
        private void MakeTabKey(string keyName, int direction)
        {
            var key = MenuUi.Find(screen, keyName);
            if (key != null) MenuUi.MakeClickable(key.gameObject, () => StepTab(direction));
        }

        private void Next()
        {
            if (tab == Tab.Finalise) Finalise();
            else StepTab(1);
        }

        private void Back()
        {
            if (leaving) return;
            if (tab != Tab.Origin)
            {
                StepTab(-1);
                return;
            }
            leaving = true;
            MenuMoments.Raise(MenuMoment.Back);
            FrontEnd.Go(FrontEnd.FactionSelectScene); // the slot being filled stays pending
        }

        private void Randomise()
        {
            if (lineage == null) return;
            Record(DiscreteEdit);
            Apply(RecipeEdit.Randomize(lineage, recipe.bodyType, database, random));
        }

        private void Undo()
        {
            if (history.Count == 0) return;
            var previous = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            lastEditSource = DiscreteEdit;
            Apply(previous);
        }

        // ---------------- Shared ----------------

        /// <summary>Remember the look before an edit. A stream of edits from one slider counts once.</summary>
        private void Record(int source)
        {
            if (source != DiscreteEdit && source == lastEditSource && history.Count > 0) return;
            lastEditSource = source;
            history.Add(recipe.Clone());
            if (history.Count > MaxHistory) history.RemoveAt(0);
        }

        /// <summary>Replace the whole look (origin change, undo, randomise) and rebuild what is on screen.</summary>
        private void Apply(in CharacterRecipe next)
        {
            recipe = next;
            recipe.Upgrade(); // the sliders edit a look of the current version
            // A look built from scratch (new origin, randomise) carries no Discipline; one from the undo history does.
            if (WeaponClassRules.BelongsTo(recipe.weaponClass, factionId)) discipline = recipe.weaponClass;
            else recipe.weaponClass = discipline;
            lineage = database.GetLineage(recipe.lineage);
            setup = lineage.SetupFor(recipe.bodyType);
            Preview();
            RefreshTab();
        }

        private void Preview()
        {
            if (showroom != null) showroom.Show(recipe);
        }

        private void SetListTitle(string title)
        {
            if (optionsSection != null) MenuUi.SetText(optionsSection.transform, "Label_Header", title);
        }

        private void HideOptionRows(int from)
        {
            for (int i = from; i < optionRows.Length; i++)
                if (optionRows[i] != null) optionRows[i].Hide();
        }

        private void RefreshOptionRows()
        {
            foreach (var row in optionRows)
                if (row != null && row.gameObject.activeSelf) row.Refresh();
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }
    }
}
