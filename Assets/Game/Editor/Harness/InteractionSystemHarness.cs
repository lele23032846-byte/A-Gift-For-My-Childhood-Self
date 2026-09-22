using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>
/// 通用射线交互系统的自动化验收（Play 模式）：
/// - 用 InputTestRuntime（InputSystem 自带测试运行时）替换原生运行时，确定性地驱动“按下/按住/松开 E”；
/// - 直接调用 PlayerInteractor 的生产方法 UpdateTarget / HandleInteractInput（组件本身被禁用，
///   仅绕过 Update 驱动，所有射线、目标、提示、边沿逻辑均为生产代码真实执行）；
/// - 结束后写出 交互系统_验收日志.md，并以退出码反映结果。
/// 仅用于 Editor 自动化，不参与打包逻辑。
/// </summary>
public static class InteractionSystemHarness
{
    private const string ReportFileName = "交互系统_验收日志.md";

    private struct CheckResult
    {
        public string Name;
        public bool Passed;
        public string Detail;
    }

    private static readonly List<CheckResult> _results = new List<CheckResult>();
    private static readonly List<GameObject> _tempObjects = new List<GameObject>();

    private static PlayerInteractor _interactor;
    private static TestInteractable _main;
    private static InteractionPromptUI _ui;
    private static FirstPersonController _movement;
    private static GameObject _player;
    private static Camera _camera;
    private static Transform _cameraRoot;
    private static Keyboard _keyboard;
    private static DoorInteractable _door;
    private static Collider _doorBlocker;
    private static Vector3 _doorClosedCenter;
    private static object _testRuntime;
    private static MethodInfo _runtimeUpdate;
    private static MethodInfo _createAndInitialize;

    // 持有被替换下来的原生 InputManager，防止其被 GC 终结后释放状态缓冲
    // （自然帧期间原生输入模块仍会更新它并切换静态状态缓冲，悬空会导致 mock 设备读到空指针）
    private static object _nativeManager;

    private static Action _next;
    private static int _waitFrames;
    private static Func<bool> _waitPredicate;
    private static DateTime _waitStart;
    private static double _waitMinRealSeconds;
    private static DateTime _deadline;
    private static EnterPlayModeOptions _previousOptions;
    private static bool _optionsOverridden;
    private static bool _finished;

    /// <summary>-executeMethod 入口：归类目录、搭建交互、配置卧室门，再进入 Play 模式验收。</summary>
    public static void RunBatch()
    {
        ProjectStructureReorganizer.Reorganize();
        int layer = InteractionSystemSetup.RunFullSetup();
        DoorInteractionSetup.Setup();
        UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
        Debug.Log("[交互系统验收] 搭建完成，Interactable Layer = " + layer);

        _deadline = DateTime.Now.AddSeconds(240);

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
            Record("验收看门狗", false, "验收流程超过 240s 未完成，判定失败。");
            FinishPlayMode();
            return;
        }

        if (!Application.isPlaying)
        {
            return;
        }

        if (_waitPredicate != null)
        {
            bool ready;
            try
            {
                ready = _waitPredicate()
                    && (DateTime.Now - _waitStart).TotalSeconds >= _waitMinRealSeconds;
            }
            catch (Exception e)
            {
                Record("等待条件异常：" + (_next != null ? _next.Method.Name : "?"),
                    false, e.ToString());
                FinishPlayMode();
                return;
            }

            if (ready)
            {
                _waitPredicate = null;
                _waitFrames = 0;
            }
            else
            {
                _waitFrames--;
                if (_waitFrames > 0)
                {
                    return;
                }

                Record("等待条件超时：" + (_next != null ? _next.Method.Name : "?"),
                    false, "未在指定帧数内满足等待条件。");
                FinishPlayMode();
                return;
            }
        }
        else if (_waitFrames > 0)
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
        _main = UnityEngine.Object.FindAnyObjectByType<TestInteractable>();
        _ui = UnityEngine.Object.FindAnyObjectByType<InteractionPromptUI>();
        _camera = _player.GetComponentInChildren<Camera>();
        _cameraRoot = FindChildByName(_player.transform, "CameraRoot");

