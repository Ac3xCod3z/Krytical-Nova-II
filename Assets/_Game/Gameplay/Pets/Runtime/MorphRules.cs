namespace KryticalNova.Pets
{
    /// <summary>
    /// Morphweave: showing the look of one pet over another, the way a Weave shows one piece of gear over another
    /// (<c>WeaveRules</c>). A Morph is one pet at one of its five growth stages. An account collects it when a pet
    /// evolves into that stage while out with one of its characters, or when a pet is put out at that stage: so a
    /// pet bought fully grown gives its last look only, never the ones it grew through in other hands. A Morph goes
    /// over any pet, but never above the stage the pet that is out has reached itself. Free of networking so it
    /// can be tested; every machine works out what is shown with <see cref="Shown"/>.
    /// </summary>
    public static class MorphRules
    {
        public const string ReasonNotAMorph = "That is no Morph.";
        public const string ReasonNotOwned = "That Morph is not in your Weave Library.";
        public const string ReasonNoPet = "Put a pet out first.";

        /// <summary>The key a Morph is collected under: the pet and the stage. 0 = nothing to collect.</summary>
        public static uint Key(ushort petItemId, int stage) =>
            petItemId == 0 || stage < 0 || stage > PetRules.LastStage ? 0u : ((uint)petItemId << 8) | (uint)stage;

        public static ushort PetOf(uint key) => (ushort)(key >> 8);
        public static int StageOf(uint key) => (int)(key & 0xFF);

        /// <summary>True when a pet at <paramref name="wornStage"/> has grown far enough to show a Morph of <paramref name="morphStage"/>.</summary>
        public static bool Suits(int wornStage, int morphStage) => morphStage >= 0 && morphStage <= wornStage;

        /// <summary>
        /// What the pet that is out shows: the Morph when the pet has grown as far as the Morph's stage, itself
        /// otherwise. False when a Morph is shown.
        /// </summary>
        public static bool Shown(PetDefinition worn, int wornStage, PetDefinition morph, int morphStage, out PetDefinition pet, out int stage)
        {
            pet = worn;
            stage = wornStage;
            if (worn == null || morph == null || !Suits(wornStage, morphStage)) return false;
            if (morph == worn && morphStage == wornStage) return false; // its own look is no Morph
            pet = morph;
            stage = morphStage;
            return true;
        }

        public static string ReasonNotGrown(int morphStage) =>
            $"Your pet has not grown that far: this Morph needs {PetRules.StageStart(morphStage)}% growth.";

        /// <summary>Why a Morph may not go over the pet that is out (shown to the player), or null.</summary>
        public static string ApplyBlockedReason(PetDefinition worn, int wornStage, PetDefinition morph, int morphStage, bool owned)
        {
            if (morph == null || Key(morph.itemId, morphStage) == 0) return ReasonNotAMorph;
            if (!owned) return ReasonNotOwned;
            if (worn == null) return ReasonNoPet;
            if (!Suits(wornStage, morphStage)) return ReasonNotGrown(morphStage);
            return null;
        }

        /// <summary>A Morph as players read it: "Kitsune Spirit, Nine Tails".</summary>
        public static string NameOf(PetDefinition pet, int stage) =>
            pet == null ? string.Empty : $"{pet.displayName}, {pet.StageName(stage)}";
    }
}
