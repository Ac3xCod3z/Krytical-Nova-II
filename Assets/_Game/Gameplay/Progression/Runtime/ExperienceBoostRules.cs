
namespace KryticalNova.Progression
{
    /// <summary>
    /// The sums of the XP boosters (<see cref="EconomySettings.boosters"/>), free of the scene so they can be tested.
    /// Boosters that run together add up: +50 % and +50 % is +100 %, never +125 %. Using the same booster again adds
    /// its time to what is left, up to the most a character may hold.
    /// </summary>
    public static class ExperienceBoostRules
    {
        /// <summary>The share added to a character's XP (0.5 = +50 %) by every booster that still has time.</summary>
        public static float CharacterBonus(ExperienceBooster[] boosters, float[] secondsLeft) => Bonus(boosters, secondsLeft, pet: false);

        /// <summary>The share added to a pet's XP by every booster that still has time.</summary>
        public static float PetBonus(ExperienceBooster[] boosters, float[] secondsLeft) => Bonus(boosters, secondsLeft, pet: true);

        private static float Bonus(ExperienceBooster[] boosters, float[] secondsLeft, bool pet)
        {
            if (boosters == null || secondsLeft == null) return 0f;
            int percent = 0;
            for (int i = 0; i < boosters.Length && i < secondsLeft.Length; i++)
                if (secondsLeft[i] > 0f) percent += pet ? boosters[i].petPercent : boosters[i].characterPercent;
            return percent > 0 ? percent / 100f : 0f;
        }

        /// <summary>The time left after one more of a booster is used.</summary>
        public static float AfterUse(float secondsLeft, int minutes, int maxHours) =>
            System.Math.Min(System.Math.Max(0f, secondsLeft) + minutes * 60f, maxHours * 3600f);

        /// <summary>True when one more would be wasted: what is left and one more would pass the most a character may hold.</summary>
        public static bool WouldWaste(float secondsLeft, int minutes, int maxHours) =>
            System.Math.Max(0f, secondsLeft) + minutes * 60f > maxHours * 3600f + 0.5f;

        /// <summary>A kill's XP with the boosters' share added.</summary>
        public static uint Boosted(uint amount, float bonus) =>
            bonus <= 0f ? amount : (uint)System.Math.Min(uint.MaxValue, System.Math.Round(amount * (1.0 + bonus)));
    }
}
