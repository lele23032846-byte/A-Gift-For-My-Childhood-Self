using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Exploration HUD. It presents data supplied by the interaction and inventory systems;
/// it does not decide which world object can be interacted with or picked up.
/// </summary>
[DefaultExecutionOrder(-50)]
public sealed class InteractionPromptUI : MonoBehaviour
{
    private static readonly Color Paper = new Color32(233, 228, 218, 255);
    private static readonly Color Ink = new Color32(73, 70, 64, 255);
    private static readonly Color Muted = new Color32(133, 128, 120, 255);
    private static readonly Color Line = new Color32(184, 177, 165, 255);
    private static readonly Color Memory = new Color32(141, 156, 134, 255);

    [Tooltip("Action text. Existing scene references are reused automatically.")]
    [SerializeField] private TMP_Text promptText;

    [Header("Animation")]
    [SerializeField, Min(0.01f)] private float fadeInDuration = 0.18f;
    [SerializeField, Min(0.01f)] private float fadeOutDuration = 0.12f;
    [SerializeField, Min(0.01f)] private float pickupFadeDuration = 0.30f;
    [SerializeField, Min(0f)] private float pickupHoldDuration = 1.8f;

    [Header("Configurable copy")]
    [SerializeField] private string interactionKeyLabel = "E";
    [SerializeField] private string pickupLabel = "已收起";

    private CanvasGroup promptGroup;
    private CanvasGroup pickupGroup;
    private UnityEngine.UI.Image reticleDot;
    private UIRingGraphic reticleRing;
    private TMP_Text pickupHeading;
    private TMP_Text pickupItemName;
    private Inventory inventory;
    private Coroutine pickupRoutine;
    private float targetPromptAlpha;

    public bool IsVisible { get; private set; }
    public string CurrentText => promptText != null ? promptText.text : string.Empty;

    private void Awake()
    {
        ConfigureCanvas();
        BuildHud();
        HideImmediate();
    }

    private void OnEnable()
    {
        BindInventory(FindAnyObjectByType<Inventory>());
    }

    private void OnDisable()
    {
        BindInventory(null);
        if (pickupRoutine != null)
        {
            StopCoroutine(pickupRoutine);
            pickupRoutine = null;
        }
    }

    private void Update()
    {
        if (promptGroup == null)
        {
            return;
        }

        float duration = targetPromptAlpha > promptGroup.alpha ? fadeInDuration : fadeOutDuration;
        promptGroup.alpha = Mathf.MoveTowards(
            promptGroup.alpha,
            targetPromptAlpha,
            Time.unscaledDeltaTime / Mathf.Max(0.01f, duration));
    }

    public void Show(string text)
    {
        if (promptText == null)
        {
            Debug.LogWarning("[InteractionPromptUI] Action text is not configured.", this);
            return;
        }

        promptText.text = RemoveLegacyKeyPrefix(text);
        promptText.gameObject.SetActive(true);
        targetPromptAlpha = 1f;
        IsVisible = true;
        SetFocused(true);
    }

    public void Hide()
    {
        targetPromptAlpha = 0f;
        IsVisible = false;
        SetFocused(false);
    }

    private void HideImmediate()
    {
        targetPromptAlpha = 0f;
        if (promptGroup != null)
        {
            promptGroup.alpha = 0f;
        }

        if (pickupGroup != null)
        {
            pickupGroup.alpha = 0f;
        }

        IsVisible = false;
        SetFocused(false);
    }

    private void SetFocused(bool focused)
    {
        if (reticleDot != null)
        {
            reticleDot.gameObject.SetActive(!focused);
        }

        if (reticleRing != null)
        {
            reticleRing.gameObject.SetActive(focused);
        }
    }

    private void BindInventory(Inventory target)
    {
        if (inventory == target)
        {
            return;
        }

        if (inventory != null)
        {
            inventory.ItemAdded -= OnItemAdded;
        }

        inventory = target;
        if (inventory != null)
        {
            inventory.ItemAdded += OnItemAdded;
        }
    }

    private void OnItemAdded(ItemId itemId)
    {
        if (pickupRoutine != null)
        {
            StopCoroutine(pickupRoutine);
        }

        pickupHeading.text = pickupLabel + "  /  FOUND";
        pickupItemName.text = itemId.GetDisplayName();
        pickupRoutine = StartCoroutine(PlayPickupNotification());
    }

    private IEnumerator PlayPickupNotification()
    {
        pickupGroup.alpha = 0f;
        while (pickupGroup.alpha < 1f)
        {
            pickupGroup.alpha = Mathf.MoveTowards(
                pickupGroup.alpha,
                1f,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, pickupFadeDuration));
            yield return null;
        }

        float hold = 0f;
        while (hold < pickupHoldDuration)
        {
            hold += Time.unscaledDeltaTime;
            yield return null;
        }

