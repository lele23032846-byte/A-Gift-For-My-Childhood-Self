using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 最小章节状态管理器（任务5）：只管理三章的状态与顺序解锁，不做任何表现层逻辑。
///
/// 职责边界（严格遵守）：
/// - 只保存/查询章节状态（<see cref="ChapterState"/>），并在满足条件时推进状态；
/// - 不引用、不控制任何 Tripo 模型；不实现灯光变化；不实现复杂叙事 UI；
/// - 不监听玩家背包：拥有物品绝不等于完成章节。
///
/// 第一章的真实完成条件（接线）：
/// - Inspector 把【正确的放置目标】SmallBoxPlacement 上的 <see cref="ItemPlacement"/> 拖入
///   <see cref="lostPetPlacement"/>，并约定所需物品 <see cref="lostPetRequiredItem"/>=SmallBox；
/// - <see cref="ItemPlacement"/> 只在真实放置成功（拥有物品 → 消耗成功 → IsPlaced=true）后
///   才触发 ItemPlaced 事件；没有物品时按 E 走失败反馈，事件永远不会发出；
/// - 因为订阅的是【指定放置点实例】而不是背包，所以完成条件明确关联到正确的放置目标，
///   不可能通过按任意 E、仅凭拥有物品来完成章节。
///
/// 幂等与顺序保护：
/// - 只有 Active 的章节能被完成；Locked（未解锁）与 Completed（重复请求）一律拒绝、不发事件；
/// - 完成一章后仅把紧随其后的下一章 Locked→Active，重复放置/重复请求不会跳过任何章节。
///
/// 第二、第三章的扩展入口：
/// - 两章的状态数据已存在（初始 Locked）；
/// - 未来任务实现其完成条件时，调用 <see cref="BindPlacement"/>（或在新增放置点后按同样方式
///   订阅）即可，章节推进逻辑无需改动。本轮不接线、不创建其放置目标。
///
/// 挂载位置：场景根物体 ChapterManager（由 ChapterSystemSetup 创建并绑定引用）。
/// </summary>
public class ChapterManager : MonoBehaviour
{
    [Header("第一章 LostPet —— 真实完成条件接线")]
    [Tooltip("第一章的正确放置目标（SmallBoxPlacement 根节点上的 ItemPlacement）。只有该放置点真实放置成功，第一章才会完成。")]
    [SerializeField] private ItemPlacement lostPetPlacement;

    [Tooltip("第一章放置点要求的物品 Id（应与该 ItemPlacement 的 RequiredItemId 一致：SmallBox）。")]
    [SerializeField] private ItemId lostPetRequiredItem = ItemId.SmallBox;

    /// <summary>章节总数。顺序由 <see cref="ChapterId"/> 枚举值定义。</summary>
    public const int ChapterCount = 3;

    /// <summary>
    /// 某一章真实完成时触发（参数为刚完成的章节 Id），每章只触发一次。
    /// 供后续灯光、UI、音效系统订阅；ChapterManager 自身不做这些表现。
    /// </summary>
    public event Action<ChapterId> ChapterCompleted;

    /// <summary>
    /// 任意章节状态发生变化后触发（参数为章节 Id 与新状态）。
    /// 一次“章节完成”通常产生两次通知：本章 Active→Completed、下一章 Locked→Active。
    /// 供未来章节 UI / 存档系统订阅。
    /// </summary>
    public event Action<ChapterId, ChapterState> ChapterStateChanged;

    /// <summary>当前处于 Active 的章节；全部完成后为 null。</summary>
    public ChapterId? CurrentChapter { get; private set; }

    /// <summary>是否三章全部完成（本轮正常流程下始终为 false）。</summary>
    public bool IsAllCompleted { get; private set; }

    /// <summary>按 <see cref="ChapterId"/> 顺序保存各章状态，索引即 (int)ChapterId。</summary>
    private ChapterState[] _states;

    /// <summary>已接线的“放置点 → 章节”绑定（含事件处理器，便于退订）。</summary>
    private readonly Dictionary<ChapterId, PlacementBinding> _bindings =
        new Dictionary<ChapterId, PlacementBinding>();

    private void Awake()
    {
        InitializeStates();
    }

