using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Housing;
using NUnit.Framework;
using UnityEngine;

namespace KryticalNova.Tests
{
    public sealed class HousingRulesTests
    {
        private const ushort Table = 26010, Vase = 26042, Tree = 26064, Banner = 26052, NullChair = 23003;
        private FurnishingCatalog catalog;
        private HomeSave home;
        private Dictionary<ushort, int> crate;
        private readonly List<HomeChange> changes = new List<HomeChange>();
        private readonly Dictionary<ushort, int> delta = new Dictionary<ushort, int>();
        private readonly List<Object> made = new List<Object>();

        private FurnishingDefinition Furnishing(ushort id, FactionId faction, FurnishingMount mount, bool hasTop, int cost = 1,
            FurnishingPlace place = FurnishingPlace.Anywhere)
        {
            var furnishing = ScriptableObject.CreateInstance<FurnishingDefinition>();
            furnishing.itemId = id;
            furnishing.faction = faction;
            furnishing.mount = mount;
            furnishing.hasTop = hasTop;
            furnishing.budgetCost = cost;
            furnishing.place = place;
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
            catalog.SetContents(new List<FurnishingDefinition>
            {
                Furnishing(Table, FactionId.Covenant, FurnishingMount.Floor, hasTop: true, cost: 2),
                Furnishing(Vase, FactionId.Covenant, FurnishingMount.Surface, hasTop: false),
                Furnishing(Tree, FactionId.Covenant, FurnishingMount.Floor, hasTop: false, cost: 4, place: FurnishingPlace.LawnOnly),
                Furnishing(Banner, FactionId.Covenant, FurnishingMount.Wall, hasTop: false, place: FurnishingPlace.IndoorOnly),
                Furnishing(NullChair, FactionId.Null, FurnishingMount.Floor, hasTop: false),
            });
            home = new HomeSave();
            crate = new Dictionary<ushort, int> { { Table, 2 }, { Vase, 3 }, { Tree, 5 }, { Banner, 1 }, { NullChair, 1 } };
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private EditContext Context(int budget = 20) => new EditContext
        {
            catalog = catalog,
            area = HomeArea.Lawn,
            shape = new AreaShape
            {
                halfSize = new Vector2(18f, 24f),
                height = 14f,
                blocked = new[] { new Rect(-8f, -2f, 16f, 14f) }, // the Hall's pad
            },
            faction = FactionId.Covenant,
            owner = 0,
            budget = budget,
            maxStackDepth = 3,
            crateCount = id => crate.TryGetValue(id, out int n) ? n : 0,
        };

        private static PlacedPiece At(ushort item, float x, float z, uint parent = 0, float y = 0f)
        {
            var piece = new PlacedPiece { i = item, p = parent, s = PlacedPiece.NormalScale };
            piece.Position = new Vector3(x, y, z);
            return piece;
        }

        private string Apply(params HomeOp[] ops)
        {
            string refusal = HousingRules.TryApply(home, Context(), ops, changes, delta);
            if (refusal == null)
                foreach (KeyValuePair<ushort, int> d in delta) crate[d.Key] = (crate.TryGetValue(d.Key, out int n) ? n : 0) + d.Value;
            return refusal;
        }

        [Test]
        public void APieceSetDown_GetsANumber_AndComesOutOfTheCrate()
        {
            Assert.IsNull(Apply(HomeOp.Place(At(Table, -12f, -10f))));
            Assert.AreEqual(1, home.lawn.Count);
            Assert.AreEqual(1u, home.lawn[0].u);
            Assert.AreEqual(2u, home.nextUid);
            Assert.AreEqual(1, crate[Table]);
            Assert.IsTrue(changes[0].hasAfter && !changes[0].hadBefore);
        }

        [Test]
        public void NothingIsSetDown_WhenTheCrateHasNoneLeft()
        {
            crate[Table] = 0;
            Assert.AreEqual(HousingRules.ReasonNoneLeft, Apply(HomeOp.Place(At(Table, -12f, -10f))));
            Assert.AreEqual(0, home.lawn.Count);
        }

        [Test]
        public void AnotherFactionsFurnishing_IsRefused()
        {
            Assert.AreEqual(HousingRules.ReasonOtherFaction, Apply(HomeOp.Place(At(NullChair, -12f, -10f))));
            Assert.AreEqual(1, crate[NullChair]);
        }

        [Test]
        public void OutsideTheLot_OnThePad_AndIndoorPiecesOnALawn_AreRefused()
        {
            Assert.AreEqual(HousingRules.ReasonOutside, Apply(HomeOp.Place(At(Table, 30f, 0f))));
            Assert.AreEqual(HousingRules.ReasonBlocked, Apply(HomeOp.Place(At(Table, 0f, 4f))));
            Assert.AreEqual(HousingRules.ReasonIndoors, Apply(HomeOp.Place(At(Banner, -12f, -10f))));
            Assert.AreEqual(0, home.lawn.Count);
        }

        [Test]
        public void TheBudget_StopsTheEditThatWouldPassIt_AndNothingOfThatEditStays()
        {
            // 5 trees at 4 points each are 20; the table's 2 would pass it.
            var ops = new List<HomeOp>();
            for (int n = 0; n < 5; n++) ops.Add(HomeOp.Place(At(Tree, -16f + n * 2f, -20f)));
            Assert.IsNull(HousingRules.TryApply(home, Context(), ops, changes, delta));
            Assert.AreEqual(HousingRules.ReasonBudget, HousingRules.TryApply(home, Context(), new[] { HomeOp.Place(At(Table, -12f, -10f)) }, changes, delta));
            Assert.AreEqual(5, home.lawn.Count);
        }

        [Test]
        public void APieceOnATable_MovesWithTheTable_AndIsPutAwayWithIt()
        {
            Assert.IsNull(Apply(HomeOp.Place(At(Table, -12f, -10f))));
            uint table = home.lawn[0].u;
            Assert.IsNull(Apply(HomeOp.Place(At(Vase, 0.25f, 0f, table, 1f))));
            uint vase = home.lawn[1].u;

            PlacedPiece moved = home.lawn[0];
            moved.Position = new Vector3(10f, 0f, -18f);
            moved.Yaw = 90f;
            Assert.IsNull(Apply(HomeOp.Move(moved)));
            HousingLayout.TryFind(home.lawn, vase, out PlacedPiece onTop);
            HousingLayout.Pose(home.lawn, onTop, out Vector3 position, out _);
            // A quarter metre along the table's x, which now points along -z.
            Assert.AreEqual(10f, position.x, 0.02f);
            Assert.AreEqual(1f, position.y, 0.02f);
            Assert.AreEqual(-18.25f, position.z, 0.02f);

            Assert.IsNull(Apply(HomeOp.Remove(table)));
            Assert.AreEqual(0, home.lawn.Count);
            Assert.AreEqual(2, changes.Count);
            Assert.AreEqual(2, crate[Table]);
            Assert.AreEqual(3, crate[Vase]);
        }

        [Test]
        public void NothingStandsOnWhatHasNoTop_AndATableCannotBeMovedOffTheLotWithWhatIsOnIt()
        {
            Assert.IsNull(Apply(HomeOp.Place(At(Tree, -12f, -10f))));
            Assert.AreEqual(HousingRules.ReasonNoTop, Apply(HomeOp.Place(At(Vase, 0f, 0f, home.lawn[0].u, 1f))));

            Assert.IsNull(Apply(HomeOp.Place(At(Table, 17f, -20f))));
            uint table = home.lawn[1].u;
            Assert.IsNull(Apply(HomeOp.Place(At(Vase, 0.5f, 0f, table, 1f))));
            PlacedPiece nudged = home.lawn[1];
            nudged.Position = new Vector3(17.75f, 0f, -20f); // the table is still inside; the vase would not be
            Assert.AreEqual(HousingRules.ReasonOutside, Apply(HomeOp.Move(nudged)));
        }

        [Test]
        public void AMovedPiece_KeepsItsItemAndOwner_WhateverTheRequestSays()
        {
            Assert.IsNull(Apply(HomeOp.Place(At(Table, -12f, -10f))));
            PlacedPiece forged = home.lawn[0];
            forged.i = Tree;
            forged.o = 7;
            forged.Position = new Vector3(-11f, 0f, -10f);
            Assert.IsNull(Apply(HomeOp.Move(forged)));
            Assert.AreEqual(Table, home.lawn[0].i);
            Assert.AreEqual(0, home.lawn[0].o);
        }

        [Test]
        public void AFactionsOwnItem_IsForThatFactionOnly_AndAnyoneMayHaveTheRest()
        {
            var bound = ScriptableObject.CreateInstance<KryticalNova.Items.ItemDefinition>();
            var free = ScriptableObject.CreateInstance<KryticalNova.Items.ItemDefinition>();
            made.Add(bound);
            made.Add(free);
            bound.boundFaction = FactionId.Covenant;
            Assert.IsTrue(KryticalNova.Items.ItemFactionRules.IsBound(bound));
            Assert.IsTrue(KryticalNova.Items.ItemFactionRules.MayHave(bound, FactionId.Covenant));
            Assert.IsFalse(KryticalNova.Items.ItemFactionRules.MayHave(bound, FactionId.Null));
            Assert.IsFalse(KryticalNova.Items.ItemFactionRules.MayHave(bound, FactionId.None));
            Assert.IsFalse(KryticalNova.Items.ItemFactionRules.IsBound(free));
            Assert.IsTrue(KryticalNova.Items.ItemFactionRules.MayHave(free, FactionId.Null));
            Assert.IsTrue(KryticalNova.Items.ItemFactionRules.MayHave(null, FactionId.Null));
        }

        [Test]
        public void APosition_SurvivesBeingSaved_ToTheSixtyFourthOfAMetre()
        {
            var piece = new PlacedPiece();
            piece.Position = new Vector3(12.375f, 0.5f, -23.125f);
            piece.Yaw = 345f;
            string json = JsonUtility.ToJson(new HomeSave { lawn = new List<PlacedPiece> { piece } });
            PlacedPiece back = JsonUtility.FromJson<HomeSave>(json).lawn[0];
            Assert.AreEqual(new Vector3(12.375f, 0.5f, -23.125f), back.Position);
            Assert.AreEqual(-15f, back.Yaw, 0.001f);
        }

        [Test]
        public void ASavedHall_IsBroughtUpToDate_TheSameWayTwice()
        {
            var saved = new HomeSave { nextUid = 0, lawn = new List<PlacedPiece> { new PlacedPiece { u = 9, i = Table } } };
            HomeSave once = HousingMigrations.Upgrade(saved);
            Assert.AreEqual(10u, once.nextUid);
            HomeSave twice = HousingMigrations.Upgrade(once);
            Assert.AreEqual(10u, twice.nextUid);
            Assert.AreEqual(1, twice.lawn.Count);
        }
    }

