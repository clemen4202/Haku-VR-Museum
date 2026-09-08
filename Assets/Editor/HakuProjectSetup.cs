// Assets/Editor/HakuProjectSetup.cs
//
// Headless Quest 3 project configuration for the Haku VR museum (Unity 6000.5.5f1).
//
// Run with (note: do NOT pass -quit, this method exits with its own status code):
//   "Unity.exe" -batchmode -nographics -projectPath C:/Dev/haku-museum/unity ^
//               -executeMethod HakuProjectSetup.Configure -logFile -
//
// Design notes:
//  * Everything that touches com.unity.xr.management / com.unity.xr.openxr goes through
//    reflection. Those packages are resolved by the Editor and are not version-pinned, so a
//    direct type reference would turn a missing/renamed API into a COMPILE error that kills
//    every other editor script in the project. Reflection turns it into a logged step failure.
//  * Same treatment for the handful of PlayerSettings APIs that are internal or that changed
//    shape in Unity 6 (batching, mobile MT rendering, GPU skinning / mesh deformation).
//  * com.unity.xr.oculus is DEPRECATED on 6000.5 and is deliberately never referenced.
//    The Quest path is OpenXR + the Meta Quest feature set + the Oculus Touch interaction profile.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

public static class HakuProjectSetup
{
    const string kCompanyName = "Team Haku";
    const string kProductName = "Haku";
    const string kPackageId = "com.teamhaku.haku";

    const string kXrSettingsFolder = "Assets/XR";
    const string kXrSettingsAsset = "Assets/XR/XRGeneralSettings.asset";
    const string kXrSettingsKeyFallback = "com.unity.xr.management.loader_settings";
    const string kOpenXRLoaderFullName = "UnityEngine.XR.OpenXR.OpenXRLoader";

    const string kManifestFolder = "Assets/Plugins/Android";
    const string kManifestPath = "Assets/Plugins/Android/AndroidManifest.xml";

    // Meta Quest feature set ids across OpenXR plugin versions (newest first).
    static readonly string[] kMetaFeatureSetIds =
    {
        "com.unity.openxr.featureset.meta",
        "com.unity.openxr.featureset.metaquest",
        "com.unity.openxr.featureset.oculus"
    };

    static readonly string[] kMetaQuestFeatureIds = { "com.unity.openxr.feature.metaquest" };
    static readonly string[] kMetaQuestFeatureTypeNames = { "MetaQuestFeature" };

    static readonly string[] kOculusTouchFeatureIds = { "com.unity.openxr.feature.input.oculustouch" };
    static readonly string[] kOculusTouchFeatureTypeNames = { "OculusTouchControllerProfile" };

    static readonly XNamespace Android = "http://schemas.android.com/apk/res/android";
    static readonly XNamespace Tools = "http://schemas.android.com/tools";

    static readonly List<string> s_Failures = new List<string>();

    // ------------------------------------------------------------------ entry point

    public static void Configure()
    {
        s_Failures.Clear();
        Log("=== Haku Quest 3 project setup starting ===");

        Step("1. switch active build target to Android", SwitchToAndroid);
        Step("2. player settings", ApplyPlayerSettings);
        Step("3. XR Plug-in Management: enable OpenXR for Android", EnableOpenXRForAndroid);
        Step("4. OpenXR: Meta Quest feature set + Oculus Touch profile", ConfigureOpenXRFeatures);
        Step("5. AndroidManifest.xml (cleartext HTTP)", WriteAndroidManifest);

        try
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
        catch (Exception e)
        {
            s_Failures.Add("final AssetDatabase.SaveAssets");
            Error("final AssetDatabase.SaveAssets threw: " + e);
        }

        if (s_Failures.Count == 0)
        {
            Log("=== Haku Quest 3 project setup COMPLETE - all steps OK ===");
            Finish(0);
        }
        else
        {
            Error("=== Haku Quest 3 project setup FAILED. Failed steps: " +
                  string.Join(" | ", s_Failures) + " ===");
            Finish(1);
        }
    }

    static void Step(string name, Action action)
    {
        Log("--> " + name);
        try
        {
            action();
            Log("<-- OK: " + name);
        }
        catch (Exception e)
        {
            s_Failures.Add(name);
            Error("STEP FAILED: '" + name + "' -> " + e.GetType().Name + ": " + e.Message +
                  "\n" + e.StackTrace);
        }
    }

