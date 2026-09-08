// Headless APK builds for the Quest 3 target.
//
// Run headless:
//   Unity.exe -batchmode -nographics -projectPath <proj> -executeMethod HakuBuild.BuildQuest
//   Unity.exe -batchmode -nographics -projectPath <proj> -executeMethod HakuBuild.BuildQuestRelease
//
// Do NOT pass -quit: this class calls EditorApplication.Exit itself so the shell
// gets 0 only when the BuildReport says Succeeded.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class HakuBuild
{
    // The one scene that has to boot on the headset. It must be in the Build
    // Settings scene list (File > Build Profiles > Scene List) and enabled.
    const string MainScene = "Museum_Greybox";

    // Relative to the project folder, so it lands in <proj>/Builds/Haku.apk.
    const string OutputPath = "Builds/Haku.apk";

    /// Development APK: profiler-connectable, managed script debugging on.
    public static void BuildQuest()
    {
        Run(BuildOptions.Development | BuildOptions.AllowDebugging, "development");
    }

    /// Release APK: no development flags.
    public static void BuildQuestRelease()
    {
        Run(BuildOptions.None, "release");
    }

    static void Run(BuildOptions options, string flavour)
    {
        // Guard 1: Android build support installed for this Editor?
        // BuildPipeline still takes a BuildTargetGroup here, so derive it from
        // NamedBuildTarget rather than hard-coding the legacy enum.
        BuildTargetGroup group = NamedBuildTarget.Android.ToBuildTargetGroup();
        if (!BuildPipeline.IsBuildTargetSupported(group, BuildTarget.Android))
        {
            Fail("Android Build Support is not installed for this Editor. " +
                 "Unity Hub > Installs > 6000.5.5f1 > Add modules > Android Build Support " +
                 "(including OpenJDK and Android SDK & NDK Tools), then re-run.");
            return;
        }

        // Guard 2: scenes actually present in Build Settings.
        List<string> scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled && !string.IsNullOrEmpty(s.path))
            .Select(s => s.path)
            .ToList();

        if (scenes.Count == 0)
        {
            Fail("Build Settings has no enabled scenes. Add and tick " +
                 $"Assets/Scenes/{MainScene}.unity in File > Build Profiles > Scene List, then re-run.");
            return;
        }

        int mainIndex = scenes.FindIndex(p => Path.GetFileNameWithoutExtension(p) == MainScene);
        if (mainIndex < 0)
        {
            Fail($"Scene '{MainScene}' is not in the enabled Build Settings scene list. " +
                 "Enabled scenes: " + string.Join(", ", scenes));
            return;
        }

        // Scene 0 is what loads on the headset, so put Museum_Greybox first.
        string main = scenes[mainIndex];
        scenes.RemoveAt(mainIndex);
        scenes.Insert(0, main);

        string absoluteOutput = Path.GetFullPath(OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput));

        // .apk for `adb install`, not an .aab store bundle, and not a Gradle project export.
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[haku-build] switching active build target to Android ...");
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(NamedBuildTarget.Android, BuildTarget.Android))
            {
                Fail("Could not switch the active build target to Android.");
                return;
            }
        }

        var opts = new BuildPlayerOptions
        {
            scenes = scenes.ToArray(),
            locationPathName = absoluteOutput,
            target = BuildTarget.Android,
            targetGroup = group,
            options = options,
        };

        Debug.Log($"[haku-build] {flavour} build -> {absoluteOutput}");
        Debug.Log("[haku-build] scenes: " + string.Join(", ", opts.scenes));

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        if (report == null)
        {
            Fail("BuildPipeline.BuildPlayer returned no report - the build never started.");
            return;
        }

        // Every error, with the build step it came from.
        foreach (BuildStep step in report.steps)
        {
            foreach (BuildStepMessage msg in step.messages)
            {
                if (msg.type == LogType.Error || msg.type == LogType.Exception || msg.type == LogType.Assert)
                    Debug.LogError($"[haku-build] ERROR in '{step.name}': {msg.content}");
            }
        }

        BuildSummary summary = report.summary;
        Debug.Log(
            "[haku-build] ---- summary ----\n" +
            $"  result   : {summary.result}\n" +
            $"  output   : {summary.outputPath}\n" +
            $"  size     : {summary.totalSize / (1024f * 1024f):F1} MB ({summary.totalSize} bytes)\n" +
            $"  time     : {summary.totalTime}\n" +
            $"  errors   : {summary.totalErrors}\n" +
            $"  warnings : {summary.totalWarnings}");

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[haku-build] OK - adb install -r \"{summary.outputPath}\"");
            EditorApplication.Exit(0);
            return;
        }

        Debug.LogError($"[haku-build] build {summary.result} with {summary.totalErrors} error(s).");
        EditorApplication.Exit(1);
    }

    static void Fail(string message)
    {
        Debug.LogError("[haku-build] " + message);
        EditorApplication.Exit(1);
    }
}
