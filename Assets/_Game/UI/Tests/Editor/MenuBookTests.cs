using System.IO;
using KryticalNova.EditorTools;
using KryticalNova.UI.Menus;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KryticalNova.Tests
{
    /// <summary>
    /// The menus' comic book: that the book is whole, that every menu scene turns its pages with it, and that the
    /// pages are in the order the menus are gone through (which is what decides the way a page turns).
    /// </summary>
    public sealed class MenuBookTests
    {
        private static readonly string[] MenuScenes =
        {
            FrontEnd.TitleScene, FrontEnd.CharacterSelectScene, FrontEnd.FactionSelectScene,
            FrontEnd.CharacterCreatorScene, FrontEnd.LoadingScene,
        };

        [Test]
        public void ThePages_AreInTheOrderTheMenusAreGoneThrough()
        {
            for (int i = 1; i < MenuScenes.Length; i++)
                Assert.Less(FrontEnd.PageOf(MenuScenes[i - 1]), FrontEnd.PageOf(MenuScenes[i]), MenuScenes[i - 1] + " comes before " + MenuScenes[i]);
            Assert.Less(FrontEnd.PageOf(FrontEnd.LoadingScene), FrontEnd.PageOf(FrontEnd.WorldScene), "the world is after the last page");
        }

        [Test]
        public void TheBook_IsWhole()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FrontEndBuilder.BookPrefabPath);
            Assert.IsNotNull(prefab, "no book at " + FrontEndBuilder.BookPrefabPath);
            // MegaBook also runs in edit mode: the prefab sleeps, so that it never builds a page into itself.
            Assert.IsFalse(prefab.activeSelf, "the book prefab is asleep until a page turns");
            Assert.AreEqual(0, prefab.GetComponentsInChildren<MeshRenderer>(true).Length, "no page is saved in the prefab");

            var book = prefab.GetComponent<MenuBook>();
            Assert.IsNotNull(book);
            var so = new SerializedObject(book);
            var builder = so.FindProperty("book").objectReferenceValue;
            Assert.IsNotNull(builder, "the book has no MegaBook Builder");
            Assert.AreEqual("MegaBookBuilder", builder.GetType().Name, "the page is turned by MegaBook, nothing else");
            Assert.IsNotNull(so.FindProperty("settings").objectReferenceValue, "the book has no settings asset");
            Assert.AreEqual(3, so.FindProperty("skins").arraySize, "one skin per faction, for the back of the page");

            var camera = so.FindProperty("bookCamera").objectReferenceValue as Camera;
            Assert.IsNotNull(camera);
            Assert.IsFalse(camera.enabled, "the book's camera draws only while a page turns");
            Assert.AreEqual(0f, camera.backgroundColor.a, "what is not page is see-through");
            Assert.AreEqual(1 << prefab.layer, camera.cullingMask, "the book's camera sees the book and nothing else");

            var leaf = new SerializedObject(builder);
            Assert.AreEqual(1, leaf.FindProperty("NumPages").intValue, "one leaf: the screen that is left");
            foreach (string material in new[] { "basematerial", "basematerial1", "basematerial2" })
            {
                var paper = leaf.FindProperty(material).objectReferenceValue as Material;
                Assert.IsNotNull(paper, material + " is not set");
                Assert.AreEqual("KryticalNova/UI/Comic Page", paper.shader.name);
                StringAssert.StartsWith("Assets/_Game/", AssetDatabase.GetAssetPath(paper), "the paper is ours, not the vendor's");
            }
        }

        [Test]
        public void EveryMenuScene_TurnsItsPagesWithTheBook()
        {
            string guid = AssetDatabase.AssetPathToGUID(FrontEndBuilder.BookPrefabPath);
            Assert.IsNotEmpty(guid);
            foreach (string scene in MenuScenes)
            {
                string text = File.ReadAllText(FrontEndBuilder.ScenePath(scene));
                StringAssert.Contains("guid: " + guid, text, scene + " does not refer to the book");
                StringAssert.Contains("m_Name: Page Still", text, scene + " has no picture of the screen that is left");
                StringAssert.Contains("m_Name: Page Turn", text, scene + " has no view of the turning page");
            }
        }

        [Test]
        public void TheTurn_StartsFlatAndEndsOver()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MenuBookSettings>(FrontEndBuilder.BookSettingsPath);
            Assert.IsNotNull(settings);
            Assert.AreEqual(0f, settings.turn.Evaluate(0f), 1e-3f, "the page starts flat: it is the screen that was left");
            Assert.AreEqual(1f, settings.turn.Evaluate(1f), 1e-3f, "and ends turned over");
            Assert.Greater(settings.turnSeconds, 0f);
            Assert.Less(settings.stage.y, -100f, "the book stands well away from anything a game camera looks at");
        }
    }
}
