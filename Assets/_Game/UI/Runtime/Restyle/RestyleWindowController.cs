using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Core;
using KryticalNova.Items;
using KryticalNova.Items.Visuals;
using KryticalNova.NPC;
using KryticalNova.Restyle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace KryticalNova.UI
{
    /// <summary>
    /// The Stylist's, the Reshaper's and the Coach's window: one window, three services. It lists what that service
    /// may change about the character (the regions, colours and sliders its lineage offers), each with a step to
    /// either side, and shows every change on the equipment page's doll, which opens beside it. The button uses up
    /// a pass and sends the look to <see cref="PlayerRestyle"/>; the server keeps only what the service owns out
    /// of it. Built on the creator's own editing rules (<see cref="RecipeEdit"/>). Closed when the player walks away.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class RestyleWindowController : MonoBehaviour
    {
        private const float RangeCheckSeconds = 0.25f;
        private const string WindowName = "restyle-window";
        private const string EquipmentWindowName = "equipment-window";
        private const int MarkStep = 10, BodyStep = 10;

        private VisualElement root, window, rowsHost;
        private Label title, reasonLabel, helpLabel;
        private Button act, reset;
        private readonly ServicePanelPlacement placement = new ServicePanelPlacement();
        private PlayerRestyle restyle;
        private RestyleService service;
        private CharacterRecipe current, draft;
        private bool initialized;
        // Open at the NPC, as against merely shown: the layout editor shows the window to have it placed.
        private bool open, previewing;
        private bool openedEquipment;
        private float nextRangeCheck;
        private int shownPasses = -1;

        // ------------------------------------------------------------------ lifecycle

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            root = GetComponent<UIDocument>().rootVisualElement;
            window = root?.Q(WindowName);
            if (window == null)
            {
                Debug.LogError("[Restyle] RestyleWindows.uxml is not instanced in this UI document.", this);
                return;
            }
            rowsHost = window.Q("restyle-rows");
            title = window.Q<Label>("restyle-title");
            reasonLabel = window.Q<Label>("restyle-reason");
            helpLabel = window.Q<Label>("restyle-help");
            act = window.Q<Button>("restyle-act");
            reset = window.Q<Button>("restyle-reset");
            window.Q<Button>("close-restyle").clicked += Close;
            act.clicked += Act;
            reset.clicked += ResetDraft;
            SetOpen(false);

            NpcServices.Register(ServiceIds.Stylist, NpcOffer.Stylist, "CHANGE HAIR", () => Open(RestyleService.Stylist));
            NpcServices.Register(ServiceIds.Reshaper, NpcOffer.Reshaper, "CHANGE FACE", () => Open(RestyleService.Reshaper));
            NpcServices.Register(ServiceIds.Coach, NpcOffer.Coach, "CHANGE BUILD", () => Open(RestyleService.Coach));
            PlayerInventory.LocalChanged += Close;
            HudLayoutEditor.RegisterPreview(WindowName, Preview);
            initialized = true;
        }

        private void OnDisable()
        {
            if (!initialized) return;
            Close();
            initialized = false;
            previewing = false;
            HudLayoutEditor.UnregisterPreview(WindowName);
            PlayerInventory.LocalChanged -= Close;
            NpcServices.Unregister(ServiceIds.Stylist);
            NpcServices.Unregister(ServiceIds.Reshaper);
            NpcServices.Unregister(ServiceIds.Coach);
            Bind(null);
        }

        private void Bind(PlayerRestyle next)
        {
            if (restyle != null) restyle.Restyled -= OnRestyled;
            restyle = next;
            if (restyle != null) restyle.Restyled += OnRestyled;
        }

        // The new look is on: the window starts again from it.
        private void OnRestyled()
        {
            if (!open || restyle == null) return;
            current = restyle.Identity.State.ToRecipe();
            ResetDraft();
        }

        // ------------------------------------------------------------------ open / close

        public bool IsOpen => initialized && open;

        private void Open(RestyleService which)
        {
            if (!initialized) return;
            previewing = false;
            if (open) Close();
            Bind(PlayerRestyle.Local);
            if (restyle == null || !restyle.Identity.HasIdentity || restyle.Identity.Database == null)
            {
                Notify("That isn't available for this character.");
                return;
            }
            service = which;
            title.text = PlayerRestyle.NpcName(service).ToUpperInvariant();
            helpLabel.text = service == RestyleService.Coach
                ? "One Training Pass covers every change made at once"
                : "One Restyle Pass covers every change made at once";
            current = restyle.Identity.State.ToRecipe();
            draft = current.Clone();

            SetOpen(true);
            window.parent?.BringToFront();
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;

            // Every change is judged on the doll: the equipment page opens with the window, which sits beside it.
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                openedEquipment = !gear.IsEquipmentOpen;
                bool packWasOpen = gear.IsInventoryOpen;
                gear.SetEquipmentOpen(true);
                if (!packWasOpen) gear.SetInventoryOpen(false); // the page brings the Pack with it; it is not needed here
                gear.SuppressWorldInput();
            }
            placement.Open(window, root.Q(EquipmentWindowName));
            BuildRows();
        }

        public void Close()
        {
            if (!initialized || !open) return;
            SetOpen(false);
            placement.Close();
            GearUIController gear = GearUIController.Instance;
            if (gear != null)
            {
                gear.ClearPreview();
                if (openedEquipment) gear.SetEquipmentOpen(false);
                gear.SuppressWorldInput();
            }
            openedEquipment = false;
            Bind(null);
        }

        private void SetOpen(bool shown)
        {
            open = shown;
            window.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            window.EnableInClassList("visible", shown);
        }

        /// <summary>The layout editor has the window out to be placed, away from any NPC: it shows what it would at a Stylist.</summary>
        private void Preview(bool on)
        {
            if (!initialized || open) return; // open at an NPC: it is its own likeness
            previewing = on;
            if (!on) { Bind(null); return; }
            Bind(PlayerRestyle.Local);
            if (restyle == null || !restyle.Identity.HasIdentity || restyle.Identity.Database == null) return;
            service = RestyleService.Stylist;
            current = restyle.Identity.State.ToRecipe();
            draft = current.Clone();
            BuildRows();
        }

        private void Update()
        {
            if (!open) return;
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !HudTyping.IsTyping(root)) { Close(); return; }
            if (restyle == null) { Close(); return; }
            placement.Tick();
            if (restyle.PassesHeld(service) != shownPasses) ShowAction();
            if (Time.unscaledTime < nextRangeCheck) return;
            nextRangeCheck = Time.unscaledTime + RangeCheckSeconds;
            if (restyle.InReach(service)) return;
            string who = PlayerRestyle.NpcName(service);
            Close();
            Notify($"You walked away from the {who}.");
        }

        // ------------------------------------------------------------------ what can be changed

        /// <summary>One row for everything the service owns that this lineage has a choice in.</summary>
        private void BuildRows()
        {
            rowsHost.Clear();
            CharacterCreationDatabase database = restyle.Identity.Database;
            LineageDefinition lineage = database.GetLineage(draft.lineage);
            LineageDefinition.Setup setup = lineage != null ? lineage.SetupFor(draft.bodyType) : null;
            if (setup == null) { ShowAction(); return; }
            SidekickPartCatalog catalog = database.Parts;

            foreach (LineageDefinition.RegionOptions options in setup.regions)
            {
                if (!RestyleRules.Owns(service, options.region) || !RecipeEdit.HasChoice(options)) continue;
                LineageDefinition.RegionOptions picked = options;
                Stepper(RegionName(options.region),
                    () =>
                    {
                        int index = RecipeEdit.IndexOf(draft, catalog, picked);
                        return index < 0 ? "None" : $"{index + 1} / {picked.partIds.Length}";
                    },
                    by => RecipeEdit.Step(ref draft, catalog, setup, picked, by));
            }

            foreach (byte id in setup.channels)
            {
                if (!RestyleRules.OwnsChannel(service, id) || !database.TryGetChannel(id, out CharacterCreationDatabase.ColorChannel channel)) continue;
                if (channel.swatches == null || channel.swatches.Length == 0) continue;
                CharacterCreationDatabase.ColorChannel picked = channel;
                Stepper(string.IsNullOrEmpty(channel.displayName) ? "Colour" : channel.displayName + " colour",
                    () =>
                    {
                        int index = RecipeEdit.SwatchIndex(draft, picked);
                        return index < 0 ? "Own" : $"{index + 1} / {picked.swatches.Length}";
                    },
                    by =>
                    {
                        int count = picked.swatches.Length;
                        int index = ((Mathf.Max(RecipeEdit.SwatchIndex(draft, picked), by > 0 ? -1 : 0) + by) % count + count) % count;
                        RecipeEdit.SetColor(ref draft, picked.id, picked.swatches[index]);
                    },
                    () => RecipeEdit.TryGetColor(draft, picked.id, out Color32 colour) ? (Color?)(Color)colour : null);
            }

            if (RestyleRules.OwnsSkin(service))
            {
                int tones = setup.skinTones.Count;
                if (tones > 0)
                    Stepper("Skin tone", () => draft.skinTone == 0 ? "Own" : $"{draft.skinTone} / {tones}",
                        by => draft.skinTone = (byte)(((draft.skinTone + by) % (tones + 1) + tones + 1) % (tones + 1)),
                        () => draft.skinTone > 0 && draft.skinTone <= tones ? (Color?)(Color)setup.skinTones[draft.skinTone - 1].swatch : null);
                Stepper("Scars", () => draft.scars + "%", by => draft.scars = Mark(draft.scars, by));
                Stepper("Eyeliner", () => draft.eyeliner + "%", by => draft.eyeliner = Mark(draft.eyeliner, by));
                Stepper("Grime", () => draft.grime + "%", by => draft.grime = Mark(draft.grime, by));
            }

            if (RestyleRules.OwnsBody(service))
            {
                Stepper("Build", () => Signed(draft.build, "slim", "heavy"), by => draft.build = Body(draft.build, by));
                Stepper("Muscle", () => Signed(draft.muscle, "lean", "muscular"), by => draft.muscle = Body(draft.muscle, by));
            }

            if (rowsHost.childCount == 0)
            {
                var none = new Label($"The {PlayerRestyle.NpcName(service)} has nothing to offer your lineage.") { pickingMode = PickingMode.Ignore };
                none.AddToClassList("restyle-none");
                rowsHost.Add(none);
            }
            ShowAction();
        }

        private static byte Mark(byte value, int by) => (byte)Mathf.Clamp(value + by * MarkStep, 0, RestyleRules.MarkLimit);
        private static sbyte Body(sbyte value, int by) => (sbyte)Mathf.Clamp(value + by * BodyStep, -RestyleRules.BodyLimit, RestyleRules.BodyLimit);
        private static string Signed(int value, string low, string high) => value == 0 ? "Even" : value < 0 ? $"{-value}% {low}" : $"{value}% {high}";

        private static string RegionName(BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Head: return "Face";
                case BodyRegion.Hair: return "Hair";
                case BodyRegion.EyebrowLeft: return "Eyebrows";
                case BodyRegion.EyeLeft: return "Left eye";
                case BodyRegion.EyeRight: return "Right eye";
                case BodyRegion.EarLeft: return "Ears";
                case BodyRegion.FacialHair: return "Facial hair";
                case BodyRegion.Nose: return "Nose";
                case BodyRegion.Teeth: return "Teeth";
                case BodyRegion.Tongue: return "Tongue";
                default: return region.ToString();
            }
        }

        /// <summary>A row: what it changes, a step back, where it stands, a step on. A colour row shows the colour beside the number.</summary>
        private void Stepper(string label, Func<string> value, Action<int> step, Func<Color?> colour = null)
        {
            var row = new VisualElement();
            row.AddToClassList("restyle-row");
            var name = new Label(label) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("restyle-row-label");
            row.Add(name);
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("restyle-row-swatch");
            var shown = new Label { pickingMode = PickingMode.Ignore };
            shown.AddToClassList("restyle-row-value");

            void Show()
            {
                shown.text = value();
                Color? tint = colour != null ? colour() : null;
                swatch.style.display = colour != null ? DisplayStyle.Flex : DisplayStyle.None;
                swatch.style.backgroundColor = tint ?? new Color(0f, 0f, 0f, 0f);
            }
            Button Arrow(string text, int by)
            {
                var button = new Button(() =>
                {
                    step(by);
                    Show();
                    ShowOnDoll();
                    ShowAction();
                }) { text = text };
                button.AddToClassList("restyle-step");
                return button;
            }
            row.Add(Arrow("<", -1));
            row.Add(swatch);
            row.Add(shown);
            row.Add(Arrow(">", 1));
            rowsHost.Add(row);
            Show();
        }

        // ------------------------------------------------------------------ the doll, and paying

        /// <summary>The doll wears the look being put together: the character's own look with this service's changes.</summary>
        private void ShowOnDoll()
        {
            GearUIController gear = GearUIController.Instance;
            if (gear == null || !open || restyle == null) return;
            CharacterCreationDatabase database = restyle.Identity.Database;
            CharacterRecipe look = draft.Clone();
            gear.PreviewOnDoll(doll =>
            {
                if (doll.TryGetComponent(out CharacterLook dollLook)) dollLook.Apply(look, database);
            }, $"{PlayerRestyle.NpcName(service)}: a new look");
        }

        private void ResetDraft()
        {
            draft = current.Clone();
            GearUIController.Instance?.ClearPreview();
            if (open || previewing) BuildRows();
        }

        /// <summary>The button, what it uses up, and why it would not work.</summary>
        private void ShowAction()
        {
            if (restyle == null) return;
            shownPasses = restyle.PassesHeld(service);
            bool costs = restyle.PassItemId(service) != 0;
            string pass = restyle.PassName(service);
            act.text = costs ? $"APPLY   ·   1 {pass} (you have {shownPasses})" : "APPLY";

            CharacterCreationDatabase database = restyle.Identity.Database;
            CharacterRecipe wanted = RestyleRules.Merge(current, draft, service, database != null ? database.Parts : null);
            bool changed = !RestyleRules.Same(current, wanted);
            string reason = null;
            if (changed && !CharacterRecipeValidator.Validate(wanted, database, out string invalid)) reason = invalid;
            if (reason == null && changed && costs && shownPasses < 1) reason = $"You have no {pass}.";
            act.SetEnabled(changed && reason == null);
            reset.style.display = changed ? DisplayStyle.Flex : DisplayStyle.None;
            reasonLabel.text = reason ?? string.Empty;
            reasonLabel.style.display = string.IsNullOrEmpty(reason) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void Act()
        {
            if (!open || restyle == null) return;
            restyle.RequestRestyle(service, draft);
        }

        private static void Notify(string text) => ClientPresentationRegistry.Messages?.ShowMessage(text);
    }
}
