using KryticalNova.Core;
using UnityEngine;

namespace KryticalNova.Mounts
{
    /// <summary>
    /// Carries out Mount Feed (<see cref="ItemUseEffect.FeedMount"/>) for the player's mounts. Apart from
    /// <see cref="PlayerMounts"/> because each item effect has one handler, and that one registers mounts.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerMounts))]
    public sealed class MountFeeding : MonoBehaviour, IItemUseHandler
    {
        private PlayerMounts mounts;

        private void Awake() => mounts = GetComponent<PlayerMounts>();

        public ItemUseEffect Effect => ItemUseEffect.FeedMount;

        public string UseBlockedReason(UsedItem item) => mounts != null ? mounts.FeedBlockedReason() : "No mounts.";

        public void ServerUse(UsedItem item)
        {
            if (mounts != null) mounts.ServerFeed();
        }
    }
}