        while (pickupGroup.alpha > 0f)
        {
            pickupGroup.alpha = Mathf.MoveTowards(
                pickupGroup.alpha,
                0f,
                Time.unscaledDeltaTime / Mathf.Max(0.01f, pickupFadeDuration));
            yield return null;
        }

        pickupRoutine = null;
    }

    private void ConfigureCanvas()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 80;
        }

        UnityEngine.UI.CanvasScaler scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        RectTransform rootRect = transform as RectTransform;
        if (rootRect != null)
        {
            rootRect.localScale = Vector3.one;
        }
    }

    private void BuildHud()
    {
        RectTransform hud = CreateRect("GameplayHUD", transform);
        Stretch(hud);

        RectTransform dotRect = CreateRect("ReticleDot", hud);
        Anchor(dotRect, new Vector2(0.5f, 0.5f), new Vector2(4f, 4f), Vector2.zero);
        reticleDot = dotRect.gameObject.AddComponent<UnityEngine.UI.Image>();
        reticleDot.color = new Color(Paper.r, Paper.g, Paper.b, 0.92f);
        reticleDot.raycastTarget = false;
        UnityEngine.UI.Shadow dotShadow = dotRect.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        dotShadow.effectColor = new Color(0.08f, 0.07f, 0.06f, 0.72f);
        dotShadow.effectDistance = new Vector2(1f, -1f);

        RectTransform ringRect = CreateRect("ReticleRing", hud);
        Anchor(ringRect, new Vector2(0.5f, 0.5f), new Vector2(22f, 22f), Vector2.zero);
        reticleRing = ringRect.gameObject.AddComponent<UIRingGraphic>();
        reticleRing.color = new Color(Paper.r, Paper.g, Paper.b, 0.98f);
        reticleRing.Thickness = 1.5f;
        reticleRing.raycastTarget = false;
        UnityEngine.UI.Shadow ringShadow = ringRect.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        ringShadow.effectColor = new Color(0.08f, 0.07f, 0.06f, 0.78f);
        ringShadow.effectDistance = new Vector2(1f, -1f);

        RectTransform prompt = CreateRect("InteractionPrompt", hud);
        prompt.anchorMin = new Vector2(0.5f, 0f);
        prompt.anchorMax = new Vector2(0.5f, 0f);
        prompt.pivot = new Vector2(0.5f, 0f);
        prompt.anchoredPosition = new Vector2(0f, 116f);
        prompt.sizeDelta = new Vector2(520f, 58f);
        promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        promptGroup.interactable = false;
        promptGroup.blocksRaycasts = false;

        UnityEngine.UI.Image promptPaper = prompt.gameObject.AddComponent<UnityEngine.UI.Image>();
        promptPaper.color = new Color(Paper.r, Paper.g, Paper.b, 0.97f);
        promptPaper.raycastTarget = false;
        UnityEngine.UI.Shadow promptShadow = prompt.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        promptShadow.effectColor = new Color(0.06f, 0.05f, 0.04f, 0.34f);
        promptShadow.effectDistance = new Vector2(4f, -5f);
        UnityEngine.UI.Outline promptOutline = prompt.gameObject.AddComponent<UnityEngine.UI.Outline>();
        promptOutline.effectColor = new Color(Line.r, Line.g, Line.b, 0.62f);
        promptOutline.effectDistance = new Vector2(1f, -1f);
        MemoryUI.AddPaperGrain(prompt, 0.17f, 3f);

        RectTransform topRule = CreateRect("ArchiveRule", prompt);
        topRule.anchorMin = new Vector2(0f, 1f);
        topRule.anchorMax = new Vector2(1f, 1f);
        topRule.pivot = new Vector2(0.5f, 1f);
        topRule.anchoredPosition = new Vector2(0f, -1f);
        topRule.sizeDelta = new Vector2(-18f, 1f);
        UnityEngine.UI.Image topRuleImage = topRule.gameObject.AddComponent<UnityEngine.UI.Image>();
        topRuleImage.color = new Color(Line.r, Line.g, Line.b, 0.56f);
        topRuleImage.raycastTarget = false;

        RectTransform keyBlock = CreateRect("KeyBlock", prompt);
        keyBlock.anchorMin = new Vector2(0f, 0.5f);
        keyBlock.anchorMax = new Vector2(0f, 0.5f);
        keyBlock.pivot = new Vector2(0f, 0.5f);
        keyBlock.anchoredPosition = new Vector2(12f, 0f);
        keyBlock.sizeDelta = new Vector2(40f, 34f);
        UnityEngine.UI.Image keyBackground = keyBlock.gameObject.AddComponent<UnityEngine.UI.Image>();
        keyBackground.color = new Color(Ink.r, Ink.g, Ink.b, 0.94f);
        keyBackground.raycastTarget = false;
        TMP_Text keyText = CreateText("Key", keyBlock, interactionKeyLabel, 18f, Paper, TextAlignmentOptions.Center);
        Stretch(keyText.rectTransform);

        if (promptText == null)
        {
            promptText = CreateText("ActionText", prompt, string.Empty, 23f, Ink, TextAlignmentOptions.MidlineLeft);
        }
        else
        {
            promptText.gameObject.SetActive(true);
            promptText.transform.SetParent(prompt, false);
            promptText.name = "ActionText";
            MemoryUI.ApplyTypography(promptText, 23f, Ink);
            promptText.alignment = TextAlignmentOptions.MidlineLeft;
            promptText.raycastTarget = false;
        }

        RectTransform actionRect = promptText.rectTransform;
        actionRect.anchorMin = Vector2.zero;
        actionRect.anchorMax = Vector2.one;
        actionRect.pivot = new Vector2(0.5f, 0.5f);
        actionRect.offsetMin = new Vector2(68f, 7f);
        actionRect.offsetMax = new Vector2(-20f, -7f);
        actionRect.localScale = Vector3.one;

        RectTransform pickup = CreateRect("PickupNotification", hud);
        pickup.anchorMin = new Vector2(1f, 0f);
        pickup.anchorMax = new Vector2(1f, 0f);
        pickup.pivot = new Vector2(1f, 0f);
        pickup.anchoredPosition = new Vector2(-42f, 42f);
        pickup.sizeDelta = new Vector2(348f, 88f);
        pickupGroup = pickup.gameObject.AddComponent<CanvasGroup>();
        pickupGroup.interactable = false;
        pickupGroup.blocksRaycasts = false;

        UnityEngine.UI.Image pickupPaper = pickup.gameObject.AddComponent<UnityEngine.UI.Image>();
        pickupPaper.color = new Color(Paper.r, Paper.g, Paper.b, 0.98f);
        pickupPaper.raycastTarget = false;
        UnityEngine.UI.Shadow pickupShadow = pickup.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        pickupShadow.effectColor = new Color(0.06f, 0.05f, 0.04f, 0.28f);
        pickupShadow.effectDistance = new Vector2(5f, -6f);
        UnityEngine.UI.Outline pickupOutline = pickup.gameObject.AddComponent<UnityEngine.UI.Outline>();
        pickupOutline.effectColor = new Color(Line.r, Line.g, Line.b, 0.60f);
        pickupOutline.effectDistance = new Vector2(1f, -1f);
        MemoryUI.AddPaperGrain(pickup, 0.18f, 3f);

        RectTransform accent = CreateRect("MemoryLine", pickup);
        accent.anchorMin = Vector2.zero;
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.anchoredPosition = Vector2.zero;
        accent.sizeDelta = new Vector2(3f, 0f);
        UnityEngine.UI.Image accentImage = accent.gameObject.AddComponent<UnityEngine.UI.Image>();
        accentImage.color = Memory;
        accentImage.raycastTarget = false;

        pickupHeading = CreateText("Heading", pickup, pickupLabel + "  /  FOUND", 13f, Muted, TextAlignmentOptions.BottomLeft);
        pickupHeading.characterSpacing = 2f;
        pickupHeading.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        pickupHeading.rectTransform.anchorMax = Vector2.one;
        pickupHeading.rectTransform.offsetMin = new Vector2(24f, 1f);
        pickupHeading.rectTransform.offsetMax = new Vector2(-18f, -10f);

        pickupItemName = CreateText("ItemName", pickup, string.Empty, 23f, Ink, TextAlignmentOptions.TopLeft);
        pickupItemName.rectTransform.anchorMin = Vector2.zero;
        pickupItemName.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        pickupItemName.rectTransform.offsetMin = new Vector2(24f, 9f);
        pickupItemName.rectTransform.offsetMax = new Vector2(-18f, 0f);
    }

    private static string RemoveLegacyKeyPrefix(string value)
    {
        string result = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        if (result.Length == 0 || char.ToUpperInvariant(result[0]) != 'E')
        {
            return result;
        }

        int index = 1;
        while (index < result.Length)
        {
            char character = result[index];
            if (!char.IsWhiteSpace(character) && character != '/' && character != '-' && character != ':' && character != '·')
            {
                break;
            }
            index++;
        }

        return result.Substring(index).TrimStart();
    }

    private static RectTransform CreateRect(string objectName, Transform parent)
    {
        GameObject gameObject = new GameObject(objectName, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return (RectTransform)gameObject.transform;
    }

    private static TMP_Text CreateText(
        string objectName,
        Transform parent,
        string value,
        float size,
        Color color,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(objectName, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        MemoryUI.ApplyTypography(text, size, color);
        text.text = value;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 position)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
}
