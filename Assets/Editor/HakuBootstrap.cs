// Adds the packages Haku needs, resolving versions against the installed Editor
// so we never pin a version that does not exist for 6000.5.5f1.
//
// Run headless:
//   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod HakuBootstrap.AddPackages
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

public static class HakuBootstrap
{
    // Order matters: URP first (render pipeline), then XR plumbing, then XRI.
    // Deliberately NOT included:
    //   com.unity.xr.oculus       - deprecated on Unity 6000.5, do not use
    //   com.unity.xr.meta-openxr  - AR Foundation / passthrough, not needed for a virtual museum
    static readonly string[] Packages =
    {
        "com.unity.render-pipelines.universal",
        "com.unity.xr.management",
        "com.unity.xr.openxr",
        "com.unity.xr.interaction.toolkit",
        "com.unity.inputsystem",
    };

    public static void AddPackages()
    {
        var failed = new List<string>();
        foreach (var id in Packages)
        {
            Debug.Log($"[haku-bootstrap] adding {id} ...");
            AddRequest req = Client.Add(id);
            while (!req.IsCompleted)
                System.Threading.Thread.Sleep(100);

            if (req.Status == StatusCode.Success)
                Debug.Log($"[haku-bootstrap] OK {req.Result.packageId}");
            else
            {
                Debug.LogError($"[haku-bootstrap] FAILED {id}: {req.Error?.message}");
                failed.Add(id);
            }
        }

        if (failed.Count > 0)
        {
            Debug.LogError("[haku-bootstrap] FAILED PACKAGES: " + string.Join(", ", failed));
            EditorApplication.Exit(1);
        }

        Debug.Log("[haku-bootstrap] all packages added");
        EditorApplication.Exit(0);
    }

    /// Prints what is actually installed, so we can see resolved versions.
    public static void ListPackages()
    {
        ListRequest req = Client.List(true, false);
        while (!req.IsCompleted) System.Threading.Thread.Sleep(100);
        if (req.Status == StatusCode.Success)
            foreach (var p in req.Result)
                if (!p.name.StartsWith("com.unity.modules."))
                    Debug.Log($"[haku-pkg] {p.name} @ {p.version}");
        EditorApplication.Exit(0);
    }
}