    static void Finish(int exitCode)
    {
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(exitCode);
        }
        else
        {
            // Running interactively - never kill a human's Editor. Report instead.
            Log("Not in batchmode; skipping EditorApplication.Exit(" + exitCode + ").");
        }
    }

    static void Log(string msg) { Debug.Log("[HakuProjectSetup] " + msg); }
    static void Warn(string msg) { Debug.LogWarning("[HakuProjectSetup] " + msg); }
    static void Error(string msg) { Debug.LogError("[HakuProjectSetup] " + msg); }

    // ------------------------------------------------------------------ 1. build target

    static void SwitchToAndroid()
    {
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            Log("Active build target is already Android.");
            return;
        }

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
        {
            throw new Exception(
                "SwitchActiveBuildTarget(Android) returned false. The Android Build Support module " +
                "(with OpenJDK + Android SDK/NDK) is probably not installed for 6000.5.5f1.");
        }

        Log("Active build target is now Android.");
    }

    // ------------------------------------------------------------------ 2. player settings

    static void ApplyPlayerSettings()
    {
        PlayerSettings.companyName = kCompanyName;
        PlayerSettings.productName = kProductName;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, kPackageId);
        Log("Identity: " + kCompanyName + " / " + kProductName + " / " + kPackageId);

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64; // ARMv7 off
        Log("Scripting backend IL2CPP, target architecture ARM64 only.");

        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        Log("Graphics API: Vulkan only (OpenGLES3 removed).");

        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
        Log("Min API level 32, target API level 34.");

        PlayerSettings.colorSpace = ColorSpace.Linear;
        Log("Colour space: Linear.");

        // Internet Access = Require. "Force Remove Internet Permission" is intentionally left OFF.
        PlayerSettings.Android.forceInternetPermission = true;
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        Log("Internet Access = Require, Allow downloads over HTTP = Always Allowed.");

        SetMultithreadedRendering(true);
        SetBatching(staticBatching: true, dynamicBatching: false);
        SetGpuSkinningBatched();

        try
        {
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            Log("Splash screen disabled (the Unity logo is not removable on a Personal licence).");
        }
        catch (Exception e)
        {
            Warn("Could not disable the splash screen (licence restriction?): " + e.Message);
        }
    }

    static void SetMultithreadedRendering(bool enable)
    {
        // Unity 6 exposes both a NamedBuildTarget and a (deprecated) BuildTargetGroup overload.
        var m = FindMethod(typeof(PlayerSettings), "SetMobileMTRendering",
                           typeof(NamedBuildTarget), typeof(bool));
        if (m != null)
        {
            m.Invoke(null, new object[] { NamedBuildTarget.Android, enable });
            Log("Multithreaded rendering = " + enable + " (NamedBuildTarget overload).");
            return;
        }

        m = FindMethod(typeof(PlayerSettings), "SetMobileMTRendering",
                       typeof(BuildTargetGroup), typeof(bool));
        if (m != null)
        {
            m.Invoke(null, new object[] { BuildTargetGroup.Android, enable });
            Log("Multithreaded rendering = " + enable + " (BuildTargetGroup overload).");
            return;
        }

        throw new Exception("PlayerSettings.SetMobileMTRendering not found in this Editor version. " +
                            "Set Project Settings > Player > Android > Multithreaded Rendering by hand.");
    }

    static void SetBatching(bool staticBatching, bool dynamicBatching)
    {
        // SetBatchingForPlatform is internal - reflection is the only route.
        // Dynamic batching is deprecated on 6.5 (the SRP Batcher supersedes it) so it stays off.
        var candidates = typeof(PlayerSettings)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => m.Name == "SetBatchingForPlatform")
            .Where(m => m.GetParameters().Length == 3)
            .ToArray();

        foreach (var m in candidates)
        {
            var p = m.GetParameters();
            if (p[1].ParameterType != typeof(int) || p[2].ParameterType != typeof(int))
                continue;

            object first;
            if (p[0].ParameterType == typeof(BuildTarget)) first = BuildTarget.Android;
            else if (p[0].ParameterType == typeof(NamedBuildTarget)) first = NamedBuildTarget.Android;
            else if (p[0].ParameterType == typeof(BuildTargetGroup)) first = BuildTargetGroup.Android;
            else continue;

            m.Invoke(null, new object[] { first, staticBatching ? 1 : 0, dynamicBatching ? 1 : 0 });
            Log("Static batching = " + staticBatching + ", dynamic batching = " + dynamicBatching + ".");
            return;
        }

        throw new Exception("PlayerSettings.SetBatchingForPlatform not found. " +
                            "Tick Static Batching / untick Dynamic Batching by hand in Player settings.");
    }

    static void SetGpuSkinningBatched()
    {
        // Unity 6 replaced the GPU Skinning checkbox with a Mesh Deformation dropdown
        // (CPU / GPU / GPU (Batched)). The old bool still exists on some versions.
        var meshDeformation = FindType("UnityEditor.MeshDeformation") ??
                              FindType("UnityEngine.MeshDeformation");

        if (meshDeformation != null && meshDeformation.IsEnum)
        {
            object batched = null;
            foreach (var name in new[] { "GPUBatched", "GpuBatched", "GPU_Batched", "BatchedGPU" })
            {
                if (Enum.GetNames(meshDeformation).Contains(name))
                {
                    batched = Enum.Parse(meshDeformation, name);
                    break;
                }
            }

            if (batched != null)
            {
                var m = FindMethod(typeof(PlayerSettings), "SetMeshDeformation",
                                   typeof(NamedBuildTarget), meshDeformation);
                if (m != null)
                {
                    m.Invoke(null, new[] { (object)NamedBuildTarget.Android, batched });
                    Log("Mesh deformation (GPU skinning) = GPU (Batched).");
                    return;
                }

                m = FindMethod(typeof(PlayerSettings), "SetMeshDeformation",
                               typeof(BuildTargetGroup), meshDeformation);
                if (m != null)
                {
                    m.Invoke(null, new[] { (object)BuildTargetGroup.Android, batched });
                    Log("Mesh deformation (GPU skinning) = GPU (Batched).");
                    return;
                }

                if (TrySetMember(null, typeof(PlayerSettings), "meshDeformation", batched))
                {
                    Log("Mesh deformation (GPU skinning) = GPU (Batched) via PlayerSettings.meshDeformation.");
                    return;
                }
            }
        }

        if (TrySetMember(null, typeof(PlayerSettings), "gpuSkinning", true))
        {
            Warn("Set the legacy PlayerSettings.gpuSkinning = true. Could not find the Unity 6 " +
                 "'GPU (Batched)' option - check Player settings > Other > Mesh Deformation by hand.");
            return;
        }

        throw new Exception("Neither MeshDeformation nor PlayerSettings.gpuSkinning could be set.");
    }

    // ------------------------------------------------------------------ 3. XR management / OpenXR loader

    static void EnableOpenXRForAndroid()
    {
        var tGeneral = RequireType("UnityEngine.XR.Management.XRGeneralSettings",
                                   "com.unity.xr.management is not installed or failed to compile.");
        var tPerTarget = RequireType("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget",
                                     "com.unity.xr.management editor assembly not found.");
        var tManager = RequireType("UnityEngine.XR.Management.XRManagerSettings",
                                   "XRManagerSettings not found.");
        var tStore = RequireType("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore",
                                 "XRPackageMetadataStore not found.");
        RequireType(kOpenXRLoaderFullName,
                    "com.unity.xr.openxr is not installed - the Quest 3 XR path needs it.");

        // --- the per-build-target config object -------------------------------------
        var key = kXrSettingsKeyFallback;
        var keyField = tGeneral.GetField("k_SettingsKey",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (keyField != null && keyField.FieldType == typeof(string))
            key = (string)(keyField.IsLiteral ? keyField.GetRawConstantValue() : keyField.GetValue(null));

        var tryGet = typeof(EditorBuildSettings)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "TryGetConfigObject" && m.IsGenericMethodDefinition);
        if (tryGet == null)
            throw new Exception("EditorBuildSettings.TryGetConfigObject<T> not found.");

        var args = new object[] { key, null };
        tryGet.MakeGenericMethod(tPerTarget).Invoke(null, args);
        var perTarget = args[1] as UnityEngine.Object;

        if (perTarget == null)
        {
            if (!AssetDatabase.IsValidFolder(kXrSettingsFolder))
                AssetDatabase.CreateFolder("Assets", "XR");

            var created = ScriptableObject.CreateInstance(tPerTarget);
            created.name = "XRGeneralSettingsPerBuildTarget";
            AssetDatabase.CreateAsset(created, kXrSettingsAsset);
            EditorBuildSettings.AddConfigObject(key, created, true);
            perTarget = created;
            Log("Created " + kXrSettingsAsset + " and registered it under '" + key + "'.");
        }
        else
        {
            Log("Found existing XR settings config object under '" + key + "'.");
        }

        // --- the Android XRGeneralSettings ------------------------------------------
        var settingsFor = FindMethod(tPerTarget, "SettingsForBuildTarget", typeof(BuildTargetGroup));
        if (settingsFor == null)
            throw new Exception("XRGeneralSettingsPerBuildTarget.SettingsForBuildTarget(BuildTargetGroup) not found.");

        var androidSettings = settingsFor.Invoke(perTarget, new object[] { BuildTargetGroup.Android })
                              as UnityEngine.Object;

        if (androidSettings == null)
        {
            var setSettingsFor = FindMethod(tPerTarget, "SetSettingsForBuildTarget",
                                            typeof(BuildTargetGroup), tGeneral);
            if (setSettingsFor == null)
                throw new Exception("XRGeneralSettingsPerBuildTarget.SetSettingsForBuildTarget not found.");

            var created = ScriptableObject.CreateInstance(tGeneral);
            created.name = "Android Settings";
            AssetDatabase.AddObjectToAsset(created, perTarget);
            setSettingsFor.Invoke(perTarget, new object[] { BuildTargetGroup.Android, created });
            androidSettings = created;
            Log("Created XRGeneralSettings for the Android build target group.");
        }

        // --- the XRManagerSettings ---------------------------------------------------
        object managerObj;
        TryGetMember(androidSettings, tGeneral, "Manager", out managerObj);
        var manager = managerObj as UnityEngine.Object;

        if (manager == null)
        {
            var created = ScriptableObject.CreateInstance(tManager);
            created.name = "Android Providers";
            AssetDatabase.AddObjectToAsset(created, perTarget);

            if (!TrySetMember(androidSettings, tGeneral, "Manager", created) &&
                !TrySetMember(androidSettings, tGeneral, "m_LoaderManagerInstance", created))
                throw new Exception("Could not assign XRManagerSettings to XRGeneralSettings.Manager.");

            manager = created;
            Log("Created the Android XRManagerSettings (loader list).");
        }

        if (!TrySetMember(androidSettings, tGeneral, "InitManagerOnStart", true) &&
            !TrySetMember(androidSettings, tGeneral, "m_InitManagerOnStart", true))
            Warn("Could not set InitManagerOnStart - tick 'Initialize XR on Startup' by hand.");

        // --- assign the OpenXR loader ------------------------------------------------
        var assigned = false;
        var assign = FindMethod(tStore, "AssignLoader", tManager, typeof(string), typeof(BuildTargetGroup));
        if (assign != null)
        {
            foreach (var loaderName in new[] { kOpenXRLoaderFullName, "OpenXRLoader" })
            {
                try
                {
                    var result = assign.Invoke(null, new object[] { manager, loaderName, BuildTargetGroup.Android });
                    if (result is bool && (bool)result)
                    {
                        assigned = true;
                        Log("XRPackageMetadataStore.AssignLoader succeeded for '" + loaderName + "'.");
                        break;
                    }
                    Warn("AssignLoader returned false for '" + loaderName + "'.");
                }
                catch (Exception e)
                {
                    Warn("AssignLoader threw for '" + loaderName + "': " + (e.InnerException ?? e).Message);
                }
            }
        }
        else
        {
            Warn("XRPackageMetadataStore.AssignLoader(XRManagerSettings, string, BuildTargetGroup) not found.");
        }

        if (!assigned)
            assigned = ManuallyAddOpenXRLoader(tManager, manager, perTarget);

        EditorUtility.SetDirty(perTarget);
        EditorUtility.SetDirty(androidSettings);
        EditorUtility.SetDirty(manager);
        AssetDatabase.SaveAssets();

        var loaderNames = DescribeActiveLoaders(tManager, manager);
        Log("Android XR loaders: " + (loaderNames.Count == 0 ? "(none)" : string.Join(", ", loaderNames)));

        if (!loaderNames.Any(n => n.IndexOf("OpenXR", StringComparison.OrdinalIgnoreCase) >= 0))
            throw new Exception("OpenXRLoader is NOT in the Android loader list after assignment. " +
                                "Open Project Settings > XR Plug-in Management (Android tab) and tick OpenXR.");
    }

    static bool ManuallyAddOpenXRLoader(Type tManager, UnityEngine.Object manager, UnityEngine.Object owner)
    {
        var loaderType = FindType(kOpenXRLoaderFullName);
        if (loaderType == null) return false;

        var tryAdd = tManager.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                             .FirstOrDefault(m => m.Name == "TryAddLoader" && m.GetParameters().Length >= 1);
        if (tryAdd == null)
        {
            Warn("XRManagerSettings.TryAddLoader not found - cannot add the loader manually.");
            return false;
        }

        var loader = ScriptableObject.CreateInstance(loaderType);
        loader.name = loaderType.Name;
        AssetDatabase.AddObjectToAsset(loader, owner);

        var pars = tryAdd.GetParameters();
        var callArgs = new object[pars.Length];
        callArgs[0] = loader;
        for (var i = 1; i < pars.Length; i++)
            callArgs[i] = pars[i].HasDefaultValue ? pars[i].DefaultValue : (object)(-1);

        var ok = tryAdd.Invoke(manager, callArgs);
        var added = !(ok is bool) || (bool)ok;
        Log(added
            ? "Added OpenXRLoader manually via XRManagerSettings.TryAddLoader."
            : "XRManagerSettings.TryAddLoader returned false for OpenXRLoader.");
        return added;
    }

    static List<string> DescribeActiveLoaders(Type tManager, UnityEngine.Object manager)
    {
        var names = new List<string>();
        object list;
        if (!TryGetMember(manager, tManager, "activeLoaders", out list) || list == null)
            TryGetMember(manager, tManager, "m_Loaders", out list);

        var enumerable = list as System.Collections.IEnumerable;
        if (enumerable == null) return names;

        foreach (var loader in enumerable)
            if (loader != null) names.Add(loader.GetType().Name);

        return names;
    }

    // ------------------------------------------------------------------ 4. OpenXR features

    static void ConfigureOpenXRFeatures()
    {
        var tSettings = RequireType("UnityEngine.XR.OpenXR.OpenXRSettings",
                                    "com.unity.xr.openxr is not installed.");

        // Make sure the feature list for Android has been discovered before we poke at it.
        var tHelpers = FindType("UnityEditor.XR.OpenXR.Features.FeatureHelpers");
        if (tHelpers != null)
        {
            var refresh = tHelpers.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                  .FirstOrDefault(m => m.Name == "RefreshFeatures" &&
                                                       m.GetParameters().Length == 1);
            if (refresh != null)
            {
                var pt = refresh.GetParameters()[0].ParameterType;
                if (pt == typeof(BuildTargetGroup)) refresh.Invoke(null, new object[] { BuildTargetGroup.Android });
                else if (pt == typeof(BuildTarget)) refresh.Invoke(null, new object[] { BuildTarget.Android });
                Log("FeatureHelpers.RefreshFeatures ran for Android.");
            }
        }
        else
        {
            Warn("UnityEditor.XR.OpenXR.Features.FeatureHelpers not found; skipping RefreshFeatures.");
        }

        // --- Meta Quest feature set (do this FIRST: it rewrites individual feature flags) ---
        EnableMetaQuestFeatureSet();

        // --- individual features ------------------------------------------------------
        var getSettings = tSettings.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "GetSettingsForBuildTargetGroup" &&
                                 m.GetParameters().Length == 1 &&
                                 m.GetParameters()[0].ParameterType == typeof(BuildTargetGroup));
        if (getSettings == null)
            throw new Exception("OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup) not found.");

        var androidOpenXR = getSettings.Invoke(null, new object[] { BuildTargetGroup.Android });
        if (androidOpenXR == null)
            throw new Exception("OpenXRSettings for the Android build target group is null.");

        var features = GetFeatures(tSettings, androidOpenXR);
        if (features.Length == 0)
            throw new Exception("OpenXRSettings reported zero features for Android - cannot enable the " +
                                "Oculus Touch Controller Profile.");

        var quest = EnableFeature(features, kMetaQuestFeatureIds, kMetaQuestFeatureTypeNames);
        if (quest != null) Log("Enabled OpenXR feature: " + quest);
        else Warn("Meta Quest support feature not found by id/type name - the feature set above may " +
                  "already cover it.");

        var touch = EnableFeature(features, kOculusTouchFeatureIds, kOculusTouchFeatureTypeNames);
        if (touch != null)
        {
            Log("Enabled OpenXR interaction profile: " + touch);
        }
        else
        {
            Error("Available OpenXR features for Android were: " +
                  string.Join(", ", features.Select(f => f.GetType().Name).ToArray()));
            throw new Exception("Oculus Touch Controller Profile not found in the Android OpenXR feature list.");
        }

        var settingsAsObject = androidOpenXR as UnityEngine.Object;
        if (settingsAsObject != null) EditorUtility.SetDirty(settingsAsObject);
        foreach (var f in features)
        {
            var o = f as UnityEngine.Object;
            if (o != null) EditorUtility.SetDirty(o);
        }
        AssetDatabase.SaveAssets();

        var enabled = features.Where(IsFeatureEnabled).Select(f => f.GetType().Name).ToArray();
        Log("Enabled Android OpenXR features: " + (enabled.Length == 0 ? "(none)" : string.Join(", ", enabled)));
    }

    static void EnableMetaQuestFeatureSet()
    {
        var tManager = FindType("UnityEditor.XR.OpenXR.Features.OpenXRFeatureSetManager");
        if (tManager == null)
        {
            Warn("OpenXRFeatureSetManager not found - enable the 'Meta Quest' feature group by hand " +
                 "in Project Settings > XR Plug-in Management > OpenXR (Android tab).");
            return;
        }

        object featureSet = null;
        var getById = FindMethod(tManager, "GetFeatureSetWithId", typeof(BuildTargetGroup), typeof(string));
        if (getById != null)
        {
            foreach (var id in kMetaFeatureSetIds)
            {
                featureSet = getById.Invoke(null, new object[] { BuildTargetGroup.Android, id });
                if (featureSet != null)
                {
                    Log("Found OpenXR feature set '" + id + "'.");
                    break;
                }
            }
        }

        if (featureSet == null)
        {
            // Fall back to scanning whatever feature sets this OpenXR version reports.
            var infos = tManager.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "FeatureSetInfosForBuildTarget" &&
                                     m.GetParameters().Length == 1);
            if (infos != null)
            {
                var list = infos.Invoke(null, new object[] { BuildTargetGroup.Android })
                           as System.Collections.IEnumerable;
                if (list != null)
                {
                    var seen = new List<string>();
                    foreach (var fs in list)
                    {
                        object id, name;
                        TryGetMember(fs, fs.GetType(), "featureSetId", out id);
                        TryGetMember(fs, fs.GetType(), "name", out name);
                        var idStr = (id as string) ?? "";
                        var nameStr = (name as string) ?? "";
                        seen.Add(idStr + " (" + nameStr + ")");

                        if (idStr.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            idStr.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            nameStr.IndexOf("meta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            nameStr.IndexOf("quest", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            featureSet = fs;
                            Log("Matched OpenXR feature set by name: " + idStr + " (" + nameStr + ")");
                            break;
                        }
                    }

                    if (featureSet == null)
                        Warn("No Meta/Quest feature set among: " + string.Join(", ", seen.ToArray()));
                }
            }
        }

        if (featureSet == null)
            throw new Exception("Could not locate the Meta Quest OpenXR feature set. Tick 'Meta Quest' " +
                                "in Project Settings > XR Plug-in Management > OpenXR > Android by hand.");

        if (!TrySetMember(featureSet, featureSet.GetType(), "isEnabled", true))
            throw new Exception("Could not set isEnabled on the Meta Quest feature set.");

        var apply = tManager.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(m => m.Name == "SetFeaturesFromEnabledFeatureSets" &&
                                 m.GetParameters().Length == 1);
        if (apply != null)
        {
            var pt = apply.GetParameters()[0].ParameterType;
            if (pt == typeof(BuildTargetGroup)) apply.Invoke(null, new object[] { BuildTargetGroup.Android });
            else if (pt == typeof(BuildTarget)) apply.Invoke(null, new object[] { BuildTarget.Android });
            Log("Applied the Meta Quest feature set to the Android OpenXR settings.");
        }
        else
        {
            Warn("OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets not found; the feature set " +
                 "is marked enabled but its features may not have been switched on.");
        }
    }

    static object[] GetFeatures(Type tSettings, object settings)
    {
        var getFeatures = tSettings.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "GetFeatures" &&
                                 !m.IsGenericMethod &&
                                 m.GetParameters().Length == 0);

        object raw = null;
        if (getFeatures != null) raw = getFeatures.Invoke(settings, null);
        if (raw == null) TryGetMember(settings, tSettings, "features", out raw);
        if (raw == null) TryGetMember(settings, tSettings, "m_features", out raw);

        var array = raw as Array;
        if (array == null) return new object[0];

        return array.Cast<object>().Where(o => o != null).ToArray();
    }

    static string EnableFeature(object[] features, string[] ids, string[] typeNames)
    {
        foreach (var feature in features)
        {
            var type = feature.GetType();
            var id = GetFeatureId(type);
            var match = (id != null && ids.Contains(id)) ||
                        typeNames.Any(n => string.Equals(n, type.Name, StringComparison.Ordinal));
            if (!match) continue;

            if (!TrySetMember(feature, type, "enabled", true) &&
                !TrySetMember(feature, type, "m_enabled", true))
                throw new Exception("Found feature " + type.Name + " but could not set 'enabled' on it.");

            return type.Name + (id == null ? "" : " [" + id + "]");
        }
        return null;
    }

    static bool IsFeatureEnabled(object feature)
    {
        object value;
        if (TryGetMember(feature, feature.GetType(), "enabled", out value) && value is bool)
            return (bool)value;
        return false;
    }

    static string GetFeatureId(Type featureType)
    {
        var field = featureType.GetField("featureId",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        if (field != null && field.FieldType == typeof(string))
        {
            try
            {
                return (string)(field.IsLiteral ? field.GetRawConstantValue() : field.GetValue(null));
            }
            catch { /* fall through to the attribute */ }
        }

        foreach (var attr in featureType.GetCustomAttributes(false))
        {
            var at = attr.GetType();
            if (at.Name != "OpenXRFeatureAttribute") continue;
            object id;
            if (TryGetMember(attr, at, "FeatureId", out id) && id is string) return (string)id;
        }

        return null;
    }

    // ------------------------------------------------------------------ 5. Android manifest

    static void WriteAndroidManifest()
    {
        var projectRoot = Directory.GetParent(Application.dataPath).FullName;
        var absoluteDir = Path.Combine(projectRoot, kManifestFolder.Replace('/', Path.DirectorySeparatorChar));
        var absolutePath = Path.Combine(projectRoot, kManifestPath.Replace('/', Path.DirectorySeparatorChar));

        Directory.CreateDirectory(absoluteDir);

        XDocument doc;
        if (File.Exists(absolutePath))
        {
            doc = XDocument.Load(absolutePath);
            var manifest = doc.Root;
            if (manifest == null || manifest.Name.LocalName != "manifest")
                throw new Exception("Existing " + kManifestPath + " has no <manifest> root element.");

            if (manifest.Attribute(XNamespace.Xmlns + "tools") == null)
                manifest.SetAttributeValue(XNamespace.Xmlns + "tools", Tools.NamespaceName);

            var application = manifest.Element("application");
            if (application == null)
            {
                application = new XElement("application");
                manifest.Add(application);
            }

            application.SetAttributeValue(Android + "usesCleartextTraffic", "true");
            application.SetAttributeValue(Tools + "replace", "android:usesCleartextTraffic");
            Log("Patched the existing " + kManifestPath + " (usesCleartextTraffic=\"true\").");
        }
        else
        {
            doc = BuildManifestDocument();
            Log("Created " + kManifestPath + " (usesCleartextTraffic=\"true\").");
        }

        doc.Save(absolutePath);
        AssetDatabase.ImportAsset(kManifestPath, ImportAssetOptions.ForceUpdate);

        EnableCustomMainManifestFlag();
    }

    static XDocument BuildManifestDocument()
    {
        string activityName, activityTheme;
        var gameActivity = UsesGameActivityEntryPoint();
        if (gameActivity)
        {
            activityName = "com.unity3d.player.UnityPlayerGameActivity";
            activityTheme = "@style/BaseUnityGameActivityTheme";
        }
        else
        {
            activityName = "com.unity3d.player.UnityPlayerActivity";
            activityTheme = "@style/UnityThemeSelector";
        }
        Log("Manifest launcher activity: " + activityName +
            " (Player > Application Entry Point = " + (gameActivity ? "GameActivity" : "Activity") + ").");

        var activity = new XElement("activity",
            new XAttribute(Android + "name", activityName),
            new XAttribute(Android + "theme", activityTheme),
            new XAttribute(Android + "exported", "true"),
            new XElement("intent-filter",
                new XElement("action", new XAttribute(Android + "name", "android.intent.action.MAIN")),
                new XElement("category", new XAttribute(Android + "name", "android.intent.category.LAUNCHER"))),
            new XElement("meta-data",
                new XAttribute(Android + "name", "unityplayer.UnityActivity"),
                new XAttribute(Android + "value", "true")));

        if (gameActivity)
        {
            activity.Add(new XElement("meta-data",
                new XAttribute(Android + "name", "android.app.lib_name"),
                new XAttribute(Android + "value", "game")));
        }

        // INTERNET is for the guide service on the laptop; RECORD_AUDIO for MicrophoneCapture.
        // A custom main manifest replaces Unity's default one, so both are declared explicitly.
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("manifest",
                new XAttribute(XNamespace.Xmlns + "android", Android.NamespaceName),
                new XAttribute(XNamespace.Xmlns + "tools", Tools.NamespaceName),
                new XElement("uses-permission", new XAttribute(Android + "name", "android.permission.INTERNET")),
                new XElement("uses-permission", new XAttribute(Android + "name", "android.permission.RECORD_AUDIO")),
                new XElement("application",
                    new XAttribute(Android + "usesCleartextTraffic", "true"),
                    new XAttribute(Tools + "replace", "android:usesCleartextTraffic"),
                    activity)));
    }

    static bool UsesGameActivityEntryPoint()
    {
        var prop = typeof(PlayerSettings.Android).GetProperty("applicationEntry",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (prop == null) return false;

        object value;
        try { value = prop.GetValue(null); }
        catch { return false; }
        if (value == null) return false;

        var flags = value.ToString().Split(',').Select(s => s.Trim()).ToArray();
        var hasClassicActivity = flags.Contains("Activity");
        var hasGameActivity = flags.Any(f => f == "GameActivity");
        return hasGameActivity && !hasClassicActivity;
    }

    static void EnableCustomMainManifestFlag()
    {
        // There is no documented public toggle for this: Unity ticks Publishing Settings >
        // Custom Main Manifest when Assets/Plugins/Android/AndroidManifest.xml exists. Try the
        // internal property anyway, and tell the team plainly if it isn't there.
        foreach (var owner in new[] { typeof(PlayerSettings.Android), typeof(PlayerSettings) })
        {
            foreach (var name in new[] { "androidCustomMainManifest", "customMainManifest" })
            {
                if (TrySetMember(null, owner, name, true))
                {
                    Log("Set " + owner.Name + "." + name + " = true.");
                    return;
                }
            }
        }

        Log("No scripting API for the 'Custom Main Manifest' toggle in this Editor version. " +
            "That is fine: Unity treats " + kManifestPath + " as the custom main manifest because the " +
            "file now exists. Verify it is ticked in Project Settings > Player > Android > " +
            "Publishing Settings before building.");
    }

    // ------------------------------------------------------------------ reflection helpers

    static Type FindType(string fullName)
    {
        var t = Type.GetType(fullName, false);
        if (t != null) return t;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                t = assembly.GetType(fullName, false);
                if (t != null) return t;
            }
            catch { /* dynamic or unloadable assembly - skip */ }
        }
        return null;
    }

    static Type RequireType(string fullName, string why)
    {
        var t = FindType(fullName);
        if (t == null) throw new Exception("Type not found: " + fullName + ". " + why);
        return t;
    }

    static MethodInfo FindMethod(Type type, string name, params Type[] parameterTypes)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Static | BindingFlags.Instance;
        try { return type.GetMethod(name, flags, null, parameterTypes, null); }
        catch { return null; }
    }

    static bool TryGetMember(object instance, Type type, string name, out object value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        value = null;
        try
        {
            var p = type.GetProperty(name, flags);
            if (p != null && p.CanRead) { value = p.GetValue(instance); return true; }

            var f = type.GetField(name, flags);
            if (f != null) { value = f.GetValue(instance); return true; }
        }
        catch (Exception e)
        {
            Warn("Reading " + type.Name + "." + name + " threw: " + (e.InnerException ?? e).Message);
        }
        return false;
    }

    static bool TrySetMember(object instance, Type type, string name, object value)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
        try
        {
            var p = type.GetProperty(name, flags);
            if (p != null && p.CanWrite) { p.SetValue(instance, value); return true; }

            var f = type.GetField(name, flags);
            if (f != null && !f.IsLiteral && !f.IsInitOnly) { f.SetValue(instance, value); return true; }
        }
        catch (Exception e)
        {
            Warn("Writing " + type.Name + "." + name + " threw: " + (e.InnerException ?? e).Message);
        }
        return false;
    }
}
