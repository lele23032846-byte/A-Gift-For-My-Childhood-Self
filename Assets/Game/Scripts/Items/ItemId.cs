/// <summary>
/// 任务物品的稳定标识（通用拾取系统）。
/// 用枚举而不是字符串或场景 GameObject 引用：场景物件可增删、改名、换模型，
/// 但 ItemId 一旦在版本中发布就保持不变，Inventory、任务条件与未来的存档都以它为准。
///
/// 演进规则：
/// - 新增物品只允许在末尾追加枚举值；
/// - 禁止重排顺序、禁止复用旧数值（枚举值会进入存档）；
/// - 真实物品从 1 开始，0 保留给 None 表示“未设置”。
/// </summary>
public enum ItemId
{
    /// <summary>未设置/空值，避免默认值 0 意外指向真实物品。</summary>
    None = 0,

    /// <summary>第一章任务道具：小盒。</summary>
    SmallBox = 1,
}

/// <summary>
/// ItemId 的展示辅助：把稳定 Id 翻译成 UI 上显示的名称。
/// 与枚举同文件，保证“新增 Id”时编译器能提醒补齐展示名。
/// </summary>
public static class ItemIdExtensions
{
    /// <summary>获取该物品在物品栏 UI 中显示的名称。</summary>
    public static string GetDisplayName(this ItemId itemId)
    {
        switch (itemId)
        {
            case ItemId.SmallBox:
                return "小盒子";
            case ItemId.None:
                return string.Empty;
            default:
                // 未配置展示名时回退到枚举名，而不是返回空字符串
                return itemId.ToString();
        }
    }
}
