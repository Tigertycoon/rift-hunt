using System.Runtime.InteropServices;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    public static class StoryMintBrowserNavigation
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern void StoryMintNavigate_To(string url);
        [DllImport("__Internal")] private static extern void StoryMintNavigate_ReplaceAddress(string url);
#else
        private static void StoryMintNavigate_To(string url) => Application.OpenURL(url);
        private static void StoryMintNavigate_ReplaceAddress(string url) { }
#endif

        public static void NavigateTo(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            StoryMintNavigate_To(url);
        }

        public static void ReplaceCurrentAddress(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;

            StoryMintNavigate_ReplaceAddress(url);
        }
    }
}
