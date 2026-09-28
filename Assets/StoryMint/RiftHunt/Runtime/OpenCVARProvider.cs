#if STORYMINT_OPENCV
using System;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Wraps <see cref="OpenCVARSceneSetup"/> behind
    /// <see cref="IRiftHuntARProvider"/> so the experience controller
    /// doesn't depend on the OpenCV types directly.
    /// </summary>
    public sealed class OpenCVARProvider : IRiftHuntARProvider
    {
        private readonly OpenCVARSceneSetup setup;

        public OpenCVARProvider(OpenCVARSceneSetup setup)
        {
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
            setup.ImageFound += OnImageFound;
            setup.ImageLost += OnImageLost;
        }

        // -------------------------------------------------------------------
        //  IRiftHuntARProvider
        // -------------------------------------------------------------------

        public event Action<string> ImageFound;
        public event Action<string> ImageLost;

        public Transform GetTargetTransform(string targetId)
        {
            return setup.GetTargetTransform(targetId);
        }

        public bool IsImageTracked(string targetId)
        {
            return setup.IsImageTracked(targetId);
        }

        // -------------------------------------------------------------------
        //  Lifecycle
        // -------------------------------------------------------------------

        /// <summary>
        /// Call when the provider is no longer needed to unsubscribe from
        /// the underlying OpenCVARSceneSetup.
        /// </summary>
        public void Dispose()
        {
            if (setup != null)
            {
                setup.ImageFound -= OnImageFound;
                setup.ImageLost -= OnImageLost;
            }
        }

        private void OnImageFound(string id) => ImageFound?.Invoke(id);
        private void OnImageLost(string id) => ImageLost?.Invoke(id);
    }
}

#endif
