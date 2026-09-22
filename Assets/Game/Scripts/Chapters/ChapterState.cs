/// <summary>
/// 单个章节的状态（最小章节状态系统，任务5）。
/// 合法生命周期只有一条单向路径：
/// <code>
///   Locked ──(上一章完成)──&gt; Active ──(本章真实完成条件触发)──&gt; Completed
/// </code>
/// - Locked：未解锁。禁止完成（ChapterManager.TryCompleteChapter 直接拒绝、不发事件）；
/// - Active：已解锁、进行中。只有 Active 的章节允许被完成；
/// - Completed：已完成。重复完成请求一律拒绝，保证不会重复推进、重复发事件。
/// 状态不可回退（本轮不提供重置/回退接口）。
/// </summary>
public enum ChapterState
{
    /// <summary>未解锁：等待上一章完成。</summary>
    Locked = 0,

    /// <summary>已解锁、进行中：初始只有第一章处于该状态。</summary>
    Active = 1,

    /// <summary>已完成：终态，重复请求不再产生任何效果。</summary>
    Completed = 2,
}
