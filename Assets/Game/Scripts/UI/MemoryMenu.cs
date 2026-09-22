using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>Main menu and pause layer. Gameplay never owns focus while this layer is open.</summary>
[DefaultExecutionOrder(-900)]
public sealed class MemoryMenu : MonoBehaviour
{
    public bool IsOpen { get; private set; }

    private bool started;
    private CanvasGroup group;
    private TMP_Text title;
    private TMP_Text subtitle;
    private TMP_Text startLabel;
    private TMP_Text status;
    private FirstPersonController movement;
    private PlayerInteractor interactor;
    private readonly List<Canvas> hud = new List<Canvas>();
    private readonly List<bool> hudStates = new List<bool>();
    private bool movementWasEnabled;
    private bool interactorWasEnabled;
    private float priorTimeScale;
    private UnityEngine.UI.Button startButton;
    private Camera viewCamera;
    private CameraCollision collision;
    private bool collisionWasEnabled;
    private Vector3 savedCameraPosition;
    private Quaternion savedCameraRotation;
    private bool titlePose;
    private Texture2D menuGradient;
    private Renderer[] titleHidden = System.Array.Empty<Renderer>();
    private bool[] titleHiddenStates = System.Array.Empty<bool>();
    private float reopenAllowedAt;
    private MemoryCardPresenter memoryCardPresenter;

    private void Awake()
    {
        movement = FindAnyObjectByType<FirstPersonController>();
        viewCamera = movement != null ? movement.GetComponentInChildren<Camera>() : Camera.main;
        collision = viewCamera != null ? viewCamera.GetComponent<CameraCollision>() : null;
        interactor = FindAnyObjectByType<PlayerInteractor>();
        memoryCardPresenter = GetComponent<MemoryCardPresenter>();

        foreach (Canvas existingCanvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (existingCanvas.isRootCanvas)
            {
                hud.Add(existingCanvas);
            }
        }

        Build();
        Open();
    }

    private void Update()
    {
        if (group != null)
        {
            group.alpha = Mathf.MoveTowards(group.alpha, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.42f);
        }

        if (memoryCardPresenter != null && memoryCardPresenter.IsPresenting)
        {
            return;
        }

        if (Keyboard.current == null || !Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            return;
        }

        if (IsOpen && started)
        {
            Resume();
        }
        else if (!IsOpen && Time.unscaledTime >= reopenAllowedAt)
        {
            Open();
        }
    }

