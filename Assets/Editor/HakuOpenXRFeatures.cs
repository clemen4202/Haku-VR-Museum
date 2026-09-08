// Enables the OpenXR features Haku needs for Quest 3.
//
// WHY THIS EXISTS SEPARATELY FROM HakuProjectSetup:
// HakuProjectSetup step 4 tried to enable a "Meta Quest" FEATURE SET and failed. That was correct
// behaviour on its part - in com.unity.xr.openxr 1.18.0 there IS no bundled Meta Quest feature set.
// Feature *sets* for Meta ship with the Meta XR SDK, which we deliberately do not install
// (it is not needed for a fully-virtual museum and drags in AR Foundation).
//
// What DOES exist in 1.18.0 are the individual features, verified by grepping the package:
//     com.unity.openxr.feature.metaquest          - Meta Quest Support
//     com.unity.openxr.feature.input.oculustouch  - Oculus Touch Controller Profile
// Enabling those two directly is the correct OpenXR-only path.
//
// Run:
//   Unity.exe -batchmode -nographics -projectPath <proj> -executeMethod HakuOpenXRFeatures.Enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class HakuOpenXRFeatures
{
    static readonly string[] WantedFeatureIds =
    {
        "com.unity.openxr.feature.metaquest",
        "com.unity.openxr.feature.input.oculustouch",
    };

    public static void Enable()
    {
        int enabled = 0;
        var problems = new List<string>();

        try
        {
            var editorAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.XR.OpenXR.Editor");
            var runtimeAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.XR.OpenXR");

            if (editorAsm == null || runtimeAsm == null)
                throw new Exception("OpenXR assemblies not loaded. Is com.unity.xr.openxr installed?");

            var helpers = editorAsm.GetType("UnityEditor.XR.OpenXR.Features.FeatureHelpers");
            if (helpers == null) throw new Exception("FeatureHelpers type not found.");

            // Populate the feature list for Android before touching it.
            helpers.GetMethod("RefreshFeatures", BindingFlags.Public | BindingFlags.Static)
                   ?.Invoke(null, new object[] { BuildTargetGroup.Android });

            var getById = helpers.GetMethod("GetFeatureWithIdForBuildTarget",
                                            BindingFlags.Public | BindingFlags.Static);
            if (getById == null) throw new Exception("GetFeatureWithIdForBuildTarget not found.");

            foreach (var id in WantedFeatureIds)
            {
                var feature = getById.Invoke(null, new object[] { BuildTargetGroup.Android, id });
                if (feature == null)
                {
                    problems.Add(id + " (not present in this OpenXR version)");
                    continue;
                }

                var so = new SerializedObject((UnityEngine.Object)feature);
                var prop = so.FindProperty("m_enabled") ?? so.FindProperty("enabled");
                if (prop == null)
                {
                    problems.Add(id + " (no enabled property)");
                    continue;
                }

                prop.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty((UnityEngine.Object)feature);
                enabled++;
                Debug.Log("[HakuOpenXR] enabled " + id);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            Debug.LogError("[HakuOpenXR] FAILED: " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
            Exit(1);
            return;
        }

        // Report what is actually enabled now, so the log is proof rather than a claim.
        DumpEnabled();

        if (problems.Count > 0)
            Debug.LogWarning("[HakuOpenXR] not enabled: " + string.Join(", ", problems));

        Debug.Log("[HakuOpenXR] enabled " + enabled + "/" + WantedFeatureIds.Length + " feature(s).");
        Exit(enabled == WantedFeatureIds.Length ? 0 : 1);
    }

    /// Lists every OpenXR feature currently enabled for Android.
    public static void DumpEnabled()
    {
        try
        {
            var runtimeAsm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.XR.OpenXR");
            var settingsType = runtimeAsm?.GetType("UnityEngine.XR.OpenXR.OpenXRSettings");
            var get = settingsType?.GetMethod("GetSettingsForBuildTargetGroup",
                                              BindingFlags.Public | BindingFlags.Static);
            var settings = get?.Invoke(null, new object[] { BuildTargetGroup.Android });
            if (settings == null) { Debug.LogWarning("[HakuOpenXR] no Android OpenXR settings object."); return; }

            var featuresProp = settingsType.GetProperty("features",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var features = featuresProp?.GetValue(settings) as Array;
            if (features == null) { Debug.LogWarning("[HakuOpenXR] could not read features array."); return; }

            foreach (var f in features)
            {
                if (f == null) continue;
                var t = f.GetType();
                var en = t.GetProperty("enabled")?.GetValue(f);
                var nm = t.GetProperty("nameUi")?.GetValue(f) ?? t.Name;
                if (en is bool b && b) Debug.Log("[HakuOpenXR]   ENABLED: " + nm);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[HakuOpenXR] DumpEnabled failed: " + e.Message);
        }
    }

    static void Exit(int code)
    {
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }
}
