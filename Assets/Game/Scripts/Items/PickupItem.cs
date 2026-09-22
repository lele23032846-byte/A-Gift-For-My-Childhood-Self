using UnityEngine;

/// <summary>
/// 可拾取的任务道具（通用拾取系统），适配现有 InteractableBase 射线交互：
/// - 准星对准、按 E 后，先向玩家 <see cref="Inventory"/> 尝试添加物品；
/// - 【关键顺序】只有添加成功，才隐藏场景中的物件；
///   添加失败（背包满 / 已拥有同 Id 物品）时物件原样保留，避免“背包没进去但东西没了”；
/// - 不负责章节完成判断，不监听章节事件；
/// - 物件被拾取后本交互即失效，防止同一物件重复拾取。
///
/// 挂载要求：所在物体（或其子物体）需有 Collider，并设置为 Interactable Layer，
/// 以便 PlayerInteractor 的射线能命中。
/// </summary>
public class PickupItem : InteractableBase
{
    [Header("Pickup（拾取）")]
    [Tooltip("该场景物件对应的稳定物品 Id。")]
    [SerializeField] private ItemId itemId = ItemId.None;

    [Tooltip("拾取成功后要隐藏的根物体（通常是整个道具）。留空则隐藏本组件所在物体。")]
    [SerializeField] private GameObject visualRoot;

    [Tooltip("准星对准时的提示，{0} 会替换为物品显示名。")]
    [SerializeField] private string promptFormat = "E - Pick up {0}";

    /// <summary>本物件是否已被拾取（每实例独立的状态标记）。</summary>
    private bool _pickedUp;

    /// <summary>缓存的玩家物品栏，首次交互时从 interactor 取得。</summary>
    private Inventory _inventory;

    /// <summary>该拾取物对应的物品 Id。</summary>
    public ItemId ItemId => itemId;

    /// <summary>是否已被拾取。</summary>
    public bool IsPickedUp => _pickedUp;

    /// <summary>已拾取 / 未配置 Id 的物件不再允许交互。</summary>
    public override bool CanInteract()
    {
        return !_pickedUp && itemId != ItemId.None;
    }

    /// <summary>对准时显示“拾取 + 物品名”。</summary>
    public override string GetInteractionPrompt()
    {
        if (itemId == ItemId.None)
        {
            return base.GetInteractionPrompt();
        }

        return string.Format(promptFormat, itemId.GetDisplayName());
    }

    public override void Interact(PlayerInteractor interactor)
    {
        // 防御性判断：同一物件重复拾取时直接忽略（正常路径下 CanInteract 已拦截）
        if (_pickedUp)
        {
            return;
        }

        if (itemId == ItemId.None)
        {
            Debug.LogError("[" + nameof(PickupItem) + "] " + gameObject.name + " 未配置 itemId，无法拾取。", this);
            return;
        }

        _inventory = GetInventory(interactor);
        if (_inventory == null)
        {
            Debug.LogError("[" + nameof(PickupItem) + "] 玩家身上找不到 Inventory，无法拾取 " + itemId + "。", this);
            return;
        }

        // 关键顺序：先尝试添加物品
        bool added = _inventory.TryAddItem(itemId);
        if (!added)
        {
            // 添加失败：不修改 _pickedUp、不隐藏物件，小盒继续留在场景中
            OnPickupFailed();
            return;
        }

        // 添加成功后，才隐藏场景物件并标记本实例失效
        _pickedUp = true;
        HideSceneObject();
    }

    /// <summary>从交互发起者（Player）身上获取物品栏；Inventory 与 PlayerInteractor 同在 Player 根物体。</summary>
    private Inventory GetInventory(PlayerInteractor interactor)
    {
        if (_inventory != null)
        {
            return _inventory;
        }

        return interactor.GetComponent<Inventory>();
    }

    /// <summary>拾取失败的反馈：区分“重复拥有”和“背包已满”，便于排查。</summary>
    protected virtual void OnPickupFailed()
    {
        string reason = _inventory.HasItem(itemId)
            ? "已经拥有该物品"
            : "背包已满";

        Debug.Log("[" + nameof(PickupItem) + "] 拾取失败（" + reason + "）：" + itemId + "，物件保留在场景中。", this);
    }

    /// <summary>隐藏场景中的道具物件。</summary>
    private void HideSceneObject()
    {
        GameObject target = visualRoot != null ? visualRoot : gameObject;
        target.SetActive(false);
    }
}
