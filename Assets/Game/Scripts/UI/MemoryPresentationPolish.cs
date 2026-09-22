using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public sealed partial class MemoryPresentationController
{
    private void BuildPolish()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            mainCamera = FindAnyObjectByType<Camera>();
        }
        if (mainCamera != null)
        {
            UniversalAdditionalCameraData data = mainCamera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.volumeLayerMask |= 1;
        }

        Bloom bloom = volume.profile.Add<Bloom>(true);
        bloom.threshold.value = 1.1f;
        bloom.intensity.value = 0.14f;
        bloom.scatter.value = 0.55f;

        Vignette vignette = volume.profile.Add<Vignette>(true);
        vignette.intensity.value = 0.10f;
        vignette.smoothness.value = 0.68f;

        FilmGrain grain = volume.profile.Add<FilmGrain>(true);
        grain.type.value = FilmGrainLookup.Thin1;
        grain.intensity.value = 0.045f;
        grain.response.value = 0.8f;

        Tonemapping tone = volume.profile.Add<Tonemapping>(true);
        tone.mode.value = TonemappingMode.Neutral;

        BuildDreamcore();

        foreach (TMP_Text text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            MemoryUI.ApplyTypography(text, text.fontSize, text.color, text.fontStyle);
        }
    }

    private void TickPolish()
    {
    }

    private void CleanupPolish()
    {
        CleanupDreamcore();
        if (volume != null && volume.HasInstantiatedProfile())
        {
            Destroy(volume.profile);
        }
    }
}
