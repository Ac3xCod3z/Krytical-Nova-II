using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The icon packs the library draws from, and the two jobs that keep the library honest: listing what the packs
    /// hold (<see cref="Scan"/>) and checking that every skill and item has an icon of its own (<see cref="Check"/>).
    /// The packs are third-party and are only ever read.
    /// </summary>
    public static class IconPacks
    {
        /// <summary>Each pack: the name its sets are filed under, and the folder that holds one sub-folder per set.</summary>
        public static readonly (string name, string root)[] Packs =
        {
            ("Fantasy", "Assets/_ThirdParty/Fantasy RPG Icons Pack/Asset"),
            ("Cyberpunk", "Assets/_ThirdParty/Cyberpunk RPG Icons Pack/Asset"),
        };

        // The variant of a set that is used, in order of preference: a full painting, else the cut-out, else what there is.
        private static readonly (string folder, ComicIconShape shape)[] Variants =
        {
            ("PNG", ComicIconShape.Square), ("No Background", ComicIconShape.CutOut), ("Standard", ComicIconShape.Square),
        };

        /// <summary>
        /// Brings the library's list of sets up to date with the packs in the project. A set already listed keeps
        /// its tags; only its folder, shape and count are refreshed. Returns how many sets are new.
        /// </summary>
        public static int Scan(IconLibrary library)
        {
            IconBaker.ForgetFolders();
            int added = 0;
            foreach ((string pack, string root) in Packs)
            {
                if (!Directory.Exists(root))
                {
                    Debug.LogWarning($"[Icons] The {pack} icon pack is not at {root}: its sets are left as they are.");
                    continue;
                }
                string[] categories = Directory.GetDirectories(root);
                System.Array.Sort(categories, System.StringComparer.Ordinal);
                foreach (string category in categories)
                {
                    string folder = null;
                    ComicIconShape shape = ComicIconShape.Square;
                    foreach ((string variant, ComicIconShape variantShape) in Variants)
                    {
                        string candidate = category.Replace('\\', '/') + "/" + variant;
                        if (!Directory.Exists(candidate)) continue;
                        folder = candidate;
                        shape = variantShape;
                        break;
                    }
                    if (folder == null) continue;
                    int count = 0;
                    foreach (int number in IconBaker.FilesOf(folder).Keys)
                        if (number > count) count = number;
                    if (count == 0) continue;

                    string id = pack + "/" + SetName(Path.GetFileName(category));
                    IconLibrary.Set set = library.FindSet(id);
                    if (set == null)
                    {
                        set = new IconLibrary.Set { id = id, use = UseByName(id), factions = IconFactions.All };
                        library.sets.Add(set);
                        added++;
                    }
                    set.folder = folder;
                    set.shape = shape;
                    set.count = count;
                }
            }
            library.sets.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            EditorUtility.SetDirty(library);
            return added;
        }

        /// <summary>
        /// The packs that are not in this copy of the project. They are large and the game does not need them: it
        /// shows the baked copies. Without a pack its icons cannot be baked again or given to anything new.
        /// </summary>
        public static List<string> Missing()
        {
            var missing = new List<string>();
            foreach ((string pack, string root) in Packs)
                if (!Directory.Exists(root)) missing.Add(pack);
            return missing;
        }

        private static bool IsFromMissingPack(string setId, List<string> missing)
        {
            foreach (string pack in missing)
                if (setId.StartsWith(pack + "/", System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>"Swordman Skill Icons" → "Swordman Skill"; "Resource Icons 2" → "Resource 2".</summary>
        public static string SetName(string category)
        {
            string name = category.Replace(" Icons", string.Empty).Replace(" Icon", string.Empty);
            while (name.Contains("  ")) name = name.Replace("  ", " ");
            return name.Trim();
        }

        private static IconUse UseByName(string id)
        {
            if (id.EndsWith("/Buff Skill")) return IconUse.Buff;
            if (id.EndsWith("/Debuff Skill")) return IconUse.Debuff;
            if (id.Contains(" Skill")) return IconUse.Attack;
            return IconUse.Item;
        }

        /// <summary>
        /// Everything that is wrong with the game's icons, a line each: a skill or item with no icon in the library,
        /// an icon given twice, a pack file that is missing, or a skill or item that is not showing the icon the
        /// library gave it. An empty list is what is wanted. A whole pack that is not in this copy of the project
        /// (<see cref="Missing"/>) is not a problem of the icons: its files are then not looked for.
        /// </summary>
        public static List<string> Check(IconLibrary library)
        {
            var problems = new List<string>(library.Duplicates());
            var targets = new List<IconTarget>(IconTargets.Skills());
            targets.AddRange(IconTargets.Items());
            var known = new HashSet<(IconTargetKind, string)>();
            List<string> missing = Missing();
            foreach (IconTarget target in targets)
            {
                known.Add((target.kind, target.id));
                IconLibrary.Assignment assignment = library.AssignmentOf(target.kind, target.id);
                if (assignment == null)
                {
                    problems.Add($"{target.name} ({target.id}) has no icon in the library.");
                    continue;
                }
                IconLibrary.Set set = library.FindSet(assignment.set);
                if (set == null) { problems.Add($"{target.name}: the library has no set '{assignment.set}'."); continue; }
                if (!IsFromMissingPack(set.id, missing) && IconBaker.FileOf(set, assignment.number) == null)
                {
                    problems.Add($"{target.name}: {assignment.set} {assignment.number:00} is not in the pack's folder ({set.folder}).");
                    continue;
                }
                string baked = IconBaker.OutputPath(target.kind, target.id);
                string shown = target.Icon != null ? AssetDatabase.GetAssetPath(target.Icon) : null;
                if (shown != baked) problems.Add($"{target.name} shows {(shown ?? "no icon")}, not its baked icon {baked}. Bake the icons.");
            }
            foreach (IconLibrary.Assignment assignment in library.assignments)
                if (assignment != null && !known.Contains((assignment.kind, assignment.target)))
                    problems.Add($"The library has an icon for '{assignment.target}', but there is no such {assignment.kind} in the project.");
            return problems;
        }

        [MenuItem("Krytical Nova/Icons/Bake Every Assigned Icon")]
        public static void BakeEverything()
        {
            IconLibrary library = IconBaker.Library();
            int baked = IconBaker.BakeAll(library, IconBaker.Settings());
            Debug.Log($"[Icons] Baked {baked} icon(s) from the library into {IconBaker.SkillIconFolder} and {IconBaker.ItemIconFolder}.");
        }

        [MenuItem("Krytical Nova/Icons/Check Icons")]
        public static void CheckEverything()
        {
            List<string> problems = Check(IconBaker.Library());
            foreach (string pack in Missing())
                Debug.LogWarning($"[Icons] The {pack} icon pack is not in this copy of the project: its icons show as baked, but cannot be baked again or given to anything new.");
            if (problems.Count == 0)
            {
                Debug.Log("[Icons] Every skill and every item without a model has an icon of its own, and shows it.");
                return;
            }
            foreach (string problem in problems) Debug.LogWarning("[Icons] " + problem);
            Debug.LogWarning($"[Icons] {problems.Count} thing(s) to put right. Open Krytical Nova ▸ Icons ▸ Icon Library.");
        }
    }
}
