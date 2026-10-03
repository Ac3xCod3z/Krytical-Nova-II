using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Components;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class EffectTrailComponent : BillboardComponent<EffectTrail, EffectsObject, EffectTrail.EffectTrailModule>
    {
        public EffectTrail settings { get; set; }
        public Smoke.SmokeModule Parent { get; set; }

        public override EffectTrail Settings => settings;
        public override ReferencesObject.System Reference => Creator.reference.references.smoke;

        protected override bool Animate => true;
        protected override float? AnimationSpeed => Object.speed;
        protected override bool RandomStartFrame => Object.randomStartFrame;

        protected override EffectsObject Object => Settings.Object;

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Main.SetLifetime(0.8f);
            System.Main.SetSize(Curve.TwoConstants(Settings.size.x * Object.sizeMultiplier, Settings.size.y * Object.sizeMultiplier));

            System.Main.SetRotation(z: (Mathf.PI * 2, 0));
            System.Main.SetParticleLimit(10000);

            System.Emission.SetBursts((0, Curve.TwoConstants(Settings.count.x, Settings.count.y)));

            System.LimitVelocityOverLifetime.SetSpeed(10000);
            System.LimitVelocityOverLifetime.SetDrag(Parent.drag);

            System.InheritVelocity.SetMode(ParticleSystemInheritVelocityMode.Initial);
            System.InheritVelocity.SetMultiplier(1);

            System.Shape.SetAbsolutePosition(0, 0, 0);
            System.Shape.SetRandomizeDirection(0);

            System.Renderer.SetPivot(x: -1 / (Settings.size.y * Object.sizeMultiplier));

            if (Settings.useSecondColor)
                System.CustomData.Set(ParticleSystemCustomData.Custom1, (Gradient)(
                    Settings.colorIsGradient ? Settings.colorGradient : Settings.colorSolid,
                    Settings.secondColorIsGradient ? Settings.secondColorGradient : Settings.secondColorSolid)
                );
            else
                System.CustomData.Set(ParticleSystemCustomData.Custom1, Settings.colorIsGradient ? Settings.colorGradient : Settings.colorSolid);
        }
    }
}