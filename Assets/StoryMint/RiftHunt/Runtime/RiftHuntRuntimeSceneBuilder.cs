#if STORYMINT_IMAGINE
using Imagine.WebAR;
#endif
using UnityEngine;
using Object = UnityEngine.Object;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Sets up the AR runtime scene for Rift Hunt.
    /// Picks the correct <see cref="IRiftHuntARProvider"/> based on scene name:
    ///   • SampleScene → OpenCV pipeline (<see cref="OpenCVARProvider"/>)
    ///   • Imagine scenes → Imagine WebAR pipeline (<see cref="ImagineARProvider"/>)
    /// </summary>
    public static class RiftHuntRuntimeSceneBuilder
    {
        private const string SampleSceneName = "SampleScene";
        private const string SampleTargetId = "flower";

        /// <summary>
        /// Ensures the scene has a working AR provider.
        /// Returns the abstracted <see cref="IRiftHuntARProvider"/>.
        /// </summary>
        public static bool EnsureSceneReady(
            RiftHuntTrailDefinition trail,
            out IRiftHuntARProvider arProvider,
            out Camera mainCamera,
            out string error)
        {
            string sceneName = trail != null ? trail.sceneName : null;
            string targetId = ResolveOpenCVTargetId(trail);

            mainCamera = Camera.main;
            if (mainCamera == null)
                mainCamera = Object.FindObjectOfType<Camera>();

            if (mainCamera == null)
            {
                arProvider = null;
                error = "No camera was found in the active scene.";
                return false;
            }

            foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (component is IRiftHuntARProvider sceneProvider)
                {
                    arProvider = sceneProvider;
                    error = null;
                    return true;
                }
            }
#if STORYMINT_OPENCV
            if (sceneName == SampleSceneName || string.IsNullOrWhiteSpace(sceneName))
                return BuildOpenCVScene(sceneName, targetId, mainCamera, out arProvider, out error);
#endif
#if STORYMINT_IMAGINE
            return BuildImagineScene(mainCamera, out arProvider, out error);
#else
            arProvider = null;
            error = "No AR provider is configured. Use the editor preview or enable a separately installed tracking integration.";
            return false;
#endif

        }

        public static bool EnsureSceneReady(
            string sceneName,
            out IRiftHuntARProvider arProvider,
            out Camera mainCamera,
            out string error)
        {
            RiftHuntTrailDefinition trail = new RiftHuntTrailDefinition { sceneName = sceneName };
            return EnsureSceneReady(trail, out arProvider, out mainCamera, out error);
        }

        // ===================================================================
        //  OpenCV path
        // ===================================================================

#if STORYMINT_OPENCV
        private static bool BuildOpenCVScene(
            string sceneName,
            string targetId,
            Camera mainCamera,
            out IRiftHuntARProvider arProvider,
            out string error)
        {
            Texture2D targetImage = LoadTargetImage(targetId);
            if (targetImage == null)
            {
                arProvider = null;
                error = "No OpenCV target image was found for targetId '" + targetId + "'. " +
                        "Add it under Assets/Resources/StoryMint/Targets/" + targetId + ".";
                return false;
            }

            // Look for an existing OpenCVARSceneSetup in the scene
            OpenCVARSceneSetup existingSetup = Object.FindObjectOfType<OpenCVARSceneSetup>();
            if (existingSetup != null)
            {
                ConfigureOpenCVSetup(existingSetup, targetImage, targetId);
                arProvider = new OpenCVARProvider(existingSetup);
                error = null;
                return true;
            }

            // Build one at runtime for the sample scene
            if (sceneName == SampleSceneName || string.IsNullOrWhiteSpace(sceneName))
            {
                DisableImagineComponents();

                GameObject setupObj = GameObject.Find("StoryMintARSetup");
                if (setupObj == null)
                    setupObj = new GameObject("StoryMintARSetup");

                OpenCVARSceneSetup setup = setupObj.GetComponent<OpenCVARSceneSetup>();
                if (setup == null)
                    setup = setupObj.AddComponent<OpenCVARSceneSetup>();

                ConfigureOpenCVSetup(setup, targetImage, targetId);

                arProvider = new OpenCVARProvider(setup);
                error = null;
                return true;
            }

            arProvider = null;
            error = "No OpenCVARSceneSetup was found in scene '" + sceneName + "'.";
            return false;
        }

