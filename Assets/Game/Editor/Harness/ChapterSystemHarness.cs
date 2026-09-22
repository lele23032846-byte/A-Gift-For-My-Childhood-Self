using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>
/// 任务5「三章顺序状态管理」的自动化验收（Play 模式）：
/// - 初始状态必须为 LostPet=Active / AfraidOfDark=Locked / BirthdayAlone=Locked；
/// - 未解锁时直接请求完成第二、第三章必须被拒绝，状态零变化、事件零触发；
/// - 没有小盒时对准放置点按 E（真实失败路径）不得完成章节；
/// - 完整真实流程：拾取小盒 → 对准正确放置点按 E 真实放置成功，
///   LostPet=Completed / AfraidOfDark=Active / BirthdayAlone=Locked，
///   ChapterCompleted 恰好触发一次（LostPet），状态变化顺序正确；
/// - 重复放置请求、重复完成请求不能二次推进，第三章保持 Locked（不跳过第二章）；
/// - 结束后写出 章节系统_验收日志.md，并以退出码反映结果。
/// 输入驱动方式与任务4验收同款：InputTestRuntime + 直接驱动 PlayerInteractor 的生产方法。
/// 仅用于 Editor 自动化，不参与打包逻辑。
/// </summary>
public static class ChapterSystemHarness
{
    private const string ReportFileName = "章节系统_验收日志.md";

    private struct CheckResult
    {
        public string Name;
        public bool Passed;
        public string Detail;
    }

    private static readonly List<CheckResult> _results = new List<CheckResult>();

    private static PlayerInteractor _interactor;
    private static FirstPersonController _movement;
    private static Inventory _inventory;
    private static PickupItem _pickup;
    private static ItemPlacement _placement;
    private static ChapterManager _manager;
    private static GameObject _player;
    private static Camera _camera;
    private static Keyboard _keyboard;
    private static Collider _spotCollider;

    private static int _completedEventCount;
    private static ChapterId _lastCompletedChapter;
    private static readonly List<string> _stateChangeLog = new List<string>();
    private static bool _hadError;

    private static object _testRuntime;
    private static MethodInfo _runtimeUpdate;
    private static MethodInfo _createAndInitialize;

    private static Action _next;
    private static int _waitFrames;
    private static DateTime _deadline;
    private static EnterPlayModeOptions _previousOptions;
    private static bool _optionsOverridden;
    private static bool _finished;

