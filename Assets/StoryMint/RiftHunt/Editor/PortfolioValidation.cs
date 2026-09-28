using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace StoryMint.RiftHunt.Editor
{
    public static class PortfolioValidation
    {
        public const string ScenePath = "Assets/Scenes/RiftHuntPreview.unity";

        [MenuItem("Tools/StoryMint/Open Gameplay Preview")]
        public static void OpenPreview()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory("Assets/Scenes");
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                Camera.main.transform.SetPositionAndRotation(new Vector3(0, 0, -3), Quaternion.identity);
                new GameObject("Manual AR Provider - simulated tracking").AddComponent<ManualARProvider>();
                EditorSceneManager.SaveScene(scene, ScenePath);
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
                AssetDatabase.SaveAssets();
            }
            else EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Run()
        {
            try
            {
                OpenPreview();
                var results = new List<string>();
                int trails = 0, steps = 0;
                foreach (var path in Directory.GetFiles("Assets/StreamingAssets/StoryMint", "*.json"))
                {
                    var trail = JsonUtility.FromJson<RiftHuntTrailDefinition>(File.ReadAllText(path));
                    Require(trail.GetStartStep() != null, "Missing start: " + path);
                    var ids = new HashSet<string>();
                    foreach (var step in trail.steps)
                    {
                        Require(!string.IsNullOrWhiteSpace(step.id) && ids.Add(step.id), "Duplicate/blank ID: " + path);
                        Require(step.GetStepKind() != RiftHuntStepKind.Unknown, "Unknown step type: " + step.type);
                        Require(string.IsNullOrEmpty(step.nextStepId) || trail.GetStepById(step.nextStepId) != null, "Broken next step: " + path);
                    }
                    trails++; steps += trail.steps.Count;
                }
                results.Add($"Trail parsing and references: {trails} trails, {steps} steps.");
                var target = new StoryMintGeoCoordinate(49, 8);
                var fix = new StoryMintLocationFix { coordinate = target, accuracyMeters = 5 };
                Require(StoryMintGeoMath.EvaluateGeofence(fix, true, target, 20, 15).isInside, "Accurate position rejected.");
                fix.accuracyMeters = 100;
                Require(!StoryMintGeoMath.EvaluateGeofence(fix, true, target, 20, 15).isInside, "Inaccurate position accepted.");
                Require(!StoryMintGeoMath.EvaluateGeofence(fix, false, target, 20, 15).isInside, "Missing fix accepted.");
                double distance = StoryMintGeoMath.DistanceMeters(target, new StoryMintGeoCoordinate(50, 8));
                Require(Math.Abs(distance - 111195) < 10, "Known geodesic distance differs.");
                results.Add("Geofence: accurate/poor/missing fix and known-distance checks passed.");
                var testId = "publication-test-" + Guid.NewGuid().ToString("N");
                try
                {
                    StoryMintSaveState.SaveStep(testId, "checkpoint");
                    Require(StoryMintSaveState.LoadStep(testId) == "checkpoint", "Progress did not round-trip.");
                    StoryMintSaveState.SaveGameCompleted(testId, "game");
                    Require(StoryMintSaveState.IsGameCompleted(testId, "game"), "Completion was not saved.");
                    Require(!StoryMintSaveState.IsGameCompleted(testId + "-other", "game"), "Completion leaked between trails.");
                }
                finally
                {
                    StoryMintSaveState.ClearSave(testId);
                    StoryMintSaveState.ClearGameCompleted(testId, "game");
                }
                results.Add("Progress: round-trip, clear and trail-scoped completion passed.");
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/unity-validation.txt", "PASS - Unity " + Application.unityVersion + "\n" + string.Join("\n", results) + "\nNo physical tracking, mobile browser or licensed SDK acceptance is implied.\n");
                Debug.Log("Rift Hunt publication validation: PASS");
                EditorApplication.Exit(0);
            }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
