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
            InputAction weapon = Action("PointAndClick", "ToggleWeapon");
            weapon.ApplyBindingOverride(0, "<Mouse>/backButton");

            Assert.IsFalse(KeyBindings.Clean(actions));
            Assert.AreEqual("<Keyboard>/v", slot.bindings[0].effectivePath);
            Assert.AreEqual("<Mouse>/backButton", weapon.bindings[0].effectivePath);
        }

        [Test]
        public void TheDrawKeyIsSpace_AndShiftIsOnlyTheClickModifier()
        {
            // The owner, 6 Nov 2026 (pick B): Shift as a click modifier also drew the weapon; draw / sheathe moves to Space (both games)
            InputAction weapon = Action("PointAndClick", "ToggleWeapon");
            Assert.AreEqual(1, weapon.bindings.Count, "one key");
            Assert.AreEqual("<Keyboard>/space", weapon.bindings[0].path);
            Assert.AreEqual("1a9467b2-3b6e-4229-afd6-e8661e98d14a", weapon.bindings[0].id.ToString(), "the same binding: a key a player chose for it still applies");
            Assert.IsTrue(KeyBindings.IsAllowedPath("<Keyboard>/space"));
            foreach (InputActionMap map in actions.actionMaps)
                foreach (InputBinding binding in map.bindings)
                {
                    if (binding.path == "<Keyboard>/space") Assert.AreEqual("ToggleWeapon", binding.action, $"{map.name}/{binding.action} is on Space too");
                    if (binding.path == "<Keyboard>/leftShift" || binding.path == "<Keyboard>/rightShift")
                        Assert.AreNotEqual("ToggleWeapon", binding.action, "Shift no longer draws the weapon");
                }
        }
    }
}
