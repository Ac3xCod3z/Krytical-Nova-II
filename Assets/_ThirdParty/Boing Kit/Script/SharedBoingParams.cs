/******************************************************************************/
/*
  Project   - Boing Kit
  Publisher - Long Bunny Labs
              http://LongBunnyLabs.com
  Author    - Ming-Lun "Allen" Chou
              http://AllenChou.net
*/
/******************************************************************************/

using UnityEngine;

namespace BoingKit
{
  /// <summary>
  /// A <code>ScriptableObject</code> wrapper for <code>BoingWork.Params</code> so they can be shared across multiple components.
  /// </summary>
  [CreateAssetMenu(fileName = "BoingParams", menuName = "Boing Kit/Shared Boing Params", order = 550)]
  public class SharedBoingParams : ScriptableObject
  {
    public BoingWork.Params Params;

    public SharedBoingParams()
    {
      Params.Init();
    }
  }
}
