using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 任务4「指定位置放置物品」的场景配置工具（增量、幂等，不重建场景）：
/// - 复用场景中【已存在】的真实道具：MyBedroomBookshelf（书架）与 GlassBowl（玻璃鱼缸，放在书架顶上）；
///   本工具【不创建任何鱼缸/标记等自造可见物件】，只创建放置交互所必需的最小对象：
///     MyBedroomBookshelf（场景已有）
///     └── SmallBoxPlacement        （挂 ItemPlacement 组件，位于书架顶、鱼缸旁边）
///         ├── InteractionCollider  （Interactable 层实体 BoxCollider，隐形射线目标）
///         └── PlacedSmallBox       （展示物件，初始隐藏；其下 SmallBox_Model 为小盒模型）
/// - 放置坐标由书架/鱼缸的真实 Renderer Bounds 计算：与鱼缸处于同一顶面、水平相邻且落在书架顶面范围内；
/// - 所有引用均 Inspector 明确绑定（playerInventory / displayObject / interactionCollider），运行时不搜索；
/// - ItemPlacement 组件挂在 SmallBoxPlacement 根节点：PlayerInteractor 用
///   hit.collider.GetComponentInParent 从 InteractionCollider 向上查找，能命中根节点的组件。
/// 菜单：Tools/交互系统/配置任务道具放置（任务4）。
/// </summary>
public static class ItemPlacementSetup
{
    public const string PlacementName = "SmallBoxPlacement";
    public const string ColliderName = "InteractionCollider";
    public const string DisplayName = "PlacedSmallBox";
    public const string DisplayModelName = "SmallBox_Model";
    public const string ScenePath = "Assets/Game/Scenes/SC_Bedroom.unity";

    public const string ShelfName = "MyBedroomBookshelf";
    public const string BowlName = "GlassBowl";

    private const string BoxMaterialPath = "Assets/Game/Prefabs/M_SmallBox.mat";

    // 旧版本（v1/v2）自造的对象与材质，回退时删除
    private const string LegacyAreaName = "FishTankArea";
    private static readonly string[] LegacyMaterialPaths =
    {
        "Assets/Game/Prefabs/M_TankGlass.mat",
        "Assets/Game/Prefabs/M_PlacementSpot.mat"
    };
    private const string LegacyDiagPath = "Assets/Game/Editor/DiagReportGeometry.cs";

    // 展示小盒：0.25m 见方（与拾取小盒一致）
    private const float BoxSize = 0.25f;
    private const float BoxHalf = BoxSize * 0.5f;
    private const float SideGap = 0.05f;   // 与鱼缸之间的留缝
    private const float ShelfMargin = 0.02f; // 小盒与书架边缘的最小内缩

