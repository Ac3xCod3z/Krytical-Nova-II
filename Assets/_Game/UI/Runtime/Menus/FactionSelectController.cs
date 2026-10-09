using System;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Faction select: one card per faction (crest, tagline, lore, its lineages). Click or use the arrow keys to
    /// highlight one, Enter to carry it into the character creator. Built on Synty's "Select Difficulty" screen:
    /// each difficulty card is a faction.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FactionSelectController : MonoBehaviour
    {
        [Tooltip("The Select Difficulty screen instance (Screen_FantasyMenus_SelectDifficulty_01).")]
        [SerializeField] private Transform screen;
        [SerializeField] private CharacterCreationDatabase database;
        [SerializeField] private MenuHotkeys hotkeys;
        [Tooltip("The cards, left to right. Each shows the faction at the same position in the faction catalog.")]
        [SerializeField] private Transform[] cards = Array.Empty<Transform>();
        [Tooltip("Background glow, tinted with the highlighted faction's colour.")]
        [SerializeField] private Graphic glow;
        [SerializeField, Range(0f, 1f)] private float glowAlpha = 0.6f;
        [Tooltip("The factions' champions, one standing in each card. None: the cards are crests and words only.")]
        [SerializeField] private FactionShowcase showcase;
        [Tooltip("The one panel of words: the chosen faction's tagline, lore and lineages. None: each card carries its own.")]
        [SerializeField] private TMP_Text info;

        /// <summary>The part of a card its champion stands in. A card without one is the frame itself.</summary>
        public const string DollWindowName = "Doll_Window";

        private readonly List<FactionDefinition> factions = new List<FactionDefinition>();
        private readonly List<string> words = new List<string>();
        private readonly List<MenuRow> rows = new List<MenuRow>();
        private readonly List<LineageDefinition> lineageBuffer = new List<LineageDefinition>();
        private int selected = -1;

        private void Start()
        {
            MenuUi.SetText(screen, "Screen_Header", "Choose Your Allegiance");
            var catalog = database != null ? database.Factions : null;
            for (int i = 0; i < cards.Length; i++)
            {
                var card = cards[i];
                if (card == null) continue;
                var faction = catalog != null && i < catalog.Factions.Count ? catalog.Factions[i] : null;
                if (faction == null)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }

                Fill(card, faction);
                if (!card.TryGetComponent<MenuRow>(out var row)) row = card.gameObject.AddComponent<MenuRow>();
                int index = factions.Count;
                row.Clicked += () => Select(index);
                row.DoubleClicked += Confirm;
                factions.Add(faction);
                rows.Add(row);
            }

            if (showcase != null)
            {
                var frames = new List<RectTransform>(rows.Count);
                foreach (MenuRow row in rows)
                {
                    Transform window = MenuUi.Find(row.transform, DollWindowName);
                    frames.Add((RectTransform)(window != null ? window : row.transform));
                }
                showcase.Show(factions, frames);
            }

            BindPrompts();
            int initial = factions.FindIndex(f => f.id == FrontEnd.PendingFaction);
            Select(Mathf.Max(initial, 0));
        }

        private void Fill(Transform card, FactionDefinition faction)
        {
            MenuUi.SetText(card, "Label_Difficulty", faction.displayName);

            database.GetLineages(faction.id, lineageBuffer);
            var names = new List<string>(lineageBuffer.Count);
            foreach (var lineage in lineageBuffer) names.Add(lineage.displayName);
            string accent = ColorUtility.ToHtmlStringRGB(faction.accentColor);
            string text = $"<color=#{accent}><i>{faction.tagline}</i></color>\n\n{faction.lore}"
                          + (names.Count > 0 ? $"\n\n<color=#{accent}>{string.Join(" & ", names)}</color>" : string.Empty);
            words.Add(text);
            var description = MenuUi.Find<TMP_Text>(card, "Label_Description");
            if (description != null && info == null)
            {
                description.text = text;
                // A paragraph of lore, where the pack's card holds a sentence: start at the top and shrink to fit.
                description.alignment = TextAlignmentOptions.Top;
                description.enableAutoSizing = true;
                description.fontSizeMin = 26f;
                description.fontSizeMax = 40f;
            }

            var crest = MenuUi.Find<Image>(card, "Faction_Crest");
            if (crest != null)
            {
                crest.sprite = faction.crest;
                crest.color = faction.accentColor;
                crest.enabled = faction.crest != null;
                crest.preserveAspect = true;
            }
            var crestGlow = MenuUi.Find<Image>(card, "Faction_Glow");
            if (crestGlow != null) crestGlow.color = WithAlpha(faction.accentColor, 0.45f);

            // The chosen card lights up in its faction's colour instead of the pack's blue.
            var active = MenuUi.Find(card, "Background_Active");
            if (active == null) return;
            Color.RGBToHSV(faction.accentColor, out float h, out float s, out _);
            Color wash = Color.HSVToRGB(h, Mathf.Min(s, 0.75f), 0.55f);
            foreach (var layer in active.GetComponentsInChildren<Image>(true))
                if (layer.name.StartsWith("SPR_Gradient")) layer.color = WithAlpha(wash, layer.color.a);
        }

        private void BindPrompts()
        {
            Prompt("Hotkey_Prompt_01", Key.Enter, "Choose", Confirm);
            Prompt("Hotkey_Prompt_02", Key.Escape, "Back", Back);
            if (hotkeys == null) return;
            hotkeys.Bind(Key.NumpadEnter, Confirm);
            hotkeys.Bind(Key.LeftArrow, () => Select(Mathf.Max(selected - 1, 0)));
            hotkeys.Bind(Key.RightArrow, () => Select(Mathf.Min(selected + 1, factions.Count - 1)));
        }

        private void Prompt(string promptName, Key key, string label, Action action)
        {
            var prompt = MenuUi.Require(screen, promptName, this);
            if (prompt == null) return;
            MenuUi.SetText(prompt, "Label_Action", label);
            MenuUi.MakeClickable(prompt.gameObject, () => action());
            if (hotkeys != null) hotkeys.Bind(key, action, prompt);
        }

        private void Select(int index)
        {
            if (index < 0 || index >= factions.Count) return;
            if (index != selected && index < rows.Count && rows[index] != null) MenuMoments.Raise(MenuMoment.Toggle, rows[index].transform);
            selected = index;
            for (int i = 0; i < rows.Count; i++) rows[i].SetSelected(i == index);
            if (glow != null) glow.color = WithAlpha(factions[index].accentColor, glowAlpha);
            if (info != null && index < words.Count) info.text = words[index];
            if (showcase != null) showcase.Select(index);
        }

        private void Confirm()
        {
            if (selected < 0 || selected >= factions.Count) return;
            FrontEnd.PendingFaction = factions[selected].id;
            MenuMoments.Raise(MenuMoment.Confirm, selected < rows.Count && rows[selected] != null ? rows[selected].transform : null);
            FrontEnd.Go(FrontEnd.CharacterCreatorScene);
        }

        // Back to wherever a new character was started from: character select if there is one to select, else the title.
        private void Back()
        {
            FrontEnd.PendingFaction = FactionId.None;
            MenuMoments.Raise(MenuMoment.Back);
            FrontEnd.Go(CharacterSession.Store.Load().Count > 0 ? FrontEnd.CharacterSelectScene : FrontEnd.TitleScene);
        }

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);
    }
}