        if (_interactor == null || _main == null || _ui == null || _camera == null)
        {
            Record("关键对象齐全", false,
                "interactor=" + (_interactor != null) + " main=" + (_main != null)
                + " ui=" + (_ui != null) + " camera=" + (_camera != null));
            FinishPlayMode();
            return;
        }
        Record("关键对象齐全", true, "PlayerInteractor / TestInteractable / InteractionPromptUI / Camera 均存在。");

        Record("开局没有目标时提示隐藏", !_ui.IsVisible, "IsVisible=" + _ui.IsVisible);

        // 1) 先在旧管理器上禁用驱动组件（FPC 不写摄像机；interactor 不跑 Update）
        _movement.enabled = false;
        _interactor.enabled = false;

        // 2) 安装测试运行时（确定性输入），替换原生 InputManager。
        //    测试运行时与 InputManager 均为 internal，预定义程序集无法直接引用，这里用反射接入
        //    （等价于 InputSystem 自身测试框架 InputTestFixture 的安装方式）。
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

        // 3) 测试运行时中显式添加键盘
        _keyboard = InputSystem.AddDevice<Keyboard>();

        // 4) 让 interactor 在新管理器上完成初始化（Awake 解析 action；OnEnable 启用 action）
        InvokePrivate(_interactor, "Awake");
        InvokePrivate(_interactor, "OnEnable");

        // 摄像机复位：准星水平朝 +Z
        _player.transform.rotation = Quaternion.identity;
        _cameraRoot.localRotation = Quaternion.identity;
        _camera.transform.localRotation = Quaternion.identity;

        // 1. 对准显示提示
        PlaceMain(1.4f);
        _interactor.UpdateTarget();
        bool aimPassed = _interactor.CurrentTarget == _main
                         && _ui.IsVisible
                         && _ui.CurrentText != null && _ui.CurrentText.Contains("Test");
        Record("对准测试物件时显示 E 键提示", aimPassed,
            "target=" + SafeName(_interactor.CurrentTarget) + " visible=" + _ui.IsVisible
            + " text=\"" + _ui.CurrentText + "\"");

        // 2. 按 E 一次
        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("按 E 只触发当前目标一次", _main.InteractCount == 1,
            "InteractCount=" + _main.InteractCount);

        // 3. 按住不连发（模拟 12 个输入更新，状态保持按下，无新按下事件）
        for (int i = 0; i < 12; i++)
        {
            Pump();
            _interactor.HandleInteractInput();
        }
        Record("按住 E 不连续触发", _main.InteractCount == 1,
            "按住 12 个输入更新后 InteractCount=" + _main.InteractCount);

        // 4. 松开再按可再次触发
        ReleaseE();
        Pump();
        _interactor.HandleInteractInput();
        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("松开后再次按下可再次触发", _main.InteractCount == 2,
            "InteractCount=" + _main.InteractCount);
        ReleaseE();
        Pump();

        // 5. 超出距离隐藏，回到距离恢复
        PlaceMain(3.1f);
        _interactor.UpdateTarget();
        Record("离开交互距离时提示消失", _interactor.CurrentTarget == null && !_ui.IsVisible,
            "target=" + SafeName(_interactor.CurrentTarget) + " visible=" + _ui.IsVisible);

        PlaceMain(1.4f);
        _interactor.UpdateTarget();
        Record("回到距离内提示恢复", _interactor.CurrentTarget == _main && _ui.IsVisible,
            "target=" + SafeName(_interactor.CurrentTarget) + " visible=" + _ui.IsVisible);

