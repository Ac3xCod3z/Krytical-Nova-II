using UnityEngine;
using System.Collections.Generic;
using UnityEditor;
using PSD.Creator.Objects;
using PSD.Creator.Inspector;
using PSD.Creator.Inspector.Utility;
using PSD.Preferences.Theming;
using System;
using PSD.Creator.Data;
using CTFX2.Creator.Objects;
using System.Linq;

namespace PSD.Creator.Editor.Inspector.Editors
{
    public abstract class BasicSettingsPage : Page
    {
        protected override string Settings => "basic";
        protected override bool Toggleable => false;

        public override string Title => "Basic Settings";
        public override string Subtitle => "Configure basic parameters";

        public override void DrawContent(SerializedObject target)
        {
            DrawProperty(target, "stopAction");
            DrawProperty(target, "ditherFade");
        }
    }

    public class ReferencePage : Page
    {
        public override string Category => "General";

        protected override string Settings => "reference";
        protected override bool Toggleable => false;
        protected override bool RequiresValidation => true;

        protected override List<(string name, bool valid)> GetValidationStates(SerializedObject target)
            => new List<(string name, bool valid)>
            {
                ("References", GetProperty(target, "references").objectReferenceValue != null),
                ("Default System in References Object", ((ReferencesObject)GetProperty(target, "references").objectReferenceValue)?.defaultSystem.system != null)
            };

        public override string Title => "Reference";
        public override string Subtitle => "You must first assign data before creating Cartoon FX";

        public override void DrawContent(SerializedObject target) => DrawProperty(target, "references");
    }

    public abstract class RandomizationPage : Page
    {
        public override string Category => "Randomization";

        protected override string Settings => "randomization";
        protected override bool Toggleable => false;
        protected override bool RequiresValidation => true;

        protected override List<(string name, bool valid)> GetValidationStates(SerializedObject target)
            => new List<(string name, bool valid)>
            {
                ("Randomization", GetProperty(target, "randomization").objectReferenceValue != null)
            };

        public override string Title => "Randomization";
        public override string Subtitle => "Lets you randomize the effect to try different variations.";

        private Texture2D btnBg, btnBgHover, btnBgActive;
        private GUIStyle style;

        public class RandomizationBuilder
        {
            private readonly List<(Randomization.ETargets flag, string page, Func<RandomizationObject, BillboardObject[]> pick)> entries = new();

            public RandomizationBuilder Add(Randomization.ETargets flag, string page, Func<RandomizationObject, BillboardObject[]> pick)
            {
                entries.Add((flag, page, pick));
                return this;
            }

            protected void Set<T>(SerializedObject target, string page, T value) where T : BillboardObject
            {
                var billboards = target.FindProperty($"{page}.billboards");
                if (billboards == null) return;
                for (int i = 0; i < billboards.arraySize; i++)
                {
                    var billboard = billboards.GetArrayElementAtIndex(i);
                    var obj = billboard.FindPropertyRelative("Object");
                    if (obj == null) continue;
                    obj.objectReferenceValue = value;
                }
            }

            protected void Set<T>(SerializedObject target, string page, RandomizationObject randomization, params T[] values) where T : BillboardObject
            {
                var billboards = target.FindProperty($"{page}.billboards");
                if (billboards == null) return;
                values = values.ToArray();
                for (int i = 0; i < billboards.arraySize; i++)
                {
                    var billboard = billboards.GetArrayElementAtIndex(i);
                    var obj = billboard.FindPropertyRelative("Object");
                    if (obj == null) continue;
                    var newObj = randomization.Get(values);
                    if (values.Length > 1)
                        values = values.Where(x => !ReferenceEquals(x, newObj)).ToArray();
                    obj.objectReferenceValue = newObj;
                }
            }

            internal void Apply(SerializedObject target, RandomizationObject randomization, Randomization.ETargets targets)
            {
                foreach (var (flag, page, pick) in entries)
                {
                    if (!targets.HasFlag(flag)) continue;

                    var array = pick(randomization);
                    if (array == null || array.Length == 0) continue;

                    var sample = array[0];
                    switch (sample)
                    {
                        case ImpactObject:
                        case EffectsObject:
                            Set(target, page, randomization, array);
                            break;
                        default:
                            Set(target, page, randomization.Get(array));
                            break;
                    }
                }
            }
        }

        protected virtual RandomizationBuilder Configure(RandomizationBuilder builder) => builder;

        protected virtual void Randomize(SerializedObject target, RandomizationObject randomization, Randomization.ETargets targets)
        {
            Configure(new RandomizationBuilder()).Apply(target, randomization, targets);
            target.ApplyModifiedProperties();
        }

