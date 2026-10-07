using UnityEngine;

namespace KryticalNova.Items.Visuals
{
    /// <summary>
    /// A component the equipment preview doll keeps from the character it is a copy of. Everything else on the copy
    /// is stripped; these are told which character they mirror so the doll looks like it while wearing what is tried on.
    /// </summary>
    public interface IPreviewDollPart
    {
        void BindToOriginal(GameObject original);
    }
}
