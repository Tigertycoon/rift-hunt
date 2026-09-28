using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace StoryMint.RiftHunt
{
    [DisallowMultipleComponent]
    public class StoryMintLocationService : MonoBehaviour
    {
        private const string ServiceObjectName = "StoryMintLocationService";

        [SerializeField] private bool useHighAccuracy = true;
        [SerializeField] private int timeoutMilliseconds = 12000;
        [SerializeField] private int maximumAgeMilliseconds = 5000;
        [SerializeField] private bool dontDestroyOnLoad = true;
        [SerializeField] private bool useMockOutsideWebGl = true;
        [SerializeField] private StoryMintGeoCoordinate mockCoordinate = new StoryMintGeoCoordinate(52.520008d, 13.404954d);
        [SerializeField] private float mockAccuracyMeters = 12f;

        private bool isWatching;

        public static StoryMintLocationService Instance { get; private set; }

        public StoryMintLocationStatus Status { get; private set; }
        public StoryMintLocationPermissionState PermissionState { get; private set; }
        public StoryMintLocationFix LastFix { get; private set; }
        public bool HasFix { get; private set; }
        public string LastError { get; private set; }

        public event Action<StoryMintLocationFix> LocationUpdated;
        public event Action<StoryMintLocationStatus, string> StatusChanged;

        public static StoryMintLocationService GetOrCreate()
        {
            StoryMintLocationService service = Instance != null
                ? Instance
                : Object.FindObjectOfType<StoryMintLocationService>();

            if (service != null)
                return service;

            GameObject serviceObject = new GameObject(ServiceObjectName);
            return serviceObject.AddComponent<StoryMintLocationService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            gameObject.name = ServiceObjectName;
            PermissionState = ParsePermissionState(StoryMintWebGlGeolocationBridge.GetCachedPermissionState());
            Status = StoryMintLocationStatus.Idle;

            if (dontDestroyOnLoad)
                DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            StopWatching();
        }

        public void RequestSingleUpdate()
        {
            LastError = null;

            if (ShouldUseMockLocation())
            {
                ApplyMockFix();
                return;
            }

            if (!StoryMintWebGlGeolocationBridge.IsSupported())
            {
                PermissionState = StoryMintLocationPermissionState.Unsupported;
                SetStatus(StoryMintLocationStatus.Unavailable, "Browser-Geolocation ist nicht verfuegbar.");
                return;
            }

            PermissionState = ParsePermissionState(StoryMintWebGlGeolocationBridge.GetCachedPermissionState());
            SetStatus(StoryMintLocationStatus.RequestingPermission, "Standortfreigabe wird angefragt.");
            StoryMintWebGlGeolocationBridge.RequestPosition(gameObject.name, useHighAccuracy, timeoutMilliseconds, maximumAgeMilliseconds);
        }

        public void StartWatching()
        {
            LastError = null;

            if (ShouldUseMockLocation())
            {
                isWatching = true;
                ApplyMockFix();
                return;
            }

            if (!StoryMintWebGlGeolocationBridge.IsSupported())
            {
                PermissionState = StoryMintLocationPermissionState.Unsupported;
                SetStatus(StoryMintLocationStatus.Unavailable, "Browser-Geolocation ist nicht verfuegbar.");
                return;
            }

            PermissionState = ParsePermissionState(StoryMintWebGlGeolocationBridge.GetCachedPermissionState());
            SetStatus(StoryMintLocationStatus.RequestingPermission, "Standortfreigabe wird angefragt.");

            int watchId = StoryMintWebGlGeolocationBridge.StartWatch(gameObject.name, useHighAccuracy, timeoutMilliseconds, maximumAgeMilliseconds);
            isWatching = watchId >= 0;
            if (!isWatching)
                SetStatus(StoryMintLocationStatus.Error, "GPS-Watch konnte nicht gestartet werden.");
        }

        public void StopWatching()
        {
            if (!isWatching)
                return;

            StoryMintWebGlGeolocationBridge.StopWatch();
            isWatching = false;

            if (Status == StoryMintLocationStatus.Watching || Status == StoryMintLocationStatus.RequestingPermission)
                SetStatus(HasFix ? StoryMintLocationStatus.Ready : StoryMintLocationStatus.Idle, string.Empty);
        }

        public void SetMockLocation(double latitude, double longitude, float accuracyMeters)
        {
            mockCoordinate = new StoryMintGeoCoordinate(latitude, longitude);
            mockAccuracyMeters = Mathf.Max(0f, accuracyMeters);
            ApplyMockFix();
        }

        public StoryMintGeofenceResult EvaluateGeofence(StoryMintGeoCoordinate target, float radiusMeters, float requiredAccuracyMeters)
        {
            return StoryMintGeoMath.EvaluateGeofence(LastFix, HasFix, target, radiusMeters, requiredAccuracyMeters);
        }

        public void OnWebGlLocationUpdate(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
                return;

            WebGlLocationPayload fixPayload = JsonUtility.FromJson<WebGlLocationPayload>(payload);
            if (fixPayload == null)
            {
                SetStatus(StoryMintLocationStatus.Error, "GPS-Daten konnten nicht gelesen werden.");
                return;
            }

            LastFix = new StoryMintLocationFix
            {
                coordinate = new StoryMintGeoCoordinate(fixPayload.latitude, fixPayload.longitude),
                accuracyMeters = Mathf.Max(0f, fixPayload.accuracy),
                hasAltitude = fixPayload.hasAltitude,
                altitudeMeters = fixPayload.altitude,
                hasHeading = fixPayload.hasHeading,
                headingDegrees = fixPayload.heading,
                hasSpeed = fixPayload.hasSpeed,
                speedMetersPerSecond = fixPayload.speed,
                timestampSeconds = fixPayload.timestamp,
                isMock = false
            };

            HasFix = LastFix.IsUsable;
            PermissionState = StoryMintLocationPermissionState.Granted;
            LastError = null;
            SetStatus(isWatching ? StoryMintLocationStatus.Watching : StoryMintLocationStatus.Ready, string.Empty);
            LocationUpdated?.Invoke(LastFix);
        }

        public void OnWebGlLocationError(string payload)
        {
            WebGlLocationErrorPayload errorPayload = string.IsNullOrWhiteSpace(payload)
                ? null
                : JsonUtility.FromJson<WebGlLocationErrorPayload>(payload);

            int code = errorPayload != null ? errorPayload.code : 0;
            string message = errorPayload != null ? errorPayload.message : "GPS-Fehler.";
            LastError = message;

            if (errorPayload != null && !string.IsNullOrWhiteSpace(errorPayload.permissionState))
                PermissionState = ParsePermissionState(errorPayload.permissionState);

            switch (code)
            {
                case 1:
                    PermissionState = StoryMintLocationPermissionState.Denied;
                    SetStatus(StoryMintLocationStatus.PermissionDenied, "Standortfreigabe wurde abgelehnt.");
                    break;
                case 2:
                    SetStatus(StoryMintLocationStatus.Unavailable, message);
                    break;
                case 3:
                    SetStatus(StoryMintLocationStatus.Timeout, "GPS hat nicht rechtzeitig geantwortet.");
                    break;
                default:
                    SetStatus(StoryMintLocationStatus.Error, message);
                    break;
            }
        }

        public void OnWebGlPermissionState(string state)
        {
            PermissionState = ParsePermissionState(state);
        }

        private bool ShouldUseMockLocation()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return useMockOutsideWebGl;
#endif
        }

        private void ApplyMockFix()
        {
            LastFix = new StoryMintLocationFix
            {
                coordinate = mockCoordinate,
                accuracyMeters = Mathf.Max(0f, mockAccuracyMeters),
                timestampSeconds = Time.realtimeSinceStartupAsDouble,
                isMock = true
            };

            HasFix = LastFix.IsUsable;
            PermissionState = StoryMintLocationPermissionState.Granted;
            LastError = null;
            SetStatus(StoryMintLocationStatus.Mock, "Editor-Mock-Standort aktiv.");
            LocationUpdated?.Invoke(LastFix);
        }

        private void SetStatus(StoryMintLocationStatus status, string message)
        {
            Status = status;
            StatusChanged?.Invoke(status, message);
        }

        private static StoryMintLocationPermissionState ParsePermissionState(string state)
        {
            switch ((state ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "prompt":
                    return StoryMintLocationPermissionState.Prompt;
                case "granted":
                    return StoryMintLocationPermissionState.Granted;
                case "denied":
                    return StoryMintLocationPermissionState.Denied;
                case "unsupported":
                    return StoryMintLocationPermissionState.Unsupported;
                default:
                    return StoryMintLocationPermissionState.Unknown;
            }
        }

        [Serializable]
        private class WebGlLocationPayload
        {
            public double latitude;
            public double longitude;
            public float accuracy;
            public bool hasAltitude;
            public double altitude;
            public bool hasHeading;
            public double heading;
            public bool hasSpeed;
            public double speed;
            public double timestamp;
        }

        [Serializable]
        private class WebGlLocationErrorPayload
        {
            public int code;
            public string message;
            public string permissionState;
        }
    }
}
