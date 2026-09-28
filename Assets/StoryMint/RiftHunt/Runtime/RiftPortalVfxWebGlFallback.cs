using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StoryMint.RiftHunt
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class RiftPortalVfxWebGlFallback : MonoBehaviour
    {
        private const int SegmentCount = 36;
        private Transform fallbackRoot;
        private readonly List<Transform> pulseTransforms = new List<Transform>();
        private Vector3[] baseScales = new Vector3[0];

        private void Awake()
        {
            BuildFallback();
        }

        private void OnEnable()
        {
            BuildFallback();
            ForceParticlesToPlay();
        }

        private void Update()
        {
            if (fallbackRoot == null || pulseTransforms.Count == 0)
                return;

            float time = Time.time;
            fallbackRoot.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(time * 0.85f) * 5f);

            for (int i = 0; i < pulseTransforms.Count && i < baseScales.Length; i++)
            {
                float pulse = 1f + Mathf.Sin(time * (2.8f + i * 0.8f) + i * 0.5f) * (0.025f + i * 0.014f);
                pulseTransforms[i].localScale = baseScales[i] * pulse;
            }
        }

        private void ForceParticlesToPlay()
        {
            ParticleSystem[] particleSystems = GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particles = particleSystems[i];
                if (particles == null)
                    continue;

                particles.gameObject.SetActive(true);
                particles.Clear(true);
                particles.Play(true);
            }
        }

        private void BuildFallback()
        {
            if (fallbackRoot != null)
                return;

            Transform existingRoot = transform.Find("StoryMintWebGLFallback");
            if (existingRoot != null)
            {
                fallbackRoot = existingRoot;
                CachePulseTransforms();
                return;
            }

            fallbackRoot = new GameObject("StoryMintWebGLFallback").transform;
            fallbackRoot.SetParent(transform, false);
            fallbackRoot.localPosition = new Vector3(0f, 0f, 0.015f);
            fallbackRoot.localRotation = Quaternion.identity;
            fallbackRoot.localScale = Vector3.one;

            Material auraMaterial = CreateMaterial("StoryMintVfxFallbackAura", new Color(0.03f, 0.72f, 1f, 0.14f), true, 3000);
            Material rimMaterial = CreateMaterial("StoryMintVfxFallbackRim", new Color(0.12f, 1f, 0.9f, 0.48f), true, 3001);
            Material hotMaterial = CreateMaterial("StoryMintVfxFallbackHotEdge", new Color(1f, 0.32f, 0.16f, 0.48f), true, 3002);
            Material coreMaterial = CreateMaterial("StoryMintVfxFallbackCore", new Color(0.01f, 0.012f, 0.045f, 0.52f), false, 3000);

            AddDisc("FallbackAura", 0.38f, 0.62f, -0.035f, auraMaterial);
            AddDisc("FallbackCore", 0.18f, 0.48f, -0.02f, coreMaterial);
            AddRing("FallbackRim", 0.34f, 0.58f, 0.22f, 0.42f, 0.01f, rimMaterial);
            AddRing("FallbackHotEdge", 0.25f, 0.48f, 0.19f, 0.36f, 0.03f, hotMaterial);
            AddCrack("FallbackHotCrack", hotMaterial);

            CachePulseTransforms();
        }

        private void AddDisc(string objectName, float radiusX, float radiusY, float z, Material material)
        {
            AddMeshRenderer(objectName, CreateDiscMesh(radiusX, radiusY, z), material);
        }

        private void AddRing(string objectName, float outerRadiusX, float outerRadiusY, float innerRadiusX, float innerRadiusY, float z, Material material)
        {
            AddMeshRenderer(objectName, CreateRingMesh(outerRadiusX, outerRadiusY, innerRadiusX, innerRadiusY, z), material);
        }

        private void AddCrack(string objectName, Material material)
        {
            var points = new[]
            {
                new Vector2(-0.06f, 0.31f),
                new Vector2(0.05f, 0.12f),
                new Vector2(-0.03f, -0.04f),
                new Vector2(0.08f, -0.33f)
            };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < points.Length - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                Vector2 direction = (b - a).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x) * 0.018f;
                int start = vertices.Count;
                vertices.Add(new Vector3(a.x - normal.x, a.y - normal.y, 0.055f));
                vertices.Add(new Vector3(a.x + normal.x, a.y + normal.y, 0.055f));
                vertices.Add(new Vector3(b.x + normal.x, b.y + normal.y, 0.055f));
                vertices.Add(new Vector3(b.x - normal.x, b.y - normal.y, 0.055f));
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }

            AddMeshRenderer(objectName, CreateMesh(objectName + "Mesh", vertices, triangles), material);
        }

        private void AddMeshRenderer(string objectName, Mesh mesh, Material material)
        {
            GameObject meshObject = new GameObject(objectName);
            meshObject.transform.SetParent(fallbackRoot, false);
            MeshFilter filter = meshObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = meshObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void CachePulseTransforms()
        {
            pulseTransforms.Clear();
            if (fallbackRoot == null)
                return;

            for (int i = 0; i < fallbackRoot.childCount; i++)
                pulseTransforms.Add(fallbackRoot.GetChild(i));

            baseScales = new Vector3[pulseTransforms.Count];
            for (int i = 0; i < pulseTransforms.Count; i++)
                baseScales[i] = pulseTransforms[i].localScale;
        }

        private static Mesh CreateRingMesh(float outerRadiusX, float outerRadiusY, float innerRadiusX, float innerRadiusY, float z)
        {
            var vertices = new List<Vector3>(SegmentCount * 2);
            var triangles = new List<int>(SegmentCount * 6);
            for (int i = 0; i < SegmentCount; i++)
            {
                float angle = Mathf.PI * 2f * i / SegmentCount;
                float edge = 1f + Mathf.Sin(i * 2.7f) * 0.08f + Mathf.Sin(i * 5.1f) * 0.035f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * outerRadiusX * edge, Mathf.Sin(angle) * outerRadiusY * edge, z));
                vertices.Add(new Vector3(Mathf.Cos(angle) * innerRadiusX, Mathf.Sin(angle) * innerRadiusY, z + 0.01f));
            }

            for (int i = 0; i < SegmentCount; i++)
            {
                int next = (i + 1) % SegmentCount;
                int outer = i * 2;
                int inner = outer + 1;
                int nextOuter = next * 2;
                int nextInner = nextOuter + 1;
                triangles.Add(outer);
                triangles.Add(inner);
                triangles.Add(nextInner);
                triangles.Add(outer);
                triangles.Add(nextInner);
                triangles.Add(nextOuter);
            }

            return CreateMesh("StoryMintVfxFallbackRing", vertices, triangles);
        }

        private static Mesh CreateDiscMesh(float radiusX, float radiusY, float z)
        {
            var vertices = new List<Vector3>(SegmentCount + 1) { new Vector3(0f, 0f, z) };
            var triangles = new List<int>(SegmentCount * 3);
            for (int i = 0; i < SegmentCount; i++)
            {
                float angle = Mathf.PI * 2f * i / SegmentCount;
                float edge = 1f + Mathf.Sin(i * 2.1f) * 0.065f + Mathf.Sin(i * 4.4f) * 0.025f;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radiusX * edge, Mathf.Sin(angle) * radiusY * edge, z));
            }

            for (int i = 1; i <= SegmentCount; i++)
            {
                int next = i == SegmentCount ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(next);
            }

            return CreateMesh("StoryMintVfxFallbackDisc", vertices, triangles);
        }

        private static Mesh CreateMesh(string meshName, List<Vector3> vertices, List<int> triangles)
        {
            Mesh mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateMaterial(string materialName, Color color, bool additive, int renderQueue)
        {
            Material source = Resources.Load<Material>("StoryMint/Materials/mat-white");
            Material material;
            if (source != null)
            {
                material = new Material(source) { name = materialName };
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                material = shader != null ? new Material(shader) { name = materialName } : new Material(Shader.Find("Sprites/Default"));
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.color = color;
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * 2.4f);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);

            material.renderQueue = renderQueue;
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_EMISSION");
            return material;
        }
    }
}