#endif

        private static string ResolveOpenCVTargetId(RiftHuntTrailDefinition trail)
        {
            if (trail != null && trail.steps != null)
            {
                for (int index = 0; index < trail.steps.Count; index++)
                {
                    RiftHuntStepDefinition step = trail.steps[index];
                    if (step != null
                        && step.GetStepKind() == RiftHuntStepKind.Scan
                        && !string.IsNullOrWhiteSpace(step.targetId))
                    {
                        return step.targetId.Trim();
                    }
                }

                for (int index = 0; index < trail.steps.Count; index++)
                {
                    RiftHuntStepDefinition step = trail.steps[index];
                    if (step != null && !string.IsNullOrWhiteSpace(step.targetId))
                        return step.targetId.Trim();
                }
            }

            return SampleTargetId;
        }

        private static Texture2D LoadTargetImage(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId))
                targetId = SampleTargetId;

            Texture2D targetImage = Resources.Load<Texture2D>("StoryMint/Targets/" + targetId);
            if (targetImage == null)
                targetImage = Resources.Load<Texture2D>(targetId);

            return targetImage;
        }

#if STORYMINT_OPENCV
        private static void ConfigureOpenCVSetup(OpenCVARSceneSetup setup, Texture2D targetImage, string targetId)
        {
            setup.ConfigureTarget(
                targetImage,
                targetId,
                0.15f,
                0.5f,
                new Vector3(0f, 0.08f, 0.03f));
        }

#endif

        // ===================================================================
        //  Imagine path
        // ===================================================================

#if STORYMINT_IMAGINE
        private static bool BuildImagineScene(
            Camera mainCamera,
            out IRiftHuntARProvider arProvider,
            out string error)
        {
            // Find the Imagine ImageTracker via reflection so we don't need
            // a compile-time dependency on the Imagine.WebAR namespace.
            MonoBehaviour imagineTracker = FindImagineImageTracker();
            if (imagineTracker == null)
            {
                arProvider = null;
                error = "This scene is configured for Imagine WebAR but no " +
                        "Imagine.WebAR.ImageTracker component was found. " +
                        "Make sure the Imagine prefab is in the scene.";
                return false;
            }

            // Cast to the concrete Imagine type at runtime.
            // ImagineARProvider expects Imagine.WebAR.ImageTracker.
            var tracker = imagineTracker as Imagine.WebAR.ImageTracker;
            if (tracker == null)
            {
                arProvider = null;
                error = "Found an Imagine component but it could not be cast to ImageTracker.";
                return false;
            }

            arProvider = new ImagineARProvider(tracker);
            error = null;
            Debug.Log("[StoryMint] Using Imagine WebAR provider.");
            return true;
        }

#endif

        private static MonoBehaviour FindImagineImageTracker()
        {
            MonoBehaviour[] allComponents = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (MonoBehaviour comp in allComponents)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().FullName;
                if (typeName == "Imagine.WebAR.ImageTracker")
                    return comp;
            }
            return null;
        }

        // ===================================================================
        //  Helpers
        // ===================================================================

        /// <summary>
        /// Disables any Imagine WebAR components found in the scene so they
        /// don't conflict with the OpenCV pipeline.
        /// </summary>
        private static void DisableImagineComponents()
        {
            MonoBehaviour[] allComponents = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (MonoBehaviour comp in allComponents)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().FullName;
                if (typeName != null && typeName.StartsWith("Imagine.WebAR."))
                {
                    Debug.Log("[StoryMint] Disabling Imagine component: " + typeName + " on " + comp.gameObject.name);
                    comp.enabled = false;
                }
            }
        }

    }
}
