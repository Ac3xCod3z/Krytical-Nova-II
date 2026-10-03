using PSD.Creator.Data;
using PSD.Creator.Objects;

namespace PSD.Creator.Components
{
    public abstract class AmountBillboardComponent<T, S, TModule> : BillboardComponent<T, S, TModule>
        where T : AmountBillboard<S, TModule>
        where S : BillboardObject
        where TModule : AmountBillboard<S, TModule>.AmountBillboardModule
    {
        protected override void ResetSystem()
        {
            base.ResetSystem();

            System.Emission.SetBursts((0, Curve.TwoConstants(module.count.x, module.count.y)));

            System.Main.SetLifetime((Settings.lifetime, 0.75f));
        }
    }
}