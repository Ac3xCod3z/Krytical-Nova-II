using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using PSD.Creator.Utility;
using System;
using PSD.Creator.Components;
using PSD.Creator.Data;
using PSD.Creator.Objects;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PSD.Creator.Tool
{
    public abstract class PSDCreator<TCreator, TBasic> : MonoBehaviour where TCreator : PSDCreator<TCreator, TBasic> where TBasic : Basic
    {
        public Reference reference;
        public Randomization randomization;
        public TBasic basic;

        [SerializeField, HideInInspector]
        private string trackedName;
        public string TrackedName
        {
            get => trackedName;
            set => trackedName = value;
        }

        private static bool IsSubclassOfRawGeneric(Type generic, Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (generic == cur)
                    return true;
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        public IComponent[] GetComponents() => GetComponentsInChildren<IComponent>(true)
           .Where(c => IsSubclassOfRawGeneric(typeof(Component<,,>), c.GetType()))
           .ToArray();

        public List<IComponent> FindMissingMaterials()
        {
            var components = GetComponents();
            var missing = new List<IComponent>();
            foreach (var component in components)
            {
                if (component.Material == null) missing.Add(component);
            }
            return missing;
        }

        public abstract string GetTypeName(IComponent component);

        private ParticleSystem system;
        public ParticleSystem System
        {
            get
            {
                if (system == null)
                    system = GetComponent<ParticleSystem>() ?? gameObject.AddComponent<ParticleSystem>();
                return system;
            }
            private set => system = value;
        }

        private bool Setup()
        {
#if UNITY_EDITOR
            if (!(EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode))
                if (Selection.activeGameObject != gameObject && enabled && gameObject.activeSelf) return false;
#endif

            if (reference == null) return false;

#if UNITY_EDITOR
            reference.references ??= Resources.Load<ReferencesObject>("References");
#endif
            if (reference.references == null) return false;

            if (FindMissingMaterials().Count > 0) return false;

            if (reference.references.defaultSystem.system == null) return false;

            System = System;

            return true;
        }

        protected abstract void SetupComponents();

        protected bool SetupComponent<T>(Settings settings, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
            => SetupComponent<T>(settings, this, initializer, reset, enabledConditions);
        protected bool SetupComponent<T>(Settings settings, out T component, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
            => SetupComponent(settings, out component, this, initializer, reset, enabledConditions);
        protected bool SetupComponent<T>(Settings settings, Component parent, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
            => SetupComponent<T>(settings, _ => true, parent, initializer, reset, enabledConditions);
        protected bool SetupComponent<T>(Settings settings, out T component, Component parent, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
            => SetupComponent(settings, out component, _ => true, parent, initializer, reset, enabledConditions);
        protected bool SetupComponent<T>(Settings settings, Func<T, bool> idCheck, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
            => SetupComponent(settings, idCheck, this, initializer, reset, enabledConditions);
        protected bool SetupComponent<T>(Settings settings, out T component, Func<T, bool> idCheck, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
            => SetupComponent(settings, out component, idCheck, this, initializer, reset, enabledConditions);
        protected bool SetupComponent<T>(Settings settings, Func<T, bool> idCheck, Component parent, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
            => SetupComponent(settings, out _, idCheck, parent, initializer, reset, enabledConditions);
        protected bool SetupComponent<T>(Settings settings, out T component, Func<T, bool> idCheck, Component parent, Action<T> initializer = null, bool reset = true, params bool[] enabledConditions) where T : Component, IComponent
        {
            T[] components = parent.GetComponentsInChildren<T>(true);
            component = components.FirstOrDefault(idCheck);

            if (!settings.enabled || !enabledConditions.All(c => c))
            {
                if (component != null)
                {
#if UNITY_EDITOR
                    DestroyImmediate(component.gameObject);
#else
                    Destroy(component.gameObject);
#endif
                }
                return false;
            }

            if (component == null)
            {
                var copy = new GameObject(settings.GetType().Name, typeof(T), typeof(ParticleSystem));
                copy.transform.SetParent(parent.transform, false);
                component = copy.GetComponent<T>();
            }
            initializer?.Invoke(component);

            if (!component.Valid()) return false;
            if (component.Reference.system == null) return false;
            if (reset)
            {
                component.ResetSystem(component.Reference.system);
#if UNITY_EDITOR
                EditorUtility.SetDirty(component);
#endif
            }
            return true;
        }

        protected void SetupMultiple<T, TList>(
            ICollection<T> components,
            Settings settings,
            IEnumerable<TList> values,
            Action<T, TList> setter,
            Action<T, TList> delayedSetter,
            Func<T, int, string> name,
            Func<T, int, bool> idCheck = null,
            bool cleanup = true,
            Func<T, TList, bool> enabled = null) where T : Component, IMultipleComponent
        {
            idCheck ??= (c, id) => c.Id == id;

            for (int i = 0; i < values.Count(); i++)
            {
                if (!SetupComponent<T>(settings, out var component, idCheck: c => idCheck(c, i), reset: false)) continue;
                var element = values.ElementAt(i);
                component.Id = i;
                component.Max = values.Count();
                setter(component, element);
                component.name = name?.Invoke(component, i);
                component.ResetSystem(component.Reference.system);
                delayedSetter(component, element);
#if UNITY_EDITOR
                EditorUtility.SetDirty(component);
#endif
                if (enabled != null) component?.gameObject.SetActive(enabled.Invoke(component, element));
                components.Add(component);
            }

            if (cleanup) CleanupMultiple(components);
        }

        protected void SetupBillboards<TBillboard, TSettings, TObject, TModule>(
            ICollection<TBillboard> components,
            TSettings settings,
            Func<TBillboard, int, string> name,
            ref int order,
            Action<TBillboard> setter = null,
            Func<TBillboard, int, bool> idCheck = null,
            bool cleanup = true)
                where TBillboard : BillboardComponent<TSettings, TObject, TModule>
                where TSettings : Billboard<TObject, TModule>
                where TObject : BillboardObject
                where TModule : Billboard<TObject, TModule>.BillboardModule
        {
            int current = order;
            SetupMultiple(components, settings, settings.billboards?.ToList() ?? new List<TModule>(), (c, module) =>
                { c.module = module; c.Order = current++; setter?.Invoke(c); },
                SetupBillboardsSetter<TBillboard, TModule>(current),
                name, idCheck, cleanup, enabled: (c, module) => module.enabled
            );
            order = current;
        }

        protected virtual Action<TBillboard, TModule> SetupBillboardsSetter<TBillboard, TModule>(int current)
            where TBillboard : Component
            where TModule : class
            => null;

        protected void CleanupMultiple<T>(ICollection<T> components) where T : Component, IMultipleComponent
        {
            // Krytical Nova patch: Object.GetInstanceID() is a compile error in Unity 6.6; compare the components themselves.
            var ids = new HashSet<T>(components);
            bool changed = false;
            GetComponentsInChildren<T>(true)
                .Where(c => !ids.Contains(c))
                .ToList()
                .ForEach(c =>
                {
#if UNITY_EDITOR
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(c.Material));
                    DestroyImmediate(c.gameObject);
#endif
                    changed = true;
                });

            if (changed)
            {
#if UNITY_EDITOR
                AssetDatabase.Refresh();
                ResetSystem();
#endif
            }
        }

        private bool isResetting = false;

        public void ResetSystem()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying) return;
#else
            if (Application.isPlaying) return;
#endif

            if (isResetting) return;
            isResetting = true;

            try
            {
                if (!Setup()) return;

#if UNITY_EDITOR
                System.CopySystem(reference.references.defaultSystem.system);
#endif
                var main = System.main;
                main.stopAction = basic.stopAction;

                float time = System.time;
                bool isPlaying = System.isPlaying;
                bool isPaused = System.isPaused;

                SetupComponents();

                ResetPlay(time, isPlaying, isPaused);

#if UNITY_EDITOR
                AssetDatabase.SaveAssets();
#endif
            }
            finally
            {
                isResetting = false;
            }
        }

        public void ResetPlay() => ResetPlay(System.time, System.isPlaying, System.isPaused);
        public void ResetPlay(float time, bool isPlaying, bool isPaused)
        {
#if UNITY_EDITOR
            System.Simulate(time, true, true);

            if (isPlaying) System.Play();
            else if (isPaused) System.Pause();
#endif
        }
    }
}