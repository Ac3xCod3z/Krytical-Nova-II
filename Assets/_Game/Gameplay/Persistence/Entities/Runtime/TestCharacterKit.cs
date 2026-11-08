using KryticalNova.Progression;
using UnityEngine;

namespace KryticalNova.Persistence.Entities
{
    /// <summary>
    /// A test character as the owner wants every one (5-6 Nov 2026, findings item 20; memory test-players-fully-leveled): the maximum
    /// level and every skill at max rank, set on its server body. One copy for Play DOTS here (KnPlayHere, Editor) and the dedicated
    /// entity test server (PlayerAdmission, test-lane builds only: <c>KN_TEST_LANE</c>), moved here from KnPlayHere on 8 Nov 2026.
    /// </summary>
    public static class TestCharacterKit
    {
        /// <summary>Every combat skill and the movement ranks at max on <paramref name="serverBody"/>; false when it has no skills.</summary>
        public static bool GiveEverySkill(GameObject serverBody)
        {
            var skills = serverBody != null ? serverBody.GetComponent<PlayerCombatSkills>() : null;
            if (skills == null) return false;
            skills.GmSetAllRanksToMax();
            var movement = serverBody.GetComponent<PlayerMovementSkills>();
            if (movement != null) movement.GmSetMaxRanks();
            return true;
        }

        /// <summary>The maximum level and every skill at max rank; false when it has no progression or could not be raised (yet).</summary>
        public static bool MakeTestReady(GameObject serverBody)
        {
            var progression = serverBody != null ? serverBody.GetComponent<PlayerCharacterProgression>() : null;
            if (progression == null) return false;
            if (progression.CurrentLevel < progression.MaxLevel) progression.SetLevel(progression.MaxLevel);
            if (progression.CurrentLevel < progression.MaxLevel) return false;
            return GiveEverySkill(serverBody);
        }
    }
}
