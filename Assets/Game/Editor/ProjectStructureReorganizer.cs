using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 按约定的项目目录结构归类已有资产（GUID 与 .meta 全部保留，场景引用不受影响）：
/// - 场景 Assets/Scenes → Assets/Game/Scenes；
/// - 交互提示 UI 脚本 → Assets/Game/Scripts/UI；
/// - Tripo 模型目录 Assets/Models → Assets/TripoAssets；
/// - 补齐 Scripts/Items、Scripts/Chapters、Prefabs 等预留目录。
/// 菜单：Tools/项目结构/按规范归类目录（幂等，可重复执行）。
/// </summary>
public static class ProjectStructureReorganizer
{
    private const string OldScenePath = "Assets/Scenes/SC_Bedroom.unity";
    private const string NewScenePath = "Assets/Game/Scenes/SC_Bedroom.unity";

    private const string OldPromptUIPath =
        "Assets/Game/Scripts/Interaction/InteractionPromptUI.cs";
    private const string NewPromptUIPath =
        "Assets/Game/Scripts/UI/InteractionPromptUI.cs";

    private const string OldModelsPath = "Assets/Models";
    private const string NewModelsPath = "Assets/TripoAssets";

    [MenuItem("Tools/项目结构/按规范归类目录")]
    public static void ReorganizeViaMenu()
    {
        Reorganize();
    }

    public static void Reorganize()
    {
        EnsureFolder("Assets/Game/Scenes");
        EnsureFolder("Assets/Game/Scripts/UI");
        EnsureFolder("Assets/Game/Scripts/Items");
        EnsureFolder("Assets/Game/Scripts/Chapters");
        EnsureFolder("Assets/Game/Prefabs");

        MoveAsset(OldScenePath, NewScenePath);
        MoveAsset(OldPromptUIPath, NewPromptUIPath);
        MoveAsset(OldModelsPath, NewModelsPath);

        UpdateBuildSettingsScenePath();
        RemoveEmptyOldScenesFolder();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[项目结构] 归类完成：场景已移入 Game/Scenes，UI 脚本已移入 Scripts/UI，"
                  + "Tripo 模型已移入 TripoAssets，并补齐 Items/Chapters/Prefabs 预留目录。");
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string folderName = Path.GetFileName(folderPath);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
        {
            Debug.LogError("[项目结构] 无法创建目录：" + folderPath);
            return;
        }

        if (!AssetDatabase.IsValidFolder(parent))
        {
            EnsureFolder(parent);
        }

        AssetDatabase.CreateFolder(parent, folderName);
    }

    private static void MoveAsset(string fromPath, string toPath)
    {
        bool fromExists = AssetDatabase.AssetPathExists(fromPath);
        bool toExists = AssetDatabase.AssetPathExists(toPath);

        if (!fromExists)
        {
            if (toExists)
            {
                // 已经在目标位置，无需处理
                return;
            }

            Debug.LogWarning("[项目结构] 源资产不存在，跳过：" + fromPath);
            return;
        }

        if (toExists)
        {
            Debug.LogWarning("[项目结构] 目标位置已存在资产，未移动：" + toPath);
            return;
        }

        string error = AssetDatabase.MoveAsset(fromPath, toPath);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError("[项目结构] 移动失败：" + fromPath + " → " + toPath + "（" + error + "）");
        }
        else
        {
            Debug.Log("[项目结构] 已移动：" + fromPath + " → " + toPath);
        }
    }

    private static void UpdateBuildSettingsScenePath()
    {
        var scenes = EditorBuildSettings.scenes;
        bool changed = false;

        foreach (EditorBuildSettingsScene scene in scenes)
        {
            if (scene.path == OldScenePath)
            {
                scene.path = NewScenePath;
                changed = true;
            }
        }

        if (changed)
        {
            EditorBuildSettings.scenes = scenes;
            Debug.Log("[项目结构] 已同步 Build Settings 中的场景路径。");
        }
    }

    private static void RemoveEmptyOldScenesFolder()
    {
        const string oldFolder = "Assets/Scenes";
        if (!AssetDatabase.IsValidFolder(oldFolder))
        {
            return;
        }

        string absolute = Path.GetFullPath(oldFolder);
        if (Directory.GetFiles(absolute, "*", SearchOption.AllDirectories).Length > 0)
        {
            Debug.LogWarning("[项目结构] 旧目录 " + oldFolder + " 非空，未删除，请人工检查。");
            return;
        }

        AssetDatabase.DeleteAsset(oldFolder);
        Debug.Log("[项目结构] 已移除空目录 " + oldFolder + "。");
    }
}
