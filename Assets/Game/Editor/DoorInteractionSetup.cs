using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 卧室门交互的场景配置工具（增量配置，不重建场景）：
/// - 在 Door 根下创建门铰链节点 Door_Hinge（位置自动取门板靠门框的一侧竖边）；
/// - 把门板 FBX 实例（含既有 Doorcollision 碰撞体）移到铰链下；
/// - 门板相关对象设为 Interactable Layer（门框保持 Default，对交互射线透明）；
/// - 在铰链上挂载并配置 DoorInteractable。
/// 菜单：Tools/交互系统/配置卧室门交互（幂等）。
/// </summary>
public static class DoorInteractionSetup
{
    public const string DoorRootName = "Door";
    public const string HingeName = "Door_Hinge";
    public const string BlockerName = "Doorcollision";

    // 门板 FBX（SM_MyBedroomDoor.fbx）与门框 FBX（SM_Doorframe.fbx）的 GUID
    private const string DoorPanelFbxGuid = "1705f5cb6762385458b0a3c2e50d565d";
    private const string DoorFrameFbxGuid = "cc49a97893f10364d8cf34e611cd6d53";

    [MenuItem("Tools/交互系统/配置卧室门交互")]
    public static void SetupViaMenu()
    {
        Setup();
        EditorSceneManager.SaveOpenScenes();
        Debug.Log("[卧室门] 配置完成并已保存场景。");
    }

