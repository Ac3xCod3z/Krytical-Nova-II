using System.Collections;
using KryticalNova.CharacterCreation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Loading screen between character select and the world: loads the zone in the background, shows progress and a
    /// line about the character's faction, then lets the halftone wipe cover the switch.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadingScreenController : MonoBehaviour
    {
        [Tooltip("The Loading screen instance (Screen_FantasyMenus_Loading_01).")]
        [SerializeField] private Transform screen;
        [SerializeField] private CharacterCreationDatabase database;
        [Tooltip("Shown at least this long, so the screen never just flashes by.")]
        [SerializeField, Min(0f)] private float minimumSeconds = 1.6f;

        private static readonly string[] GeneralTips =
        {
            "Drag to turn your character on the equipment page. Scroll to zoom.",
            "Gear from your own faction is waiting in your bag.",
            "Three factions share this town for now. Nobody is drawing blades yet."
        };

        private IEnumerator Start()
        {
            var slider = screen != null ? screen.GetComponentInChildren<Slider>(true) : null;
            string who = CharacterSession.TryGetActive(out var profile) ? profile.name : null;
            MenuUi.SetLine(screen, "Label_FantasyMenus_Header", who != null ? $"{who} enters Starter Town" : "Entering Starter Town");
            MenuUi.SetText(screen, "Label_Tooltip", PickTip(profile));
            if (slider != null) slider.value = 0f;

            var load = SceneManager.LoadSceneAsync(FrontEnd.WorldScene);
            if (load == null)
            {
                Debug.LogError($"[Menus] Scene '{FrontEnd.WorldScene}' is not in the build settings.", this);
                yield break;
            }
            load.allowSceneActivation = false;

            float elapsed = 0f;
            float shown = 0f;
            // Unity reports 0.9 when the scene is loaded and only waiting to be activated.
            while (load.progress < 0.9f || elapsed < minimumSeconds || shown < 0.999f)
            {
                elapsed += Time.unscaledDeltaTime;
                float target = Mathf.Min(Mathf.Clamp01(load.progress / 0.9f), elapsed / Mathf.Max(minimumSeconds, 0.01f));
                shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 1.5f);
                if (slider != null) slider.value = shown;
                MenuUi.SetText(screen, "Label_LoadingPercentage", $"{Mathf.RoundToInt(shown * 100f)}%");
                yield return null;
            }

            // The world has no transition of its own, so this one goes along and uncovers it.
            var transition = MenuTransition.Current;
            if (transition != null) transition.Close(() => load.allowSceneActivation = true, carryIntoNextScene: true);
            else load.allowSceneActivation = true;
        }

        private string PickTip(CharacterProfile profile)
        {
            if (profile != null && database != null && database.Factions != null)
            {
                var faction = database.Factions.Get(profile.recipe.faction);
                if (faction != null && !string.IsNullOrEmpty(faction.tagline))
                    return $"{faction.displayName}: {faction.tagline}";
            }
            return GeneralTips[Random.Range(0, GeneralTips.Length)];
        }
    }
}