    private void OnEnable()
    {
        // 重新订阅运行时通过 BindPlacement 建立的绑定（禁用/启用场景时不丢接线）
        foreach (KeyValuePair<ChapterId, PlacementBinding> pair in _bindings)
        {
            pair.Value.Placement.ItemPlaced += pair.Value.Handler;
        }

        // 第一章：始终以 Inspector 配置的放置目标为准重新绑定（BindPlacement 内部会先清掉旧绑定，幂等）
        if (lostPetPlacement != null)
        {
            BindPlacement(ChapterId.LostPet, lostPetPlacement, lostPetRequiredItem);
        }
        else
        {
            Debug.LogError("[" + nameof(ChapterManager) + "] 未配置 lostPetPlacement（SmallBoxPlacement 上的 ItemPlacement），"
                + "第一章将无法完成。请运行菜单 Tools/交互系统/配置章节状态系统（任务5），或手动拖入引用。", this);
        }
    }

    private void OnDisable()
    {
        // 退订全部放置事件，避免对象禁用后仍被回调
        foreach (KeyValuePair<ChapterId, PlacementBinding> pair in _bindings)
        {
            pair.Value.Placement.ItemPlaced -= pair.Value.Handler;
        }
    }

    // ---------- 状态查询 ----------

    /// <summary>查询指定章节的当前状态。非法枚举值按防御性处理返回 Locked。</summary>
    public ChapterState GetChapterState(ChapterId chapter)
    {
        if (!IsValidChapter(chapter) || _states == null)
        {
            return ChapterState.Locked;
        }

        return _states[(int)chapter];
    }

    /// <summary>指定章节是否为 Locked（未解锁）。</summary>
    public bool IsChapterLocked(ChapterId chapter)
    {
        return GetChapterState(chapter) == ChapterState.Locked;
    }

    /// <summary>指定章节是否为 Active（进行中）。</summary>
    public bool IsChapterActive(ChapterId chapter)
    {
        return GetChapterState(chapter) == ChapterState.Active;
    }

    /// <summary>指定章节是否为 Completed（已完成）。</summary>
    public bool IsChapterCompleted(ChapterId chapter)
    {
        return GetChapterState(chapter) == ChapterState.Completed;
    }

    // ---------- 状态推进 ----------

    /// <summary>
    /// 尝试完成指定章节。仅当该章节当前为 Active 时才推进：
    /// - Locked：拒绝（禁止在未解锁时提前完成后续章节）；
    /// - Completed：拒绝（重复完成请求不重复推进、不重复发事件）；
    /// - Active：本章→Completed 并发出 <see cref="ChapterCompleted"/>，随后把下一章 Locked→Active。
    /// </summary>
    /// <returns>是否真正完成了该章节。</returns>
    public bool TryCompleteChapter(ChapterId chapter)
    {
        if (!IsValidChapter(chapter))
        {
            Debug.LogError("[" + nameof(ChapterManager) + "] 非法章节 Id：" + chapter + "，拒绝完成。", this);
            return false;
        }

        if (_states[(int)chapter] != ChapterState.Active)
        {
            // 未解锁或已完成：零副作用、零事件。这是“顺序解锁 + 幂等”的核心拦截点。
            Debug.Log("[" + nameof(ChapterManager) + "] 章节 " + chapter + " 当前为 "
                + _states[(int)chapter] + "，完成请求被忽略（不推进、不发事件）。", this);
            return false;
        }

        // Publish a coherent current chapter before state/completion notifications.
        CurrentChapter = (int)chapter + 1 < ChapterCount ? (ChapterId?)((int)chapter + 1) : null;
        IsAllCompleted = !CurrentChapter.HasValue;

        // ---- 本章 Active → Completed ----
        SetState(chapter, ChapterState.Completed);

        // 状态落定后再发出完成事件，保证订阅者读到的是最新状态；每章恰好一次
        ChapterCompleted?.Invoke(chapter);

        // ---- 顺序解锁下一章 ----
        int nextIndex = (int)chapter + 1;
        if (nextIndex < ChapterCount)
        {
            ChapterId nextChapter = (ChapterId)nextIndex;

            // 只有仍处于 Locked 的下一章才解锁，避免异常状态下重复激活
            if (_states[nextIndex] == ChapterState.Locked)
            {
                SetState(nextChapter, ChapterState.Active);
            }

            CurrentChapter = nextChapter;
            IsAllCompleted = false;
        }
        else
        {
            // 最后一章完成：不再有进行中的章节
            CurrentChapter = null;
            IsAllCompleted = true;
        }

        return true;
    }

