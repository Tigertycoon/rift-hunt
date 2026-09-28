#if STORYMINT_IMAGINE
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Imagine.WebAR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StoryMint.RiftHunt.Editor
{
    [InitializeOnLoad]
    public static class StoryMintImagineTargetRegistrar
    {
        private const string AutoRunKey = "StoryMintImagineTargetRegistrar.v20260614c";
        private const string ImagineScenePath = "Assets/Scenes/Imagine-Scene.unity";
        private const string MaterialFolder = "Assets/Imagine/ImageTracker/Imagetargets/Materials";
        private const string MeshFolder = "Assets/Imagine/ImageTracker/Imagetargets/Meshes";

        private struct TargetSpec
        {
            public readonly string id;
            public readonly string texturePath;

            public TargetSpec(string id, string texturePath)
            {
                this.id = id;
                this.texturePath = texturePath;
            }
        }

        private static readonly TargetSpec[] TestTargets =
        {
            new TargetSpec("berliner-mauer", "Assets/Resources/StoryMint/Targets/berliner-mauer.jpg"),
            new TargetSpec("steinmauer-mit-treppe", "Assets/Resources/StoryMint/Targets/steinmauer-mit-treppe.jpg"),
            new TargetSpec("julius-menzer-gedenktafel", "Assets/Resources/StoryMint/Targets/julius-menzer-gedenktafel.jpg"),
            new TargetSpec("stein-evian", "Assets/Resources/StoryMint/Targets/stein-evian.jpg")
        };

        private static readonly HashSet<string> TestTargetIds = new HashSet<string>
        {
            "berliner-mauer",
            "steinmauer-mit-treppe",
            "julius-menzer-gedenktafel",
            "stein-evian"
        };

        static StoryMintImagineTargetRegistrar()
        {
            EditorApplication.delayCall += RunOnceAfterCompile;
        }

        [MenuItem("StoryMint/Imagine/Sync Test Targets")]
        public static void SyncTestTargets()
        {
            SyncTargets(true);
        }

        private static void RunOnceAfterCompile()
        {
            if (EditorPrefs.GetBool(AutoRunKey, false))
                return;

            EditorPrefs.SetBool(AutoRunKey, true);
            SyncTargets(false);
        }

        private static void SyncTargets(bool logSummary)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            bool changed = false;
            Dictionary<string, Texture2D> texturesById = LoadTextures();
            if (texturesById.Count == 0)
                return;

            changed |= EnsureGlobalTargetSettings(texturesById);
            changed |= EnsureTargetAssets(texturesById);
            changed |= EnsureImagineSceneTargets(texturesById);
            changed |= EnsureImagineBuildScene();

            if (changed)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            if (logSummary)
                Debug.Log("[StoryMint] Imagine test targets synced.");
        }

        private static Dictionary<string, Texture2D> LoadTextures()
        {
            Dictionary<string, Texture2D> texturesById = new Dictionary<string, Texture2D>();
            foreach (TargetSpec spec in TestTargets)
            {
                EnsureTextureImportSettings(spec.texturePath);

                Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(spec.texturePath);
                if (texture == null)
                {
                    Debug.LogWarning("[StoryMint] Missing Imagine test target texture: " + spec.texturePath);
                    continue;
                }

                texturesById[spec.id] = texture;
            }

            return texturesById;
        }

        private static void EnsureTextureImportSettings(string texturePath)
        {
            TextureImporter importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;
            if (importer.npotScale != TextureImporterNPOTScale.None)
            {
                importer.npotScale = TextureImporterNPOTScale.None;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }

        private static bool EnsureGlobalTargetSettings(Dictionary<string, Texture2D> texturesById)
        {
            ImageTrackerGlobalSettings settings = ImageTrackerGlobalSettings.Instance;
            if (settings == null)
            {
                Debug.LogWarning("[StoryMint] ImageTrackerGlobalSettings could not be loaded.");
                return false;
            }

            bool changed = false;
            if (settings.imageTargetInfos == null)
            {
                settings.imageTargetInfos = new List<ImageTargetInfo>();
                changed = true;
            }

            for (int index = settings.imageTargetInfos.Count - 1; index >= 0; index--)
            {
                ImageTargetInfo info = settings.imageTargetInfos[index];
                if (info == null || !TestTargetIds.Contains(info.id))
                {
                    settings.imageTargetInfos.RemoveAt(index);
                    changed = true;
                }
            }

            foreach (KeyValuePair<string, Texture2D> entry in texturesById)
            {
                ImageTargetInfo info = settings.imageTargetInfos.Find(candidate => candidate != null && candidate.id == entry.Key);
                if (info == null)
                {
                    settings.imageTargetInfos.Add(new ImageTargetInfo { id = entry.Key, texture = entry.Value });
                    changed = true;
                    continue;
                }

                if (info.texture != entry.Value)
                {
                    info.texture = entry.Value;
                    changed = true;
                }
            }

            if (changed)
                EditorUtility.SetDirty(settings);

            return changed;
        }

        private static bool EnsureTargetAssets(Dictionary<string, Texture2D> texturesById)
        {
            bool changed = false;
            EnsureFolder(MaterialFolder);
            EnsureFolder(MeshFolder);

            foreach (KeyValuePair<string, Texture2D> entry in texturesById)
            {
                string materialPath = MaterialFolder + "/" + entry.Key + " Material.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Unlit/Texture"));
                    material.mainTexture = entry.Value;
                    AssetDatabase.CreateAsset(material, materialPath);
                    changed = true;
                }
                else if (material.mainTexture != entry.Value)
                {
                    material.mainTexture = entry.Value;
                    EditorUtility.SetDirty(material);
                    changed = true;
                }

                string meshPath = MeshFolder + "/" + entry.Key + " Mesh.asset";
                Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (mesh == null)
                {
                    mesh = CreateTargetMesh(entry.Value);
                    mesh.name = entry.Key + " Mesh";
                    AssetDatabase.CreateAsset(mesh, meshPath);
                    changed = true;
                }
                else if (!DoesMeshMatchTexture(mesh, entry.Value))
                {
                    ApplyTargetMeshData(mesh, entry.Value);
                    EditorUtility.SetDirty(mesh);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool EnsureImagineSceneTargets(Dictionary<string, Texture2D> texturesById)
        {
            EditorSceneManager.SaveOpenScenes();

            Scene activeScene = SceneManager.GetActiveScene();
            bool openedImagineScene = activeScene.path != ImagineScenePath;
            if (openedImagineScene)
                EditorSceneManager.OpenScene(ImagineScenePath, OpenSceneMode.Single);

            ImageTracker tracker = UnityEngine.Object.FindObjectOfType<ImageTracker>();
            if (tracker == null)
            {
                Debug.LogWarning("[StoryMint] No ImageTracker found in Imagine-Scene.");
                return false;
            }

            bool changed = false;
            SerializedObject trackerObject = new SerializedObject(tracker);
            SerializedProperty imageTargets = trackerObject.FindProperty("imageTargets");

            // Force NARROW detect zone: better for the weaker natural-stone targets.
            SerializedProperty detectZone = trackerObject.FindProperty("trackerSettings.advancedSettings.detectZone");
            if (detectZone != null && detectZone.enumValueIndex != (int)AdvancedSettings.DetectZone.NARROW)
            {
                detectZone.enumValueIndex = (int)AdvancedSettings.DetectZone.NARROW;
                changed = true;
            }

            if (PruneImageTargetEntries(imageTargets))
                changed = true;

            if (PruneSceneTargetObjects(tracker.transform))
                changed = true;

            foreach (KeyValuePair<string, Texture2D> entry in texturesById)
            {
                Transform targetTransform = EnsureSceneTargetObject(tracker.transform, entry.Key);
                if (EnsureImageTargetEntry(imageTargets, entry.Key, targetTransform))
                    changed = true;
            }

            if (changed)
            {
                trackerObject.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            return changed || openedImagineScene;
        }

        private static bool PruneImageTargetEntries(SerializedProperty imageTargets)
        {
            bool changed = false;
            for (int index = imageTargets.arraySize - 1; index >= 0; index--)
            {
                SerializedProperty item = imageTargets.GetArrayElementAtIndex(index);
                string id = item.FindPropertyRelative("id").stringValue;
                if (TestTargetIds.Contains(id))
                    continue;

                imageTargets.DeleteArrayElementAtIndex(index);
                changed = true;
            }

            return changed;
        }

        private static bool PruneSceneTargetObjects(Transform trackerTransform)
        {
            bool changed = false;
            for (int index = trackerTransform.childCount - 1; index >= 0; index--)
            {
                Transform child = trackerTransform.GetChild(index);
                if (child == null || TestTargetIds.Contains(child.name))
                    continue;

                UnityEngine.Object.DestroyImmediate(child.gameObject);
                changed = true;
            }

            return changed;
        }

        private static Transform EnsureSceneTargetObject(Transform trackerTransform, string id)
        {
            Transform child = trackerTransform.Find(id);
            if (child == null)
            {
                GameObject targetObject = new GameObject(id);
                targetObject.transform.SetParent(trackerTransform, false);
                targetObject.transform.localPosition = new Vector3(trackerTransform.childCount - 1, 0f, 0f);
                child = targetObject.transform;
            }

            MeshFilter meshFilter = child.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = child.gameObject.AddComponent<MeshFilter>();

            MeshRenderer meshRenderer = child.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = child.gameObject.AddComponent<MeshRenderer>();

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + "/" + id + " Mesh.asset");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + id + " Material.mat");
            if (meshFilter.sharedMesh != mesh)
                meshFilter.sharedMesh = mesh;
            if (meshRenderer.sharedMaterial != material)
                meshRenderer.sharedMaterial = material;

            return child;
        }

        private static bool EnsureImageTargetEntry(SerializedProperty imageTargets, string id, Transform targetTransform)
        {
            for (int index = 0; index < imageTargets.arraySize; index++)
            {
                SerializedProperty item = imageTargets.GetArrayElementAtIndex(index);
                if (item.FindPropertyRelative("id").stringValue != id)
                    continue;

                SerializedProperty transformProperty = item.FindPropertyRelative("transform");
                if (transformProperty.objectReferenceValue == targetTransform)
                    return false;

                transformProperty.objectReferenceValue = targetTransform;
                return true;
            }

            int newIndex = imageTargets.arraySize;
            imageTargets.arraySize++;
            SerializedProperty newItem = imageTargets.GetArrayElementAtIndex(newIndex);
            newItem.FindPropertyRelative("id").stringValue = id;
            newItem.FindPropertyRelative("transform").objectReferenceValue = targetTransform;
            newItem.FindPropertyRelative("targetPos").vector3Value = Vector3.zero;
            newItem.FindPropertyRelative("targetRot").quaternionValue = Quaternion.identity;
            return true;
        }

        private static bool EnsureImagineBuildScene()
        {
            EditorBuildSettingsScene[] currentScenes = EditorBuildSettings.scenes;
            if (currentScenes.Length == 1 && currentScenes[0].path == ImagineScenePath && currentScenes[0].enabled)
                return false;

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ImagineScenePath, true)
            };
            return true;
        }

        private static Mesh CreateTargetMesh(Texture2D texture)
        {
            Mesh mesh = new Mesh();
            ApplyTargetMeshData(mesh, texture);
            return mesh;
        }

        private static bool DoesMeshMatchTexture(Mesh mesh, Texture2D texture)
        {
            if (mesh == null || texture == null)
                return false;

            float maxSide = Mathf.Max(texture.width, texture.height);
            Vector3 boundsSize = mesh.bounds.size;
            return Mathf.Abs(boundsSize.x - texture.width / maxSide) < 0.001f
                && Mathf.Abs(boundsSize.y - texture.height / maxSide) < 0.001f;
        }

        private static void ApplyTargetMeshData(Mesh mesh, Texture2D texture)
        {
            float maxSide = Mathf.Max(texture.width, texture.height);
            float width = texture.width / maxSide;
            float height = texture.height / maxSide;

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

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            string parent = Path.GetDirectoryName(assetFolder)?.Replace("\\", "/");
            string folderName = Path.GetFileName(assetFolder);
            if (!string.IsNullOrWhiteSpace(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
#endif

#endif
