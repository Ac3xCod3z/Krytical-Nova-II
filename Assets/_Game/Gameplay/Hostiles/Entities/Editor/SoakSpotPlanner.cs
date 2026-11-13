using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Unity.Mathematics;

namespace KryticalNova.Hostiles.Entities.Editor
{
    /// <summary>
    /// Krytical Nova ▸ Online ▸ Entities ▸ Soak ▸ 2 Plan Soak Spot (wave 4, 12 Nov 2026): with Zone01 open in edit mode, takes the Scene view's
    /// pivot as the centre of the brain soak's square (<see cref="SoakLayout"/>, 2,000 points), samples the NavMesh at every grid point and
    /// writes the proven centre into <see cref="SoakSpots.AssetPath"/>, reporting the points off the NavMesh. No scene is written.
    /// </summary>
    public static class SoakSpotPlanner
    {
        const float Reach = 4f;   // as far as the spawn path's NavMesh.SamplePosition looks (HostileSpawner.NavMeshReach)

        [MenuItem("Krytical Nova/Online/Entities/Soak/2 Plan Soak Spot", priority = 2)]
        static void Plan()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorUtility.DisplayDialog("Plan Soak Spot", "Stop Play first.", "OK"); return; }
            Scene scene = SceneManager.GetActiveScene();
            SoakSpots spots = AssetDatabase.LoadAssetAtPath<SoakSpots>(SoakSpots.AssetPath);
            string map = spots != null ? spots.map : "Zone01_StarterTown";
            if (scene.name != map) { EditorUtility.DisplayDialog("Plan Soak Spot", $"Open {map} first (the active scene is {scene.name}).", "OK"); return; }
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) { EditorUtility.DisplayDialog("Plan Soak Spot", "Open a Scene view and centre it on the spot.", "OK"); return; }

            int count = spots != null && spots.count > 0 ? spots.count : 2000;
            float spacing = spots != null && spots.spacing > 0f ? spots.spacing : SoakLayout.DefaultSpacing;
            var centre = new float2(view.pivot.x, view.pivot.z);
            int offNav = 0;
            foreach (SoakLayout.Point p in SoakLayout.Build(centre, count, spacing))
            {
                var probe = new Vector3(p.Position.x, Physics.Raycast(new Vector3(p.Position.x, 500f, p.Position.z), Vector3.down, out RaycastHit hit, 1000f) ? hit.point.y : 0f, p.Position.z);
                if (!NavMesh.SamplePosition(probe, out _, Reach, NavMesh.AllAreas)) offNav++;
            }

            if (spots == null)
            {
                spots = ScriptableObject.CreateInstance<SoakSpots>();
                AssetDatabase.CreateAsset(spots, SoakSpots.AssetPath);
            }
            spots.map = map;
            spots.centre = new Vector2(centre.x, centre.y);
            spots.count = count;
            spots.spacing = spacing;
            spots.offNav = offNav;
            spots.provenAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            EditorUtility.SetDirty(spots);
            AssetDatabase.SaveAssets();
            Debug.Log($"[KnSoak] Soak spot on {map}: centre {centre.x:F1},{centre.y:F1}, {count} points {spacing} m apart, {offNav} off the NavMesh " +
                      $"({(offNav * 100f / count):F1} %). Server switch: -knbrainsoak {count} -knbrainsoakat {centre.x:F1},{centre.y:F1}");
        }
    }
}