    // --------- 放置点接线（第一章已用；第二、第三章的扩展入口） ----------

    /// <summary>
    /// 把一个真实放置目标绑定为指定章节的完成条件：
    /// 该放置点上报“所需物品放置成功”时，自动尝试完成对应章节。
    /// 重复绑定同一章节会先退订旧绑定（幂等）。
    /// 第二、第三章未来实现完成条件时调用本方法即可，无需改动状态推进逻辑。
    /// </summary>
    /// <param name="chapter">要绑定的章节。</param>
    /// <param name="placement">该章节的正确放置目标（场景中的 ItemPlacement 实例）。</param>
    /// <param name="requiredItem">该放置点要求的物品 Id，与 ItemPlacement.RequiredItemId 保持一致。</param>
    public void BindPlacement(ChapterId chapter, ItemPlacement placement, ItemId requiredItem)
    {
        if (!IsValidChapter(chapter))
        {
            Debug.LogError("[" + nameof(ChapterManager) + "] BindPlacement 收到非法章节 Id：" + chapter, this);
            return;
        }

        if (placement == null)
        {
            Debug.LogError("[" + nameof(ChapterManager) + "] BindPlacement(" + chapter
                + ") 收到空的 ItemPlacement，绑定未建立。", this);
            return;
        }

        // 同章节重复绑定：先退订旧处理器，保证一个放置信号只触发一次
        if (_bindings.TryGetValue(chapter, out PlacementBinding old))
        {
            old.Placement.ItemPlaced -= old.Handler;
        }

        Action<ItemId> handler = placedItemId =>
            OnPlacementSignaled(chapter, requiredItem, placedItemId);

        placement.ItemPlaced += handler;
        _bindings[chapter] = new PlacementBinding(placement, handler, requiredItem);
    }

    /// <summary>放置点上报放置成功：物品 Id 与章节约定一致时才尝试完成章节。</summary>
    private void OnPlacementSignaled(ChapterId chapter, ItemId requiredItem, ItemId placedItemId)
    {
        if (placedItemId != requiredItem)
        {
            // 该放置点放了别的物品（理论上 ItemPlacement 已按 requiredItemId 限制），与本章节无关
            return;
        }

        TryCompleteChapter(chapter);
    }

    // ---------- 内部工具 ----------

    /// <summary>初始状态：第一章 Active，其余全部 Locked。</summary>
    private void InitializeStates()
    {
        _states = new ChapterState[ChapterCount];

        for (int i = 0; i < ChapterCount; i++)
        {
            _states[i] = ChapterState.Locked;
        }

        _states[(int)ChapterId.LostPet] = ChapterState.Active;
        CurrentChapter = ChapterId.LostPet;
        IsAllCompleted = false;
    }

    /// <summary>写入新状态并在真正变化时发出 <see cref="ChapterStateChanged"/>。</summary>
    private void SetState(ChapterId chapter, ChapterState state)
    {
        if (_states[(int)chapter] == state)
        {
            return;
        }

        _states[(int)chapter] = state;
        ChapterStateChanged?.Invoke(chapter, state);
    }

    /// <summary>章节 Id 是否落在有效范围内。</summary>
    private bool IsValidChapter(ChapterId chapter)
    {
        int index = (int)chapter;
        return index >= 0 && index < ChapterCount;
    }

    /// <summary>一条“放置点 → 章节”的运行时绑定。</summary>
    private struct PlacementBinding
    {
        public readonly ItemPlacement Placement;
        public readonly Action<ItemId> Handler;
        public readonly ItemId RequiredItem;

        public PlacementBinding(ItemPlacement placement, Action<ItemId> handler, ItemId requiredItem)
        {
            Placement = placement;
            Handler = handler;
            RequiredItem = requiredItem;
        }
    }
}