    public void Open()
    {
        if (IsOpen)
        {
            return;
        }

        if (!started && viewCamera != null)
        {
            GameObject younger = GameObject.Find("MyYoungerSelf");
            if (younger != null)
            {
                titleHidden = younger.GetComponentsInChildren<Renderer>();
                titleHiddenStates = new bool[titleHidden.Length];
                for (int i = 0; i < titleHidden.Length; i++)
                {
                    titleHiddenStates[i] = titleHidden[i].enabled;
                    titleHidden[i].enabled = false;
                }
            }

            GameObject pose = GameObject.Find("MemoryTitleView");
            if (pose != null)
            {
                savedCameraPosition = viewCamera.transform.localPosition;
                savedCameraRotation = viewCamera.transform.localRotation;
                collisionWasEnabled = collision != null && collision.enabled;
                if (collision != null)
                {
                    collision.enabled = false;
                }
                viewCamera.transform.SetPositionAndRotation(pose.transform.position, pose.transform.rotation);
                titlePose = true;
            }
        }

        IsOpen = true;
        priorTimeScale = Time.timeScale;
        Time.timeScale = 0f;
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

        hudStates.Clear();
        foreach (Canvas existingCanvas in hud)
        {
            hudStates.Add(existingCanvas != null && existingCanvas.enabled);
            if (existingCanvas != null)
            {
                existingCanvas.enabled = false;
            }
        }

        group.interactable = true;
        group.blocksRaycasts = true;
        title.text = started ? "歇一会儿" : "迟到的礼物";
        subtitle.text = started ? "时间停在这里，等你回来。" : "有些礼物，隔了很久才送到。";
        startLabel.text = started ? "继续游戏" : "开始游戏";
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(startButton.gameObject);
        }
    }

    public void Resume()
    {
        if (!IsOpen)
        {
            return;
        }

        for (int i = 0; i < titleHidden.Length; i++)
        {
            if (titleHidden[i] != null)
            {
                titleHidden[i].enabled = titleHiddenStates[i];
            }
        }

        if (titlePose && viewCamera != null)
        {
            viewCamera.transform.localPosition = savedCameraPosition;
            viewCamera.transform.localRotation = savedCameraRotation;
            if (collision != null)
            {
                collision.enabled = collisionWasEnabled;
            }
            titlePose = false;
        }

        started = true;
        IsOpen = false;
        reopenAllowedAt = Time.unscaledTime + 0.30f;
        Time.timeScale = priorTimeScale;
        group.interactable = false;
        group.blocksRaycasts = false;

        for (int i = 0; i < hud.Count; i++)
        {
            if (hud[i] != null)
            {
                hud[i].enabled = hudStates[i];
            }
        }

        if (movement != null)
        {
            movement.enabled = movementWasEnabled;
        }
        if (interactor != null)
        {
            interactor.enabled = interactorWasEnabled;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    private void OnDestroy()
    {
        if (IsOpen)
        {
            Time.timeScale = priorTimeScale;
        }
        if (menuGradient != null)
        {
            Destroy(menuGradient);
        }
    }

    private void ToggleSound()
    {
        AudioListener.pause = !AudioListener.pause;
        status.text = AudioListener.pause ? "声音已关闭" : "声音已开启";
    }

    private static void ExitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void Build()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
        {
            new GameObject("MemoryEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        GameObject canvasObject = new GameObject(
            "MemoryMenuCanvas",
            typeof(Canvas),
            typeof(UnityEngine.UI.CanvasScaler),
            typeof(UnityEngine.UI.GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);

        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;

        UnityEngine.UI.CanvasScaler scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        group = canvasObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        MemoryUI.Image("Shade", canvasObject.transform, new Color(0.055f, 0.065f, 0.075f, 0.08f), Vector2.zero, Vector2.one);
        menuGradient = new Texture2D(64, 1, TextureFormat.RGBA32, false);
        menuGradient.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < 64; x++)
        {
            menuGradient.SetPixel(x, 0, new Color(0.045f, 0.05f, 0.055f, 0.5f * Mathf.Pow(1f - x / 63f, 1.2f)));
        }
        menuGradient.Apply();

        UnityEngine.UI.RawImage gradient = MemoryUI.Rect(
            "SoftLeftShade",
            canvasObject.transform,
            Vector2.zero,
            new Vector2(0.64f, 1f)).gameObject.AddComponent<UnityEngine.UI.RawImage>();
        gradient.texture = menuGradient;
        gradient.raycastTarget = false;

        MemoryUI.Image("PaperEdge", canvasObject.transform, new Color(0.9f, 0.87f, 0.8f, 0.32f), new Vector2(0.073f, 0.16f), new Vector2(0.074f, 0.84f));
        MemoryUI.Text("Archive", canvasObject.transform, "一份迟到的记忆   /   2000", 17f, new Vector2(0.105f, 0.79f), new Vector2(0.52f, 0.84f), MemoryUI.Muted);
        title = MemoryUI.Text("Title", canvasObject.transform, "迟到的礼物", 76f, new Vector2(0.1f, 0.60f), new Vector2(0.67f, 0.75f));
        MemoryUI.Text("EnglishTitle", canvasObject.transform, "THE LATE GIFT", 15f, new Vector2(0.105f, 0.565f), new Vector2(0.65f, 0.605f), MemoryUI.Muted);
        subtitle = MemoryUI.Text("Subtitle", canvasObject.transform, string.Empty, 23f, new Vector2(0.105f, 0.46f), new Vector2(0.7f, 0.525f), MemoryUI.Muted);

        startButton = CreateButton(canvasObject.transform, "Start", 0.345f, "开始游戏", Resume, out startLabel);
        UnityEngine.UI.Button settingsButton = CreateButton(canvasObject.transform, "Settings", 0.265f, "设置", ToggleSound, out _);
        UnityEngine.UI.Button exitButton = CreateButton(canvasObject.transform, "Exit", 0.185f, "退出", ExitGame, out _);
        ConfigureNavigation(startButton, settingsButton, exitButton);

        status = MemoryUI.Text("Status", canvasObject.transform, string.Empty, 16f, new Vector2(0.33f, 0.185f), new Vector2(0.68f, 0.24f), MemoryUI.Muted);
        MemoryUI.Text("Controls", canvasObject.transform, "W A S D  慢慢走    ·    鼠标  看一看    ·    E  触碰    ·    Esc  停一会儿", 17f, new Vector2(0.105f, 0.075f), new Vector2(0.91f, 0.12f), MemoryUI.Muted);
        MemoryUI.Text("Signature", canvasObject.transform, "献给那个，还在等你的孩子。", 18f, new Vector2(0.69f, 0.79f), new Vector2(0.95f, 0.85f), MemoryUI.Muted);
    }

    private static UnityEngine.UI.Button CreateButton(
        Transform parent,
        string objectName,
        float y,
        string label,
        UnityEngine.Events.UnityAction action,
        out TMP_Text text)
    {
        RectTransform rect = MemoryUI.Rect(objectName, parent, new Vector2(0.102f, y), new Vector2(0.37f, y + 0.061f));
        UnityEngine.UI.Image background = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
        background.color = Color.white;
        UnityEngine.UI.Button button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = background;

        UnityEngine.UI.ColorBlock colors = button.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.035f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(1f, 1f, 1f, 0.07f);
        button.colors = colors;
        button.onClick.AddListener(action);

        MemoryUI.Image("HoverLine", rect, new Color(0.91f, 0.89f, 0.84f, 0.65f), new Vector2(0f, 0.47f), new Vector2(0.04f, 0.50f));
        text = MemoryUI.Text("Label", rect, label, 26f, new Vector2(0.065f, 0f), Vector2.one);
        return button;
    }

    private static void ConfigureNavigation(params UnityEngine.UI.Button[] buttons)
    {
        for (int i = 0; i < buttons.Length; i++)
        {
            UnityEngine.UI.Navigation navigation = new UnityEngine.UI.Navigation
            {
                mode = UnityEngine.UI.Navigation.Mode.Explicit,
                selectOnUp = buttons[(i - 1 + buttons.Length) % buttons.Length],
                selectOnDown = buttons[(i + 1) % buttons.Length]
            };
            buttons[i].navigation = navigation;
        }
    }
}
