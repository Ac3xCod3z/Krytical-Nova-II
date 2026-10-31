using System;
using System.Collections.Generic;
using System.Linq;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// What is on and off in a map, object by object: a GameObject's active box by its hierarchy path, a component's enabled
    /// box by its path and type. The same shape is filled from a scene in the Editor and from the dump of its built bundle,
    /// so the publish gate can name every difference (and catch a swap, one thing on and another off, that counts miss).
    /// A name used twice under one parent shares its entry: the entry holds one flag for each of them.
    /// </summary>
    public sealed class ObjectStates
    {
        public const string BehaviourPrefix = "MB:";

        /// <summary>GameObject path (Root/Child/Grandchild) to the active flag of each object of that path.</summary>
        public readonly Dictionary<string, List<bool>> Objects = new Dictionary<string, List<bool>>(StringComparer.Ordinal);

        /// <summary>"path|type" to the enabled flag of each such component. A script is typed "MB:" and its field signature.</summary>
        public readonly Dictionary<string, List<bool>> Components = new Dictionary<string, List<bool>>(StringComparer.Ordinal);

        /// <summary>A script's signature to its class name, known only from the scene side (the bundle has no class names).</summary>
        public readonly Dictionary<string, string> ScriptNames = new Dictionary<string, string>(StringComparer.Ordinal);

        public void AddObject(string path, bool active) => Add(Objects, path, active);

        public void AddComponent(string path, string type, bool enabled) => Add(Components, path + "|" + type, enabled);

        private static void Add(Dictionary<string, List<bool>> map, string key, bool flag)
        {
            if (!map.TryGetValue(key, out List<bool> list)) map[key] = list = new List<bool>();
            list.Add(flag);
        }
    }

    /// <summary>Builds <see cref="ObjectStates"/> from a bundle dump, and lists what differs from a scene's (pure, tested without Unity).</summary>
    public static class ObjectCompare
    {
        public static ObjectStates FromDump(IList<DumpObject> objects)
        {
            var goName = new Dictionary<long, string>();
            var transformOfGo = new Dictionary<long, long>();
            var transforms = new Dictionary<long, DumpObject>();
            foreach (DumpObject o in objects)
            {
                if (o.ClassId == 1) goName[o.Id] = o.Name ?? "";
                else if (o.ClassId == 4 || o.ClassId == 224) { transforms[o.Id] = o; transformOfGo[o.OwnerId] = o.Id; }
            }

            var paths = new Dictionary<long, string>();
            string PathOf(long go)
            {
                if (paths.TryGetValue(go, out string known)) return known;
                var names = new List<string>();
                long current = go;
                for (int depth = 0; depth < 256 && goName.TryGetValue(current, out string name); depth++)
                {
                    names.Add(name);
                    if (!transformOfGo.TryGetValue(current, out long transform) || !transforms.TryGetValue(transform, out DumpObject t)) break;
                    if (t.FatherId == 0 || !transforms.TryGetValue(t.FatherId, out DumpObject father)) break;
                    current = father.OwnerId;
                }
                names.Reverse();
                return paths[go] = string.Join("/", names);
            }

            var states = new ObjectStates();
            foreach (DumpObject o in objects)
            {
                if (o.ClassId == 1) states.AddObject(PathOf(o.Id), o.Active != 0);
                else if (o.Enabled >= 0 && goName.ContainsKey(o.OwnerId))
                    states.AddComponent(PathOf(o.OwnerId), o.ClassId == BundleDump.MonoBehaviourClassId ? ObjectStates.BehaviourPrefix + o.Signature : o.ClassName, o.Enabled != 0);
            }
            return states;
        }

        /// <summary>
        /// Every object or component the bundle has in another state than the scene, one plain line each (at most <paramref name="limit"/>,
        /// then a count of the rest). Objects only the scene has are not listed: editor-only objects are left out of a build.
        /// </summary>
        public static List<string> Compare(string map, ObjectStates scene, ObjectStates bundle, int limit = 25)
        {
            var lines = new List<string>();
            foreach (KeyValuePair<string, List<bool>> entry in bundle.Objects)
            {
                scene.Objects.TryGetValue(entry.Key, out List<bool> inScene);
                int bundleOff = entry.Value.Count(f => !f), sceneOff = inScene == null ? 0 : inScene.Count(f => !f);
                if (inScene == null && bundleOff == 0) continue;
                if (bundleOff == sceneOff) continue;
                lines.Add(bundleOff > sceneOff
                    ? $"{map}: '{entry.Key}' is INACTIVE in the built bundle but {(inScene == null ? "not in the scene" : "active in the scene")}."
                    : $"{map}: '{entry.Key}' is ACTIVE in the built bundle but inactive in the scene.");
            }
            foreach (KeyValuePair<string, List<bool>> entry in bundle.Components)
            {
                scene.Components.TryGetValue(entry.Key, out List<bool> inScene);
                int bundleOff = entry.Value.Count(f => !f), sceneOff = inScene == null ? 0 : inScene.Count(f => !f);
                if (inScene == null && bundleOff == 0) continue;
                if (bundleOff == sceneOff) continue;
                int bar = entry.Key.LastIndexOf('|');
                string path = entry.Key.Substring(0, bar), type = entry.Key.Substring(bar + 1);
                string what = Describe(type, scene);
                lines.Add(bundleOff > sceneOff
                    ? $"{map}: {what} on '{path}' is SWITCHED OFF in the built bundle but {(inScene == null ? "not in the scene" : "on in the scene")}."
                    : $"{map}: {what} on '{path}' is SWITCHED ON in the built bundle but off in the scene.");
            }
            lines.Sort(StringComparer.Ordinal);
            if (lines.Count > limit)
            {
                int more = lines.Count - limit;
                lines.RemoveRange(limit, more);
                lines.Add($"{map}: ... and {more} more difference(s).");
            }
            return lines;
        }

        private static string Describe(string type, ObjectStates scene)
        {
            if (!type.StartsWith(ObjectStates.BehaviourPrefix, StringComparison.Ordinal)) return type;
            string signature = type.Substring(ObjectStates.BehaviourPrefix.Length);
            if (scene.ScriptNames.TryGetValue(signature, out string name)) return name;
            return "a script with the fields (" + (signature.Length > 40 ? signature.Substring(0, 40) + "..." : signature) + ")";
        }
    }
}
