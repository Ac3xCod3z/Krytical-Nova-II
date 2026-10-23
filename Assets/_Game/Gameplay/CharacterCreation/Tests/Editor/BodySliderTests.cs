using System;
using KryticalNova.CharacterCreation;
using KryticalNova.Factions;
using KryticalNova.Items.Visuals;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The body sliders of the recipe (22 Oct 2026): a look from before them keeps its body, and they travel.</summary>
    public sealed class BodySliderTests
    {
        private static CharacterRecipe Look(BodyType bodyType) => new CharacterRecipe
        {
            version = CharacterRecipe.CurrentVersion, faction = FactionId.Hegemony, lineage = 1, bodyType = bodyType,
            build = 10, muscle = 20, palette = 3, parts = new ushort[] { 101 }, colors = new ColorOverride[0],
            bust = 70, butt = 40, defined = 90, height = 100
        };

        [Serializable]
        private sealed class Holder
        {
            public CharacterRecipe recipe;
        }

        [Test]
        public void ALookFromBeforeTheSlidersKeepsItsBody()
        {
            // Saved by an older game: version 1, no slider fields at all.
            const string json = "{\"recipe\":{\"version\":1,\"faction\":1,\"lineage\":1,\"bodyType\":1,\"build\":10,\"muscle\":20,\"parts\":[101],\"palette\":3}}";
            CharacterRecipe old = JsonUtility.FromJson<Holder>(json).recipe;
            Assert.That(old.Height, Is.EqualTo(CharacterRecipe.DefaultHeight));
            Assert.That(old.HeightScale, Is.EqualTo(1f).Within(1e-5f));
            BodyShapeWeights shape = old.ToShape();
            Assert.That(shape.bust + shape.butt + shape.defined, Is.EqualTo(0f));

            old.Upgrade();
            Assert.That(old.version, Is.EqualTo(CharacterRecipe.CurrentVersion));
            Assert.That(old.height, Is.EqualTo(CharacterRecipe.DefaultHeight));
            Assert.That(old.HeightScale, Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void HeightRunsFromNinetyToOneHundredTenPercent()
        {
            CharacterRecipe look = Look(BodyType.Masculine);
            look.height = 0;
            Assert.That(look.HeightScale, Is.EqualTo(0.90f).Within(1e-5f));
            look.height = 50;
            Assert.That(look.HeightScale, Is.EqualTo(1.00f).Within(1e-5f));
            look.height = 100;
            Assert.That(look.HeightScale, Is.EqualTo(1.10f).Within(1e-5f));
        }

        [Test]
        public void EachBodyTypeWearsOnlyItsOwnSliders()
        {
            BodyShapeWeights feminine = Look(BodyType.Feminine).ToShape();
            Assert.That(feminine.bust, Is.EqualTo(70f));
            Assert.That(feminine.butt, Is.EqualTo(40f));
            Assert.That(feminine.defined, Is.EqualTo(0f));
            BodyShapeWeights masculine = Look(BodyType.Masculine).ToShape();
            Assert.That(masculine.bust + masculine.butt, Is.EqualTo(0f));
            Assert.That(masculine.defined, Is.EqualTo(90f));
            Assert.That(masculine.WeightFor("10TORSBlends.kn_defined"), Is.EqualTo(90f));
            Assert.That(feminine.WeightFor("17HIPSBlends.kn_butt"), Is.EqualTo(40f));
        }

        [Test]
        public void ChangingBodyTypeKeepsOnlyTheSlidersTheNewBodyHas()
        {
            CharacterRecipe masculine = Look(BodyType.Feminine);
            masculine.bodyType = BodyType.Masculine;
            masculine.CopyBodyFrom(Look(BodyType.Feminine));
            Assert.That(masculine.bust, Is.EqualTo(0));
            Assert.That(masculine.butt, Is.EqualTo(0));
            Assert.That(masculine.defined, Is.EqualTo(0));
            Assert.That(masculine.height, Is.EqualTo(100));
            Assert.That(masculine.build, Is.EqualTo(10));
        }

        [Test]
        public void TheSlidersTravelOverTheNetwork()
        {
            CharacterRecipe look = Look(BodyType.Feminine);
            CharacterNetState sent = CharacterNetState.From("Ayla", look);
            using var writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteNetworkSerializable(sent);
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            reader.ReadNetworkSerializable(out CharacterNetState received);
            Assert.That(received.Equals(sent));
            CharacterRecipe back = received.ToRecipe();
            Assert.That(back.bust, Is.EqualTo(70));
            Assert.That(back.butt, Is.EqualTo(40));
            Assert.That(back.defined, Is.EqualTo(90));
            Assert.That(back.height, Is.EqualTo(100));
            Assert.That(back.Equals(look));
        }

        [Test]
        public void TheSlidersChangeTheLook()
        {
            CharacterRecipe a = Look(BodyType.Feminine), b = Look(BodyType.Feminine);
            b.height = 20;
            Assert.That(a.Equals(b), Is.False);
            Assert.That(RestyleRules.Same(a, b), Is.False);
            Assert.That(CharacterNetState.From("A", a).Equals(CharacterNetState.From("A", b)), Is.False);
        }
    }
}
