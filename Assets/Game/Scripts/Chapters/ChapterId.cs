/// <summary>
/// 游戏章节的稳定标识（最小章节状态系统，任务5）。
/// 用枚举而不是字符串或场景 GameObject 引用：章节可改名、换场景、换演出，
/// 但 ChapterId 一旦在版本中发布就保持不变，章节状态与未来的存档都以它为准。
///
/// 顺序语义（重要）：
/// - 章节必须按枚举值从小到大的顺序解锁：完成第 N 章才解锁第 N+1 章；
/// - ChapterManager 的「下一章」由 (int)chapter + 1 计算；
///
/// 演进规则：
/// - 新增章节只允许在末尾追加枚举值（例如后续在 BirthdayAlone 之后追加）；
/// - 禁止重排顺序、禁止复用旧数值（枚举值会进入存档）。
/// </summary>
public enum ChapterId
{
    /// <summary>第一章：走失的宠物（小盒放置到书架顶、鱼缸旁边）。</summary>
    LostPet = 0,

    /// <summary>第二章：怕黑（本轮仅有状态数据与扩展入口，无完成条件接线）。</summary>
    AfraidOfDark = 1,

    /// <summary>第三章：独自过生日（本轮仅有状态数据与扩展入口，无完成条件接线）。</summary>
    BirthdayAlone = 2,
}

/// <summary>
/// ChapterId 的展示辅助：把稳定 Id 翻译成 UI 上显示的名称。
/// 与枚举同文件，保证“新增章节”时编译器能提醒补齐展示名。
/// </summary>
public static class ChapterIdExtensions
{
    /// <summary>获取该章节在 UI 中显示的名称（本轮不做章节 UI，仅预留给后续系统）。</summary>
    public static string GetDisplayName(this ChapterId chapter)
    {
        switch (chapter)
        {
            case ChapterId.LostPet:
                return "Lost Pet";
            case ChapterId.AfraidOfDark:
                return "Afraid of Dark";
            case ChapterId.BirthdayAlone:
                return "Birthday Alone";
            default:
                // 未配置展示名时回退到枚举名，而不是返回空字符串
                return chapter.ToString();
        }
    }
}