        // 6. 移开视角隐藏，移回恢复（抬头看天花板，避免与场景中门的位置耦合）
        _cameraRoot.localRotation = Quaternion.Euler(80f, 0f, 0f);
        _interactor.UpdateTarget();
        Record("移开视角时提示消失", _interactor.CurrentTarget == null && !_ui.IsVisible,
            "target=" + SafeName(_interactor.CurrentTarget) + " visible=" + _ui.IsVisible);

        _cameraRoot.localRotation = Quaternion.identity;
        _interactor.UpdateTarget();
        Record("视角移回后提示恢复", _interactor.CurrentTarget == _main && _ui.IsVisible,
            "target=" + SafeName(_interactor.CurrentTarget) + " visible=" + _ui.IsVisible);

        // 7. 非交互物体不显示提示
        PlaceMain(5f);
        CreatePlainCube("PlainCube_NonInteractable", 1f);
        _interactor.UpdateTarget();
        Record("非交互物体不显示提示", _interactor.CurrentTarget == null && !_ui.IsVisible,
            "target=" + SafeName(_interactor.CurrentTarget) + " visible=" + _ui.IsVisible);

        DestroyTempObjects();
        PlaceMain(1.4f);
        _interactor.UpdateTarget();
        Record("移除遮挡后目标恢复", _interactor.CurrentTarget == _main,
            "target=" + SafeName(_interactor.CurrentTarget));

        // 8. 两个靠近的可交互物：只选更近、只触发更近
        PlaceMain(5f);
        CreateInteractableCube("Interactable_Near", 1.2f);
        CreateInteractableCube("Interactable_Far", 1.6f);
        _interactor.UpdateTarget();
        TestInteractable near = GetByName<TestInteractable>("Interactable_Near");
        TestInteractable far = GetByName<TestInteractable>("Interactable_Far");
        Record("两个靠近的可交互物只选中更近者", _interactor.CurrentTarget == near,
            "target=" + SafeName(_interactor.CurrentTarget));

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        ReleaseE();
        Pump();
        Record("按 E 不会同时触发两个靠近的物体",
            near.InteractCount == 1 && far.InteractCount == 0,
            "near=" + near.InteractCount + " far=" + far.InteractCount);

        DestroyTempObjects();
        PlaceMain(1.4f);
        _interactor.UpdateTarget();
        Record("多目标场景清理后恢复主目标", _interactor.CurrentTarget == _main,
            "target=" + SafeName(_interactor.CurrentTarget));

        // 9. 条件判断与失败反馈
        _main.SetAllowInteract(false);
        _interactor.UpdateTarget();
        Record("条件不满足时仍可对准并显示提示",
            _interactor.CurrentTarget == _main && _ui.IsVisible,
            "target=" + SafeName(_interactor.CurrentTarget) + " visible=" + _ui.IsVisible);

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        ReleaseE();
        Pump();
        Record("条件不满足时按 E 不执行并给出失败反馈",
            _main.InteractCount == 2 && _main.FailedCount == 1,
            "InteractCount=" + _main.InteractCount + " FailedCount=" + _main.FailedCount);

        _main.SetAllowInteract(true);

