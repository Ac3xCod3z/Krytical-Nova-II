using System;
using System.Text.RegularExpressions;

namespace KryticalNova.Core
{
    /// <summary>
    /// A saved document whose shape has changed since it was first saved (online phase 4: save migrations). A save
    /// participant (<see cref="ISaveParticipant"/>) or a world document implements it when, and only when, a change to
    /// what it saves would make an older save read wrongly: a field renamed, split or given another meaning or unit.
    /// Adding or removing a field needs no version: Unity's JSON reader fills in what is missing and skips what it does
    /// not know. Each step is a pure function of the text and is tested; a copy of real saves is run through every step
    /// before a release ships (Krytical Nova ▸ Online ▸ Saves ▸ Check Saves Against This Build).
    /// </summary>
    public interface IMigratingSave
    {
        /// <summary>The version this build saves. 1 is a document saved before it had a version.</summary>
        int SaveVersion { get; }

        /// <summary>The document one step up: from version <paramref name="from"/> to <paramref name="from"/> + 1.</summary>
        string Migrate(string json, int from);
    }

    /// <summary>
    /// How a document's version travels: as a first field <c>"_v"</c> in its own text, written only from version 2 on.
    /// Readers that do not know it skip it, so stamping a document changes nothing for anything that reads it.
    /// </summary>
    public static class SaveVersions
    {
        public const string Field = "_v";
        private static readonly Regex Stamp = new Regex("^\\s*\\{\\s*\"_v\"\\s*:\\s*(\\d+)\\s*,?", RegexOptions.CultureInvariant);

        /// <summary>The version a saved text says it is: 1 when it carries none (saved before it had a version).</summary>
        public static int Of(string json)
        {
            if (string.IsNullOrEmpty(json)) return 1;
            Match m = Stamp.Match(json);
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) && v > 0 ? v : 1;
        }

        /// <summary>The text with its version stamp set to <paramref name="version"/> (version 1 carries none).</summary>
        public static string With(string json, int version)
        {
            if (string.IsNullOrEmpty(json)) return json;
            string bare = Without(json);
            if (version <= 1) return bare;
            int brace = bare.IndexOf('{');
            if (brace < 0) return bare; // not an object: left as it is
            string rest = bare.Substring(brace + 1).TrimStart();
            return bare.Substring(0, brace) + "{\"" + Field + "\":" + version + (rest.StartsWith("}") ? "" : ",") + rest;
        }

        /// <summary>The text without its version stamp.</summary>
        public static string Without(string json)
        {
            if (string.IsNullOrEmpty(json)) return json;
            Match m = Stamp.Match(json);
            if (!m.Success) return json;
            int brace = json.IndexOf('{');
            return json.Substring(0, brace + 1) + json.Substring(m.Length);
        }

        /// <summary>
        /// A saved text stepped up to <paramref name="by"/>'s current version and stamped with it. Throws when a step fails or
        /// the text is newer than this build knows (a save made by a newer build): the caller must then leave it untouched.
        /// </summary>
        public static string Upgrade(string json, IMigratingSave by)
        {
            if (string.IsNullOrEmpty(json) || by == null) return json;
            int current = by.SaveVersion, at = Of(json);
            if (at > current)
                throw new InvalidOperationException($"saved by a newer build (version {at}; this build knows up to {current})");
            string text = Without(json);
            for (; at < current; at++)
            {
                text = by.Migrate(text, at);
                if (string.IsNullOrEmpty(text) || text.TrimStart()[0] != '{')
                    throw new InvalidOperationException($"the step from version {at} gave no document");
                text = Without(text);
            }
            return With(text, current);
        }

        /// <summary>The text as this build saves it: stamped with <paramref name="by"/>'s version when it has one.</summary>
        public static string Stamped(string json, object by) =>
            by is IMigratingSave migrating ? With(json, migrating.SaveVersion) : json;
    }
}