    [MenuItem("Tools/交互系统/配置任务道具放置（任务4）")]
    public static void SetupViaMenu()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[道具放置] 配置完成并已保存场景。");
    }

    /// <summary>batchmode 入口：打开场景 → 配置 → 保存 → 退出。</summary>
    public static void RunBatch()
    {
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Setup();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[道具放置] batchmode 配置完成并已保存场景。");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[道具放置] batchmode 配置失败：" + e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>执行全部配置（要求 SC_Bedroom 为当前活动场景），幂等可重复执行。</summary>
    public static void Setup()
    {
        int interactableLayer = InteractionSystemSetup.EnsureInteractableLayer();
        InteractionSystemSetup.EnsureTMPEssentials();

        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            throw new System.InvalidOperationException("场景中找不到名为 Player 的物体。");
        }

        // 依赖任务3产物：Inventory 与拾取小盒缺失时先补全（幂等，已存在则复用）
        if (player.GetComponent<Inventory>() == null || GameObject.Find("Pickup_SmallBox") == null)
        {
            InventorySystemSetup.Setup();
        }

        Inventory inventory = player.GetComponent<Inventory>();
        if (inventory == null)
        {
            throw new System.InvalidOperationException("Player 上仍找不到 Inventory，无法绑定放置点。");
        }

        // ---- 回退旧版本自造的可见物件 ----
        RollbackLegacy();

        // ---- 场景中已有的真实书架与鱼缸 ----
        GameObject shelf = GameObject.Find(ShelfName);
        GameObject bowl = GameObject.Find(BowlName);
        if (shelf == null)
        {
            throw new System.InvalidOperationException("场景中找不到 " + ShelfName + "，无法在书架上配置放置点。");
        }
        if (bowl == null)
        {
            throw new System.InvalidOperationException("场景中找不到 " + BowlName + "，无法在鱼缸旁配置放置点。");
        }

        Bounds shelfBounds = CombinedBounds(shelf);
        Bounds bowlBounds = CombinedBounds(bowl);

        // 放置点：书架顶面（与鱼缸底面同高）、鱼缸水平相邻、落在书架顶面范围内
        Vector3 spot = ComputeSpot(shelfBounds, bowlBounds);

        EnsurePlacement(shelf.transform, spot, interactableLayer, inventory);
    }

    // ---------- 回退 ----------

    private static void RollbackLegacy()
    {
        // 删除旧版 FishTankArea 整棵层级（含 Tank_Body / PlacementMarker / 旧放置点）
        GameObject legacyArea = GameObject.Find(LegacyAreaName);
        if (legacyArea != null)
        {
            Object.DestroyImmediate(legacyArea);
            Debug.Log("[道具放置] 已回退旧版 " + LegacyAreaName + " 层级。");
        }

        // 兼容：若书架下已残留旧放置节点，先删除（保证幂等）
        GameObject shelfNow = GameObject.Find(ShelfName);
        if (shelfNow != null)
        {
            Transform oldNode = shelfNow.transform.Find(PlacementName);
            if (oldNode != null)
            {
                Object.DestroyImmediate(oldNode.gameObject);
                Debug.Log("[道具放置] 已删除书架下残留的 " + PlacementName + "。");
            }
        }

        foreach (string matPath in LegacyMaterialPaths)
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(matPath) != null)
            {
                AssetDatabase.DeleteAsset(matPath);
                Debug.Log("[道具放置] 已回退材质 " + matPath + "。");
            }
        }

        if (AssetDatabase.LoadAssetAtPath<Object>(LegacyDiagPath) != null)
        {
            AssetDatabase.DeleteAsset(LegacyDiagPath);
            Debug.Log("[道具放置] 已删除一次性诊断脚本。");
        }
    }

    // ---------- 几何计算 ----------

    private static Bounds CombinedBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.zero);
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        return b;
    }

    /// <summary>在书架顶面、鱼缸旁边选一个确定的放置中心；找不到合法位置时抛错（不静默）。</summary>
    private static Vector3 ComputeSpot(Bounds shelfBounds, Bounds bowlBounds)
    {
        float surfaceY = bowlBounds.min.y; // 鱼缸底面即书架顶面
        Vector3 bowlCenter = bowlBounds.center;

        // 水平方向上鱼缸的最大外接半径，盒子放在该半径之外并留缝
        float bowlRadius = Mathf.Max(bowlBounds.extents.x, bowlBounds.extents.z);
        float distance = bowlRadius + BoxHalf + SideGap;

        // 固定尝试顺序，保证结果确定；书架为轴对齐世界 bounds
        Vector3[] directions =
        {
            Vector3.right, Vector3.left, Vector3.forward, Vector3.back
        };

        foreach (Vector3 dir in directions)
        {
            Vector3 candidate = bowlCenter + dir * distance;
            candidate.y = surfaceY + BoxHalf;

            if (!IsOnShelf(shelfBounds, candidate))
            {
                continue;
            }

            if (OverlapsBowl(bowlBounds, candidate))
            {
                continue;
            }

            Debug.Log("[道具放置] 放置点=" + candidate + "（书架顶、鱼缸旁，方向 "
                + dir + "，顶面 y=" + surfaceY + "）。");
            return candidate;
        }

        throw new System.InvalidOperationException(
            "无法在书架顶面、鱼缸旁边找到合法放置位置。书架 bounds=" + shelfBounds
            + "；鱼缸 bounds=" + bowlBounds
            + "。请检查书架顶面空间是否被占满。");
    }

    /// <summary>小盒（含边距）是否完整落在书架顶面水平范围内。</summary>
    private static bool IsOnShelf(Bounds shelfBounds, Vector3 boxCenter)
    {
        float minX = shelfBounds.min.x + BoxHalf + ShelfMargin;
        float maxX = shelfBounds.max.x - BoxHalf - ShelfMargin;
        float minZ = shelfBounds.min.z + BoxHalf + ShelfMargin;
        float maxZ = shelfBounds.max.z - BoxHalf - ShelfMargin;

        return boxCenter.x >= minX && boxCenter.x <= maxX
            && boxCenter.z >= minZ && boxCenter.z <= maxZ;
    }

    /// <summary>小盒是否与鱼缸水平重叠（含留缝）。</summary>
    private static bool OverlapsBowl(Bounds bowlBounds, Vector3 boxCenter)
    {
        float dx = Mathf.Abs(boxCenter.x - bowlBounds.center.x);
        float dz = Mathf.Abs(boxCenter.z - bowlBounds.center.z);

        bool overlapX = dx < bowlBounds.extents.x + BoxHalf + SideGap;
        bool overlapZ = dz < bowlBounds.extents.z + BoxHalf + SideGap;
        return overlapX && overlapZ;
    }

    // ---------- 创建放置层级 ----------

    private static void EnsurePlacement(Transform shelf, Vector3 spot, int interactableLayer, Inventory inventory)
    {
        // 幂等：已存在则先移除（RollbackLegacy 已处理书架直属节点，这里再兜底）
        Transform existing = shelf.Find(PlacementName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing.gameObject);
        }

        var nodeObject = new GameObject(PlacementName);
        Undo.RegisterCreatedObjectUndo(nodeObject, "配置任务道具放置");
        Transform node = nodeObject.transform;
        node.SetParent(shelf, true);
        node.position = spot;
        node.rotation = Quaternion.identity;

        // ItemPlacement 挂在 SmallBoxPlacement 根节点（InteractionCollider 的父级）
        ItemPlacement itemPlacement = Undo.AddComponent<ItemPlacement>(nodeObject);

        // InteractionCollider：Interactable 层、实体 BoxCollider（PlayerInteractor 忽略 Trigger，必须实体）
        var colliderObject = new GameObject(ColliderName);
        Undo.RegisterCreatedObjectUndo(colliderObject, "配置任务道具放置");
        Transform colliderTransform = colliderObject.transform;
        colliderTransform.SetParent(node, false);
        colliderTransform.localPosition = Vector3.zero;
        colliderTransform.localRotation = Quaternion.identity;
        colliderTransform.localScale = Vector3.one;
        SetLayerRecursively(colliderObject, interactableLayer);

        BoxCollider boxCollider = Undo.AddComponent<BoxCollider>(colliderObject);
        var colliderSerialized = new SerializedObject(boxCollider);
        colliderSerialized.FindProperty("m_IsTrigger").boolValue = false;
        colliderSerialized.FindProperty("m_Size").vector3Value = Vector3.one * BoxSize;
        colliderSerialized.FindProperty("m_Center").vector3Value = Vector3.zero;
        colliderSerialized.ApplyModifiedProperties();

        // PlacedSmallBox：展示物件，初始隐藏；其下 SmallBox_Model 为小盒模型（无碰撞体，纯展示）
        var displayObject = new GameObject(DisplayName);
        Undo.RegisterCreatedObjectUndo(displayObject, "配置任务道具放置");
        Transform display = displayObject.transform;
        display.SetParent(node, false);
        display.localPosition = Vector3.zero;
        displayObject.SetActive(false); // 初始隐藏（ItemPlacement.Awake 也会兜底）

        EnsureDisplayModel(display);

        // 绑定全部引用（Inspector 明确绑定，运行时不搜索）
        var serialized = new SerializedObject(itemPlacement);
        serialized.FindProperty("requiredItemId").enumValueIndex = (int)ItemId.SmallBox;
        serialized.FindProperty("displayObject").objectReferenceValue = displayObject;
        serialized.FindProperty("playerInventory").objectReferenceValue = inventory;
        serialized.FindProperty("promptFormat").stringValue = "E - Place {0}";
        serialized.FindProperty("lockedPromptFormat").stringValue = "Need {0}";
        serialized.FindProperty("interactionCollider").objectReferenceValue = boxCollider;
        serialized.ApplyModifiedProperties();

        Debug.Log("[道具放置] 已在书架顶、鱼缸旁创建 " + PlacementName + " 并绑定全部引用。");
    }

    private static void EnsureDisplayModel(Transform display)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = DisplayModelName;

        // 展示物件不需要碰撞体：交互只走 InteractionCollider
        Collider cubeCollider = cube.GetComponent<Collider>();
        if (cubeCollider != null)
        {
            Object.DestroyImmediate(cubeCollider);
        }

        Undo.RegisterCreatedObjectUndo(cube, "配置任务道具放置");
        Transform model = cube.transform;
        model.SetParent(display, false);
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one * BoxSize; // 0.25m 见方

        MeshRenderer renderer = model.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(BoxMaterialPath);
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
            else
            {
                Debug.LogWarning("[道具放置] 找不到小盒材质 " + BoxMaterialPath
                    + "，SmallBox_Model 使用默认材质。");
            }
        }
    }

    // ---------- 辅助 ----------

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }
}
