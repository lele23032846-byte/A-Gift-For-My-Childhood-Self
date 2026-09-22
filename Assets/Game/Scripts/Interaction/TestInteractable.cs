using UnityEngine;

/// <summary>
/// 仅用于验证通用射线交互系统的测试可交互对象：
/// 被交互时计数并输出日志；可开关 allowInteract 以验证条件判断与失败反馈。
/// 不实现任何拾取 / 放置 / 章节逻辑。
/// </summary>
public class TestInteractable : InteractableBase
{
    [Tooltip("当前是否允许交互。取消勾选可验证条件判断与失败反馈")]
    [SerializeField] private bool allowInteract = true;

    [Tooltip("被交互时打印的日志内容")]
    [SerializeField] private string interactMessage = "测试交互被触发";

    /// <summary>成功交互次数。</summary>
    public int InteractCount { get; private set; }

    /// <summary>因条件不满足而失败的次数。</summary>
    public int FailedCount { get; private set; }

    /// <summary>运行时切换交互条件（自动化测试使用）。</summary>
    public void SetAllowInteract(bool value)
    {
        allowInteract = value;
    }

    public override bool CanInteract()
    {
        return allowInteract;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        InteractCount++;
        Debug.Log("[" + name + "] " + interactMessage + "（第 " + InteractCount + " 次）", this);
    }

    protected override void OnInteractionFailed(PlayerInteractor interactor)
    {
        FailedCount++;
        base.OnInteractionFailed(interactor);
    }
}
