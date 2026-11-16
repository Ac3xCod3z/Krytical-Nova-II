using System.Collections.Generic;
using KryticalNova.EditorTools;
using NUnit.Framework;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The server build's "no render pipeline" record (owner rule 16 Nov 2026, black-sky rule): what a server build writes before it
    /// switches the pipelines off must read back exactly, so a killed build's leftover is put back as it was before any game build.
    /// </summary>
    public class ServerRenderPipelineOffTests
    {
        [Test]
        public void TheRecord_ReadsBackAsWritten_IncludingNoPipeline()
        {
            var values = new Dictionary<string, string>
            {
                ["graphics"] = "",
                ["0"] = "5e6cbd92db86f4b18aec3ed561671858",
                ["1"] = "4b83569d67af61e458304325a23e5dfd",
            };
            Dictionary<string, string> back = ServerRenderPipelineOff.Parse(ServerRenderPipelineOff.Format(values));
            CollectionAssert.AreEquivalent(values, back);
        }

        [Test]
        public void TheRecord_IsTheSameWhateverTheOrder()
        {
            var a = new Dictionary<string, string> { ["1"] = "b", ["0"] = "a", ["graphics"] = "" };
            var b = new Dictionary<string, string> { ["graphics"] = "", ["0"] = "a", ["1"] = "b" };
            Assert.AreEqual(ServerRenderPipelineOff.Format(a), ServerRenderPipelineOff.Format(b));
        }

        [Test]
        public void AnEmptyOrBrokenRecord_PutsNothingBack()
        {
            Assert.IsEmpty(ServerRenderPipelineOff.Parse(""));
            Assert.IsEmpty(ServerRenderPipelineOff.Parse(null));
            Assert.AreEqual("", ServerRenderPipelineOff.Parse("0\n")["0"], "a line with no asset means no pipeline");
        }

        [Test]
        public void ThisProject_HasAPipelineOutsideAServerBuild()
        {
            // the guard every game build runs: with no record left, the project's own settings name URP
            Assert.IsFalse(System.IO.File.Exists(ServerRenderPipelineOff.RecordPath), "a server build's record is left in Library");
            Assert.IsTrue(ServerRenderPipelineOff.AnyPipeline());
        }
    }
}
