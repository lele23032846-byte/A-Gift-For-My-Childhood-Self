using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 一键修复：门碰撞体形状 + 交互提示 Canvas 布局。
/// 菜单：Tools/修复/修复门碰撞体与交互提示UI
/// 在 Unity 内存中直接修改场景对象，保存后即生效。
/// </summary>
public static class SceneFixer
{
    [MenuItem("Tools/修复/修复门碰撞体与交互提示UI")]
    public static void Fix()
    {
        int fixedCount = 0;

        // 1) 修复 Doorcollision 的缩放：全高薄门板
        var doorcollision = GameObject.Find("Doorcollision");
        if (doorcollision != null)
        {
            var t = doorcollision.transform;
            Undo.RecordObject(t, "修复 Doorcollision 缩放");
            t.localScale = new Vector3(0.35181668f, 0.05f, 1f);
            Debug.Log("[SceneFixer] Doorcollision localScale -> (0.352, 0.05, 1)");
            fixedCount++;
        }
        else
        {
            Debug.LogWarning("[SceneFixer] 找不到 Doorcollision 对象。");
        }

        // 2) 修复交互提示 Canvas 的 RectTransform：全屏 stretch + scale 1
        var uiCanvas = GameObject.Find("UI_InteractionPrompt");
        if (uiCanvas != null)
        {
            var rt = uiCanvas.GetComponent<RectTransform>();
            if (rt != null)
            {
                Undo.RecordObject(rt, "修复 Canvas RectTransform");
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = Vector2.zero;
                rt.localScale = Vector3.one;
                Debug.Log("[SceneFixer] UI_InteractionPrompt RectTransform -> 全屏 stretch");
                fixedCount++;
            }
        }
        else
        {
            Debug.LogWarning("[SceneFixer] 找不到 UI_InteractionPrompt 对象。");
        }

        // 3) 修复 PromptText 位置：屏幕下方居中，不贴底
        var promptText = GameObject.Find("PromptText");
        if (promptText != null)
        {
            var rt = promptText.GetComponent<RectTransform>();
            if (rt != null)
            {
                Undo.RecordObject(rt, "修复 PromptText 位置");
                rt.anchoredPosition = new Vector2(0f, 200f);
                Debug.Log("[SceneFixer] PromptText anchoredPosition -> (0, 200)");
                fixedCount++;
            }
        }
        else
        {
            Debug.LogWarning("[SceneFixer] 找不到 PromptText 对象。");
        }

        // 4) CameraCollision 的 SphereCast 起点已在脚本里改好（0.1 -> 0.3），
        //    Unity 重新编译脚本后自动生效，这里无需操作场景。

        if (fixedCount > 0)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            EditorUtility.DisplayDialog("SceneFixer",
                $"修复完成 {fixedCount} 项。\n请按 Ctrl+S 保存场景，然后进 Play 模式验证。",
                "OK");
        }
        else
        {
            EditorUtility.DisplayDialog("SceneFixer",
                "没有找到需要修复的对象，请确认场景已打开。", "OK");
        }
    }
}
