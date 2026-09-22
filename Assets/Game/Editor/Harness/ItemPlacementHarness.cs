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
/// 任务4「指定位置放置物品」的自动化验收（Play 模式）：
/// - 用 InputTestRuntime（InputSystem 自带测试运行时）替换原生运行时，确定性地驱动“按下/按住/松开 E”；
/// - 直接调用 PlayerInteractor 的生产方法 UpdateTarget / HandleInteractInput（组件本身被禁用，
///   仅绕过 Update 驱动，所有射线、目标、提示、边沿逻辑均为生产代码真实执行）；
/// - 覆盖验收标准与数据安全：无小盒不能放置、拾取后可放置、放置后背包移除+展示物件出现、
///   再次按 E 不重复消耗/不重复生成、放置成功事件只触发一次、全程无报错；
/// - 结束后写出 放置系统_验收日志.md，并以退出码反映结果。
/// 仅用于 Editor 自动化，不参与打包逻辑。
/// </summary>
public static class ItemPlacementHarness
{
    private const string ReportFileName = "放置系统_验收日志.md";

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
    private static InteractionPromptUI _ui;
    private static GameObject _player;
    private static Camera _camera;
    private static Keyboard _keyboard;
    private static Collider _spotCollider;

    private static int _placedEventCount;
    private static ItemId _lastPlacedId;
    private static bool _hadError;

