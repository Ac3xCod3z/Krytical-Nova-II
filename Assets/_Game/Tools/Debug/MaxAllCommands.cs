using System.Collections.Generic;
using System.Text;
using KryticalNova.Autopilot;
using KryticalNova.Core;
using KryticalNova.Crafting;
using KryticalNova.Economy;
using KryticalNova.Factions;
using KryticalNova.Forge;
using KryticalNova.Items;
using KryticalNova.Mounts;
using KryticalNova.Persistence;
using KryticalNova.Pets;
using KryticalNova.Player;
using KryticalNova.Progression;
using QFSW.QC;
using UnityEngine;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// gm.maxall, "max everything" (owner's night list item 22, 13 Nov 2026; picks 22.1-22.3 A): one command that turns the targeted
    /// test character into a fully maxed one, adding on top of what it has (nothing is taken). It calls the existing GM commands where
    /// they already do the job and does the rest itself: the caller's own Rare and Epic gear (Epic at the Temper cap and Fusion 12),
    /// every mount and pet at 100 %, the Autopilot Passes. Creds go through the wallet (<c>CredsSource.Gm</c>); items into the Pack are
    /// written to the kept ledger by <see cref="GmCommandRelay"/>, items it puts in the Vault or Deep Vault by this command, and a gm.log
    /// line names the GM, the target and the totals. Owner, 14 Nov 2026: any GM may use it whenever they want, on the real server and the
    /// test server alike (<see cref="MaxAllRules.Refusal"/>), and only with the word confirm, because it changes a whole character.
    /// <para>One line per step in the owner's order. The gear is placed after the stackable items so they keep their Pack room; the gear
    /// goes Pack, then Vault, then Deep Vault, and whatever finds no room is the final "did not fit" line.</para>
    /// </summary>
    public static class MaxAllCommands
    {
        [Command("gm.maxall", "Max everything on the targeted character, on top of what it has: max level and skills, Ascension and " +
                              "Standings, every mount and pet at 100 %, 3 days of Autopilot and Autoward, max Serums, its own faction's and lineage's " +
                              "Rare gear (clean) and Epic gear (Temper cap, Fusion 12), forge materials x999, every Pass x50, every crafting item, " +
                              "2,000,000 Creds, 50 Chroma and 50 Prism Cells. GM only: gm.maxall confirm.")]
        internal static string MaxAll(string confirm = "")
        {
            // the usual GM gate: only the host, the server's own window or control page, or a bound GM's relayed line has authority here
            bool isGm = GmTarget.HasAuthority(out _);
            if (!GmTarget.TryGetPlayer(out GameObject player, out string error)) return isGm ? error : MaxAllRules.Refusal(false, confirm);
            string who = Name(player);
            string refusal = MaxAllRules.Refusal(isGm, confirm);
            if (refusal != null)
                return isGm
                    ? $"gm.maxall maxes {who} ({GmTarget.Describe()}): it adds on top of what the character has and takes nothing away. " +
                      "Type gm.maxall confirm to do it (gm.target <clientId> picks someone else first)."
                    : refusal;
            s_Gear = s_Pets = s_Mounts = 0;
            long credsBefore = player.TryGetComponent(out PlayerWallet wallet) ? wallet.Creds : 0;

            var lines = new List<string>(16) { $"gm.maxall on {who} ({GmTarget.Describe()}):" };
            var notFit = new List<string>();
            lines.Add("Level: " + GmCommands.MaxLevel());
            lines.Add("Skills: " + GmCommands.MaxAllSkills());
            lines.Add("Ascension and Standings: " + AscensionAndStandings(player));
            lines.Add("Mounts: " + Mounts(player));
            lines.Add("Pets: " + Pets(player, notFit));
            lines.Add("Autopilot and Autoward time: " + AutopilotCommands.Time(MaxAllRules.PassDays) + " " + AutopilotCommands.AutobuffDays(MaxAllRules.PassDays));
            lines.Add("Serums: " + Serums(player));
            int gearLine = lines.Count;
            lines.Add(null); // filled below, once the stackable items have their Pack room
            lines.Add("Forge materials: " + ForgeCommands.ForgeKit(MaxAllRules.ForgeMaterials));
            lines.Add("Pass items: " + PassItems(player));
            lines.Add("Crafting items: " + CraftItems());
            lines.Add("Creds: " + EconomyCommands.Creds(MaxAllRules.Creds));
            lines.Add("Chroma and Prism Cells: " + CellsLine(player));
            lines[gearLine] = "Gear: " + Gear(player, notFit);
            SaveNow(player);
            lines.Add(MaxAllRules.DidNotFit(notFit));
            long credsGiven = wallet != null ? wallet.Creds - credsBefore : 0;
            KryticalNova.Networking.ServerRecords.Write(KryticalNova.Networking.RecordKind.Gm, MaxAllRules.RecordLine(GmCommandRelay.RunningFor,
                $"{who} ({KryticalNova.Networking.ServerRecords.Who(player.transform)})", s_Gear, s_Pets, s_Mounts, credsGiven, notFit.Count));
            return string.Join("\n", lines);
        }

        // ------------------------------------------------------------------ the totals for gm.log (commands run on the game's thread only)

        private static int s_Gear, s_Pets, s_Mounts;

        private static string Name(GameObject player) =>
            player.TryGetComponent(out PlayerIdentity identity) ? identity.CharacterName : GmTarget.Describe();

        // ------------------------------------------------------------------ steps that need more than one existing command

        private static string AscensionAndStandings(GameObject player)
        {
            if (!player.TryGetComponent(out PlayerNova nova) || nova.Settings == null) return "that player has no Nova ladders.";
            NovaCommands.Ascension(nova.Settings.ascension.maxLevel);
            return NovaCommands.Standing("all", nova.Settings.standings.MaxLevel);
        }

        /// <summary>Every built mount not owned yet into the Mount window, then every owned one at 100 % growth and fed.</summary>
        private static string Mounts(GameObject player)
        {
            if (!player.TryGetComponent(out PlayerMounts mounts) || mounts.Catalog == null) return "that player has no mount system.";
            int was = mounts.SelectedId, given = 0, owned = 0, full = 0;
            mounts.ServerDismountNow(); // another box can only be chosen on foot
            foreach (MountDefinition mount in mounts.Catalog.Mounts)
                if (mount != null && mount.IsBuilt && !mounts.Owns(mount.mountId)) { mounts.ServerRegister(mount); given++; s_Mounts++; }
            foreach (MountDefinition mount in mounts.Catalog.Mounts)
            {
                if (mount == null || !mounts.Owns(mount.mountId)) continue;
                owned++;
                mounts.ServerSelect(mount.mountId); // the growth and feed calls act on the selected box
                if (mounts.SelectedId != mount.mountId) continue;
                mounts.ServerSetGrowth(MaxAllRules.FullGrowth);
                mounts.ServerFeed();
                if (mounts.GrowthOf(mount.mountId) >= MaxAllRules.FullGrowth) full++;
            }
            if (was != 0 && mounts.Owns(was)) mounts.ServerSelect(was);
            return $"{given} given, {full} of {owned} at 100 % growth and fed.";
        }

        /// <summary>Every pet not owned yet (Pack, worn, Vault, Deep Vault) given at 100 %, and every pet already owned set to 100 %.</summary>
        private static string Pets(GameObject player, List<string> notFit)
        {
            if (!player.TryGetComponent(out PlayerInventory inventory) || inventory.Catalog == null) return "that player has no Pack.";
            PetTuning tuning = player.TryGetComponent(out PlayerPet pet) && pet.Catalog != null ? pet.Catalog.Tuning : PetTuning.Default;
            ItemCatalog catalog = inventory.Catalog;
            bool IsPet(in ItemStack stack)
            {
                ItemDefinition item = stack.IsEmpty ? null : catalog.Get(stack.itemId);
                return item != null && item.equipSlot == EquipSlot.Pet;
            }

            var owned = new HashSet<ushort>();
            int raised = 0;
            // the Pack's pets
            inventory.ServerChangePack(cells =>
            {
                bool changed = false;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (!IsPet(cells[i])) continue;
                    owned.Add(cells[i].itemId);
                    ulong full = MaxAllRules.FullPet(cells[i].data, tuning);
                    if (full == cells[i].data) continue;
                    cells[i] = new ItemStack(cells[i].itemId, cells[i].count, full);
                    changed = true;
                    raised++;
                }
                return changed;
            });
            // the worn pet: its own component keeps the worn item's state
            ItemStack worn = inventory.GetEquipped(EquipSlot.Pet);
            if (IsPet(worn))
            {
                owned.Add(worn.itemId);
                if (pet != null && pet.Pet != null)
                {
                    pet.ServerSetGrowth(MaxAllRules.FullGrowth);
                    pet.ServerFeed();
                    raised++;
                }
            }
            // the Vault's and Deep Vault's pets
            foreach (PlayerStorage storage in player.GetComponents<PlayerStorage>())
                storage.ServerChangeCells(cells =>
                {
                    bool changed = false;
                    for (int i = 0; i < cells.Length; i++)
                    {
                        if (!IsPet(cells[i])) continue;
                        owned.Add(cells[i].itemId);
                        ulong full = MaxAllRules.FullPet(cells[i].data, tuning);
                        if (full == cells[i].data) continue;
                        cells[i] = new ItemStack(cells[i].itemId, cells[i].count, full);
                        changed = true;
                        raised++;
                    }
                    return changed;
                });

            var missing = new List<ItemStack>();
            foreach (ItemDefinition item in catalog.Items)
                if (item != null && item.equipSlot == EquipSlot.Pet && owned.Add(item.itemId))
                    missing.Add(new ItemStack(item.itemId, 1, MaxAllRules.FullPet(0UL, tuning)));
            Placed placed = Place(player, inventory, missing, "pets", notFit);
            s_Pets += placed.Total;
            return $"{placed.Total} given at 100 % growth and fed ({placed.Where}); {raised} already owned set to 100 %.";
        }

        private static string Serums(GameObject player)
        {
            if (!player.TryGetComponent(out PlayerSerums serums) || serums.Settings == null) return "that player has no Serums.";
            int kinds = SerumRules.KindCount(serums.Settings);
            for (int i = 0; i < kinds; i++) SerumCommands.Set((i + 1).ToString(), serums.Settings.Cap);
            return $"all {kinds} kinds taken to the cap ({serums.Settings.Cap}).";
        }

        /// <summary>The caller's faction and lineage Rare and Epic gear of every ladder level and slot, in its class line.</summary>
        private static string Gear(GameObject player, List<string> notFit)
        {
            if (!player.TryGetComponent(out PlayerInventory inventory) || inventory.Gear == null || inventory.Catalog == null) return "that player has no Pack.";
            var wearer = player.GetComponent<IGearWearer>();
            var source = player.GetComponent<IWeaponClassSource>();
            FactionId faction = wearer != null ? wearer.Faction : FactionId.None;
            BodyFit body = wearer != null ? wearer.Body : BodyFit.None;
            string pack = wearer != null ? wearer.GearPack : null;
            WeaponClassId cls = source != null ? source.ActiveClass : wearer != null ? wearer.Discipline : WeaponClassId.None;
            int temperCap = player.TryGetComponent(out PlayerGearWork work) && work.Settings != null ? work.Settings.temper.hardCap : GearItemState.MaxTemper;

            var pieces = new List<ItemStack>();
            int rare = 0, epic = 0;
            foreach (GearDictionary.Entry entry in MaxAllRules.PickGear(inventory.Gear.Entries, faction, body, pack, cls))
            {
                if (inventory.Catalog.Get(entry.itemId) == null) continue;
                pieces.Add(new ItemStack(entry.itemId, 1, MaxAllRules.GearState(entry.rarity, temperCap)));
                if (entry.rarity == ItemRarity.Epic) epic++; else rare++;
            }
            if (pieces.Count == 0) return "no Rare or Epic gear fits this character. Re-bake the gear ladder.";
            Placed placed = Place(player, inventory, pieces, "gear", notFit);
            s_Gear += placed.Total;
            string line = cls != WeaponClassId.None ? $", weapons of the {WeaponClasses.DisplayName(cls)} line" : string.Empty;
            return $"{rare} Rare (clean) and {epic} Epic (Temper +{temperCap}%, Fusion {GearItemState.MaxFusion}) pieces of {faction} {pack}{line}: {placed.Where}.";
        }

        private static string PassItems(GameObject player)
        {
            var parts = new List<string>(3) { RestyleCommands.Passes(MaxAllRules.PassItems) };
            if (player.TryGetComponent(out PlayerAutobuff autobuff) && autobuff.Settings != null && autobuff.Settings.autobuffTiers != null)
                foreach (PassTier tier in autobuff.Settings.autobuffTiers)
                    parts.Add(AutopilotCommands.Autoward(tier.days.ToString(), MaxAllRules.PassItems));
            parts.Add(AutopilotPasses(player));
            return string.Join(" ", parts);
        }

        /// <summary>The Autopilot Passes (items 9030, 9034, 9035): no GM command gives them, so this does.</summary>
        private static string AutopilotPasses(GameObject player)
        {
            if (!player.TryGetComponent(out PlayerAutopilot autopilot) || autopilot.Settings == null || autopilot.Settings.autopilotTiers == null)
                return "No Autopilot Passes: Autopilot is not set up.";
            if (!player.TryGetComponent(out PlayerInventory inventory)) return "No Autopilot Passes: that player has no Pack.";
            int kinds = 0, lost = 0;
            foreach (PassTier tier in autopilot.Settings.autopilotTiers)
            {
                if (tier.passItemId == 0 || inventory.Catalog == null || inventory.Catalog.Get(tier.passItemId) == null) continue;
                if (inventory.ServerGive(tier.passItemId, MaxAllRules.PassItems, earned: true) > 0) kinds++; else lost++;
            }
            return $"{MaxAllRules.PassItems} of each of {kinds} Autopilot Passes into the Pack." + (lost > 0 ? $" {lost} kind(s) did not fit." : string.Empty);
        }

        private static string CraftItems() =>
            string.Join(" ", RestyleCommands.CraftKit(MaxAllRules.CraftKitTimes), RestyleCommands.Dossiers(),
                RestyleCommands.SkillChips(), RestyleCommands.ZenithDossiers());

        /// <summary>Tops the Chroma and Prism Cells up to 50 each when the Pass items (gm.passes) left fewer.</summary>
        private static string CellsLine(GameObject player)
        {
            if (!player.TryGetComponent(out PlayerInventory inventory)) return "that player has no Pack.";
            int chroma = TopUp(inventory, CraftItemIds.ChromaCell), prism = TopUp(inventory, CraftItemIds.PrismCell);
            return $"{chroma} Chroma Cells and {prism} Prism Cells in the Pack.";
        }

        private static int TopUp(PlayerInventory inventory, ushort itemId)
        {
            int have = Count(inventory, itemId);
            if (have < MaxAllRules.Cells) inventory.ServerGive(itemId, MaxAllRules.Cells - have, earned: true);
            return Count(inventory, itemId);
        }

        private static int Count(PlayerInventory inventory, ushort itemId)
        {
            int total = 0;
            for (int i = 0; i < PlayerInventory.Capacity; i++)
            {
                ItemStack stack = inventory.Get(i);
                if (!stack.IsEmpty && stack.itemId == itemId) total += stack.count;
            }
            return total;
        }

        // ------------------------------------------------------------------ Pack, then Vault, then Deep Vault

        private struct Placed
        {
            public int Pack, Vault, Deep;
            public int Total => Pack + Vault + Deep;
            public string Where => $"{Pack} in the Pack, {Vault} in the Vault, {Deep} in the Deep Vault";
        }

        /// <summary>
        /// One-cell pieces (gear, pets) into the Pack, then the Vault, then the Deep Vault (owner's pick 22.1 A). The storage moves are
        /// written to the kept ledger here (the Pack's are written by <see cref="GmCommandRelay"/>); whatever finds no room goes on
        /// <paramref name="notFit"/>.
        /// </summary>
        private static Placed Place(GameObject player, PlayerInventory inventory, List<ItemStack> pieces, string what, List<string> notFit)
        {
            var placed = new Placed();
            var waiting = new List<ItemStack>(pieces.Count);
            foreach (ItemStack piece in pieces)
            {
                if (inventory.ServerGive(piece, earned: true) > 0) placed.Pack++;
                else waiting.Add(piece);
            }
            if (waiting.Count > 0)
            {
                placed.Vault = Store(player, inventory, player.GetComponent<PlayerVault>(), waiting, what);
                placed.Deep = Store(player, inventory, player.GetComponent<PlayerDeepVault>(), waiting, what);
            }
            foreach (ItemStack left in waiting)
            {
                ItemDefinition item = inventory.Catalog.Get(left.itemId);
                notFit.Add(item != null ? item.displayName : "item " + left.itemId);
            }
            return placed;
        }

        private static int Store(GameObject player, PlayerInventory inventory, PlayerStorage storage, List<ItemStack> waiting, string what)
        {
            if (storage == null || waiting.Count == 0) return 0;
            var stored = new List<ItemStack>();
            storage.ServerChangeCells(cells => MaxAllRules.FillEmpty(cells, waiting, stored) > 0);
            if (stored.Count == 0) return 0;
            LedgerEntry line = EconomyLedger.Line(player.transform, LedgerKinds.Gm, null, $"{GmCommandRelay.RunningFor}: gm.maxall {what} into the {storage.DisplayName}");
            foreach (ItemStack stack in stored) line.Item(inventory.Catalog, stack, 1);
            EconomyLedger.Keep(line);
            return stored.Count;
        }

        /// <summary>Everything the command changed, saved now as one change (character, account and the ledger lines with it).</summary>
        private static void SaveNow(GameObject player)
        {
            if (player.TryGetComponent(out PlayerSaveAgent agent)) ServerSave.SaveTogether(agent);
        }
    }
}
