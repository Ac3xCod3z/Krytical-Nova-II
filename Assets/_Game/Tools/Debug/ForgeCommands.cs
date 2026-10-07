using KryticalNova.Forge;
using KryticalNova.Items;
using KryticalNova.Progression;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for gear progression. Like the GM commands they only run where the game is decided
    /// (host, server or offline) and act on the player picked by <see cref="GmTarget"/>. Until drops and shops exist
    /// they are the only source of forge materials and Renown.
    /// </summary>
    public static class ForgeCommands
    {
        private const string NoForge = "That player has no forge component (run Krytical Nova/Setup/Player Prefab).";

        private static bool TryGetForge(out PlayerGearWork work, out string error)
        {
            work = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            work = player.GetComponent<PlayerGearWork>();
            if (work != null && work.Inventory != null) return true;
            error = NoForge;
            return false;
        }

        /// <summary>The first piece of ladder gear in the Pack, of one kind when <paramref name="slot"/> names it.</summary>
        private static int FindGear(PlayerInventory inventory, string slot, out string error)
        {
            error = null;
            EquipSlot wanted = EquipSlot.None;
            if (slot.Length > 0 && !System.Enum.TryParse(slot, true, out wanted))
            {
                error = $"Unknown slot '{slot}'. Use weapon, armor, gloves, boots, ring, amulet or cape.";
                return -1;
            }
            for (int cell = 0; cell < PlayerInventory.Capacity; cell++)
            {
                ItemDefinition item = inventory.GetDefinition(cell);
                if (item == null || !item.IsLadderGear) continue;
                if (wanted == EquipSlot.None || item.equipSlot == wanted) return cell;
            }
            error = wanted == EquipSlot.None ? "There is no gear in the Pack." : $"There is no {wanted} in the Pack.";
            return -1;
        }

        private static string SetState(string slot, System.Func<GearItemState, GearItemState> change)
        {
            if (!TryGetForge(out var work, out var error)) return error;
            int cell = FindGear(work.Inventory, slot, out error);
            if (cell < 0) return error;
            GearItemState next = change(GearItemState.Of(work.Inventory.Get(cell)));
            if (!work.ServerSetState(cell, next)) return "Could not change that item.";
            ItemDefinition item = work.Inventory.GetDefinition(cell);
            return $"{item.displayName} (level {item.itemLevel}): {(next.IsClean ? "untouched" : next.ToString())}.";
        }

        [Command("gm.temper", "Set the Temper % of the first piece of gear in the targeted player's Pack, up to the cap (120): " +
                              "gm.temper <percent> [slot]. Slot narrows it to weapon, armor, gloves, boots, ring, amulet or cape.")]
        private static string Temper(int percent, string slot = "")
        {
            if (!TryGetForge(out var work, out var error)) return error;
            int cap = work.Settings != null ? work.Settings.temper.hardCap : GearItemState.MaxTemper;
            return SetState(slot, state => state.WithTemper(System.Math.Min(percent, cap)));
        }

        [Command("gm.temper.all", "Set the Temper % of every piece of gear the targeted player has: gm.temper.all <percent> [worn|pack|all]. " +
                                  "worn: what is equipped. pack: what is in the Pack. all (the default): both. Stops at the cap (120).")]
        private static string TemperAll(int percent, string where = "all")
        {
            if (!TryGetForge(out var work, out var error)) return error;
            bool worn = where == "worn" || where == "all", pack = where == "pack" || where == "all";
            if (!worn && !pack) return $"Unknown place '{where}'. Use worn, pack or all.";
            int cap = work.Settings != null ? work.Settings.temper.hardCap : GearItemState.MaxTemper;
            percent = System.Math.Max(0, System.Math.Min(percent, cap));
            PlayerInventory inventory = work.Inventory;

            int changedWorn = 0, changedPack = 0;
            if (worn)
            {
                foreach (EquipSlot slot in EquipSlots.All)
                {
                    ItemStack stack = inventory.GetEquipped(slot);
                    ItemDefinition item = stack.IsEmpty || inventory.Catalog == null ? null : inventory.Catalog.Get(stack.itemId);
                    if (item == null || !item.IsLadderGear) continue;
                    inventory.ServerSetEquippedData(slot, GearItemState.Of(stack).WithTemper(percent).PackInto(stack.data));
                    changedWorn++;
                }
            }
            if (pack)
            {
                inventory.ServerChangePack(cells =>
                {
                    for (int cell = 0; cell < cells.Length; cell++)
                    {
                        ItemDefinition item = cells[cell].IsEmpty ? null : inventory.Catalog.Get(cells[cell].itemId);
                        if (item == null || !item.IsLadderGear) continue;
                        cells[cell] = new ItemStack(cells[cell].itemId, 1, GearItemState.Of(cells[cell]).WithTemper(percent).PackInto(cells[cell].data));
                        changedPack++;
                    }
                    return changedPack > 0;
                });
            }
            if (changedWorn + changedPack == 0) return "That player has no gear there to temper.";
            return $"Temper set to +{percent}% on {changedWorn} worn and {changedPack} Pack piece(s).";
        }

        [Command("gm.fuse", "Set the Fusion stage (0-12) of the first piece of gear in the Pack: gm.fuse <stage> [slot].")]
        private static string Fuse(int stage, string slot = "") => SetState(slot, state => state.WithFusion(stage));

        [Command("gm.tune", "Set the Tuning stage (0-25) of the first piece of gear in the Pack: gm.tune <stage> [slot].")]
        private static string Tune(int stage, string slot = "") => SetState(slot, state => state.WithTune(stage));

        [Command("gm.renown", "Add Renown to the targeted player (negative takes it away). With no amount it prints the balance.")]
        private static string Renown(int amount = 0)
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            var progression = player.GetComponent<PlayerCharacterProgression>();
            if (progression == null) return "That player has no progression.";
            if (amount != 0) progression.AddRenown(amount);
            return $"Renown {progression.Renown:N0}.";
        }

        [Command("gm.renown.set", "Set the targeted player's Renown outright: gm.renown.set <amount>.")]
        private static string RenownSet(int amount)
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            var progression = player.GetComponent<PlayerCharacterProgression>();
            if (progression == null) return "That player has no progression.";
            progression.AddRenown(System.Math.Max(0, amount) - progression.Renown);
            return $"Renown {progression.Renown:N0}.";
        }

        [Command("gm.forgekit", "Give the targeted player forge materials: the five Alloys, every Failsafe, the Fusion Catalyst and the Tuning chips, " +
                                "<count> of each (default 20).")]
        private static string ForgeKit(int count = 20)
        {
            if (!TryGetForge(out var work, out var error)) return error;
            ushort[] ids =
            {
                GearMaterialIds.Alumite, GearMaterialIds.Iridion, GearMaterialIds.Orichalc, GearMaterialIds.Eversteel, GearMaterialIds.TrinityPlatinum,
                GearMaterialIds.Failsafe, GearMaterialIds.AdvancedFailsafe, GearMaterialIds.LuckyFailsafe, // no Prime Alloy: it has no use yet
                GearMaterialIds.FusionCatalyst, GearMaterialIds.TuningChip, GearMaterialIds.VioletChip, GearMaterialIds.ResetChip,
            };
            int kinds = 0;
            foreach (ushort id in ids)
                if (work.Inventory.ServerGive(id, count) > 0) kinds++;
            return kinds == ids.Length ? $"Gave {count} of each forge material ({kinds} kinds)."
                : $"Gave {kinds} of {ids.Length} kinds: the rest are missing from the catalog (run Krytical Nova/Items/5) or the Pack is full.";
        }

        [Command("gm.forge", "Open the forge here. It then works anywhere, with no Armorer near, until the session ends; gm.forge off puts the rule back.")]
        private static string Forge(string which = "")
        {
            var network = Unity.Netcode.NetworkManager.Singleton;
            if (network != null && network.IsListening && !network.IsServer) return "Only the host or server can do that.";
            if (which == "off")
            {
                PlayerGearWork.GmAnywhere = false;
                return "The forge needs an Armorer again.";
            }
            PlayerGearWork.GmAnywhere = true;
            var windows = KryticalNova.Core.ClientPresentationRegistry.Services;
            bool opened = windows != null && windows.OpenService(KryticalNova.Core.ServiceIds.Forge);
            return opened ? "Forge opened. It works anywhere until gm.forge off."
                : "No HUD here to open the window in; forge requests work anywhere now.";
        }
    }
}