    private static object _testRuntime;
    private static MethodInfo _runtimeUpdate;
    private static MethodInfo _createAndInitialize;
    private static object _nativeManager;

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
            ItemPlacementSetup.Setup();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[道具放置验收] 场景配置完成。");
        }
        catch (Exception e)
        {
            Debug.LogError("[道具放置验收] 场景配置失败：" + e);
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
        _ui = UnityEngine.Object.FindAnyObjectByType<InteractionPromptUI>();
        _camera = _player.GetComponentInChildren<Camera>();
        _spotCollider = _placement != null ? _placement.InteractionCollider : null;

        if (_interactor == null || _movement == null || _inventory == null || _pickup == null
            || _placement == null || _ui == null || _camera == null || _spotCollider == null)
        {
            Record("关键对象齐全", false,
                "interactor=" + (_interactor != null) + " movement=" + (_movement != null)
                + " inventory=" + (_inventory != null) + " pickup=" + (_pickup != null)
                + " placement=" + (_placement != null) + " ui=" + (_ui != null)
                + " camera=" + (_camera != null) + " spotCollider=" + (_spotCollider != null));
            FinishPlayMode();
            return;
        }
        Record("关键对象齐全", true,
            "Player / PlayerInteractor / Inventory / PickupItem / ItemPlacement / InteractionPromptUI / Camera / 交互碰撞体 均存在。");

        _placement.ItemPlaced += OnItemPlaced;

        // 场景接线核对（Inspector 绑定是否正确）
        Record("放置点配置：requiredItemId=SmallBox",
            _placement.RequiredItemId == ItemId.SmallBox,
            "RequiredItemId=" + _placement.RequiredItemId);
        Record("放置点配置：displayObject=PlacedSmallBox",
            _placement.DisplayObject != null && _placement.DisplayObject.name == ItemPlacementSetup.DisplayName,
            "displayObject=" + (_placement.DisplayObject != null ? _placement.DisplayObject.name : "null"));
        Record("放置点配置：interactionCollider=InteractionCollider 的 Collider",
            _spotCollider.gameObject.name == ItemPlacementSetup.ColliderName,
            "colliderOn=" + _spotCollider.gameObject.name);
        Record("放置点层级：SmallBox_Model 位于 PlacedSmallBox 之下",
            _placement.DisplayObject != null
            && _placement.DisplayObject.transform.Find(ItemPlacementSetup.DisplayModelName) != null,
            "hierarchy=" + ItemPlacementSetup.ShelfName + "/" + ItemPlacementSetup.PlacementName
            + "/" + ItemPlacementSetup.DisplayName + "/" + ItemPlacementSetup.DisplayModelName);

        // 回退彻底性：场景中不应存在自造的鱼缸/标记
        Record("场景中不存在自造鱼缸 FishTankArea",
            GameObject.Find("FishTankArea") == null,
            "fishTankArea=" + (GameObject.Find("FishTankArea") != null));
        Record("场景中不存在自造鱼缸方块 Tank_Body",
            GameObject.Find("Tank_Body") == null,
            "tankBody=" + (GameObject.Find("Tank_Body") != null));
        Record("场景中不存在自造放置标记 PlacementMarker",
            GameObject.Find("PlacementMarker") == null,
            "marker=" + (GameObject.Find("PlacementMarker") != null));

        // 放置点必须挂在场景已有的书架之下
        Record("放置点位于书架 MyBedroomBookshelf 之下",
            _placement.transform.parent != null
            && _placement.transform.parent.name == ItemPlacementSetup.ShelfName,
            "parent=" + (_placement.transform.parent != null ? _placement.transform.parent.name : "null"));

        // 几何校验：小盒在书架顶面、与鱼缸同高、水平相邻不重叠、落在书架范围内
        GameObject shelfObject = GameObject.Find(ItemPlacementSetup.ShelfName);
        GameObject bowlObject = GameObject.Find(ItemPlacementSetup.BowlName);
        Bounds shelfBounds = CombinedBounds(shelfObject);
        Bounds bowlBounds = CombinedBounds(bowlObject);
        Vector3 boxCenter = _spotCollider.bounds.center;
        float boxBaseY = _spotCollider.bounds.min.y;

        Record("小盒与鱼缸同处书架顶面（底面等高）",
            Mathf.Abs(boxBaseY - bowlBounds.min.y) < 0.02f,
            "boxBaseY=" + boxBaseY + " bowlBaseY=" + bowlBounds.min.y);

        float bowlRadius = Mathf.Max(bowlBounds.extents.x, bowlBounds.extents.z);
        float horizontal = Vector2.Distance(
            new Vector2(boxCenter.x, boxCenter.z),
            new Vector2(bowlBounds.center.x, bowlBounds.center.z));
        float dMin = bowlRadius + 0.125f + 0.05f - 0.02f;
        float dMax = bowlRadius + 0.125f + 0.05f + 0.2f;
        Record("小盒位于鱼缸旁边（水平相邻、距离合理）",
            horizontal >= dMin && horizontal <= dMax,
            "horizontal=" + horizontal + " expected [" + dMin + ", " + dMax + "]");

        float absDx = Mathf.Abs(boxCenter.x - bowlBounds.center.x);
        float absDz = Mathf.Abs(boxCenter.z - bowlBounds.center.z);
        bool noOverlap = absDx >= bowlBounds.extents.x + 0.125f + 0.03f
            || absDz >= bowlBounds.extents.z + 0.125f + 0.03f;
        Record("小盒与鱼缸不发生水平重叠",
            noOverlap, "dx=" + absDx + " dz=" + absDz);

        bool insideShelf = boxCenter.x >= shelfBounds.min.x + 0.125f - 0.02f
            && boxCenter.x <= shelfBounds.max.x - 0.125f + 0.02f
            && boxCenter.z >= shelfBounds.min.z + 0.125f - 0.02f
            && boxCenter.z <= shelfBounds.max.z - 0.125f + 0.02f;
        Record("小盒落在书架顶面范围内",
            insideShelf,
            "boxXZ=(" + boxCenter.x + ", " + boxCenter.z + ") shelfXZ [("
            + shelfBounds.min.x + ", " + shelfBounds.min.z + "), ("
            + shelfBounds.max.x + ", " + shelfBounds.max.z + ")]");

        // ---- 初始状态 ----
        Record("开局背包为空", _inventory.Count == 0, "Count=" + _inventory.Count);
        Record("开局展示物件隐藏",
            !_placement.DisplayObject.activeInHierarchy,
            "activeInHierarchy=" + _placement.DisplayObject.activeInHierarchy);
        Record("开局没有小盒时 CanInteract=false（不允许放置）",
            !_placement.CanInteract(), "CanInteract=" + _placement.CanInteract());

        // ---- 安装测试运行时（确定性输入），与既有交互系统验收同款 ----
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
        _nativeManager = typeof(InputSystem)
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
        StepNoItemPlacement();
    }

    // ---------- 1. 没有小盒时无法放置 ----------

    private static void StepNoItemPlacement()
    {
        _interactor.UpdateTarget();
        Record("没有小盒时对准放置点：目标是 ItemPlacement",
            _interactor.CurrentTarget == _placement,
            "target=" + SafeName(_interactor.CurrentTarget));
        Record("没有小盒时提示为「需要」文案",
            _ui.IsVisible && _ui.CurrentText != null && _ui.CurrentText.Contains("Need"),
            "visible=" + _ui.IsVisible + " text=\"" + _ui.CurrentText + "\"");

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("没有小盒时按 E：不消耗物品",
            _inventory.Count == 0,
            "Count=" + _inventory.Count);
        Record("没有小盒时按 E：不放置、不显示展示物件",
            !_placement.IsPlaced && !_placement.DisplayObject.activeInHierarchy,
            "IsPlaced=" + _placement.IsPlaced
            + " displayActive=" + _placement.DisplayObject.activeInHierarchy);
        ReleaseE();
        Pump();

        AimAtBox();
        _interactor.UpdateTarget();
        Record("对准小盒显示拾取提示",
            _interactor.CurrentTarget == _pickup && _ui.IsVisible
            && _ui.CurrentText != null && _ui.CurrentText.Contains("Pick up"),
            "target=" + SafeName(_interactor.CurrentTarget) + " text=\"" + _ui.CurrentText + "\"");

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("按 E 拾取小盒：背包出现 SmallBox",
            _inventory.Count == 1 && _inventory.HasItem(ItemId.SmallBox),
            "Count=" + _inventory.Count);
        Record("拾取成功后场景小盒消失",
            _pickup.IsPickedUp && !_pickup.gameObject.activeInHierarchy,
            "IsPickedUp=" + _pickup.IsPickedUp + " active=" + _pickup.gameObject.activeInHierarchy);
        ReleaseE();
        Pump();

        AimAtSpot();
        StepPlace();
    }

    // ---------- 2. 拾取小盒后可以放置 ----------

    private static void StepPlace()
    {
        _interactor.UpdateTarget();
        Record("拾取后回放置点：目标是 ItemPlacement",
            _interactor.CurrentTarget == _placement,
            "target=" + SafeName(_interactor.CurrentTarget));
        Record("拾取后提示变为放置文案（E - Place SmallBox）",
            _ui.IsVisible && _ui.CurrentText != null && _ui.CurrentText.Contains("Place"),
            "visible=" + _ui.IsVisible + " text=\"" + _ui.CurrentText + "\"");
        Record("拾取后 CanInteract=true（可以放置）",
            _placement.CanInteract(), "CanInteract=" + _placement.CanInteract());

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("按 E 放置成功：IsPlaced=true",
            _placement.IsPlaced, "IsPlaced=" + _placement.IsPlaced);
        Record("放置成功后背包移除小盒（Count=0）",
            _inventory.Count == 0, "Count=" + _inventory.Count);
        Record("放置成功后鱼缸旁展示小盒出现",
            _placement.DisplayObject.activeInHierarchy,
            "displayActive=" + _placement.DisplayObject.activeInHierarchy);
        Record("放置成功事件触发一次且 Id 正确",
            _placedEventCount == 1 && _lastPlacedId == ItemId.SmallBox,
            "eventCount=" + _placedEventCount + " lastId=" + _lastPlacedId);
        Record("放置成功后交互碰撞体禁用",
            !_spotCollider.enabled, "colliderEnabled=" + _spotCollider.enabled);
        ReleaseE();
        Pump();

        StepNoDoublePlace();
    }

    // ---------- 3. 再次按 E 不重复消耗 / 不重复生成 ----------

    private static void StepNoDoublePlace()
    {
        // 玩家继续对准放置点（CurrentTarget 缓存仍为放置点），再次按 E
        PressE();
        Pump();
        _interactor.HandleInteractInput();
        for (int i = 0; i < 12; i++)
        {
            Pump();
            _interactor.HandleInteractInput(); // 按住 12 个输入更新，无新按下事件
        }

        Record("再次按 E 不重复消耗物品",
            _inventory.Count == 0, "Count=" + _inventory.Count);
        Record("再次按 E 不重复生成模型（展示物件仍仅一个、状态不变）",
            _placement.DisplayObject.activeInHierarchy && _placedEventCount == 1,
            "displayActive=" + _placement.DisplayObject.activeInHierarchy
            + " eventCount=" + _placedEventCount);
        Record("再次按 E 放置状态不变（IsPlaced 仍 true）",
            _placement.IsPlaced, "IsPlaced=" + _placement.IsPlaced);
        Record("再次按 E 走失败反馈而非交互（CanInteract=false）",
            !_placement.CanInteract(), "CanInteract=" + _placement.CanInteract());
        ReleaseE();
        Pump();

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
            Debug.LogError("[道具放置验收] 恢复原生 InputManager 失败：" + e);
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

        Debug.Log("[道具放置验收] 完成，通过 " + (_results.Count - failures) + "/" + _results.Count + "。");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void WriteReport()
    {
        int failures = 0;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# 任务4 放置系统 — 自动化验收日志");
        builder.AppendLine();
        builder.AppendLine("- Unity 版本：6000.6.0f1");
        builder.AppendLine("- 执行方式：Editor batchmode / Play 模式；InputTestRuntime 驱动 E 键按下、按住、松开；");
        builder.AppendLine("  PlayerInteractor 的射线检测、目标管理、提示通知、边沿触发，PickupItem 拾取顺序，");
        builder.AppendLine("  ItemPlacement 放置条件/消耗顺序/事件，Inventory 移除，均为生产代码真实执行。");
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
        builder.AppendLine("汇总：" + (_results.Count - failures) + " 通过，" + failures + " 失败。");

        string path = Path.GetFullPath(ReportFileName);
        File.WriteAllText(path, builder.ToString(), new System.Text.UTF8Encoding(false));
        Debug.Log("[道具放置验收] 报告已写入：" + path);
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
        Debug.Log("[道具放置验收][" + (passed ? "PASS" : "FAIL") + "] " + name + " — " + detail);
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
        {
            // 任务5（ChapterManager）未配置其放置引用时的配置错误，不属于任务4，不计入本验收
            if (condition != null && condition.Contains("[ChapterManager]") && condition.Contains("lostPetPlacement"))
                return;
            _hadError = true;
        }
    }

    private static void OnItemPlaced(ItemId itemId)
    {
        _placedEventCount++;
        _lastPlacedId = itemId;
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
    /// 其 GetComponentInParent&lt;InteractableBase&gt; 就是期望目标（与生产代码 PlayerInteractor 判定一致）。
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

    /// <summary>合并物体下所有 Renderer 的世界 Bounds（与配置工具同口径）。</summary>
    private static Bounds CombinedBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.zero);
        }

        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        return b;
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
