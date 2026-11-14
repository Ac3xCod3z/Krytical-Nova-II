namespace KryticalNova.Townsfolk
{
    // The words of town life (owner's DAY list item 18; plan Plans/living-npcs-concord-plan.md). Values are sent in the ghost and kept
    // in assets: append only, never renumber.

    /// <summary>What a smart spot offers (plan 3.5). 0 is a plain walk point (wander).</summary>
    public enum TownSpotKind : byte
    {
        Walk = 0, Bench = 1, Stall = 2, VendorStall = 3, LeanWall = 4, ChatCircle = 5, Viewpoint = 6, Work = 7, Perform = 8,
        GuardPost = 9, Gate = 10, Shelter = 11, HomeDoor = 12, JogPoint = 13,
    }

    /// <summary>What a townsperson is doing; the client picks the clip from it (plan 3.3).</summary>
    public enum TownActivity : byte
    {
        None = 0, Walk = 1, Jog = 2, Stand = 3, Sit = 4, Lean = 5, Chat = 6, Shop = 7, Work = 8, Perform = 9, Watch = 10, Guard = 11,
        Shelter = 12, Stretch = 13,
    }

    /// <summary>What a personality does when it rains (plan 3.4).</summary>
    public enum RainBehaviour : byte { Ignore = 0, Shelter = 1, ShelterThenHome = 2, GoHome = 3, PackUp = 4 }

    /// <summary>The four parts of a game day (plan 3.4: dawn 05-08, day 08-18, dusk 18-22, night 22-05; hours in TownsfolkSettings).</summary>
    public enum TownPeriod : byte { Dawn = 0, Day = 1, Dusk = 2, Night = 3 }

    [System.Flags]
    public enum TownSpotFlags : byte { None = 0, Sheltered = 1, Lit = 2, Quiet = 4, FacesPlaza = 8 }
}