        public override void DrawContent(SerializedObject target)
        {
            DrawProperty(target, "randomization");
            DrawProperty(target, "targets");

            btnBg ??= InspectorUtility.MakeGradientTex(128, 24, Theme.BtnGradientLeftNormal, Theme.BtnGradientRightNormal);
            btnBgHover ??= InspectorUtility.MakeGradientTex(128, 24, Theme.BtnGradientLeftHover, Theme.BtnGradientRightHover);
            btnBgActive ??= InspectorUtility.MakeGradientTex(128, 24, Theme.BtnGradientLeftActive, Theme.BtnGradientRightActive);
            style ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 24,
                fixedWidth = 120,
                normal = { background = btnBg, textColor = Color.white },
                hover = { background = btnBgHover, textColor = Color.white },
                active = { background = btnBgActive, textColor = Color.white },
            };

            var rect = GUILayoutUtility.GetRect(120, 24, GUILayout.Width(120), GUILayout.Height(24));
            rect.x += 4;

            EditorGUI.DrawRect(new Rect(rect.x + 2, rect.y + 4, rect.width, rect.height), Theme.ButtonShadowHeavy);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 3, rect.width, rect.height), Theme.ButtonShadowMid);
            EditorGUI.DrawRect(new Rect(rect.x + 1, rect.y + 2, rect.width, rect.height), Theme.ButtonShadowLight);

            if (GUI.Button(rect, "Randomize", style))
            {
                var randomization = GetProperty(target, "randomization").objectReferenceValue as RandomizationObject;
                var targets = (Randomization.ETargets)GetProperty(target, "targets").intValue;
                Configure(new RandomizationBuilder()).Apply(target, randomization, targets);
                target.ApplyModifiedProperties();
            }
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            EditorGUILayout.Space();
        }
    }

    public abstract class ComponentPage : Page
    {
        protected abstract ReferencesObject.System GetReference(ReferencesObject references);
        private ParticleSystem GetSystem(ReferencesObject references) => GetReference(references).system;

        protected abstract string Name { get; }

        protected override bool Toggleable => true;
        protected override bool RequiresValidation => true;

        protected override List<(string name, bool valid)> GetValidationStates(SerializedObject target)
        {
            var reference = GetReferences(target);
            if (reference == null) return new List<(string name, bool valid)> { ("References in General", false) };
            if (reference.defaultSystem.system == null) return new List<(string name, bool valid)> { ("Default System in References Object", false) };

            return new List<(string name, bool valid)>
            {
                ("Particle System in References Object", GetSystem(GetReferences(target))),
                ("Material in References Object",        GetReference(GetReferences(target)).material)
            };
        }

        public override string Title => Name;

        protected virtual Layout Layout => null;

        public override void DrawContent(SerializedObject target)
        {
            if (Layout == null) return;

            foreach (var element in Layout.Elements)
            {
                switch (element)
                {
                    case HeaderElement h:
                        DrawHeader(h.Title, h.Subtitle);
                        break;
                    case SubHeaderElement sh:
                        DrawSubHeader(sh.Title);
                        break;
                    case PropertyElement pe:
                        DrawProperty(target, pe.Property);
                        break;
                    case BeginBoxElement b:
                        BeginBox(b.Label);
                        break;
                    case EndBoxElement _:
                        EndBox();
                        break;
                    case SeparatorElement:
                        Separate();
                        break;
                }
            }
        }
    }

    public abstract class ParticlePage : ComponentPage { }

    public abstract class BillboardPage : ParticlePage
    {
        protected override List<(string name, bool valid)> GetValidationStates(SerializedObject target)
        {
            var reference = GetReferences(target);
            if (reference == null || reference.defaultSystem.system == null) return base.GetValidationStates(target);

            var states = base.GetValidationStates(target);

            var billboards = GetProperty(target, "billboards");
            if (billboards != null)
            {
                for (int i = 0; i < billboards.arraySize; i++)
                {
                    var billboard = billboards.GetArrayElementAtIndex(i);
                    var obj = billboard.FindPropertyRelative("Object");
                    states.Add(($"Billboard {i + 1} Object", obj != null && obj.objectReferenceValue != null));

                    var enableTrail = billboard.FindPropertyRelative("enableTrail");
                    if (enableTrail != null && enableTrail.boolValue)
                    {
                        var trail = billboard.FindPropertyRelative("trail");
                        var trailObj = trail?.FindPropertyRelative("Object");
                        states.Add(($"Billboard {i + 1} Trail Object", trailObj != null && trailObj.objectReferenceValue != null));
                    }
                }
            }

            return states;
        }

        protected override Layout Layout => new Layout()
            .Header("Particle", "Appearance and duration of each particle")
            .Property("lifetime")

            .Header("Overlay", "Halftone texture layered on top of the particle")
            .Property("overlay", "overlayColor", "overlayRotation", "overlayTiling")

            .Header("Modifiers", "Optional effects applied when particles spawn")
            .Property("delay", "randomDelay")

            .Separate()
            .Property("billboards");
    }

    public abstract class AmountBillboardPage : BillboardPage { }
}