using System.Runtime.InteropServices;

namespace StoryMint.RiftHunt
{
    internal static class StoryMintWebGlGeolocationBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int StoryMintGeoIsSupported();
        [DllImport("__Internal")] private static extern string StoryMintGeoGetCachedPermissionState();
        [DllImport("__Internal")] private static extern void StoryMintGeoRequestPosition(string callbackObjectName, int highAccuracy, int timeoutMs, int maximumAgeMs);
        [DllImport("__Internal")] private static extern int StoryMintGeoStartWatch(string callbackObjectName, int highAccuracy, int timeoutMs, int maximumAgeMs);
        [DllImport("__Internal")] private static extern void StoryMintGeoStopWatch();
#endif

        public static bool IsSupported()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return StoryMintGeoIsSupported() != 0;
#else
            return false;
#endif
        }

        public static string GetCachedPermissionState()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return StoryMintGeoGetCachedPermissionState();
#else
            return "mock";
#endif
        }

        public static void RequestPosition(string callbackObjectName, bool highAccuracy, int timeoutMs, int maximumAgeMs)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            StoryMintGeoRequestPosition(callbackObjectName, highAccuracy ? 1 : 0, timeoutMs, maximumAgeMs);
#endif
        }

        public static int StartWatch(string callbackObjectName, bool highAccuracy, int timeoutMs, int maximumAgeMs)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return StoryMintGeoStartWatch(callbackObjectName, highAccuracy ? 1 : 0, timeoutMs, maximumAgeMs);
#else
            return -1;
#endif
        }

        public static void StopWatch()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            StoryMintGeoStopWatch();
#endif
        }
    }
}
