#if STORYMINT_IMAGINE
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace StoryMint.RiftHunt.Editor
{
    [InitializeOnLoad]
    public static class StoryMintImagineTargetAspectFixOnce
    {
        private const string EditorPrefKey = "StoryMintImagineTargetAspectFixOnce.v20260614_v81";

        private struct TargetMeshSpec
        {
            public readonly string texturePath;
            public readonly string meshPath;

            public TargetMeshSpec(string texturePath, string meshPath)
            {
                this.texturePath = texturePath;
                this.meshPath = meshPath;
            }
        }

        private static readonly TargetMeshSpec[] Specs =
        {
            new TargetMeshSpec(
                "Assets/Resources/StoryMint/Targets/berliner-mauer.jpg",
                "Assets/Imagine/ImageTracker/Imagetargets/Meshes/berliner-mauer Mesh.asset"),
            new TargetMeshSpec(
                "Assets/Resources/StoryMint/Targets/altes-tor.jpg",
                "Assets/Imagine/ImageTracker/Imagetargets/Meshes/altes-tor Mesh.asset"),
            new TargetMeshSpec(
                "Assets/Resources/StoryMint/Targets/schoene-holz-tuer.jpg",
                "Assets/Imagine/ImageTracker/Imagetargets/Meshes/schoene-holz-tuer Mesh.asset"),
            new TargetMeshSpec(
                "Assets/Resources/StoryMint/Targets/stein-evian.jpg",
                "Assets/Imagine/ImageTracker/Imagetargets/Meshes/stein-evian Mesh.asset")
        };

        static StoryMintImagineTargetAspectFixOnce()
        {
            EditorApplication.delayCall += Run;
        }

        [MenuItem("StoryMint/Imagine/Fix Target Aspect Ratios")]
        public static void ForceRun()
        {
            EditorPrefs.DeleteKey(EditorPrefKey);
            Run();
        }

        private static void Run()
        {
            if (EditorPrefs.GetBool(EditorPrefKey, false))
                return;

            EditorPrefs.SetBool(EditorPrefKey, true);

            foreach (TargetMeshSpec spec in Specs)
            {
                EnsureImportKeepsAspect(spec.texturePath);
                if (!TryReadImageSize(spec.texturePath, out int width, out int height))
                    continue;

                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(spec.meshPath);
                if (mesh == null)
                {
                    mesh = new Mesh { name = Path.GetFileNameWithoutExtension(spec.meshPath) };
                    AssetDatabase.CreateAsset(mesh, spec.meshPath);
                }

                ApplyTargetMeshData(mesh, width, height);
                EditorUtility.SetDirty(mesh);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[StoryMint] Imagine target mesh aspect ratios refreshed from source image sizes.");
        }

        private static void EnsureImportKeepsAspect(string texturePath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
                return;

            if (importer.npotScale == TextureImporterNPOTScale.None)
                return;

            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        private static bool TryReadImageSize(string texturePath, out int width, out int height)
        {
            width = 0;
            height = 0;

            string fullPath = Path.Combine(Directory.GetCurrentDirectory(), texturePath);
            if (!File.Exists(fullPath))
                return false;

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(fullPath), false))
                    return false;

                width = texture.width;
                height = texture.height;
                return width > 0 && height > 0;
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }

        private static void ApplyTargetMeshData(Mesh mesh, int textureWidth, int textureHeight)
        {
            float maxSide = Mathf.Max(textureWidth, textureHeight);
            float width = textureWidth / maxSide;
            float height = textureHeight / maxSide;

            mesh.Clear();
            mesh.vertices = new[]
            {
                new Vector3(-0.5f * width, -0.5f * height, 0f),
                new Vector3(0.5f * width, -0.5f * height, 0f),
                new Vector3(-0.5f * width, 0.5f * height, 0f),
                new Vector3(0.5f * width, 0.5f * height, 0f)
            };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 2, 1, 0, 2, 3, 1 };
            mesh.RecalculateBounds();
        }
    }

    internal sealed class StoryMintImagineTargetAspectBuildPreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            StoryMintImagineTargetAspectFixOnce.ForceRun();
        }
    }
}
#endif

#endif
