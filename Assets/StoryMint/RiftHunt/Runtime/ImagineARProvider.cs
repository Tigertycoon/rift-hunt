#if STORYMINT_IMAGINE
using System;
using Imagine.WebAR;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Wraps Imagine WebAR's <see cref="ImageTracker"/> behind
    /// <see cref="IRiftHuntARProvider"/> so the experience controller
    /// doesn't depend on Imagine types directly.
    /// </summary>
    public sealed class ImagineARProvider : IRiftHuntARProvider
    {
        private readonly ImageTracker tracker;

        public ImagineARProvider(ImageTracker tracker)
        {
            this.tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
            tracker.ImageFound += OnImageFound;
            tracker.ImageLost += OnImageLost;
        }

        // -------------------------------------------------------------------
        //  IRiftHuntARProvider
        // -------------------------------------------------------------------

        public event Action<string> ImageFound;
        public event Action<string> ImageLost;

        public Transform GetTargetTransform(string targetId)
        {
            return tracker.GetTargetTransform(targetId);
        }

        public bool IsImageTracked(string targetId)
        {
            return tracker.IsImageTracked(targetId);
        }

        // -------------------------------------------------------------------
        //  Lifecycle
        // -------------------------------------------------------------------

        /// <summary>
        /// Call when the provider is no longer needed to unsubscribe from
        /// the underlying Imagine tracker.
        /// </summary>
        public void Dispose()
        {
            if (tracker != null)
            {
                tracker.ImageFound -= OnImageFound;
                tracker.ImageLost -= OnImageLost;
            }
        }

        private void OnImageFound(string id) => ImageFound?.Invoke(id);
        private void OnImageLost(string id) => ImageLost?.Invoke(id);
    }
}

#endif
