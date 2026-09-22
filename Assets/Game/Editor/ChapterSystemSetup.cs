using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 任务5「三章顺序状态管理」的场景配置工具（增量、幂等，不重建场景）：
/// - 依赖任务4产物 SmallBoxPlacement（书架顶、鱼缸旁的小盒放置点）；缺失时先调用
///   <see cref="ItemPlacementSetup.Setup"/> 补全（已存在则原样复用，不重建）；
/// - 在场景根创建/复用 ChapterManager 物体并挂载 <see cref="ChapterManager"/>；
/// - 明确绑定第一章的真实完成条件：lostPetPlacement = SmallBoxPlacement 上的 ItemPlacement，
///   lostPetRequiredItem = SmallBox；
/// - 第二、第三章不在本工具创建任何对象（只存在于 ChapterManager 的状态数据中，初始 Locked）。
/// 菜单：Tools/交互系统/配置章节状态系统（任务5）。
/// </summary>
public static class ChapterSystemSetup
{
    public const string ManagerName = "ChapterManager";

    [MenuItem("Tools/交互系统/配置章节状态系统（任务5）")]
    public static void SetupViaMenu()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[章节系统] 配置完成并已保存场景。");
    }

    /// <summary>batchmode 入口：打开场景 → 配置 → 保存 → 退出。</summary>
    public static void RunBatch()
    {
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ItemPlacementSetup.ScenePath, OpenSceneMode.Single);
            Setup();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[章节系统] batchmode 配置完成并已保存场景。");
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[章节系统] batchmode 配置失败：" + e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>执行全部配置（要求 SC_Bedroom 为当前活动场景），幂等可重复执行。</summary>
    public static ChapterManager Setup()
    {
        // 依赖任务4：放置点缺失时先补全；已存在则不重建，保留场景当前状态
        ItemPlacement placement = FindSmallBoxPlacement();
        if (placement == null)
        {
            ItemPlacementSetup.Setup();
            placement = FindSmallBoxPlacement();
        }

        if (placement == null)
        {
            throw new System.InvalidOperationException(
                "找不到 " + ItemPlacementSetup.PlacementName + " 上的 ItemPlacement，章节系统无法绑定第一章完成条件。");
        }

        ChapterManager manager = EnsureManager();
        BindLostPet(manager, placement);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return manager;
    }

    // ---------- 查找放置点 ----------

    /// <summary>在书架层级中定位任务4创建的 SmallBoxPlacement（根节点上的 ItemPlacement）。</summary>
    private static ItemPlacement FindSmallBoxPlacement()
    {
        GameObject shelf = GameObject.Find(ItemPlacementSetup.ShelfName);
        if (shelf == null)
        {
            // 书架都不存在时，交给 ItemPlacementSetup 整体补全
            return null;
        }

        Transform node = shelf.transform.Find(ItemPlacementSetup.PlacementName);
        if (node == null)
        {
            return null;
        }

        return node.GetComponent<ItemPlacement>();
    }

    // ---------- ChapterManager 物体 ----------

    private static ChapterManager EnsureManager()
    {
        GameObject managerObject = GameObject.Find(ManagerName);
        if (managerObject == null)
        {
            managerObject = new GameObject(ManagerName);
            Undo.RegisterCreatedObjectUndo(managerObject, "配置章节状态系统");
            Debug.Log("[章节系统] 已创建场景根物体 " + ManagerName + "。");
        }

        ChapterManager manager = managerObject.GetComponent<ChapterManager>();
        if (manager == null)
        {
            manager = Undo.AddComponent<ChapterManager>(managerObject);
        }

        return manager;
    }

    /// <summary>绑定第一章的真实放置目标与所需物品（Inspector 明确引用，运行时不搜索）。</summary>
    private static void BindLostPet(ChapterManager manager, ItemPlacement placement)
    {
        var serialized = new SerializedObject(manager);
        serialized.FindProperty("lostPetPlacement").objectReferenceValue = placement;
        serialized.FindProperty("lostPetRequiredItem").enumValueIndex = (int)ItemId.SmallBox;
        serialized.ApplyModifiedProperties();

        Debug.Log("[章节系统] 已绑定第一章完成条件：" + ManagerName
            + ".lostPetPlacement = " + placement.gameObject.name
            + "（要求 " + ItemId.SmallBox + " 真实放置成功）。");
    }
}
