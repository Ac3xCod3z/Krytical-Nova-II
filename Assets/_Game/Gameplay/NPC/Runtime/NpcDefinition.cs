using UnityEngine;

namespace KryticalNova.NPC
{
    public enum NpcRole
    {
        HerbMaster,
        Blacksmith,
        Trainer,
        Trader,
        MallManager,
        GuardCaptain,
        Agent,
        Vaultkeeper,
        Wayfinder,
        Herald,
        Holoweaver,
        Outfitter,
        Stylist,
        Reshaper,
        Coach,
        Fabricator
    }

    /// <summary>
    /// Working services an NPC offers from its dialogue: each is a button there. The server checks that the player
    /// stands by an NPC offering the service before it does anything for it.
    /// </summary>
    [System.Flags]
    public enum NpcOffer
    {
        None = 0,
        /// <summary>The character's own storage.</summary>
        Vault = 1 << 0,
        /// <summary>Storage shared by every character of the account.</summary>
        DeepVault = 1 << 1,
        /// <summary>Founding a Crew.</summary>
        CrewRegistry = 1 << 2,
        /// <summary>The forge: Tempering, Fusion, Tuning and Reforge.</summary>
        GearWork = 1 << 3,
        /// <summary>The Renown ladders: Ascension and Nova Standings.</summary>
        Herald = 1 << 4,
        /// <summary>A shop: it sells what its stock lists, and buys what the player has to sell.</summary>
        Shop = 1 << 5,
        /// <summary>Holoweave: showing a collected look (a Weave) over worn gear.</summary>
        Holoweave = 1 << 6,
        /// <summary>Dyeing outfit pieces in another colourway.</summary>
        Dye = 1 << 7,
        /// <summary>A change of hair.</summary>
        Stylist = 1 << 8,
        /// <summary>A change of face.</summary>
        Reshaper = 1 << 9,
        /// <summary>A change of build and muscle.</summary>
        Coach = 1 << 10,
        /// <summary>Crafting: Alloys, Chroma Cells, Dossiers.</summary>
        Craft = 1 << 11,
    }

    /// <summary>Static data for one town NPC. Shops, quests and crafting hook in by role later.</summary>
    [CreateAssetMenu(menuName = "Krytical Nova/NPC/Definition")]
    public sealed class NpcDefinition : ScriptableObject
    {
        public NpcRole role;
        public string displayName = "Villager";
        [Tooltip("Shown under the name on the nameplate and in the dialogue header.")]
        public string title = "";
        [TextArea(2, 4)] public string greeting = "";
        [Tooltip("Placeholder description of what this NPC will offer once its systems exist.")]
        [TextArea(3, 8)] public string services = "";
        [Tooltip("Working services: each adds a button to this NPC's dialogue.")]
        public NpcOffer offers = NpcOffer.None;
        [Min(1f)] public float interactRange = 3f;
    }
}
