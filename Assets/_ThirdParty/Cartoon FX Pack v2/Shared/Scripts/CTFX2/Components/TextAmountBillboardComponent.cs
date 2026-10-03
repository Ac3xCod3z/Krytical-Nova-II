using CTFX2.Creator.Data;
using PSD.Creator.Objects;

namespace CTFX2.Creator.Components
{
    public abstract class TextAmountBillboardComponent<T, S, TModule> : TextBillboardComponent<T, S, TModule>
        where T : TextAmountBillboard<S, TModule>
        where S : BillboardObject
        where TModule : TextAmountBillboard<S, TModule>.TextAmountBillboardModule
    {
        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Emission.SetBursts((0, Curve.TwoConstants(module.count.x, module.count.y)));
        }
    }
}