using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Scene presentation and the short chapter objective toast.
/// Interaction decisions remain in PlayerInteractor and chapter state remains in ChapterManager.
/// </summary>
public sealed partial class MemoryPresentationController : MonoBehaviour
{
    private static readonly Color Paper = new Color32(233, 228, 218, 255);
    private static readonly Color Ink = new Color32(73, 70, 64, 255);
    private static readonly Color Muted = new Color32(133, 128, 120, 255);
    private static readonly Color Line = new Color32(184, 177, 165, 255);
    private static readonly Color Memory = new Color32(141, 156, 134, 255);
    private static readonly Color Accent = new Color32(194, 155, 113, 255);

    private ChapterManager chapterManager;
    private Canvas canvas;
    private CanvasGroup objectiveGroup;
    private TMP_Text chapterLabel;
    private TMP_Text objectiveLabel;
    private Volume volume;
    private ColorAdjustments colorAdjustments;
    private Light memoryLight;
    private Light[] sceneLights = Array.Empty<Light>();
    private float[] baseLightIntensities = Array.Empty<float>();
    private Color[] baseLightColors = Array.Empty<Color>();
    private float targetRestoration;
    private float restoration;
    private float presentationTime;
    private float objectiveToastTime = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnMemorySceneLoaded;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnMemorySceneLoaded;
        if (FindAnyObjectByType<ChapterManager>() == null || FindAnyObjectByType<MemoryPresentationController>() != null)
        {
            return;
        }

