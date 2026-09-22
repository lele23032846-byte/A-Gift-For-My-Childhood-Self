using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Reusable dreamcore presentation layer. It owns no chapter state and never modifies source models/materials.
/// </summary>
public sealed partial class MemoryPresentationController
{
    private WhiteBalance memoryWhiteBalance;
    private SplitToning memorySplitToning;
    private FullScreenPassRendererFeature memoryRevealFeature;
    private Material memoryRevealSourceMaterial;
    private Material memoryRevealRuntimeMaterial;

    private void BuildDreamcore()
    {
        memoryWhiteBalance = volume.profile.Add<WhiteBalance>(true);
        memoryWhiteBalance.temperature.overrideState = true;
        memoryWhiteBalance.tint.overrideState = true;

        memorySplitToning = volume.profile.Add<SplitToning>(true);
        memorySplitToning.shadows.overrideState = true;
        memorySplitToning.highlights.overrideState = true;
        memorySplitToning.balance.overrideState = true;

        foreach (FullScreenPassRendererFeature feature in Resources.FindObjectsOfTypeAll<FullScreenPassRendererFeature>())
        {
            if (feature == null || feature.name != "Memory Reveal" || feature.passMaterial == null)
            {
                continue;
            }

            memoryRevealFeature = feature;
            memoryRevealSourceMaterial = feature.passMaterial;
            memoryRevealRuntimeMaterial = new Material(memoryRevealSourceMaterial)
            {
                name = memoryRevealSourceMaterial.name + " (Runtime)"
            };
            feature.passMaterial = memoryRevealRuntimeMaterial;
            break;
        }

        ApplyDreamcore(0f);
    }

    private void ApplyDreamcore(float amount)
    {
        if (memoryWhiteBalance != null)
        {
            memoryWhiteBalance.temperature.value = Mathf.Lerp(-6f, 8f, amount);
            memoryWhiteBalance.tint.value = Mathf.Lerp(-2f, 0f, amount);
        }

        if (memorySplitToning != null)
        {
            memorySplitToning.shadows.value = Color.Lerp(
                new Color(0.44f, 0.47f, 0.52f),
                new Color(0.50f, 0.50f, 0.50f),
                amount);
            memorySplitToning.highlights.value = Color.Lerp(
                new Color(0.60f, 0.55f, 0.47f),
                new Color(0.56f, 0.53f, 0.49f),
                amount);
            memorySplitToning.balance.value = Mathf.Lerp(8f, 3f, amount);
        }

        if (memoryRevealRuntimeMaterial != null)
        {
            memoryRevealRuntimeMaterial.SetFloat("_Scene_Saturation", Mathf.Lerp(0.82f, 1f, amount));
            memoryRevealRuntimeMaterial.SetFloat("_Scene_Contrast", Mathf.Lerp(0.96f, 1f, amount));
            memoryRevealRuntimeMaterial.SetColor(
                "_Scene_Tint",
                Color.Lerp(new Color(0.97f, 0.985f, 1f, 1f), new Color(1f, 0.99f, 0.97f, 1f), amount));
        }
    }

    private void CleanupDreamcore()
    {
        if (memoryRevealFeature != null && memoryRevealFeature.passMaterial == memoryRevealRuntimeMaterial)
        {
            memoryRevealFeature.passMaterial = memoryRevealSourceMaterial;
        }

        if (memoryRevealRuntimeMaterial != null)
        {
            Destroy(memoryRevealRuntimeMaterial);
        }
    }
}
