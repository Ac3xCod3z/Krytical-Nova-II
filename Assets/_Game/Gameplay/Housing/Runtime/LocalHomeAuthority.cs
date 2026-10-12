using System;
using System.Collections.Generic;
using KryticalNova.Factions;
using KryticalNova.Persistence;

namespace KryticalNova.Housing
{
    /// <summary>
    /// The Hall sandbox's authority: one player, one Hall, decided on the machine it is played on (offline or as
    /// host) and saved there as a world document. It runs the same <see cref="HousingRules"/> a server will.
    /// </summary>
    public sealed class LocalHomeAuthority : IHomeAuthority
    {
        private readonly string document;
        private readonly PlayerCrate crate;
        private readonly AreaShape lawn, interior;
        private readonly Func<FactionId> faction;
        private readonly string characterId;
        private readonly List<HomeChange> changes = new List<HomeChange>();
        private readonly Dictionary<ushort, int> crateDelta = new Dictionary<ushort, int>();
        private ushort lastRequest;

        public HomeSave Home { get; }
        public FurnishingCatalog Catalog { get; }
        public HousingSettings Settings { get; }

        public event Action<HomeArea, ushort, IReadOnlyList<HomeChange>> Changed;
        public event Action<ushort, string> Refused;

        /// <summary>The name of a character's sandbox Hall among the world documents.</summary>
        public static string DocumentName(string characterId) =>
            "housing/sandbox/" + (string.IsNullOrEmpty(characterId) ? "offline" : characterId);

        public LocalHomeAuthority(string characterId, Func<FactionId> faction, PlayerCrate crate, FurnishingCatalog catalog,
            HousingSettings settings, AreaShape lawn, AreaShape interior)
        {
            this.characterId = characterId ?? string.Empty;
            this.faction = faction;
            this.crate = crate;
            this.lawn = lawn;
            this.interior = interior;
            Catalog = catalog;
            Settings = settings;
            document = DocumentName(characterId);
            Home = HousingMigrations.Upgrade(ServerSave.World<HomeSave>(document));
        }

        public AreaShape Shape(HomeArea area) => area == HomeArea.Interior ? interior : lawn;
        public int Budget(HomeArea area) => Settings != null ? Settings.Budget(area) : int.MaxValue;

        public int BudgetUsed(HomeArea area) => HousingLayout.BudgetUsed(Home.Area(area), CostOf);

        private int CostOf(ushort itemId)
        {
            FurnishingDefinition furnishing = Catalog != null ? Catalog.Get(itemId) : null;
            return furnishing != null ? furnishing.budgetCost : 1;
        }

        public ushort Submit(HomeArea area, IReadOnlyList<HomeOp> ops)
        {
            ushort request = ++lastRequest == 0 ? ++lastRequest : lastRequest;
            var context = new EditContext
            {
                catalog = Catalog,
                area = area,
                shape = Shape(area),
                faction = faction != null ? faction() : FactionId.None,
                owner = Home.OwnerIndex(characterId),
                budget = Budget(area),
                maxStackDepth = Settings != null ? Settings.maxStackDepth : 3,
                crateCount = id => crate != null ? crate.Count(id) : 0,
            };

            string refusal = HousingRules.TryApply(Home, context, ops, changes, crateDelta);
            if (refusal != null)
            {
                Refused?.Invoke(request, refusal);
                return request;
            }

            if (crate != null)
            {
                foreach (KeyValuePair<ushort, int> delta in crateDelta)
                {
                    if (delta.Value > 0) crate.ServerAdd(delta.Key, delta.Value);
                    else if (delta.Value < 0) crate.ServerTake(delta.Key, -delta.Value);
                }
            }
            ServerSave.SaveWorld(document);
            Changed?.Invoke(area, request, changes);
            return request;
        }

        /// <summary>Puts everything in the Hall back into the Crate and empties it (a GM command).</summary>
        public int PutEverythingAway()
        {
            int count = 0;
            foreach (HomeArea area in new[] { HomeArea.Lawn, HomeArea.Interior })
            {
                List<PlacedPiece> pieces = Home.Area(area);
                if (pieces.Count == 0) continue;
                var ops = new List<HomeOp>();
                foreach (PlacedPiece piece in pieces)
                    if (piece.p == 0) ops.Add(HomeOp.Remove(piece.u));
                count += pieces.Count;
                Submit(area, ops);
            }
            return count;
        }
    }

    /// <summary>
    /// Brings a saved Hall up to the form the game reads now. Each step is safe to run on a Hall that has had it,
    /// so a document is never harmed by being loaded twice. There is one version so far.
    /// </summary>
    public static class HousingMigrations
    {
        public static HomeSave Upgrade(HomeSave home)
        {
            home ??= new HomeSave();
            home.owners ??= new List<string>();
            home.lawn ??= new List<PlacedPiece>();
            home.interior ??= new List<PlacedPiece>();
            if (home.nextUid == 0) home.nextUid = 1;
            // A number is never given twice, whatever a damaged document says.
            foreach (PlacedPiece piece in home.lawn) if (piece.u >= home.nextUid) home.nextUid = piece.u + 1;
            foreach (PlacedPiece piece in home.interior) if (piece.u >= home.nextUid) home.nextUid = piece.u + 1;
            home.version = HomeSave.CurrentVersion;
            return home;
        }
    }
}
