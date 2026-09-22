using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Presents configured chapter cards and temporarily suspends player control.
/// Chapter state remains exclusively owned by ChapterManager.
/// </summary>
[DefaultExecutionOrder(-40)]
public sealed class MemoryCardPresenter : MonoBehaviour
{
    private static readonly Color Paper = new Color32(233, 228, 218, 255);
    private static readonly Color Ink = new Color32(73, 70, 64, 255);
    private static readonly Color Muted = new Color32(133, 128, 120, 255);
    private static readonly Color Line = new Color32(184, 177, 165, 255);
    private static readonly Color Memory = new Color32(141, 156, 134, 255);

    public bool IsPresenting { get; private set; }

    private readonly HashSet<ChapterId> presented = new HashSet<ChapterId>();
    private readonly Queue<ChapterId> pending = new Queue<ChapterId>();
    private MemoryCardCatalog catalog;
    private ChapterManager chapterManager;
    private Canvas canvas;
    private CanvasGroup group;
    private TMP_Text memoryLabel;
    private TMP_Text chineseText;
    private TMP_Text englishText;
    private FirstPersonController movement;
    private PlayerInteractor interactor;
    private bool movementWasEnabled;
    private bool interactorWasEnabled;
    private float elapsed;

    private void Awake()
    {
        catalog = Resources.Load<MemoryCardCatalog>("MemoryCardCatalog");
        BuildCard();
    }

    private void Start()
    {
        BindChapterManager();
    }

    private void Update()
    {
        if (chapterManager == null)
        {
            BindChapterManager();
        }

        if (!IsPresenting || catalog == null)
        {
            return;
        }

        elapsed += Time.unscaledDeltaTime;
        float fadeInEnd = catalog.FadeInDuration;
        float holdEnd = fadeInEnd + catalog.HoldDuration;
        float end = holdEnd + catalog.FadeOutDuration;

        if (elapsed < fadeInEnd)
        {
            group.alpha = Mathf.Clamp01(elapsed / fadeInEnd);
        }
        else if (elapsed < holdEnd)
        {
            group.alpha = 1f;
        }
        else
        {
            group.alpha = 1f - Mathf.Clamp01((elapsed - holdEnd) / catalog.FadeOutDuration);
        }

        if (elapsed >= end)
        {
            FinishCard();
        }
    }

    private void OnDestroy()
    {
        if (chapterManager != null)
        {
            chapterManager.ChapterCompleted -= OnChapterCompleted;
        }

        if (IsPresenting)
        {
            RestoreControls();
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
            chapterManager.ChapterCompleted -= OnChapterCompleted;
        }

        chapterManager = found;
        if (chapterManager != null)
        {
            chapterManager.ChapterCompleted += OnChapterCompleted;
        }
    }

    private void OnChapterCompleted(ChapterId chapter)
    {
        if (!presented.Add(chapter))
        {
            return;
        }

        if (IsPresenting)
        {
            pending.Enqueue(chapter);
            return;
        }

        BeginCard(chapter);
    }

    private void BeginCard(ChapterId chapter)
    {
        if (catalog == null || !catalog.TryGet(chapter, out MemoryCardEntry entry))
        {
            Debug.LogWarning("[MemoryCardPresenter] MemoryCardCatalog 中没有章节 " + chapter + " 的文案。", this);
            return;
        }

        memoryLabel.text = entry.memoryLabel;
        chineseText.text = entry.chinese;
        englishText.text = entry.english;
        elapsed = 0f;
        group.alpha = 0f;
        canvas.enabled = true;
        IsPresenting = true;
        SuspendControls();
    }

    private void FinishCard()
    {
        group.alpha = 0f;
        canvas.enabled = false;
        IsPresenting = false;
        RestoreControls();

        if (pending.Count > 0)
        {
            BeginCard(pending.Dequeue());
        }
    }

