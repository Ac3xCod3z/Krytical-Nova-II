using KryticalNova.CharacterCreation;
using KryticalNova.Crafting;
using KryticalNova.Economy;
using KryticalNova.Items;
using KryticalNova.Restyle;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console commands for Holoweave, dye, the Stylist / Reshaper / Coach and crafting. Like the GM
    /// commands they only run where the game is decided (host, server or offline) and act on the player picked by
    /// <see cref="GmTarget"/>. Until the Nova Store exists, gm.passes is the only way Restyle and Training Passes
    /// enter the game.
    /// </summary>
    public static class RestyleCommands
    {
        private const string NoComponent = "That player has no such component (run Krytical Nova/Setup/Player Prefab).";

        private static bool HostOnly(out string error)
        {
            var network = Unity.Netcode.NetworkManager.Singleton;
            error = network != null && network.IsListening && !network.IsServer ? "Only the host or server can do that." : null;
            return error == null;
        }

        private static string OpenWindow(string serviceId, string what)
        {
            var windows = KryticalNova.Core.ClientPresentationRegistry.Services;
            return windows != null && windows.OpenService(serviceId) ? $"{what} opened. It works anywhere until it is switched off."
                : $"No HUD here to open the window in; {what} requests work anywhere now.";
        }

        // ------------------------------------------------------------------ Holoweave and dye

        [Command("gm.weave", "Open the Holoweaver here. Weaves and dyes then work anywhere, with no NPC near, until the session ends; gm.weave off puts the rule back.")]
        private static string Weave(string which = "")
        {
            if (!HostOnly(out string error)) return error;
            if (which == "off")
            {
                PlayerWeaves.GmAnywhere = false;
                return "Weaves need a Holoweaver, and dyes an Outfitter, again.";
            }
            PlayerWeaves.GmAnywhere = true;
            return OpenWindow(KryticalNova.Core.ServiceIds.Holoweave, "Holoweaver");
        }

        [Command("gm.dye", "Open the Outfitter's dye window here. Dyes and Weaves then work anywhere until the session ends (gm.weave off puts the rule back).")]
        private static string Dye()
        {
            if (!HostOnly(out string error)) return error;
            PlayerWeaves.GmAnywhere = true;
            return OpenWindow(KryticalNova.Core.ServiceIds.Dye, "Outfitter");
        }

        [Command("gm.weave.unlock", "Add looks to the targeted player's Weave Library: gm.weave.unlock all, or an item by id or name (its look is added).")]
        private static string WeaveUnlock(string what = "all")
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerWeaveLibrary library) || !player.TryGetComponent(out PlayerInventory inventory)) return NoComponent;
            if (inventory.Catalog == null) return "No item catalog.";
            if (what != "all")
            {
                ItemDefinition item = inventory.Catalog.Find(what);
                if (item == null) return $"Unknown item '{what}'. Try item.list.";
                if (WeaveRules.KeyOf(item) == 0) return $"{item.displayName} has no look to collect.";
                return library.ServerCollectLine(WeaveRules.KeyOf(item)) ? $"{item.displayName} added. {library.Count} look(s) collected." : "That look was collected already.";
            }
            int added = 0;
            foreach (ItemDefinition item in inventory.Catalog.Items)
                if (library.ServerCollectLine(WeaveRules.KeyOf(item))) added++;
            return $"{added} look(s) added. {library.Count} collected.";
        }

        [Command("gm.weave.clear", "Empty the targeted player's Weave Library and take every Weave and dye off the character.")]
        private static string WeaveClear()
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerWeaveLibrary library) || !player.TryGetComponent(out PlayerWeaves weaves)) return NoComponent;
            library.ServerClear();
            weaves.ServerSetLooks(default);
            weaves.ServerClearDyes();
            return "Weave Library emptied; no Weave is on the character and no dye on what it wears.";
        }

        [Command("gm.weave.list", "Print what the targeted player shows in each slot (worn piece, Weave, dye) and how many looks its library holds.")]
        private static string WeaveList()
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerWeaveLibrary library) || !player.TryGetComponent(out PlayerEquipment equipment)) return NoComponent;
            var lines = new System.Text.StringBuilder($"{library.Count} look(s) collected.");
            foreach (EquipSlot slot in WeaveRules.WeaveSlots)
            {
                ItemDefinition worn = equipment.GetDefinition(slot), shown = equipment.GetShownDefinition(slot);
                ushort dye = equipment.Looks.GetDye(slot);
                string colour = dye != 0 && equipment.Colourways != null && equipment.Colourways.TryGet(dye, out var theme) ? $", dyed {theme.name}" : string.Empty;
                lines.Append($"\n{slot}: {(worn == null ? "nothing worn" : shown != worn ? $"{worn.displayName}, showing {shown.displayName}" : worn.displayName)}{colour}");
            }
            return lines.ToString();
        }

        // ------------------------------------------------------------------ Stylist, Reshaper, Coach

        [Command("gm.restyle", "Open the Stylist, the Reshaper or the Coach here: gm.restyle <stylist|reshaper|coach>. All three then work anywhere until the session ends; gm.restyle off puts the rule back.")]
        private static string Restyle(string which = "stylist")
        {
            if (!HostOnly(out string error)) return error;
            switch (which.ToLowerInvariant())
            {
                case "off":
                    PlayerRestyle.GmAnywhere = false;
                    return "A change of look needs its NPC again.";
                case "stylist": PlayerRestyle.GmAnywhere = true; return OpenWindow(KryticalNova.Core.ServiceIds.Stylist, "Stylist");
                case "reshaper": PlayerRestyle.GmAnywhere = true; return OpenWindow(KryticalNova.Core.ServiceIds.Reshaper, "Reshaper");
                case "coach": PlayerRestyle.GmAnywhere = true; return OpenWindow(KryticalNova.Core.ServiceIds.Coach, "Coach");
                default: return "gm.restyle <stylist|reshaper|coach|off>";
            }
        }

        [Command("gm.passes", "Give the targeted player what the appearance services use up: n Chroma Cells, n Prism Cells, n Restyle Passes and n Training Passes (10 unless told).")]
        private static string Passes(int count = 10)
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerInventory inventory)) return NoComponent;
            int kinds = 0;
            foreach (ushort id in new[] { CraftItemIds.ChromaCell, CraftItemIds.PrismCell, CraftItemIds.RestylePass, CraftItemIds.TrainingPass })
                if (inventory.ServerGive(id, count, earned: true) > 0) kinds++;
            return kinds == 4 ? $"Gave {count} Chroma Cells, {count} Prism Cells, {count} Restyle Passes and {count} Training Passes."
                : $"Only {kinds} of the 4 kinds fitted: the Pack is full, or the items are missing (run Krytical Nova/Items/5. Generate Utility Items).";
        }

        // ------------------------------------------------------------------ crafting

        [Command("gm.craft", "Open the Fabricator's bench here. Crafting then works anywhere, with no Fabricator near, until the session ends; gm.craft off puts the rule back.")]
        private static string Craft(string which = "")
        {
            if (!HostOnly(out string error)) return error;
            if (which == "off")
            {
                PlayerCrafting.GmAnywhere = false;
                return "Crafting needs a Fabricator again.";
            }
            PlayerCrafting.GmAnywhere = true;
            return OpenWindow(KryticalNova.Core.ServiceIds.Craft, "Fabricator");
        }

        [Command("gm.craftkit", "Give the targeted player what every recipe uses up, n times over (4 unless told): the four colour cells, every Dossier Page, and Alumite, Iridion and Orichalc by the four.")]
        private static string CraftKit(int times = 4)
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerCrafting crafting) || !player.TryGetComponent(out PlayerInventory inventory)) return NoComponent;
            if (crafting.Settings == null) return "No crafting settings (run Krytical Nova/Items/11. Build Crafting Recipes).";
            times = System.Math.Max(1, times);
            int kinds = 0, lost = 0;
            var given = new System.Collections.Generic.HashSet<ushort>();
            foreach (CraftRecipe recipe in crafting.Settings.recipes)
            {
                if (!recipe.IsValid) continue;
                foreach (CraftIngredient ingredient in recipe.ingredients)
                {
                    if (ingredient.itemId == 0 || !given.Add(ingredient.itemId)) continue;
                    if (inventory.ServerGive(ingredient.itemId, (int)CraftRules.Needed(recipe, ingredient.itemId) * times, earned: true) > 0) kinds++; else lost++;
                }
            }
            return $"Gave the materials of {crafting.Settings.recipes.Count} recipe(s), {times} time(s) over: {kinds} kind(s){(lost > 0 ? $", {lost} did not fit" : string.Empty)}.";
        }

        [Command("gm.dossier", "Give the targeted player the three Dossiers (Backlash, Iron Nerve, Shatterpoint), ready to read.")]
        private static string Dossiers()
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerInventory inventory)) return NoComponent;
            int given = 0;
            foreach (ushort id in new[] { CraftItemIds.BacklashDossier, CraftItemIds.IronNerveDossier, CraftItemIds.ShatterpointDossier })
                if (inventory.ServerGive(id, 1, earned: true) > 0) given++;
            return $"Gave {given} of the 3 Dossiers. Reading one needs level 80.";
        }

        [Command("gm.chips", "Give the targeted player the Skill Chips it can read: its faction's three classes' level 60 pair and level 80 pair, " +
                             "and Aegis, Killer Instinct and Hot Streak. With 'all', every chip of every class. Reading one needs the skill's level.")]
        private static string SkillChips(string which = "")
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerInventory inventory)) return NoComponent;
            bool all = which.Trim().ToLowerInvariant() == "all";
            player.TryGetComponent(out KryticalNova.Progression.PlayerCombatSkills skills);
            int given = 0, lost = 0;
            foreach (ushort id in CraftItemIds.SkillChips())
            {
                if (!all && (skills == null || skills.SkillTaughtBy(id) == null)) continue;
                if (inventory.ServerGive(id, 1, earned: true) > 0) given++; else lost++;
            }
            return $"Gave {given} Skill Chip(s){(lost > 0 ? $", {lost} did not fit" : string.Empty)}. Right-click one in the Pack to read it.";
        }

        [Command("gm.zenith", "Give the targeted player the Zenith Dossiers its faction's three weapon classes can read, or with 'all' all nine. " +
                              "Each unlocks that class's Zenith technique (level 80); in the game they drop only in the Prime grounds.")]
        private static string ZenithDossiers(string which = "")
        {
            if (!GmTarget.TryGetPlayer(out var player, out var error)) return error;
            if (!player.TryGetComponent(out PlayerInventory inventory)) return NoComponent;
            bool all = which.Trim().ToLowerInvariant() == "all";
            player.TryGetComponent(out KryticalNova.Progression.PlayerCombatSkills skills);
            int given = 0, lost = 0;
            foreach (ushort id in CraftItemIds.ZenithDossiers)
            {
                if (!all && (skills == null || skills.SkillTaughtBy(id) == null)) continue;
                if (inventory.ServerGive(id, 1, earned: true) > 0) given++; else lost++;
            }
            return $"Gave {given} Zenith Dossier(s){(lost > 0 ? $", {lost} did not fit" : string.Empty)}. Reading one needs level 80.";
        }
    }
}
