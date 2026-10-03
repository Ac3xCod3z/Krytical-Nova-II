using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Components;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class SmokeTrailComponent : BillboardComponent<SmokeTrail, SmokeObject, SmokeTrail.SmokeTrailModule>
    {
        public SmokeTrail settings { get; set; }

        public override SmokeTrail Settings => settings;
        public override ReferencesObject.System Reference => Creator.reference.references.smoke;

        protected override bool RandomStartFrame => true;

        protected override SmokeObject Object => Settings.Object;

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.CustomData.Set(ParticleSystemCustomData.Custom1, Settings.colorIsGradient ? Settings.colorGradient : Settings.colorSolid);

            System.Main.SetSize(Curve.TwoConstants(Settings.size.x, Settings.size.y));

            System.Main.SetRotation(z: (Mathf.PI * 2, 0));
            System.Main.SetParticleLimit(10000);

            System.Emission.SetBursts();
            System.Emission.SetRateOverDistance((Settings.density, 0.8f));

            System.Main.SetSpeed(0);
            System.LimitVelocityOverLifetime.SetEnabled(false);

            System.Shape.SetAbsolutePosition(0, 0, 0);
        }
    }
}