#if STORYMINT_IMAGINE
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StoryMint.RiftHunt.Editor
{
    internal static class StoryMintWebGLBuilder
    {
        private const string ImagineScenePath = "Assets/Scenes/Imagine-Scene.unity";
        private const string ImagineTemplate = "PROJECT:iTracker";
        private const string DefaultOutputFolderName = "Targetfinder_ImagineTest";

        [MenuItem("Tools/StoryMint/Rift Hunt/Build Imagine Target Test WebGL")]
        public static void BuildImagineTargetTest()
        {
            string outputPath = GetOutputPathFromCommandLine();
            BuildImagineTargetTest(outputPath, exitOnFinish: ApplicationIsBatchMode());
        }

        private static void BuildImagineTargetTest(string outputPath, bool exitOnFinish)
        {
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (string.IsNullOrEmpty(projectRoot))
                    throw new InvalidOperationException("Could not resolve project root.");

                string fullOutputPath = Path.GetFullPath(Path.IsPathRooted(outputPath)
                    ? outputPath
                    : Path.Combine(projectRoot, outputPath));
                string fullProjectRoot = Path.GetFullPath(projectRoot);

                if (!fullOutputPath.StartsWith(fullProjectRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Refusing to build outside the Unity project folder: " + fullOutputPath);

                PlayerSettings.WebGL.template = ImagineTemplate;
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(ImagineScenePath, true)
                };

                if (Directory.Exists(fullOutputPath))
                    Directory.Delete(fullOutputPath, recursive: true);

                Directory.CreateDirectory(fullOutputPath);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ImagineScenePath },
                    locationPathName = fullOutputPath,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                bool succeeded = summary.result == BuildResult.Succeeded;

                UnityEngine.Debug.Log("[StoryMint] WebGL build " + summary.result +
                                      " at " + fullOutputPath +
                                      " (" + summary.totalSize + " bytes).");

                if (!succeeded)
                    throw new InvalidOperationException("WebGL build failed: " + summary.result);

                if (exitOnFinish)
                    EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
                if (exitOnFinish)
                    EditorApplication.Exit(1);
                throw;
            }
        }

        private static string GetOutputPathFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-storyMintBuildPath", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }

            return DefaultOutputFolderName;
        }

        private static bool ApplicationIsBatchMode()
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (string.Equals(arg, "-batchmode", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}

#endif
