using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StoryMint.RiftHunt
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class StoryMintVisibleRiftMarker : MonoBehaviour
    {
        private const string RootName = "VisibleRiftMarkerRoot";
        private const int RingSegments = 28;

        private Transform markerRoot;
        private readonly List<Transform> animatedParts = new List<Transform>();
        private Vector3[] baseScales = new Vector3[0];
        private Vector3 markerRootBasePosition;

        private void Awake()
        {
            BuildIfNeeded();
        }

        private void OnEnable()
        {
            BuildIfNeeded();
        }

        private void Update()
        {
            if (markerRoot == null)
                return;

            float time = Application.isPlaying ? Time.time : (float)UnityEditorSafeTime();
            markerRoot.localPosition = markerRootBasePosition + Vector3.up * (Mathf.Sin(time * 2.2f) * 0.025f);

            for (int index = 0; index < animatedParts.Count; index++)
            {
                Transform part = animatedParts[index];
                if (part == null || index >= baseScales.Length)
                    continue;

                float pulse = 1f + Mathf.Sin(time * (2.8f + index * 0.2f) + index * 0.7f) * 0.035f;
                part.localScale = baseScales[index] * pulse;
            }
        }

        private void BuildIfNeeded()
        {
            Transform existingRoot = transform.Find(RootName);
            if (existingRoot != null)
            {
                if (HasUsableMeshes(existingRoot))
                {
                    markerRoot = existingRoot;
                    markerRootBasePosition = markerRoot.localPosition;
                    CacheAnimatedParts();
                    return;
                }

                DestroyExistingRoot(existingRoot.gameObject);
            }

            markerRoot = new GameObject(RootName).transform;
            markerRoot.SetParent(transform, false);
            markerRoot.localPosition = Vector3.zero;
            markerRoot.localRotation = Quaternion.identity;
            markerRoot.localScale = Vector3.one;
            markerRootBasePosition = markerRoot.localPosition;

            Material darkCore = CreateMaterial("StoryMintRift_DarkCore", new Color(0.02f, 0.05f, 0.08f, 1f), 3100);
            Material cyan = CreateMaterial("StoryMintRift_Cyan", new Color(0.05f, 1f, 0.95f, 1f), 3400);
            Material white = CreateMaterial("StoryMintRift_White", new Color(0.9f, 1f, 1f, 1f), 3450);
            Material blue = CreateMaterial("StoryMintRift_Blue", new Color(0.1f, 0.35f, 1f, 1f), 3420);
            Material orange = CreateMaterial("StoryMintRift_OrangeCrack", new Color(1f, 0.28f, 0.12f, 1f), 3500);

            AddDisc("DarkCore", new Vector2(0.46f, 0.27f), -0.035f, darkCore);
            AddJaggedRing("CyanOuterRing", new Vector2(0.78f, 0.43f), new Vector2(0.56f, 0.29f), 0f, cyan, 0.12f);
            AddBrokenRing("WhiteInnerArc", new Vector2(0.61f, 0.34f), new Vector2(0.55f, 0.29f), 0.02f, white);
            AddBrokenRing("BlueInnerArc", new Vector2(0.48f, 0.27f), new Vector2(0.44f, 0.23f), 0.03f, blue);

            AddPolyline(
                "OrangeRiftCrack",
                new[]
                {
                    new Vector2(0.02f, 0.34f),
                    new Vector2(0.12f, 0.16f),
                    new Vector2(0.02f, 0.02f),
                    new Vector2(0.10f, -0.15f),
                    new Vector2(-0.02f, -0.36f)
                },
                0.055f,
                0.055f,
                orange);

            AddPolyline(
                "CyanSideCrackLeft",
                new[] { new Vector2(-0.18f, 0.08f), new Vector2(-0.32f, -0.02f), new Vector2(-0.46f, -0.08f) },
                0.018f,
                0.065f,
                cyan);

            AddPolyline(
                "CyanSideCrackRight",
                new[] { new Vector2(0.16f, 0.16f), new Vector2(0.35f, 0.24f), new Vector2(0.48f, 0.19f) },
                0.018f,
                0.065f,
                cyan);

            AddAccentQuad("SparkLeft", new Vector2(-0.82f, 0.04f), new Vector2(0.075f, 0.075f), 0.08f, white, 8f);
            AddAccentQuad("SparkRight", new Vector2(0.82f, -0.04f), new Vector2(0.08f, 0.035f), 0.08f, cyan, -12f);
            AddAccentQuad("SparkTop", new Vector2(0.26f, 0.47f), new Vector2(0.18f, 0.025f), 0.08f, white, -28f);
            AddAccentQuad("SparkBottom", new Vector2(-0.18f, -0.48f), new Vector2(0.14f, 0.025f), 0.08f, cyan, 18f);

            CacheAnimatedParts();
        }

        private void AddDisc(string name, Vector2 radius, float z, Material material)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            vertices.Add(new Vector3(0f, 0f, z));

            for (int index = 0; index < RingSegments; index++)
            {
                float angle = Mathf.PI * 2f * index / RingSegments;
                vertices.Add(new Vector3(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y, z));
            }

            for (int index = 0; index < RingSegments; index++)
            {
                int current = index + 1;
                int next = index == RingSegments - 1 ? 1 : current + 1;
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(current);
            }

            AddMesh(name, vertices, triangles, material);
        }

        private void AddJaggedRing(string name, Vector2 outerRadius, Vector2 innerRadius, float z, Material material, float jaggedness)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int index = 0; index < RingSegments; index++)
            {
                float angle = Mathf.PI * 2f * index / RingSegments;
                float wobble = 1f + (index % 2 == 0 ? jaggedness : -jaggedness * 0.55f);
                float innerWobble = 1f + (index % 3 == 0 ? jaggedness * 0.35f : -jaggedness * 0.15f);
                vertices.Add(new Vector3(Mathf.Cos(angle) * outerRadius.x * wobble, Mathf.Sin(angle) * outerRadius.y * wobble, z));
                vertices.Add(new Vector3(Mathf.Cos(angle) * innerRadius.x * innerWobble, Mathf.Sin(angle) * innerRadius.y * innerWobble, z + 0.002f));
            }

            for (int index = 0; index < RingSegments; index++)
            {
                int outerCurrent = index * 2;
                int innerCurrent = outerCurrent + 1;
                int outerNext = ((index + 1) % RingSegments) * 2;
                int innerNext = outerNext + 1;

                triangles.Add(outerCurrent);
                triangles.Add(outerNext);
                triangles.Add(innerNext);
                triangles.Add(outerCurrent);
                triangles.Add(innerNext);
                triangles.Add(innerCurrent);
            }

            AddMesh(name, vertices, triangles, material);
        }

        private void AddBrokenRing(string name, Vector2 outerRadius, Vector2 innerRadius, float z, Material material)
        {
            for (int arc = 0; arc < 4; arc++)
            {
                float start = (arc * 90f + 12f) * Mathf.Deg2Rad;
                float end = (arc * 90f + 70f) * Mathf.Deg2Rad;
                AddArcMesh(name + "_" + arc, outerRadius, innerRadius, start, end, z + arc * 0.002f, material);
            }
        }

        private void AddArcMesh(string name, Vector2 outerRadius, Vector2 innerRadius, float start, float end, float z, Material material)
        {
            const int steps = 8;
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int index = 0; index <= steps; index++)
            {
                float angle = Mathf.Lerp(start, end, (float)index / steps);
                vertices.Add(new Vector3(Mathf.Cos(angle) * outerRadius.x, Mathf.Sin(angle) * outerRadius.y, z));
                vertices.Add(new Vector3(Mathf.Cos(angle) * innerRadius.x, Mathf.Sin(angle) * innerRadius.y, z + 0.002f));
            }

            for (int index = 0; index < steps; index++)
            {
                int outerCurrent = index * 2;
                int innerCurrent = outerCurrent + 1;
                int outerNext = outerCurrent + 2;
                int innerNext = outerCurrent + 3;
                triangles.Add(outerCurrent);
                triangles.Add(outerNext);
                triangles.Add(innerNext);
                triangles.Add(outerCurrent);
                triangles.Add(innerNext);
                triangles.Add(innerCurrent);
            }

            AddMesh(name, vertices, triangles, material);
        }

        private void AddPolyline(string name, Vector2[] points, float width, float z, Material material)
        {
            if (points == null || points.Length < 2)
                return;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int index = 0; index < points.Length - 1; index++)
            {
                Vector2 start = points[index];
                Vector2 end = points[index + 1];
                Vector2 direction = (end - start).normalized;
                Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
                int baseIndex = vertices.Count;

                vertices.Add(new Vector3(start.x + normal.x, start.y + normal.y, z));
                vertices.Add(new Vector3(start.x - normal.x, start.y - normal.y, z));
                vertices.Add(new Vector3(end.x + normal.x, end.y + normal.y, z));
                vertices.Add(new Vector3(end.x - normal.x, end.y - normal.y, z));

                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 2);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 1);
            }

            AddMesh(name, vertices, triangles, material);
        }

        private void AddAccentQuad(string name, Vector2 position, Vector2 size, float z, Material material, float rotationDegrees)
        {
            Transform accent = AddMesh(name, BuildQuadVertices(size, z), new List<int> { 0, 2, 1, 0, 3, 2 }, material);
            accent.localPosition = new Vector3(position.x, position.y, 0f);
            accent.localRotation = Quaternion.Euler(0f, 0f, rotationDegrees);
        }

        private static List<Vector3> BuildQuadVertices(Vector2 size, float z)
        {
            float halfWidth = size.x * 0.5f;
            float halfHeight = size.y * 0.5f;
            return new List<Vector3>
            {
                new Vector3(-halfWidth, -halfHeight, z),
                new Vector3(-halfWidth, halfHeight, z),
                new Vector3(halfWidth, halfHeight, z),
                new Vector3(halfWidth, -halfHeight, z)
            };
        }

        private Transform AddMesh(string name, List<Vector3> vertices, List<int> triangles, Material material)
        {
            GameObject meshObject = new GameObject(name);
            meshObject.transform.SetParent(markerRoot, false);

            Mesh mesh = new Mesh
            {
                name = name + "Mesh"
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            MeshFilter meshFilter = meshObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off;
            meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            return meshObject.transform;
        }

        private void CacheAnimatedParts()
        {
            animatedParts.Clear();
            if (markerRoot == null)
                return;

            for (int index = 0; index < markerRoot.childCount; index++)
                animatedParts.Add(markerRoot.GetChild(index));

            baseScales = new Vector3[animatedParts.Count];
            for (int index = 0; index < animatedParts.Count; index++)
                baseScales[index] = animatedParts[index] != null ? animatedParts[index].localScale : Vector3.one;
        }

        private static Material CreateMaterial(string materialName, Color color, int renderQueue)
        {
            Material resourceMaterial = Resources.Load<Material>("StoryMint/Materials/" + materialName);
            Material material = resourceMaterial != null
                ? new Material(resourceMaterial)
                : new Material(FindSafeShader());

            material.name = materialName;
            material.renderQueue = renderQueue;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * 2.6f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);

            material.EnableKeyword("_EMISSION");
            return material;
        }

        private static bool HasUsableMeshes(Transform root)
        {
            MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
            if (meshFilters == null || meshFilters.Length == 0)
                return false;

            for (int index = 0; index < meshFilters.Length; index++)
            {
                if (meshFilters[index] == null || meshFilters[index].sharedMesh == null)
                    return false;
            }

            return true;
        }

        private static void DestroyExistingRoot(GameObject target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
            {
                Destroy(target);
                return;
            }

            DestroyImmediate(target);
        }

        private static Shader FindSafeShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Standard");

            return shader;
        }


        private static double UnityEditorSafeTime()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorApplication.timeSinceStartup;
#else
            return 0d;
#endif
        }
    }
}
