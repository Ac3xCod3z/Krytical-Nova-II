// Used by make_unitypackage.py inside a throwaway Unity project, never in the game project.
// Takes a pet model that has just been imported from the base package, adds an Animator Controller and a
// prefab for it, checks the import, and exports everything as one .unitypackage made by Unity itself.
//
//   Unity -batchmode -projectPath <temp> -executeMethod PetPackager.Run
//         -knModel Assets/.../SM_Pet_X_S1.fbx -knOut <file.unitypackage> -knReport <file.txt>
//         [-knControllerGuid <guid>] [-knPrefabGuid <guid>]
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PetPackager
{
    private static string Arg(string name)
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public static void Run()
    {
        var report = new StringBuilder();
        string reportPath = Arg("-knReport") ?? "pet_report.txt";
        try
        {
            Build(report);
        }
        catch (Exception e)
        {
            report.AppendLine("FAILED " + e);
        }
        File.WriteAllText(reportPath, report.ToString());
    }

    private static void Build(StringBuilder report)
    {
        string modelPath = Arg("-knModel");
        string outPath = Arg("-knOut");
        string folder = Path.GetDirectoryName(modelPath).Replace('\\', '/');
        string stem = Path.GetFileNameWithoutExtension(modelPath);            // SM_Pet_KitsuneSpirit_S1
        string pet = stem.Replace("SM_Pet_", "");                              // KitsuneSpirit_S1
        string petName = pet.Contains("_S") ? pet.Substring(0, pet.LastIndexOf("_S", StringComparison.Ordinal)) : pet;
        string controllerPath = $"{folder}/AC_Pet_{petName}.controller";
        string prefabPath = $"{folder}/Pet_{pet}.prefab";

        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (importer == null || model == null) throw new Exception("No model at " + modelPath);
        // A clip named Idle is the pet's resting loop: make it loop, and re-import so the clip carries that.
        var takes = importer.defaultClipAnimations;
        foreach (var take in takes)
        {
            bool loop = take.name == "Idle";
            take.loopTime = loop;
            take.loopPose = false;
        }
        importer.clipAnimations = takes;
        importer.SaveAndReimport();
        model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview")).OrderBy(c => c.name).ToArray();

        // ---- Animator Controller: plays Idle (or rests, if there is none); one trigger per other
        //      animation, each returning to the default state
        AssetDatabase.DeleteAsset(controllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        var machine = controller.layers[0].stateMachine;
        var idle = clips.FirstOrDefault(c => c.name == "Idle");
        var rest = machine.AddState(idle != null ? "Idle" : "Rest");
        rest.motion = idle;
        machine.defaultState = rest;
        foreach (var clip in clips)
        {
            if (clip == idle) continue;
            controller.AddParameter(clip.name, AnimatorControllerParameterType.Trigger);
            var state = machine.AddState(clip.name);
            state.motion = clip;
            var enter = machine.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0, clip.name);
            enter.hasExitTime = false;
            enter.duration = 0.08f;
            enter.canTransitionToSelf = false;
            var leave = state.AddTransition(rest);
            leave.hasExitTime = true;
            leave.exitTime = 1f;
            leave.duration = idle != null ? 0.15f : 0f;      // the loop does not start on the rest pose: blend into it
        }
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        // ---- Prefab: the model with its Animator wired to the controller
        AssetDatabase.DeleteAsset(prefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        var animator = instance.GetComponent<Animator>() ?? instance.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
        PrefabUtility.SaveAsPrefabAsset(instance, prefabPath);
        UnityEngine.Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();

        // ---- keep the same GUIDs from one export to the next, so a re-import updates in place
        ReGuid(controllerPath, Arg("-knControllerGuid"), prefabPath);
        ReGuid(prefabPath, Arg("-knPrefabGuid"), null);
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);   // the old reference died with the old GUID
        clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview")).OrderBy(c => c.name).ToArray();

        // ---- check what Unity made of it
        report.AppendLine($"unity {Application.unityVersion}");
        report.AppendLine($"model {modelPath} guid {AssetDatabase.AssetPathToGUID(modelPath)}");
        report.AppendLine($"rig {importer.animationType} importAnimation {importer.importAnimation} bakeAxisConversion {importer.bakeAxisConversion} " +
                          $"materials {importer.materialImportMode} fileScale {importer.useFileScale}");
        foreach (var c in clips) report.AppendLine($"clip {c.name} {c.length:F3}s at {c.frameRate} fps{(c.isLooping ? " loops" : "")}");
        report.AppendLine($"default state {controller.layers[0].stateMachine.defaultState.name}");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var check = UnityEngine.Object.Instantiate(prefab);
        var smr = check.GetComponentInChildren<SkinnedMeshRenderer>();
        var bones = check.GetComponentsInChildren<Transform>();
        Transform Bone(string n) => bones.FirstOrDefault(t => t.name == n);
        var a = check.GetComponent<Animator>();
        report.AppendLine($"prefab {prefabPath} guid {AssetDatabase.AssetPathToGUID(prefabPath)} animator {(a != null)} " +
                          $"controller {(a != null && a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "none")} " +
                          $"avatar {(a != null && a.avatar != null && a.avatar.isValid ? "valid" : "MISSING")}");
        report.AppendLine($"controller {controllerPath} guid {AssetDatabase.AssetPathToGUID(controllerPath)} parameters [{string.Join(", ", controller.parameters.Select(p => p.name))}]");
        if (smr != null)
        {
            var wb = smr.bounds;
            report.AppendLine($"mesh tris {smr.sharedMesh.triangles.Length / 3} bones {smr.bones.Length} unweighted {smr.sharedMesh.boneWeights.Count(w => w.weight0 <= 0f)}");
            report.AppendLine($"world size {wb.size.ToString("F3")} lowest point y {wb.min.y:F3}");
            foreach (var m in smr.sharedMaterials)
                report.AppendLine($"material {(m ? m.name : "null")} at {(m ? AssetDatabase.GetAssetPath(m) : "")}");
        }
        var head = Bone("DEF-spine.011");
        var tail = Bone("DEF-spine");
        var paw = Bone("DEF-front_toe.L");
        if (head != null && tail != null && paw != null)
            report.AppendLine($"head at {head.position.ToString("F3")} tail tip bone at {tail.position.ToString("F3")} left front paw at {paw.position.ToString("F3")}");
        var spin = clips.FirstOrDefault(c => c.name == "SpinChase");
        var root = Bone("root");
        if (spin != null && root != null)
        {
            var before = root.rotation;
            spin.SampleAnimation(check, spin.length * 0.4f);
            report.AppendLine($"SpinChase at 40 %: root turned {Quaternion.Angle(before, root.rotation):F1} deg");
            spin.SampleAnimation(check, spin.length);
            report.AppendLine($"SpinChase at end: root is {Quaternion.Angle(before, root.rotation):F1} deg from rest");
        }
        UnityEngine.Object.DestroyImmediate(check);
        foreach (var tex in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/_Game" }).Select(AssetDatabase.GUIDToAssetPath))
        {
            var ti = AssetImporter.GetAtPath(tex) as TextureImporter;
            var t2 = AssetDatabase.LoadAssetAtPath<Texture2D>(tex);
            report.AppendLine($"texture {Path.GetFileName(tex)} {t2.width}x{t2.height} {ti.filterMode} mips {ti.mipmapEnabled} sRGB {ti.sRGBTexture} {ti.textureCompression}");
        }

        // ---- one package, made by Unity
        var all = AssetDatabase.FindAssets("", new[] { "Assets/_Game" }).Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => !AssetDatabase.IsValidFolder(p)).Distinct().ToArray();
        AssetDatabase.ExportPackage(all, outPath, ExportPackageOptions.Default);
        report.AppendLine($"package {outPath} with {all.Length} assets: {string.Join(", ", all.Select(Path.GetFileName))}");
        report.AppendLine("OK");
    }

    private static void ReGuid(string assetPath, string wanted, string alsoFixReferencesIn)
    {
        if (string.IsNullOrEmpty(wanted)) return;
        string meta = assetPath + ".meta";
        string current = AssetDatabase.AssetPathToGUID(assetPath);
        if (current == wanted) return;
        File.WriteAllText(meta, File.ReadAllText(meta).Replace("guid: " + current, "guid: " + wanted));
        if (alsoFixReferencesIn != null)
            File.WriteAllText(alsoFixReferencesIn, File.ReadAllText(alsoFixReferencesIn).Replace(current, wanted));
    }
}
