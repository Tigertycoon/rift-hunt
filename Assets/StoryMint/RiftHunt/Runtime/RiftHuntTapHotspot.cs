using UnityEngine;

namespace StoryMint.RiftHunt
{
    public class RiftHuntTapHotspot : MonoBehaviour
    {
        [SerializeField] private float pulseSpeed = 4f;
        [SerializeField] private float pulseAmount = 0.08f;
        [SerializeField] private float rotationSpeed = 60f;

        public string StepId { get; private set; }
        public string TargetId { get; private set; }

        private Vector3 initialLocalScale = Vector3.one;
        private Vector3 baseScale = Vector3.one;
        private Quaternion baseRotation = Quaternion.identity;
        private bool hasCachedScale;
        private bool visualHandlesMotion;

        public void Configure(string stepId, string targetId, float scale)
        {
            StepId = stepId;
            TargetId = targetId;
            if (!hasCachedScale)
            {
                initialLocalScale = transform.localScale;
                hasCachedScale = true;
            }

            baseRotation = transform.localRotation;
            baseScale = initialLocalScale * scale;
            transform.localScale = baseScale;
            visualHandlesMotion =
                GetComponent<StoryMintRiftPortal>() != null ||
                GetComponent<RiftHuntAnchorVisual>() != null ||
                GetComponentsInChildren<ParticleSystem>(true).Length > 0 ||
                HasChildMeshVisual();
        }

        private void Update()
        {
            if (visualHandlesMotion)
            {
                transform.localScale = baseScale;
                return;
            }

            float pulse = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            transform.localScale = baseScale * pulse;
            transform.localRotation = baseRotation * Quaternion.Euler(0f, Time.time * rotationSpeed, 0f);
        }

        private bool HasChildMeshVisual()
        {
            MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null && renderers[index].transform != transform)
                    return true;
            }

            return false;
        }
    }

    public class RiftHuntAnchorVisual : MonoBehaviour
    {
        private Transform floatRoot;
        private Transform haloOuter;
        private Transform haloInner;
        private Transform core;
        private Transform glow;
        private Transform orbitRoot;
        private Transform[] shards = new Transform[0];
        private Vector3 floatRootBasePosition;
        private Vector3 haloOuterBaseScale;
        private Vector3 haloInnerBaseScale;
        private Vector3 coreBaseScale;
        private Vector3 glowBaseScale;
        private Vector3[] shardBasePositions = new Vector3[0];

        private void Awake()
        {
            CacheReferences();
        }

        private void OnEnable()
        {
            if (floatRoot == null)
                CacheReferences();
        }

        private void Update()
        {
            if (floatRoot == null)
                return;

            float time = Time.time;
            floatRoot.localPosition = floatRootBasePosition + Vector3.up * (Mathf.Sin(time * 2.4f) * 0.035f);

            float haloPulse = 1f + Mathf.Sin(time * 3.1f) * 0.08f;
            if (haloOuter != null)
                haloOuter.localScale = haloOuterBaseScale * haloPulse;

            if (haloInner != null)
                haloInner.localScale = haloInnerBaseScale * (1f + Mathf.Sin(time * 2.7f + 0.8f) * 0.05f);

            if (core != null)
                core.localScale = coreBaseScale * (1f + Mathf.Sin(time * 5.2f) * 0.14f);

            if (glow != null)
                glow.localScale = glowBaseScale * (1f + Mathf.Sin(time * 4.6f + 0.4f) * 0.12f);

            if (orbitRoot != null)
                orbitRoot.localRotation = Quaternion.Euler(0f, time * 120f, 0f);

            for (int index = 0; index < shards.Length; index++)
            {
                if (shards[index] == null)
                    continue;

                float bob = Mathf.Sin(time * 3.8f + index * 0.75f) * 0.02f;
                shards[index].localPosition = shardBasePositions[index] + Vector3.up * bob;
                shards[index].localRotation = Quaternion.Euler(0f, time * 180f + index * 40f, time * 90f);
            }
        }

        private void CacheReferences()
        {
            floatRoot = transform.Find("FloatRoot");
            if (floatRoot == null)
                return;

            haloOuter = floatRoot.Find("HaloOuter");
            haloInner = floatRoot.Find("HaloInner");
            core = floatRoot.Find("Core");
            glow = floatRoot.Find("Glow");
            orbitRoot = floatRoot.Find("OrbitRoot");

            floatRootBasePosition = floatRoot.localPosition;
            haloOuterBaseScale = haloOuter != null ? haloOuter.localScale : Vector3.one;
            haloInnerBaseScale = haloInner != null ? haloInner.localScale : Vector3.one;
            coreBaseScale = core != null ? core.localScale : Vector3.one;
            glowBaseScale = glow != null ? glow.localScale : Vector3.one;

            if (orbitRoot != null)
            {
                shards = new Transform[orbitRoot.childCount];
                shardBasePositions = new Vector3[orbitRoot.childCount];
                for (int index = 0; index < orbitRoot.childCount; index++)
                {
                    shards[index] = orbitRoot.GetChild(index);
                    shardBasePositions[index] = shards[index].localPosition;
                }
            }
            else
            {
                shards = new Transform[0];
                shardBasePositions = new Vector3[0];
            }
        }
    }
}
