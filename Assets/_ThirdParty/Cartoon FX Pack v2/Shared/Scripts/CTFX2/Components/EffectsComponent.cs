using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class EffectsComponent : TextAmountBillboardComponent<Effects, EffectsObject, Effects.EffectsModule>
    {
        public override Effects Settings => Creator.effects;
        public override ReferencesObject.System Reference => Creator.reference.references.effect;

        protected override Curve Rotation => (Mathf.PI * 2, 0);

        protected override bool Animate => true;
        protected override float? AnimationSpeed => Object.speed;
        protected override bool RandomStartFrame => Object.randomStartFrame;

        protected override SystemHandler.ShapeModule.Shape Shape => new SystemHandler.ShapeModule.Circle(module.spread * module.pivot / 2, 1);

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Main.SetSize(Curve.TwoConstants(module.size.x * Object.sizeMultiplier, module.size.y * Object.sizeMultiplier));

            if (module.useSecondColor)
                System.CustomData.Set(ParticleSystemCustomData.Custom1, (Gradient)(
                    module.colorIsGradient ? module.colorGradient : module.colorSolid,
                    module.secondColorIsGradient ? module.secondColorGradient : module.secondColorSolid
                ));

            System.Renderer.SetPivot(x: -(module.pivot - module.spread * module.pivot / 2) / (module.size.y * Object.sizeMultiplier));
        }
    }
}