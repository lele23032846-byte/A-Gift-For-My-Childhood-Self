using System;
using UnityEngine;

/// <summary>
/// 任务道具放置点（通用放置系统，任务4）：
/// - 继承 <see cref="InteractableBase"/>，复用 PlayerInteractor 的射线交互与 E 键，不新写输入逻辑；
/// - Inspector 配置：
///   · requiredItemId —— 放置此位置所需的稳定物品 Id；
///   · displayObject —— 放置成功后显示的展示物件（预先摆放在场景中，初始隐藏）；
///   · playerInventory —— 玩家物品栏（Player 上的 Inventory），优先 Inspector 绑定；
///   · interactionCollider —— 放置成功后禁用的交互碰撞体（InteractionCollider 子物体），停止该点位继续被射线命中；
/// - 玩家没有指定物品时不允许放置：CanInteract=false，按 E 走失败反馈，零副作用；
/// - 【数据安全】只有全部条件满足才消耗物品；消耗成功后才改变放置状态、显示展示物件：
///   已放置 / 未配置 Id / 没有物品 / 移除失败 → 一律不改物品栏、不改放置状态、不显示展示物件；
/// - 放置成功后触发 <see cref="ItemPlaced"/> 事件（参数为物品 Id），供 ChapterManager 订阅推进章节；
///   本组件不调用金鱼特效、不播放章节结局、不做运行时 FindObjectsOfType 搜索管理器，引用全部 Inspector 绑定；
/// - 放置只能成功一次：IsPlaced 状态 + 成功后禁用交互碰撞体双保险。
/// </summary>
public class ItemPlacement : InteractableBase
{
    [Header("Placement（放置）")]
    [Tooltip("放置此位置所需的稳定物品 Id。")]
    [SerializeField] private ItemId requiredItemId = ItemId.None;

    [Tooltip("放置成功后显示的展示物件（预先摆放在场景中，初始隐藏，例如 PlacedSmallBox）。")]
    [SerializeField] private GameObject displayObject;

    [Tooltip("玩家物品栏（Player 上的 Inventory）。优先在 Inspector 绑定；留空时首次交互从 interactor 所在物体获取并缓存。")]
    [SerializeField] private Inventory playerInventory;

    [Tooltip("玩家拥有所需物品时的提示，{0} 替换为物品显示名。")]
    [SerializeField] private string promptFormat = "E - Place {0}";

    [Tooltip("玩家还没有所需物品时的提示，{0} 替换为物品显示名。")]
    [SerializeField] private string lockedPromptFormat = "Need {0}";

    [Tooltip("放置成功后禁用交互碰撞体（通常是 InteractionCollider 子物体上的 Collider），使该点位不再被射线命中、提示自然消失。留空则仅靠 IsPlaced 拦截。")]
    [SerializeField] private Collider interactionCollider;

    /// <summary>放置成功后触发，参数为已放置的物品 Id（供 ChapterManager 订阅推进章节）。</summary>
    public event Action<ItemId> ItemPlaced;

    /// <summary>该点位是否已完成放置（每实例独立状态，只允许成功一次）。</summary>
    public bool IsPlaced { get; private set; }

    /// <summary>所需物品 Id（Inspector 配置值，供外部读取/校验）。</summary>
    public ItemId RequiredItemId => requiredItemId;

    /// <summary>当前展示物件引用（供自动化验收读取）。</summary>
    public GameObject DisplayObject => displayObject;

    /// <summary>当前交互碰撞体引用（供自动化验收读取）。</summary>
    public Collider InteractionCollider => interactionCollider;

    private Inventory _inventory;

    private void Awake()
    {
        _inventory = playerInventory;

        // 硬性要求「展示物件初始隐藏」：场景配置已隐藏，这里做防御性兜底，
        // 保证即使设计器忘记隐藏也不会在放置前露馅。
        if (displayObject != null)
        {
            displayObject.SetActive(false);
        }
    }

    /// <summary>没有指定物品 / 已放置 / 未配置 Id 时不允许交互。</summary>
    public override bool CanInteract()
    {
        if (IsPlaced || requiredItemId == ItemId.None)
        {
            return false;
        }

        Inventory inventory = GetInventory();
        return inventory != null && inventory.HasItem(requiredItemId);
    }

    /// <summary>拥有所需物品时提示“E 放置 X”；没有物品时提示“需要 X”。</summary>
    public override string GetInteractionPrompt()
    {
        if (requiredItemId == ItemId.None)
        {
            return base.GetInteractionPrompt();
        }

        string displayName = requiredItemId.GetDisplayName();
        Inventory inventory = GetInventory();
        bool hasItem = inventory != null && inventory.HasItem(requiredItemId);

        return hasItem
            ? string.Format(promptFormat, displayName)
            : string.Format(lockedPromptFormat, displayName);
    }

    public override void Interact(PlayerInteractor interactor)
    {
        // 防御性判断：已放置的点位直接忽略（正常路径下 CanInteract 已拦截，此处双保险防重复消耗）
        if (IsPlaced)
        {
            return;
        }

        if (requiredItemId == ItemId.None)
        {
            Debug.LogError("[" + nameof(ItemPlacement) + "] " + gameObject.name
                + " 未配置 requiredItemId，无法放置。", this);
            return;
        }

        _inventory = GetInventory(interactor);
        if (_inventory == null)
        {
            Debug.LogError("[" + nameof(ItemPlacement) + "] 找不到玩家物品栏（Inventory），无法放置 "
                + requiredItemId.GetDisplayName()
                + "。请把 Player 上的 Inventory 拖入 playerInventory 字段。", this);
            return;
        }

        // 条件 1：必须拥有指定物品，否则视为失败操作（不消耗、不改状态、不显示展示物件）
        if (!_inventory.HasItem(requiredItemId))
        {
            OnPlacementFailed("背包中没有指定物品 " + requiredItemId.GetDisplayName());
            return;
        }

        // 条件 2：只有全部条件满足才消耗物品。移除失败同样视为失败操作，状态原样保留。
        if (!_inventory.TryRemoveItem(requiredItemId))
        {
            OnPlacementFailed("从背包移除 " + requiredItemId.GetDisplayName() + " 失败");
            return;
        }

        // ---- 消耗成功后才允许改变放置状态与展示物件 ----
        IsPlaced = true;

        if (displayObject != null)
        {
            displayObject.SetActive(true);
        }

        if (interactionCollider != null)
        {
            interactionCollider.enabled = false;
        }

        // 明确的放置成功通知，供 ChapterManager 订阅（本组件不关心谁订阅、不调用金鱼特效/章节结局）
        ItemPlaced?.Invoke(requiredItemId);

        Debug.Log("[" + nameof(ItemPlacement) + "] " + gameObject.name + " 放置成功："
            + requiredItemId.GetDisplayName() + " 已从背包移除并放置到位。", this);
    }

    /// <summary>获取玩家物品栏：Inspector 绑定优先；未绑定时首次交互从 interactor 所在物体获取并缓存。</summary>
    private Inventory GetInventory(PlayerInteractor interactor)
    {
        if (_inventory != null)
        {
            return _inventory;
        }

        if (interactor != null)
        {
            _inventory = interactor.GetComponent<Inventory>();
        }

        return _inventory;
    }

    private Inventory GetInventory()
    {
        return GetInventory(null);
    }

    /// <summary>放置失败反馈（不修改任何状态）。子类可重写做提示音等表现。</summary>
    protected virtual void OnPlacementFailed(string reason)
    {
        Debug.Log("[" + nameof(ItemPlacement) + "] 放置失败（" + reason
            + "），物品栏与放置状态均未改变。", this);
    }
}
