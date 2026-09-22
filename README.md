# A Gift For My Childhood Self｜项目交接说明

## 1. 项目简介

这是一个第一人称叙事探索原型。当前可玩的主流程是：进入旧卧室 → 探索并与物体交互 → 拾取小盒 → 将小盒放到书架上的指定位置 → 完成第一段记忆并触发画面恢复与记忆卡片。

当前版本是开发原型，不代表三章内容均已完成，也未完成正式 Player Build 验收。

## 2. 环境要求

- Unity：`6000.6.0f1`
- 渲染管线：URP `17.6.0`
- 输入：Input System `1.20.0`
- UI：uGUI + TextMesh Pro
- 当前验证平台：Windows Editor Play Mode
- 主场景：`Assets/Game/Scenes/SC_Bedroom.unity`

首次打开时，Unity 会依据 `Packages/manifest.json` 和 `Packages/packages-lock.json` 恢复依赖。请勿复制其他开发者的 `Library`、`Temp`、`Logs`、`obj` 或 `UserSettings` 目录来替代正常导入。

## 3. 启动步骤

1. 使用 Unity Hub 或指定编辑器打开仓库根目录。
2. 确认编辑器版本为 `6000.6.0f1`，不要自动升级项目。
3. 等待 Package Manager 和脚本编译结束。
4. 打开 `Assets/Game/Scenes/SC_Bedroom.unity`。
5. 确认 Console 没有编译错误或 Missing Script。
6. 点击 Play。
7. 在标题界面开始游戏，使用鼠标观察、移动键移动、`E` 交互；暂停/返回操作以界面实际提示为准。

## 4. 目录职责

| 路径 | 职责 |
| --- | --- |
| `Assets/Game/Scenes` | 正式游戏场景；当前只有 `SC_Bedroom` |
| `Assets/Game/Scripts/Player` | 第一人称移动、视角和相机防穿 |
| `Assets/Game/Scripts/Interaction` | 通用交互接口、玩家射线交互、门交互 |
| `Assets/Game/Scripts/Items` | 物品 Id、背包、拾取、放置 |
| `Assets/Game/Scripts/Chapters` | 章节 Id、状态与顺序推进 |
| `Assets/Game/Scripts/UI` | 交互提示、背包、菜单、记忆演出 |
| `Assets/Game/Editor` | 场景配置、修复和验收工具；部分工具会保存或改写场景 |
| `Assets/Game/Prefabs` | 小盒预制体与材质 |
| `Assets/Game/Resources` | 运行时加载的记忆文案和中文字体 |
| `Assets/TripoAssets` | 外部/AI 模型与贴图；来源状态见 `ASSET_SOURCES.md` |
| `Assets/Generated/MemoryLook` | 记忆视觉层；包含运行时必需代码，不可整体删除 |
| `Assets/Generated/VisualPolish` | 烘焙、反射和材质通道等视觉生成资源 |
| `Assets/Settings` | URP 和渲染设置 |
| `Packages` | Unity Package Manager 依赖及锁文件 |
| `ProjectSettings` | Unity 项目设置、Build Settings、Tag/Layer 等 |

## 5. 核心流程与代码入口

```text
FirstPersonController
        ↓
PlayerInteractor ──查找──> InteractableBase
        ├── DoorInteractable
        ├── PickupItem ──写入──> Inventory ──通知──> InventoryHUD
        └── ItemPlacement ──消耗物品/发事件──> ChapterManager
                                             ↓
                          MemoryPresentationController / MemoryCardPresenter
```

重要入口：

- 玩家控制：`Assets/Game/Scripts/Player/FirstPersonController.cs`
- 交互目标选择：`Assets/Game/Scripts/Interaction/PlayerInteractor.cs`
- 拾取与放置：`Assets/Game/Scripts/Items/PickupItem.cs`、`ItemPlacement.cs`
- 章节状态：`Assets/Game/Scripts/Chapters/ChapterManager.cs`
- 记忆表现：`Assets/Game/Scripts/UI/MemoryPresentationController.cs`

## 6. 当前完成度

| 模块 | 状态 | 说明 |
| --- | --- | --- |
| 第一人称移动与视角 | 已完成 | Editor Play Mode 有历史运行记录 |
| 通用交互与提示 | 已完成 | 支持当前目标、距离、遮挡与按键触发 |
| 卧室门开关 | 已完成 | 历史专项验收 9/9 |
| 小盒拾取与背包 | 已完成 | 历史专项验收 15/15 |
| 小盒指定位置放置 | 已完成 | 历史专项验收 37/37 |
| 第一章 LostPet | 已完成 | 真实完成条件接到 `SmallBoxPlacement` |
| 第二章 AfraidOfDark | 部分完成 | 只有 ChapterId、状态和记忆卡文案，没有完成条件或正式玩法接线 |
| 第三章 BirthdayAlone | 部分完成 | 只有 ChapterId、状态和记忆卡文案，没有完成条件或正式玩法接线 |
| 存档/进度持久化 | 未实现 | 退出 Play 后章节与背包状态不会保留 |
| Player Build | 待验证 | 当前只确认 Windows Editor Play Mode 历史记录 |

第二、第三章不可按正常游戏流程完成。不要把 `MemoryCardCatalog` 中存在三条文案理解成三章均已完成。

## 7. 已知风险与修改注意事项

### 不可随意改名的场景对象

当前视觉/UI 层仍使用名称搜索。重命名下列对象前，必须同步搜索代码并完成回归：

- `GlassBowl`
- `UI_InteractionPrompt`
- `UI_Inventory`
- `MyYoungerSelf`
- `MemoryTitleView`
- `SmallBoxPlacement`
- `Player`

