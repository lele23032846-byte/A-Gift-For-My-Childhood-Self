using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// 一键修复第一人称相机贴墙穿模：
/// 1. Main Camera 的 Near Clip Plane 0.3 -> 0.05（墙不会被近裁剪面裁掉）
/// 2. 为 Main Camera 挂载 CameraCollision 并自动配置 Player Root / Camera Root 引用
/// 3. 保存当前场景
/// 可通过菜单 Tools/修复/配置相机防穿 手动运行，
/// 或由批处理（-executeMethod CameraSetupFixer.FixCamera）自动运行。
/// </summary>
public static class CameraSetupFixer
{
    [MenuItem("Tools/修复/配置相机防穿（Near Clip + CameraCollision）")]
    public static void FixCamera()
    {
        // 批处理模式（-batchmode）下默认不加载场景，先定位并打开项目场景
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path) || !scene.isLoaded)
        {
            string[] guids = AssetDatabase.FindAssets("SC_Bedroom t:Scene");
            if (guids.Length == 0)
            {
                Debug.LogError("[CameraSetupFixer] 找不到 SC_Bedroom 场景，请手动打开后运行。");
                return;
            }
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        }

        Camera cam = Object.FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            Debug.LogError("[CameraSetupFixer] 场景中未找到 Camera，请手动处理。");
            return;
        }

        // 1. 近裁剪面：贴墙最近距离约 0.2m，0.3m 会把墙面裁掉
        cam.nearClipPlane = 0.05f;

        // 2. 挂载 CameraCollision 并配置引用
        CameraCollision collision = cam.GetComponent<CameraCollision>();
        if (collision == null)
        {
            collision = cam.gameObject.AddComponent<CameraCollision>();
        }

        GameObject player = GameObject.Find("Player");
        Transform cameraRoot = player != null ? player.transform.Find("CameraRoot") : null;
        if (player != null && cameraRoot != null)
        {
            SerializedObject so = new SerializedObject(collision);
            so.FindProperty("playerRoot").objectReferenceValue = player.transform;
            so.FindProperty("cameraRoot").objectReferenceValue = cameraRoot;
            so.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning("[CameraSetupFixer] 未找到 Player/CameraRoot，CameraCollision 已挂载但引用为空，请手动拖入。");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[CameraSetupFixer] 完成：Near Clip Plane=" + cam.nearClipPlane
            + "，CameraCollision 已挂载到 " + cam.gameObject.name
            + (player != null && cameraRoot != null ? "，引用已配置。" : "，引用未配置。"));
    }
}
