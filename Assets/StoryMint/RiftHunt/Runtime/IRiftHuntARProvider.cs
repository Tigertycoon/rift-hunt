using System;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Abstraction over AR tracking backends (Imagine WebAR, OpenCV, etc.).
    /// The RiftHuntExperienceController works exclusively through this interface,
    /// making it easy to swap backends per scene.
    /// </summary>
    public interface IRiftHuntARProvider
    {
        /// <summary>Fired when a target image is detected. Argument: target ID.</summary>
        event Action<string> ImageFound;

        /// <summary>Fired when a target image is lost. Argument: target ID.</summary>
        event Action<string> ImageLost;

        /// <summary>
        /// Returns the world-space transform that follows the tracked target.
        /// Returns null if the target ID is not configured.
        /// </summary>
        Transform GetTargetTransform(string targetId);

        /// <summary>
        /// Whether the specified target is currently being tracked.
        /// </summary>
        bool IsImageTracked(string targetId);
    }
}