    public sealed class HousingPlacementTests
    {
        [Test]
        public void TheGrid_SnapsAcrossAndAlong_AndLeavesHeightAlone()
        {
            Vector3 snapped = HousingPlacement.SnapToGrid(new Vector3(1.26f, 0.37f, -2.74f), 0.5f);
            Assert.AreEqual(new Vector3(1.5f, 0.37f, -2.5f), snapped);
            Assert.AreEqual(1.25f, HousingPlacement.Snap(1.26f, 0.125f), 0.0001f);
        }

        [Test]
        public void ATurn_GoesByItsStep_BothWays_AndWrapsRound()
        {
            Assert.AreEqual(15f, HousingPlacement.StepYaw(0f, 15f, 1), 0.001f);
            Assert.AreEqual(-15f, HousingPlacement.StepYaw(0f, 15f, -1), 0.001f);
            Assert.AreEqual(-165f, HousingPlacement.StepYaw(180f, 15f, 1), 0.001f);
        }

        [Test]
        public void Footprints_SideBySideDoNotOverlap_AndOverlappingOnesDo()
        {
            Vector2 half = new Vector2(1f, 0.5f);
            Assert.IsFalse(HousingPlacement.FootprintsOverlap(Vector2.zero, half, 0f, new Vector2(2f, 0f), half, 0f));
            Assert.IsTrue(HousingPlacement.FootprintsOverlap(Vector2.zero, half, 0f, new Vector2(1.5f, 0f), half, 0f));
            // Turned a quarter, the long side no longer reaches.
            Assert.IsFalse(HousingPlacement.FootprintsOverlap(Vector2.zero, half, 90f, new Vector2(1.6f, 0f), half, 90f));
            // A corner poking in counts.
            Assert.IsTrue(HousingPlacement.FootprintsOverlap(Vector2.zero, half, 0f, new Vector2(1.3f, 0.6f), half, 45f));
            Assert.IsFalse(HousingPlacement.FootprintsOverlap(Vector2.zero, half, 0f, new Vector2(1.6f, 0.9f), half, 45f));
        }
    }