        GameObject root = new GameObject("MemoryPresentation");
        root.AddComponent<MemoryPresentationController>();
    }

    private static void OnMemorySceneLoaded(
        UnityEngine.SceneManagement.Scene scene,
        UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        Bootstrap();
    }

    private void Awake()
    {
        BuildPresentation();

        if (GetComponent<MemoryCardPresenter>() == null)
        {
            gameObject.AddComponent<MemoryCardPresenter>();
        }

        // Build and open the title layer before the first rendered frame.
        if (GetComponent<MemoryMenu>() == null)
        {
            gameObject.AddComponent<MemoryMenu>();
        }
    }

    private void Start()
    {
        BindChapterManager();
        TickPolish();
    }

    private void Update()
    {
        if (chapterManager == null)
        {
            BindChapterManager();
        }

        presentationTime += Time.unscaledDeltaTime;
        restoration = Mathf.MoveTowards(restoration, targetRestoration, Time.deltaTime * 0.22f);
        ApplyAtmosphere(restoration);
        UpdateObjectiveToast();
        TickPolish();
    }

    private void OnDestroy()
    {
        CleanupPolish();
        if (chapterManager != null)
        {
            chapterManager.ChapterStateChanged -= OnChapterStateChanged;
        }
    }

    private void BindChapterManager()
    {
        ChapterManager found = FindAnyObjectByType<ChapterManager>();
        if (found == chapterManager)
        {
            return;
        }

        if (chapterManager != null)
        {
            chapterManager.ChapterStateChanged -= OnChapterStateChanged;
        }

        chapterManager = found;
        if (chapterManager == null)
        {
            return;
        }

        chapterManager.ChapterStateChanged += OnChapterStateChanged;
        RefreshChapterUI();
        ShowObjectiveToast();
    }

    private void OnChapterStateChanged(ChapterId chapter, ChapterState state)
    {
        RefreshChapterUI();
        ShowObjectiveToast();
    }

    private void RefreshChapterUI()
    {
        if (chapterManager == null || chapterLabel == null || objectiveLabel == null)
        {
            return;
        }

        bool complete = chapterManager.IsChapterCompleted(ChapterId.LostPet);
        chapterLabel.text = complete ? "MEMORY 01  /  已归还" : "CHAPTER 01  /  遗失的小盒子";
        objectiveLabel.text = complete ? "它一直在这里，等你回来。" : "把小盒子，放回鱼缸旁。";
        targetRestoration = complete ? 1f : 0f;
    }

    private void ShowObjectiveToast()
    {
        objectiveToastTime = 0f;
        if (objectiveGroup != null)
        {
            objectiveGroup.alpha = 0f;
        }
    }

    private void UpdateObjectiveToast()
    {
        if (objectiveGroup == null || objectiveToastTime < 0f || canvas == null || !canvas.enabled)
        {
            return;
        }

        objectiveToastTime += Time.unscaledDeltaTime;
        const float fadeIn = 0.35f;
        const float holdEnd = 4.2f;
        const float fadeOut = 0.7f;

        if (objectiveToastTime < fadeIn)
        {
            objectiveGroup.alpha = objectiveToastTime / fadeIn;
        }
        else if (objectiveToastTime < holdEnd)
        {
            objectiveGroup.alpha = 1f;
        }
        else
        {
            objectiveGroup.alpha = 1f - Mathf.Clamp01((objectiveToastTime - holdEnd) / fadeOut);
            if (objectiveToastTime >= holdEnd + fadeOut)
            {
                objectiveGroup.alpha = 0f;
                objectiveToastTime = -1f;
            }
        }
    }

    private void BuildPresentation()
    {
        GameObject canvasObject = new GameObject(
            "MemoryHUD",
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        UnityEngine.UI.CanvasScaler scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        canvasRect.localScale = Vector3.one;

        RectTransform washRect = CreateRect("AtmosphereWash", canvasObject.transform, Vector2.zero, Vector2.one);
        UnityEngine.UI.Image wash = washRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        wash.color = new Color(0.16f, 0.18f, 0.21f, 0.018f);
        wash.raycastTarget = false;

        RectTransform objective = CreateRect(
            "ObjectiveToast",
            canvasObject.transform,
            new Vector2(0.675f, 0.825f),
            new Vector2(0.965f, 0.955f));
        objectiveGroup = objective.gameObject.AddComponent<CanvasGroup>();
        objectiveGroup.alpha = 0f;
        objectiveGroup.interactable = false;
        objectiveGroup.blocksRaycasts = false;

        UnityEngine.UI.Image objectivePaper = objective.gameObject.AddComponent<UnityEngine.UI.Image>();
        objectivePaper.color = new Color(Paper.r, Paper.g, Paper.b, 0.96f);
        objectivePaper.raycastTarget = false;
        UnityEngine.UI.Shadow objectiveShadow = objective.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        objectiveShadow.effectColor = new Color(0.06f, 0.05f, 0.04f, 0.30f);
        objectiveShadow.effectDistance = new Vector2(5f, -6f);
        UnityEngine.UI.Outline objectiveOutline = objective.gameObject.AddComponent<UnityEngine.UI.Outline>();
        objectiveOutline.effectColor = new Color(Line.r, Line.g, Line.b, 0.62f);
        objectiveOutline.effectDistance = new Vector2(1f, -1f);
        MemoryUI.AddPaperGrain(objective, 0.16f, 3.5f);

        RectTransform memoryMark = CreateRect("MemoryMark", objective, new Vector2(0.035f, 0.18f), new Vector2(0.043f, 0.82f));
        UnityEngine.UI.Image memoryMarkImage = memoryMark.gameObject.AddComponent<UnityEngine.UI.Image>();
        memoryMarkImage.color = Memory;
        memoryMarkImage.raycastTarget = false;

        RectTransform rule = CreateRect("Rule", objective, new Vector2(0.085f, 0.61f), new Vector2(0.93f, 0.618f));
        UnityEngine.UI.Image ruleImage = rule.gameObject.AddComponent<UnityEngine.UI.Image>();
        ruleImage.color = new Color(Line.r, Line.g, Line.b, 0.72f);
        ruleImage.raycastTarget = false;

        chapterLabel = AddText(
            objective,
            "ChapterLabel",
            "CHAPTER 01  /  遗失的小盒子",
            15,
            Muted,
            new Vector2(0.085f, 0.64f),
            new Vector2(0.93f, 0.88f));
        chapterLabel.characterSpacing = 1.8f;
        objectiveLabel = AddText(
            objective,
            "Objective",
            "把小盒子，放回鱼缸旁。",
            22,
            Ink,
            new Vector2(0.085f, 0.16f),
            new Vector2(0.93f, 0.56f));

        CreateAmbientLight();
        CreateColorVolume();
        EnsureExistingCanvases();
        StyleExistingInventory();
        BuildPolish();
    }

    private void CreateAmbientLight()
    {
        Light[] allLights = FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        List<Light> lights = new List<Light>();
        List<float> intensities = new List<float>();
        List<Color> colors = new List<Color>();

        foreach (Light sceneLight in allLights)
        {
            if (sceneLight == null || sceneLight.type == LightType.Rectangle)
            {
                continue;
            }

            lights.Add(sceneLight);
            intensities.Add(sceneLight.intensity);
            colors.Add(sceneLight.color);
        }

        sceneLights = lights.ToArray();
        baseLightIntensities = intensities.ToArray();
        baseLightColors = colors.ToArray();

        GameObject lightObject = new GameObject("MemoryWarmthLight", typeof(Light));
        lightObject.transform.SetParent(transform, false);
        memoryLight = lightObject.GetComponent<Light>();
        memoryLight.type = LightType.Point;
        memoryLight.range = 8f;
        memoryLight.intensity = 0.15f;
        memoryLight.color = new Color(1f, 0.72f, 0.47f);

        GameObject bowl = GameObject.Find("GlassBowl");
        lightObject.transform.position = bowl != null ? bowl.transform.position + Vector3.up * 1.5f : Vector3.up * 2f;
    }

    private void CreateColorVolume()
    {
        GameObject volumeObject = new GameObject("MemoryColorVolume", typeof(Volume));
        volumeObject.transform.SetParent(transform, false);
        volume = volumeObject.GetComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 20f;
        volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
        colorAdjustments = volume.profile.Add<ColorAdjustments>(true);
        colorAdjustments.saturation.overrideState = true;
        colorAdjustments.contrast.overrideState = true;
        colorAdjustments.colorFilter.overrideState = true;
        colorAdjustments.postExposure.overrideState = true;
    }

    private void ApplyAtmosphere(float amount)
    {
        if (colorAdjustments == null)
        {
            return;
        }

        colorAdjustments.saturation.value = Mathf.Lerp(-42f, -5f, amount);
        colorAdjustments.contrast.value = Mathf.Lerp(-10f, -3f, amount);
        colorAdjustments.colorFilter.value = Color.Lerp(
            new Color(0.97f, 0.985f, 1f),
            new Color(1f, 0.98f, 0.94f),
            amount);
        colorAdjustments.postExposure.value = Mathf.Lerp(0.22f, 0.10f, amount);
        ApplyDreamcore(amount);

        for (int i = 0; i < sceneLights.Length; i++)
        {
            Light sceneLight = sceneLights[i];
            if (sceneLight == null)
            {
                continue;
            }

            float breathing = 1f + Mathf.Sin(presentationTime * 0.7f + i * 0.8f) * 0.012f;
            sceneLight.intensity = baseLightIntensities[i] * Mathf.Lerp(0.96f, 1f, amount) * breathing;
            sceneLight.color = Color.Lerp(new Color(0.88f, 0.91f, 0.95f), baseLightColors[i], amount);
        }

        if (memoryLight != null)
        {
            memoryLight.intensity = Mathf.Lerp(0.05f, 0.35f, amount)
                * (1f + Mathf.Sin(presentationTime * 0.9f) * 0.05f);
        }
    }

    private static void EnsureExistingCanvases()
    {
        EnsureCanvasVisible(GameObject.Find("UI_InteractionPrompt"), 80);
        EnsureCanvasVisible(GameObject.Find("UI_Inventory"), 25);
    }

    private static void StyleExistingInventory()
    {
        // InventoryHUD owns its paper layout. Keeping this hook prevents older scenes from
        // losing the canvas while avoiding a second system that overwrites its visual state.
        EnsureCanvasVisible(GameObject.Find("UI_Inventory"), 25);
    }

    private static void EnsureCanvasVisible(GameObject canvasObject, int sortingOrder)
    {
        if (canvasObject == null)
        {
            return;
        }

        RectTransform rect = canvasObject.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.localScale = Vector3.one;
        }

        Canvas targetCanvas = canvasObject.GetComponent<Canvas>();
        if (targetCanvas != null)
        {
            targetCanvas.overrideSorting = true;
            targetCanvas.sortingOrder = sortingOrder;
        }
    }

    private static RectTransform CreateRect(string objectName, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static TMP_Text AddText(
        Transform parent,
        string objectName,
        string content,
        int size,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax)
    {
        RectTransform rect = CreateRect(objectName, parent, anchorMin, anchorMax);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        MemoryUI.ApplyTypography(text, size, color);
        text.text = content;
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }
}
