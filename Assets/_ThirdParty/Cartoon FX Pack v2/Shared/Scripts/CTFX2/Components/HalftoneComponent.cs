using UnityEngine;
using CTFX2.Creator.Data;
using CTFX2.Creator.Objects;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public class HalftoneComponent : TextBillboardComponent<Halftone, HalftoneObject, Halftone.HalftoneModule>
    {
        public override Halftone Settings => Creator.halftone;
        public override ReferencesObject.System Reference => Creator.reference.references.halftone;

        protected override Curve Rotation => (module.randomRotation * Mathf.Deg2Rad, -1);
    }
}