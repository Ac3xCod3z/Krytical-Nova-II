using NUnit.Framework;

namespace KryticalNova.Networking.Tests
{
    /// <summary>
    /// Writing server-settings.json from NGO's fields must keep the keys only the entity server knows (netcode, plainTransport):
    /// DOTS builds, 8 Nov 2026, the control page made its password and the entity test server then refused to start.
    /// </summary>
    public sealed class SettingsKeepKeysTests
    {
        [Test]
        public void KeysTheNewTextLacksComeBackWithTheirValues()
        {
            string old = "{\n  \"netcode\": \"entities\",\n  \"port\": 7799,\n  \"plainTransport\": true,\n  \"mapServers\": [{\"a\": \"x,}\"}]\n}";
            string now = "{\n    \"port\": 7799,\n    \"pagePassword\": \"p\"\n}";
            string merged = SettingsKeepKeys.Merge(old, now);
            StringAssert.Contains("\"netcode\": \"entities\"", merged);
            StringAssert.Contains("\"plainTransport\": true", merged);
            StringAssert.Contains("\"mapServers\"", merged, "a key the new text lacks");
            StringAssert.Contains("\"pagePassword\": \"p\"", merged);
            Assert.AreEqual(1, Count(merged, "\"port\""), "a key both have is not doubled");
            Assert.IsTrue(merged.TrimEnd().EndsWith("}"));
        }

        [Test]
        public void TheNewValueWinsOverTheOldOne()
        {
            string merged = SettingsKeepKeys.Merge("{\"port\": 1, \"plainTransport\": true}", "{\"port\": 2}");
            StringAssert.Contains("\"port\": 2", merged);
            StringAssert.DoesNotContain("\"port\": 1", merged);
            StringAssert.Contains("\"plainTransport\": true", merged);
        }

        [Test]
        public void NothingToKeepLeavesTheNewTextAsItWas()
        {
            string now = "{\n  \"port\": 7799\n}";
            Assert.AreEqual(now, SettingsKeepKeys.Merge("{\"port\": 1}", now));
            Assert.AreEqual(now, SettingsKeepKeys.Merge(null, now));
            Assert.AreEqual(now, SettingsKeepKeys.Merge("not json", now));
        }

        [Test]
        public void AnEmptyNewObjectTakesTheOldKeys()
        {
            string merged = SettingsKeepKeys.Merge("{\"netcode\": \"entities\"}", "{}");
            StringAssert.Contains("\"netcode\": \"entities\"", merged);
            StringAssert.DoesNotContain("{,", merged);
        }

        static int Count(string text, string part)
        {
            int count = 0, at = 0;
            while ((at = text.IndexOf(part, at, System.StringComparison.Ordinal)) >= 0) { count++; at += part.Length; }
            return count;
        }
    }
}
