using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 玩家交互器（通用射线交互系统）：
/// - 每帧从玩家摄像机中心（屏幕准星位置）发射检测（默认 0.03m 小半径，可在 Inspector 设为 0 恢复细射线）；
/// - 交互距离、检测 Layer 均可在 Inspector 配置；
/// - 通过 GetComponentInParent 识别碰撞体所属的可交互对象（交互组件挂在父物体也能识别）；
/// - 统一使用输入资产中的 Player/Interact（默认绑定 E），边沿触发，按住 E 不会连续交互；
/// - 当前目标变化 / 丢失（移开视角、超出距离、非交互 Layer）时通知 InteractionPromptUI 显示或隐藏。
/// 不处理移动、抓取、拾取，不使用事件总线，不修改移动脚本。
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [Header("输入")]
    [Tooltip("项目现有输入资产：Assets/Input/IA_Gameplay.inputactions，需包含 Player/Interact（默认绑定 E）")]
    [SerializeField] private InputActionAsset inputActions;

    [Header("射线检测")]
    [Tooltip("交互距离（米），默认 2")]
    [Min(0.1f)]
    [SerializeField] private float interactRange = 2f;

    [Tooltip("只检测这些 Layer。建议只勾选 Interactable 层，避免无关物体干扰")]
    [SerializeField] private LayerMask interactableMask;

    [Tooltip("是否检测设为 Trigger 的碰撞体。默认忽略，避免误触隐形触发区域")]
    [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

    [Tooltip("交互检测半径（米）。0 = 从准星发射无限细的射线；设很小的值（如 0.03）可在准星略微偏离小物件时仍能选中，避免小盒紧挨门时目标被门抢走")]
    [Min(0f)]
    [SerializeField] private float castRadius = 0.03f;

    [Tooltip("是否要求光标处于锁定状态才处理交互（Esc 暂停时不交互）。特殊场景可关闭")]
    [SerializeField] private bool requireLockedCursor = true;

    [Header("引用")]
    [Tooltip("玩家摄像机（射线起点）。留空时自动从 Player 子物体中查找")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("交互提示 UI。留空时自动在场景中查找")]
    [SerializeField] private InteractionPromptUI promptUI;

    private InputAction _interactAction;

    private const int MaxHits = 16;
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[MaxHits];

    /// <summary>当前交互目标（每帧由射线结果更新）。</summary>
    public InteractableBase CurrentTarget { get; private set; }

    /// <summary>当前交互距离（米）。</summary>
    public float InteractRange => interactRange;

    private void Awake()
    {
        if (inputActions == null)
        {
            Debug.LogError("[" + nameof(PlayerInteractor) + "] 未配置 inputActions，请把 IA_Gameplay 拖入。", this);
            enabled = false;
            return;
        }

        _interactAction = inputActions.FindAction("Player/Interact");
        if (_interactAction == null)
        {
            Debug.LogError("[" + nameof(PlayerInteractor) + "] 在输入资产中找不到 Player/Interact 动作（应绑定 E 键）。", this);
            enabled = false;
            return;
        }

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        if (playerCamera == null)
        {
            Debug.LogError("[" + nameof(PlayerInteractor) + "] 找不到玩家摄像机，请把 Camera 拖入或确认其位于 Player 子物体下。", this);
            enabled = false;
            return;
        }

        if (promptUI == null)
        {
            promptUI = FindAnyObjectByType<InteractionPromptUI>();
            if (promptUI == null)
            {
                // 没有 UI 不阻断交互本身，只给出一次性警告
                Debug.LogWarning("[" + nameof(PlayerInteractor) + "] 场景中没有 InteractionPromptUI，交互仍可执行但不会显示提示。", this);
            }
        }
    }

    private void OnEnable()
    {
        _interactAction?.Enable();
    }

    private void OnDisable()
    {
        _interactAction?.Disable();
        ClearTarget();
    }

    private void Update()
    {
        // 与移动系统保持一致：Esc 释放光标（暂停）期间不做检测与交互，并隐藏提示
        if (!IsInputAllowed())
        {
            ClearTarget();
            return;
        }

        UpdateTarget();
        HandleInteractInput();
    }

    /// <summary>
    /// 从摄像机中心发射检测并更新当前目标。public 便于自动化测试直接驱动。
    /// </summary>
    public void UpdateTarget()
    {
        Transform camTransform = playerCamera.transform;
        Vector3 origin = camTransform.position;
        Vector3 direction = camTransform.forward;

        // 主查询按配置处理 Trigger（默认忽略，避免误触隐形触发区）
        InteractableBase found = Query(origin, direction, triggerInteraction);

        if (found == null && triggerInteraction != QueryTriggerInteraction.Collide)
        {
            // 回退查询：允许命中 Trigger——用于“开门后门板碰撞体被设为 Trigger、仍需按 E 关门”等场景。
            // 只接受真正挂了 InteractableBase 的命中，没有交互组件的隐形触发区不会被选中。
            found = Query(origin, direction, QueryTriggerInteraction.Collide);
        }

        SetTarget(found);
    }

    /// <summary>
    /// 沿中心方向做一次检测（castRadius=0 为细射线，否则为小半径球体），
    /// 返回命中的最近 InteractableBase；没有则返回 null。
    /// </summary>
    private InteractableBase Query(Vector3 origin, Vector3 direction, QueryTriggerInteraction triggers)
    {
        if (castRadius <= 0f)
        {
            // 半径为 0：单条细射线，取最近的命中
            if (Physics.Raycast(
                    origin,
                    direction,
                    out RaycastHit hit,
                    interactRange,
                    interactableMask,
                    triggers))
            {
                // 交互组件可能挂在碰撞体的父物体上；禁用的组件不会被返回
                return hit.collider.GetComponentInParent<InteractableBase>();
            }
            return null;
        }

        // 小半径查询：给准星少量容差，小物件紧挨其他碰撞体（如门）时也能稳定选中
        int count = Physics.SphereCastNonAlloc(
            origin,
            castRadius,
            direction,
            _hitBuffer,
            interactRange,
            interactableMask,
            triggers);

        if (count <= 0)
        {
            return null;
        }

        // SphereCast 结果不保证按距离排序，按命中距离升序后取最近的可交互对象
        System.Array.Sort(_hitBuffer, 0, count, HitDistanceComparer.Instance);
        for (int i = 0; i < count; i++)
        {
            InteractableBase candidate = _hitBuffer[i].collider.GetComponentInParent<InteractableBase>();
            if (candidate != null)
            {
                return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// 处理交互输入：仅在按下当帧（边沿）触发一次，按住不连发。public 便于自动化测试。
    /// </summary>
    public void HandleInteractInput()
    {
        if (CurrentTarget == null || _interactAction == null)
        {
            return;
        }

        // WasPressedThisFrame 为按下边沿，按住期间持续为 false，天然防止连续触发
        if (_interactAction.WasPressedThisFrame())
        {
            CurrentTarget.TryInteract(this);
        }
    }

    protected virtual bool IsInputAllowed()
    {
        if (!requireLockedCursor)
        {
            return true;
        }

        return Cursor.lockState == CursorLockMode.Locked;
    }

    private void SetTarget(InteractableBase target)
    {
        bool targetChanged = target != CurrentTarget;
        CurrentTarget = target;

        if (target != null)
        {
            if (promptUI != null)
            {
                // 目标未切换但提示文案变化时也要刷新（如门在同一对象上于 Open/Close 提示间切换）
                string prompt = target.GetInteractionPrompt();
                if (targetChanged || !promptUI.IsVisible || promptUI.CurrentText != prompt)
                {
                    promptUI.Show(prompt);
                }
            }
        }
        else
        {
            if (targetChanged && promptUI != null)
            {
                promptUI.Hide();
            }
        }
    }

    private void ClearTarget()
    {
        SetTarget(null);
    }

    /// <summary>按 RaycastHit.distance 升序比较，供小半径查询结果排序。</summary>
    private sealed class HitDistanceComparer : System.Collections.Generic.IComparer<RaycastHit>
    {
        public static readonly HitDistanceComparer Instance = new HitDistanceComparer();

        public int Compare(RaycastHit a, RaycastHit b)
        {
            return a.distance.CompareTo(b.distance);
        }
    }
}
