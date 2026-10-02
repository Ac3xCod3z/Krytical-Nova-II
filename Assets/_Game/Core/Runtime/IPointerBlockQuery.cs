using UnityEngine;

namespace KryticalNova.Core
{
    /// <summary>Answers whether a screen position is covered by client UI and must not reach world click handling.</summary>
    public interface IPointerBlockQuery
    {
        bool BlocksWorldPointer(Vector2 screenPosition);
    }
}