    private void SuspendControls()
    {
        movement = FindAnyObjectByType<FirstPersonController>();
        interactor = FindAnyObjectByType<PlayerInteractor>();
        movementWasEnabled = movement != null && movement.enabled;
        interactorWasEnabled = interactor != null && interactor.enabled;

        if (movement != null)
        {
            movement.enabled = false;
        }
        if (interactor != null)
        {
            interactor.enabled = false;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void RestoreControls()
    {
        if (movement != null)
        {
            movement.enabled = movementWasEnabled;
        }
        if (interactor != null)
        {
            interactor.enabled = interactorWasEnabled;
        }
    }

    private void BuildCard()
    {
        GameObject canvasObject = new GameObject(
            "MemoryCardCanvas",
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120;

        UnityEngine.UI.CanvasScaler scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        RectTransform shadow = CreateFixedRect("Shadow", canvasObject.transform, new Vector2(12f, -14f), new Vector2(884f, 494f));
        UnityEngine.UI.Image shadowImage = shadow.gameObject.AddComponent<UnityEngine.UI.Image>();
        shadowImage.color = new Color(0.09f, 0.075f, 0.06f, 0.24f);
        shadowImage.raycastTarget = false;

        RectTransform backingSheet = CreateFixedRect("BackingSheet", canvasObject.transform, new Vector2(-8f, 7f), new Vector2(876f, 486f));
        backingSheet.localRotation = Quaternion.Euler(0f, 0f, 0.45f);
        UnityEngine.UI.Image backingPaper = backingSheet.gameObject.AddComponent<UnityEngine.UI.Image>();
        backingPaper.color = new Color(0.83f, 0.80f, 0.73f, 0.96f);
        backingPaper.raycastTarget = false;

        RectTransform card = CreateFixedRect("Card", canvasObject.transform, Vector2.zero, new Vector2(872f, 482f));
        card.localRotation = Quaternion.Euler(0f, 0f, -0.18f);
        UnityEngine.UI.Image paper = card.gameObject.AddComponent<UnityEngine.UI.Image>();
        paper.color = Paper;
        paper.raycastTarget = false;
        UnityEngine.UI.Outline paperOutline = card.gameObject.AddComponent<UnityEngine.UI.Outline>();
        paperOutline.effectColor = new Color(Line.r, Line.g, Line.b, 0.72f);
        paperOutline.effectDistance = new Vector2(1f, -1f);
        MemoryUI.AddPaperGrain(card, 0.28f, 5f);

        RectTransform innerFrame = CreateStretchRect("InnerFrame", card, new Vector2(0.026f, 0.046f), new Vector2(0.974f, 0.954f));
        UnityEngine.UI.Image innerFrameImage = innerFrame.gameObject.AddComponent<UnityEngine.UI.Image>();
        innerFrameImage.color = Color.clear;
        innerFrameImage.raycastTarget = false;
        UnityEngine.UI.Outline frameOutline = innerFrame.gameObject.AddComponent<UnityEngine.UI.Outline>();
        frameOutline.effectColor = new Color(Line.r, Line.g, Line.b, 0.52f);
        frameOutline.effectDistance = new Vector2(1f, -1f);

        RectTransform leftRail = CreateStretchRect("ArchiveRail", card, new Vector2(0.055f, 0.14f), new Vector2(0.060f, 0.86f));
        UnityEngine.UI.Image leftRailImage = leftRail.gameObject.AddComponent<UnityEngine.UI.Image>();
        leftRailImage.color = new Color(Memory.r, Memory.g, Memory.b, 0.88f);
        leftRailImage.raycastTarget = false;

        RectTransform topTape = CreateStretchRect("TopTape", card, new Vector2(0.69f, 0.91f), new Vector2(0.86f, 1.03f));
        topTape.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
        UnityEngine.UI.Image topTapeImage = topTape.gameObject.AddComponent<UnityEngine.UI.Image>();
        topTapeImage.color = new Color(0.78f, 0.70f, 0.57f, 0.20f);
        topTapeImage.raycastTarget = false;

        memoryLabel = AddText(card, "MemoryLabel", string.Empty, 18f, Memory, new Vector2(0.095f, 0.78f), new Vector2(0.54f, 0.89f));
        memoryLabel.characterSpacing = 7f;
        TMP_Text archive = AddText(card, "ArchiveNote", "THE LATE GIFT  /  ARCHIVE", 12f, Muted, new Vector2(0.55f, 0.79f), new Vector2(0.90f, 0.88f));
        archive.alignment = TextAlignmentOptions.MidlineRight;
        archive.characterSpacing = 4f;

        RectTransform divider = CreateStretchRect("Divider", card, new Vector2(0.095f, 0.695f), new Vector2(0.90f, 0.70f));
        UnityEngine.UI.Image dividerImage = divider.gameObject.AddComponent<UnityEngine.UI.Image>();
        dividerImage.color = new Color(Line.r, Line.g, Line.b, 0.88f);
        dividerImage.raycastTarget = false;

        chineseText = AddText(card, "Chinese", string.Empty, 44f, Ink, new Vector2(0.095f, 0.40f), new Vector2(0.90f, 0.64f));
        chineseText.characterSpacing = 2f;
        englishText = AddText(card, "English", string.Empty, 22f, Muted, new Vector2(0.095f, 0.22f), new Vector2(0.90f, 0.39f));
        englishText.fontStyle = FontStyles.Italic;
        englishText.characterSpacing = 2.5f;

        RectTransform accent = CreateStretchRect("MemoryAccent", card, new Vector2(0.095f, 0.125f), new Vector2(0.20f, 0.132f));
        UnityEngine.UI.Image accentImage = accent.gameObject.AddComponent<UnityEngine.UI.Image>();
        accentImage.color = Memory;
        accentImage.raycastTarget = false;

        canvas.enabled = false;
    }

    private static RectTransform CreateFixedRect(string objectName, Transform parent, Vector2 position, Vector2 size)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static RectTransform CreateStretchRect(string objectName, Transform parent, Vector2 min, Vector2 max)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
        return rect;
    }

    private static TMP_Text AddText(Transform parent, string name, string value, float size, Color color, Vector2 min, Vector2 max)
    {
        TextMeshProUGUI text = CreateStretchRect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        MemoryUI.ApplyTypography(text, size, color);
        text.text = value;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        return text;
    }
}
