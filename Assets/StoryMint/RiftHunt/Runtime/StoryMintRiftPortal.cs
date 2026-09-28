using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StoryMint.RiftHunt
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class StoryMintRiftPortal : MonoBehaviour
    {
        private const int ArcSegments = 54;
        private const int TextureSize = 192;

        private readonly List<AnimatedLayer> animatedLayers = new List<AnimatedLayer>();
        private readonly List<MeshRenderer> flickerRenderers = new List<MeshRenderer>();
        private readonly List<Color> flickerBaseColors = new List<Color>();

        private Transform floatRoot;
        private Transform auraRoot;
        private Transform arcRoot;
        private Transform crackRoot;
        private Transform fleckRoot;
        private Transform particleRoot;

        private MeshRenderer auraRenderer;
        private MeshRenderer coreRenderer;
        private MeshRenderer crackGlowRenderer;
        private Vector3 auraBaseScale;
        private Vector3 coreBaseScale;
        private Vector3 crackGlowBaseScale;

        private struct AnimatedLayer
        {
            public Transform Transform;
            public Vector3 BaseScale;
            public float RotationSpeed;
            public float RotationWobble;
            public float PulseAmount;
            public float Phase;
        }

        private void Awake()
        {
            BuildVisual();
        }

        private void OnEnable()
        {
            BuildVisual();
        }

        private void Update()
        {
            if (floatRoot == null)
                return;

            float time = Time.time;
            floatRoot.localPosition = Vector3.up * (Mathf.Sin(time * 1.7f) * 0.018f);
            floatRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 0.85f) * 1.8f);

            if (auraRenderer != null)
                auraRenderer.transform.localScale = auraBaseScale * (1f + Mathf.Sin(time * 2.6f) * 0.045f);

            if (coreRenderer != null)
            {
                coreRenderer.transform.localScale = coreBaseScale * (1f + Mathf.Sin(time * 3.1f + 0.8f) * 0.025f);
            }

            if (crackGlowRenderer != null)
            {
                float pulse = 1f + Mathf.Sin(time * 6.8f) * 0.055f;
                crackGlowRenderer.transform.localScale = new Vector3(crackGlowBaseScale.x * pulse, crackGlowBaseScale.y, crackGlowBaseScale.z);
            }

            for (int index = 0; index < animatedLayers.Count; index++)
            {
                AnimatedLayer layer = animatedLayers[index];
                if (layer.Transform == null)
                    continue;

                float rotation = time * layer.RotationSpeed + Mathf.Sin(time * 1.9f + layer.Phase) * layer.RotationWobble;
                float scale = 1f + Mathf.Sin(time * 2.8f + layer.Phase) * layer.PulseAmount;
                layer.Transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
                layer.Transform.localScale = layer.BaseScale * scale;
            }

            for (int index = 0; index < flickerRenderers.Count; index++)
            {
                MeshRenderer renderer = flickerRenderers[index];
                if (renderer == null || renderer.sharedMaterial == null)
                    continue;

                Color color = flickerBaseColors[index];
                color.a *= 0.82f + Mathf.Sin(time * (4.1f + index * 0.27f) + index) * 0.18f;
                SetMaterialColor(renderer.sharedMaterial, color);
            }
        }

        private void BuildVisual()
        {
            if (floatRoot != null)
                return;

            ClearGeneratedChildren();
            animatedLayers.Clear();
            flickerRenderers.Clear();
            flickerBaseColors.Clear();

            SphereCollider tapCollider = gameObject.GetComponent<SphereCollider>();
            if (tapCollider == null)
                tapCollider = gameObject.AddComponent<SphereCollider>();
            tapCollider.isTrigger = true;
            tapCollider.radius = 0.78f;

            floatRoot = CreateChild("FloatRoot", transform);
            auraRoot = CreateChild("AuraRoot", floatRoot);
            arcRoot = CreateChild("EnergyArcs", floatRoot);
            crackRoot = CreateChild("CrackRoot", floatRoot);
            fleckRoot = CreateChild("LightFlecks", floatRoot);
            particleRoot = CreateChild("AetherParticles", floatRoot);

            Texture2D bandTexture = CreateBandTexture("StoryMintRiftSoftBand", 2.25f, 0.12f);
            Texture2D hotBandTexture = CreateBandTexture("StoryMintRiftHotBand", 4.2f, 0.04f);
            Texture2D mistTexture = CreateRadialTexture("StoryMintRiftMistParticle", 0.02f, 1f, 2.35f, 0.22f);
            Texture2D sparkTexture = CreateSparkTexture("StoryMintRiftSparkParticle");

            Material shadowMaterial = CreateMaterial("StoryMintRiftShadowMat", new Color(0.004f, 0.008f, 0.025f, 0.5f), false, 0f, 0, null);
            Material auraMaterial = CreateMaterial("StoryMintRiftAuraMat", new Color(0.07f, 0.95f, 1.2f, 0.48f), true, 2.4f, 1, null);
            Material coreMaterial = CreateMaterial("StoryMintRiftCoreMat", new Color(0.012f, 0.018f, 0.07f, 0.44f), false, 0.2f, 2, null);
            Material blueArcMaterial = CreateMaterial("StoryMintRiftBlueArcMat", new Color(0.08f, 0.95f, 1f, 0.96f), true, 2.2f, 4, bandTexture);
            Material whiteArcMaterial = CreateMaterial("StoryMintRiftWhiteArcMat", new Color(0.86f, 1f, 0.98f, 0.92f), true, 2.8f, 5, bandTexture);
            Material violetArcMaterial = CreateMaterial("StoryMintRiftVioletArcMat", new Color(0.45f, 0.36f, 1.25f, 0.74f), true, 1.9f, 6, bandTexture);
            Material crackGlowMaterial = CreateMaterial("StoryMintRiftCrackGlowMat", new Color(0.48f, 1.15f, 1.3f, 0.84f), true, 3.2f, 7, bandTexture);
            Material crackHotMaterial = CreateMaterial("StoryMintRiftCrackHotMat", new Color(1f, 0.29f, 0.12f, 0.98f), true, 2.6f, 8, hotBandTexture);
            Material fleckMaterial = CreateMaterial("StoryMintRiftFleckMat", new Color(0.94f, 1f, 0.96f, 0.86f), true, 2.1f, 9, hotBandTexture);
            Material mistMaterial = CreateMaterial("StoryMintRiftMistMat", new Color(0.08f, 0.82f, 1.1f, 0.32f), true, 1.7f, 10, mistTexture);
            Material sparkMaterial = CreateMaterial("StoryMintRiftSparkMat", new Color(0.92f, 1.35f, 1.45f, 0.96f), true, 3.8f, 11, sparkTexture);

            MeshRenderer shadow = CreateEllipseRenderer("DeepRiftShadow", auraRoot, 0.63f, 0.37f, -0.09f, 0.018f, 1.2f, shadowMaterial);
            shadow.transform.localScale = new Vector3(1f, 0.92f, 1f);

            auraRenderer = CreateEllipseRingRenderer("BreathingAetherGlow", auraRoot, 0.88f, 0.53f, 0.58f, 0.35f, -0.075f, 0.02f, 2.1f, auraMaterial);
            auraBaseScale = auraRenderer.transform.localScale;

            coreRenderer = CreateEllipseRenderer("DeepVortexWindow", auraRoot, 0.48f, 0.285f, -0.04f, 0.012f, 3.4f, coreMaterial);
            coreBaseScale = coreRenderer.transform.localScale;

            MeshRenderer outerA = CreateArcRenderer("OuterArcTop", arcRoot, 0.67f, 0.41f, 18f, 134f, 0.066f, 0.03f, 0.012f, blueArcMaterial, 0.7f);
            MeshRenderer outerB = CreateArcRenderer("OuterArcBottom", arcRoot, 0.69f, 0.42f, 202f, 128f, 0.06f, 0.025f, 0.016f, blueArcMaterial, 2.1f);
            MeshRenderer outerC = CreateArcRenderer("OuterArcRightSpark", arcRoot, 0.73f, 0.43f, -34f, 62f, 0.043f, 0.02f, 0.02f, whiteArcMaterial, 3.4f);
            MeshRenderer innerA = CreateArcRenderer("InnerHotArcLeft", arcRoot, 0.49f, 0.3f, 144f, 102f, 0.036f, 0.018f, 0.031f, whiteArcMaterial, 4.2f);
            MeshRenderer innerB = CreateArcRenderer("InnerHotArcRight", arcRoot, 0.51f, 0.31f, -46f, 112f, 0.032f, 0.016f, 0.035f, whiteArcMaterial, 5.6f);
            MeshRenderer swirlA = CreateArcRenderer("VioletSwirlA", arcRoot, 0.35f, 0.2f, 208f, 125f, 0.018f, 0.012f, 0.042f, violetArcMaterial, 7.1f);
            MeshRenderer swirlB = CreateArcRenderer("VioletSwirlB", arcRoot, 0.38f, 0.23f, 32f, 114f, 0.016f, 0.012f, 0.045f, violetArcMaterial, 8.7f);

            AddAnimatedLayer(outerA.transform, -3.2f, 2.3f, 0.028f, 0.1f);
            AddAnimatedLayer(outerB.transform, 2.4f, 1.9f, 0.024f, 1.7f);
            AddAnimatedLayer(outerC.transform, -5.8f, 2.8f, 0.035f, 2.8f);
            AddAnimatedLayer(innerA.transform, 4.6f, 1.2f, 0.026f, 0.9f);
            AddAnimatedLayer(innerB.transform, -4.1f, 1.5f, 0.024f, 2.3f);
            AddAnimatedLayer(swirlA.transform, -10.5f, 0.8f, 0.018f, 3.6f);
            AddAnimatedLayer(swirlB.transform, 9.5f, 0.9f, 0.018f, 4.8f);

            BuildCrack(crackGlowMaterial, crackHotMaterial);
            BuildLightFlecks(fleckMaterial);
            BuildParticleSystems(mistMaterial, sparkMaterial);

            AddFlicker(outerC, new Color(0.86f, 1f, 0.98f, 0.92f));
            AddFlicker(innerA, new Color(0.86f, 1f, 0.98f, 0.92f));
            AddFlicker(innerB, new Color(0.86f, 1f, 0.98f, 0.92f));
        }

        private void BuildCrack(Material crackGlowMaterial, Material crackHotMaterial)
        {
            var mainCrack = new[]
            {
                new Vector2(-0.03f, 0.34f),
                new Vector2(0.07f, 0.17f),
                new Vector2(-0.025f, 0.015f),
                new Vector2(0.075f, -0.14f),
                new Vector2(-0.015f, -0.36f)
            };

            crackGlowRenderer = CreateMeshRenderer("BlueCrackBloom", crackRoot, CreatePolylineStripMesh(mainCrack, 0.094f, 0.06f), crackGlowMaterial);
            crackGlowBaseScale = crackGlowRenderer.transform.localScale;

            MeshRenderer hotCrack = CreateMeshRenderer("HotCrackCore", crackRoot, CreatePolylineStripMesh(mainCrack, 0.026f, 0.073f), crackHotMaterial);
            AddFlicker(hotCrack, new Color(1f, 0.29f, 0.12f, 0.98f));

            CreateMeshRenderer("CrackBranchTop", crackRoot, CreatePolylineStripMesh(new[]
            {
                new Vector2(0.038f, 0.19f),
                new Vector2(0.16f, 0.27f),
                new Vector2(0.23f, 0.24f)
            }, 0.018f, 0.071f), crackHotMaterial);

            CreateMeshRenderer("CrackBranchLow", crackRoot, CreatePolylineStripMesh(new[]
            {
                new Vector2(0.035f, -0.14f),
                new Vector2(0.17f, -0.23f),
                new Vector2(0.22f, -0.33f)
            }, 0.014f, 0.071f), crackGlowMaterial);

            CreateMeshRenderer("CrackBranchLeft", crackRoot, CreatePolylineStripMesh(new[]
            {
                new Vector2(-0.012f, 0.02f),
                new Vector2(-0.15f, -0.055f),
                new Vector2(-0.23f, -0.035f)
            }, 0.016f, 0.071f), crackGlowMaterial);
        }

        private void BuildLightFlecks(Material material)
        {
            var flecks = new[]
            {
                new Vector2(-0.62f, 0.23f), new Vector2(-0.52f, -0.22f), new Vector2(0.56f, 0.28f),
                new Vector2(0.63f, -0.08f), new Vector2(-0.2f, 0.38f), new Vector2(0.26f, -0.4f),
                new Vector2(0.02f, 0.46f), new Vector2(-0.7f, -0.02f), new Vector2(0.47f, -0.31f)
            };

            for (int index = 0; index < flecks.Length; index++)
            {
                Vector2 center = flecks[index];
                float angle = 22f + index * 37f;
                float length = 0.045f + (index % 3) * 0.015f;
                Vector2 dir = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                MeshRenderer fleck = CreateMeshRenderer(
                    "AetherFleck" + index,
                    fleckRoot,
                    CreatePolylineStripMesh(new[] { center - dir * length * 0.5f, center + dir * length * 0.5f }, 0.012f, 0.082f + index * 0.0006f),
                    material);
                AddAnimatedLayer(fleck.transform, index % 2 == 0 ? 7.2f : -6.5f, 0.8f, 0.11f, index * 0.73f);
                AddFlicker(fleck, new Color(0.94f, 1f, 0.96f, 0.78f));
            }
        }

        private void BuildParticleSystems(Material mistMaterial, Material sparkMaterial)
        {
            ParticleSystem mist = CreatePortalParticleSystem(
                "SlowAetherMist",
                particleRoot,
                mistMaterial,
                72,
                18f,
                0.38f,
                new Vector3(1.55f, 0.72f, 1f),
                1.7f,
                3.1f,
                0.008f,
                0.035f,
                0.13f,
                0.31f,
                new Color(0.04f, 0.72f, 1.1f, 0.16f),
                new Color(0.3f, 1.15f, 1.3f, 0.28f),
                -0.015f);
            ConfigureParticleFade(mist, new Color(0.1f, 0.95f, 1.2f, 1f), 0.34f);
            ConfigureParticleMotion(mist, 0.1f, 0.045f, 1.2f, 0.55f);

            ParticleSystem rimSparks = CreatePortalParticleSystem(
                "RimOrbitSparks",
                particleRoot,
                sparkMaterial,
                96,
                38f,
                0.71f,
                new Vector3(1.42f, 0.62f, 1f),
                0.58f,
                1.25f,
                0.015f,
                0.075f,
                0.018f,
                0.052f,
                new Color(0.28f, 1.15f, 1.35f, 0.78f),
                new Color(1f, 1f, 0.92f, 0.98f),
                0.09f);
            ConfigureParticleFade(rimSparks, new Color(0.7f, 1.25f, 1.35f, 1f), 0.95f);
            ConfigureParticleMotion(rimSparks, 1.9f, 0.02f, 2.1f, 1.8f);

            ParticleSystem innerAsh = CreatePortalParticleSystem(
                "InnerBlueAsh",
                particleRoot,
                sparkMaterial,
                52,
                20f,
                0.31f,
                new Vector3(1.35f, 0.58f, 1f),
                0.8f,
                1.6f,
                0.006f,
                0.028f,
                0.012f,
                0.034f,
                new Color(0.12f, 0.55f, 1.3f, 0.65f),
                new Color(0.85f, 1.2f, 1.35f, 0.95f),
                0.115f);
            ConfigureParticleFade(innerAsh, new Color(0.3f, 0.9f, 1.25f, 1f), 0.72f);
            ConfigureParticleMotion(innerAsh, -0.85f, -0.01f, 1.65f, 0.9f);
        }

        private void ClearGeneratedChildren()
        {
            for (int index = transform.childCount - 1; index >= 0; index--)
            {
                Transform child = transform.GetChild(index);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        private void AddAnimatedLayer(Transform target, float rotationSpeed, float rotationWobble, float pulseAmount, float phase)
        {
            if (target == null)
                return;

            animatedLayers.Add(new AnimatedLayer
            {
                Transform = target,
                BaseScale = target.localScale,
                RotationSpeed = rotationSpeed,
                RotationWobble = rotationWobble,
                PulseAmount = pulseAmount,
                Phase = phase
            });
        }

        private void AddFlicker(MeshRenderer renderer, Color color)
        {
            if (renderer == null)
                return;

            flickerRenderers.Add(renderer);
            flickerBaseColors.Add(color);
        }

        private static Transform CreateChild(string name, Transform parent)
        {
            Transform child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static MeshRenderer CreateQuadRenderer(string name, Transform parent, float width, float height, float z, Material material)
        {
            return CreateMeshRenderer(name, parent, CreateQuadMesh(name + "Mesh", width, height, z), material);
        }

        private static MeshRenderer CreateEllipseRenderer(string name, Transform parent, float radiusX, float radiusY, float z, float wobble, float seed, Material material)
        {
            return CreateMeshRenderer(name, parent, CreateEllipseDiscMesh(name + "Mesh", radiusX, radiusY, z, wobble, seed), material);
        }

        private static MeshRenderer CreateEllipseRingRenderer(
            string name,
            Transform parent,
            float outerRadiusX,
            float outerRadiusY,
            float innerRadiusX,
            float innerRadiusY,
            float z,
            float wobble,
            float seed,
            Material material)
        {
            return CreateMeshRenderer(name, parent, CreateEllipseRingMesh(name + "Mesh", outerRadiusX, outerRadiusY, innerRadiusX, innerRadiusY, z, wobble, seed), material);
        }

        private static MeshRenderer CreateArcRenderer(
            string name,
            Transform parent,
            float radiusX,
            float radiusY,
            float startDegrees,
            float sweepDegrees,
            float width,
            float wobble,
            float z,
            Material material,
            float seed)
        {
            return CreateMeshRenderer(name, parent, CreateArcStripMesh(name + "Mesh", radiusX, radiusY, startDegrees, sweepDegrees, width, wobble, z, seed), material);
        }

        private static MeshRenderer CreateMeshRenderer(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject meshObject = new GameObject(name);
            meshObject.transform.SetParent(parent, false);

            MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sortingOrder = 100;
            meshRenderer.allowOcclusionWhenDynamic = false;
            return meshRenderer;
        }

        private static Mesh CreateQuadMesh(string name, float width, float height, float z)
        {
            var vertices = new List<Vector3>
            {
                new Vector3(-width * 0.5f, -height * 0.5f, z),
                new Vector3(width * 0.5f, -height * 0.5f, z),
                new Vector3(width * 0.5f, height * 0.5f, z),
                new Vector3(-width * 0.5f, height * 0.5f, z)
            };

            var uvs = new List<Vector2>
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };

            return CreateMesh(name, vertices, new List<int> { 0, 1, 2, 0, 2, 3 }, uvs);
        }

        private static Mesh CreateEllipseDiscMesh(string name, float radiusX, float radiusY, float z, float wobble, float seed)
        {
            var vertices = new List<Vector3>(ArcSegments + 1)
            {
                new Vector3(0f, 0f, z)
            };
            var triangles = new List<int>(ArcSegments * 3);
            var uvs = new List<Vector2>(ArcSegments + 1)
            {
                new Vector2(0.5f, 0.5f)
            };

            for (int index = 0; index < ArcSegments; index++)
            {
                float angle = Mathf.PI * 2f * index / ArcSegments;
                float ripple = Mathf.Sin(angle * 5.1f + seed) * wobble + Mathf.Sin(angle * 11.3f + seed * 1.7f) * wobble * 0.45f;
                float x = Mathf.Cos(angle) * radiusX * (1f + ripple);
                float y = Mathf.Sin(angle) * radiusY * (1f + ripple * 0.75f);
                vertices.Add(new Vector3(x, y, z));
                uvs.Add(new Vector2(x / (radiusX * 2f) + 0.5f, y / (radiusY * 2f) + 0.5f));
            }

            for (int index = 0; index < ArcSegments; index++)
            {
                triangles.Add(0);
                triangles.Add(1 + index);
                triangles.Add(1 + ((index + 1) % ArcSegments));
            }

            return CreateMesh(name, vertices, triangles, uvs);
        }

        private static Mesh CreateEllipseRingMesh(
            string name,
            float outerRadiusX,
            float outerRadiusY,
            float innerRadiusX,
            float innerRadiusY,
            float z,
            float wobble,
            float seed)
        {
            var vertices = new List<Vector3>(ArcSegments * 2);
            var triangles = new List<int>(ArcSegments * 6);
            var uvs = new List<Vector2>(ArcSegments * 2);

            for (int index = 0; index < ArcSegments; index++)
            {
                float angle = Mathf.PI * 2f * index / ArcSegments;
                float outerRipple = Mathf.Sin(angle * 4.7f + seed) * wobble + Mathf.Sin(angle * 10.1f + seed) * wobble * 0.4f;
                float innerRipple = Mathf.Sin(angle * 6.2f + seed * 1.4f) * wobble * 0.45f;

                vertices.Add(new Vector3(
                    Mathf.Cos(angle) * outerRadiusX * (1f + outerRipple),
                    Mathf.Sin(angle) * outerRadiusY * (1f + outerRipple * 0.8f),
                    z));
                vertices.Add(new Vector3(
                    Mathf.Cos(angle) * innerRadiusX * (1f + innerRipple),
                    Mathf.Sin(angle) * innerRadiusY * (1f + innerRipple * 0.8f),
                    z));
                uvs.Add(new Vector2(index / (float)ArcSegments, 1f));
                uvs.Add(new Vector2(index / (float)ArcSegments, 0f));
            }

            for (int index = 0; index < ArcSegments; index++)
            {
                int next = (index + 1) % ArcSegments;
                int outer = index * 2;
                int inner = outer + 1;
                int nextOuter = next * 2;
                int nextInner = nextOuter + 1;

                triangles.Add(outer);
                triangles.Add(nextOuter);
                triangles.Add(inner);
                triangles.Add(nextOuter);
                triangles.Add(nextInner);
                triangles.Add(inner);
            }

            return CreateMesh(name, vertices, triangles, uvs);
        }

        private static ParticleSystem CreatePortalParticleSystem(
            string name,
            Transform parent,
            Material material,
            int maxParticles,
            float emissionRate,
            float radius,
            Vector3 shapeScale,
            float minLifetime,
            float maxLifetime,
            float minSpeed,
            float maxSpeed,
            float minSize,
            float maxSize,
            Color colorA,
            Color colorB,
            float z)
        {
            GameObject particleObject = new GameObject(name);
            particleObject.transform.SetParent(parent, false);
            particleObject.transform.localPosition = new Vector3(0f, 0f, z);

            ParticleSystem particleSystem = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particleSystem.main;
            main.duration = 4f;
            main.loop = true;
            main.playOnAwake = true;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.gravityModifier = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);

            ParticleSystem.EmissionModule emission = particleSystem.emission;
            emission.rateOverTime = emissionRate;

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = radius;
            shape.radiusThickness = 0.18f;
            shape.scale = shapeScale;
            shape.arcMode = ParticleSystemShapeMultiModeValue.Random;

            ParticleSystem.SizeOverLifetimeModule size = particleSystem.sizeOverLifetime;
            size.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve(
                new Keyframe(0f, 0.15f),
                new Keyframe(0.25f, 1f),
                new Keyframe(1f, 0.08f));
            size.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortingOrder = 130;
            renderer.minParticleSize = 0.002f;
            renderer.maxParticleSize = 0.28f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.allowOcclusionWhenDynamic = false;

            particleSystem.Play();
            return particleSystem;
        }

        private static void ConfigureParticleFade(ParticleSystem particleSystem, Color color, float peakAlpha)
        {
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particleSystem.colorOverLifetime;
            colorOverLifetime.enabled = true;

            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(color, 0f),
                    new GradientColorKey(Color.white, 0.42f),
                    new GradientColorKey(color, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(peakAlpha, 0.28f),
                    new GradientAlphaKey(peakAlpha * 0.55f, 0.68f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        private static void ConfigureParticleMotion(ParticleSystem particleSystem, float orbitalVelocity, float radialVelocity, float noiseStrength, float noiseFrequency)
        {
            ParticleSystem.VelocityOverLifetimeModule velocity = particleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.orbitalZ = orbitalVelocity;
            velocity.radial = radialVelocity;

            ParticleSystem.RotationOverLifetimeModule rotation = particleSystem.rotationOverLifetime;
            rotation.enabled = true;
            rotation.z = new ParticleSystem.MinMaxCurve(-1.8f, 1.8f);

            ParticleSystem.NoiseModule noise = particleSystem.noise;
            noise.enabled = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;
            noise.strength = noiseStrength;
            noise.frequency = noiseFrequency;
            noise.scrollSpeed = 0.28f;
        }

        private static Mesh CreateArcStripMesh(
            string name,
            float radiusX,
            float radiusY,
            float startDegrees,
            float sweepDegrees,
            float width,
            float wobble,
            float z,
            float seed)
        {
            int segments = Mathf.Max(8, Mathf.CeilToInt(Mathf.Abs(sweepDegrees) / 5f));
            var centers = new Vector3[segments + 1];
            for (int index = 0; index <= segments; index++)
            {
                float normalized = index / (float)segments;
                float angle = (startDegrees + sweepDegrees * normalized) * Mathf.Deg2Rad;
                float ripple = Mathf.Sin(angle * 5.2f + seed) * wobble + Mathf.Sin(angle * 11.1f + seed * 1.9f) * wobble * 0.42f;
                float x = Mathf.Cos(angle) * radiusX * (1f + ripple);
                float y = Mathf.Sin(angle) * radiusY * (1f + ripple * 0.8f);
                centers[index] = new Vector3(x, y, z);
            }

            return CreateStripFromCenters(name, centers, width);
        }

        private static Mesh CreatePolylineStripMesh(Vector2[] points, float width, float z)
        {
            var centers = new Vector3[points.Length];
            for (int index = 0; index < points.Length; index++)
                centers[index] = new Vector3(points[index].x, points[index].y, z);

            return CreateStripFromCenters("StoryMintRiftLine", centers, width);
        }

        private static Mesh CreateStripFromCenters(string name, Vector3[] centers, float width)
        {
            var vertices = new List<Vector3>(centers.Length * 2);
            var triangles = new List<int>((centers.Length - 1) * 6);
            var uvs = new List<Vector2>(centers.Length * 2);

            for (int index = 0; index < centers.Length; index++)
            {
                Vector3 tangent;
                if (index == 0)
                    tangent = centers[1] - centers[0];
                else if (index == centers.Length - 1)
                    tangent = centers[index] - centers[index - 1];
                else
                    tangent = centers[index + 1] - centers[index - 1];

                Vector3 normal = new Vector3(-tangent.y, tangent.x, 0f).normalized * width * 0.5f;
                float u = index / (float)(centers.Length - 1);
                vertices.Add(centers[index] + normal);
                vertices.Add(centers[index] - normal);
                uvs.Add(new Vector2(u, 1f));
                uvs.Add(new Vector2(u, 0f));
            }

            for (int index = 0; index < centers.Length - 1; index++)
            {
                int a = index * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
                triangles.Add(b);
            }

            return CreateMesh(name, vertices, triangles, uvs);
        }

        private static Mesh CreateMesh(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            Mesh mesh = new Mesh
            {
                name = name
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Texture2D CreateRadialTexture(string name, float innerStrength, float outerRadius, float power, float noiseStrength)
        {
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = (x + 0.5f) / TextureSize * 2f - 1f;
                    float v = (y + 0.5f) / TextureSize * 2f - 1f;
                    float radius = Mathf.Sqrt(u * u + v * v);
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - radius / outerRadius), power);
                    float noise = Mathf.Sin((u * 17.3f + v * 9.7f) * Mathf.PI) * Mathf.Sin((u * 5.1f - v * 13.2f) * Mathf.PI);
                    alpha *= Mathf.Lerp(1f, 0.72f + Mathf.Abs(noise) * 0.28f, noiseStrength);
                    alpha = Mathf.Max(alpha, innerStrength * Mathf.Clamp01(1f - radius * 2.8f));
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateCoreTexture()
        {
            Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "StoryMintRiftCoreTexture",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    float u = (x + 0.5f) / TextureSize * 2f - 1f;
                    float v = (y + 0.5f) / TextureSize * 2f - 1f;
                    float ellipse = Mathf.Sqrt((u * u) + (v * v * 1.9f));
                    float angle = Mathf.Atan2(v, u);
                    float swirl = Mathf.Sin(angle * 3.7f + ellipse * 15.5f) * 0.07f;
                    float edge = Mathf.SmoothStep(1f, 0.72f, ellipse + swirl);
                    float center = Mathf.Clamp01(1f - ellipse * 1.8f) * 0.2f;
                    float alpha = Mathf.Clamp01(edge * 0.95f + center);
                    pixels[y * TextureSize + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateBandTexture(string name, float edgePower, float noiseStrength)
        {
            const int width = 128;
            const int height = 16;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                float soft = Mathf.Pow(Mathf.Sin(v * Mathf.PI), edgePower);
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    float noise = 1f - noiseStrength + Mathf.Sin(u * Mathf.PI * 18f) * noiseStrength;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(soft * noise));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Texture2D CreateSparkTexture(string name)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float radial = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v)), 3.6f);
                    float cross = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u) * 5.5f), 4f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v) * 0.75f), 1.7f);
                    cross += Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(v) * 5.5f), 4f) * Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(u) * 0.75f), 1.7f);
                    float alpha = Mathf.Clamp01(radial + cross * 0.85f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        private static Material CreateMaterial(string materialName, Color color, bool additive, float emissionBoost, int queueOffset, Texture2D texture)
        {
            Shader shader = texture != null ? Shader.Find("Universal Render Pipeline/Particles/Unlit") : null;
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("[StoryMint] Could not create rift material '" + materialName + "' because no compatible shader was found.");
                return null;
            }

            Material material = new Material(shader)
            {
                name = materialName
            };

            SetMaterialColor(material, color);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * emissionBoost);
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
                material.mainTexture = texture;
            }

            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);

            material.SetOverrideTag("RenderType", "Transparent");
            material.SetOverrideTag("Queue", "Transparent");

            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_SrcBlendAlpha"))
                material.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (material.HasProperty("_DstBlendAlpha"))
                material.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_AlphaClip"))
                material.SetFloat("_AlphaClip", 0f);
            if (material.HasProperty("_ReceiveShadows"))
                material.SetFloat("_ReceiveShadows", 0f);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);

            material.renderQueue = (int)RenderQueue.Transparent + 470 + queueOffset;
            material.SetShaderPassEnabled("DepthOnly", false);
            material.SetShaderPassEnabled("SHADOWCASTER", false);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material == null)
                return;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.color = color;
        }

        private static void PanMaterialTexture(Material material, float x, float y)
        {
            if (material == null)
                return;

            Vector2 offset = new Vector2(x, y);
            if (material.HasProperty("_BaseMap"))
                material.SetTextureOffset("_BaseMap", offset);
            if (material.HasProperty("_MainTex"))
                material.SetTextureOffset("_MainTex", offset);
        }
    }
}
