using KryticalNova.GameFeel;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools
{
    /// <summary>
    /// The feel of the menus and of Settings: one Feel player per menu moment (<see cref="MenuFeelPresenter"/>), in
    /// a prefab of its own that every menu scene holds and that the game feel prefab carries into the world. The
    /// numbers set here are the ones a first build starts from; from then on they are tuned on the prefab's players.
    /// </summary>
    public static partial class GameFeelBuilder
    {
        public const string MenuFeelPrefabPath = PrefabFolder + "/PFX_MenuFeel.prefab";
        private const string MenuFeelChildName = "Menu Feel";

        /// <summary>The menu feel prefab, built if there is none. An existing one is left as it was tuned.</summary>
        public static GameObject EnsureMenuFeel()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MenuFeelPrefabPath);
            if (existing != null && existing.GetComponent<MenuFeelPresenter>() != null) return existing;
            GameObject built = BuildMenuFeel();
            NestMenuFeel();
            return built;
        }

        /// <summary>
        /// Builds the menu feel prefab again from the numbers below (tuning on it is lost) and makes sure the game
        /// feel prefab carries it, so Settings feels the same in the world as from character select.
        /// </summary>
        [MenuItem("Krytical Nova/GameFeel/Rebuild Menu Feel")]
        public static void RebuildMenuFeel()
        {
            BuildMenuFeel();
            NestMenuFeel();
            AssetDatabase.SaveAssets();
            Debug.Log($"[GameFeel] Menu feel rebuilt at {MenuFeelPrefabPath}. Assign sounds on its MMF_Sound slots.");
        }

        private static GameObject BuildMenuFeel()
        {
            WeatherVfxBuilder.EnsureFolder(PrefabFolder);
            var root = new GameObject("PFX_MenuFeel");
            var driver = root.AddComponent<MenuPunchDriver>();
            var presenter = root.AddComponent<MenuFeelPresenter>();
            var so = new SerializedObject(presenter);
            so.FindProperty("driver").objectReferenceValue = driver;

            // The movements, over their time (0 to 1): out and back; out, past rest and back; side to side, dying
            // away; and starting out of place and coming to rest.
            AnimationCurve bump = Curve((0f, 0f), (0.35f, 1f), (1f, 0f));
            AnimationCurve kick = Curve((0f, 0f), (0.2f, 1f), (0.55f, -0.3f), (0.8f, 0.1f), (1f, 0f));
            AnimationCurve shake = Curve((0f, 0f), (0.1f, 1f), (0.26f, -0.85f), (0.42f, 0.65f), (0.58f, -0.45f), (0.74f, 0.25f), (0.88f, -0.1f), (1f, 0f));
            AnimationCurve settle = Curve((0f, 1f), (0.45f, 0.18f), (1f, 0f));

            void Moment(string field, string name, float seconds, Vector2 swell, Vector2 nudge, float turn, AnimationCurve shape)
            {
                MMF_Player player = Player(root, name, p =>
                {
                    var punch = p.AddFeedback(typeof(MMF_MenuPunch)) as MMF_MenuPunch;
                    punch.Label = name;
                    punch.Seconds = seconds;
                    punch.Swell = swell;
                    punch.Nudge = nudge;
                    punch.Turn = turn;
                    punch.Shape = shape;
                    Sound(p, name);
                });
                so.FindProperty(field).objectReferenceValue = player;
            }

            Vector2 none = Vector2.zero;
            Moment("hover", "Hover", 0.14f, new Vector2(0.035f, 0.035f), none, 0f, bump);
            Moment("press", "Press", 0.13f, new Vector2(-0.06f, -0.06f), none, 0f, kick);
            Moment("confirm", "Confirm", 0.24f, new Vector2(0.09f, 0.09f), none, 0f, kick);
            Moment("back", "Back", 0.18f, none, new Vector2(-10f, 0f), 0f, bump);
            Moment("error", "Error", 0.32f, none, new Vector2(12f, 0f), 0f, shake);
            Moment("toggle", "Toggle", 0.14f, new Vector2(0.06f, -0.04f), none, 0f, bump);
            Moment("slide", "Slide", 0.09f, new Vector2(0.12f, 0.12f), none, 0f, bump);
            Moment("open", "Open", 0.2f, new Vector2(-0.035f, -0.035f), none, 0f, settle);
            Moment("tab", "Tab", 0.16f, none, new Vector2(0f, -8f), 0f, settle);
            // The screen a page uncovers: it starts a touch large (a touch small on the way back) and settles.
            Moment("pageForward", "Page Forward", 0.4f, new Vector2(0.012f, 0.012f), none, 0f, settle);
            Moment("pageBack", "Page Back", 0.4f, new Vector2(-0.012f, -0.012f), none, 0f, settle);
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, MenuFeelPrefabPath);
            Object.DestroyImmediate(root);
            return saved;
        }

        /// <summary>Puts the menu feel inside the game feel prefab, once: the world's Settings window is answered by it.</summary>
        private static void NestMenuFeel()
        {
            var menuFeel = AssetDatabase.LoadAssetAtPath<GameObject>(MenuFeelPrefabPath);
            if (menuFeel == null || AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) return;
            using (var scope = new PrefabUtility.EditPrefabContentsScope(PrefabPath))
            {
                Transform root = scope.prefabContentsRoot.transform;
                if (root.Find(MenuFeelChildName) != null) return;
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(menuFeel, root);
                instance.name = MenuFeelChildName;
            }
        }

        private static AnimationCurve Curve(params (float time, float value)[] keys)
        {
            var curve = new AnimationCurve();
            foreach ((float time, float value) in keys) curve.AddKey(time, value);
            for (int i = 0; i < curve.length; i++) curve.SmoothTangents(i, 0f);
            return curve;
        }
    }
}
