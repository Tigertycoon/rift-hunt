#if STORYMINT_OPENCV
using System;
using System.Collections;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.ImgprocModule;
using UnityEngine;
using UnityEngine.Rendering;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Manages the webcam feed, renders it as a Unity background quad,
    /// and feeds frames to OpenCVImageTracker.
    ///
    /// This replaces Imagine WebAR's ARCamera. Everything renders inside
    /// Unity's pipeline — no HTML canvas layering.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class OpenCVARCameraController : MonoBehaviour
    {
        [Header("Camera Settings")]
        [Tooltip("Preferred webcam resolution width.")]
        [SerializeField] private int requestedWidth = 1280;

        [Tooltip("Preferred webcam resolution height.")]
        [SerializeField] private int requestedHeight = 720;

        [Tooltip("Preferred FPS.")]
        [SerializeField] private int requestedFps = 30;

        [Tooltip("Swap requested webcam width/height to match the current screen orientation.")]
        [SerializeField] private bool matchRequestedResolutionToScreenOrientation;

        [Tooltip("Use front camera instead of back.")]
        [SerializeField] private bool useFrontCamera;

        [Tooltip("When WebGL cannot identify front/back cameras, prefer the last device for rear camera mode.")]
        [SerializeField] private bool preferLastDeviceWhenFacingIsUnknown = true;

        [Tooltip("Optional camera override for testing. Accepts front, back, first, last, an index like 0/1, or part of a device name.")]
        [SerializeField] private string cameraDeviceOverride;

        [Tooltip("Allow URL overrides like ?camera=last, ?camera=back, or ?camera=1 in WebGL builds.")]
        [SerializeField] private bool allowCameraUrlOverride;

        [Tooltip("Tap the top-right corner repeatedly to cycle cameras during testing.")]
        [SerializeField] private bool enableHiddenCameraSwitch = true;

        [Tooltip("Seconds to wait for WebGL/mobile camera permission and device enumeration.")]
        [SerializeField] private float cameraDeviceWaitSeconds = 8f;

        [SerializeField, Range(2, 8)] private int cornerTapCountToSwitch = 4;

        [SerializeField, Range(0.08f, 0.35f)] private float cornerTapAreaNormalized = 0.18f;

        [SerializeField] private float cornerTapWindowSeconds = 2f;

        [Tooltip("Show camera diagnostics on the device screen while tuning the WebGL build.")]
        [SerializeField] private bool showDebugOverlay = true;

        [Header("References")]
        [Tooltip("The tracker to feed frames to.")]
        [SerializeField] private OpenCVImageTracker tracker;

        [Header("Background")]
        [Tooltip("Distance of the video background quad from the camera.")]
        [SerializeField] private float backgroundDistance = 50f;

        [Header("WebGL Orientation Fix")]
        [Tooltip("On WebGL mobile, browsers often pre-rotate the webcam feed but report raw " +
                 "sensor dimensions. When enabled, the aspect ratio is corrected if the video " +
                 "orientation (landscape vs portrait) does not match the screen orientation " +
                 "and videoRotationAngle is 0.")]
        [SerializeField] private bool autoFixWebGLOrientationMismatch = true;

        [Header("Tracker Performance")]
        [Tooltip("Max width of the frame sent to the tracker (0 = no downscale). " +
                 "Smaller values are faster. 640 is a good balance.")]
        [SerializeField] private int trackerMaxWidth = 640;

        private Camera cam;
        private WebCamTexture webcamTexture;
        private GameObject backgroundQuad;
        private Material backgroundMaterial;

        // OpenCV mats (reused)
        private Mat rgbaMat;
        private Mat grayMat;
        private Mat trackerGrayMat;
        private Mat downscaledMat;
        private Color32[] pixelBuffer;
        private byte[] rgbaBytes;

        private bool isInitialized;
        private int activeDeviceIndex = -1;
        private int cornerTapCount;
        private float firstCornerTapTime;
        private Coroutine webcamStartRoutine;
        private string cameraStatus = "Camera not started";
        private int lastAppliedVideoRotation;
        private float lastRawVideoAspect;
        private float lastVisibleVideoAspect;
        private float lastScreenAspect;
        private bool orientationSwapDetected;

        // -------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------

        /// <summary>
        /// Whether the camera is running and providing frames.
        /// </summary>
        public bool IsRunning => webcamTexture != null && webcamTexture.isPlaying;

        /// <summary>
        /// The camera component on this GameObject.
        /// </summary>
        public Camera Camera => cam;

        /// <summary>
        /// Assign the image tracker that receives camera frames.
        /// </summary>
        public void SetTracker(OpenCVImageTracker value) => tracker = value;

        // -------------------------------------------------------------------
        //  Unity Lifecycle
        // -------------------------------------------------------------------

        private void Awake()
        {
            cam = GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Depth;
            cam.backgroundColor = Color.black;
            cam.allowHDR = false;
        }

        private void Start()
        {
            webcamStartRoutine = StartCoroutine(StartWebcamWhenReady());
        }

        private void Update()
        {
            HandleCameraSwitchShortcut();

            if (webcamTexture == null || !webcamTexture.isPlaying)
                return;

            if (!webcamTexture.didUpdateThisFrame)
            {
                if (webcamTexture.width < 100)
                    cameraStatus = "Waiting for first camera frame";
                return;
            }

            cameraStatus = "Camera running " + webcamTexture.width + "x" + webcamTexture.height +
                           " rot=" + webcamTexture.videoRotationAngle +
                           " mirror=" + webcamTexture.videoVerticallyMirrored;

            UpdateBackgroundQuad();
            FeedToTracker();
        }

        private void OnDestroy()
        {
            if (webcamTexture != null)
            {
                webcamTexture.Stop();
                Destroy(webcamTexture);
            }

            if (backgroundQuad != null)
                Destroy(backgroundQuad);
            if (backgroundMaterial != null)
                Destroy(backgroundMaterial);

            if (webcamStartRoutine != null)
                StopCoroutine(webcamStartRoutine);

            rgbaMat?.Dispose();
            grayMat?.Dispose();
            trackerGrayMat?.Dispose();
            downscaledMat?.Dispose();
        }

        // -------------------------------------------------------------------
        //  Webcam Setup
        // -------------------------------------------------------------------

        private IEnumerator StartWebcamWhenReady()
        {
            cameraStatus = "Requesting camera permission";

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
                yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);

            if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
            {
                cameraStatus = "Camera permission denied";
                Debug.LogError("[StoryMint] Webcam permission was not granted.");
                webcamStartRoutine = null;
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + Mathf.Max(0.5f, cameraDeviceWaitSeconds);
            WebCamDevice[] devices = WebCamTexture.devices;
            while (devices.Length == 0 && Time.realtimeSinceStartup < deadline)
            {
                cameraStatus = "Waiting for camera devices";
                yield return new WaitForSecondsRealtime(0.25f);
                devices = WebCamTexture.devices;
            }

            if (devices.Length == 0)
            {
                cameraStatus = "No webcam devices found";
                Debug.LogError("[StoryMint] No webcam found after waiting for permission/device enumeration.");
                webcamStartRoutine = null;
                yield break;
            }

            int deviceIndex = SelectCameraDeviceIndex(devices);
            StartWebcamDevice(devices, deviceIndex);
            webcamStartRoutine = null;
        }

        private void StartWebcamDevice(WebCamDevice[] devices, int deviceIndex)
        {
            if (deviceIndex < 0 || deviceIndex >= devices.Length)
                deviceIndex = 0;

            StopWebcam();

            string deviceName = devices[deviceIndex].name;
            activeDeviceIndex = deviceIndex;
            GetRequestedWebcamDimensions(out int cameraWidth, out int cameraHeight);

            webcamTexture = new WebCamTexture(deviceName, cameraWidth, cameraHeight, requestedFps);
            webcamTexture.Play();
            cameraStatus = "Starting camera #" + deviceIndex;

            Debug.Log("[StoryMint] Webcam started: #" + deviceIndex + " '" + deviceName + "'" +
                      " requested=" + cameraWidth + "x" + cameraHeight +
                      " actualRequest=" + webcamTexture.requestedWidth + "x" + webcamTexture.requestedHeight + ")");
        }

        private void StopWebcam()
        {
            if (webcamTexture == null)
                return;

            webcamTexture.Stop();
            Destroy(webcamTexture);
            webcamTexture = null;
        }

        private int SelectCameraDeviceIndex(WebCamDevice[] devices)
        {
            LogAvailableCameras(devices);

            if (devices.Length == 1)
                return 0;

            int overrideMatch = FindOverrideDeviceIndex(devices);
            if (overrideMatch >= 0)
                return overrideMatch;

            int keywordMatch = FindDeviceByName(devices, useFrontCamera);
            if (keywordMatch >= 0)
                return keywordMatch;

            int facingMatch = FindDeviceByFacingFlag(devices, useFrontCamera);
            if (facingMatch >= 0)
                return facingMatch;

            int fallbackIndex = !useFrontCamera && preferLastDeviceWhenFacingIsUnknown
                ? devices.Length - 1
                : 0;

            Debug.LogWarning("[StoryMint] Camera facing metadata is inconclusive. Falling back to device " +
                             fallbackIndex + " for " + (useFrontCamera ? "front" : "rear") + " camera mode.");
            return fallbackIndex;
        }

        private int FindOverrideDeviceIndex(WebCamDevice[] devices)
        {
            string requestedDevice = GetRequestedCameraDevice();
            if (string.IsNullOrWhiteSpace(requestedDevice))
                return -1;

            requestedDevice = requestedDevice.Trim();
            if (int.TryParse(requestedDevice, out int index))
            {
                if (index >= 0 && index < devices.Length)
                {
                    Debug.Log("[StoryMint] Camera override selected device #" + index + ".");
                    return index;
                }

                Debug.LogWarning("[StoryMint] Camera override index '" + requestedDevice + "' is out of range.");
                return -1;
            }

            if (IsKeyword(requestedDevice, "first", "default"))
                return 0;

            if (IsKeyword(requestedDevice, "last"))
                return devices.Length - 1;

            if (IsKeyword(requestedDevice, "back", "rear", "environment", "world", "main"))
            {
                int backCamera = FindDeviceByName(devices, false);
                return backCamera >= 0 ? backCamera : devices.Length - 1;
            }

            if (IsKeyword(requestedDevice, "front", "user", "selfie", "facetime"))
            {
                int frontCamera = FindDeviceByName(devices, true);
                return frontCamera >= 0 ? frontCamera : 0;
            }

            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].name.IndexOf(requestedDevice, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }

            Debug.LogWarning("[StoryMint] Camera override '" + requestedDevice + "' did not match any device.");
            return -1;
        }

        private string GetRequestedCameraDevice()
        {
            if (allowCameraUrlOverride)
            {
                string cameraFromUrl = GetUrlParameter("camera");
                if (string.IsNullOrWhiteSpace(cameraFromUrl))
                    cameraFromUrl = GetUrlParameter("cam");

                if (!string.IsNullOrWhiteSpace(cameraFromUrl))
                    return cameraFromUrl;
            }

            return cameraDeviceOverride;
        }

        private void GetRequestedWebcamDimensions(out int width, out int height)
        {
            width = Mathf.Max(16, requestedWidth);
            height = Mathf.Max(16, requestedHeight);

            if (!matchRequestedResolutionToScreenOrientation)
                return;

            if (Screen.height > Screen.width)
                EnsurePortraitDimensions(ref width, ref height);
            else
                EnsureLandscapeDimensions(ref width, ref height);
        }

        private static void EnsurePortraitDimensions(ref int width, ref int height)
        {
            if (width <= height)
                return;

            int temp = width;
            width = height;
            height = temp;
        }

        private static void EnsureLandscapeDimensions(ref int width, ref int height)
        {
            if (width >= height)
                return;

            int temp = width;
            width = height;
            height = temp;
        }

        private static int FindDeviceByName(WebCamDevice[] devices, bool frontCamera)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                string name = devices[i].name;
                if (frontCamera && HasAnyKeyword(name, "front", "user", "selfie", "facetime"))
                    return i;

                if (!frontCamera && HasAnyKeyword(name, "back", "rear", "environment", "world", "main"))
                    return i;
            }

            return -1;
        }

        private static int FindDeviceByFacingFlag(WebCamDevice[] devices, bool frontCamera)
        {
            if (frontCamera)
            {
                for (int i = 0; i < devices.Length; i++)
                {
                    if (devices[i].isFrontFacing)
                        return i;
                }

                return -1;
            }

            bool hasKnownFrontCamera = false;
            for (int i = 0; i < devices.Length; i++)
            {
                if (devices[i].isFrontFacing || HasAnyKeyword(devices[i].name, "front", "user", "selfie", "facetime"))
                {
                    hasKnownFrontCamera = true;
                    break;
                }
            }

            if (!hasKnownFrontCamera)
                return -1;

            for (int i = 0; i < devices.Length; i++)
            {
                if (!devices[i].isFrontFacing && !HasAnyKeyword(devices[i].name, "front", "user", "selfie", "facetime"))
                    return i;
            }

            return -1;
        }

        private static bool HasAnyKeyword(string value, params string[] keywords)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i < keywords.Length; i++)
            {
                if (value.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsKeyword(string value, params string[] keywords)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                if (string.Equals(value, keywords[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static void LogAvailableCameras(WebCamDevice[] devices)
        {
            for (int i = 0; i < devices.Length; i++)
            {
                Debug.Log("[StoryMint] Camera device " + i + ": '" + devices[i].name +
                          "', isFrontFacing=" + devices[i].isFrontFacing);
            }
        }

        private static string GetUrlParameter(string key)
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url))
                return null;

            int queryStart = url.IndexOf('?');
            if (queryStart < 0 || queryStart >= url.Length - 1)
                return null;

            int fragmentStart = url.IndexOf('#', queryStart);
            string query = fragmentStart >= 0
                ? url.Substring(queryStart + 1, fragmentStart - queryStart - 1)
                : url.Substring(queryStart + 1);

            string[] pairs = query.Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                string pair = pairs[i];
                if (string.IsNullOrEmpty(pair))
                    continue;

                int equalsIndex = pair.IndexOf('=');
                string pairKey = equalsIndex >= 0 ? pair.Substring(0, equalsIndex) : pair;
                if (!string.Equals(Uri.UnescapeDataString(pairKey), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = equalsIndex >= 0 ? pair.Substring(equalsIndex + 1) : string.Empty;
                return Uri.UnescapeDataString(value.Replace("+", " "));
            }

            return null;
        }

        private void HandleCameraSwitchShortcut()
        {
            if (!enableHiddenCameraSwitch)
                return;

            if (Input.GetKeyDown(KeyCode.C))
            {
                CycleCameraDevice();
                return;
            }

            if (!TryGetPrimaryPointerDown(out Vector2 pointerPosition))
                return;

            if (!IsInTopRightSwitchArea(pointerPosition))
                return;

            float now = Time.unscaledTime;
            if (cornerTapCount == 0 || now - firstCornerTapTime > cornerTapWindowSeconds)
            {
                firstCornerTapTime = now;
                cornerTapCount = 0;
            }

            cornerTapCount++;
            if (cornerTapCount < cornerTapCountToSwitch)
                return;

            cornerTapCount = 0;
            CycleCameraDevice();
        }

        private void CycleCameraDevice()
        {
            WebCamDevice[] devices = WebCamTexture.devices;
            if (devices.Length <= 1)
            {
                Debug.LogWarning("[StoryMint] Cannot cycle camera: only one webcam device is available.");
                return;
            }

            int nextIndex = activeDeviceIndex >= 0
                ? (activeDeviceIndex + 1) % devices.Length
                : 0;

            Debug.Log("[StoryMint] Cycling webcam to device #" + nextIndex + " '" + devices[nextIndex].name + "'.");
            StartWebcamDevice(devices, nextIndex);
        }

        private bool IsInTopRightSwitchArea(Vector2 pointerPosition)
        {
            float minX = Screen.width * (1f - cornerTapAreaNormalized);
            float minY = Screen.height * (1f - cornerTapAreaNormalized);
            return pointerPosition.x >= minX && pointerPosition.y >= minY;
        }

        private static bool TryGetPrimaryPointerDown(out Vector2 pointerPosition)
        {
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    pointerPosition = touch.position;
                    return true;
                }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                pointerPosition = Input.mousePosition;
                return true;
            }

            pointerPosition = default;
            return false;
        }

        private void OnGUI()
        {
            if (!showDebugOverlay)
                return;

            GUIStyle style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(16, Screen.height / 54)
            };
            style.normal.textColor = Color.white;

            UnityEngine.Rect backgroundRect = new UnityEngine.Rect(12f, 252f, Mathf.Min(Screen.width - 24f, 620f), 120f);
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(backgroundRect, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new UnityEngine.Rect(22f, 260f, backgroundRect.width - 20f, backgroundRect.height - 12f),
                "Camera: " + cameraStatus + "\n" +
                "Screen: " + Screen.width + "x" + Screen.height +
                " displayRot=" + lastAppliedVideoRotation +
                " rawAspect=" + lastRawVideoAspect.ToString("0.###") +
                " visibleAspect=" + lastVisibleVideoAspect.ToString("0.###") +
                " screenAspect=" + lastScreenAspect.ToString("0.###") +
                (orientationSwapDetected ? " [ORIENT_FIX]" : ""), style);
        }

        // -------------------------------------------------------------------
        //  Background Rendering
        // -------------------------------------------------------------------

        private void UpdateBackgroundQuad()
        {
            if (webcamTexture.width < 100 || webcamTexture.height < 100)
                return;

            if (backgroundQuad == null)
                CreateBackgroundQuad();

            backgroundMaterial.mainTexture = webcamTexture;

            float rawVideoAspect = (float)webcamTexture.width / webcamTexture.height;
            float screenAspect = (float)Screen.width / Screen.height;
            int videoRot = GetEffectiveVideoRotationAngle();

            lastAppliedVideoRotation = videoRot;
            lastRawVideoAspect = rawVideoAspect;
            lastScreenAspect = screenAspect;

            // ---------------------------------------------------------------
            // Strategy: size the quad to exactly fill the camera frustum at
            // backgroundDistance, matching the screen dimensions. The webcam
            // texture stretches to fit. On mobile AR this is fine — any minor
            // aspect mismatch is invisible on a camera feed.
            //
            // We still apply videoRotationAngle, but ONLY when the rotation
            // wouldn't flip the orientation relative to the screen (which
            // would mean the browser already handled it).
            // ---------------------------------------------------------------

            float heightAtDist = 2f * backgroundDistance * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
            float widthAtDist = heightAtDist * screenAspect;

            // Detect if applying the reported rotation would flip the
            // orientation (landscape ↔ portrait). If so, the browser likely
            // already pre-rotated the pixels and we should skip the rotation.
            bool applyRotation = false;
            orientationSwapDetected = false;
            if (videoRot == 90 || videoRot == 270)
            {
                // After rotation, visible aspect would be inverted.
                float rotatedAspect = 1f / rawVideoAspect;
                bool rotatedMatchesScreen = (rotatedAspect > 1f) == (screenAspect > 1f) ||
                                            Mathf.Abs(rotatedAspect - screenAspect) < Mathf.Abs(rawVideoAspect - screenAspect);
                if (rotatedMatchesScreen)
                {
                    applyRotation = true; // rotation makes it match screen → apply
                }
                else
                {
                    applyRotation = false; // rotation would flip away from screen → skip
                    orientationSwapDetected = true;
                }
            }
            else if (videoRot == 180)
            {
                applyRotation = true;
            }

            // Check if raw video aspect already mismatches screen orientation
            // when no rotation is reported — this means browser pre-rotated
            // but the texture dimensions are still raw sensor.
            if (videoRot == 0 && autoFixWebGLOrientationMismatch)
            {
                bool videoIsLandscape = rawVideoAspect > 1.05f;
                bool screenIsLandscape = screenAspect > 1.05f;
                if (videoIsLandscape != screenIsLandscape)
                    orientationSwapDetected = true;
            }

            float scaleX = widthAtDist;
            float scaleY = heightAtDist;

            if (applyRotation && (videoRot == 90 || videoRot == 270))
            {
                // Quad needs to be rotated; swap its local axes so it still
                // fills the screen after rotation.
                scaleX = heightAtDist;
                scaleY = widthAtDist;
            }

            if (webcamTexture.videoVerticallyMirrored)
                scaleX = -scaleX;

            lastVisibleVideoAspect = applyRotation && (videoRot == 90 || videoRot == 270)
                ? 1f / rawVideoAspect
                : rawVideoAspect;

            backgroundQuad.transform.localScale = new Vector3(scaleX, scaleY, 1f);
            backgroundQuad.transform.localPosition = new Vector3(0f, 0f, backgroundDistance);
            backgroundQuad.transform.localEulerAngles = new Vector3(0f, 0f, applyRotation ? -videoRot : 0f);
        }

        private int GetEffectiveVideoRotationAngle()
        {
            if (webcamTexture == null || webcamTexture.width < 100 || webcamTexture.height < 100)
                return 0;

            return NormalizeRightAngle(webcamTexture.videoRotationAngle);
        }

        private void CreateBackgroundQuad()
        {
            backgroundQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            backgroundQuad.name = "StoryMintCameraBackground";
            backgroundQuad.transform.SetParent(transform, false);

            // Remove collider
            Collider col = backgroundQuad.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Material: simple unlit, renders first (low queue)
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");

            backgroundMaterial = new Material(shader)
            {
                name = "StoryMintCameraBgMat",
                renderQueue = (int)RenderQueue.Background + 1
            };

            // Disable depth write, no shadows
            if (backgroundMaterial.HasProperty("_ZWrite"))
                backgroundMaterial.SetFloat("_ZWrite", 0f);

            MeshRenderer renderer = backgroundQuad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = backgroundMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            isInitialized = true;
        }

        // -------------------------------------------------------------------
        //  Frame Processing
        // -------------------------------------------------------------------

        private void FeedToTracker()
        {
            if (tracker == null)
                return;

            int w = webcamTexture.width;
            int h = webcamTexture.height;

            if (w < 100 || h < 100)
                return;

            // Lazy-init pixel buffer and mats
            if (pixelBuffer == null || pixelBuffer.Length != w * h)
            {
                pixelBuffer = new Color32[w * h];
                rgbaBytes = new byte[w * h * 4];
                rgbaMat?.Dispose();
                grayMat?.Dispose();
                rgbaMat = new Mat(h, w, CvType.CV_8UC4);
                grayMat = new Mat(h, w, CvType.CV_8UC1);
            }

            // Copy pixels → byte[] → Mat
            webcamTexture.GetPixels32(pixelBuffer);
            for (int i = 0; i < pixelBuffer.Length; i++)
            {
                int bi = i * 4;
                rgbaBytes[bi] = pixelBuffer[i].r;
                rgbaBytes[bi + 1] = pixelBuffer[i].g;
                rgbaBytes[bi + 2] = pixelBuffer[i].b;
                rgbaBytes[bi + 3] = pixelBuffer[i].a;
            }
            rgbaMat.put(0, 0, rgbaBytes);

            // Convert to grayscale
            Imgproc.cvtColor(rgbaMat, grayMat, Imgproc.COLOR_RGBA2GRAY);

            Mat trackerFrame = PrepareTrackerFrame(grayMat);

            // Downscale for faster ORB processing
            trackerFrame = DownscaleForTracker(trackerFrame);

            // Feed to tracker
            tracker.ProcessFrame(trackerFrame);
        }

        private Mat PrepareTrackerFrame(Mat source)
        {
            // WebCamTexture pixels arrive bottom-up relative to OpenCV's row order.
            Core.flip(source, source, 0);

            // Mirror correction for front camera
            if (webcamTexture != null && webcamTexture.videoVerticallyMirrored)
                Core.flip(source, source, 1);

            int rotation = GetTrackerRotationDegrees();

            // If the browser already pre-rotated (orientationSwapDetected),
            // skip the rotation — the pixels are already in screen orientation.
            if (orientationSwapDetected)
                rotation = 0;

            if (rotation == 0)
                return source;

            EnsureTrackerGrayMat(source, rotation == 90 || rotation == 270);

            switch (rotation)
            {
                case 90:
                    Core.rotate(source, trackerGrayMat, Core.ROTATE_90_CLOCKWISE);
                    break;
                case 180:
                    Core.rotate(source, trackerGrayMat, Core.ROTATE_180);
                    break;
                case 270:
                    Core.rotate(source, trackerGrayMat, Core.ROTATE_90_COUNTERCLOCKWISE);
                    break;
                default:
                    return source;
            }

            return trackerGrayMat;
        }

        private void EnsureTrackerGrayMat(Mat source, bool swapDimensions)
        {
            int rows = swapDimensions ? source.cols() : source.rows();
            int cols = swapDimensions ? source.rows() : source.cols();
            if (trackerGrayMat != null && trackerGrayMat.rows() == rows && trackerGrayMat.cols() == cols)
                return;

            trackerGrayMat?.Dispose();
            trackerGrayMat = new Mat(rows, cols, CvType.CV_8UC1);
        }

        private Mat DownscaleForTracker(Mat source)
        {
            if (trackerMaxWidth <= 0 || source.cols() <= trackerMaxWidth)
                return source;

            float scale = (float)trackerMaxWidth / source.cols();
            int newW = trackerMaxWidth;
            int newH = Mathf.Max(1, Mathf.RoundToInt(source.rows() * scale));

            if (downscaledMat == null || downscaledMat.cols() != newW || downscaledMat.rows() != newH)
            {
                downscaledMat?.Dispose();
                downscaledMat = new Mat(newH, newW, source.type());
            }

            Imgproc.resize(source, downscaledMat, new OpenCVForUnity.CoreModule.Size(newW, newH),
                0, 0, Imgproc.INTER_AREA);
            return downscaledMat;
        }

        private int GetTrackerRotationDegrees()
        {
            return GetEffectiveVideoRotationAngle();
        }

        private static int NormalizeRightAngle(int degrees)
        {
            degrees %= 360;
            if (degrees < 0)
                degrees += 360;

            if (degrees < 45 || degrees >= 315)
                return 0;
            if (degrees < 135)
                return 90;
            if (degrees < 225)
                return 180;
            return 270;
        }
    }
}

#endif
