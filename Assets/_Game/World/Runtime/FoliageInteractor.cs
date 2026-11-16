using System.Collections.Generic;
using UnityEngine;

namespace KryticalNova.World
{
    /// <summary>
    /// Marks something that parts grass as it moves through it (players, NPCs, mobs).
    /// Presentation only — remote players already have synced transforms, so nothing is networked.
    /// On a dedicated server this is an empty shell (owner rule, no visual code in the server build): the player prefab and
    /// scenes carry it, so the server still knows the script and never logs it as missing. The grass that reads it,
    /// FoliageInteractorSystem, lives in KryticalNova.World.Presentation, which the server leaves out.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FoliageInteractor : MonoBehaviour
    {
#if !UNITY_SERVER
        [Tooltip("Metres around the feet in which grass bends away.")]
        [Range(0.2f, 3f)] public float radius = 0.9f;

        public static readonly List<FoliageInteractor> Active = new List<FoliageInteractor>(64);

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);
#endif
    }
}
