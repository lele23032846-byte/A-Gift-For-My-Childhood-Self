using UnityEngine;

/// <summary>
/// 可交互对象抽象基类（通用射线交互系统）。
/// 子类至少实现 Interact；可按需重写 CanInteract（交互条件）与 GetInteractionPrompt（动态提示）。
/// 本基类不包含任何拾取 / 放置 / 章节逻辑，也不依赖事件总线或大型框架。
/// </summary>
public abstract class InteractableBase : MonoBehaviour
{
    [Tooltip("默认交互提示文字；子类重写 GetInteractionPrompt 后以此作为回退")]
    [SerializeField] private string interactionPrompt = "交互";

    /// <summary>当前是否允许交互？条件不满足时按 E 会走失败反馈而不是执行 Interact。</summary>
    public virtual bool CanInteract()
    {
        return true;
    }

    /// <summary>当前应对准准星显示的提示文本。</summary>
    public virtual string GetInteractionPrompt()
    {
        return interactionPrompt;
    }

    /// <summary>玩家按 E 后执行的具体操作，由子类实现。</summary>
    public abstract void Interact(PlayerInteractor interactor);

    /// <summary>
    /// 由 PlayerInteractor 统一调用的模板方法：
    /// 先判断 CanInteract，满足才执行 Interact；不满足则给失败反馈。
    /// </summary>
    /// <returns>是否成功执行了交互。</returns>
    public bool TryInteract(PlayerInteractor interactor)
    {
        if (!CanInteract())
        {
            OnInteractionFailed(interactor);
            return false;
        }

        Interact(interactor);
        return true;
    }

    /// <summary>交互条件不满足时的失败反馈。子类可重写（提示音、红字、摇头等）。</summary>
    protected virtual void OnInteractionFailed(PlayerInteractor interactor)
    {
        Debug.Log("[" + GetType().Name + "] 现在不能交互：" + gameObject.name, this);
    }
}
