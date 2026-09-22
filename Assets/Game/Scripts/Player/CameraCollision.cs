using UnityEngine;

/// <summary>
/// 第一人称摄像机防穿模（Camera Collision）：
/// - 每帧从玩家脚底上方沿竖直方向 SphereCast 到相机目标位置，
///   检测头顶/上方障碍（横梁、门框上沿、书柜上层、矮天花板、家具顶面等）。
/// - 命中时把相机沿竖直方向拉低到障碍面下方，避免相机进入物体内部；
///   未命中时相机平滑回弹到正常眼高。
/// - 挂在 Main Camera 上使用；与 FirstPersonController 配合，
///   只修改相机位置，不改输入、移动与旋转逻辑。
/// 注：Near Clip Plane 过大会在贴墙时裁掉墙面导致视觉穿模，
/// 需在 Inspector 中把 Main Camera 的 Near Clip Plane 调到 0.05 左右（见交付说明）。
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraCollision : MonoBehaviour
{
    [Header("参考")]
    [Tooltip("玩家根节点（带 CharacterController 的 Player）")]
    [SerializeField] private Transform playerRoot;
    [Tooltip("相机根节点（FirstPersonController.cameraRoot），其世界位置即无碰撞时的期望眼位")]
    [SerializeField] private Transform cameraRoot;

    [Header("防穿参数")]
    [Tooltip("检测球半径（m，世界坐标）。略小于 CharacterController 半径即可")]
    [SerializeField] private float probeRadius = 0.12f;
    [Tooltip("相机被压缩后与障碍物表面保留的最小间隔（m）")]
    [SerializeField] private float padding = 0.05f;
    [Tooltip("相机最低允许眼高（m，世界坐标），防止被压到胸口/地面视角")]
    [SerializeField] private float minHeight = 0.6f;
    [Tooltip("压缩/回弹平滑时间（s）。0 = 瞬时跟随")]
    [SerializeField] private float smoothTime = 0.08f;
    [Tooltip("只检测这些层的碰撞体（默认 Everything；建议配为墙体/家具所在层）")]
    [SerializeField] private LayerMask collisionMask = ~0;

    private Transform _cameraTransform;
    private Vector3 _currentVelocity;

    private void Awake()
    {
        _cameraTransform = transform;

        if (playerRoot == null)
        {
            playerRoot = transform.root;
        }

        if (cameraRoot == null)
        {
            Debug.LogError("[" + nameof(CameraCollision) + "] 未指定 cameraRoot，请把 FirstPersonController.cameraRoot 拖入。", this);
            enabled = false;
            return;
        }
    }

    private void LateUpdate()
    {
        // 期望眼位：相机根的世界位置（由 FirstPersonController 设定眼高）
        Vector3 target = cameraRoot.position;

        // 检测起点：玩家脚底上方足够高度（CharacterController 胶囊底部约在 y=0.28）。
        // 起点过低会让 SphereCast 的球与地板碰撞体重叠，开场即误命中地板把相机压到 minHeight。
        Vector3 origin = playerRoot.position + Vector3.up * 0.3f;
        float distance = target.y - origin.y;
        if (distance <= 0.2f)
        {
            return;
        }

        Vector3 desired = target;
        if (Physics.SphereCast(origin, probeRadius, Vector3.up, out RaycastHit hit, distance,
                collisionMask, QueryTriggerInteraction.Ignore))
        {
            // 命中头顶障碍：球心应贴在被撞面的“下方”（命中面法线朝下时 y 减小）
            float faceY = hit.point.y + hit.normal.y * probeRadius;
            float clampedY = Mathf.Max(faceY - padding, origin.y + minHeight);
            if (clampedY < target.y - 0.001f)
            {
                desired = new Vector3(target.x, clampedY, target.z);
            }
        }

        _cameraTransform.position = smoothTime > 0f
            ? Vector3.SmoothDamp(_cameraTransform.position, desired, ref _currentVelocity, smoothTime)
            : desired;
    }
}
