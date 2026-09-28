using UnityEngine;
using UnityEngine.Rendering;

namespace StoryMint.RiftHunt
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RiftPortalVfxRuntimeFix : MonoBehaviour
    {
        [SerializeField] private string circleMaterialPath = "StoryMint/Materials/RiftVFX_Circle_URP";
        [SerializeField] private string pointMaterialPath = "StoryMint/Materials/RiftVFX_Point_URP";
        [SerializeField] private string smokeMaterialPath = "StoryMint/Materials/RiftVFX_Smoke_URP";
        [SerializeField] private string distortionMaterialPath = "StoryMint/Materials/RiftVFX_Distortion_URP";

        private static readonly Color RingColor = new Color(0.03f, 1.35f, 1.6f, 0.96f);
        private static readonly Color SparkColor = new Color(1.35f, 2f, 2f, 1f);
        private static readonly Color SmokeColor = new Color(0.08f, 0.95f, 1.2f, 0.82f);
        private static readonly Color HiddenDistortionColor = new Color(0.08f, 0.8f, 1f, 0.15f);

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
            Apply();
            PlayParticles();
        }

        public void Apply()
        {
            ParticleSystemRenderer ring = GetComponent<ParticleSystemRenderer>();
            ParticleSystemRenderer sparks = FindRenderer("CircleSparks");
            ParticleSystemRenderer smoke = FindRenderer("Smoke");
            ParticleSystemRenderer distortion = FindRenderer("Distortion");

            AssignMaterial(ring, circleMaterialPath, RingColor, false, 3580);
            AssignMaterial(sparks, pointMaterialPath, SparkColor, true, 3610);
            AssignMaterial(smoke, smokeMaterialPath, SmokeColor, false, 3570);
            AssignMaterial(distortion, distortionMaterialPath, HiddenDistortionColor, false, 3560);

            ConfigureSystem(GetComponent<ParticleSystem>(), ring, RingColor, 1.2f, 0.6f, true, ParticleSystemRenderSpace.Local);

            // Child particle systems can escape to the top-right of the WebGL canvas on mobile.
            // Keep the stable portal body and suppress free-floating child particles.
            ConfigureSystem(FindSystem("CircleSparks"), sparks, SparkColor, 0.65f, 0.12f, false, ParticleSystemRenderSpace.Local);

            ConfigureSystem(FindSystem("Smoke"), smoke, SmokeColor, 1.15f, 0.55f, false, ParticleSystemRenderSpace.Local);

            // The original Hovl distortion particle depends on a legacy distortion shader path.
            // In WebGL/URP it tends to render as a huge faint billboard, so keep the child present
            // for prefab fidelity but hide its renderer until we build a proper URP distortion pass.
            ConfigureSystem(FindSystem("Distortion"), distortion, HiddenDistortionColor, 1f, 0.18f, false, ParticleSystemRenderSpace.Local);
        }

        private ParticleSystemRenderer FindRenderer(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<ParticleSystemRenderer>() : null;
        }

        private ParticleSystem FindSystem(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<ParticleSystem>() : null;
        }

        private static void AssignMaterial(ParticleSystemRenderer renderer, string resourcePath, Color color, bool additive, int renderQueue)
        {
            if (renderer == null)
                return;

            Material source = Resources.Load<Material>(resourcePath);
            if (source == null)
            {
                Debug.LogWarning("[StoryMint] RiftPortalVFX material missing at Resources path '" + resourcePath + "'.");
                return;
            }

            Material material = Application.isPlaying ? new Material(source) : source;
            ConfigureParticleMaterial(material, color, additive, renderQueue);
            renderer.sharedMaterial = material;
        }

        private static void ConfigureParticleMaterial(Material material, Color color, bool additive, int renderQueue)
        {
            if (material == null)
                return;

            material.SetOverrideTag("RenderType", "Transparent");
            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.SetColor("_EmissionColor", color * 3.8f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_AlphaClip", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            material.SetFloat("_DstBlendAlpha", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_ColorMode", 0f);
            material.SetFloat("_SoftParticlesEnabled", 0f);
            material.SetFloat("_DistortionEnabled", 0f);

            material.renderQueue = renderQueue;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SOFTPARTICLES_ON");
            material.DisableKeyword("_DISTORTION_ON");
        }

        private static void ConfigureSystem(
            ParticleSystem particles,
            ParticleSystemRenderer renderer,
            Color color,
            float sizeMultiplier,
            float maxParticleSize,
            bool rendererVisible,
            ParticleSystemRenderSpace alignment)
        {
            if (particles != null)
            {
                ParticleSystem.MainModule main = particles.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startColor = color;
                main.startSizeMultiplier = Mathf.Max(sizeMultiplier, 0.08f);

                ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
                colorOverLifetime.enabled = false;

                ParticleSystem.EmissionModule emission = particles.emission;
                emission.enabled = rendererVisible;

                if (!rendererVisible)
                {
                    particles.Clear(true);
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            if (renderer == null)
                return;

            renderer.alignment = alignment;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = maxParticleSize;
            renderer.sortingFudge = rendererVisible ? 6f : -10f;
            renderer.sortingOrder = rendererVisible ? 80 : -100;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.allowOcclusionWhenDynamic = false;
            renderer.enabled = rendererVisible;
        }

        private void PlayParticles()
        {
            ParticleSystem[] particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particles = particleSystems[i];
                if (particles == null)
                    continue;

                particles.gameObject.SetActive(true);
                particles.Clear(true);

                ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
                if (renderer != null && !renderer.enabled)
                {
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    continue;
                }

                particles.Simulate(0.08f, true, true, true);
                particles.Play(true);
            }
        }
    }
}
