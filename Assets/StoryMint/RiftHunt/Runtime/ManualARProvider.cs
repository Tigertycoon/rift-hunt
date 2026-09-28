using System;
using UnityEngine;

namespace StoryMint.RiftHunt
{
    /// <summary>Explicit test provider. Does not access a camera or perform tracking.</summary>
    public sealed class ManualARProvider : MonoBehaviour, IRiftHuntARProvider
    {
        [SerializeField] private string targetId = "preview-target";
        private bool tracked;
        public event Action<string> ImageFound;
        public event Action<string> ImageLost;

        public Transform GetTargetTransform(string id) => id == targetId ? transform : null;
        public bool IsImageTracked(string id) => id == targetId && tracked;

        [ContextMenu("Signal Found (simulated tracking)")]
        public void SignalFound()
        {
            if (!Application.isPlaying || tracked) return;
            tracked = true;
            ImageFound?.Invoke(targetId);
        }

        [ContextMenu("Signal Lost (simulated tracking)")]
        public void SignalLost()
        {
            if (!Application.isPlaying || !tracked) return;
            tracked = false;
            ImageLost?.Invoke(targetId);
        }
    }
}
