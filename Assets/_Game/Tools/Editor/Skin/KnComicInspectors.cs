using System;
using System.Collections.Generic;
using System.Reflection;
using Sirenix.OdinInspector.Editor;
using Sirenix.Utilities.Editor;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KryticalNova.EditorTools.Skin
{
    /// <summary>
    /// Every ScriptableObject and component of our own is drawn in the comic look: the dotted field behind it, the
    /// type's name in Orbitron on a banner, every [Header] as a comic bar, and the fields in the skin's styles
    /// (<see cref="KnSkinScope"/>). Odin still draws the fields. Odin registers an editor of its own for each type,
    /// so ours is put in its place after Odin's registration, type by type, for our types only; a type with an
    /// inspector of its own (EconomySettings, the progression data) keeps it. Vendor and Unity types are untouched.
    /// </summary>
    public class KnScriptableObjectInspector : OdinEditor
    {
        public override void OnInspectorGUI() => KnBanner.Draw(this, target, base.OnInspectorGUI);
    }

    public class KnMonoBehaviourInspector : OdinEditor
    {
        public override void OnInspectorGUI() => KnBanner.Draw(this, target, base.OnInspectorGUI);
    }

    [InitializeOnLoad]
    internal static class KnEditorRegistration
    {
        static KnEditorRegistration() => EditorApplication.delayCall += Register;

        private static void Register()
        {
            if (!KnEditorSkin.Enabled) return;
            // The types that have an inspector written for them: those are left alone.
            var own = new HashSet<Type>();
            FieldInfo inspected = typeof(CustomEditor).GetField("m_InspectedType", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (Type editor in TypeCache.GetTypesWithAttribute<CustomEditor>())
            {
                if (editor == typeof(KnScriptableObjectInspector) || editor == typeof(KnMonoBehaviourInspector)) continue;
                foreach (CustomEditor attribute in editor.GetCustomAttributes<CustomEditor>())
                    if (inspected?.GetValue(attribute) is Type t) own.Add(t);
            }
            int count = 0;
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
                if (Take(type, own, typeof(KnScriptableObjectInspector))) count++;
            foreach (Type type in TypeCache.GetTypesDerivedFrom<MonoBehaviour>())
                if (Take(type, own, typeof(KnMonoBehaviourInspector))) count++;
            if (count > 0) InternalEditorUtility.RepaintAllViews();
        }

        private static bool Take(Type type, HashSet<Type> own, Type editor)
        {
            if (type.IsAbstract || type.IsGenericTypeDefinition || !KnEditorSkin.IsOurs(type) || own.Contains(type)) return false;
            if (typeof(UnityEditor.Editor).IsAssignableFrom(type) || typeof(EditorWindow).IsAssignableFrom(type)) return false;
            CustomEditorUtility.SetCustomEditor(type, editor, false, false);
            return true;
        }
    }

    internal static class KnBanner
    {
        public static void ForTarget(Object target)
        {
            if (target == null || !KnEditorSkin.Enabled || !KnEditorSkin.IsOurs(target.GetType())) return;
            string system = target.GetType().Namespace;
            int cut = system.LastIndexOf('.');
            string note = cut >= 0 ? system.Substring(cut + 1) : system;
            KnEditorSkin.Banner(ObjectNames.NicifyVariableName(target.GetType().Name), note);
        }

        /// <summary>The whole inspector in the comic look: the dotted field behind it, the banner, and the fields in the skin's styles.</summary>
        public static void Draw(UnityEditor.Editor editor, Object target, Action body)
        {
            if (target == null || !KnEditorSkin.Enabled || !KnEditorSkin.IsOurs(target.GetType())) { body(); return; }
            int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(editor);
            float top = KnSkinScope.BeginBody(key);
            using (new KnSkinScope())
            {
                ForTarget(target);
                body();
            }
            KnSkinScope.EndBody(key, top);
        }
    }

    /// <summary>What a [Header] on one of our fields becomes: a comic bar in place of Unity's bold line.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class KnHeaderAttribute : Attribute
    {
        public readonly string Text;
        public KnHeaderAttribute(string text) => Text = text;
    }

    /// <summary>Swaps every [Header] on our own types for the comic bar, so Unity's own header is not drawn as well.</summary>
    public sealed class KnHeaderProcessor : OdinAttributeProcessor
    {
        public override bool CanProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member) =>
            KnEditorSkin.Enabled && member is FieldInfo && KnEditorSkin.IsOurs(member.DeclaringType);

        public override void ProcessChildMemberAttributes(InspectorProperty parentProperty, MemberInfo member, List<Attribute> attributes)
        {
            for (int i = attributes.Count - 1; i >= 0; i--)
            {
                if (!(attributes[i] is HeaderAttribute header)) continue;
                attributes.RemoveAt(i);
                attributes.Add(new KnHeaderAttribute(header.header));
            }
        }
    }

    [DrawerPriority(1.0, 0.0, 0.0)]
    public sealed class KnHeaderDrawer : OdinAttributeDrawer<KnHeaderAttribute>
    {
        protected override void DrawPropertyLayout(GUIContent label)
        {
            KnEditorSkin.Header(Attribute.Text);
            CallNextDrawer(label);
        }
    }

    /// <summary>Preferences ▸ Krytical Nova: the switch for the comic inspector.</summary>
    internal static class KnSkinPreferences
    {
        [SettingsProvider]
        public static SettingsProvider Provider()
        {
            return new SettingsProvider("Preferences/Krytical Nova", SettingsScope.User)
            {
                label = "Krytical Nova",
                guiHandler = _ =>
                {
                    EditorGUILayout.Space(8f);
                    bool on = EditorGUILayout.ToggleLeft("Comic Inspector (Ben-Day banners, header bars and the comic styles on our own inspectors and tools; a restart of the Editor applies a change fully)", KnEditorSkin.Enabled);
                    if (on != KnEditorSkin.Enabled)
                    {
                        KnEditorSkin.Enabled = on;
                        InternalEditorUtility.RepaintAllViews();
                    }
                    EditorGUILayout.Space(8f);
                    KnEditorSkin.Banner("Krytical Nova", "a preview of the banner");
                    KnEditorSkin.Header("A header bar");
                    KnEditorSkin.Note("The look follows the Colour Bible (Design/Rendering-Bible.md): Ink, Shadow indigo, Paper, Neon cyan and Signal yellow, with the dots on the icons' 45° grid.");
                },
                keywords = new[] { "comic", "inspector", "Krytical", "Ben-Day", "skin" },
            };
        }
    }
}
