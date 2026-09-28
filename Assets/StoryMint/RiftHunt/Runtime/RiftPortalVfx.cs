using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StoryMint.RiftHunt
{
    /// <summary>
    /// Procedurally builds a rift/portal VFX optimised for WebGL + URP + mobile AR.
    /// All sizes are in local-space units; the parent transform controls world scale.
    /// Attach to an empty GameObject. Call Rebuild() to recreate the visual.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RiftPortalVfx : MonoBehaviour
    {
        [Header("Visual Tuning")]
        [SerializeField] private Color edgeColor = new Color(0.1f, 0.95f, 1f, 0.8f);
        [SerializeField] private Color coreColor = new Color(0.02f, 0.03f, 0.06f, 0.6f);
        [SerializeField] private Color crackColor = new Color(1f, 0.35f, 0.18f, 0.9f);
        [SerializeField] private Color sparkColorA = new Color(0.5f, 1f, 0.95f, 0.65f);
        [SerializeField] private Color sparkColorB = new Color(1f, 0.42f, 0.24f, 0.85f);
        [SerializeField] private float portalSize = 0.35f;

        [Header("Shader References (auto-found if null)")]
        [SerializeField] private Shader portalShader;
        [SerializeField] private Shader sparkShader;

        // Built objects
        private Transform floatRoot;
        private MeshRenderer portalQuadRenderer;
        private Material portalMaterial;
        private ParticleSystem edgeSparks;
        private ParticleSystem ambientDust;

        private bool isBuilt;

        private void Awake()
        {
            Build();
        }

        private void OnEnable()
        {
            if (!isBuilt)
                Build();
        }

        private void Update()
        {
            if (floatRoot == null)
                return;

            float t = Time.time;
            // Gentle float
            floatRoot.localPosition = Vector3.up * (Mathf.Sin(t * 1.6f) * 0.008f);
            // Slow wobble
            floatRoot.localRotation = Quaternion.Euler(
                Mathf.Sin(t * 0.9f) * 2f,
                Mathf.Sin(t * 0.7f + 0.5f) * 3f,
                Mathf.Sin(t * 1.1f + 1f) * 1.5f);
        }

        /// <summary>
        /// Destroy and recreate the entire visual. Safe to call at any time.
        /// </summary>
        public void Rebuild()
        {
            DestroyVisual();
            Build();
        }

        public void SetColors(Color edge, Color core, Color crack)
        {
            edgeColor = edge;
            coreColor = core;
            crackColor = crack;
            if (portalMaterial != null)
            {
                portalMaterial.SetColor("_Color", edgeColor);
                portalMaterial.SetColor("_CoreColor", coreColor);
                portalMaterial.SetColor("_CrackColor", crackColor);
            }
        }

        // -------------------------------------------------------------------
        //  Construction
        // -------------------------------------------------------------------

        private void Build()
        {
            if (isBuilt)
                return;

            FindShaders();
            if (portalShader == null)
            {
                Debug.LogWarning("[StoryMint] RiftPortalVfx: Could not find StoryMint/RiftPortalEffect shader.");
                return;
            }

            floatRoot = CreateChild("FloatRoot", transform);

            BuildPortalQuad();
            BuildEdgeSparks();
            BuildAmbientDust();

            // Collider for tap interaction
            SphereCollider collider = gameObject.GetComponent<SphereCollider>();
            if (collider == null)
                collider = gameObject.AddComponent<SphereCollider>();
            collider.isTrigger = true;
            collider.radius = portalSize * 1.5f;

            isBuilt = true;
        }

        private void FindShaders()
        {
            if (portalShader == null)
                portalShader = Shader.Find("StoryMint/RiftPortalEffect");
            if (sparkShader == null)
                sparkShader = Shader.Find("StoryMint/RiftSparkParticle");

            // Fallback: use URP Particles/Unlit for sparks if custom not found
            if (sparkShader == null)
                sparkShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }

        private void BuildPortalQuad()
        {
            GameObject quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quadObj.name = "PortalQuad";
            quadObj.transform.SetParent(floatRoot, false);
            quadObj.transform.localScale = Vector3.one * portalSize * 2f;
            quadObj.transform.localPosition = Vector3.zero;

            // Remove the default collider from the quad
            Collider quadCollider = quadObj.GetComponent<Collider>();
            if (quadCollider != null)
            {
                if (Application.isPlaying) Destroy(quadCollider);
                else DestroyImmediate(quadCollider);
            }

            portalMaterial = new Material(portalShader)
            {
                name = "StoryMintRiftPortalMat",
                renderQueue = (int)RenderQueue.Transparent + 50
            };
            portalMaterial.SetColor("_Color", edgeColor);
            portalMaterial.SetColor("_CoreColor", coreColor);
            portalMaterial.SetColor("_CrackColor", crackColor);

            portalQuadRenderer = quadObj.GetComponent<MeshRenderer>();
            portalQuadRenderer.sharedMaterial = portalMaterial;
            portalQuadRenderer.shadowCastingMode = ShadowCastingMode.Off;
            portalQuadRenderer.receiveShadows = false;
            portalQuadRenderer.lightProbeUsage = LightProbeUsage.Off;
            portalQuadRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void BuildEdgeSparks()
        {
            if (sparkShader == null)
                return;

            GameObject sparkObj = new GameObject("EdgeSparks");
            sparkObj.transform.SetParent(floatRoot, false);
            edgeSparks = sparkObj.AddComponent<ParticleSystem>();

            // Main module - AR-scaled sizes
            ParticleSystem.MainModule main = edgeSparks.main;
            main.loop = true;
            main.duration = 2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.022f);
            main.startColor = new ParticleSystem.MinMaxGradient(sparkColorA, sparkColorB);
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            // Emission
            ParticleSystem.EmissionModule emission = edgeSparks.emission;
            emission.rateOverTime = 18f;

            // Shape - emit from the edge ring
            ParticleSystem.ShapeModule shape = edgeSparks.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = portalSize * 0.85f;
            shape.radiusThickness = 0.08f;
            shape.rotation = new Vector3(0f, 0f, 0f);

            // Velocity - slight outward drift
            ParticleSystem.VelocityOverLifetimeModule velocity = edgeSparks.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.radial = new ParticleSystem.MinMaxCurve(0.01f, 0.04f);

            // Size over lifetime - fade out
            ParticleSystem.SizeOverLifetimeModule sizeOverLife = edgeSparks.sizeOverLifetime;
            sizeOverLife.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 0f));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Color over lifetime - fade alpha
            ParticleSystem.ColorOverLifetimeModule colorOverLife = edgeSparks.colorOverLifetime;
            colorOverLife.enabled = true;
            Gradient alphaGradient = new Gradient();
            alphaGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = alphaGradient;

            // Renderer
            ParticleSystemRenderer sparkRenderer = edgeSparks.GetComponent<ParticleSystemRenderer>();
            sparkRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            sparkRenderer.alignment = ParticleSystemRenderSpace.View;
            sparkRenderer.material = CreateSparkMaterial("StoryMintEdgeSparkMat", sparkColorA);
            sparkRenderer.sortingOrder = 2;
            sparkRenderer.maxParticleSize = 0.05f;
            sparkRenderer.minParticleSize = 0f;
            sparkRenderer.shadowCastingMode = ShadowCastingMode.Off;
            sparkRenderer.receiveShadows = false;
        }

        private void BuildAmbientDust()
        {
            if (sparkShader == null)
                return;

            GameObject dustObj = new GameObject("AmbientDust");
            dustObj.transform.SetParent(floatRoot, false);
            ambientDust = dustObj.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ambientDust.main;
            main.loop = true;
            main.duration = 3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.005f, 0.02f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.003f, 0.01f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.3f, 0.8f, 1f, 0.3f),
                new Color(0.8f, 1f, 0.9f, 0.5f));
            main.maxParticles = 25;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = ambientDust.emission;
            emission.rateOverTime = 8f;

            ParticleSystem.ShapeModule shape = ambientDust.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = portalSize * 0.6f;

            ParticleSystem.ColorOverLifetimeModule colorOverLife = ambientDust.colorOverLifetime;
            colorOverLife.enabled = true;
            Gradient dustGradient = new Gradient();
            dustGradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.3f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = dustGradient;

            ParticleSystemRenderer dustRenderer = ambientDust.GetComponent<ParticleSystemRenderer>();
            dustRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            dustRenderer.alignment = ParticleSystemRenderSpace.View;
            dustRenderer.material = CreateSparkMaterial("StoryMintDustMat", new Color(0.4f, 0.9f, 1f, 0.4f));
            dustRenderer.sortingOrder = 1;
            dustRenderer.maxParticleSize = 0.03f;
            dustRenderer.minParticleSize = 0f;
            dustRenderer.shadowCastingMode = ShadowCastingMode.Off;
            dustRenderer.receiveShadows = false;
        }

        private Material CreateSparkMaterial(string matName, Color tint)
        {
            Material mat = new Material(sparkShader)
            {
                name = matName,
                renderQueue = (int)RenderQueue.Transparent + 60
            };

            // Handle both custom shader and URP Particles/Unlit fallback
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", tint);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_EmissionBoost"))
                mat.SetFloat("_EmissionBoost", 2.0f);

            // URP Particles/Unlit specific
            if (mat.HasProperty("_Surface"))
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetFloat("_Blend", 2f); // Additive
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.One);
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_Cull", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }

            return mat;
        }

        // -------------------------------------------------------------------
        //  Cleanup
        // -------------------------------------------------------------------

        private void DestroyVisual()
        {
            if (floatRoot != null)
            {
                if (Application.isPlaying) Destroy(floatRoot.gameObject);
                else DestroyImmediate(floatRoot.gameObject);
            }

            floatRoot = null;
            portalQuadRenderer = null;
            portalMaterial = null;
            edgeSparks = null;
            ambientDust = null;
            isBuilt = false;
        }

        private static Transform CreateChild(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }
    }
}
