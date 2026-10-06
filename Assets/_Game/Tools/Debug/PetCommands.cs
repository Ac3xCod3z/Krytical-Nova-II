using KryticalNova.Pets;
using QFSW.QC;

namespace KryticalNova.DevTools
{
    /// <summary>
    /// Quantum Console pet commands. Like the GM commands they only run where the game is decided (host, server or
    /// offline) and act on the player picked by <see cref="GmTarget"/>. They change the pet that is out.
    /// </summary>
    public static class PetCommands
    {
        private const string NoPet = "That player has no pet out. Equip one first (item.give Pet_Emberwing, then equip it).";

        private static bool TryGetPet(out PlayerPet pet, out string error)
        {
            pet = null;
            if (!GmTarget.TryGetPlayer(out var player, out error)) return false;
            pet = player.GetComponent<PlayerPet>();
            if (pet == null) { error = "That player has no pet system (run Krytical Nova/Setup/Player Prefab)."; return false; }
            if (pet.Pet == null) { error = NoPet; return false; }
            return true;
        }

        private static string Describe(PlayerPet pet)
        {
            PetItemState state = pet.State;
            string bonus = KryticalNova.Progression.StatDisplay.FormatModifier(pet.Pet.stat, KryticalNova.Progression.ModifierType.Flat, pet.StatBonus);
            return $"{pet.Pet.displayName}: {pet.GrowthPercent}% growth ({state.Xp:0.##} XP), {pet.Pet.StageName(pet.Stage)}, " +
                   $"hunger {state.HungerPercent:0.###}%, bonus {(pet.IsFed ? bonus : "off (starving)")}.";
        }

        [Command("gm.pet", "Print the growth, stage, hunger and bonus of the pet the targeted player has out.")]
        private static string Status() => TryGetPet(out var pet, out var error) ? Describe(pet) : error;

        [Command("gm.pet.xp", "Give pet XP to the pet that is out (100,000 fills the growth meter; a Hostile kill is worth 0.04).")]
        private static string Xp(float amount)
        {
            if (!TryGetPet(out var pet, out var error)) return error;
            if (amount <= 0f) return "Give a positive amount of pet XP.";
            pet.ServerAddXp(amount);
            return Describe(pet);
        }

        [Command("gm.pet.growth", "Set the growth of the pet that is out, 0-100. Evolutions happen at 25, 50, 75 and 100.")]
        private static string Growth(float percent)
        {
            if (!TryGetPet(out var pet, out var error)) return error;
            pet.ServerSetGrowth(percent);
            return Describe(pet);
        }

        [Command("gm.pet.hunger", "Set the hunger of the pet that is out, 0-100. At 0 it is starving: no bonus, no XP.")]
        private static string Hunger(float percent)
        {
            if (!TryGetPet(out var pet, out var error)) return error;
            pet.ServerSetHunger(percent);
            return Describe(pet);
        }

        [Command("gm.pet.feed", "Feed the pet that is out to 100% hunger without a Pet Ration.")]
        private static string Feed()
        {
            if (!TryGetPet(out var pet, out var error)) return error;
            pet.ServerFeed();
            return Describe(pet);
        }

        [Command("gm.pet.event", "Give the pet that is out its share of a PvP event: gm.pet.event win (50 XP) or gm.pet.event loss (25 XP).")]
        private static string Event(string result = "win")
        {
            if (!TryGetPet(out var pet, out var error)) return error;
            bool won = !result.Equals("loss", System.StringComparison.OrdinalIgnoreCase) && !result.Equals("lose", System.StringComparison.OrdinalIgnoreCase);
            pet.ServerAwardEventXp(won);
            return Describe(pet);
        }
    }
}
