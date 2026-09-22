using UnityEngine;

/// <summary>
/// 卧室门交互：挂在“门铰链（Door_Hinge）”节点上。
/// 按 E 在开/关之间切换，门板（含碰撞体）绕铰链竖直轴平滑旋转。
/// 开门时门板碰撞体自动设为 Trigger（不挡路），关门时恢复为实碰撞。
/// 不涉及拾取、物理抓取，仅做旋转动画。
/// </summary>
public class DoorInteractable : InteractableBase
{
    [Header("Door（门）")]
    [Tooltip("开门时的旋转角度（度）。")]
    [SerializeField] private float openAngle = 90f;

    [Tooltip("开/关门动画时长（秒）；设为 0 则瞬间到位。")]
    [SerializeField] private float openDuration = 0.6f;

    [Tooltip("开门方向：1 或 -1（若开门方向撞墙，改成 -1）。")]
    [Range(-1, 1)]
    [SerializeField] private int openDirection = 1;

    [Tooltip("门关闭时显示的提示。")]
    [SerializeField] private string closedPrompt = "E - Open Door";

    [Tooltip("门打开时显示的提示。")]
    [SerializeField] private string openPrompt = "E - Close Door";

    /// <summary>门当前是否处于打开状态。</summary>
    public bool IsOpen { get; private set; }

    /// <summary>门是否正处于开/关动画中。</summary>
    public bool IsMoving => _tweening;

    /// <summary>铰链当前的旋转角度（度，绕世界竖直轴）。</summary>
    public float CurrentAngle { get; private set; }

    private Transform _hinge;
    private float _targetAngle;
    private float _fromAngle;
    private float _elapsed;
    private bool _tweening;

    private Quaternion _closedLocalRotation;
    private Vector3 _swingAxisLocal;

    // 门板碰撞体：开门后置为 Trigger 不再挡路，关门时恢复阻挡。
    private Collider _doorCollider;

    private void Awake()
    {
        _hinge = transform;
        CurrentAngle = 0f;

        // 铰链节点的本地轴未必与世界轴对齐（FBX 轴转换会烘焙进铰链旋转），
        // 求出“世界竖直轴”在铰链本地空间中的方向，保证门始终竖直开合。
        _closedLocalRotation = _hinge.localRotation;
        _swingAxisLocal = Quaternion.Inverse(_hinge.rotation) * Vector3.up;

        // 门板下的碰撞体（Doorcollision）
        _doorCollider = GetComponentInChildren<Collider>();
    }

    public override bool CanInteract()
    {
        // 动画过程中也允许按 E（反向切换）
        return true;
    }

    public override string GetInteractionPrompt()
    {
        return IsOpen ? openPrompt : closedPrompt;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        IsOpen = !IsOpen;

        if (_doorCollider != null)
        {
            // 开门时设为 Trigger 不挡路；关门时恢复实碰撞。
            _doorCollider.isTrigger = IsOpen;
        }

        _targetAngle = IsOpen ? openAngle * Mathf.Sign(openDirection) : 0f;
        _fromAngle = CurrentAngle;
        _elapsed = 0f;
        _tweening = true;
    }

    private void Update()
    {
        if (!_tweening)
        {
            return;
        }

        _elapsed += Time.deltaTime;

        float t = openDuration <= 0f
            ? 1f
            : Mathf.Clamp01(_elapsed / openDuration);
        t = t * t * (3f - 2f); // SmoothStep，起止更柔和

        CurrentAngle = Mathf.LerpAngle(_fromAngle, _targetAngle, t);
        _hinge.localRotation = _closedLocalRotation
                              * Quaternion.AngleAxis(CurrentAngle, _swingAxisLocal);

        if (t >= 1f)
        {
            _tweening = false;
            CurrentAngle = _targetAngle;
        }
    }
}
