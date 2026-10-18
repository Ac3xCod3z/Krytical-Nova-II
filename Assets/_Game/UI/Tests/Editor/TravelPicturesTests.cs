using System.Collections.Generic;
using KryticalNova.World;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>The travel screen draws its pictures at random from the whole catalog and never shows one twice in a row.</summary>
    public sealed class TravelPicturesTests
    {
        private readonly List<Object> made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object each in made) Object.DestroyImmediate(each);
            made.Clear();
        }

        private Texture2D Picture(string name)
        {
            var texture = new Texture2D(4, 4) { name = name };
            made.Add(texture);
            return texture;
        }

        private ZoneCatalog Catalog(out Texture2D[] all)
        {
            var catalog = ScriptableObject.CreateInstance<ZoneCatalog>();
            made.Add(catalog);
            Texture2D north = Picture("north"), south = Picture("south"), extra = Picture("extra");
            catalog.regions.Add(new ZoneRegion { name = "North", art = north });
            catalog.regions.Add(new ZoneRegion { name = "South", art = south });
            catalog.regions.Add(new ZoneRegion { name = "Bare" });
            catalog.extraPictures.Add(extra);
            catalog.extraPictures.Add(north); // listed twice: still one picture
            catalog.extraPictures.Add(null);
            all = new[] { north, south, extra };
            return catalog;
        }

        [Test]
        public void ThePicturesAreEveryRegionsPaintingAndTheExtras_EachOnce()
        {
            ZoneCatalog catalog = Catalog(out Texture2D[] all);
            CollectionAssert.AreEqual(all, catalog.Pictures());
        }

        [Test]
        public void AShuffleKeepsEveryPicture_AndNeverOpensOnTheLastOne()
        {
            ZoneCatalog catalog = Catalog(out Texture2D[] all);
            List<Texture2D> pictures = catalog.Pictures();
            var random = new System.Random(1);
            var openers = new HashSet<Texture2D>();
            Texture2D last = null;
            for (int trip = 0; trip < 200; trip++)
            {
                List<Texture2D> order = ZoneCatalog.Shuffled(pictures, last, random);
                CollectionAssert.AreEquivalent(all, order);
                Assert.AreNotSame(last, order[0], $"trip {trip} opened on the picture the last one ended on");
                openers.Add(order[0]);
                last = order[order.Count - 1];
            }
            Assert.AreEqual(all.Length, openers.Count, "every picture gets to open a trip");
        }

        [Test]
        public void ASinglePictureIsAllThereIs()
        {
            Texture2D only = Picture("only");
            CollectionAssert.AreEqual(new[] { only }, ZoneCatalog.Shuffled(new[] { only }, only, new System.Random(3)));
            Assert.IsEmpty(ZoneCatalog.Shuffled(new Texture2D[0], null, new System.Random(3)));
        }
    }
}
