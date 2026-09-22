using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家物品栏（通用拾取系统）：
/// - 内部仅保存 <see cref="ItemId"/> 列表，不引用任何场景物件 GameObject；
/// - 默认容量 3，每种任务道具唯一（重复添加返回 false）；
/// - 只提供查询 / 添加 / 移除三个基本操作，不做拖拽、排序、丢弃、组合、存档；
/// - 添加/移除成功后通过事件通知 UI，UI 无需每帧轮询刷新。
/// 挂载位置：Player 根物体（与 PlayerInteractor 同级）。
/// </summary>
public class Inventory : MonoBehaviour
{
    [Tooltip("背包最大容量（格数）。")]
    [Min(1)]
    [SerializeField] private int capacity = 3;

    private readonly List<ItemId> _items = new List<ItemId>();

    /// <summary>物品被成功添加后触发，参数为刚加入的物品 Id。</summary>
    public event Action<ItemId> ItemAdded;

    /// <summary>物品被成功移除后触发，参数为刚移除的物品 Id。</summary>
    public event Action<ItemId> ItemRemoved;

    /// <summary>物品栏发生任何变化（添加或移除）后的统一通知，UI 监听它即可刷新。</summary>
    public event Action Changed;

    /// <summary>背包最大容量。</summary>
    public int Capacity => capacity;

    /// <summary>当前拥有的物品数量。</summary>
    public int Count => _items.Count;

    /// <summary>背包是否已满。</summary>
    public bool IsFull => _items.Count >= capacity;

    /// <summary>当前物品的只读视图（按槽位顺序），供 UI 读取，外部无法修改。</summary>
    public IReadOnlyList<ItemId> Items => _items;

    /// <summary>是否拥有指定物品。</summary>
    public bool HasItem(ItemId itemId)
    {
        return _items.Contains(itemId);
    }

    /// <summary>
    /// 尝试添加物品。
    /// 失败情形（均返回 false 且不改变物品栏）：
    /// - itemId 为 None；
    /// - 已拥有同 Id 物品（任务道具唯一）；
    /// - 背包已满。
    /// 仅在添加成功后触发事件。
    /// </summary>
    public bool TryAddItem(ItemId itemId)
    {
        if (itemId == ItemId.None)
        {
            Debug.LogWarning("[" + nameof(Inventory) + "] 拒绝添加：ItemId 为 None。", this);
            return false;
        }

        if (_items.Contains(itemId))
        {
            Debug.Log("[" + nameof(Inventory) + "] 拒绝添加：已拥有 " + itemId + "（任务道具唯一）。", this);
            return false;
        }

        if (_items.Count >= capacity)
        {
            Debug.Log("[" + nameof(Inventory) + "] 拒绝添加：背包已满（" + _items.Count + "/" + capacity + "）。", this);
            return false;
        }

        _items.Add(itemId);

        // 列表更新完成后再通知，保证监听器读到的是最新状态
        ItemAdded?.Invoke(itemId);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 尝试移除物品。物品不存在时返回 false 且不触发事件。
    /// （本期拾取流程暂不移除，预留给后续任务系统消耗道具。）
    /// </summary>
    public bool TryRemoveItem(ItemId itemId)
    {
        if (!_items.Remove(itemId))
        {
            return false;
        }

        ItemRemoved?.Invoke(itemId);
        Changed?.Invoke();
        return true;
    }
}
