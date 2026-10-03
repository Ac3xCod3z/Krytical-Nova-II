using PSD.Creator.Data;
using PSD.Creator.Objects;
using PSD.Creator.Tool;
using UnityEngine;

namespace PSD.Creator.Components
{
    public interface IBillboardComponent : IMultipleComponent
    {
        int Order { get; set; }
    }

    public abstract class BillboardComponent<T, S, TModule> : ParticleComponent<T, S>, IBillboardComponent
        where T : Billboard<S, TModule>
        where S : BillboardObject
        where TModule : Billboard<S, TModule>.BillboardModule
    {
        protected override bool[] IsValidConditions() => new bool[] { Object != null };

        [SerializeField] private int id;
        [SerializeField] private int max;
        [SerializeField] private int order;

        public int Id { get => id; set => id = value; }
        public int Max { get => max; set => max = value; }
        public int Order { get => order; set => order = value; }

        protected virtual int GetOrder(int v) => v;

        protected virtual int Seed => Id;

        public TModule module;

        protected virtual bool Animate => false;
        protected virtual float? AnimationSpeed => null;
        protected virtual bool RandomStartFrame => false;
        protected virtual bool UseMeshIndexAsFrame => false;

        protected override S Object => module.Object;

        protected override void ResetSystem()
        {
            base.ResetSystem();

            Random.InitState(Seed);

            System.Main.SetLifetime(Settings.lifetime);

            bool useOverlay = Settings.useOverlay && module.moduleUseOverlay && Settings.overlay?.albedo != null;
            System.SetTexture("_Overlay", useOverlay ? Settings.overlay.albedo : null);
            System.SetVector("_OverlayOffset", (useOverlay && Settings.overlay.randomizeOffset) ? (Random.insideUnitCircle * Settings.overlay.tiling) : Vector2.zero);
            System.SetFloat("_OverlayTiling", useOverlay ? Settings.overlay.tiling * Settings.overlayTiling : 1);
            System.SetFloat("_OverlayRotation", useOverlay ? Settings.overlayRotation : 0);
            System.SetColor("_OverlayColor", useOverlay ? Settings.overlayColor : Color.black);

            System.Main.SetDelay(
                (Settings.useDelay && Settings.useRandomDelay) ? (Settings.delay + Settings.randomDelay, Settings.delay / (Settings.delay + Settings.randomDelay)) : (
                Settings.useDelay ? Settings.delay : (
                Settings.useRandomDelay ? (Settings.randomDelay, 0) :
                0
            )));

            System.CustomData.Set(ParticleSystemCustomData.Custom1, module.colorIsGradient ? module.colorGradient : module.colorSolid);
            System.Main.SetSize(Curve.TwoConstants(module.size.x, module.size.y));

            System.SetFloat("_Animate", Animate ? 1 : 0);
            System.SetFloat("_AnimationSpeed", AnimationSpeed ?? 1);
            System.SetFloat("_RandomStartFrame", RandomStartFrame ? 1 : 0);
            System.SetFloat("_UseMeshIndex", UseMeshIndexAsFrame ? 1 : 0);

            System.Renderer.SetOrder(GetOrder(Order));
        }
    }
}