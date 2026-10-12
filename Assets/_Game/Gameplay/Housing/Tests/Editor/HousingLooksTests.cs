using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Housing;
using KryticalNova.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The rules that came with the room inside a Hall: what a Hall wears, what hangs on a wall or from a ceiling,
    /// how large a piece may be made; the world's measure; and the built catalog held to all of it.
    /// </summary>
    public sealed class HousingLooksTests
    {
        private const ushort StandardWall = 29201, Marble = 29202, Timber = 29204, NullBrick = 29102, NullGate = 29521, Painting = 26070, Lamp = 26043, Chair = 26003;
        private const string CatalogPath = "Assets/_Game/Gameplay/Housing/Data/Assets/FurnishingCatalog.asset";
        private const string ScalePath = "Assets/_Game/World/Data/Assets/WorldScale.asset";
        private FurnishingCatalog catalog;
        private HomeSave home;
        private Dictionary<ushort, int> crate;
        private readonly Dictionary<ushort, int> delta = new Dictionary<ushort, int>();
        private readonly List<Object> made = new List<Object>();

        private HallLookDefinition Look(ushort id, FactionId faction, HomeLook slot, bool standard = false)
        {
            var look = ScriptableObject.CreateInstance<HallLookDefinition>();
            look.itemId = id;
            look.faction = faction;
            look.slot = slot;
            look.standard = standard;
            made.Add(look);
            return look;
        }

        private FurnishingDefinition Furnishing(ushort id, FurnishingMount mount)
        {
            var furnishing = ScriptableObject.CreateInstance<FurnishingDefinition>();
            furnishing.itemId = id;
            furnishing.faction = FactionId.Covenant;
            furnishing.mount = mount;
            furnishing.boundsCenter = new Vector3(0f, 0.5f, 0f);
            furnishing.boundsSize = Vector3.one;
            made.Add(furnishing);
            return furnishing;
        }

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<FurnishingCatalog>();
            made.Add(catalog);
            catalog.SetLooks(new List<HallLookDefinition>
            {
                Look(StandardWall, FactionId.Covenant, HomeLook.Wall, standard: true),
                Look(Marble, FactionId.Covenant, HomeLook.Wall),
                Look(Timber, FactionId.Covenant, HomeLook.Wall),
                Look(NullBrick, FactionId.Null, HomeLook.Wall),
                Look(NullGate, FactionId.Null, HomeLook.Facade),
            });
            catalog.SetContents(new List<FurnishingDefinition>
            {
                Furnishing(Painting, FurnishingMount.Wall), Furnishing(Lamp, FurnishingMount.Ceiling), Furnishing(Chair, FurnishingMount.Floor),
            });
            home = new HomeSave();
            crate = new Dictionary<ushort, int> { { Marble, 1 }, { Timber, 1 }, { NullBrick, 1 }, { NullGate, 1 } };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private string Wear(HomeLook slot, ushort id) =>
            HousingRules.TryWear(home, catalog, FactionId.Covenant, item => crate.TryGetValue(item, out int n) ? n : 0, slot, id, delta);

        private EditContext Room() => new EditContext
        {
            catalog = catalog,
            area = HomeArea.Interior,
            shape = new AreaShape { halfSize = new Vector2(10f, 7.5f), height = 6f, hasWalls = true },
            faction = FactionId.Covenant,
            budget = 100,
            maxStackDepth = 3,
            minScale = 0.5f,
            maxScale = 2f,
        };

        private static PlacedPiece At(ushort item, float x, float y, float z, bool free = false, float scale = 1f)
        {
            var piece = new PlacedPiece { u = 1, i = item, f = (byte)(free ? PieceFlags.Free : PieceFlags.None) };
            piece.Scale = scale;
            piece.Position = new Vector3(x, y, z);
            return piece;
        }

        private string Judge(PlacedPiece piece) =>
            HousingRules.PoseBlockedReason(new List<PlacedPiece> { piece }, piece, catalog.Get(piece.i), Room());

        // ------------------------------------------------------------------ what a Hall wears

        [Test]
        public void AFinishComesOutOfTheCrateAndTheOneItReplacesGoesBack()
        {
            Assert.IsNull(Wear(HomeLook.Wall, Marble));
            Assert.AreEqual(Marble, home.wallFinish);
            Assert.AreEqual(-1, delta[Marble]);
            Assert.AreEqual(1, delta.Count, "the standard look it replaced is no item: nothing goes back");

            crate[Marble] = 0;
            Assert.IsNull(Wear(HomeLook.Wall, Timber));
            Assert.AreEqual(Timber, home.wallFinish);
            Assert.AreEqual(-1, delta[Timber]);
            Assert.AreEqual(1, delta[Marble], "the Finish that came off is back in the Crate");
        }

        [Test]
        public void TheStandardLookIsPutBackForNothingAndIsNeverAnItem()
        {
            Wear(HomeLook.Wall, Marble);
            Assert.IsNull(Wear(HomeLook.Wall, 0));
            Assert.AreEqual(0, home.wallFinish);
            Assert.AreEqual(1, delta[Marble]);
            Assert.AreEqual(HousingRules.ReasonWornAlready, Wear(HomeLook.Wall, StandardWall), "asked for by its id, the standard look is the same as none");
            Assert.AreSame(catalog.GetLook(StandardWall), catalog.WornLook(FactionId.Covenant, HomeLook.Wall, home.wallFinish));
            Assert.IsFalse(catalog.HeldInCrate(StandardWall));
            Assert.IsTrue(catalog.HeldInCrate(Marble));
        }

        [Test]
        public void OnlyAFacadeIsItsFactionsOwn_AndALookIsRefusedWhenItIsTheWrongKindOrNotOwned()
        {
            Assert.AreEqual(HousingRules.ReasonOtherFaction, Wear(HomeLook.Facade, NullGate), "a Hall fits its faction's Quarter");
            Assert.AreEqual(0, home.facade);
            Assert.IsTrue(catalog.MayKeep(NullBrick, FactionId.Covenant));
            Assert.IsFalse(catalog.MayKeep(NullGate, FactionId.Covenant));
            Assert.IsNull(Wear(HomeLook.Wall, NullBrick), "a Finish is anyone's: the inside is furnished in any style");
            Assert.IsNull(Wear(HomeLook.Wall, 0));
            Assert.AreEqual(HousingRules.ReasonNotALook, Wear(HomeLook.Floor, Marble), "a wall Finish is no floor");
            Assert.AreEqual(HousingRules.ReasonNotALook, Wear(HomeLook.Wall, Chair), "a furnishing is no Finish");
            crate[Timber] = 0;
            Assert.AreEqual(HousingRules.ReasonNoneLeft, Wear(HomeLook.Wall, Timber));
            Assert.AreEqual(0, home.wallFinish, "a refusal changes nothing");
        }

        // ------------------------------------------------------------------ what hangs

        [Test]
        public void WhatHangsOnAWallMustBeAtOne()
        {
            Assert.IsNull(Judge(At(Painting, 3f, 2f, 7.5f)), "on the back wall");
            Assert.IsNull(Judge(At(Painting, -10f, 2f, 0f)), "on the left wall");
            Assert.AreEqual(HousingRules.ReasonNeedsWall, Judge(At(Painting, 0f, 2f, 0f)), "in the middle of the room");
            Assert.IsNull(Judge(At(Painting, 0f, 2f, 0f, free: true)), "set down freely, it may hang from anything");
        }

        [Test]
        public void WhatHangsFromACeilingMustReachIt()
        {
            Assert.IsNull(Judge(At(Lamp, 0f, 5f, 0f)), "its top (1 m above its foot) is at the ceiling");
            Assert.AreEqual(HousingRules.ReasonNeedsCeiling, Judge(At(Lamp, 0f, 2f, 0f)));
            EditContext lawn = Room();
            lawn.area = HomeArea.Lawn;
            lawn.shape.hasWalls = false;
            PlacedPiece outside = At(Lamp, 0f, 5f, 0f);
            Assert.AreEqual(HousingRules.ReasonNeedsCeiling,
                HousingRules.PoseBlockedReason(new List<PlacedPiece> { outside }, outside, catalog.Get(Lamp), lawn), "a lawn has no ceiling");
        }

        [Test]
        public void APieceIsNeverMadeSmallerOrLargerThanTheSettingsAllow()
        {
            Assert.IsNull(Judge(At(Chair, 0f, 0f, 0f, scale: 0.5f)));
            Assert.IsNull(Judge(At(Chair, 0f, 0f, 0f, scale: 2f)));
            Assert.AreEqual(HousingRules.ReasonSize, Judge(At(Chair, 0f, 0f, 0f, scale: 0.25f)));
            Assert.AreEqual(HousingRules.ReasonSize, Judge(At(Chair, 0f, 0f, 0f, scale: 3f)));
        }

        // ------------------------------------------------------------------ the world's measure

        [Test]
        public void AModelIsMultipliedBackIntoItsKindsRangeAndOtherwiseLeftAlone()
        {
            var scale = ScriptableObject.CreateInstance<WorldScaleSettings>();
            made.Add(scale);
            scale.kinds.Add(new ScaleKind { id = "chair", axis = ScaleAxis.Height, real = 0.95f, min = 0.8f, max = 1.2f });
            scale.kinds.Add(new ScaleKind { id = "bench", axis = ScaleAxis.Length, real = 1.8f, min = 1.2f, max = 3.2f });
            Assert.AreEqual(1f, scale.Fit("chair", new Vector3(0.5f, 1.0f, 0.5f)), "true already");
            Assert.AreEqual(1f, scale.Fit("chair", new Vector3(0.5f, 1.25f, 0.5f)), "within a twentieth of its range: left as its maker made it");
            Assert.AreEqual(0.6f, scale.Fit("chair", new Vector3(0.5f, 2.0f, 0.5f)), 0.0001f, "twice too tall: brought down to the top of the range");
            Assert.AreEqual(2f, scale.Fit("chair", new Vector3(0.5f, 0.4f, 0.5f)), 0.0001f, "a doll's chair: brought up to the bottom of it");
            Assert.AreEqual(0.5f, scale.Fit("bench", new Vector3(1f, 9f, 6.4f)), 0.0001f, "a bench is judged by its length, not its height");
            Assert.AreEqual(1f, scale.Fit("no such kind", new Vector3(50f, 50f, 50f)), "an unknown kind is not guessed at");
        }

        // ------------------------------------------------------------------ what the builders made

        [Test]
        public void EveryFurnishingIsOfAKnownKindWithinItsRangeAndHasAPictureOfItsOwn()
        {
            var built = AssetDatabase.LoadAssetAtPath<FurnishingCatalog>(CatalogPath);
            var scale = AssetDatabase.LoadAssetAtPath<WorldScaleSettings>(ScalePath);
            Assert.IsNotNull(built, "run Krytical Nova ▸ Housing ▸ 2. Build Furnishings");
            Assert.IsNotNull(scale, "run Krytical Nova ▸ World ▸ Build World Scale");
            var icons = new HashSet<Sprite>();
            foreach (FurnishingDefinition furnishing in built.Furnishings)
            {
                string name = furnishing.name + " " + furnishing.DisplayName;
                Assert.IsNotNull(furnishing.prefab, name);
                Assert.IsNotNull(furnishing.item, name);
                Assert.AreEqual(FactionId.None, furnishing.item.boundFaction, name + ": a furnishing is anyone's");
                int block = furnishing.faction == FactionId.Hegemony ? 20000 : furnishing.faction == FactionId.Null ? 23000 : 26000;
                Assert.IsTrue(furnishing.itemId >= block && furnishing.itemId < block + 3000, name + ": its id is in its faction's block");
                Assert.IsTrue(scale.TryGet(furnishing.kind, out ScaleKind kind), name + ": kind '" + furnishing.kind + "' is not in the world's measure");
                float measured = WorldScaleSettings.Measure(kind.axis, furnishing.boundsSize);
                float slack = 1f + scale.leaveAlone + 0.01f;
                Assert.IsTrue(measured >= kind.min / slack && measured <= kind.max * slack,
                    $"{name}: {measured:0.00} m is outside its kind's {kind.min} to {kind.max} m");
                if (furnishing.blocks) Assert.IsTrue(furnishing.solidSize.x > 0f && furnishing.solidSize.z > 0f, name + ": it stands in the way but has no solid part");
                if (furnishing.item.icon != null) Assert.IsTrue(icons.Add(furnishing.item.icon), name + ": its picture is another furnishing's");
            }
        }

        [Test]
        public void EveryFactionHasAStandardLookForEachOfTheFour_AndOnlyFacadesAreBound()
        {
            var built = AssetDatabase.LoadAssetAtPath<FurnishingCatalog>(CatalogPath);
            Assert.IsNotNull(built);
            foreach (FactionId faction in new[] { FactionId.Hegemony, FactionId.Null, FactionId.Covenant })
                foreach (HomeLook slot in new[] { HomeLook.Facade, HomeLook.Wall, HomeLook.Floor, HomeLook.Ceiling })
                {
                    int standard = 0;
                    foreach (HallLookDefinition look in built.Looks)
                        if (look.faction == faction && look.slot == slot && look.standard) standard++;
                    Assert.AreEqual(1, standard, $"{faction} {slot}: exactly one standard look");
                }
            foreach (HallLookDefinition look in built.Looks)
            {
                Assert.IsNotNull(look.prefab, look.name);
                Assert.IsNotNull(look.item, look.name);
                Assert.AreEqual(look.IsFinish ? FactionId.None : look.faction, look.item.boundFaction, look.name + ": only a Facade is its faction's own");
                Assert.IsTrue(look.IsFinish ? look.itemId >= 29000 && look.itemId < 29500 : look.itemId >= 29500 && look.itemId < 30000, look.name + ": its id is in its block");
                Assert.IsNull(built.Get(look.itemId), look.name + ": a look is no furnishing");
            }
        }
    }
}
