using System.Collections.Generic;
using KryticalNova.Progression;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The icon library's browser. Left: every skill (or item) and the icon it has. Middle: the packs' icons, by
    /// set or as the best free matches for the chosen skill. Right: the chosen icon as painted and as the game will
    /// show it, its tags, and the button that gives it to the skill. Giving an icon bakes its treated copy and
    /// hands it to the skill or item at once.
    /// </summary>
    public sealed class IconLibraryWindow : EditorWindow
    {
        private const float TargetWidth = 300f, DetailWidth = 330f, Thumb = 72f;
        private const int MostSuggestions = 60;

        private IconLibrary library;
        private IconTreatmentSettings settings;
        private List<IconTarget> targets = new List<IconTarget>();
        private bool showItems;
        private IconTarget target;
        private string setId;
        private int number;
        private bool suggest = true, freeOnly = true;
        private string search = string.Empty;
        private int setIndex;
        private Vector2 targetScroll, gridScroll, detailScroll;
        private readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();
        private Texture2D preview;
        private string previewKey, message;
        private List<(IconLibrary.Set set, int number, int score)> suggestions = new List<(IconLibrary.Set, int, int)>();
        private IconTarget suggestionsFor;
        private string keywordText, raceText;
        private IconLibrary.Icon keywordIcon;

        [MenuItem("Krytical Nova/Icons/Icon Library")]
        public static void Open()
        {
            var window = GetWindow<IconLibraryWindow>("Icon Library");
            window.minSize = new Vector2(1000f, 560f);
        }

        private void OnEnable()
        {
            library = IconBaker.Library();
            settings = IconBaker.Settings();
            Reload();
        }

        private void OnDisable()
        {
            if (preview != null) DestroyImmediate(preview);
            preview = null;
            thumbs.Clear();
        }

        private void Reload()
        {
            targets = showItems ? IconTargets.Items() : IconTargets.Skills();
            target = null;
            suggestionsFor = null;
        }

        private void OnGUI()
        {
            if (library == null || settings == null) { OnEnable(); if (library == null) return; }
            Toolbar();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                Targets();
                Icons();
                Detail();
            }
        }

        // ---------------------------------------------------------------- toolbar

        private void Toolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                bool items = GUILayout.Toolbar(showItems ? 1 : 0, new[] { "Skills", "Items" }, EditorStyles.toolbarButton, GUILayout.Width(140f)) == 1;
                if (items != showItems) { showItems = items; Reload(); }
                GUILayout.Space(8f);
                if (GUILayout.Button(new GUIContent("Scan Packs", "List the sets of the icon packs in the project. Sets already listed keep their tags."), EditorStyles.toolbarButton))
                {
                    int added = IconPacks.Scan(library);
                    Save();
                    message = $"{library.sets.Count} sets listed, {added} of them new.";
                }
                if (GUILayout.Button(new GUIContent("Give Icons To All Without One", "Every listed skill or item that has no icon gets the best free match by its tags."), EditorStyles.toolbarButton))
                    GiveToAllWithout();
                if (GUILayout.Button(new GUIContent("Bake All", "Redraw every assigned icon with the treatment settings and hand each to its skill or item."), EditorStyles.toolbarButton))
                    message = $"Baked {IconBaker.BakeAll(library, settings)} icon(s).";
                if (GUILayout.Button(new GUIContent("Check", "List every skill or item without an icon of its own."), EditorStyles.toolbarButton))
                {
                    List<string> problems = IconPacks.Check(library);
                    message = problems.Count == 0 ? "Every skill and every item without a model has an icon of its own, and shows it."
                        : string.Join("\n", problems.GetRange(0, Mathf.Min(8, problems.Count))) + (problems.Count > 8 ? $"\n… and {problems.Count - 8} more (see the Console)." : string.Empty);
                    if (problems.Count > 8) foreach (string problem in problems) Debug.LogWarning("[Icons] " + problem);
                }
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Treatment Settings", EditorStyles.toolbarButton)) Selection.activeObject = settings;
                if (GUILayout.Button("Library Asset", EditorStyles.toolbarButton)) Selection.activeObject = library;
            }
        }

        private void GiveToAllWithout()
        {
            int given = 0;
            foreach (IconTarget each in targets)
            {
                if (library.AssignmentOf(each.kind, each.id) != null) continue;
                var best = library.Suggest(each.request, 1);
                if (best.Count == 0) continue;
                if (!library.Assign(each.kind, each.id, best[0].set.id, best[0].number, false, out _)) continue;
                IconBaker.BakeFor(library, settings, each);
                given++;
            }
            Save();
            message = given == 0 ? "Nothing listed here is without an icon." : $"{given} icon(s) given from the tags. Look them over: they are suggestions, not choices.";
        }

        // ---------------------------------------------------------------- left: what needs an icon

        private void Targets()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(targetScroll, GUILayout.Width(TargetWidth)))
            {
                targetScroll = scroll.scrollPosition;
                string group = null;
                foreach (IconTarget each in targets)
                {
                    if (each.group != group)
                    {
                        group = each.group;
                        GUILayout.Space(4f);
                        EditorGUILayout.LabelField(group, EditorStyles.boldLabel);
                    }
                    IconLibrary.Assignment assignment = library.AssignmentOf(each.kind, each.id);
                    Rect row = EditorGUILayout.GetControlRect(false, 36f);
                    if (each == target) EditorGUI.DrawRect(row, new Color(0.24f, 0.48f, 0.9f, 0.35f));
                    var icon = new Rect(row.x + 2f, row.y + 2f, 32f, 32f);
                    Sprite sprite = each.Icon;
                    if (sprite != null && sprite.texture != null) GUI.DrawTexture(icon, sprite.texture, ScaleMode.ScaleToFit);
                    else EditorGUI.DrawRect(icon, new Color(0.5f, 0.1f, 0.1f, 0.6f));
                    GUI.Label(new Rect(row.x + 40f, row.y + 1f, row.width - 40f, 18f), each.name);
                    GUI.Label(new Rect(row.x + 40f, row.y + 17f, row.width - 40f, 16f),
                        assignment != null ? $"{assignment.set}  {assignment.number:00}{(assignment.chosen ? string.Empty : "  (suggested)")}" : "no icon in the library",
                        EditorStyles.miniLabel);
                    if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                    {
                        target = each;
                        if (assignment != null) Choose(assignment.set, assignment.number);
                        Event.current.Use();
                        Repaint();
                    }
                }
            }
        }

        // ---------------------------------------------------------------- middle: the packs' icons

        private void Icons()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    suggest = GUILayout.Toggle(suggest, new GUIContent("Best matches", "Show the free icons that suit the chosen skill or item, best first, instead of one set."), EditorStyles.miniButton, GUILayout.Width(100f));
                    using (new EditorGUI.DisabledScope(suggest))
                    {
                        var ids = SetIds();
                        if (ids.Length > 0)
                        {
                            setIndex = Mathf.Clamp(setIndex, 0, ids.Length - 1);
                            setIndex = EditorGUILayout.Popup(setIndex, ids);
                        }
                        freeOnly = GUILayout.Toggle(freeOnly, new GUIContent("Free only", "Hide icons a skill or item already has."), EditorStyles.miniButton, GUILayout.Width(74f));
                    }
                    search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.Width(150f));
                }
                if (library.sets.Count == 0)
                {
                    EditorGUILayout.HelpBox("The library lists no sets yet. Press Scan Packs.", MessageType.Warning);
                    return;
                }
                using (var scroll = new EditorGUILayout.ScrollViewScope(gridScroll))
                {
                    gridScroll = scroll.scrollPosition;
                    float width = position.width - TargetWidth - DetailWidth - 40f;
                    int columns = Mathf.Max(1, Mathf.FloorToInt(width / (Thumb + 6f)));
                    if (suggest) Suggestions(columns);
                    else SetGrid(columns);
                }
            }
        }

        private string[] SetIds()
        {
            var ids = new List<string>();
            foreach (IconLibrary.Set set in library.sets)
                if (set != null && (string.IsNullOrEmpty(search) || set.id.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0)) ids.Add(set.id);
            return ids.ToArray();
        }

        private void Suggestions(int columns)
        {
            if (target == null)
            {
                EditorGUILayout.HelpBox("Choose a skill or an item on the left to see the icons that suit it.", MessageType.Info);
                return;
            }
            if (suggestionsFor != target || Event.current.type == EventType.Layout && suggestions.Count == 0)
            {
                suggestions = library.Suggest(target.request, MostSuggestions);
                suggestionsFor = target;
            }
            if (suggestions.Count == 0)
            {
                EditorGUILayout.HelpBox("No free icon is tagged for this. Switch off Best matches and browse a set.", MessageType.Info);
                return;
            }
            Grid(columns, suggestions.Count, i => (suggestions[i].set, suggestions[i].number, "match " + suggestions[i].score));
        }

        private void SetGrid(int columns)
        {
            string[] ids = SetIds();
            if (ids.Length == 0) return;
            IconLibrary.Set set = library.FindSet(ids[Mathf.Clamp(setIndex, 0, ids.Length - 1)]);
            if (set == null) return;
            var shown = new List<int>();
            for (int n = 1; n <= set.count; n++)
                if (!freeOnly || library.UserOf(set.id, n) == null) shown.Add(n);
            Grid(columns, shown.Count, i => (set, shown[i], null));
        }

        private void Grid(int columns, int count, System.Func<int, (IconLibrary.Set set, int number, string note)> at)
        {
            for (int start = 0; start < count; start += columns)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = start; i < Mathf.Min(count, start + columns); i++)
                    {
                        (IconLibrary.Set set, int n, string note) = at(i);
                        Rect cell = GUILayoutUtility.GetRect(Thumb + 4f, Thumb + 18f, GUILayout.Width(Thumb + 4f));
                        var picture = new Rect(cell.x + 2f, cell.y + 2f, Thumb, Thumb);
                        if (set.id == setId && n == number) EditorGUI.DrawRect(cell, new Color(0.24f, 0.48f, 0.9f, 0.6f));
                        Texture2D thumb = ThumbOf(set, n);
                        if (thumb != null) GUI.DrawTexture(picture, thumb, ScaleMode.ScaleToFit);
                        IconLibrary.Assignment user = library.UserOf(set.id, n);
                        if (user != null) EditorGUI.DrawRect(new Rect(picture.x, picture.yMax - 6f, picture.width, 6f), new Color(1f, 0.79f, 0.24f));
                        GUI.Label(new Rect(cell.x + 2f, picture.yMax, Thumb, 16f),
                            new GUIContent(note ?? n.ToString("00"), user != null ? "the icon of " + user.target : set.id + " " + n.ToString("00")), EditorStyles.miniLabel);
                        if (Event.current.type == EventType.MouseDown && cell.Contains(Event.current.mousePosition))
                        {
                            Choose(set.id, n);
                            Event.current.Use();
                            Repaint();
                        }
                    }
                    GUILayout.FlexibleSpace();
                }
            }
        }

        private Texture2D ThumbOf(IconLibrary.Set set, int n)
        {
            string path = IconBaker.FileOf(set, n);
            if (path == null) return null;
            if (!thumbs.TryGetValue(path, out Texture2D thumb) || thumb == null)
            {
                thumb = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                thumbs[path] = thumb;
            }
            return thumb;
        }

        private void Choose(string set, int n)
        {
            setId = set;
            number = n;
            keywordIcon = null;
            message = null;
        }

        // ---------------------------------------------------------------- right: the chosen icon

        private void Detail()
        {
            using (var scroll = new EditorGUILayout.ScrollViewScope(detailScroll, GUILayout.Width(DetailWidth)))
            {
                detailScroll = scroll.scrollPosition;
                IconLibrary.Set set = library.FindSet(setId);
                if (set == null || number < 1)
                {
                    EditorGUILayout.HelpBox("Choose an icon to see it as the game will show it, to tag it, and to give it to a skill or an item.", MessageType.Info);
                    return;
                }
                EditorGUILayout.LabelField($"{set.id}  {number:00}", EditorStyles.boldLabel);
                string key = $"{set.id}|{number}|{EditorUtility.GetDirtyCount(settings)}|{set.moveHue}|{set.hueFrom}|{set.hueTo}|{set.hueBand}";
                if (key != previewKey)
                {
                    if (preview != null) DestroyImmediate(preview);
                    preview = IconBaker.Preview(set, number, settings);
                    previewKey = key;
                }
                Rect pictures = GUILayoutUtility.GetRect(DetailWidth - 20f, 150f);
                Texture2D source = ThumbOf(set, number);
                if (source != null) GUI.DrawTexture(new Rect(pictures.x, pictures.y, 146f, 146f), source, ScaleMode.ScaleToFit);
                if (preview != null)
                {
                    var after = new Rect(pictures.x + 154f, pictures.y, 146f, 146f);
                    EditorGUI.DrawRect(after, new Color(0.15f, 0.2f, 0.26f));
                    GUI.DrawTexture(after, preview, ScaleMode.ScaleToFit);
                    var small = new Rect(after.xMax - 50f, after.yMax - 50f, 50f, 50f);
                    EditorGUI.DrawRect(small, new Color(0.15f, 0.2f, 0.26f));
                    GUI.DrawTexture(small, preview, ScaleMode.ScaleToFit);
                }
                EditorGUILayout.LabelField("as painted   |   as the game shows it (and at hotbar size)", EditorStyles.miniLabel);

                IconLibrary.Assignment user = library.UserOf(set.id, number);
                EditorGUILayout.LabelField(user != null ? "The icon of " + user.target : "Free", EditorStyles.miniBoldLabel);
                using (new EditorGUI.DisabledScope(target == null || (user != null && (user.kind != target.kind || user.target != target.id))))
                {
                    if (GUILayout.Button(target != null ? $"Give to {target.name}" : "Choose a skill or an item first", GUILayout.Height(26f)) && target != null)
                    {
                        if (library.Assign(target.kind, target.id, set.id, number, true, out string refusal))
                        {
                            Save();
                            message = IconBaker.BakeFor(library, settings, target) ? $"{target.name} now has {set.id} {number:00}." : $"{set.id} {number:00} could not be baked.";
                            suggestionsFor = null;
                        }
                        else message = refusal;
                    }
                }
                if (user != null && GUILayout.Button("Take it back (leaves " + user.target + " without an icon in the library)"))
                {
                    library.Unassign(user.kind, user.target);
                    Save();
                    suggestionsFor = null;
                }

                GUILayout.Space(8f);
                Tags(set);
                GUILayout.Space(8f);
                SetTags(set);
            }
        }

        private void Tags(IconLibrary.Set set)
        {
            EditorGUILayout.LabelField("This icon", EditorStyles.boldLabel);
            IconLibrary.Icon icon = library.FindIcon(set.id, number);
            EditorGUI.BeginChangeCheck();
            bool ownUse = EditorGUILayout.Toggle(new GUIContent("Use of its own", "Off = the set's use."), icon != null && icon.ownUse);
            IconUse use = icon != null && icon.ownUse ? icon.use : set.use;
            using (new EditorGUI.DisabledScope(!ownUse)) use = (IconUse)EditorGUILayout.EnumPopup("Use", use);
            var factions = (IconFactions)EditorGUILayout.EnumFlagsField(new GUIContent("Factions", "None = the set's."), icon != null ? icon.factions : IconFactions.None);
            if (keywordIcon != icon || keywordText == null)
            {
                keywordIcon = icon;
                keywordText = icon != null ? string.Join(", ", icon.keywords) : string.Empty;
                raceText = icon != null ? string.Join(", ", icon.races) : string.Empty;
            }
            keywordText = EditorGUILayout.TextField(new GUIContent("Keywords", "What kind of move it suits: sweep, single, triple, heavy, flurry, leap, stance, guard, shield, speed …"), keywordText);
            raceText = EditorGUILayout.TextField(new GUIContent("Races", "Lineages it suits: Civilian, Soldier, Robot, Skeleton, Elf, Viking."), raceText);
            string shows = EditorGUILayout.TextField(new GUIContent("Shows", "What the picture shows, in plain words."), icon != null ? icon.shows : string.Empty);
            if (!EditorGUI.EndChangeCheck()) return;
            icon = library.TagsFor(set.id, number);
            keywordIcon = icon;
            icon.ownUse = ownUse;
            icon.use = use;
            icon.factions = factions;
            icon.keywords = Words(keywordText);
            icon.races = Words(raceText);
            icon.shows = shows;
            Save();
            suggestionsFor = null;
        }

        private void SetTags(IconLibrary.Set set)
        {
            EditorGUILayout.LabelField("Its set: " + set.id, EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var use = (IconUse)EditorGUILayout.EnumPopup("Use", set.use);
            var factions = (IconFactions)EditorGUILayout.EnumFlagsField("Factions", set.factions);
            WeaponClassId first = set.weaponClasses.Count > 0 ? set.weaponClasses[0] : WeaponClassId.None;
            var weaponClass = (WeaponClassId)EditorGUILayout.EnumPopup(new GUIContent("Class", "The class (weapon line) the set is drawn for. None = any. More than one: edit the library asset."), first);
            string races = EditorGUILayout.TextField("Races", string.Join(", ", set.races));
            bool moveHue = EditorGUILayout.Toggle(new GUIContent("Move its colour", "For a class that wears a set painted in another colour."), set.moveHue);
            float from = set.hueFrom, to = set.hueTo, band = set.hueBand;
            using (new EditorGUI.DisabledScope(!moveHue))
            {
                from = EditorGUILayout.Slider(new GUIContent("From hue", "The colour the set is painted in (0 red, 120 green, 240 blue)."), from, 0f, 360f);
                to = EditorGUILayout.Slider("To hue", to, 0f, 360f);
                band = EditorGUILayout.Slider(new GUIContent("Reach", "How far from its own colour a hue may be and still be moved."), band, 0f, 180f);
            }
            if (!EditorGUI.EndChangeCheck()) return;
            set.use = use;
            set.factions = factions;
            if (weaponClass != first)
            {
                set.weaponClasses.Clear();
                if (weaponClass != WeaponClassId.None) set.weaponClasses.Add(weaponClass);
            }
            set.races = Words(races);
            set.moveHue = moveHue;
            set.hueFrom = from;
            set.hueTo = to;
            set.hueBand = band;
            Save();
            suggestionsFor = null;
        }

        private static List<string> Words(string text)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(text)) return words;
            foreach (string raw in text.Split(','))
            {
                string word = raw.Trim();
                if (word.Length > 0 && !words.Contains(word)) words.Add(word);
            }
            return words;
        }

        private void Save()
        {
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
        }
    }
}
