using UnityEngine;
using UnityEngine.SceneManagement;

namespace StoryMint.RiftHunt
{
    public static class RiftHuntBootstrap
    {
        private static bool isRegistered;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneBootstrap()
        {
            if (isRegistered)
                return;

            isRegistered = true;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode loadMode)
        {
            if (Object.FindObjectOfType<RiftHuntExperienceController>() != null)
                return;

            GameObject runtimeObject = new GameObject("StoryMintRiftHuntExperience");
            runtimeObject.AddComponent<RiftHuntExperienceController>();
        }
    }
}
