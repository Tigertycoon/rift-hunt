#if STORYMINT_IMAGINE
#if UNITY_EDITOR
using UnityEditor;

namespace StoryMint.RiftHunt.Editor
{
    [InitializeOnLoad]
    public static class StoryMintImagineTargetResyncOnce
    {
        private const string EditorPrefKey = "StoryMintImagineTargetResyncOnce.v20260614_target_import_aspect";

        static StoryMintImagineTargetResyncOnce()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            if (EditorPrefs.GetBool(EditorPrefKey, false))
                return;

            EditorPrefs.SetBool(EditorPrefKey, true);
            StoryMintImagineTargetRegistrar.SyncTestTargets();
        }
    }
}
#endif

#endif