    public sealed class ArrangeHistoryTests
    {
        private const ushort Table = 26010, Vase = 26042;
        private FurnishingCatalog catalog;
        private HomeSave home;
        private ArrangeHistory history;
        private readonly List<HomeChange> changes = new List<HomeChange>();
        private readonly Dictionary<ushort, int> delta = new Dictionary<ushort, int>();
        private readonly List<Object> made = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<FurnishingCatalog>();
            var table = ScriptableObject.CreateInstance<FurnishingDefinition>();
            table.itemId = Table; table.hasTop = true;
            var vase = ScriptableObject.CreateInstance<FurnishingDefinition>();
            vase.itemId = Vase; vase.mount = FurnishingMount.Surface;
            catalog.SetContents(new List<FurnishingDefinition> { table, vase });
            made.AddRange(new Object[] { catalog, table, vase });
            home = new HomeSave();
            history = new ArrangeHistory(50);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object o in made) Object.DestroyImmediate(o);
            made.Clear();
        }

        private EditContext Context() => new EditContext
        {
            catalog = catalog,
            shape = new AreaShape { halfSize = new Vector2(18f, 24f), height = 14f },
            budget = 100,
            maxStackDepth = 3,
            crateCount = _ => 99,
        };

        private void Edit(params HomeOp[] ops)
        {
            Assert.IsNull(HousingRules.TryApply(home, Context(), ops, changes, delta));
            history.Record(changes);
        }

