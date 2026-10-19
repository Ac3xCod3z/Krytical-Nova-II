using KryticalNova.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using KryticalNova.Core;

namespace KryticalNova.Tests
{
    /// <summary>
    /// Saved keys are put right when they are loaded: a key no action may be on is taken back, so the action is on
    /// its own key again. Works on a copy of the project's input asset; nothing stored is read or written.
    /// </summary>
    public sealed class KeyBindingsTests
    {
        private const string ActionsPath = "Assets/_Game/Input/Actions/InputSystem_Actions.inputactions";
        private InputActionAsset actions;

        [SetUp]
        public void SetUp()
        {
            var source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            Assert.IsNotNull(source, ActionsPath);
            actions = Object.Instantiate(source);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(actions);

        private InputAction Action(string map, string name) => actions.FindActionMap(map, true).FindAction(name, true);

        [Test]
        public void AKeyMayBeAKeyboardKeyOrASpareMouseButton()
        {
            Assert.IsTrue(KeyBindings.IsAllowedPath("<Keyboard>/e"));
            Assert.IsTrue(KeyBindings.IsAllowedPath("<Mouse>/middleButton"));
            Assert.IsFalse(KeyBindings.IsAllowedPath("<Mouse>/leftButton"), "the game is played with it");
            Assert.IsFalse(KeyBindings.IsAllowedPath("<Mouse>/rightButton"));
            Assert.IsFalse(KeyBindings.IsAllowedPath("<Keyboard>/escape"), "a fixed key");
            Assert.IsFalse(KeyBindings.IsAllowedPath("<Gamepad>/buttonSouth"));
            Assert.IsFalse(KeyBindings.IsAllowedPath(string.Empty));
            Assert.IsFalse(KeyBindings.IsAllowedPath(null));
        }

        [Test]
        public void TIsTheFifteenthHotbarSlotAndTheKioskOpensOnJ()
        {
            // The owner, 16 Oct 2026: T is a hotbar key like Q W E R; the Kiosk window it opened moves to J.
            Assert.AreEqual("<Keyboard>/t", Action("SkillCommands", "SlotT").bindings[0].path);
            Assert.AreEqual("<Keyboard>/j", Action("Windows", "Kiosk").bindings[0].path);
            foreach (InputActionMap map in actions.actionMaps)
                foreach (InputBinding binding in map.bindings)
                    if (binding.path == "<Keyboard>/t") Assert.AreEqual("SlotT", binding.action, $"{map.name}/{binding.action} is on T too");
        }

        [Test]
        public void Clean_TakesBackASavedKeyTheGameIsPlayedWith()
        {
            // As it was found saved on the owner's machine: Hotbar 13 on the left mouse button, and E on nothing.
            actions.LoadBindingOverridesFromJson(
                "{\"bindings\":[{\"action\":\"SkillCommands/SlotE\",\"id\":\"282bd0a0-88a1-4bfe-9388-011000000015\"," +
                "\"path\":\"<Mouse>/leftButton\",\"interactions\":\"null\",\"processors\":\"null\"}]}");
            InputAction slot = Action("SkillCommands", "SlotE");
            Assert.AreEqual("<Mouse>/leftButton", slot.bindings[0].effectivePath, "the saved key loaded");

            Assert.IsTrue(KeyBindings.Clean(actions));
            Assert.AreEqual("<Keyboard>/e", slot.bindings[0].effectivePath);
            Assert.IsFalse(KeyBindings.Clean(actions), "nothing left to put right");
        }

        [Test]
        public void Clean_TakesBackAnActionLeftWithoutAKey()
        {
            InputAction slot = Action("SkillCommands", "SlotQ");
            slot.ApplyBindingOverride(0, string.Empty);
            Assert.IsTrue(KeyBindings.Clean(actions));
            Assert.AreEqual("<Keyboard>/q", slot.bindings[0].effectivePath);
        }

        [Test]
        public void Clean_LeavesTheKeysThePlayerChose()
        {
            InputAction slot = Action("SkillCommands", "SlotR");
            slot.ApplyBindingOverride(0, "<Keyboard>/v");
            // Draw / sheathe ships on both Shift keys; choosing one switches the other off.
            InputAction weapon = Action("PointAndClick", "ToggleWeapon");
            weapon.ApplyBindingOverride(0, "<Mouse>/backButton");
            weapon.ApplyBindingOverride(1, string.Empty);

            Assert.IsFalse(KeyBindings.Clean(actions));
            Assert.AreEqual("<Keyboard>/v", slot.bindings[0].effectivePath);
            Assert.AreEqual("<Mouse>/backButton", weapon.bindings[0].effectivePath);
            Assert.AreEqual(string.Empty, weapon.bindings[1].effectivePath);
        }
    }
}
