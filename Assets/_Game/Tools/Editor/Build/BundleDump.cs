using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KryticalNova.EditorTools
{
    /// <summary>One object of a built scene bundle as Unity's binary2text tool prints it (only what the publish gate reads).</summary>
    public sealed class DumpObject
    {
        public long Id;
        public int ClassId;
        public string ClassName;
        /// <summary>The top-level m_Enabled (1 on, 0 off), or -1 when the object has none.</summary>
        public int Enabled = -1;
        /// <summary>A GameObject's m_IsActive (1 or 0), or -1.</summary>
        public int Active = -1;
        public string Name;
        /// <summary>For a component: the GameObject it sits on (m_GameObject's path id).</summary>
        public long OwnerId;
        /// <summary>For a Transform: the Transform it hangs under (m_Father's path id), 0 at the root.</summary>
        public long FatherId;
        /// <summary>The component's own serialized field names, standard ones left out: tells scripts apart without the script.</summary>
        public string Signature;
    }

    /// <summary>What a map holds that is switched off: the same numbers read from a scene in the Editor and from its built bundle.</summary>
    public sealed class MapCounts
    {
        /// <summary>MonoBehaviours with the enabled box off.</summary>
        public int DisabledBehaviours;
        /// <summary>GameObjects that are not active themselves.</summary>
        public int InactiveObjects;
        /// <summary>The switched-off MonoBehaviours by signature: how many, and a readable name (a class name, or an owner's name for a bundle).</summary>
        public readonly Dictionary<string, KeyValuePair<string, int>> OffBySignature = new Dictionary<string, KeyValuePair<string, int>>();

        public void AddOff(string signature, string name)
        {
            OffBySignature.TryGetValue(signature, out KeyValuePair<string, int> now);
            OffBySignature[signature] = new KeyValuePair<string, int>(now.Key ?? name, now.Value + 1);
        }
    }

    /// <summary>
    /// Reads binary2text's text dump of a scene bundle and compares it with the scene it was built from (the publish gate,
    /// <see cref="BundleAuditGate"/>). Pure text work, so it is tested without Unity's tools.
    /// </summary>
    public static class BundleDump
    {
        private static readonly Regex ObjectLine = new Regex(@"^ID: (-?\d+) \(ClassID: (\d+)\) (.*)$", RegexOptions.Compiled);
        private static readonly HashSet<string> StandardFields = new HashSet<string>
        {
            "m_ObjectHideFlags", "m_CorrespondingSourceObject", "m_PrefabInstance", "m_PrefabAsset", "m_GameObject",
            "m_Enabled", "m_EditorHideFlags", "m_Script", "m_Name", "m_EditorClassIdentifier",
        };

        public const int MonoBehaviourClassId = 114;

        /// <summary>A component's signature: its own field names, sorted, standard ones left out.</summary>
        public static string SignatureOf(IEnumerable<string> fieldNames) =>
            string.Join(",", fieldNames.Where(n => !StandardFields.Contains(n)).OrderBy(n => n, StringComparer.Ordinal));

        public static List<DumpObject> Parse(IEnumerable<string> lines)
        {
            var objects = new List<DumpObject>();
            DumpObject current = null;
            List<string> fields = null;
            string lastTop = null;
            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                Match header = ObjectLine.Match(line);
                if (header.Success)
                {
                    Finish(current, fields);
                    current = new DumpObject { Id = long.Parse(header.Groups[1].Value), ClassId = int.Parse(header.Groups[2].Value), ClassName = header.Groups[3].Value.Trim() };
                    fields = new List<string>();
                    objects.Add(current);
                    lastTop = null;
                    continue;
                }
                if (current == null || line.Length < 2) continue;
                if (line[0] == '\t' && line[1] != '\t')
                {
                    string field = line.Substring(1);
                    int space = field.IndexOf(' ');
                    string name = space < 0 ? field : field.Substring(0, space);
                    fields.Add(name);
                    lastTop = name;
                    if (name == "m_Enabled") current.Enabled = FirstInt(field);
                    else if (name == "m_IsActive") current.Active = FirstInt(field);
                    else if (name == "m_Name") current.Name = Quoted(field);
                }
                else if (lastTop == "m_GameObject" && line.StartsWith("\t\tm_PathID ", StringComparison.Ordinal))
                {
                    current.OwnerId = FirstLong(line.Substring(2));
                }
                else if (lastTop == "m_Father" && line.StartsWith("\t\tm_PathID ", StringComparison.Ordinal))
                {
                    current.FatherId = FirstLong(line.Substring(2));
                }
            }
            Finish(current, fields);
            return objects;
        }

        private static void Finish(DumpObject o, List<string> fields)
        {
            if (o != null && o.ClassId == MonoBehaviourClassId) o.Signature = SignatureOf(fields);
        }

        /// <summary>What a scene bundle's objects say is switched off. <paramref name="ownerNames"/> names the owners of switched-off components.</summary>
        public static MapCounts Count(IEnumerable<DumpObject> objects)
        {
            var list = objects as IList<DumpObject> ?? objects.ToList();
            var names = new Dictionary<long, string>();
            foreach (DumpObject o in list) if (o.ClassId == 1) names[o.Id] = o.Name ?? "";
            var counts = new MapCounts();
            foreach (DumpObject o in list)
            {
                if (o.ClassId == 1 && o.Active == 0) counts.InactiveObjects++;
                if (o.ClassId != MonoBehaviourClassId || o.Enabled != 0) continue;
                counts.DisabledBehaviours++;
                counts.AddOff(o.Signature ?? "", names.TryGetValue(o.OwnerId, out string owner) ? "on '" + owner + "'" : "");
            }
            return counts;
        }

        /// <summary>
        /// Plain sentences for everything the bundle has switched off that the scene has on. Fewer off in the bundle than in the
        /// scene is not a problem (editor-only objects are left out of a build). Empty when the bundle is as the scene.
        /// </summary>
        public static List<string> Compare(string map, MapCounts scene, MapCounts bundle)
        {
            var problems = new List<string>();
            if (bundle.DisabledBehaviours > scene.DisabledBehaviours)
            {
                var kinds = new List<string>();
                foreach (KeyValuePair<string, KeyValuePair<string, int>> off in bundle.OffBySignature)
                {
                    scene.OffBySignature.TryGetValue(off.Key, out KeyValuePair<string, int> inScene);
                    int extra = off.Value.Value - inScene.Value;
                    if (extra <= 0) continue;
                    string what = inScene.Key ?? ("a component " + off.Value.Key);
                    kinds.Add(extra + " x " + what);
                }
                problems.Add($"{map}: {bundle.DisabledBehaviours - scene.DisabledBehaviours} more component(s) are switched off in the built bundle than in the scene " +
                             $"({bundle.DisabledBehaviours} against {scene.DisabledBehaviours})" + (kinds.Count > 0 ? ": " + string.Join(", ", kinds) : "") + ".");
            }
            if (bundle.InactiveObjects > scene.InactiveObjects)
                problems.Add($"{map}: {bundle.InactiveObjects - scene.InactiveObjects} more object(s) are inactive in the built bundle than in the scene ({bundle.InactiveObjects} against {scene.InactiveObjects}).");
            return problems;
        }

        private static int FirstInt(string field) => (int)FirstLong(field);

        private static long FirstLong(string field)
        {
            string[] parts = field.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 && long.TryParse(parts[1], out long value) ? value : 0;
        }

        private static string Quoted(string field)
        {
            int a = field.IndexOf('"'), b = field.LastIndexOf('"');
            return a >= 0 && b > a ? field.Substring(a + 1, b - a - 1) : "";
        }
    }
}