        BeginDoor();
    }

    // ---------- 卧室门验收 ----------

    private static void BeginDoor()
    {
        _door = UnityEngine.Object.FindAnyObjectByType<DoorInteractable>();

        GameObject blockerObject = null;
        foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>())
        {
            if (go.name == DoorInteractionSetup.BlockerName)
            {
                blockerObject = go;
                break;
            }
        }

        bool blockerUnderHinge = blockerObject != null && _door != null
                                 && blockerObject.transform.IsChildOf(_door.transform);
        bool ready = _door != null && blockerObject != null && blockerUnderHinge;
        Record("卧室门：铰链与 DoorInteractable 就绪", ready,
            "door=" + (_door != null) + " blocker=" + (blockerObject != null)
            + " blockerUnderHinge=" + blockerUnderHinge);

        int expectedLayer = LayerMask.NameToLayer(InteractionSystemSetup.InteractableLayerName);
        Record("门板碰撞体位于 Interactable Layer",
            blockerObject != null && blockerObject.layer == expectedLayer,
            "layer=" + (blockerObject != null ? LayerMask.LayerToName(blockerObject.layer) : "null"));

        // 摄像机移动到门板正前方（沿碰撞体薄面法向）
        var blockerCollider = blockerObject.GetComponent<Collider>();
        _doorBlocker = blockerCollider;
        _doorClosedCenter = blockerCollider.bounds.center;
        Vector3 normal = Vector3.ProjectOnPlane(
            blockerObject.transform.TransformDirection(Vector3.forward), Vector3.up).normalized;
        Vector3 center = blockerCollider.bounds.center;
        Vector3 cameraPosition = center + normal * 0.45f;
        _camera.transform.SetPositionAndRotation(
            cameraPosition, Quaternion.LookRotation(center - cameraPosition));
        Physics.SyncTransforms();

        _interactor.UpdateTarget();
        Record("对准卧室门显示开门提示",
            _interactor.CurrentTarget == _door && _ui.IsVisible
            && _ui.CurrentText != null && _ui.CurrentText.Contains("Door"),
            "target=" + SafeName(_interactor.CurrentTarget)
            + " text=\"" + _ui.CurrentText + "\"");

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("按 E 门开始打开", _door.IsOpen,
            "IsOpen=" + _door.IsOpen + " angle=" + _door.CurrentAngle.ToString("F1"));

        WaitUntil(() => !_door.IsMoving, 60000, DoorOpenedStep, 1.5);
    }

    private static void DoorOpenedStep()
    {
        Record("门动画结束后约打开 90 度",
            Mathf.Abs(_door.CurrentAngle - 90f) < 3f,
            "angle=" + _door.CurrentAngle.ToString("F1"));

        Vector3 openedCenter = _doorBlocker.bounds.center;
        Vector3 delta = openedCenter - _doorClosedCenter;
        float horizontalMove = new Vector2(delta.x, delta.z).magnitude;
        Record("开门后门板保持竖直、整体水平转动",
            Mathf.Abs(Vector3.Dot(_doorBlocker.transform.up, Vector3.up)) > 0.99f
            && Mathf.Abs(delta.y) < 0.05f
            && horizontalMove > 0.1f,
            "upDot=" + Vector3.Dot(_doorBlocker.transform.up, Vector3.up).ToString("F3")
            + " verticalDelta=" + delta.y.ToString("F3")
            + " horizontalMove=" + horizontalMove.ToString("F3"));

        Record("打开后提示变为关门",
            _door.GetInteractionPrompt().Contains("Close"),
            "prompt=\"" + _door.GetInteractionPrompt() + "\"");

        // 松开开门时的 E，再按下触发关门（边沿检测要求 1→0→1）
        ReleaseE();
        Pump();

        PressE();
        Pump();
        _interactor.HandleInteractInput();
        Record("再次按 E 门开始关闭", !_door.IsOpen,
            "IsOpen=" + _door.IsOpen);

        WaitUntil(() => !_door.IsMoving, 60000, DoorClosedStep, 1.5);
    }

    private static void DoorClosedStep()
    {
        Record("门动画结束后回到约 0 度",
            Mathf.Abs(_door.CurrentAngle) < 3f,
            "angle=" + _door.CurrentAngle.ToString("F1"));

        FinishPlayMode();
    }

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
        // 恢复原生 InputManager（编辑器后续正常使用）
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
            Debug.LogError("[交互系统验收] 恢复原生 InputManager 失败：" + e);
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

        Debug.Log("[交互系统验收] 完成，通过 " + (_results.Count - failures) + "/" + _results.Count + "。");
        EditorApplication.Exit(failures == 0 ? 0 : 1);
    }

    private static void WriteReport()
    {
        int failures = 0;
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("# 通用射线交互系统 — 自动化验收日志");
        builder.AppendLine();
        builder.AppendLine("- Unity 版本：6000.6.0f1");
        builder.AppendLine("- 执行方式：Editor batchmode / Play 模式；InputTestRuntime 驱动 E 键按下、按住、松开；");
        builder.AppendLine("  PlayerInteractor 的射线检测、目标管理、提示通知、边沿触发均为生产代码真实执行。");
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
        Debug.Log("[交互系统验收] 报告已写入：" + path);
    }

    // ---------- 辅助 ----------

    private static void Wait(int frames, Action then)
    {
        _waitFrames = frames;
        _next = then;
    }

    /// <summary>等待到条件成立（每帧检查；超过 maxFrames 判负；可选最少真实等待秒数），再执行 then。</summary>
    private static void WaitUntil(Func<bool> predicate, int maxFrames, Action then,
        double minRealSeconds = 0.0)
    {
        _waitPredicate = predicate;
        _waitFrames = maxFrames;
        _waitStart = DateTime.Now;
        _waitMinRealSeconds = minRealSeconds;
        _next = then;
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Invoke(target, null);
    }

    /// <summary>推进一次输入更新（Dynamic，玩家更新），事件在该更新中被 action 状态机消费。</summary>
    private static void Pump()
    {
        _runtimeUpdate.Invoke(_testRuntime, new object[] { InputUpdateType.Dynamic });
    }

    private static void Record(string name, bool passed, string detail)
    {
        _results.Add(new CheckResult { Name = name, Passed = passed, Detail = detail });
        Debug.Log("[交互系统验收][" + (passed ? "PASS" : "FAIL") + "] " + name + " — " + detail);
    }

    private static void PlaceMain(float distance)
    {
        _main.transform.position = _camera.transform.position
            + _camera.transform.forward * distance;
        // 验收从 Editor 回调驱动，没有自然 FixedUpdate 同步点：手动把 Transform 变化刷入物理世界
        Physics.SyncTransforms();
    }

    private static void CreatePlainCube(string cubeName, float distance)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = cubeName;
        cube.transform.localScale = Vector3.one * 0.3f;
        cube.transform.position = _camera.transform.position
            + _camera.transform.forward * distance;
        // 保持 Default Layer（0），不挂 InteractableBase
        Physics.SyncTransforms();
        _tempObjects.Add(cube);
    }

    private static void CreateInteractableCube(string cubeName, float distance)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = cubeName;
        cube.transform.localScale = Vector3.one * 0.3f;
        cube.transform.position = _camera.transform.position
            + _camera.transform.forward * distance;
        SetLayerRecursively(cube, LayerMask.NameToLayer(InteractionSystemSetup.InteractableLayerName));
        cube.AddComponent<TestInteractable>();
        Physics.SyncTransforms();
        _tempObjects.Add(cube);
    }

    private static T GetByName<T>(string objectName) where T : Component
    {
        foreach (var t in UnityEngine.Object.FindObjectsByType<T>())
        {
            if (t.gameObject.name == objectName)
            {
                return t;
            }
        }

        return null;
    }

    private static void DestroyTempObjects()
    {
        foreach (GameObject go in _tempObjects)
        {
            if (go != null)
            {
                // Play 模式下 Destroy 延迟到帧末，验收是同步流程，使用立即销毁
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        _tempObjects.Clear();
        Physics.SyncTransforms();
    }

    private static void PressE()
    {
        // 自然帧期间原生管理器的更新会把静态状态缓冲切走；先 pump 一次 mock 运行时，
        // 让 StateEvent.From 从 mock 键盘自己的状态缓冲取数据
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

    private static Transform FindChildByName(Transform root, string targetName)
    {
        if (root.name == targetName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildByName(root.GetChild(i), targetName);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
        {
            SetLayerRecursively(child.gameObject, layer);
        }
    }

    private static string SafeName(Component component)
    {
        return component != null ? component.gameObject.name : "null";
    }
}
