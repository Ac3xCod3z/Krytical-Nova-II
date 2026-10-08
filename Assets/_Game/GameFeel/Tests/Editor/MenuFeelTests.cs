using System;
using System.IO;
using System.Linq;
using KryticalNova.GameFeel;
using KryticalNova.UI.Menus;
using MoreMountains.Feedbacks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The feel of the menus and of Settings: every moment the interface can raise has a Feel player that moves
    /// something and has a slot for a sound; the menu scenes and the world all hold it; and a movement leaves the
    /// thing it moved exactly where whoever places it (an animator, a layout) has put it.
    /// </summary>
    public sealed class MenuFeelTests
    {
        private const string MenuFeelPath = "Assets/_Game/GameFeel/Prefabs/PFX_MenuFeel.prefab";
        private const string GameFeelPath = "Assets/_Game/GameFeel/Prefabs/PFX_GameFeel.prefab";
        private const string MenuSceneFolder = "Assets/_Game/Scenes/MainMenu/";
        private static readonly string[] MenuScenes = { "Title", "CharacterSelect", "FactionSelect", "CharacterCreator", "Loading" };

        private GameObject body;

        [TearDown]
        public void TearDown()
        {
            if (body != null) UnityEngine.Object.DestroyImmediate(body);
        }

        // ---------------------------------------------------------------- what is built

        [Test]
        public void EveryMenuMoment_HasAPlayer_ThatMovesSomethingAndCanSound()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuFeelPath);
            Assert.IsNotNull(prefab, "no menu feel at " + MenuFeelPath);
            var presenter = prefab.GetComponent<MenuFeelPresenter>();
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(prefab.GetComponent<MenuPunchDriver>(), "the prefab has the driver that does the moving");

            foreach (MenuMoment moment in Enum.GetValues(typeof(MenuMoment)))
            {
                MMF_Player player = presenter.PlayerOf(moment);
                Assert.IsNotNull(player, moment + " has no player");
                var punches = player.FeedbacksList.OfType<MMF_MenuPunch>().ToList();
                Assert.AreEqual(1, punches.Count, moment + " moves one thing, once");
                MMF_MenuPunch punch = punches[0];
                Assert.Greater(punch.Seconds, 0f, moment.ToString());
                Assert.LessOrEqual(punch.Seconds, 0.5f, moment + " is over before the next thing the player does");
                Assert.IsTrue(punch.Swell != Vector2.zero || punch.Nudge != Vector2.zero || punch.Turn != 0f, moment + " moves nothing");
                Assert.IsNotNull(punch.Shape);
                Assert.AreEqual(0f, punch.Shape.Evaluate(1f), 1e-3f, moment + " ends at rest");
                Assert.AreEqual(1, player.FeedbacksList.OfType<MMF_Sound>().Count(), moment + " has one slot for a sound");
            }
        }

        [Test]
        public void EveryPlayer_IsUsedByOneMomentOnly()
        {
            var presenter = AssetDatabase.LoadAssetAtPath<GameObject>(MenuFeelPath).GetComponent<MenuFeelPresenter>();
            var players = ((MenuMoment[])Enum.GetValues(typeof(MenuMoment))).Select(presenter.PlayerOf).ToList();
            Assert.AreEqual(players.Count, players.Distinct().Count());
        }

        [Test]
        public void TheWorld_CarriesTheMenuFeel_InTheGameFeelPrefab()
        {
            var feel = AssetDatabase.LoadAssetAtPath<GameObject>(GameFeelPath);
            Assert.IsNotNull(feel);
            var presenters = feel.GetComponentsInChildren<MenuFeelPresenter>(true);
            Assert.AreEqual(1, presenters.Length, "Settings in the game is answered by one menu feel");
            Assert.AreEqual(MenuFeelPath, PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(presenters[0].gameObject),
                "it is the same prefab the menus use, not a copy");
        }

        [Test]
        public void EveryMenuScene_HoldsTheMenuFeel()
        {
            string guid = AssetDatabase.AssetPathToGUID(MenuFeelPath);
            Assert.IsNotEmpty(guid);
            foreach (string scene in MenuScenes)
                StringAssert.Contains("guid: " + guid, File.ReadAllText(MenuSceneFolder + scene + ".unity"), scene + " has no menu feel");
        }

        // ---------------------------------------------------------------- the moving

        private static AnimationCurve OutAndBack() => new AnimationCurve(new Keyframe(0f, 0f, 0f, 2f), new Keyframe(0.5f, 1f, 0f, 0f), new Keyframe(1f, 0f, -2f, 0f));

        private MenuPunchDriver Driver(out RectTransform rect)
        {
            body = new GameObject("menu element", typeof(RectTransform));
            rect = (RectTransform)body.transform;
            return body.AddComponent<MenuPunchDriver>();
        }

        [Test]
        public void AMovement_GoesOut_AndLeavesTheThingAtRest()
        {
            MenuPunchDriver driver = Driver(out RectTransform rect);
            rect.localScale = new Vector3(2f, 2f, 2f);
            rect.anchoredPosition = new Vector2(40f, -10f);

            driver.Play(rect, null, 1f, new Vector2(0.1f, 0.1f), new Vector2(8f, 0f), 0f, OutAndBack());
            driver.Step(0.5f); // shown at its start, then half of its time passes
            Assert.AreEqual(2f, rect.localScale.x, 1e-4f);
            driver.Step(0.25f); // shown at its furthest
            Assert.AreEqual(2.2f, rect.localScale.x, 1e-3f);
            Assert.AreEqual(2f, rect.localScale.z, 1e-4f, "it swells across and up, not in depth");
            Assert.AreEqual(48f, rect.anchoredPosition.x, 1e-2f);
            driver.Step(0.5f);
            driver.Step(0.5f); // past its end
            Assert.AreEqual(new Vector3(2f, 2f, 2f), rect.localScale);
            Assert.AreEqual(new Vector2(40f, -10f), rect.anchoredPosition);
            Assert.AreEqual(0, driver.Moving);
        }

        [Test]
        public void WhatAnAnimatorPutsThereMeanwhile_IsWhereItRests()
        {
            MenuPunchDriver driver = Driver(out RectTransform rect);
            driver.Play(rect, null, 1f, new Vector2(0.1f, 0.1f), Vector2.zero, 0f, OutAndBack());
            driver.Step(0.5f);
            driver.Step(0.1f);
            Assert.AreEqual(1.1f, rect.localScale.x, 1e-3f);

            // The pack's own hover animation scales the row while the movement runs.
            rect.localScale = new Vector3(1.5f, 1.5f, 1f);
            driver.Step(0.1f);
            Assert.Greater(rect.localScale.x, 1.5f, "the movement goes on, on top of the animator's size");
            rect.localScale = new Vector3(1.5f, 1.5f, 1f);
            driver.Step(1f);
            driver.Step(1f);
            Assert.AreEqual(1.5f, rect.localScale.x, 1e-4f, "and ends on the animator's size, not on the one it started from");
        }

        [Test]
        public void ANewMovementOnTheSameThing_StartsFromRest()
        {
            MenuPunchDriver driver = Driver(out RectTransform rect);
            driver.Play(rect, null, 1f, new Vector2(0.2f, 0.2f), Vector2.zero, 0f, OutAndBack());
            driver.Step(0.5f);
            driver.Step(0.1f);
            Assert.Greater(rect.localScale.x, 1.1f);

            driver.Play(rect, null, 1f, new Vector2(0.2f, 0.2f), Vector2.zero, 0f, OutAndBack());
            Assert.AreEqual(1, driver.Moving, "one movement per thing");
            driver.Step(2f);
            driver.Step(2f);
            Assert.AreEqual(Vector3.one, rect.localScale, "hovering twice in a row does not leave it swollen");
        }

        [Test]
        public void AThingThatIsGone_IsLetGo()
        {
            MenuPunchDriver driver = Driver(out RectTransform _);
            var other = new GameObject("leaves with its scene", typeof(RectTransform));
            driver.Play(other.transform, null, 1f, new Vector2(0.1f, 0.1f), Vector2.zero, 0f, OutAndBack());
            Assert.AreEqual(1, driver.Moving);
            UnityEngine.Object.DestroyImmediate(other);
            Assert.DoesNotThrow(() => driver.Step(0.1f));
            Assert.AreEqual(0, driver.Moving);
        }
    }
}
