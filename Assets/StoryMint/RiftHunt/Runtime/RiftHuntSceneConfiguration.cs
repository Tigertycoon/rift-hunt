#if STORYMINT_OPENCV
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Optional scene-level configuration component for the Rift Hunt AR pipeline.
    /// Drop this on a GameObject in the scene to pre-configure the OpenCV AR setup
    /// instead of relying on the runtime builder.
    ///
    /// If this component exists, RiftHuntRuntimeSceneBuilder will find the
    /// OpenCVARSceneSetup it creates and skip its own construction.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class RiftHuntSceneConfiguration : MonoBehaviour
    {
        [Header("OpenCV AR Setup")]
        [Tooltip("Reference to the OpenCVARSceneSetup in this scene. Auto-resolved if null.")]
        [SerializeField] private OpenCVARSceneSetup arSetup;

        [Header("Target")]
        [SerializeField] private string sampleTargetId = "flower";

        [Header("Auto-Configure")]
        [SerializeField] private bool configureOnAwake = true;

        /// <summary>The configured AR setup.</summary>
        public OpenCVARSceneSetup ConfiguredARSetup => arSetup;

        private void Reset()
        {
            AutoResolveReferences();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
                AutoResolveReferences();
        }

        private void Awake()
        {
            // Disable any leftover Imagine components
            DisableImagineComponents();

            if (configureOnAwake)
                ConfigureScene();
        }

        public bool ConfigureScene()
        {
            AutoResolveReferences();

            if (arSetup == null)
            {
                Debug.LogWarning("[StoryMint] RiftHuntSceneConfiguration: No OpenCVARSceneSetup found. " +
                                 "The runtime builder will create one.");
                return false;
            }

            Debug.Log("[StoryMint] Scene configured with OpenCVARSceneSetup.");
            return true;
        }

        private void AutoResolveReferences()
        {
            if (arSetup == null)
                arSetup = FindObjectOfType<OpenCVARSceneSetup>();

            if (string.IsNullOrWhiteSpace(sampleTargetId))
                sampleTargetId = "flower";
        }

        /// <summary>
        /// Disables any Imagine WebAR components that may still be in the scene.
        /// Uses string matching to avoid compile-time dependency on Imagine SDK.
        /// </summary>
        private static void DisableImagineComponents()
        {
            MonoBehaviour[] allComponents = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
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

#endif
