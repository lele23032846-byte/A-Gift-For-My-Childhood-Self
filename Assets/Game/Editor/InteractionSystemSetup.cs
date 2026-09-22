using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 通用射线交互系统的场景搭建工具（仅做增量搭建，不重建场景）：
/// - 确保 Interactable 用户 Layer 存在；
/// - 必要时自动导入 TMP Essential Resources（默认字体）；
/// - 在 Player 上挂载并配置 PlayerInteractor；
/// - 创建简单 Canvas + TextMeshPro 提示 UI；
/// - 创建一个测试用 TestInteractable 方块。
/// 可通过菜单 Tools/交互系统/搭建交互测试场景 重复执行（幂等）。
/// </summary>
public static class InteractionSystemSetup
{
    public const string InteractableLayerName = "Interactable";
    public const string ScenePath = "Assets/Game/Scenes/SC_Bedroom.unity";
    public const string InputActionsPath = "Assets/Input/IA_Gameplay.inputactions";
    public const string CanvasName = "UI_InteractionPrompt";
    public const string TextName = "PromptText";
    public const string TestCubeName = "Interactable_Test";

    [MenuItem("Tools/交互系统/搭建交互测试场景")]
    public static void SetupViaMenu()
    {
        RunFullSetup();
        Debug.Log("[交互系统] 搭建完成。");
    }

    /// <summary>完整搭建流程，返回 Interactable 所在的 Layer 索引。</summary>
    public static int RunFullSetup()
    {
        EnsureTMPEssentials();
        int layer = EnsureInteractableLayer();

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SetupScene(layer);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        return layer;
    }

    /// <summary>确保存在 Interactable 用户 Layer，返回其索引（优先 Layer 6）。</summary>
    public static int EnsureInteractableLayer()
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
        var tagManager = new SerializedObject(assets[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        int firstEmptyUserLayer = -1;
        for (int i = 6; i < layers.arraySize; i++)
        {
            SerializedProperty element = layers.GetArrayElementAtIndex(i);
            if (element.stringValue == InteractableLayerName)
            {
                return i;
            }

            if (firstEmptyUserLayer < 0 && string.IsNullOrEmpty(element.stringValue))
            {
                firstEmptyUserLayer = i;
            }
        }

        if (firstEmptyUserLayer < 0)
        {
            throw new InvalidOperationException("项目中没有空闲的 User Layer（6~31）可用于 Interactable。");
        }

        layers.GetArrayElementAtIndex(firstEmptyUserLayer).stringValue = InteractableLayerName;
        tagManager.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
        Debug.Log("[交互系统] 已创建 Layer：" + InteractableLayerName + "（User Layer " + firstEmptyUserLayer + "）。");
        return firstEmptyUserLayer;
    }

    /// <summary>TMP 文本依赖 TMP Settings 与默认字体（Essential Resources），缺失则自动导入。</summary>
    public static void EnsureTMPEssentials()
    {
        if (TMP_Settings.instance != null && TMP_Settings.defaultFontAsset != null)
        {
            return;
        }

        // 在包资源中定位 TMP Essential Resources.unitypackage（包内容以 Packages/ 路径可被 AssetDatabase 检索）
        string packageAssetPath = null;
        string[] guids = AssetDatabase.FindAssets("TMP Essential Resources");
        foreach (string guid in guids)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (assetPath.EndsWith("TMP Essential Resources.unitypackage", StringComparison.Ordinal))
            {
                packageAssetPath = assetPath;
                break;
            }
        }

        UnityEditor.PackageManager.PackageInfo info = packageAssetPath != null
            ? UnityEditor.PackageManager.PackageInfo.FindForAssetPath(packageAssetPath)
            : UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
        if (info == null)
        {
            throw new InvalidOperationException("找不到 TextMeshPro 所在的包（com.unity.ugui）。");
        }

        string packagePath = Path.Combine(
            info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
        if (!File.Exists(packagePath))
        {
            throw new FileNotFoundException("未找到 TMP Essential Resources unitypackage：" + packagePath);
        }

        Debug.Log("[交互系统] 正在导入 TMP Essential Resources ...");
#pragma warning disable CS0618 // 新 API 在部分小版本不可用时回退旧 API
        try
        {
            UnityEditor.AssetPackage.Package.Import(packagePath, false);
        }
        catch (Exception)
        {
            AssetDatabase.ImportPackage(packagePath, false);
        }
#pragma warning restore CS0618
        AssetDatabase.Refresh();

        if (TMP_Settings.instance == null || TMP_Settings.defaultFontAsset == null)
        {
            throw new InvalidOperationException(
                "TMP Essential Resources 导入后仍找不到 TMP_Settings 或默认字体，请手动执行 Window > TextMeshPro > Import TMP Essential Resources。");
        }
    }

    private static void SetupScene(int interactableLayer)
    {
        var inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
        if (inputActions == null)
        {
            throw new FileNotFoundException("未找到输入资产：" + InputActionsPath);
        }

        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            throw new InvalidOperationException("场景中找不到名为 Player 的对象。");
        }

        Transform cameraRoot = FindChildByName(player.transform, "CameraRoot");
        Camera playerCamera = player.GetComponentInChildren<Camera>(true);

        // 场景中既有的 FirstPersonController 的 cameraRoot 引用原本为空，补全它（不改其代码与行为）
        var movement = player.GetComponent<FirstPersonController>();
        if (movement != null && cameraRoot != null)
        {
            var movementSerialized = new SerializedObject(movement);
            SerializedProperty cameraRootProperty = movementSerialized.FindProperty("cameraRoot");
            if (cameraRootProperty.objectReferenceValue == null)
            {
                cameraRootProperty.objectReferenceValue = cameraRoot;
                movementSerialized.ApplyModifiedProperties();
                Debug.Log("[交互系统] 已补全 FirstPersonController 的 cameraRoot 引用（原本为空）。");
            }
        }

        InteractionPromptUI promptUI = EnsurePromptUI();
        ConfigureInteractor(player, inputActions, playerCamera, promptUI, interactableLayer);
        EnsureTestCube(player.transform, interactableLayer);
    }

