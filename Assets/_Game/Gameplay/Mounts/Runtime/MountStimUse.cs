using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// Carries out the Mount Stim (<see cref="ItemUseEffect.HealMount"/>, day list item 19): heals the selected mount's companion HP, out
    /// or at home, never the player. Apart from <see cref="PlayerMounts"/> because each item effect has one handler (as
    /// <see cref="MountFeeding"/>).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMounts))]
    public sealed class MountStimUse : MonoBehaviour, IItemUseHandler
    {
        private PlayerMounts mounts;

        private void Awake() => mounts = GetComponent<PlayerMounts>();

        public ItemUseEffect Effect => ItemUseEffect.HealMount;

        public string UseBlockedReason(UsedItem item) => mounts != null ? mounts.StimBlockedReason() : "No mounts.";

        public void ServerUse(UsedItem item)
        {
            if (mounts != null) mounts.ServerStim();
        }
    }
}
