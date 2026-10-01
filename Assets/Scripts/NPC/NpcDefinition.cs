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
        Agent
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
        [Min(1f)] public float interactRange = 3f;
    }
}
