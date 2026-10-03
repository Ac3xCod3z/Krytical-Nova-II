using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Objects;
using UnityEngine;

namespace CTFX2.Creator.Components
{
    public class BackgroundComponent : TextBillboardComponent<Background, BackgroundObject, Background.BackgroundModule>
    {
        public override Background Settings => Creator.background;
        public override ReferencesObject.System Reference => Creator.reference.references.background;

        protected override Curve Rotation => (module.randomRotation * Mathf.Deg2Rad, -1);
    }
}