### `Generated` 不是全部可删除

`Assets/Generated/MemoryLook/Scripts/MemoryPresentationDreamcore.cs` 是 `MemoryPresentationController` 的 partial class 实现，提供 `BuildDreamcore()` 和 `CleanupDreamcore()`。删除它会导致编译失败。

### Editor 菜单可能改写项目

以下工具会创建、移动、删除资源或保存场景。运行前先确认工作区干净，建议只在临时分支或副本中使用：

- `Tools/项目结构/按规范归类目录`
- `Tools/交互系统/搭建交互测试场景`
- `Tools/交互系统/配置物品拾取与物品栏（任务3）`
- `Tools/交互系统/配置任务道具放置（任务4）`
- `Tools/交互系统/配置章节状态系统（任务5）`
- `Tools/交互系统/配置卧室门交互`
- `Tools/迟到的礼物/应用视觉与章节演示`
- `Tools/修复/*`

### 当前编译警告

`MemoryPresentationController` 与 `MemoryPresentationPolish` 使用了 Unity 已标记过时的 `FindObjectsByType` 重载。当前不阻止运行，后续升级 Unity 前应处理。

## 8. 验证方法

### 安全的手动 Smoke Test

1. 在干净工作区打开主场景。
2. 记录 Play 前的版本管理状态。
3. Play 后确认标题界面可见并可进入游戏。
4. 确认移动、鼠标观察、暂停和恢复正常。
5. 对准门，按 `E` 打开，再次按 `E` 关闭。
6. 在没有小盒时对准放置点，确认不能放置。
7. 拾取小盒，确认物品栏出现小盒且场景拾取物隐藏。
8. 返回书架放置点，放置小盒。
9. 确认背包移除小盒、展示物出现、第一章只完成一次、记忆卡和画面恢复触发。
10. 退出 Play，确认 Console 没有未说明的 Error/Exception。
11. 再次检查版本管理状态；手动 Smoke Test 不应修改正式资产。

### 自动化 Harness（会写入项目）

以下 Harness 会先运行场景 Setup、保存场景，并在根目录写验收 Markdown。不要在有未提交场景改动时执行。推荐在干净 clone、临时分支或副本中运行。

```powershell
& '<Unity.exe>' -batchmode -nographics -projectPath '<ProjectPath>' -executeMethod InteractionSystemHarness.RunBatch -logFile '<ProjectPath>\交互系统_batch.log'
& '<Unity.exe>' -batchmode -nographics -projectPath '<ProjectPath>' -executeMethod ItemPlacementHarness.RunBatch -logFile '<ProjectPath>\放置系统_验收_batch.log'
& '<Unity.exe>' -batchmode -nographics -projectPath '<ProjectPath>' -executeMethod ChapterSystemHarness.RunBatch -logFile '<ProjectPath>\章节系统_验收_batch.log'
& '<Unity.exe>' -batchmode -nographics -projectPath '<ProjectPath>' -executeMethod MemoryVisualVerification.RunStage1Batch -logFile '<ProjectPath>\视觉验收_batch.log'
```

退出码 `0` 表示 Harness 自身报告无失败。仍需检查日志中的编译错误、异常，以及执行前后的文件差异。

历史验收文档仅证明当时版本曾通过，不能替代当前代码的重新验证：

- `交互系统_验收日志.md`
- `门交互_验收日志.md`
- `拾取系统_验收日志.md`
- `放置系统_验收日志.md`
- `章节系统_验收日志.md`

## 9. Package 说明

| 包 | 用途/状态 |
| --- | --- |
| `com.unity.render-pipelines.universal` | 运行必需；当前渲染管线 |
| `com.unity.inputsystem` | 运行必需；移动和交互输入 |
| `com.unity.ugui` | 运行必需；HUD、菜单和记忆卡 |
| `com.unity.test-framework` | 开发/验收使用 |
| `com.unity.pipeline` | 开发期自动化与 Editor 控制；不是游戏玩法依赖 |
| `com.unity.ai.assistant` | 开发期工具；当前游戏代码未直接调用 |
| `com.unity.ai.inference` | 当前游戏代码未发现直接调用，删除前仍需在干净分支验证 |
| `com.unity.ai.navigation` | 当前游戏代码未发现直接调用 |
| `com.unity.probuilder` | 编辑期建模工具，当前游戏代码未直接调用 |
| `com.unity.visualscripting` | 当前游戏代码未发现直接调用 |
| `com.unity.timeline` | 当前游戏代码未发现直接调用 |
| Rider / Visual Studio IDE 包 | 开发环境集成 |

不要仅依据“未发现直接调用”移除包；还应检查场景、资源、Editor 工具和重新导入结果。

## 10. 构建状态

当前只承诺 Unity Editor Play Mode。尚未完成独立 Player Build 验收，目标平台、应用标识和发布参数也未最终确定。`companyName` 仍为 `DefaultCompany`，`applicationIdentifier` 为空。

正式构建前必须确定：

- 目标平台和所需 Unity 模块
- 公司/发行者名称
- Application Identifier
- 版本号与输出目录
- 场景列表
- 目标设备上的启动、输入、字体、画面和性能验收

## 11. 现有专项文档

- `拾取系统_交付说明.md`
- `放置系统_交付说明.md`
- `交互系统_验收日志.md`
- `门交互_验收日志.md`
- `拾取系统_验收日志.md`
- `放置系统_验收日志.md`
- `章节系统_验收日志.md`

专项文档用于追溯单项实现；若与本 README 或当前代码冲突，以当前代码、场景和最新验收结果为准。

