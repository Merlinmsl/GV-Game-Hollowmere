using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Hollowmere.EditorTools
{
    /// <summary>
    /// Builds the Windows game into Builds/Windows/Hollowmere.exe, from the scenes ticked in
    /// File > Build Profiles. The World scene must be first: it is the one the game starts in.
    ///
    /// Menu: Hollowmere > Build. Can also run with Unity closed:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod Hollowmere.EditorTools.GameBuild.BuildFromCommandLine [-development] -logFile Logs/build.log
    /// A development build can be attached to the Profiler; use it for measuring, not for handing in.
    /// </summary>
    public static class GameBuild
    {
        const string Output = "Builds/Windows/Hollowmere.exe";
        const string DevelopmentOutput = "Builds/WindowsDevelopment/Hollowmere.exe";

        [MenuItem("Hollowmere/Build/Windows Game", priority = 100)]
        public static void BuildFromMenu() => Build(false);

        [MenuItem("Hollowmere/Build/Windows Game (development, for the Profiler)", priority = 101)]
        public static void BuildDevelopmentFromMenu() => Build(true);

        public static void BuildFromCommandLine()
        {
            bool development = Array.IndexOf(Environment.GetCommandLineArgs(), "-development") >= 0;
            EditorApplication.Exit(Build(development) ? 0 : 1);
        }

        static bool Build(bool development)
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenes.Add(scene.path);

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = development ? DevelopmentOutput : Output,
                target = BuildTarget.StandaloneWindows64,
                options = development ? BuildOptions.Development | BuildOptions.ConnectWithProfiler : BuildOptions.None
            };

            Debug.Log("Hollowmere build: ===== building " + scenes.Count + " scenes to " + options.locationPathName + " =====");
            BuildSummary summary = BuildPipeline.BuildPlayer(options).summary;

            Debug.Log("Hollowmere build: " + summary.result + " in " + summary.totalTime.TotalMinutes.ToString("F1") + " min, "
                      + (summary.totalSize / (1024f * 1024f)).ToString("F0") + " MB, "
                      + summary.totalErrors + " errors, " + summary.totalWarnings + " warnings.");
            return summary.result == BuildResult.Succeeded;
        }
    }
}
