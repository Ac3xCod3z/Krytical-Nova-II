/******************************************************************************/
/*
  Project   - Boing Kit
  Publisher - Long Bunny Labs
              http://LongBunnyLabs.com
  Author    - Ming-Lun "Allen" Chou
              http://AllenChou.net
*/
/******************************************************************************/

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

namespace BoingKit
{
  [InitializeOnLoad]
  internal static class BoingDocRegistry
  {
    private static Dictionary<string, string> s_docs;

    static BoingDocRegistry()
    {
      // Prime the cache eagerly so the first inspector render is instant.
      EnsureLoaded();
    }

    private static void EnsureLoaded()
    {
      if (s_docs != null)
        return;

      s_docs = new Dictionary<string, string>(StringComparer.Ordinal);

      foreach (var type in typeof(BoingDocAttribute).Assembly.GetTypes())
      {
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
          var attr = field.GetCustomAttribute<BoingDocAttribute>();
          if (attr == null)
            continue;
          if (!s_docs.ContainsKey(field.Name))
            s_docs[field.Name] = attr.Summary;
        }
      }
    }

    internal static string Get(string fieldName)
    {
      EnsureLoaded();
      return s_docs.TryGetValue(fieldName, out var v) ? v : string.Empty;
    }
  }
}
