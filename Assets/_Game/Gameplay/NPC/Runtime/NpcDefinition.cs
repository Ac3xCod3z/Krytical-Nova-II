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
        Wayfinder
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
