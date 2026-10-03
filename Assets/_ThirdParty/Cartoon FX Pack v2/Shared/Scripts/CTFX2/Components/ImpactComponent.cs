using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class ImpactComponent : TextBillboardComponent<Impact, ImpactObject, Impact.ImpactModule>
    {
        public override Impact Settings => Creator.impact;
        public override ReferencesObject.System Reference => Creator.reference.references.impact;

        protected override Curve Rotation => (module.randomRotation * Mathf.Deg2Rad, -1);

        protected override bool Animate => true;

        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.SizeOverLifetime.SetEnabled(false);
        }

        protected override Vector3 GetForwardMovement() => base.GetForwardMovement() + new Vector3(Object.offset.x, Object.offset.y);
    }
}