#if STORYMINT_OPENCV
using System;
using System.Collections.Generic;
using OpenCVForUnity.Calib3dModule;
using OpenCVForUnity.CoreModule;
using OpenCVForUnity.Features2dModule;
using OpenCVForUnity.ImgcodecsModule;
using OpenCVForUnity.ImgprocModule;
using UnityEngine;
using UnityEngine.Events;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Detects and tracks a reference image in a webcam feed using OpenCV.
    /// Outputs pose (position + rotation) that can drive a 3D transform.
    ///
    /// Pipeline:
    ///   WebCamTexture → Mat (grayscale) → ORB detect+compute → BFMatcher knnMatch
    ///   → ratio test → findHomography → decompose to corners → solvePnP → pose
    /// </summary>
    public sealed class OpenCVImageTracker : MonoBehaviour
    {
        [Header("Tracking Settings")]
        [Tooltip("Minimum good matches required to consider the target found.")]
        [SerializeField] private int minMatchCount = 8;

        [Tooltip("Lowe's ratio test threshold. Lower = stricter.")]
        [SerializeField, Range(0.5f, 0.9f)] private float ratioThreshold = 0.75f;

        [Tooltip("RANSAC reprojection threshold for findHomography.")]
        [SerializeField] private double ransacThreshold = 5.0;

        [Tooltip("Maximum ORB features to detect per frame.")]
        [SerializeField] private int maxFeatures = 1000;

        [Tooltip("Process every Nth frame to save CPU (1 = every frame).")]
        [SerializeField, Range(1, 6)] private int processEveryNFrames = 1;

        [Tooltip("Show a small tracking diagnostics overlay while tuning the WebGL camera build.")]
        [SerializeField] private bool allowDebugOverlay = true;

        [Header("Reference Image")]
        [Tooltip("The target image to detect. Assign in inspector or call SetReferenceImage().")]
        [SerializeField] private Texture2D referenceImage;

        [Tooltip("Physical width of the printed target in meters (for solvePnP).")]
        [SerializeField] private float targetWidthMeters = 0.15f;

        [Header("Events")]
        public UnityEvent<string> OnTargetFound = new UnityEvent<string>();
        public UnityEvent<string> OnTargetLost = new UnityEvent<string>();
        public UnityEvent<Vector3, Quaternion> OnPoseUpdated = new UnityEvent<Vector3, Quaternion>();

        // Target descriptor state
        private Mat referenceGray;
        private MatOfKeyPoint referenceKeypoints;
        private Mat referenceDescriptors;
        private ORB detector;
        private BFMatcher matcher;

        // 3D object points for solvePnP (corners of the reference image)
        private MatOfPoint3f objectPoints3D;

        // Tracking state
        private bool isTracking;
        private int frameCounter;
        private int framesLost;
        private const int LostFrameThreshold = 20;

        // Smoothing
        private Vector3 smoothedPosition;
        private Quaternion smoothedRotation;
        private bool hasSmoothedPose;

        // Reusable mats to avoid per-frame GC
        private Mat sceneGray;
        private MatOfKeyPoint sceneKeypoints;
        private Mat sceneDescriptors;
        private List<MatOfDMatch> knnMatches;

        private string targetId = "default";
        private bool debugOverlayEnabled;
        private GUIStyle debugStyle;
        private int lastFrameWidth;
        private int lastFrameHeight;
        private int lastReferenceKeypoints;
        private int lastSceneKeypoints;
        private int lastGoodMatches;
        private string lastTrackingStage = "Waiting for frame";

        // -------------------------------------------------------------------
        //  Public API
        // -------------------------------------------------------------------

        /// <summary>
        /// Set or change the reference image at runtime.
        /// </summary>
        public void SetReferenceImage(Texture2D image, string id = "default", float widthMeters = 0.15f)
        {
            referenceImage = image;
            targetId = id;
            targetWidthMeters = widthMeters;
            PrepareReferenceDescriptors();
        }

        /// <summary>
        /// Call this every frame with the current camera frame as a grayscale Mat.
        /// The tracker will decide whether to process based on processEveryNFrames.
        /// </summary>
        public void ProcessFrame(Mat grayFrame)
        {
            if (referenceDescriptors == null || referenceDescriptors.empty())
            {
                lastTrackingStage = "No reference descriptors";
                return;
            }

            frameCounter++;
            if (frameCounter % processEveryNFrames != 0)
                return;

            DetectAndTrack(grayFrame);
        }

        /// <summary>
        /// Whether the target is currently being tracked.
        /// </summary>
        public bool IsTracked => isTracking;

        /// <summary>
        /// Smoothed world-space position of the target center.
        /// Only valid when IsTracked is true.
        /// </summary>
        public Vector3 TrackedPosition => smoothedPosition;

        /// <summary>
        /// Smoothed world-space rotation of the target.
        /// Only valid when IsTracked is true.
        /// </summary>
        public Quaternion TrackedRotation => smoothedRotation;

        // -------------------------------------------------------------------
        //  Unity Lifecycle
        // -------------------------------------------------------------------

        private void Awake()
        {
            debugOverlayEnabled = allowDebugOverlay;
            InitializeDetector();

            if (referenceImage != null)
                PrepareReferenceDescriptors();
        }

        private void OnDestroy()
        {
            referenceGray?.Dispose();
            referenceKeypoints?.Dispose();
            referenceDescriptors?.Dispose();
            sceneGray?.Dispose();
            sceneKeypoints?.Dispose();
            sceneDescriptors?.Dispose();
            detector?.Dispose();
            matcher?.Dispose();
            objectPoints3D?.Dispose();

            if (knnMatches != null)
            {
                for (int i = 0; i < knnMatches.Count; i++)
                    knnMatches[i]?.Dispose();
            }
        }

        private void OnGUI()
        {
            if (!debugOverlayEnabled)
                return;

            if (debugStyle == null)
            {
                debugStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(18, Screen.height / 48)
                };
                debugStyle.normal.textColor = Color.white;
            }

            UnityEngine.Rect backgroundRect = new UnityEngine.Rect(12f, 88f, Mathf.Min(Screen.width - 24f, 560f), 156f);
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(backgroundRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            string label =
                "StoryMint OpenCV\n" +
                "Stage: " + lastTrackingStage + "\n" +
                "Frame: " + lastFrameWidth + "x" + lastFrameHeight + "\n" +
                "Ref KP: " + lastReferenceKeypoints +
                " | Scene KP: " + lastSceneKeypoints +
                " | Good: " + lastGoodMatches +
                " | Need: " + minMatchCount;
            GUI.Label(new UnityEngine.Rect(22f, 96f, backgroundRect.width - 20f, backgroundRect.height - 12f), label, debugStyle);
        }

        // -------------------------------------------------------------------
        //  Initialization
        // -------------------------------------------------------------------

        private void InitializeDetector()
        {
            detector = ORB.create(maxFeatures);
            matcher = new BFMatcher(Core.NORM_HAMMING, false);
            sceneGray = new Mat();
            sceneKeypoints = new MatOfKeyPoint();
            sceneDescriptors = new Mat();
            knnMatches = new List<MatOfDMatch>();
        }

        private void PrepareReferenceDescriptors()
        {
            if (referenceImage == null)
            {
                Debug.LogWarning("[StoryMint] OpenCVImageTracker: No reference image assigned.");
                return;
            }

            // Convert reference to grayscale Mat
            referenceGray?.Dispose();
            referenceKeypoints?.Dispose();
            referenceDescriptors?.Dispose();

            Texture2D readable = MakeReadable(referenceImage);
            Color32[] pixels = readable.GetPixels32();
            int w = readable.width;
            int h = readable.height;

            referenceGray = new Mat(h, w, CvType.CV_8UC1);
            byte[] grayBytes = new byte[w * h];
            for (int i = 0; i < pixels.Length; i++)
            {
                // BT.601 luminance
                grayBytes[i] = (byte)(0.299f * pixels[i].r + 0.587f * pixels[i].g + 0.114f * pixels[i].b);
            }
            referenceGray.put(0, 0, grayBytes);

            // Flip vertically (Unity textures are bottom-up)
            Core.flip(referenceGray, referenceGray, 0);

            // Detect features
            referenceKeypoints = new MatOfKeyPoint();
            referenceDescriptors = new Mat();
            detector.detectAndCompute(referenceGray, new Mat(), referenceKeypoints, referenceDescriptors);

            Debug.Log("[StoryMint] Reference image prepared: " + referenceKeypoints.rows() + " keypoints, " +
                      w + "x" + h + " px");
            lastReferenceKeypoints = referenceKeypoints.rows();

            // 3D object points: corners of the physical target (in meters)
            float halfW = targetWidthMeters * 0.5f;
            float halfH = halfW * ((float)h / w);
            objectPoints3D?.Dispose();
            objectPoints3D = new MatOfPoint3f(
                new Point3(-halfW, -halfH, 0),
                new Point3(halfW, -halfH, 0),
                new Point3(halfW, halfH, 0),
                new Point3(-halfW, halfH, 0));

            if (readable != referenceImage)
            {
                if (Application.isPlaying) Destroy(readable);
                else DestroyImmediate(readable);
            }
        }

        // -------------------------------------------------------------------
        //  Per-Frame Detection
        // -------------------------------------------------------------------

        private void DetectAndTrack(Mat grayFrame)
        {
            lastFrameWidth = grayFrame.cols();
            lastFrameHeight = grayFrame.rows();
            lastSceneKeypoints = 0;
            lastGoodMatches = 0;
            lastTrackingStage = "Detecting";

            // Clear previous matches
            for (int i = 0; i < knnMatches.Count; i++)
                knnMatches[i]?.Dispose();
            knnMatches.Clear();

            // Detect features in scene
            sceneKeypoints.fromArray(new KeyPoint[0]);
            sceneDescriptors.release();
            detector.detectAndCompute(grayFrame, new Mat(), sceneKeypoints, sceneDescriptors);
            lastSceneKeypoints = sceneKeypoints.rows();

            if (sceneDescriptors.empty() || sceneDescriptors.rows() < minMatchCount)
            {
                lastTrackingStage = "Not enough scene descriptors";
                HandleLostFrame();
                return;
            }

            // KNN match
            matcher.knnMatch(referenceDescriptors, sceneDescriptors, knnMatches, 2);

            // Ratio test
            List<DMatch> goodMatches = new List<DMatch>();
            for (int i = 0; i < knnMatches.Count; i++)
            {
                if (knnMatches[i].rows() < 2) continue;
                DMatch[] pair = knnMatches[i].toArray();
                if (pair[0].distance < ratioThreshold * pair[1].distance)
                    goodMatches.Add(pair[0]);
            }
            lastGoodMatches = goodMatches.Count;

            if (goodMatches.Count < minMatchCount)
            {
                lastTrackingStage = "Not enough good matches";
                HandleLostFrame();
                return;
            }

            // Build point correspondences
            List<KeyPoint> refKPList = referenceKeypoints.toList();
            List<KeyPoint> sceneKPList = sceneKeypoints.toList();
            List<Point> objPts = new List<Point>(goodMatches.Count);
            List<Point> scenePts = new List<Point>(goodMatches.Count);

            for (int i = 0; i < goodMatches.Count; i++)
            {
                objPts.Add(refKPList[goodMatches[i].queryIdx].pt);
                scenePts.Add(sceneKPList[goodMatches[i].trainIdx].pt);
            }

            // Find homography
            MatOfPoint2f objMat = new MatOfPoint2f(objPts.ToArray());
            MatOfPoint2f sceneMat = new MatOfPoint2f(scenePts.ToArray());

            Mat homography;
            try
            {
                homography = Calib3d.findHomography(objMat, sceneMat, Calib3d.RANSAC, ransacThreshold);
            }
            catch (Exception)
            {
                objMat.Dispose();
                sceneMat.Dispose();
                lastTrackingStage = "Homography exception";
                HandleLostFrame();
                return;
            }

            objMat.Dispose();
            sceneMat.Dispose();

            if (homography == null || homography.empty() || homography.rows() < 3)
            {
                homography?.Dispose();
                lastTrackingStage = "Homography failed";
                HandleLostFrame();
                return;
            }

            // Get corners in scene image
            int refW = referenceGray.cols();
            int refH = referenceGray.rows();
            MatOfPoint2f refCorners = new MatOfPoint2f(
                new Point(0, 0),
                new Point(refW, 0),
                new Point(refW, refH),
                new Point(0, refH));
            MatOfPoint2f sceneCorners = new MatOfPoint2f();

            Core.perspectiveTransform(refCorners, sceneCorners, homography);
            homography.Dispose();
            refCorners.Dispose();

            Point[] corners = sceneCorners.toArray();
            sceneCorners.Dispose();

            // Validate: reject degenerate homography (too small, too skewed)
            if (!IsValidQuad(corners, grayFrame.cols(), grayFrame.rows()))
            {
                lastTrackingStage = "Rejected quad";
                HandleLostFrame();
                return;
            }

            // SolvePnP for 3D pose
            ComputePose(corners, grayFrame.cols(), grayFrame.rows());
            lastTrackingStage = "Tracked";

            // Mark as found
            framesLost = 0;
            if (!isTracking)
            {
                isTracking = true;
                OnTargetFound?.Invoke(targetId);
            }
        }

        private void ComputePose(Point[] imageCorners, int frameWidth, int frameHeight)
        {
            // Simple camera intrinsics estimate (no proper calibration)
            double focalLength = frameWidth;
            double cx = frameWidth * 0.5;
            double cy = frameHeight * 0.5;

            Mat cameraMatrix = new Mat(3, 3, CvType.CV_64F);
            cameraMatrix.put(0, 0,
                focalLength, 0, cx,
                0, focalLength, cy,
                0, 0, 1);
            MatOfDouble distCoeffs = new MatOfDouble(0, 0, 0, 0, 0);

            MatOfPoint2f imagePoints = new MatOfPoint2f(imageCorners);
            Mat rvec = new Mat();
            Mat tvec = new Mat();

            try
            {
                Calib3d.solvePnP(objectPoints3D, imagePoints, cameraMatrix, distCoeffs, rvec, tvec);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[StoryMint] solvePnP failed: " + e.Message);
                imagePoints.Dispose();
                cameraMatrix.Dispose();
                distCoeffs.Dispose();
                rvec.Dispose();
                tvec.Dispose();
                return;
            }

            // Convert to Unity coordinates
            double[] tvecData = new double[3];
            tvec.get(0, 0, tvecData);
            Vector3 position = new Vector3((float)tvecData[0], -(float)tvecData[1], (float)tvecData[2]);

            // Convert Rodrigues rotation to Unity quaternion
            Mat rotMat = new Mat();
            Calib3d.Rodrigues(rvec, rotMat);
            Quaternion rotation = RotationMatToQuaternion(rotMat);

            // Smooth (lower = more stable but laggier; 0.15 is good for AR)
            float smoothFactor = hasSmoothedPose ? 0.15f : 1f;
            smoothedPosition = hasSmoothedPose
                ? Vector3.Lerp(smoothedPosition, position, smoothFactor)
                : position;
            smoothedRotation = hasSmoothedPose
                ? Quaternion.Slerp(smoothedRotation, rotation, smoothFactor)
                : rotation;
            hasSmoothedPose = true;

            OnPoseUpdated?.Invoke(smoothedPosition, smoothedRotation);

            imagePoints.Dispose();
            cameraMatrix.Dispose();
            distCoeffs.Dispose();
            rvec.Dispose();
            tvec.Dispose();
            rotMat.Dispose();
        }

        // -------------------------------------------------------------------
        //  Helpers
        // -------------------------------------------------------------------

        private void HandleLostFrame()
        {
            framesLost++;
            if (framesLost >= LostFrameThreshold && isTracking)
            {
                isTracking = false;
                hasSmoothedPose = false;
                OnTargetLost?.Invoke(targetId);
            }
        }

        private static bool IsValidQuad(Point[] corners, int frameWidth, int frameHeight)
        {
            if (corners == null || corners.Length != 4)
                return false;

            // Check area (reject too small or negative area)
            double area = Math.Abs(
                (corners[0].x * (corners[1].y - corners[3].y) +
                 corners[1].x * (corners[2].y - corners[0].y) +
                 corners[2].x * (corners[3].y - corners[1].y) +
                 corners[3].x * (corners[0].y - corners[2].y)) * 0.5);

            double frameArea = frameWidth * frameHeight;
            if (area < frameArea * 0.0005 || area > frameArea * 0.98)
                return false;

            // Check that corners are within extended frame bounds
            double margin = Math.Max(frameWidth, frameHeight) * 0.5;
            for (int i = 0; i < 4; i++)
            {
                if (corners[i].x < -margin || corners[i].x > frameWidth + margin ||
                    corners[i].y < -margin || corners[i].y > frameHeight + margin)
                    return false;
            }

            return true;
        }

        private static Quaternion RotationMatToQuaternion(Mat rotMat)
        {
            // OpenCV rotation matrix → Unity quaternion
            // OpenCV: right-handed, Y-down, Z-forward
            // Unity:  left-handed, Y-up, Z-forward
            double[] m = new double[9];
            rotMat.get(0, 0, m);

            // Convert to Unity coordinate system
            Matrix4x4 mat = Matrix4x4.identity;
            mat.m00 = (float)m[0]; mat.m01 = -(float)m[1]; mat.m02 = (float)m[2];
            mat.m10 = -(float)m[3]; mat.m11 = (float)m[4]; mat.m12 = -(float)m[5];
            mat.m20 = (float)m[6]; mat.m21 = -(float)m[7]; mat.m22 = (float)m[8];

            Quaternion q = mat.rotation;
            return q;
        }

        private static Texture2D MakeReadable(Texture2D source)
        {
            if (source.isReadable)
                return source;

            RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(source, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new UnityEngine.Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return readable;
        }
    }
}

#endif
