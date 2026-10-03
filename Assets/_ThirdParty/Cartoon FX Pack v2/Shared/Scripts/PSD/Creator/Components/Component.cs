using System.Linq;
using PSD.Creator.Data;
using PSD.Creator.Objects;
using UnityEngine;

namespace PSD.Creator.Tool
{
    public interface IComponent
    {
        ReferencesObject.System Reference { get; }
        ParticleSystem ParticleSystem { get; }
        Material Material { get; set; }
        void ResetSystem(ParticleSystem source);
        bool Valid();
    }

    public interface IMultipleComponent : IComponent
    {
        int Id { get; set; }
        int Max { get; set; }
    }

    [DisallowMultipleComponent]
    // [RequireComponent(typeof(ParticleSystem))]
    public abstract partial class Component<T, TCreator, TBasic> : MonoBehaviour, IComponent where T : Settings where TCreator : PSDCreator<TCreator, TBasic> where TBasic : Basic
    {
        [SerializeField] private Material material;
        public Material Material { get => material; set => material = value; }
        public ParticleSystem ParticleSystem => TryGetComponent(out ParticleSystem system) ? system : gameObject.AddComponent<ParticleSystem>();

        private SystemHandler system;
        protected SystemHandler System => system ??= SystemHandler.Build(this);

        public bool Valid() => Creator != null;

        protected TCreator Creator => GetComponentInParent<TCreator>();

        protected virtual bool[] IsValidConditions() => new bool[] { };

        public abstract T Settings { get; }
        public TBasic BasicSettings => Creator.basic;

        public abstract ReferencesObject.System Reference { get; }

        public void SetActive(bool enabled) => gameObject.SetActive(enabled);

        protected bool IsPlaying => Creator.System.IsAlive(true);

        protected bool IsGamePlaying() =>
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying;
#else
            Application.isPlaying;
#endif

        public void ResetSystem(ParticleSystem source)
        {
            if (Creator == null) return;
            if (!IsValid(IsValidConditions().Append(source != null).ToArray())) return;
            if (Material == null) return;

            System.CopyFrom(source);

            ResetSystem();

            System.Renderer.SetSharedMaterial(Material);
        }

        protected abstract void ResetSystem();

        private bool IsValid(params bool[] conditions)
        {
            bool valid = System != null && Settings != null && conditions.All(c => c);
            SetActive(valid);
            return valid;
        }
    }
}