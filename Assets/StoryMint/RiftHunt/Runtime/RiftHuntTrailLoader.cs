using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace StoryMint.RiftHunt
{
    public static class RiftHuntTrailLoader
    {
        public const string ActiveTrailRelativePath = "StoryMint/rift-hunt-preview.json";
        public const string GpsProbeTrailRelativePath = "StoryMint/rift-hunt-gps-sample.json";

        public static IEnumerator LoadActiveTrail(Action<RiftHuntTrailDefinition> onLoaded, Action<string> onError)
        {
            string fullPath = Path.Combine(Application.streamingAssetsPath, ResolveActiveTrailRelativePath());
            string requestUrl = fullPath.Replace("\\", "/");

            if (!requestUrl.Contains("://"))
                requestUrl = "file://" + requestUrl;

            using (UnityWebRequest request = UnityWebRequest.Get(requestUrl))
            {
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke("Failed to load trail definition from '" + requestUrl + "'. " + request.error);
                    yield break;
                }

                RiftHuntTrailDefinition trailDefinition = JsonUtility.FromJson<RiftHuntTrailDefinition>(request.downloadHandler.text);
                if (trailDefinition == null || trailDefinition.steps == null || trailDefinition.steps.Count == 0)
                {
                    onError?.Invoke("Trail definition is empty or malformed.");
                    yield break;
                }

                onLoaded?.Invoke(trailDefinition);
            }
        }

        private static string ResolveActiveTrailRelativePath()
        {
            string requestedTrail = GetQueryParameter(Application.absoluteURL, "trail");
            if (string.IsNullOrWhiteSpace(requestedTrail))
                return ActiveTrailRelativePath;

            requestedTrail = requestedTrail.Trim();
            if (string.Equals(requestedTrail, "gps", StringComparison.OrdinalIgnoreCase))
                return GpsProbeTrailRelativePath;

            if (requestedTrail.IndexOf("..", StringComparison.Ordinal) >= 0 || requestedTrail.Contains("/") || requestedTrail.Contains("\\"))
                return ActiveTrailRelativePath;

            if (!requestedTrail.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                requestedTrail += ".json";

            return Path.Combine("StoryMint", requestedTrail);
        }

        private static string GetQueryParameter(string url, string key)
        {
            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key))
                return null;

            int queryStart = url.IndexOf('?');
            if (queryStart < 0 || queryStart >= url.Length - 1)
                return null;

            int fragmentStart = url.IndexOf('#', queryStart);
            string query = fragmentStart >= 0
                ? url.Substring(queryStart + 1, fragmentStart - queryStart - 1)
                : url.Substring(queryStart + 1);

            string[] pairs = query.Split('&');
            for (int index = 0; index < pairs.Length; index++)
            {
                string pair = pairs[index];
                int equalsIndex = pair.IndexOf('=');
                string pairKey = equalsIndex >= 0 ? pair.Substring(0, equalsIndex) : pair;
                if (!string.Equals(Uri.UnescapeDataString(pairKey), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = equalsIndex >= 0 ? pair.Substring(equalsIndex + 1) : string.Empty;
                return Uri.UnescapeDataString(value.Replace("+", " "));
            }

            return null;
        }
    }
}
