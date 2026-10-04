using System;
using System.Collections;
using System.Collections.Generic;
using KryticalNova.CharacterCreation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KryticalNova.UI.Menus
{
    /// <summary>
    /// Loading screen between character select and the world. It does three things before it lets the world in:
    /// loads the zone in the background, warms what the character needs (its parts), and waits for anything else
    /// that asked to be ready first (<see cref="LoadingGate"/>). It also stays up for a fixed minimum time, so the
    /// screen is a dependable place to do real preloading later and never just flashes by.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LoadingScreenController : MonoBehaviour
    {
        [Tooltip("The Loading screen instance (Screen_FantasyMenus_Loading_01).")]
        [SerializeField] private Transform screen;
        [SerializeField] private CharacterCreationDatabase database;
        [Tooltip("The screen always runs at least this long, however fast the zone loads. The bar fills over this time.")]
        [SerializeField, Min(0f)] private float minimumSeconds = 4f;
        [Tooltip("Give up waiting for a registered task after this long and enter anyway (it is named in the log).")]
        [SerializeField, Min(1f)] private float taskTimeoutSeconds = 30f;

        private static readonly string[] GeneralTips =
        {
            "Drag to turn your character on the equipment page. Scroll to zoom.",
            "Gear from your own faction is waiting in your bag.",
            "Players of other factions are not your friends."
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
            yield return null; // draw the screen once before doing any work on the main thread
            WarmCharacter(profile);

            float elapsed = 0f;
            float shown = 0f;
            while (true)
            {
                elapsed += Time.unscaledDeltaTime;
                // Unity reports 0.9 when the scene is loaded and only waiting to be activated.
                float sceneProgress = Mathf.Clamp01(load.progress / 0.9f);
                string waitingFor = LoadingGate.FirstPending();
                if (waitingFor != null && elapsed > minimumSeconds + taskTimeoutSeconds)
                {
                    Debug.LogWarning($"[Menus] Loading task '{waitingFor}' did not finish in time; entering the world without it.", this);
                    LoadingGate.Clear();
                    waitingFor = null;
                }

                // The bar follows whichever is further behind: the clock, the scene, or the tasks (held just short of full).
                float target = Mathf.Min(sceneProgress, minimumSeconds > 0f ? elapsed / minimumSeconds : 1f);
                if (waitingFor != null) target = Mathf.Min(target, 0.95f);
                shown = Mathf.MoveTowards(shown, target, Time.unscaledDeltaTime * 1.5f);
                if (slider != null) slider.value = shown;
                MenuUi.SetText(screen, "Label_LoadingPercentage", waitingFor != null && sceneProgress >= 1f
                    ? $"{waitingFor}  {Mathf.RoundToInt(shown * 100f)}%"
                    : $"{Mathf.RoundToInt(shown * 100f)}%");

                if (sceneProgress >= 1f && elapsed >= minimumSeconds && waitingFor == null && shown >= 0.999f) break;
                yield return null;
            }

            // The world has no transition of its own, so this one goes along and uncovers it.
            var transition = MenuTransition.Current;
            if (transition != null) transition.Close(() => load.allowSceneActivation = true, carryIntoNextScene: true);
            else load.allowSceneActivation = true;
        }

        /// <summary>Loads the character's own parts now, so its first frame in the world does not hitch on them.</summary>
        private void WarmCharacter(CharacterProfile profile)
        {
            if (profile == null || database == null) return;
            RecipeResolver.Resolve(profile.recipe, database, new ResolvedAppearance());
        }

        private string PickTip(CharacterProfile profile)
        {
            if (profile != null && database != null && database.Factions != null)
            {
                var faction = database.Factions.Get(profile.recipe.faction);
                if (faction != null && !string.IsNullOrEmpty(faction.tagline))
                    return $"{faction.displayName}: {faction.tagline}";
            }
            return GeneralTips[UnityEngine.Random.Range(0, GeneralTips.Length)];
        }
    }

    /// <summary>
    /// Work the loading screen must wait for. A system that has something to finish before the player enters the
    /// world (downloading data, warming shaders, filling pools) registers a label and a "done yet?" check; the
    /// loading screen shows the label and holds the door until every check passes.
    /// </summary>
    public static class LoadingGate
    {
        private static readonly List<(string label, Func<bool> ready)> Tasks = new List<(string, Func<bool>)>();

        // Enter Play Mode runs without a domain reload, so statics must be reset by hand.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear() => Tasks.Clear();

        /// <summary>Hold the loading screen until <paramref name="ready"/> returns true. The label is shown to the player.</summary>
        public static void Require(string label, Func<bool> ready)
        {
            if (ready != null) Tasks.Add((label, ready));
        }

        /// <summary>The label of the first task still running, or null when nothing is pending. Finished tasks are dropped.</summary>
        public static string FirstPending()
        {
            for (int i = Tasks.Count - 1; i >= 0; i--)
            {
                bool done;
                try { done = Tasks[i].ready(); }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    done = true; // a broken task must not lock the player out
                }
                if (done) Tasks.RemoveAt(i);
            }
            return Tasks.Count > 0 ? Tasks[0].label : null;
        }
    }
}
