using System.Runtime.InteropServices;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    internal static class StoryMintWebGlClipboardBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern int StoryMintClipboardCopy(string text);
#endif

        public static bool Copy(string text)
        {
            string safeText = text ?? string.Empty;

#if UNITY_WEBGL && !UNITY_EDITOR
            return StoryMintClipboardCopy(safeText) != 0;
#else
            GUIUtility.systemCopyBuffer = safeText;
            return true;
#endif
        }
    }
}
