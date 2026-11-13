using System.IO;
using KryticalNova.EditorTools;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>The news gate (owner, 12 Nov 2026, after build 16 went live with no launcher News): no build goes out without its own news and patch notes.</summary>
    public class LauncherNewsGateTests
    {
        const string Item = "{\"title\":\"A Headline\",\"body\":\"What changed, in plain words.\"}";
        const string Notes = "[{\"heading\":\"New\",\"lines\":[\"One new thing.\"]},{\"heading\":\"Fixed\",\"lines\":[\"One fix.\"]}]";

        static string News(int build, string items = "[" + Item + "]", string notes = Notes) =>
            "{" + (build > 0 ? "\"build\":" + build + "," : "") + "\"items\":" + items + ",\"history\":[]" + (notes != null ? ",\"patchNotes\":" + notes : "") + "}";

        [Test]
        public void NewsWrittenForThisBuild_Passes()
        {
            Assert.IsNull(LauncherNewsGate.Problem(News(17), 17));
        }

        [Test]
        public void EmptyOrBrokenFile_IsRefused()
        {
            StringAssert.Contains("empty", LauncherNewsGate.Problem("", 17));
            StringAssert.Contains("empty", LauncherNewsGate.Problem(null, 17));
            Assert.IsNotNull(LauncherNewsGate.Problem("{ not json", 17));
        }

        [Test]
        public void NoHeadlines_IsRefused()
        {
            StringAssert.Contains("no headlines", LauncherNewsGate.Problem(News(17, "[]"), 17));
            StringAssert.Contains("no headlines", LauncherNewsGate.Problem(News(17, "[{\"title\":\"Only a title\",\"body\":\"\"}]"), 17));
        }

        [Test]
        public void TooManyHeadlines_IsRefused()
        {
            string eight = "[" + string.Join(",", System.Linq.Enumerable.Repeat(Item, 8)) + "]";
            StringAssert.Contains("7 at most", LauncherNewsGate.Problem(News(17, eight), 17));
        }

        [Test]
        public void NoPatchNotes_IsRefused()
        {
            StringAssert.Contains("no patch notes", LauncherNewsGate.Problem(News(17, notes: null), 17));
            StringAssert.Contains("no patch notes", LauncherNewsGate.Problem(News(17, notes: "[]"), 17));
            StringAssert.Contains("no patch notes", LauncherNewsGate.Problem(News(17, notes: "[{\"heading\":\"New\",\"lines\":[]}]"), 17));
        }

        [Test]
        public void NewsOfAnEarlierBuild_IsRefused()
        {
            // Builds 15 and 16: the news file had not changed since build 14. Left-over news must never go out again.
            StringAssert.Contains("written for build 16, not for build 17", LauncherNewsGate.Problem(News(16), 17));
            StringAssert.Contains("says no \"build\"", LauncherNewsGate.Problem(News(0), 17));
        }

        [Test]
        public void StagedLine_MustCarryNewsAndPatchNotes()
        {
            const string ok = "STAGED build=17 files=94 new=2 newBytes=10 downloadBytes=20 exe=KryticalNovaII.exe news=7 patchNotes=40";
            Assert.IsNull(LauncherNewsGate.StagedProblem("dotnet noise\n" + ok + "\n", 17));
            // Build 16's own line, 12 Nov 2026.
            StringAssert.Contains("news=0", LauncherNewsGate.StagedProblem(
                "STAGED build=16 files=94 new=58 newBytes=1747859307 downloadBytes=2021556509 exe=KryticalNovaII.exe news=0 patchNotes=0", 16));
            StringAssert.Contains("patchNotes=0", LauncherNewsGate.StagedProblem(ok.Replace("patchNotes=40", "patchNotes=0"), 17));
            StringAssert.Contains("not build 18", LauncherNewsGate.StagedProblem(ok, 18));
            StringAssert.Contains("no STAGED line", LauncherNewsGate.StagedProblem("Unhandled exception", 17));
            StringAssert.Contains("no STAGED line", LauncherNewsGate.StagedProblem(null, 17));
        }

        [Test]
        public void EveryPublisher_StagesWithNewsAndChecksTheStagedLine()
        {
            // The gate cannot be skipped: each publish path passes --news and reads the STAGED line back (the cutover's stage had no --news).
            foreach (string file in new[] { "CutoverPublisher.cs", "DotsChannelPublisher.cs", "ReleasePublisher.cs" })
            {
                string source = File.ReadAllText("Assets/_Game/Release/Editor/" + file);
                StringAssert.Contains("LauncherNewsGate.StageArgument()", source, file + " stages without --news");
                StringAssert.Contains("LauncherNewsGate.StagedProblem(", source, file + " does not check the STAGED line");
                StringAssert.Contains("LauncherNewsGate.FileProblem(", source, file + " does not check the news before building");
            }
        }

        [Test]
        public void CommittedNewsFile_HasHeadlinesPatchNotesAndItsBuild()
        {
            string json = File.ReadAllText(ReleasePublisher.NewsPath);
            int build = UnityEngine.JsonUtility.FromJson<BuildOnly>(json).build;
            Assert.Greater(build, 0, "the committed launcher news says which build it was written for");
            Assert.IsNull(LauncherNewsGate.Problem(json, build));
        }

        [System.Serializable] sealed class BuildOnly { public int build; }
    }
}
