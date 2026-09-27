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
  // Inheriting TooltipAttribute makes [BoingDoc] work as a Unity tooltip in the
  // default inspector, so it serves as a single source of truth for both runtime
  // docs and editor tooltips without duplication.
  public class BoingDocAttribute : TooltipAttribute
  {
    public string Summary => tooltip;
    public BoingDocAttribute(string summary) : base(summary) { }
  }
}