    /// <summary>-executeMethod 入口：配置场景后进入 Play 模式验收。</summary>
    public static void RunBatch()
    {
        Application.logMessageReceived += OnLog;

        try
        {
            Scene scene = EditorSceneManager.OpenScene(ItemPlacementSetup.ScenePath, OpenSceneMode.Single);
            ChapterSystemSetup.Setup();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[章节系统验收] 场景配置完成。");
        }
        catch (Exception e)
        {
            Debug.LogError("[章节系统验收] 场景配置失败：" + e);
            EditorApplication.Exit(1);
            return;
        }

        _deadline = DateTime.Now.AddSeconds(300);

        // 关闭进入 Play 模式的域重载，使本静态验收状态机不被重置（脚本在启动时已全新编译）
        _previousOptions = EditorSettings.enterPlayModeOptions;
        EditorSettings.enterPlayModeOptionsEnabled = true;
        EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
        _optionsOverridden = true;

        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnEditorUpdate;
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            Wait(3, Begin);
        }
    }

    private static void OnEditorUpdate()
    {
        if (_finished)
        {
            return;
        }

        if (DateTime.Now > _deadline)
        {
            Record("验收看门狗", false, "验收流程超过 300s 未完成，判定失败。");
            FinishPlayMode();
            return;
        }

        if (!Application.isPlaying)
        {
            return;
        }

        if (_waitFrames > 0)
        {
            _waitFrames--;
            return;
        }

        if (_next != null)
        {
            Action step = _next;
            _next = null;
            try
            {
                step();
            }
            catch (Exception e)
            {
                Record("步骤执行：" + step.Method.Name, false,
                    e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace);
                FinishPlayMode();
            }
        }
    }

    private static void Begin()
    {
        _player = GameObject.Find("Player");
        _interactor = _player.GetComponent<PlayerInteractor>();
        _movement = _player.GetComponent<FirstPersonController>();
        _inventory = _player.GetComponent<Inventory>();
        _pickup = UnityEngine.Object.FindAnyObjectByType<PickupItem>();
        _placement = UnityEngine.Object.FindAnyObjectByType<ItemPlacement>();
        _manager = UnityEngine.Object.FindAnyObjectByType<ChapterManager>();
        _camera = _player.GetComponentInChildren<Camera>();
        _spotCollider = _placement != null ? _placement.InteractionCollider : null;

        if (_interactor == null || _movement == null || _inventory == null || _pickup == null
            || _placement == null || _manager == null || _camera == null || _spotCollider == null)
        {
            Record("关键对象齐全", false,
                "interactor=" + (_interactor != null) + " movement=" + (_movement != null)
                + " inventory=" + (_inventory != null) + " pickup=" + (_pickup != null)
                + " placement=" + (_placement != null) + " manager=" + (_manager != null)
                + " camera=" + (_camera != null) + " spotCollider=" + (_spotCollider != null));
            FinishPlayMode();
            return;
        }
        Record("关键对象齐全", true,
            "Player / PlayerInteractor / Inventory / PickupItem / ItemPlacement / ChapterManager / Camera / 交互碰撞体 均存在。");

        // ---- 事件订阅（后续灯光/UI/音效系统将按同样方式订阅） ----
        _manager.ChapterCompleted += OnChapterCompleted;
        _manager.ChapterStateChanged += OnChapterStateChanged;

        // ---- 验收标准 1：初始状态 ----
        Record("初始：LostPet = Active",
            _manager.GetChapterState(ChapterId.LostPet) == ChapterState.Active,
            "state=" + _manager.GetChapterState(ChapterId.LostPet));
        Record("初始：AfraidOfDark = Locked",
            _manager.GetChapterState(ChapterId.AfraidOfDark) == ChapterState.Locked,
            "state=" + _manager.GetChapterState(ChapterId.AfraidOfDark));
        Record("初始：BirthdayAlone = Locked",
            _manager.GetChapterState(ChapterId.BirthdayAlone) == ChapterState.Locked,
            "state=" + _manager.GetChapterState(ChapterId.BirthdayAlone));
        Record("初始：CurrentChapter = LostPet",
            _manager.CurrentChapter.HasValue && _manager.CurrentChapter.Value == ChapterId.LostPet,
            "current=" + (_manager.CurrentChapter.HasValue ? _manager.CurrentChapter.Value.ToString() : "null"));
        Record("初始：IsAllCompleted = false",
            !_manager.IsAllCompleted, "all=" + _manager.IsAllCompleted);
        Record("初始：便捷查询 IsChapterActive(LostPet)=true",
            _manager.IsChapterActive(ChapterId.LostPet), "x");
        Record("初始：开局背包为空", _inventory.Count == 0, "Count=" + _inventory.Count);

        // ---- 未解锁时禁止提前完成后续章节 ----
        bool early2 = _manager.TryCompleteChapter(ChapterId.AfraidOfDark);
        Record("Locked 状态请求完成第二章：返回 false", !early2, "ret=" + early2);
        bool early3 = _manager.TryCompleteChapter(ChapterId.BirthdayAlone);
        Record("Locked 状态请求完成第三章：返回 false", !early3, "ret=" + early3);
        Record("提前完成被拒绝后：三章状态保持 Active/Locked/Locked",
            _manager.IsChapterActive(ChapterId.LostPet)
            && _manager.IsChapterLocked(ChapterId.AfraidOfDark)
            && _manager.IsChapterLocked(ChapterId.BirthdayAlone),
            "states=" + SnapshotStates());
        Record("提前完成被拒绝后：ChapterCompleted 零触发",
            _completedEventCount == 0, "count=" + _completedEventCount);
        Record("提前完成被拒绝后：CurrentChapter 仍为 LostPet",
            _manager.CurrentChapter == ChapterId.LostPet,
            "current=" + _manager.CurrentChapter);

        // ---- 安装测试运行时（确定性输入），与既有验收同款 ----
        _movement.enabled = false;
        _interactor.enabled = false;

        Type testRuntimeType = Type.GetType(
            "UnityEngine.InputSystem.InputTestRuntime, Unity.InputSystem.TestFramework");
        if (testRuntimeType == null)
        {
            Record("测试运行时可用", false, "找不到 Unity.InputSystem.TestFramework 中的 InputTestRuntime。");
            FinishPlayMode();
            return;
        }
        Record("测试运行时可用", true, testRuntimeType.FullName);

        _testRuntime = Activator.CreateInstance(testRuntimeType);
        testRuntimeType.GetProperty("onShouldRunUpdate")
            .SetValue(_testRuntime, new Func<InputUpdateType, bool>(_ => true));
        _runtimeUpdate = testRuntimeType.GetMethod("Update");

        Type managerType = typeof(InputSystem).Assembly.GetType("UnityEngine.InputSystem.InputManager");
        _createAndInitialize = managerType.GetMethod(
            "CreateAndInitialize", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        object nativeManager = typeof(InputSystem)
            .GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null);

        object manager = _createAndInitialize.Invoke(
            null, new[] { _testRuntime, InputSystem.settings, false });
        typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic)
            .SetValue(null, manager);

        _keyboard = InputSystem.AddDevice<Keyboard>();

        InvokePrivate(_interactor, "Awake");
        InvokePrivate(_interactor, "OnEnable");

        // 相机复位后逐项验收
        AimAtSpot();
        StepNoItemNoComplete();
    }

    // ---------- 1. 没有小盒时按 E 不能完成章节 ----------

    private static void StepNoItemNoComplete()
    {
        _interactor.UpdateTarget();
        Record("无小盒时对准放置点：目标是 ItemPlacement",
            _interactor.CurrentTarget == _placement,
            "target=" + SafeName(_interactor.CurrentTarget));

        // 真实失败路径：没有物品按 E（TryInteract → CanInteract=false → 失败反馈，Interact 不执行）
        PressE();
        Pump();
        _interactor.HandleInteractInput();
        for (int i = 0; i < 6; i++)
        {
            Pump();
            _interactor.HandleInteractInput();
        }

        Record("无小盒按 E：放置点 IsPlaced 仍 false",
            !_placement.IsPlaced, "IsPlaced=" + _placement.IsPlaced);
        Record("无小盒按 E：章节状态保持 Active/Locked/Locked",
            _manager.IsChapterActive(ChapterId.LostPet)
            && _manager.IsChapterLocked(ChapterId.AfraidOfDark)
            && _manager.IsChapterLocked(ChapterId.BirthdayAlone),
            "states=" + SnapshotStates());
        Record("无小盒按 E：ChapterCompleted 零触发（不能靠按 E 完成）",
            _completedEventCount == 0, "count=" + _completedEventCount);
        ReleaseE();
        Pump();

        // ---- 真实拾取小盒 ----
        AimAtBox();
        _interactor.UpdateTarget();
        Record("对准小盒：目标是 PickupItem",
            _interactor.CurrentTarget == _pickup,
            "target=" + SafeName(_interactor.CurrentTarget));

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("按 E 拾取：背包出现 SmallBox",
            _inventory.Count == 1 && _inventory.HasItem(ItemId.SmallBox),
            "Count=" + _inventory.Count);
        Record("拾取后章节状态不变（拥有物品不等于完成章节）",
            _manager.IsChapterActive(ChapterId.LostPet)
            && _manager.IsChapterLocked(ChapterId.AfraidOfDark),
            "states=" + SnapshotStates());
        ReleaseE();
        Pump();

        // ---- 真实放置，推进章节 ----
        AimAtSpot();
        StepRealPlacement();
    }

    // ---------- 2. 真实放置成功 → 第一章完成、第二章解锁 ----------

    private static void StepRealPlacement()
    {
        _interactor.UpdateTarget();
        Record("拾取后回放置点：目标是 ItemPlacement",
            _interactor.CurrentTarget == _placement,
            "target=" + SafeName(_interactor.CurrentTarget));
        Record("拾取后 CanInteract=true（可以放置）",
            _placement.CanInteract(), "CanInteract=" + _placement.CanInteract());

        int changesBefore = _stateChangeLog.Count;

        PressE();
        Pump();
        _interactor.HandleInteractInput();

        Record("按 E 放置成功：IsPlaced=true",
            _placement.IsPlaced, "IsPlaced=" + _placement.IsPlaced);
        Record("放置成功后背包移除 SmallBox（Count=0）",
            _inventory.Count == 0, "Count=" + _inventory.Count);

        // ---- 验收标准 2：放置后状态 ----
        Record("放置后：LostPet = Completed",
            _manager.GetChapterState(ChapterId.LostPet) == ChapterState.Completed,
            "state=" + _manager.GetChapterState(ChapterId.LostPet));
        Record("放置后：AfraidOfDark = Active",
            _manager.GetChapterState(ChapterId.AfraidOfDark) == ChapterState.Active,
            "state=" + _manager.GetChapterState(ChapterId.AfraidOfDark));
        Record("放置后：BirthdayAlone = Locked",
            _manager.GetChapterState(ChapterId.BirthdayAlone) == ChapterState.Locked,
            "state=" + _manager.GetChapterState(ChapterId.BirthdayAlone));
        Record("放置后：CurrentChapter = AfraidOfDark",
            _manager.CurrentChapter == ChapterId.AfraidOfDark,
            "current=" + _manager.CurrentChapter);

        // ---- 事件：恰好一次、参数正确 ----
        Record("ChapterCompleted 恰好触发一次",
            _completedEventCount == 1, "count=" + _completedEventCount);
        Record("ChapterCompleted 参数为 LostPet",
            _lastCompletedChapter == ChapterId.LostPet,
            "last=" + _lastCompletedChapter);

        // ---- 状态变化顺序：本章先 Completed，下一章再 Active ----
        int newChanges = _stateChangeLog.Count - changesBefore;
        Record("本次推进产生 2 次 ChapterStateChanged",
            newChanges == 2, "newChanges=" + newChanges);

        bool orderOk = newChanges == 2
            && _stateChangeLog[changesBefore] == "LostPet->Completed"
            && _stateChangeLog[changesBefore + 1] == "AfraidOfDark->Active";
        Record("状态变化顺序正确（LostPet→Completed，随后 AfraidOfDark→Active）",
            orderOk,
            "log=[" + string.Join(", ",
                _stateChangeLog.GetRange(changesBefore, newChanges)) + "]");

        ReleaseE();
        Pump();

        StepNoDoubleAdvance();
    }

    // ---------- 3. 重复请求不能二次推进、不能跳过第二章 ----------

    private static void StepNoDoubleAdvance()
    {
        // 重复完成请求：直接对第一章（已 Completed）调用多次
        bool repeat1 = _manager.TryCompleteChapter(ChapterId.LostPet);
        bool repeat2 = _manager.TryCompleteChapter(ChapterId.LostPet);
        Record("对已完成的第一章重复请求：均返回 false",
            !repeat1 && !repeat2, "rets=(" + repeat1 + "," + repeat2 + ")");

        // 重复放置请求：再次对放置点发起真实交互（IsPlaced 拦截，直接 return，不发 ItemPlaced）
        _placement.Interact(_interactor);
        _placement.Interact(_interactor);
        Record("重复放置请求：IsPlaced 保持 true、不重复触发",
            _placement.IsPlaced, "IsPlaced=" + _placement.IsPlaced);

        // 第三章在任何重复请求后仍必须 Locked —— 没有跳过第二章
        Record("重复请求后：BirthdayAlone 仍 Locked（未跳过第二章）",
            _manager.IsChapterLocked(ChapterId.BirthdayAlone),
            "state=" + _manager.GetChapterState(ChapterId.BirthdayAlone));
        Record("重复请求后：AfraidOfDark 仍 Active",
            _manager.IsChapterActive(ChapterId.AfraidOfDark),
            "state=" + _manager.GetChapterState(ChapterId.AfraidOfDark));
        Record("重复请求后：LostPet 仍 Completed",
            _manager.IsChapterCompleted(ChapterId.LostPet),
            "state=" + _manager.GetChapterState(ChapterId.LostPet));
        Record("重复请求后：CurrentChapter 仍 AfraidOfDark",
            _manager.CurrentChapter == ChapterId.AfraidOfDark,
            "current=" + _manager.CurrentChapter);
        Record("重复请求后：ChapterCompleted 仍只触发过一次",
            _completedEventCount == 1, "count=" + _completedEventCount);

        // 放置点的交互碰撞体已在放置成功时禁用（射线不再命中，提示自然消失）
        Record("放置成功后交互碰撞体保持禁用",
            !_spotCollider.enabled, "enabled=" + _spotCollider.enabled);

        Record("全程无 Error/Exception", !_hadError, "hadError=" + _hadError);

        FinishPlayMode();
    }

    // ---------- 收尾 ----------

    private static void FinishPlayMode()
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

        if (Application.isPlaying)
        {
            EditorApplication.playModeStateChanged += WaitForExit;
            EditorApplication.ExitPlaymode();
        }
        else
        {
            CleanupEditor();
        }
    }

    private static void WaitForExit(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
        {
            EditorApplication.playModeStateChanged -= WaitForExit;
            CleanupEditor();
        }
    }

    private static void CleanupEditor()
    {
        Application.logMessageReceived -= OnLog;

        try
        {
            if (_testRuntime != null)
            {
                _testRuntime.GetType().GetMethod("Dispose")?.Invoke(_testRuntime, null);
            }

            Assembly inputAssembly = typeof(InputSystem).Assembly;
            Type nativeRuntimeType = inputAssembly.GetType(
                "UnityEngine.InputSystem.LowLevel.NativeInputRuntime");
            PropertyInfo instanceProp = nativeRuntimeType.GetProperty(
                "instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            object nativeRuntime = instanceProp.GetValue(null);
            object manager = _createAndInitialize.Invoke(
                null, new[] { nativeRuntime, InputSystem.settings, false });
            typeof(InputSystem).GetField("s_Manager", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, manager);
        }
        catch (Exception e)
        {
            Debug.LogError("[章节系统验收] 恢复原生 InputManager 失败：" + e);
        }

        if (_optionsOverridden)
        {
            EditorSettings.enterPlayModeOptionsEnabled = false;
            EditorSettings.enterPlayModeOptions = _previousOptions;
            _optionsOverridden = false;
        }

        WriteReport();

        int failures = 0;
        foreach (CheckResult r in _results)
        {
            if (!r.Passed)
            {
                failures++;
            }
        }

        Debug.Log("[章节系统验收] 完成，通过 " + (_results.Count - failures) + "/" + _results.Count + "。");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void WriteReport()
    {
        int failures = 0;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# 任务5 章节状态系统 — 自动化验收日志");
        builder.AppendLine();
        builder.AppendLine("- Unity 版本：6000.6.0f1");
        builder.AppendLine("- 执行方式：Editor batchmode / Play 模式；InputTestRuntime 驱动 E 键；");
        builder.AppendLine("  PlayerInteractor 射线/边沿输入、PickupItem 拾取、ItemPlacement 真实放置、");
        builder.AppendLine("  ChapterManager 状态查询/顺序推进/事件，均为生产代码真实执行。");
        builder.AppendLine("- 生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        builder.AppendLine();
        builder.AppendLine("| # | 验收项 | 结果 | 详情 |");
        builder.AppendLine("| - | ------ | ---- | ---- |");

        for (int i = 0; i < _results.Count; i++)
        {
            CheckResult r = _results[i];
            if (!r.Passed)
            {
                failures++;
            }

            string detail = (r.Detail ?? string.Empty).Replace("|", "\\|").Replace("\n", " ");
            builder.AppendLine("| " + (i + 1) + " | " + r.Name + " | "
                + (r.Passed ? "PASS" : "FAIL") + " | " + detail + " |");
        }

        builder.AppendLine();
        builder.AppendLine("状态变化时序记录：");
        foreach (string entry in _stateChangeLog)
        {
            builder.AppendLine("- " + entry);
        }

        builder.AppendLine();
        builder.AppendLine("汇总：" + (_results.Count - failures) + " 通过，" + failures + " 失败。");

        string path = Path.GetFullPath(ReportFileName);
        File.WriteAllText(path, builder.ToString(), new System.Text.UTF8Encoding(false));
        Debug.Log("[章节系统验收] 报告已写入：" + path);
    }

    // ---------- 辅助 ----------

    private static void Wait(int frames, Action then)
    {
        _waitFrames = frames;
        _next = then;
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(target, null);
    }

    private static void Pump()
    {
        _runtimeUpdate.Invoke(_testRuntime, new object[] { InputUpdateType.Dynamic });
    }

    private static void Record(string name, bool passed, string detail)
    {
        _results.Add(new CheckResult { Name = name, Passed = passed, Detail = detail });
        Debug.Log("[章节系统验收][" + (passed ? "PASS" : "FAIL") + "] " + name + " — " + detail);
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            _hadError = true;
        }
    }

    private static void OnChapterCompleted(ChapterId chapter)
    {
        _completedEventCount++;
        _lastCompletedChapter = chapter;
    }

    private static void OnChapterStateChanged(ChapterId chapter, ChapterState state)
    {
        _stateChangeLog.Add(chapter + "->" + state);
    }

    /// <summary>三章状态快照，便于日志阅读。</summary>
    private static string SnapshotStates()
    {
        return "[" + ChapterId.LostPet + "=" + _manager.GetChapterState(ChapterId.LostPet)
            + ", " + ChapterId.AfraidOfDark + "=" + _manager.GetChapterState(ChapterId.AfraidOfDark)
            + ", " + ChapterId.BirthdayAlone + "=" + _manager.GetChapterState(ChapterId.BirthdayAlone) + "]";
    }

    /// <summary>相机移动到放置点前方瞄准：自动在目标周围寻找无遮挡、且射线命中该目标的位置。</summary>
    private static void AimAtSpot()
    {
        if (!AimAt(_spotCollider, _placement))
        {
            throw new InvalidOperationException("找不到能命中放置点 InteractionCollider 的相机位置（可能被书架/鱼缸遮挡）。");
        }
    }

    /// <summary>相机移动到拾取小盒前方瞄准：自动寻找无遮挡位置。</summary>
    private static void AimAtBox()
    {
        Collider collider = _pickup.GetComponentInChildren<Collider>();
        if (!AimAt(collider, _pickup))
        {
            throw new InvalidOperationException("找不到能命中拾取小盒的相机位置。");
        }
    }

    /// <summary>
    /// 在目标周围按固定角度/半径环搜索相机位置，要求射线（忽略 Trigger）命中的第一个碰撞体
    /// 其 GetComponentInParent&lt;InteractableBase&gt; 就是期望目标（与生产代码判定一致）。
    /// </summary>
    private static bool AimAt(Collider target, InteractableBase expected)
    {
        Vector3 center = target.bounds.center;
        float[] radii = { 0.5f, 0.7f, 0.9f, 1.2f, 1.5f, 1.8f };

        for (int k = 0; k < 24; k++)
        {
            float angle = k * (360f / 24f) * Mathf.Deg2Rad;
            Vector3 horizontal = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));

            foreach (float radius in radii)
            {
                Vector3 cameraPosition = center + horizontal * radius + Vector3.up * 0.12f;
                Vector3 aimPoint = new Vector3(center.x, cameraPosition.y - 0.12f, center.z);
                Vector3 direction = (aimPoint - cameraPosition).normalized;

                if (Physics.Raycast(cameraPosition, direction, out RaycastHit hit,
                    2.2f, Physics.AllLayers, QueryTriggerInteraction.Ignore))
                {
                    InteractableBase hitBase = hit.collider.GetComponentInParent<InteractableBase>();
                    if (hitBase == expected)
                    {
                        _camera.transform.SetPositionAndRotation(
                            cameraPosition, Quaternion.LookRotation(direction));
                        Physics.SyncTransforms();
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void PressE()
    {
        Pump();

        using (StateEvent.From(_keyboard, out InputEventPtr eventPtr))
        {
            _keyboard.eKey.WriteValueIntoEvent(1f, eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
    }

    private static void ReleaseE()
    {
        Pump();

        using (StateEvent.From(_keyboard, out InputEventPtr eventPtr))
        {
            _keyboard.eKey.WriteValueIntoEvent(0f, eventPtr);
            InputSystem.QueueEvent(eventPtr);
        }
    }

    private static string SafeName(Component component)
    {
        return component != null ? component.gameObject.name : "null";
    }
}