        private void Undo()
        {
            List<HomeOp> ops = history.BeginUndo(home.lawn);
            Assert.IsNotNull(ops);
            Assert.IsNull(HousingRules.TryApply(home, Context(), ops, changes, delta));
            history.Settle(true, ops, changes);
        }

        private void Redo()
        {
            List<HomeOp> ops = history.BeginRedo(home.lawn);
            Assert.IsNotNull(ops);
            Assert.IsNull(HousingRules.TryApply(home, Context(), ops, changes, delta));
            history.Settle(false, ops, changes);
        }

        private static PlacedPiece At(ushort item, float x, float z, uint parent = 0)
        {
            var piece = new PlacedPiece { i = item, p = parent, s = PlacedPiece.NormalScale };
            piece.Position = new Vector3(x, 0f, z);
            return piece;
        }

        [Test]
        public void SettingDown_IsUndone_Redone_AndUndoneAgain()
        {
            Edit(HomeOp.Place(At(Table, 1f, 1f)));
            Undo();
            Assert.AreEqual(0, home.lawn.Count);
            Redo();
            Assert.AreEqual(1, home.lawn.Count);
            Assert.AreEqual(new Vector3(1f, 0f, 1f), home.lawn[0].Position);
            Undo();
            Assert.AreEqual(0, home.lawn.Count);
            Assert.IsFalse(history.CanUndo);
        }

        [Test]
        public void AMove_IsUndoneToWhereThePieceStood()
        {
            Edit(HomeOp.Place(At(Table, 1f, 1f)));
            PlacedPiece moved = home.lawn[0];
            moved.Position = new Vector3(5f, 0f, 5f);
            Edit(HomeOp.Move(moved));
            Undo();
            Assert.AreEqual(new Vector3(1f, 0f, 1f), home.lawn[0].Position);
            Redo();
            Assert.AreEqual(new Vector3(5f, 0f, 5f), home.lawn[0].Position);
        }

        [Test]
        public void ATablePutAwayWithItsVase_ComesBackWithTheVaseStillOnIt()
        {
            Edit(HomeOp.Place(At(Table, 1f, 1f)));
            uint table = home.lawn[0].u;
            Edit(HomeOp.Place(At(Vase, 0.25f, 0f, table)));
            Edit(HomeOp.Remove(table));
            Assert.AreEqual(0, home.lawn.Count);

            Undo();
            Assert.AreEqual(2, home.lawn.Count);
            PlacedPiece newTable = home.lawn.Find(p => p.i == Table);
            PlacedPiece newVase = home.lawn.Find(p => p.i == Vase);
            Assert.AreNotEqual(table, newTable.u, "a piece that comes back has a new number");
            Assert.AreEqual(newTable.u, newVase.p);

            // The older steps still find the pieces under their new numbers.
            Redo();
            Assert.AreEqual(0, home.lawn.Count);
            Undo();
            Undo(); // the vase's own setting down
            Assert.AreEqual(1, home.lawn.Count);
            Assert.AreEqual(Table, home.lawn[0].i);
        }

        [Test]
        public void AStepIsSkipped_WhenSomeoneElseHasMovedThePieceSince()
        {
            Edit(HomeOp.Place(At(Table, 1f, 1f)));
            PlacedPiece mine = home.lawn[0];
            mine.Position = new Vector3(5f, 0f, 5f);
            Edit(HomeOp.Move(mine));

            // Another player moves it: not through this player's history.
            PlacedPiece theirs = home.lawn[0];
            theirs.Position = new Vector3(-7f, 0f, -7f);
            Assert.IsNull(HousingRules.TryApply(home, Context(), new[] { HomeOp.Move(theirs) }, changes, delta));

            List<HomeOp> ops = history.BeginUndo(home.lawn);
            Assert.AreEqual(0, ops.Count);
            Assert.AreEqual(new Vector3(-7f, 0f, -7f), home.lawn[0].Position);
        }
    }
}
