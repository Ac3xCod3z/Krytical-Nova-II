using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The news gate (owner, 12 Nov 2026, after builds 15 and 16 went live with no launcher News and no patch notes): every publish
    /// (<see cref="ReleasePublisher"/>, <see cref="CutoverPublisher"/>, <see cref="DotsChannelPublisher"/>) stages with
    /// <c>--news</c>, and refuses, like the bundle audit, when the news file (<see cref="ReleasePublisher.NewsPath"/>) is missing, has
    /// no headlines, has no patch notes, or was not written for the build being made: its <c>"build"</c> must equal that build, so news
    /// left over from an earlier release never goes out again. After staging, the release tool's STAGED line must say
    /// <c>news&gt;0</c> and <c>patchNotes&gt;0</c> for that build, else nothing is uploaded. There is no switch to skip it.
    /// The release tool refuses the same on its own (<c>stage</c> without <c>--news</c>, or news of another build).
    /// </summary>
    public static class LauncherNewsGate
    {
        [Serializable] sealed class Item { public string title; public string body; }
        [Serializable] sealed class Section { public string heading; public List<string> lines; }
        [Serializable] sealed class News { public int build; public List<Item> items; public List<Section> patchNotes; }

        public const int MostItems = 7;

        // ------------------------------------------------------------------ rules (pure, tested)

        /// <summary>Null when <paramref name="newsJson"/> may go out with build <paramref name="build"/>; else why not, in plain words.</summary>
        public static string Problem(string newsJson, int build)
        {
            if (string.IsNullOrWhiteSpace(newsJson)) return "the launcher news file is empty";
            News news;
            try { news = JsonUtility.FromJson<News>(newsJson); }
            catch (Exception e) { return "the launcher news file is not valid JSON: " + e.Message; }
            if (news == null) return "the launcher news file could not be read";
            int items = 0;
            foreach (Item item in news.items ?? new List<Item>())
                if (item != null && !string.IsNullOrWhiteSpace(item.title) && !string.IsNullOrWhiteSpace(item.body)) items++;
            if (items == 0) return "the launcher news has no headlines (\"items\"): write what changed in this build";
            if ((news.items?.Count ?? 0) > MostItems) return $"the launcher news has {news.items.Count} headlines; the launcher shows {MostItems} at most";
            int lines = 0;
            foreach (Section section in news.patchNotes ?? new List<Section>())
                if (section?.lines != null) foreach (string line in section.lines) if (!string.IsNullOrWhiteSpace(line)) lines++;
            if (lines == 0) return "the launcher news has no patch notes (\"patchNotes\": New / Changed / Fixed): write every change of this build";
            if (news.build <= 0) return $"the launcher news says no \"build\": set \"build\": {build} once it is written for this build";
            if (news.build != build)
                return $"the launcher news was written for build {news.build}, not for build {build}: write this build's news (the old news moves into \"history\") and set \"build\": {build}";
            return null;
        }

        /// <summary>Null when the release tool's STAGED line (in <paramref name="stagedOutput"/>) shows build <paramref name="build"/> staged with news and patch notes.</summary>
        public static string StagedProblem(string stagedOutput, int build)
        {
            Match staged = Regex.Match(stagedOutput ?? "", @"STAGED build=(\d+)\b.*?\bnews=(\d+)\s+patchNotes=(\d+)");
            if (!staged.Success) return "the release tool printed no STAGED line with news and patch notes";
            if (int.Parse(staged.Groups[1].Value) != build) return $"the release tool staged build {staged.Groups[1].Value}, not build {build}";
            if (int.Parse(staged.Groups[2].Value) <= 0) return "the staged release carries no launcher news (news=0)";
            if (int.Parse(staged.Groups[3].Value) <= 0) return "the staged release carries no patch notes (patchNotes=0)";
            return null;
        }

        // ------------------------------------------------------------------ the file

        /// <summary>Null when the committed news file may go out with build <paramref name="build"/>; else why not.</summary>
        public static string FileProblem(int build)
        {
            if (!File.Exists(ReleasePublisher.NewsPath)) return "there is no launcher news file at " + ReleasePublisher.NewsPath;
            string problem = Problem(File.ReadAllText(ReleasePublisher.NewsPath), build);
            return problem == null ? null : problem + " (" + ReleasePublisher.NewsPath + ")";
        }

        /// <summary>The release tool's <c>--news</c> argument: always passed.</summary>
        public static string StageArgument() => $" --news \"{Path.GetFullPath(ReleasePublisher.NewsPath)}\"";
    }
}
