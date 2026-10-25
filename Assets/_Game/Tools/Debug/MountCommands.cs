using KryticalNova.Items;
using KryticalNova.Mounts;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console mount commands (owner, 25 Oct 2026). Like the GM commands they only run where the game is decided
    /// (host, server or offline) and act on the player picked by <see cref="GmTarget"/>. gm.mount.give puts a mount's
    /// item in the Pack, the way a player gets one; the others change the selected mount.
    /// </summary>
    public static class MountCommands
    {
        private static bool TryGetMounts(out PlayerMounts mounts, out string error)
        {
            mounts = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            mounts = player.GetComponent<PlayerMounts>();
            if (mounts == null || mounts.Catalog == null) { error = "That player has no mount system (run Krytical Nova/Setup/Player Prefab)."; return false; }
            return true;
        }

        private static bool TryGetSelected(out PlayerMounts mounts, out string error)
        {
            if (!TryGetMounts(out mounts, out error)) return false;
            if (mounts.Selected != null) return true;
            error = "That player has no mount selected. Give one with gm.mount.give, use it from the Pack, then select it (N).";
            return false;
        }

        private static string Describe(PlayerMounts mounts)
        {
            var lines = new System.Text.StringBuilder();
            foreach (MountDefinition mount in mounts.Catalog.Mounts)
            {
                if (mount == null) continue;
                int box = mount.mountId;
                if (!mounts.Owns(box)) { lines.AppendLine($"{box}. {mount.displayName}: not owned{(mount.IsBuilt ? "" : " (not in the game yet)")}."); continue; }
                MountState state = mounts.StateOf(box);
                int growth = mounts.GrowthOf(box);
                int stage = MountRules.Stage(growth);
                string mark = box == mounts.SelectedId ? " [selected" + (mounts.IsMounted ? $", {mounts.Phase}]" : "]") : "";
                lines.AppendLine($"{box}. {mount.displayName}{mark}: {growth}% growth ({state.Xp:0.##} EXP), {mount.StageName(stage)}, " +
                                 $"hunger {state.HungerPercent:0.###}%, {PlayerMounts.StatLine(mount.firstStat, mount.firstMax, growth, state.IsFed)}, " +
                                 $"{PlayerMounts.StatLine(mount.secondStat, mount.secondMax, growth, state.IsFed)}.");
            }
            return lines.ToString().TrimEnd();
        }

        private static MountDefinition Find(PlayerMounts mounts, string which)
        {
            if (int.TryParse(which, out int box)) return mounts.Catalog.Get(box);
            string wanted = which.Replace(" ", "");
            foreach (MountDefinition mount in mounts.Catalog.Mounts)
                if (mount != null && mount.displayName.Replace(" ", "").Equals(wanted, System.StringComparison.OrdinalIgnoreCase)) return mount;
            return null;
        }

        [Command("gm.mount", "Print the four mount boxes of the targeted player: owned or not, growth, stage, hunger and stats.")]
        private static string Status() => TryGetMounts(out var mounts, out var error) ? Describe(mounts) : error;

        [Command("gm.mount.give", "Put a mount's item in the targeted player's Pack: gm.mount.give brambleback (or its box number, 1). " +
                                  "Using it from the Pack adds the mount to the Mount window.")]
        private static string Give(string which = "brambleback")
        {
            if (!TryGetMounts(out var mounts, out var error)) return error;
            MountDefinition mount = Find(mounts, which);
            if (mount == null) return $"No mount called '{which}'. The four: Brambleback (1), Grave Trot (2), Stone Horn (3), Circuit Strider (4).";
            if (mount.itemId == 0) return $"The {mount.displayName} is not in the game yet: it has no item.";
            var inventory = mounts.GetComponent<PlayerInventory>();
            if (inventory == null) return "That player has no Pack.";
            int added = inventory.ServerGive(mount.itemId, 1, earned: true);
            return added > 0 ? $"Gave a {mount.displayName}. Use it from the Pack to add it to the Mount window." : "The Pack is full.";
        }

        [Command("gm.mount.forget", "Take a mount out of the targeted player's Mount window again (for testing): gm.mount.forget brambleback.")]
        private static string Forget(string which = "brambleback")
        {
            if (!TryGetMounts(out var mounts, out var error)) return error;
            MountDefinition mount = Find(mounts, which);
            if (mount == null) return $"No mount called '{which}'.";
            mounts.ServerForget(mount.mountId);
            return Describe(mounts);
        }

        [Command("gm.mount.xp", "Give EXP to the selected mount (100,000 fills the growth meter; a Hostile kill is worth 0.04).")]
        private static string Xp(float amount)
        {
            if (!TryGetSelected(out var mounts, out var error)) return error;
            if (amount <= 0f) return "Give a positive amount of mount EXP.";
            mounts.ServerAddXp(amount);
            return Describe(mounts);
        }

        [Command("gm.mount.growth", "Set the growth of the selected mount, 0-100. It evolves at 50 and 100.")]
        private static string Growth(float percent)
        {
            if (!TryGetSelected(out var mounts, out var error)) return error;
            mounts.ServerSetGrowth(percent);
            return Describe(mounts);
        }

        [Command("gm.mount.hunger", "Set the hunger of the selected mount, 0-100. At 0 it is starving: no stats, no EXP, its slowest speed.")]
        private static string Hunger(float percent)
        {
            if (!TryGetSelected(out var mounts, out var error)) return error;
            mounts.ServerSetHunger(percent);
            return Describe(mounts);
        }

        [Command("gm.mount.feed", "Feed the selected mount to 100% hunger without Mount Feed.")]
        private static string Feed()
        {
            if (!TryGetSelected(out var mounts, out var error)) return error;
            mounts.ServerFeed();
            return Describe(mounts);
        }

        [Command("gm.mount.ride", "Put the targeted player on their selected mount, or take them off it (what the Ride key does).")]
        private static string Ride()
        {
            if (!TryGetMounts(out var mounts, out var error)) return error;
            mounts.ServerToggleRide();
            return Describe(mounts);
        }
    }
}
