namespace KryticalNova.Core
{
    /// <summary>
    /// What using an item from the Pack does, beyond restoring HP / Flux (which the item's own numbers say).
    /// Each effect is carried out by the <see cref="IItemUseHandler"/> on the player that owns it. Append only.
    /// </summary>
    public enum ItemUseEffect : byte
    {
        None = 0,
        /// <summary>Feeds the pet that is out (a Pet Ration).</summary>
        FeedPet = 1,
        /// <summary>Raises a stat for good, up to a number of times per character (a Serum).</summary>
        Serum = 2,
        /// <summary>Adds days of Autopilot time to the character (an Autopilot Pass).</summary>
        AutopilotTime = 3,
        /// <summary>Unlocks the skill it teaches, to be learned with skill points (a Dossier).</summary>
        LearnSkill = 4,
        /// <summary>Adds time to one of the character's XP boosts (a Field, Companion or Tandem Booster).</summary>
        ExperienceBoost = 5,
        /// <summary>Takes the character back to town after a short channel (a Recall Tag).</summary>
        Recall = 6,
        /// <summary>A drink from a Flux Flask: Flux over a few seconds. The bottle is kept and one charge used.</summary>
        FluxFlask = 7,
        /// <summary>Adds days of Autobuff to the character, by the clock (an Autobuff Pass).</summary>
        AutobuffTime = 8,
        /// <summary>Gives the character a new name, chosen as it is used (a Name Change Pass; owner, 22 Oct 2026).</summary>
        NameChange = 9,
        /// <summary>Adds the mount the item is to the character's Mount window (a Brambleback item; owner, 25 Oct 2026).</summary>
        RegisterMount = 10,
        /// <summary>Feeds the selected mount (Mount Feed).</summary>
        FeedMount = 11,
    }

    /// <summary>
    /// The item being used, as an <see cref="IItemUseHandler"/> sees it: its id and the name the player knows it by.
    /// Handlers need no more, so they need not know the item catalog (which lives above them in Items).
    /// </summary>
    public readonly struct UsedItem
    {
        public readonly ushort Id;
        public readonly string Name;

        public UsedItem(ushort id, string name)
        {
            Id = id;
            Name = name;
        }

        /// <summary>True for no item (id 0).</summary>
        public bool IsNone => Id == 0;
    }
}
