using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CompetitionVisualPolishSetup
{
    private const string ScenePath = "Assets/Game/Scenes/SC_Bedroom.unity";
    private const string TripoRoot = "Assets/TripoAssets";
    private const string GeneratedRoot = "Assets/Generated/VisualPolish";
    private const string PackedRoot = GeneratedRoot + "/PackedMaps";
    private const string LightingAssetPath = GeneratedRoot + "/BedroomLightingSettings.lighting";
    private const string MarkerPath = GeneratedRoot + "/setup-v1-complete.txt";
    private const string ReflectionCubemapPath = GeneratedRoot + "/BedroomReflection.exr";

    private static bool running;

    static CompetitionVisualPolishSetup()
    {
        EditorApplication.delayCall += AutoApplyOnce;
    }

    [MenuItem("Tools/Competition/Apply Visual Polish 1-3")]
    public static void ApplyFromMenu()
    {
        Apply(force: true);
    }

    private static void AutoApplyOnce()
    {
        if (!File.Exists(MarkerPath))
            Apply(force: false);
    }

    private static void Apply(bool force)
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        if (!force && File.Exists(MarkerPath))
            return;

        running = true;
        try
        {
            Directory.CreateDirectory(PackedRoot);

            int packedCount = GenerateAndAssignPackedMaps();
            int cameraCount;
            int staticRendererCount;
            int lightCount;
            bool addedProbe;
            ConfigureBedroomScene(out cameraCount, out staticRendererCount, out lightCount, out addedProbe);

            Directory.CreateDirectory(GeneratedRoot);
            File.WriteAllText(
                MarkerPath,
                $"Competition visual polish setup completed at {DateTime.Now:O}\n" +
                $"Packed materials: {packedCount}\n" +
                $"SMAA cameras: {cameraCount}\n" +
                $"GI renderers: {staticRendererCount}\n" +
                $"Mixed lights: {lightCount}\n" +
                $"Reflection probe added: {addedProbe}\n");

            AssetDatabase.ImportAsset(MarkerPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CompetitionVisualPolish] COMPLETE | packed={packedCount}, cameras={cameraCount}, GI renderers={staticRendererCount}, mixed lights={lightCount}, reflectionProbeAdded={addedProbe}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            running = false;
        }
    }

    private static int GenerateAndAssignPackedMaps()
    {
        var materialGuids = AssetDatabase.FindAssets("t:Material", new[] { TripoRoot });
        var assignments = new List<(Material material, string packedPath)>();

        foreach (string guid in materialGuids)
        {
            string materialPath = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null || material.shader == null || !material.shader.name.Contains("Universal Render Pipeline/Lit"))
                continue;

            string directory = Path.GetDirectoryName(materialPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(directory))
                continue;

            string[] imageFiles = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedImage)
                .ToArray();
            string metallicPath = imageFiles.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path).EndsWith("_Metallic", StringComparison.OrdinalIgnoreCase));
            string roughnessPath = imageFiles.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path).EndsWith("_Roughness", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(metallicPath) || string.IsNullOrEmpty(roughnessPath))
                continue;

            Texture2D metallic = LoadImageLinear(metallicPath);
            Texture2D roughness = LoadImageLinear(roughnessPath);
            if (metallic == null || roughness == null)
                continue;

            int width = Mathf.Min(Mathf.Max(metallic.width, roughness.width), 2048);
            int height = Mathf.Min(Mathf.Max(metallic.height, roughness.height), 2048);
            var packed = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
            var pixels = new Color32[width * height];

            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    float u = (x + 0.5f) / width;
                    byte metal = (byte)Mathf.RoundToInt(metallic.GetPixelBilinear(u, v).r * 255f);
                    byte smoothness = (byte)Mathf.RoundToInt((1f - roughness.GetPixelBilinear(u, v).r) * 255f);
                    pixels[y * width + x] = new Color32(metal, metal, metal, smoothness);
                }
            }

            packed.SetPixels32(pixels);
            packed.Apply(false, false);
            string outputPath = $"{PackedRoot}/{SanitizeFileName(material.name)}_MetallicSmoothness.png";
            File.WriteAllBytes(outputPath, packed.EncodeToPNG());
            assignments.Add((material, outputPath));

            UnityEngine.Object.DestroyImmediate(metallic);
            UnityEngine.Object.DestroyImmediate(roughness);
            UnityEngine.Object.DestroyImmediate(packed);
        }

        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        foreach ((Material material, string packedPath) in assignments)
        {
            if (AssetImporter.GetAtPath(packedPath) is TextureImporter importer)
            {
                importer.sRGBTexture = false;
                importer.alphaSource = TextureImporterAlphaSource.FromInput;
                importer.mipmapEnabled = true;
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.maxTextureSize = 2048;
                importer.SaveAndReimport();
            }

            Texture2D packedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(packedPath);
            material.SetTexture("_MetallicGlossMap", packedTexture);
            material.SetFloat("_WorkflowMode", 1f);
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
            material.SetFloat("_SmoothnessTextureChannel", 0f);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            EditorUtility.SetDirty(material);
        }

        return assignments.Count;
    }

    private static void ConfigureBedroomScene(out int cameraCount, out int staticRendererCount, out int lightCount, out bool addedProbe)
    {
        Scene targetScene = SceneManager.GetSceneByPath(ScenePath);
        bool openedForSetup = !targetScene.IsValid() || !targetScene.isLoaded;
        if (openedForSetup)
            targetScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);

        LightingSettings lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingAssetPath);
        if (lightingSettings == null)
        {
            lightingSettings = new LightingSettings();
            AssetDatabase.CreateAsset(lightingSettings, LightingAssetPath);
        }

        lightingSettings.bakedGI = true;
        lightingSettings.realtimeGI = false;
        lightingSettings.mixedBakeMode = MixedLightingMode.IndirectOnly;
        lightingSettings.lightmapper = LightingSettings.Lightmapper.UnityComputeGPU;
        lightingSettings.lightmapResolution = 30f;
        lightingSettings.lightmapMaxSize = 2048;
        lightingSettings.directSampleCount = 64;
        lightingSettings.indirectSampleCount = 256;
        lightingSettings.environmentSampleCount = 128;
        lightingSettings.maxBounces = 2;
        lightingSettings.ao = true;
        lightingSettings.aoMaxDistance = 1.5f;
        lightingSettings.aoExponentIndirect = 1.2f;
        lightingSettings.indirectScale = 1.15f;
        lightingSettings.albedoBoost = 1f;
        EditorUtility.SetDirty(lightingSettings);
        Lightmapping.lightingSettings = lightingSettings;

        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 0.85f;
        RenderSettings.reflectionIntensity = 1f;

        cameraCount = 0;
        staticRendererCount = 0;
        lightCount = 0;
        var environmentBounds = new Bounds();
        bool hasBounds = false;

        bool punctualShadowAssigned = false;
        foreach (GameObject root in targetScene.GetRootGameObjects())
        {
            foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
            {
                UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = true;
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cameraData.antialiasingQuality = AntialiasingQuality.High;
                EditorUtility.SetDirty(cameraData);
                cameraCount++;
            }

            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                light.lightmapBakeType = LightmapBakeType.Mixed;
                if (light.type == LightType.Directional)
                {
                    light.shadows = LightShadows.Soft;
                }
                else if (!punctualShadowAssigned)
                {
                    light.shadows = LightShadows.Soft;
                    punctualShadowAssigned = true;
                }
                else
                {
                    light.shadows = LightShadows.None;
                }
                EditorUtility.SetDirty(light);
                lightCount++;
            }

            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsTripoEnvironmentRenderer(renderer))
                    continue;

                GameObject go = renderer.gameObject;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(go);
                flags |= StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
                GameObjectUtility.SetStaticEditorFlags(go, flags);
                renderer.receiveGI = ReceiveGI.Lightmaps;
                renderer.scaleInLightmap = 1f;
                EditorUtility.SetDirty(renderer);
                staticRendererCount++;

                if (!hasBounds)
                {
                    environmentBounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    environmentBounds.Encapsulate(renderer.bounds);
                }
            }
        }

        ReflectionProbe probe = targetScene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ReflectionProbe>(true))
            .FirstOrDefault(existing => existing.name == "RP_Bedroom_Auto");
        addedProbe = probe == null;
        if (probe == null)
        {
            var probeObject = new GameObject("RP_Bedroom_Auto");
            SceneManager.MoveGameObjectToScene(probeObject, targetScene);
            probe = probeObject.AddComponent<ReflectionProbe>();
        }

        if (hasBounds)
        {
            Vector3 clampedSize = new Vector3(
                Mathf.Clamp(environmentBounds.size.x, 3f, 30f),
                Mathf.Clamp(environmentBounds.size.y, 2.5f, 12f),
                Mathf.Clamp(environmentBounds.size.z, 3f, 30f));
            probe.transform.position = environmentBounds.center;
            probe.size = clampedSize;
        }

        probe.mode = ReflectionProbeMode.Baked;
        probe.resolution = 256;
        probe.hdr = true;
        probe.boxProjection = true;
        probe.blendDistance = 1f;
        probe.importance = 1;
        EditorUtility.SetDirty(probe);

        EditorSceneManager.MarkSceneDirty(targetScene);
        EditorSceneManager.SaveScene(targetScene);
        AssetDatabase.SaveAssets();

        try
        {
            Lightmapping.BakeReflectionProbe(probe, ReflectionCubemapPath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[CompetitionVisualPolish] Reflection probe bake will need a manual Lighting bake: {exception.Message}");
        }

        if (openedForSetup)
            EditorSceneManager.CloseScene(targetScene, true);
    }

    private static bool IsTripoEnvironmentRenderer(MeshRenderer renderer)
    {
        if (renderer.GetComponentInParent<Animator>() != null)
            return false;

        if (renderer.GetComponentsInParent<MonoBehaviour>(true).Any(component =>
            component != null && (component.GetType().Name.Contains("Pickup") || component.GetType().Name.Contains("Placement"))))
            return false;

        MeshFilter filter = renderer.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return false;

        string meshPath = AssetDatabase.GetAssetPath(filter.sharedMesh);
        return meshPath.StartsWith(TripoRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static Texture2D LoadImageLinear(string path)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false))
        {
            UnityEngine.Object.DestroyImmediate(texture);
            return null;
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        return texture;
    }

    private static bool IsSupportedImage(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return name;
    }
}
