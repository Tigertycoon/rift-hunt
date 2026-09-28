using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace StoryMint.RiftHunt.Editor
{
    /// <summary>Tests initialization and routing. Does not play or certify every minigame.</summary>
    [InitializeOnLoad]
    public static class GameplaySmokeValidation
    {
        private const string Key = "RiftHunt.PublicationSmoke";
        private static double started;
        private static int phase;
        private static Exception failure;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        static GameplaySmokeValidation()
        {
            if (SessionState.GetBool(Key, false))
            {
                started = EditorApplication.timeSinceStartup;
                EditorApplication.update += Tick;
                Application.logMessageReceived += OnLog;
            }
        }

        public static void Run()
        {
            PortfolioValidation.OpenPreview();
            StoryMintSaveState.ClearSave("portfolio-preview");
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error)
                failure = new Exception(message + "\n" + stack);
        }

        private static void Tick()
        {
            try
            {
                if (failure != null) throw failure;
                if (EditorApplication.timeSinceStartup - started > 45) throw new TimeoutException("Gameplay smoke timed out.");
                if (!EditorApplication.isPlaying) return;
                var controller = UnityEngine.Object.FindFirstObjectByType<RiftHuntExperienceController>();
                if (controller == null) return;
                var type = typeof(RiftHuntExperienceController);
                if (!(bool)type.GetField("isInitialized", Private).GetValue(controller)) return;
                var step = (RiftHuntStepDefinition)type.GetField("currentStep", Private).GetValue(controller);
                if (phase == 0)
                {
                    if (step?.id != "intro") throw new InvalidOperationException("Preview did not start at intro.");
                    type.GetMethod("HandlePrimaryAction", Private).Invoke(controller, null);
                    UnityEngine.Object.FindFirstObjectByType<ManualARProvider>().SignalFound();
                    phase = 1;
                    return;
                }
                if (step?.id != "rune_sort") return;
                var trail = (RiftHuntTrailDefinition)type.GetField("trail", Private).GetValue(controller);
                var fields = new[] { "activeRuneSort", "activeSimonSays", "activeGyroMaze", "activeRuneCatcher", "activeSealTrace", "activeWhackARune", "activeRuneMemory" };
                var ids = new[] { "rune_sort", "simon_says", "gyro_maze", "rune_catcher", "seal_trace", "whack_a_rune", "rune_memory" };
                for (int i = 0; i < ids.Length; i++)
                {
                    type.GetMethod("ShowStep", Private).Invoke(controller, new object[] { trail.GetStepById(ids[i]) });
                    var game = type.GetField(fields[i], Private).GetValue(controller) as MonoBehaviour;
                    if (game == null || !game.isActiveAndEnabled) throw new InvalidOperationException("Minigame did not initialize: " + ids[i]);
                }
                if (failure != null) throw failure;
                Directory.CreateDirectory("TestResults");
                File.WriteAllText("TestResults/gameplay-smoke.txt", "PASS - Unity " + Application.unityVersion + "\nPreview booted; intro action and simulated image-found event advanced to the first minigame.\nAll seven original minigame controllers initialized with their UI.\nWin/lose paths, device tracking, mobile input and visuals were not validated.\n");
                Finish(0);
            }
            catch (Exception exception) { Debug.LogException(exception); Finish(1); }
        }

        private static void Finish(int result)
        {
            SessionState.SetBool(Key, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            StoryMintSaveState.ClearSave("portfolio-preview");
            EditorApplication.Exit(result);
        }
    }
}
