using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Paper-page inventory presentation. Inventory remains the single source of truth;
/// this component only reads its item list and temporarily owns player input while open.
/// </summary>
[DefaultExecutionOrder(-45)]
public sealed class InventoryHUD : MonoBehaviour
{
    [Tooltip("要展示的真实物品栏。留空时自动查找。")]
    [SerializeField] private Inventory inventory;

    [Tooltip("旧场景引用，仅用于兼容已有场景；新版纸页会在运行时接管展示。")]
    [SerializeField] private List<TMP_Text> slotTexts = new List<TMP_Text>();

    [SerializeField, Min(0.01f)] private float fadeDuration = 0.2f;

    private readonly List<SlotView> slots = new List<SlotView>();
    private CanvasGroup group;
    private TMP_Text emptyLabel;
    private FirstPersonController movement;
    private PlayerInteractor interactor;
    private MemoryMenu menu;
    private bool movementWasEnabled;
    private bool interactorWasEnabled;
    private bool menuWasEnabled;
    private CursorLockMode previousCursorLock;
    private bool previousCursorVisible;
    private float targetAlpha;

    public bool IsOpen { get; private set; }

    private void Awake()
    {
        if (inventory == null)
        {
            inventory = FindAnyObjectByType<Inventory>();
        }

        ConfigureCanvas();
        HideLegacySlots();
        BuildPaperPage();
        CloseImmediate();
    }

