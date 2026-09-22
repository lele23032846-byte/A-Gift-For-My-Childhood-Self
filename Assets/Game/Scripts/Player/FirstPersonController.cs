using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第一人称移动控制器：
/// - WASD 水平移动（斜向输入已归一化，斜走不快于直线）
/// - 鼠标控制水平/垂直视角，垂直视角限制在 pitchLimit 内
/// - 镜头平滑跟随（SmoothDamp），灵敏度可调，默认温和
/// - 移动速度平滑（可配置，0 为即时响应）
/// - CharacterController 碰撞 + 重力 + 地面检测
/// - 点击 Game 窗口锁定光标，Esc 释放，再次点击重新锁定
/// 仅使用项目现有输入资产 IA_Gameplay（Player/Move、Player/Look），
/// 不引入 Rigidbody、跳跃/冲刺/下蹲、第三方插件。
/// 手感参考：Unity StarterAssets 的 MouseLook 手法、UnityLibrary 的 SimpleSmoothMouseLook（SmoothDamp 平滑）。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("输入")]
    [Tooltip("项目现有输入资产：Assets/Input/IA_Gameplay.inputactions")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("移动")]
    [Tooltip("水平移动速度（m/s）")]
    [SerializeField] private float moveSpeed = 2.5f;
    [Tooltip("水平速度平滑时间（s）。0 = 即时响应；0.05 左右起步/急停更柔和")]
    [SerializeField] private float moveSmoothTime = 0.05f;
    [Tooltip("重力加速度（m/s²，负值）。CharacterController 不提供重力，需手动施加")]
    [SerializeField] private float gravity = -19.62f;

    [Header("观察")]
    [Tooltip("鼠标灵敏度。感觉太快先调到 1 以下（建议 0.5~2 微调）")]
    [SerializeField] private float lookSensitivity = 1f;
    [Tooltip("镜头平滑时间（s）。0 = 即时跟随；0.05~0.1 有轻微惯性，更跟手不飘")]
    [SerializeField] private float lookSmoothTime = 0.06f;
    [Tooltip("垂直视角限制（度），上下对称")]
    [SerializeField] private float pitchLimit = 85f;

    [Header("相机")]
    [Tooltip("摄像机根节点（Player 的子物体），其下挂 Main Camera")]
    [SerializeField] private Transform cameraRoot;
    [Tooltip("摄像机高度（眼高，m）。运行时将 CameraRoot 置于该高度")]
    [SerializeField] private float cameraHeight = 1.55f;

    private CharacterController _controller;
    private InputAction _moveAction;
    private InputAction _lookAction;

    // 目标角度：由鼠标输入直接累加（响应的“意图”）
    private float _targetYaw;
    private float _targetPitch;
    // 当前角度：平滑跟随目标（实际渲染的朝向）
    private float _currentYaw;
    private float _currentPitch;
    private float _yawVelocity;
    private float _pitchVelocity;

    private Vector3 _horizontalVelocity;
    private Vector3 _moveVelocity;

    private float _verticalVelocity;
    private bool _cursorLocked;

    private void Awake()
    {
        _controller = GetComponent<CharacterController>();

        if (inputActions == null)
        {
            Debug.LogError("[" + nameof(FirstPersonController) + "] 未配置 inputActions，请在 Inspector 中把 IA_Gameplay 拖入。", this);
            enabled = false;
            return;
        }

        _moveAction = inputActions.FindAction("Player/Move");
        _lookAction = inputActions.FindAction("Player/Look");
        if (_moveAction == null || _lookAction == null)
        {
            Debug.LogError("[" + nameof(FirstPersonController) + "] 在输入资产中找不到 Player/Move 或 Player/Look 动作。", this);
            enabled = false;
            return;
        }

        if (cameraRoot == null)
        {
            Debug.LogError("[" + nameof(FirstPersonController) + "] 未指定 cameraRoot，请把 Player 下的相机根节点拖入。", this);
            enabled = false;
            return;
        }

        // 保证眼高；后续想改高度只需调整 Inspector 中的 cameraHeight 并重新进入 Play 模式
        cameraRoot.localPosition = new Vector3(0f, cameraHeight, 0f);
        _targetYaw = _currentYaw = transform.eulerAngles.y;
    }

    private void OnEnable()
    {
        _moveAction?.Enable();
        _lookAction?.Enable();
        SetCursorLocked(true);
    }

    private void OnDisable()
    {
        _moveAction?.Disable();
        _lookAction?.Disable();
        SetCursorLocked(false);
    }

    private void Update()
    {
        HandleCursorLock();

        // Esc 释放鼠标期间暂停移动与观察，重新点击锁定后恢复
        if (!_cursorLocked)
        {
            return;
        }

        HandleLook();
        HandleMove();
    }

    private void HandleCursorLock()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && _cursorLocked)
        {
            SetCursorLocked(false);
        }
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !_cursorLocked)
        {
            SetCursorLocked(true);
        }
    }

    private void HandleLook()
    {
        Vector2 look = _lookAction.ReadValue<Vector2>();

        // 目标角度实时累加输入，不直接改旋转，平滑才有正确的“跟随目标”
        _targetYaw += look.x * lookSensitivity;
        _targetPitch = Mathf.Clamp(_targetPitch - look.y * lookSensitivity, -pitchLimit, pitchLimit);

        if (lookSmoothTime > 0f)
        {
            _currentYaw = Mathf.SmoothDamp(_currentYaw, _targetYaw, ref _yawVelocity, lookSmoothTime);
            _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, lookSmoothTime);
        }
        else
        {
            _currentYaw = _targetYaw;
            _currentPitch = _targetPitch;
        }

        // 直接赋值欧拉角，避免增量旋转的累计漂移
        transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        cameraRoot.localRotation = Quaternion.Euler(_currentPitch, 0f, 0f);
    }

    private void HandleMove()
    {
        // ClampMagnitude：限制斜向输入长度，斜走速度不超过直线移动
        Vector2 input = Vector2.ClampMagnitude(_moveAction.ReadValue<Vector2>(), 1f);
        Vector3 desiredVelocity = (transform.right * input.x + transform.forward * input.y) * moveSpeed;

        if (moveSmoothTime > 0f)
        {
            _horizontalVelocity = Vector3.SmoothDamp(_horizontalVelocity, desiredVelocity, ref _moveVelocity, moveSmoothTime);
        }
        else
        {
            _horizontalVelocity = desiredVelocity;
        }

        if (_controller.isGrounded && _verticalVelocity < 0f)
        {
            _verticalVelocity = -2f; // 轻微贴地，避免每帧产生持续下坠
        }
        _verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = _horizontalVelocity;
        motion.y = _verticalVelocity;
        _controller.Move(motion * Time.deltaTime);
    }

    private void SetCursorLocked(bool locked)
    {
        _cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
