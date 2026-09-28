#if STORYMINT_OPENCV
using System;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Drop this on an empty GameObject in the scene.
    /// It sets up the OpenCV AR pipeline at runtime:
    ///   1. Creates a camera with OpenCVARCameraController
    ///   2. Creates an OpenCVImageTracker
    ///   3. Creates a target anchor that follows the tracked pose
    ///   4. Spawns the portal VFX on the anchor when tracked
    ///
    /// This replaces the Imagine WebAR ImageTracker + ARCamera setup.
    /// </summary>
    public sealed class OpenCVARSceneSetup : MonoBehaviour
    {
        [Header("Target Image")]
        [Tooltip("The image to track (drag your target texture here).")]
        [SerializeField] private Texture2D targetImage;

        [Tooltip("ID used by RiftHunt steps to reference this target.")]
        [SerializeField] private string targetId = "flower";

        [Tooltip("Physical width of the printed target in meters.")]
        [SerializeField] private float targetWidthMeters = 0.15f;

        [Header("Portal")]
        [Tooltip("Scale of the portal VFX relative to the target.")]
        [SerializeField] private float portalScale = 0.5f;

        [Tooltip("Local offset of the portal above the target.")]
        [SerializeField] private Vector3 portalOffset = new Vector3(0f, 0.08f, 0.03f);

        // -------------------------------------------------------------------
        //  Events (forwarded from internal tracker)
        // -------------------------------------------------------------------

        /// <summary>Fired when the target image is detected. Argument: target ID.</summary>
        public event Action<string> ImageFound;

        /// <summary>Fired when the target image is lost. Argument: target ID.</summary>
        public event Action<string> ImageLost;

        private Camera arCamera;
        private OpenCVARCameraController cameraController;
        private OpenCVImageTracker tracker;
        private Transform targetAnchor;
        private GameObject portalInstance;
        private bool targetIsTracked;

        public void ConfigureTarget(
            Texture2D image,
            string id,
            float widthMeters,
            float scale,
            Vector3 offset)
        {
            if (image != null)
                targetImage = image;

            if (!string.IsNullOrWhiteSpace(id))
                targetId = id;

            targetWidthMeters = widthMeters;
            portalScale = scale;
            portalOffset = offset;

            if (tracker != null && targetImage != null)
                tracker.SetReferenceImage(targetImage, targetId, targetWidthMeters);

            if (targetAnchor != null)
                targetAnchor.gameObject.name = "StoryMintTargetAnchor_" + targetId;

            if (portalInstance != null)
            {
                portalInstance.transform.localPosition = portalOffset;
                portalInstance.transform.localScale = Vector3.one * portalScale;
            }
        }

        private void Start()
        {
            SetupCamera();
            SetupTracker();
            SetupTargetAnchor();
        }

        private void Update()
        {
            if (!targetIsTracked || portalInstance == null)
                return;

            // Keep portal at tracked position
            targetAnchor.position = tracker.TrackedPosition;
            targetAnchor.rotation = tracker.TrackedRotation;
        }

        private void OnDestroy()
        {
            if (tracker == null)
                return;

            tracker.OnTargetFound.RemoveListener(HandleTargetFound);
            tracker.OnTargetLost.RemoveListener(HandleTargetLost);
            tracker.OnPoseUpdated.RemoveListener(HandlePoseUpdated);
        }

        // -------------------------------------------------------------------
        //  Setup
        // -------------------------------------------------------------------

        private void SetupCamera()
        {
            // Disable any existing Imagine ARCamera components
            var imagineComponents = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            foreach (var comp in imagineComponents)
            {
                if (comp.GetType().FullName == "Imagine.WebAR.ARCamera")
                {
                    Debug.Log("[StoryMint] Disabling Imagine ARCamera: " + comp.gameObject.name);
                    comp.enabled = false;
                }
            }

            // Find or create main camera
            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                GameObject camObj = new GameObject("StoryMintARCamera");
                mainCam = camObj.AddComponent<Camera>();
                mainCam.tag = "MainCamera";
            }

            arCamera = mainCam;
            arCamera.clearFlags = CameraClearFlags.Depth;
            arCamera.backgroundColor = new Color(0, 0, 0, 0);
            arCamera.nearClipPlane = 0.01f;
            arCamera.farClipPlane = 200f;

            // Add our camera controller
            cameraController = arCamera.gameObject.GetComponent<OpenCVARCameraController>();
            if (cameraController == null)
                cameraController = arCamera.gameObject.AddComponent<OpenCVARCameraController>();
        }

        private void SetupTracker()
        {
            tracker = gameObject.GetComponent<OpenCVImageTracker>();
            if (tracker == null)
                tracker = gameObject.AddComponent<OpenCVImageTracker>();

            // Wire up the camera controller to feed frames to the tracker
            cameraController.SetTracker(tracker);

            // Set reference image
            if (targetImage != null)
                tracker.SetReferenceImage(targetImage, targetId, targetWidthMeters);
            else
                Debug.LogWarning("[StoryMint] No target image assigned to OpenCVARSceneSetup!");

            // Subscribe to events
            tracker.OnTargetFound.AddListener(HandleTargetFound);
            tracker.OnTargetLost.AddListener(HandleTargetLost);
            tracker.OnPoseUpdated.AddListener(HandlePoseUpdated);
        }

        private void SetupTargetAnchor()
        {
            GameObject anchorObj = new GameObject("StoryMintTargetAnchor_" + targetId);
            anchorObj.transform.SetParent(transform, false);
            targetAnchor = anchorObj.transform;
        }

        // -------------------------------------------------------------------
        //  Tracking Events
        // -------------------------------------------------------------------

        private void HandleTargetFound(string id)
        {
            if (id != targetId)
                return;

            Debug.Log("[StoryMint] Target found: " + id);
            targetIsTracked = true;

            if (portalInstance == null)
                SpawnPortal();
            else
                portalInstance.SetActive(true);

            ImageFound?.Invoke(id);
        }

        private void HandlePoseUpdated(Vector3 position, Quaternion rotation)
        {
            if (targetAnchor == null)
                return;

            targetAnchor.SetPositionAndRotation(position, rotation);
        }

        private void HandleTargetLost(string id)
        {
            if (id != targetId)
                return;

            Debug.Log("[StoryMint] Target lost: " + id);
            targetIsTracked = false;

            // Don't hide immediately — keep at last known position
            // The RiftHunt controller handles the "fallback" state
            ImageLost?.Invoke(id);
        }

        private void SpawnPortal()
        {
            portalInstance = new GameObject("StoryMintRiftPortal");
            portalInstance.transform.SetParent(targetAnchor, false);
            portalInstance.transform.localPosition = portalOffset;
            portalInstance.transform.localScale = Vector3.one * portalScale;

            portalInstance.AddComponent<RiftPortalVfx>();
        }

        // -------------------------------------------------------------------
        //  Public API for RiftHunt integration
        // -------------------------------------------------------------------

        /// <summary>
        /// Returns the anchor transform for a given target ID.
        /// Used by RiftHuntExperienceController to position hotspots.
        /// </summary>
        public Transform GetTargetTransform(string id)
        {
            return id == targetId ? targetAnchor : null;
        }

        /// <summary>
        /// Whether a specific target is currently tracked.
        /// </summary>
        public bool IsImageTracked(string id)
        {
            return id == targetId && targetIsTracked;
        }
    }
}

#endif