    /// <summary>执行配置，返回铰链上的 DoorInteractable。</summary>
    public static DoorInteractable Setup()
    {
        int layer = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0])
            .FindProperty("layers")
            .GetLayerIndex(InteractionSystemSetup.InteractableLayerName);
        if (layer < 0)
        {
            layer = InteractionSystemSetup.EnsureInteractableLayer();
        }

        GameObject doorRoot = FindRootObject(DoorRootName);
        if (doorRoot == null)
        {
            throw new InvalidOperationException("场景中找不到名为 " + DoorRootName + " 的根物体。");
        }

        Transform panel = FindInstanceBySourceGuid(doorRoot.transform, DoorPanelFbxGuid);
        Transform frame = FindInstanceBySourceGuid(doorRoot.transform, DoorFrameFbxGuid);
        if (panel == null)
        {
            throw new InvalidOperationException("Door 下找不到门板 FBX 实例（GUID " + DoorPanelFbxGuid + "）。");
        }

        Transform blocker = FindChildByName(panel, BlockerName);
        var blockerCollider = blocker != null ? blocker.GetComponent<Collider>() : null;
        if (blockerCollider == null)
        {
            throw new InvalidOperationException("门板下找不到 " + BlockerName + " 碰撞体。");
        }

        // 1) 铰链节点（已存在则复用）
        Transform hinge = FindChildByName(doorRoot.transform, HingeName);
        if (hinge == null)
        {
            hinge = CreateHinge(doorRoot, panel, frame, blockerCollider);
            // 门板移到铰链下：worldPositionStays 保持世界变换；先登记撤销状态
            Undo.RegisterFullObjectHierarchyUndo(panel.gameObject, "配置卧室门铰链");
            panel.SetParent(hinge, true);
        }

        // 2) 门板（含碰撞体）设为 Interactable Layer；门框保持 Default
        SetLayerRecursively(panel.gameObject, layer);

        // 3) 挂载并配置 DoorInteractable
        var door = hinge.GetComponent<DoorInteractable>();
        if (door == null)
        {
            door = Undo.AddComponent<DoorInteractable>(hinge.gameObject);
        }

        ConfigureDoor(door);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        return door;
    }

    private static Transform CreateHinge(
        GameObject doorRoot,
        Transform panel,
        Transform frame,
        Collider blockerCollider)
    {
        Transform blockerTransform = blockerCollider.transform;
        Bounds slabBounds = blockerCollider.bounds;

        // 门板宽度方向（碰撞体本地 X，scale 0.352）投影到水平面
        Vector3 widthDir = Vector3.ProjectOnPlane(
            blockerTransform.TransformDirection(Vector3.right), Vector3.up).normalized;

        // 门板半宽（本地 X：lossyScale.x × collider size.x）
        float halfWidth = 0.5f * Mathf.Abs(blockerTransform.lossyScale.x)
                          * blockerCollider.GetLocalSize().x;

        Vector3 center = slabBounds.center;
        Vector3 edgePlus = center + widthDir * halfWidth;
        Vector3 edgeMinus = center - widthDir * halfWidth;

        // 与门框各网格 AABB 的最近距离打分，取更贴近门框的一侧为铰链
        float scorePlus = ScoreEdge(edgePlus, frame);
        float scoreMinus = ScoreEdge(edgeMinus, frame);

        Vector3 hingePoint;
        if (Mathf.Abs(scorePlus - scoreMinus) < 0.03f)
        {
            // 无法区分时默认取 -widthDir 侧（可在 Inspector 用 openDirection 或重建调整）
            hingePoint = edgeMinus;
        }
        else
        {
            hingePoint = scorePlus < scoreMinus ? edgePlus : edgeMinus;
        }

        hingePoint.y = center.y;

        var hingeObject = new GameObject(HingeName);
        Undo.RegisterCreatedObjectUndo(hingeObject, "配置卧室门铰链");
        hingeObject.transform.SetParent(doorRoot.transform, true);
        hingeObject.transform.SetPositionAndRotation(hingePoint, panel.rotation);

        Debug.Log("[卧室门] 已创建铰链 " + HingeName + "，位置 " + hingePoint
                  + "（两侧贴合度：+" + scorePlus.ToString("F3") + " / -" + scoreMinus.ToString("F3") + "）。");
        return hingeObject.transform;
    }

    private static float ScoreEdge(Vector3 edgePoint, Transform frame)
    {
        if (frame == null)
        {
            return float.MaxValue;
        }

        float best = float.MaxValue;
        foreach (Renderer renderer in frame.GetComponentsInChildren<Renderer>(true))
        {
            Bounds bounds = renderer.bounds;
            Vector3 closest = bounds.ClosestPoint(edgePoint);
            best = Mathf.Min(best, Vector3.Distance(edgePoint, closest));
        }

        return best;
    }

    private static void ConfigureDoor(DoorInteractable door)
    {
        var serialized = new SerializedObject(door);
        serialized.FindProperty("openAngle").floatValue = 90f;
        serialized.FindProperty("openDuration").floatValue = 0.6f;
        serialized.FindProperty("openDirection").intValue = 1;
        serialized.FindProperty("closedPrompt").stringValue = "E - Open Door";
        serialized.FindProperty("openPrompt").stringValue = "E - Close Door";

        // 基类的默认提示字段也同步（GetInteractionPrompt 已重写，仅为 Inspector 整洁）
        SerializedProperty basePrompt = serialized.FindProperty("interactionPrompt");
        if (basePrompt != null)
        {
            basePrompt.stringValue = "E - Open Door";
        }

        serialized.ApplyModifiedProperties();
    }

    // ---------- 辅助 ----------

    private static GameObject FindRootObject(string objectName)
    {
        foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>())
        {
            if (go.name == objectName && go.transform.parent == null)
            {
                return go;
            }
        }

        // Door 的父级是 Walls（非根），放宽到场景任意层级
        foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>())
        {
            if (go.name == objectName)
            {
                return go;
            }
        }

        return null;
    }

    private static Transform FindInstanceBySourceGuid(Transform root, string sourceGuid)
    {
        // 递归查找：首次配置时门板是 Door 的直接子物体；配置后门板位于 Door_Hinge 之下
        return FindInstanceRecursive(root, sourceGuid);
    }

    private static Transform FindInstanceRecursive(Transform node, string sourceGuid)
    {
        for (int i = 0; i < node.childCount; i++)
        {
            Transform child = node.GetChild(i);

            string assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(child.gameObject);
            if (!string.IsNullOrEmpty(assetPath)
                && AssetDatabase.AssetPathToGUID(assetPath) == sourceGuid)
            {
                return child;
            }

            Transform deeper = FindInstanceRecursive(child, sourceGuid);
            if (deeper != null)
            {
                return deeper;
            }
        }

        return null;
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

/// <summary>SerializedProperty / Collider 的小扩展，避免主流程里写反射细节。</summary>
internal static class DoorSetupExtensions
{
    public static int GetLayerIndex(this SerializedProperty layersProperty, string layerName)
    {
        for (int i = 0; i < layersProperty.arraySize; i++)
        {
            if (layersProperty.GetArrayElementAtIndex(i).stringValue == layerName)
            {
                return i;
            }
        }

        return -1;
    }

    public static Vector3 GetLocalSize(this Collider collider)
    {
        if (collider is BoxCollider box)
        {
            return box.size;
        }

        return Vector3.one;
    }
}