    private static InteractionPromptUI EnsurePromptUI()
    {
        var existingUI = UnityEngine.Object.FindAnyObjectByType<InteractionPromptUI>();

        GameObject canvasGameObject;
        InteractionPromptUI promptUI;
        if (existingUI != null)
        {
            canvasGameObject = existingUI.gameObject;
            promptUI = existingUI;
        }
        else
        {
            canvasGameObject = new GameObject(CanvasName);

            var canvas = canvasGameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGameObject.AddComponent<GraphicRaycaster>();
            promptUI = canvasGameObject.AddComponent<InteractionPromptUI>();
        }

        TMP_Text promptText = promptUI.GetComponentInChildren<TMP_Text>(true);
        if (promptText == null)
        {
            var textGameObject = new GameObject(TextName, typeof(RectTransform));
            textGameObject.transform.SetParent(canvasGameObject.transform, false);

            var rectTransform = textGameObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0f);
            rectTransform.anchorMax = new Vector2(0.5f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.anchoredPosition = new Vector2(0f, 120f);
            rectTransform.sizeDelta = new Vector2(900f, 80f);

            promptText = textGameObject.AddComponent<TextMeshProUGUI>();
        }

        promptText.font = TMP_Settings.defaultFontAsset;
        promptText.fontSize = 30f;
        promptText.alignment = TextAlignmentOptions.Center;
        promptText.color = Color.white;
        promptText.text = string.Empty;
        promptText.gameObject.SetActive(false);

        var uiSerialized = new SerializedObject(promptUI);
        uiSerialized.FindProperty("promptText").objectReferenceValue = promptText;
        uiSerialized.ApplyModifiedProperties();

        return promptUI;
    }

    private static void ConfigureInteractor(
        GameObject player,
        InputActionAsset inputActions,
        Camera playerCamera,
        InteractionPromptUI promptUI,
        int interactableLayer)
    {
        var interactor = player.GetComponent<PlayerInteractor>();
        if (interactor == null)
        {
            interactor = player.AddComponent<PlayerInteractor>();
        }

        var serialized = new SerializedObject(interactor);
        serialized.FindProperty("inputActions").objectReferenceValue = inputActions;
        serialized.FindProperty("playerCamera").objectReferenceValue = playerCamera;
        serialized.FindProperty("promptUI").objectReferenceValue = promptUI;
        serialized.FindProperty("interactRange").floatValue = 2f;
        serialized.FindProperty("interactableMask").intValue = 1 << interactableLayer;
        serialized.FindProperty("triggerInteraction").enumValueIndex = (int)QueryTriggerInteraction.Ignore;
        serialized.ApplyModifiedProperties();
    }

    private static void EnsureTestCube(Transform player, int interactableLayer)
    {
        var existing = UnityEngine.Object.FindAnyObjectByType<TestInteractable>();
        GameObject cube;
        TestInteractable test;
        bool newlyCreated;

        if (existing != null)
        {
            cube = existing.gameObject;
            test = existing;
            newlyCreated = false;
        }
        else
        {
            cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = TestCubeName;
            cube.transform.localScale = Vector3.one * 0.3f;
            test = cube.AddComponent<TestInteractable>();
            newlyCreated = true;
        }

        SetLayerRecursively(cube, interactableLayer);

        var serialized = new SerializedObject(test);
        // 默认字体 LiberationSans SDF 不含中文字形，提示先用英文保证可见；接入中文字体后可改中文
        serialized.FindProperty("interactionPrompt").stringValue = "E - Test Interact";
        serialized.FindProperty("allowInteract").boolValue = true;
        serialized.ApplyModifiedProperties();

        if (newlyCreated)
        {
            cube.transform.position = FindFreePosition(player);
            Debug.Log("[交互系统] 测试物件位置：" + cube.transform.position + "。");
        }
    }

    /// <summary>沿玩家正前方、与视线齐平高度寻找不与现有几何体穿插的位置。</summary>
    private static Vector3 FindFreePosition(Transform player)
    {
        const float eyeHeight = 1.55f;
        Vector3 halfExtents = Vector3.one * 0.18f;

        for (float distance = 1.2f; distance <= 2.2f; distance += 0.2f)
        {
            Vector3 candidate = player.position + player.forward * distance + Vector3.up * eyeHeight;
            Collider[] overlaps = Physics.OverlapBox(
                candidate, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            if (overlaps.Length == 0)
            {
                return candidate;
            }
        }

        return player.position + player.forward * 1.4f + Vector3.up * eyeHeight;
    }

    private static Transform FindChildByName(Transform root, string targetName)
    {
        if (root.name == targetName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildByName(root.GetChild(i), targetName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