    private void OnEnable()
    {
        if (inventory != null)
        {
            inventory.Changed += Refresh;
        }
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null)
        {
            inventory.Changed -= Refresh;
        }
        if (IsOpen)
        {
            RestorePlayerControl();
        }
        IsOpen = false;
    }

    private void Update()
    {
        float speed = Time.unscaledDeltaTime / Mathf.Max(0.01f, fadeDuration);
        if (group != null)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, targetAlpha, speed);
        }

        if (Keyboard.current == null)
        {
            return;
        }

        if (IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Close();
            return;
        }

        if (!Keyboard.current.tabKey.wasPressedThisFrame)
        {
            return;
        }

        if (IsOpen)
        {
            Close();
        }
        else if (CanOpen())
        {
            Open();
        }
    }

    public void Bind(Inventory target)
    {
        if (inventory == target)
        {
            return;
        }

        if (isActiveAndEnabled && inventory != null)
        {
            inventory.Changed -= Refresh;
        }

        inventory = target;
        if (isActiveAndEnabled && inventory != null)
        {
            inventory.Changed += Refresh;
        }
        Refresh();
    }

    public void Refresh()
    {
        int count = inventory != null ? Mathf.Min(inventory.Count, slots.Count) : 0;
        for (int i = 0; i < slots.Count; i++)
        {
            bool hasItem = i < count;
            slots[i].Root.SetActive(hasItem);
            if (!hasItem)
            {
                continue;
            }

            ItemId item = inventory.Items[i];
            slots[i].Number.text = (i + 1).ToString("00");
            slots[i].Name.text = item.GetDisplayName();
            slots[i].Description.text = DescriptionFor(item);
        }

        if (emptyLabel != null)
        {
            emptyLabel.gameObject.SetActive(count == 0);
        }

        // Keep legacy inspector labels synchronized for test harnesses and old scene tooling.
        for (int i = 0; i < slotTexts.Count; i++)
        {
            if (slotTexts[i] != null)
            {
                slotTexts[i].text = inventory != null && i < inventory.Count
                    ? inventory.Items[i].GetDisplayName()
                    : string.Empty;
            }
        }
    }

    private void Open()
    {
        IsOpen = true;
        targetAlpha = 1f;
        group.interactable = true;
        group.blocksRaycasts = true;
        Refresh();

        movement = FindAnyObjectByType<FirstPersonController>();
        interactor = FindAnyObjectByType<PlayerInteractor>();
        movementWasEnabled = movement != null && movement.enabled;
        interactorWasEnabled = interactor != null && interactor.enabled;
        previousCursorLock = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        menu = FindAnyObjectByType<MemoryMenu>();
        menuWasEnabled = menu != null && menu.enabled;

        if (menu != null)
        {
            menu.enabled = false;
        }
        if (movement != null)
        {
            movement.enabled = false;
        }
        if (interactor != null)
        {
            interactor.enabled = false;
        }
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Close()
    {
        if (!IsOpen)
        {
            return;
        }

        IsOpen = false;
        targetAlpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        RestorePlayerControl();
    }

    private void CloseImmediate()
    {
        IsOpen = false;
        targetAlpha = 0f;
        if (group != null)
        {
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
    }

    private void RestorePlayerControl()
    {
        if (movement != null)
        {
            movement.enabled = movementWasEnabled;
        }
        if (interactor != null)
        {
            interactor.enabled = interactorWasEnabled;
        }
        if (menu != null)
        {
            menu.enabled = menuWasEnabled;
        }
        Cursor.lockState = previousCursorLock;
        Cursor.visible = previousCursorVisible;
    }

    private static bool CanOpen()
    {
        MemoryMenu menu = FindAnyObjectByType<MemoryMenu>();
        if (menu != null && menu.IsOpen)
        {
            return false;
        }

        MemoryCardPresenter card = FindAnyObjectByType<MemoryCardPresenter>();
        return card == null || !card.IsPresenting;
    }

    private void ConfigureCanvas()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 90;
        }

        UnityEngine.UI.CanvasScaler scaler = GetComponent<UnityEngine.UI.CanvasScaler>();
        if (scaler != null)
        {
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        if (transform is RectTransform root)
        {
            root.localScale = Vector3.one;
        }
    }

    private void HideLegacySlots()
    {
        Transform legacy = transform.Find("SlotsPanel");
        if (legacy != null)
        {
            legacy.gameObject.SetActive(false);
        }
    }

    private void BuildPaperPage()
    {
        RectTransform page = CreateRect("InventoryPaper", transform);
        page.anchorMin = page.anchorMax = new Vector2(1f, 0.5f);
        page.pivot = new Vector2(1f, 0.5f);
        page.anchoredPosition = new Vector2(-64f, 0f);
        page.sizeDelta = new Vector2(540f, 650f);
        group = page.gameObject.AddComponent<CanvasGroup>();

        UnityEngine.UI.Image paper = page.gameObject.AddComponent<UnityEngine.UI.Image>();
        paper.color = new Color(MemoryUI.Paper.r, MemoryUI.Paper.g, MemoryUI.Paper.b, 0.985f);
        UnityEngine.UI.Shadow shadow = page.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0.055f, 0.045f, 0.035f, 0.34f);
        shadow.effectDistance = new Vector2(8f, -10f);
        UnityEngine.UI.Outline outline = page.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color(MemoryUI.Line.r, MemoryUI.Line.g, MemoryUI.Line.b, 0.65f);
        outline.effectDistance = new Vector2(1f, -1f);
        paper.raycastTarget = true;
        MemoryUI.AddPaperGrain(page, 0.26f, 5f);

        TMP_Text index = CreateText("ArchiveIndex", page, "02  /  INVENTORY", 13f, MemoryUI.Muted, TextAlignmentOptions.Left);
        SetRect(index.rectTransform, new Vector2(0.075f, 0.89f), new Vector2(0.53f, 0.955f));
        index.characterSpacing = 4f;

        TMP_Text title = CreateText("Title", page, "随身物件", 32f, MemoryUI.Ink, TextAlignmentOptions.Left);
        SetRect(title.rectTransform, new Vector2(0.075f, 0.78f), new Vector2(0.70f, 0.89f));
        title.characterSpacing = 2f;

        TMP_Text closeHint = CreateText("CloseHint", page, "TAB  /  ESC   收起", 12f, MemoryUI.Muted, TextAlignmentOptions.Right);
        SetRect(closeHint.rectTransform, new Vector2(0.58f, 0.88f), new Vector2(0.925f, 0.945f));
        closeHint.characterSpacing = 2f;

        RectTransform divider = CreateRect("HeaderRule", page);
        SetRect(divider, new Vector2(0.075f, 0.755f), new Vector2(0.925f, 0.758f));
        UnityEngine.UI.Image dividerImage = divider.gameObject.AddComponent<UnityEngine.UI.Image>();
        dividerImage.color = MemoryUI.Line;
        dividerImage.raycastTarget = false;

        emptyLabel = CreateText("Empty", page, "还没有带走任何东西。", 19f, MemoryUI.Muted, TextAlignmentOptions.Center);
        SetRect(emptyLabel.rectTransform, new Vector2(0.10f, 0.35f), new Vector2(0.90f, 0.64f));

        for (int i = 0; i < 3; i++)
        {
            float top = 0.71f - i * 0.205f;
            float bottom = top - 0.17f;
            RectTransform slot = CreateRect("Item_" + (i + 1), page);
            SetRect(slot, new Vector2(0.075f, bottom), new Vector2(0.925f, top));

            UnityEngine.UI.Image slotPaper = slot.gameObject.AddComponent<UnityEngine.UI.Image>();
            slotPaper.color = new Color(MemoryUI.PaperLight.r, MemoryUI.PaperLight.g, MemoryUI.PaperLight.b, 0.68f);
            slotPaper.raycastTarget = false;
            UnityEngine.UI.Outline slotOutline = slot.gameObject.AddComponent<UnityEngine.UI.Outline>();
            slotOutline.effectColor = new Color(MemoryUI.Line.r, MemoryUI.Line.g, MemoryUI.Line.b, 0.48f);
            slotOutline.effectDistance = new Vector2(1f, -1f);

            RectTransform marker = CreateRect("MemoryMark", slot);
            SetRect(marker, new Vector2(0f, 0f), new Vector2(0.012f, 1f));
            UnityEngine.UI.Image markerImage = marker.gameObject.AddComponent<UnityEngine.UI.Image>();
            markerImage.color = MemoryUI.Memory;
            markerImage.raycastTarget = false;

            TMP_Text number = CreateText("Number", slot, "00", 13f, MemoryUI.Muted, TextAlignmentOptions.TopLeft);
            SetRect(number.rectTransform, new Vector2(0.055f, 0.57f), new Vector2(0.16f, 0.88f));
            number.characterSpacing = 3f;

            TMP_Text itemName = CreateText("Name", slot, string.Empty, 25f, MemoryUI.Ink, TextAlignmentOptions.BottomLeft);
            SetRect(itemName.rectTransform, new Vector2(0.18f, 0.52f), new Vector2(0.92f, 0.88f));

            TMP_Text description = CreateText("Description", slot, string.Empty, 15f, MemoryUI.Muted, TextAlignmentOptions.TopLeft);
            SetRect(description.rectTransform, new Vector2(0.18f, 0.14f), new Vector2(0.92f, 0.50f));
            description.textWrappingMode = TextWrappingModes.Normal;

            slots.Add(new SlotView(slot.gameObject, number, itemName, description));
        }
    }

    private static string DescriptionFor(ItemId item)
    {
        switch (item)
        {
            case ItemId.SmallBox:
                return "有褪色花纹的小盒。像是一直在等你想起。";
            default:
                return "一件从旧时光里带回来的物件。";
        }
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.transform.SetParent(parent, false);
        return (RectTransform)gameObject.transform;
    }

    private static TMP_Text CreateText(
        string name,
        Transform parent,
        string value,
        float size,
        Color color,
        TextAlignmentOptions alignment)
    {
        TextMeshProUGUI text = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        MemoryUI.ApplyTypography(text, size, color);
        text.text = value;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private readonly struct SlotView
    {
        public readonly GameObject Root;
        public readonly TMP_Text Number;
        public readonly TMP_Text Name;
        public readonly TMP_Text Description;

        public SlotView(GameObject root, TMP_Text number, TMP_Text name, TMP_Text description)
        {
            Root = root;
            Number = number;
            Name = name;
            Description = description;
        }
    }
}
