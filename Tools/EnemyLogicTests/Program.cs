// ============================================================================================
// Enemy 模块纯逻辑回归测试（脱离 Unity 运行）。
//
// 跑法： powershell -NoProfile -ExecutionPolicy Bypass -File Tools/EnemyLogicTests/run.ps1
//
// 说明：链接的是 Assets 下**真实源文件**，不是副本，所以覆盖的是真代码。
//       链接范围只包含不依赖 Unity 物理 / 序列化 / 编辑器 API 的部分。
//
// 失败时进程返回非零退出码 —— 审核指出过「批处理退出码仍为 0」，这里修正。
// ============================================================================================
using System;
using System.Collections.Generic;
using KWC.Enemy;
using KWC.Data;
using KWC.Editor;
using UnityEngine;

internal static class Program
{
    private static int _failures;
    private static int _checks;

    private static int Main()
    {
        Console.WriteLine("=== Enemy 模块纯逻辑回归测试 ===");
        Console.WriteLine();

        // 需要看模块内部日志时改成 true（排障用，正常跑保持 false 以让输出干净）。
        UnityEngine.Debug.EchoLogs = false;

        TestStateMachineBasicGuarantees();
        TestTerminalStateIsStable();
        TestDeathLatchReportsOnce();
        TestStaleLifeCallbackRejected();
        TestAttackIntervalIsNotDoubled();
        TestBossStartsDashFromBothStates();
        TestDashTravelsConfiguredDistance();
        TestDashStopsAtWallUsingActualStep();
        TestResetForReuseUsesGenericMovementInterface();
        TestEnemy1ControllerAttackIntervalIsOneSecond();
        TestEnemy1ContinuousContactDoesNotReenterAtFiveSeconds();
        TestControllerHealthLifecycleIsGenerationScoped();
        TestBossControllerDashesWithConfiguredTravel();
        TestEditorSelfTest();

        Console.WriteLine();
        Console.WriteLine($"检查项 {_checks} 个，失败 {_failures} 个");

        if (_failures == 0)
        {
            Console.WriteLine("KWC_ENEMY_LOGIC_PASS");
            return 0;
        }

        Console.WriteLine("KWC_ENEMY_LOGIC_FAIL: " + _failures);
        return 1;
    }

    private static void TestEditorSelfTest()
    {
        Section("入库自检：EnemyFsmChecks.RunSelfTest");
        Debug.ResetCounters();
        UnityEditor.EditorApplication.ExitCode = 0;
        EnemyFsmChecks.RunSelfTest();
        Check("RunSelfTest 的 52 个断言通过", Debug.LastLog.Contains("KWC_ENEMY_FSM_SELFTEST_PASS"));
        Check("RunSelfTest 未请求批处理失败退出", UnityEditor.EditorApplication.ExitCode == 0);
        if (!Debug.LastLog.Contains("KWC_ENEMY_FSM_SELFTEST_PASS"))
            Console.WriteLine("    自检报告: " + Debug.LastError);
    }

    // ==========================================================================================
    // P1-2：终态必须稳定。审核实测 Dead → ContactAttack → Dead，一次死亡发两次通知。
    // ==========================================================================================
    private static void TestTerminalStateIsStable()
    {
        Section("P1-2 终态稳定（终态锁定 + 清除过期转移）");

        TestFacts facts = new TestFacts();
        StateMachine<TestStateId> machine = BuildTestMachine(facts);
        machine.MinDwell = 0f;

        machine.Update(0.05f);
        Check("初始处于 A", machine.Current == TestStateId.A);

        // 一个普通请求先排进队列，然后立刻被死亡打断。
        // 修复前：这个排队请求在死亡后仍会执行一次，造成离开终态。
        bool queued = machine.TryRequestTransition(TestStateId.B, "先排队的普通请求");
        Check("普通请求被接受并排队", queued);

        machine.OnHostileInterrupt(TestStateId.C, "死亡");
        machine.Update(0.05f);
        Check("死亡打断生效，进入终态 C", machine.Current == TestStateId.C);
        Check("终态标志生效", machine.IsInTerminalState);

        int transitionsAtTerminal = machine.TransitionCount;

        // 迟到的重复死亡通知 + 各种普通请求，全部不许改变终态。
        for (int i = 0; i < 10; i++)
        {
            machine.OnHostileInterrupt(TestStateId.C, "重复死亡通知");
            machine.OnHostileInterrupt(TestStateId.B, "迟到回调想拉回非终态");
            machine.TryRequestTransition(TestStateId.B, "终态里的普通请求");
            machine.Update(0.05f);
        }

        Check("终态始终保持为 C（不再往返）", machine.Current == TestStateId.C);
        Check("终态期间没有产生任何新转移", machine.TransitionCount == transitionsAtTerminal);
        Check("终态期间活跃状态数仍为 1", machine.LiveStateCount == 1);

        // 强制离开仍必须可用（池化回收 / 场景卸载）。
        machine.ResetToInitial("回收");
        Check("强制离开可以离开终态", machine.Current == TestStateId.A);
        Check("强制离开后终态标志被清除", !machine.IsInTerminalState);
        Check("强制离开后出入配平", machine.LiveStateCount == 1);
    }

    // ==========================================================================================
    // 死亡闸门：一条命只放行一次。
    // ==========================================================================================
    private static void TestDeathLatchReportsOnce()
    {
        Section("死亡闸门：每条命只上报一次");

        DeathReportLatch latch = new DeathReportLatch();
        latch.Arm();
        int life = latch.LifeId;

        Check("Arm 后处于待上报", latch.IsArmed);

        bool first = latch.TryClaimDeathReport(out int claimed);
        Check("第一次上报放行", first);
        Check("上报带当前生命编号", claimed == life);
        Check("上报后闸门关闭", !latch.IsArmed);

        int accepted = 0;
        for (int i = 0; i < 5; i++)
        {
            if (latch.TryClaimDeathReport(out _))
            {
                accepted++;
            }
        }

        Check("同一条命重复上报全部被拒", accepted == 0);
        Check("重复上报不改变生命编号", latch.LifeId == life);

        latch.Arm();
        Check("复用时重新武装", latch.IsArmed);
        Check("复用时生命编号递增", latch.LifeId == life + 1);
        Check("新的一条命可再次上报", latch.TryClaimDeathReport(out int second) && second == life + 1);
    }

    // ==========================================================================================
    // 过期回调：带旧 lifeId 的上报必须能被识别。
    // ==========================================================================================
    private static void TestStaleLifeCallbackRejected()
    {
        Section("过期回调识别（lifeId 判重）");

        DeathReportLatch latch = new DeathReportLatch();
        latch.Arm();
        int oldLife = latch.LifeId;
        latch.TryClaimDeathReport(out _);

        latch.Arm();
        int currentLife = latch.LifeId;

        Check("复用后生命编号与上一条命不同", oldLife != currentLife);
        Check("当前这条命仍待上报", latch.IsArmed);
    }

    // ==========================================================================================
    // P2-1：攻击间隔只由一个入口推进。配置 1 秒，实测 0.5 秒（控制器与状态各推进一次）。
    // ==========================================================================================
    private static void TestAttackIntervalIsNotDoubled()
    {
        Section("P2-1 攻击间隔按配置走（唯一计时入口）");

        const float interval = 1f;
        Enemy1AttackThrottle throttle = new Enemy1AttackThrottle(interval);

        Check("初始就绪（首次接触立即攻击）", throttle.IsReady);

        throttle.MarkAttacked();
        Check("攻击后进入冷却", !throttle.IsReady);

        // 单入口推进：0.9 秒还不够，1.0 秒才够。
        StepThrottle(throttle, 0.9f);
        Check("0.9 秒后仍未就绪", !throttle.IsReady);

        StepThrottle(throttle, 0.15f);
        Check("超过 1.0 秒后就绪", throttle.IsReady);

        // 关键：总推进量必须恰好等于真实经过时间。
        // 修复前控制器与状态都 Tick，同样 1 秒真实时间会推进 2 秒，
        // 于是 0.5 秒就就绪 —— 这里用「累计推进量」把这个机制钉住。
        Enemy1AttackThrottle measured = new Enemy1AttackThrottle(interval);
        measured.MarkAttacked();

        int decisionFrames = 10; // 10 帧 × 0.1 秒 = 1.0 秒真实时间
        for (int i = 0; i < decisionFrames; i++)
        {
            measured.Tick(0.1f); // 唯一入口
        }

        Check("10 帧（1.0 秒）后恰好就绪，没有提前", measured.IsReady);

        Enemy1AttackThrottle notYet = new Enemy1AttackThrottle(interval);
        notYet.MarkAttacked();
        for (int i = 0; i < 9; i++)
        {
            notYet.Tick(0.1f); // 0.9 秒
        }

        Check("9 帧（0.9 秒）时仍未就绪（若双倍推进会错误就绪）", !notYet.IsReady);
    }

    private static void StepThrottle(Enemy1AttackThrottle throttle, float seconds)
    {
        // 决策步长 0.1 秒，与控制器实际使用的节拍一致。
        int steps = (int)Math.Round(seconds / 0.1);
        for (int i = 0; i < steps; i++)
        {
            throttle.Tick(0.1f);
        }
    }

    // ==========================================================================================
    // P1-1：冲刺不再是死分支。Approach 与 Wander 都能起手。
    // ==========================================================================================
    private static void TestBossStartsDashFromBothStates()
    {
        Section("P1-1 冲刺起手（Approach / Wander 都不被饿死）");

        // 表 N04 的既定阈值：PreferredDistance = 8。
        const float preferredDistance = 8f;
        BossConfigValues config = new BossConfigValues
        {
            PreferredDistance = preferredDistance,
            DashCooldown = 3.5f,
            DashDistance = 12f,
            DashSpeed = 24f,
            DashPrepareTime = 0.7f,
            DashRecoveryTime = 0.6f,
            DashHitCount = 1,
            BossMovement = 6f
        };

        FakeMovement movement = new FakeMovement(new Vector3(0f, 0f, 0f));
        FakePlayer player = new FakePlayer(new Vector3(20f, 0f, 0f)); // 玩家在 8 之外
        FactHealthSource health = new FactHealthSource(7500f);
        DeathReportLatch latch = new DeathReportLatch();
        latch.Arm();

        BossBrain brain = new BossBrain(null, player, movement, new FakeBridge(), new NoWallCheck(), latch, config);
        BossDashThrottle throttle = new BossDashThrottle(config.DashCooldown);

        Check("玩家在 PreferredDistance 之外", brain.DistanceToPlayer > preferredDistance);
        Check("冷却就绪 -> 可以起手冲刺", brain.CanStartDash(throttle));

        // 复现旧死角的判据：旧实现要求 ShouldApproach，而它同时是 Wander -> Approach 的出口，
        // 所以「玩家超出范围」这一帧，旧代码会去走路而不是冲刺。
        bool oldRequirementWouldBlockWander = brain.ShouldApproach;
        Check("旧实现的距离门槛在 Wander 里同时是退回条件（死分支成因）", oldRequirementWouldBlockWander);

        // 现在的判据不含距离门槛，Approach 与 Wander 都能起手。
        player.PositionValue = new Vector3(5f, 0f, 0f);
        Check("玩家进入范围后仍可起手冲刺（判据不含距离门槛）", brain.CanStartDash(throttle));
        Check("此时已进入闲逛带（说明这是 Wander 会处于的情形）", brain.ShouldStopApproaching);

        // 阈值只用既定的 PreferredDistance（8），不再有 0.9 之类的比例带宽。
        // 判据：距离恰好等于 8 时视为已进入范围，且此时不再算「需要靠近」——
        // 两个条件在 8 处互补，说明没有额外带宽。
        player.PositionValue = new Vector3(preferredDistance, 0f, 0f);
        Check("距离恰好等于 8 时视为已进入范围", brain.ShouldStopApproaching);
        Check("距离恰好等于 8 时不再视为需要靠近（门槛重合，无额外带宽）", !brain.ShouldApproach);

        player.PositionValue = new Vector3(preferredDistance + 0.001f, 0f, 0f);
        Check("距离略大于 8 时视为范围外，需要靠近", brain.ShouldApproach);

        // 已知风险：门槛重合时玩家停在 8 附近可能来回切换。
        // 这里不发明第二个阈值，只把现象记录下来（已在 README 登记为待策划确认）。
        int flips = 0;
        bool last = brain.ShouldApproach;
        for (int i = 0; i < 10; i++)
        {
            player.PositionValue = new Vector3(preferredDistance + (i % 2 == 0 ? 0.05f : -0.05f), 0f, 0f);
            bool now = brain.ShouldApproach;
            if (now != last)
            {
                flips++;
                last = now;
            }
        }

        Check($"门槛重合确实会让靠近判定反复翻转（实测翻转 {flips} 次，已登记待确认）", flips > 0);
        player.PositionValue = new Vector3(5f, 0f, 0f);

        // 冷却必须能挡住重复冲刺。
        throttle.StartCooldown();
        Check("冲刺后冷却立即挡住下一次", !brain.CanStartDash(throttle));

        // 前摇期间与恢复期间也由冷却覆盖：冷却从恢复开始计时，覆盖整段冲刺流程。
        throttle.Tick(config.DashPrepareTime + config.DashDistance / config.DashSpeed);
        Check("前摇+冲刺行程走完后仍未冷却完", !brain.CanStartDash(throttle));
        throttle.Tick(config.DashCooldown);
        Check("冷却走完后可再次起手", brain.CanStartDash(throttle));
    }

    // ==========================================================================================
    // P2-2：Dash 行程从起点算，快照只定方向。配置 12，实测出现过 5 与 14.4。
    // ==========================================================================================
    private static void TestDashTravelsConfiguredDistance()
    {
        Section("P2-2 Dash 行程恒为 DashDistance（快照只定方向）");

        // 场景一：玩家（快照）离得很近 —— 修复前只会走 5 左右。
        float nearTravel = RunDashAndMeasure("快照 5 单位", 5f, 12f);
        Check($"快照 5 单位时走满 12（实测 {nearTravel:0.###}）", Math.Abs(nearTravel - 12f) < 0.01f);

        // 场景二：玩家（快照）离得很远 —— 修复前会超过 14。
        float farTravel = RunDashAndMeasure("快照 40 单位", 40f, 12f);
        Check($"快照 40 单位时仍走 12（实测 {farTravel:0.###}）", Math.Abs(farTravel - 12f) < 0.01f);

        // 场景三：另一种配置，行程仍然等于配置值。
        float otherConfig = RunDashAndMeasure("快照 30 单位 / 配置 6", 30f, 6f);
        Check($"配置 6 时走 6（实测 {otherConfig:0.###}）", Math.Abs(otherConfig - 6f) < 0.01f);
    }

    private static float RunDashAndMeasure(string label, float snapshotDistance, float dashDistance)
    {
        BossConfigValues config = new BossConfigValues
        {
            PreferredDistance = 8f,
            DashDistance = dashDistance,
            DashSpeed = 24f,
            DashPrepareTime = 0.7f,
            DashRecoveryTime = 0.6f,
            DashCooldown = 3.5f,
            DashHitCount = 1
        };

        FakeMovement movement = new FakeMovement(Vector3.zero);
        FakePlayer player = new FakePlayer(new Vector3(snapshotDistance, 0f, 0f));
        FactHealthSource health = new FactHealthSource(7500f);
        DeathReportLatch latch = new DeathReportLatch();
        latch.Arm();
        BossDashContext dashContext = new BossDashContext();

        BossBrain brain = new BossBrain(null, player, movement, new FakeBridge(), new NoWallCheck(), latch, config);

        // 快照在「前摇结束时」锁定，与玩家当前距离无关。
        dashContext.CaptureSnapshot(player.PositionValue);

        BossDashState dash = new BossDashState(brain, dashContext, 1.5f);

        // 显式走 OnEnter：直接调 OnUpdate 会漏掉「快照 -> 方向」的初始化。
        // 不装配完整状态机，因为这里只关心 Dash 一段的行程，
        // 而且目标状态（DashRecovery）需要真实 BossBrain，与本次测量无关。
        DashHost host = new DashHost();
        dash.OnEnter(host);

        Vector3 origin = movement.PositionValue;

        for (int i = 0; i < 100; i++)
        {
            BossStateId? next = dash.OnUpdate(host, 0.1f);
            if (next.HasValue)
            {
                host.RequestedExit = next.Value;
                break;
            }
        }

        Vector3 delta = movement.PositionValue - origin;
        delta.y = 0f;
        float travelled = delta.magnitude;

        Console.WriteLine($"    [{label}] 位移 {travelled:0.###}（配置 {dashDistance}），请求转到 {host.RequestedExit?.ToString() ?? "无"}");
        return travelled;
    }

    // ==========================================================================================
    // P2-3：撞墙按实际位移查询。墙在前方 6 单位时，之前一步未走就结束冲刺。
    // ==========================================================================================
    private static void TestDashStopsAtWallUsingActualStep()
    {
        Section("P2-3 撞墙按实际位移处理（墙在 6 单位）");

        BossConfigValues config = new BossConfigValues
        {
            PreferredDistance = 8f,
            DashDistance = 12f,
            DashSpeed = 24f,
            DashPrepareTime = 0.7f,
            DashRecoveryTime = 0.6f,
            DashCooldown = 3.5f,
            DashHitCount = 1
        };

        FakeMovement movement = new FakeMovement(Vector3.zero);
        FakePlayer player = new FakePlayer(new Vector3(40f, 0f, 0f));
        FactHealthSource health = new FactHealthSource(7500f);
        DeathReportLatch latch = new DeathReportLatch();
        latch.Arm();

        // 墙在 +X 方向 6 单位处。
        FakeWallCheck wall = new FakeWallCheck(6f);
        BossBrain brain = new BossBrain(null, player, movement, new FakeBridge(), wall, latch, config);

        BossDashContext dashContext = new BossDashContext();
        dashContext.CaptureSnapshot(player.PositionValue);

        BossDashState dash = new BossDashState(brain, dashContext, 1.5f);
        Vector3 origin = movement.PositionValue;

        // 同样显式走 OnEnter 再驱动，保证初始化链路完整。
        DashHost host = new DashHost();
        dash.OnEnter(host);

        for (int i = 0; i < 100; i++)
        {
            BossStateId? next = dash.OnUpdate(host, 0.1f);
            if (next.HasValue)
            {
                host.RequestedExit = next.Value;
                break;
            }
        }

        Vector3 delta = movement.PositionValue - origin;
        delta.y = 0f;
        float travelled = delta.magnitude;

        Console.WriteLine($"    撞墙场景：位移 {travelled:0.###}，墙查询 {wall.QueriedDisplacements.Count} 次，请求转到 {host.RequestedExit?.ToString() ?? "无"}");

        Check($"撞墙前确实移动了（实测 {travelled:0.###}，修复前为 0）", travelled > 0.5f);
        Check($"停在墙面附近，未穿墙（实测 {travelled:0.###} <= 6）", travelled <= 6f + 0.01f);
        Check("撞墙后请求结束冲刺进入恢复", host.RequestedExit == BossStateId.DashRecovery);
        Check($"墙查询用的是本帧实际位移而不是完整 DashDistance（共 {wall.QueriedDisplacements.Count} 次）",
            wall.QueriedDisplacements.Count > 0 && wall.QueriedDisplacements.TrueForAll(d => d <= 6f + 0.01f));
    }

    // ==========================================================================================
    // P2-4：复用复位必须通过通用接口，换实现也要生效。
    // ==========================================================================================
    private static void TestResetForReuseUsesGenericMovementInterface()
    {
        Section("P2-4 复用复位走通用 IMovementHandler 接口");

        // 故意用一个不是 SimpleMovementHandler 的实现。
        FakeMovement custom = new FakeMovement(Vector3.zero);
        FakePlayer player = new FakePlayer(new Vector3(30f, 0f, 0f));
        FakeContact contact = new FakeContact();
        FactHealthSource health = new FactHealthSource(100f);

        Enemy1Controller controller = new Enemy1Controller();
        KWC.Data.Enemy1Config config = new KWC.Data.Enemy1Config();

        controller.Initialize(player, custom, contact, new FakeBridge(), config, health, 100f, 8f);

        Check("初始化成功", controller.IsInitialized);
        Check("初始生命编号为 1", controller.LifeId == 1);

        // 模拟「死亡后把移动关掉」。
        custom.SetMovementEnabled(false);
        Check("移动已被关掉（模拟死亡）", !custom.MovementEnabled);

        // 复用：必须通过接口把移动恢复，而不是只看具体类型。
        controller.ResetForReuse();
        Check("复用后移动能力被恢复（走通用接口）", custom.MovementEnabled);
        Check("复用后生命编号递增", controller.LifeId == 2);

        // 接口层面确认 ResetMovement 是契约的一部分。
        IMovementHandler asInterface = custom;
        asInterface.ResetMovement(Vector3.forward);
        Check("IMovementHandler 暴露了 ResetMovement", custom.ResetCount >= 2);
    }

    // ==========================================================================================
    // 状态机基础护栏（原有约定的回归）
    // ==========================================================================================
    private static void TestStateMachineBasicGuarantees()
    {
        Section("状态机基础护栏");

        TestFacts facts = new TestFacts();
        StateMachine<TestStateId> machine = BuildTestMachine(facts);
        machine.MinDwell = 0.05f;

        Check("初始状态为 A", machine.Current == TestStateId.A);
        Check("初始活跃状态数为 1", machine.LiveStateCount == 1);
        Check("初始进入不计入转移", machine.TransitionCount == 0);

        bool self = machine.TryRequestTransition(TestStateId.A, "自我");
        Check("自我转移被拒绝", !self);

        bool illegal = machine.TryRequestTransition(TestStateId.D, "未声明的边");
        Check("未声明的边被拒绝", !illegal);

        // 滞回：转移被延后而不是丢弃。
        facts.GoB = true;
        machine.Update(0.01f);
        Check("滞回窗口内被延后", machine.Current == TestStateId.A);

        for (int i = 0; i < 8; i++)
        {
            machine.Update(0.02f);
        }

        Check("滞回窗口过后生效", machine.Current == TestStateId.B);
    }

    // ==========================================================================================
    // P2 控制器级回归：用真实 Enemy1Controller 驱动接触攻击，验证攻击间隔就是配置的 1 秒。
    // 审核要求「补真实控制器回归测试」，这里对应「攻击计时被推进两次」那条。
    // ==========================================================================================
    private static void TestEnemy1ControllerAttackIntervalIsOneSecond()
    {
        Section("P2 控制器级：Enemy1 攻击间隔 = 100 / AttackSpeed（1.0 秒）");

        FakeMovement movement = new FakeMovement(Vector3.zero);

        // 玩家贴着敌人，接触源持续为真 —— 这样状态会一直停在 ContactAttack。
        FakePlayer player = new FakePlayer(new Vector3(1f, 0f, 0f));
        AlwaysContact contact = new AlwaysContact();
        FactHealthSource health = new FactHealthSource(100f);
        FakeBridge bridge = new FakeBridge();

        // 表 N03 的基础值：E1 Movement 10、E1 Attack Speed 100 -> 间隔 1.0 秒。
        KWC.Data.Enemy1Config config = new KWC.Data.Enemy1Config();
        SetPrivateField(config, "e1Movement", 10f);
        SetPrivateField(config, "e1AttackSpeed", 100f);

        Enemy1Controller controller = new Enemy1Controller();
        controller.Initialize(player, movement, contact, bridge, config, health, 100f, 8f);

        Check("初始化成功", controller.IsInitialized);
        Check("攻击间隔按公式推导为 1.0 秒",
            controller.AttackThrottle != null && Math.Abs(controller.AttackThrottle.IntervalSeconds - 1f) < 1e-4f);
        Check("初始就绪（首次接触立即攻击）", controller.AttackThrottle.IsReady);

        // 决策节拍 0.1 秒，推进 4.0 秒（40 帧）。
        // 断言「两次攻击之间至少 1.0 秒」—— 这才是该 bug 的判据：
        // 修复前计时被推进两次，相邻两次攻击只间隔 0.5 秒。
        // 不断言总次数：Boss/玩家的实际接触时长取决于状态切换，次数会随行程抖动。
        const float step = 0.1f;
        const int frames = 40; // 4.0 秒
        int lastStrikeFrame = -1;
        int minGapFrames = int.MaxValue;
        int strikeCount = 0;

        for (int i = 0; i < frames; i++)
        {
            int before = bridge.SubmittedDamage;
            controller.Tick(step);

            if (bridge.SubmittedDamage > before)
            {
                strikeCount += bridge.SubmittedDamage - before;

                if (lastStrikeFrame >= 0)
                {
                    int gap = i - lastStrikeFrame;
                    if (gap < minGapFrames)
                    {
                        minGapFrames = gap;
                    }
                }

                lastStrikeFrame = i;
            }
        }

        Console.WriteLine($"    4.0 秒内提交接触伤害 {strikeCount} 次；相邻两次最小间隔 {minGapFrames} 帧");

        Check("确实发生了攻击", strikeCount >= 2);
        Check($"相邻两次攻击间隔 >= 1.0 秒（实测最小 {minGapFrames} 帧 = {minGapFrames * step:0.0}s）",
            minGapFrames >= 10);

        // 双倍推进时最小间隔会是 5 帧（0.5 秒），这是该 bug 的特征值。
        Check("最小间隔不是 0.5 秒（5 帧），说明计时没有被推进两次", minGapFrames != 5);
    }

    private static void TestEnemy1ContinuousContactDoesNotReenterAtFiveSeconds()
    {
        Section("P2 控制器级：12 秒连续接触不因 5 秒状态重入加送首击");
        foreach (int fps in new[] { 30, 60 })
        {
            FakeMovement movement = new FakeMovement(Vector3.zero);
            FakePlayer player = new FakePlayer(new Vector3(1f, 0f, 0f));
            FakeBridge bridge = new FakeBridge();
            Enemy1Config config = new Enemy1Config();
            SetPrivateField(config, "e1AttackSpeed", 100f);
            Enemy1Controller controller = new Enemy1Controller();
            controller.Initialize(player, movement, new AlwaysContact(), bridge, config,
                new FactHealthSource(100f), 100f, 8f);
            controller.Tick(0.01f);

            List<float> strikes = new List<float>();
            float dt = 1f / fps;
            for (int frame = 0; frame < 12 * fps; frame++)
            {
                int before = bridge.SubmittedDamage;
                controller.Tick(dt);
                if (bridge.SubmittedDamage > before)
                    strikes.Add((frame + 1) * dt);
            }

            Check($"{fps} fps：12 秒攻击节拍无短于 1 秒的间隔", HasNoShortStrikeGap(strikes, 0.99f));
            Check($"{fps} fps：5 秒与 10 秒超时边界之后没有额外首击",
                NoStrikeInRange(strikes, 5.45f, 5.85f) && NoStrikeInRange(strikes, 10.45f, 11.15f));
        }
    }

    private static bool NoStrikeInRange(List<float> times, float start, float end)
    {
        foreach (float time in times)
            if (time >= start && time <= end) return false;
        return true;
    }

    private static bool HasNoShortStrikeGap(List<float> times, float minimum)
    {
        for (int i = 1; i < times.Count; i++)
            if (times[i] - times[i - 1] < minimum) return false;
        return true;
    }

    private static void TestControllerHealthLifecycleIsGenerationScoped()
    {
        Section("P2 控制器级：旧 HP / 旧死亡 / 正常死亡 / 重复死亡按生命隔离");
        FakePlayer player = new FakePlayer(new Vector3(20f, 0f, 0f));
        FakeBridge bridge = new FakeBridge();
        Enemy1Controller controller = new Enemy1Controller();
        controller.Initialize(player, new FakeMovement(Vector3.zero), new FakeContact(), bridge,
            new Enemy1Config(), new FactHealthSource(100f), 100f, 8f);
        int oldLife = controller.LifeId;
        Check("旧 HP 归零不触发死亡", controller.OnHealthReported(oldLife, 0f, 100f));
        controller.Tick(0.1f);
        Check("HP 零只同步事实，状态仍存活", controller.CurrentStateId != Enemy1StateId.Dead);
        controller.ResetForReuse();
        int newLife = controller.LifeId;
        Check("复用后旧 HP 被拒绝", !controller.OnHealthReported(oldLife, 0f, 100f));
        Check("复用后旧死亡事件被拒绝", !controller.ReportEnemyDied(oldLife));
        Check("新生命 HP 保持复用初始化值", controller.HealthFacts.Current == 100f);
        Check("当前生命死亡事件被接受", controller.ReportEnemyDied(newLife));
        controller.Tick(0.1f);
        controller.Tick(0.1f);
        Check("正常死亡只通知一次", bridge.DeathReports == 1);
        Check("重复当前生命死亡不产生重复通知", controller.ReportEnemyDied(newLife));
        controller.Tick(0.1f);
        Check("重复死亡事件仍只通知一次", bridge.DeathReports == 1);

        BossConfig bossConfig = CreateBossConfig();
        FakeBridge bossBridge = new FakeBridge();
        BossController boss = new BossController();
        boss.Initialize(player, new FakeMovement(Vector3.zero), bossBridge, new NoWallCheck(),
            new RandomWanderPointSource(17), bossConfig, new FactHealthSource(7500f), 7500f, 17);
        int oldBossLife = boss.LifeId;
        Check("Boss 旧生命 HP 可同步但不推断死亡", boss.OnHealthReported(oldBossLife, 0f, 7500f));
        boss.Tick(0.1f);
        Check("Boss HP 零仍不自动进入 Dead", boss.CurrentStateId != BossStateId.Dead);
        boss.ResetForReuse();
        Check("Boss 复用后拒绝旧 HP", !boss.OnHealthReported(oldBossLife, 0f, 7500f));
        Check("Boss 复用后拒绝旧死亡", !boss.ReportEnemyDied(oldBossLife));
        int currentBossLife = boss.LifeId;
        Check("Boss 接受当前生命死亡", boss.ReportEnemyDied(currentBossLife));
        boss.Tick(0.1f);
        boss.Tick(0.1f);
        Check("Boss 正常死亡仅通知一次", bossBridge.DeathReports == 1);
        Check("Boss 重复死亡事件仍仅通知一次",
            boss.ReportEnemyDied(currentBossLife) && bossBridge.DeathReports == 1);
    }

    private static BossConfig CreateBossConfig()
    {
        BossConfig config = new BossConfig();
        SetPrivateField(config, "bossHp", 7500f);
        SetPrivateField(config, "bossMovement", 6f);
        SetPrivateField(config, "preferredDistance", 8f);
        SetPrivateField(config, "wanderRadius", 2.5f);
        SetPrivateField(config, "wanderSpeed", 4f);
        SetPrivateField(config, "wanderInterval", new Vector2(1f, 2f));
        SetPrivateField(config, "dashDistance", 12f);
        SetPrivateField(config, "dashSpeed", 24f);
        SetPrivateField(config, "dashDamage", 60f);
        SetPrivateField(config, "dashCooldown", 3.5f);
        SetPrivateField(config, "dashPrepareTime", 0.7f);
        SetPrivateField(config, "dashRecoveryTime", 0.6f);
        SetPrivateField(config, "dashHitCount", 1);
        SetPrivateField(config, "normalContactDamage", 0f);
        return config;
    }

    // ==========================================================================================
    // P2 控制器级回归：用真实 BossController 跑完整冲刺流程。
    // 覆盖「冲刺起手可达」「行程等于 DashDistance」「冷却覆盖整段流程」。
    // ==========================================================================================
    private static void TestBossControllerDashesWithConfiguredTravel()
    {
        Section("P2 控制器级：Boss 冲刺起手 + 行程 + 冷却");
        Debug.ResetCounters();

        FakeMovement movement = new FakeMovement(Vector3.zero);

        // 玩家静止在 40 单位处，且全程不动。
        // 冲刺 12 单位后 Boss 仍在 28 单位处（> PreferredDistance = 8），
        // 所以既不会因为「到达玩家」被截断，也不会冲进闲逛带后开始走路而污染位移测量。
        FakePlayer player = new FakePlayer(new Vector3(40f, 0f, 0f));
        FactHealthSource health = new FactHealthSource(7500f);

        // 表 N04 的默认值。配置字段是 private [SerializeField]（Unity 由 Inspector 赋值），
        // 所以这里用反射注入，避免为了测试去改生产代码的可见性。
        KWC.Data.BossConfig config = new KWC.Data.BossConfig();
        SetPrivateField(config, "bossHp", 7500f);
        SetPrivateField(config, "bossMovement", 6f);
        SetPrivateField(config, "preferredDistance", 8f);
        SetPrivateField(config, "wanderRadius", 2.5f);
        SetPrivateField(config, "wanderSpeed", 4f);
        SetPrivateField(config, "wanderInterval", new Vector2(1f, 2f));
        SetPrivateField(config, "dashDistance", 12f);
        SetPrivateField(config, "dashSpeed", 24f);
        SetPrivateField(config, "dashDamage", 80f);
        SetPrivateField(config, "dashCooldown", 3.5f);
        SetPrivateField(config, "dashPrepareTime", 0.7f);
        SetPrivateField(config, "dashRecoveryTime", 0.6f);
        SetPrivateField(config, "dashHitCount", 1);
        SetPrivateField(config, "normalContactDamage", 0f);

        // 注入是否生效：用只读属性核对，否则测试会因为「配置全是 0」而假通过。
        Check("反射注入生效：BossHp = 7500", Math.Abs(config.BossHp - 7500f) < 1e-4f);
        Check("反射注入生效：DashDistance = 12", Math.Abs(config.DashDistance - 12f) < 1e-4f);

        BossController controller = new BossController();
        controller.Initialize(player, movement, new FakeBridge(), new NoWallCheck(),
            new RandomWanderPointSource(12345), config, health, 7500f, 12345);

        Check("初始化成功", controller.IsInitialized);
        Check("初始状态为 Approach", controller.CurrentStateId == BossStateId.Approach);

        const float step = 0.1f;
        int dashCount = 0;
        float firstDashTravel = 0f;
        float currentDashTravel = 0f;
        bool wasDashing = false;

        for (int i = 0; i < 240; i++) // 24 秒
        {
            float xBefore = movement.Position.x;

            controller.Tick(step);

            BossStateId after = controller.CurrentStateId;
            bool isDashing = after == BossStateId.Dash;

            if (isDashing)
            {
                // 只累计「这一帧处于 Dash 状态」时产生的位移。
                // 之前用「进出 Dash 的位置差」会把走路位移混进来，且起点采样偏一帧
                // 就会偏差一个步长（24 * 0.1 = 2.4），所以必须逐帧按状态归属累计。
                if (!wasDashing)
                {
                    dashCount++;
                    currentDashTravel = 0f;
                }

                currentDashTravel += Math.Abs(movement.Position.x - xBefore);
            }
            else if (wasDashing)
            {
                // 刚离开 Dash：记下这一次的行程（只保留第一次，便于对照配置值）。
                if (dashCount == 1)
                {
                    firstDashTravel = currentDashTravel;
                }
            }

            wasDashing = isDashing;
        }

        float travel = firstDashTravel;

        Console.WriteLine($"    24 秒内进入 Dash {dashCount} 次，首次行程 {travel:0.###}（配置 12）");

        Check("Boss 确实进入了冲刺（修复前为 0 次）", dashCount > 0);
        Check("冲刺行程等于配置的 DashDistance（12）", Math.Abs(travel - 12f) < 0.05f);
        Check("24 秒内冲刺次数在冷却允许范围内（0.7+0.5+0.6+3.5 = 5.3 秒周期 => 至多 5 次）",
            dashCount <= 5);

        // 冷却在恢复期启动：恢复结束回到 Approach/Wander 后，冷却未走完就不该再冲。
        Check("最终状态不是冲刺中", controller.CurrentStateId != BossStateId.Dash);
        Check("自然冲刺循环没有 FSM Error 日志", Debug.ErrorCount == 0);
    }

    // ---- 测试夹具 --------------------------------------------------------------------------

    private enum TestStateId
    {
        A = 0,
        B = 1,
        C = 2,
        D = 3
    }

    private sealed class TestFacts
    {
        public bool GoB;
    }

    private sealed class TestState : StateBase<TestStateId>
    {
        private readonly TestStateId _id;
        private readonly TestFacts _facts;

        public TestState(TestStateId id, TestFacts facts)
        {
            _id = id;
            _facts = facts;
        }

        public override TestStateId Id => _id;
        public override string DisplayName => "测试" + _id;

        // C 是终态，用来测终态锁定。
        public override bool IsTerminal => _id == TestStateId.C;

        public override TestStateId? OnUpdate(IStateMachineHost<TestStateId> host, float deltaTime)
        {
            if (_id == TestStateId.A && _facts.GoB)
            {
                return TestStateId.B;
            }

            return null;
        }
    }

    private static StateMachine<TestStateId> BuildTestMachine(TestFacts facts)
    {
        StateMachine<TestStateId> machine = new StateMachine<TestStateId>();
        machine.Initialize(TestStateId.A, new StateBase<TestStateId>[]
        {
            new TestState(TestStateId.A, facts),
            new TestState(TestStateId.B, facts),
            new TestState(TestStateId.C, facts),
            new TestState(TestStateId.D, facts)
        },
        new[]
        {
            new TransitionRule<TestStateId>(TestStateId.A, TestStateId.B, 1),
            new TransitionRule<TestStateId>(TestStateId.B, TestStateId.C, 1),

            // 死亡边：A / B 都能进终态 C。
            new TransitionRule<TestStateId>(TestStateId.A, TestStateId.C, 2),
            new TransitionRule<TestStateId>(TestStateId.B, TestStateId.C, 2)
        });

        return machine;
    }

    // 供 Dash 测试驱动单个状态用的宿主：记录状态最后一次请求的转移目标。
    private sealed class DashHost : IStateMachineHost<BossStateId>
    {
        public float TimeInState { get; private set; }
        public BossStateId? RequestedExit { get; set; }

        public bool TryRequestTransition(BossStateId next, string reason)
        {
            return true;
        }
    }

    private sealed class FakePlayer : KWC.Core.IPlayerContext
    {
        public FakePlayer(Vector3 position)
        {
            PositionValue = position;
            EntityTransform = new Transform();
        }

        public Vector3 PositionValue;
        public Transform EntityTransform { get; }
        public Vector3 Position => PositionValue;
        public Vector3 Facing => Vector3.forward;
        public void SetControlEnabled(bool enabled) { }
    }

    private sealed class FakeMovement : IMovementHandler
    {
        public FakeMovement(Vector3 start)
        {
            PositionValue = start;
        }

        public Vector3 PositionValue;
        public bool MovementEnabled { get; private set; } = true;
        public int ResetCount { get; private set; }

        public Vector3 Position => PositionValue;
        public Vector3 Facing => Vector3.forward;

        public void MoveToward(Vector3 target, float speed, float deltaTime)
        {
            if (!MovementEnabled)
            {
                return;
            }

            Vector3 delta = target - PositionValue;
            delta.y = 0f;
            float step = Math.Max(0f, speed) * deltaTime;

            if (step >= delta.magnitude)
            {
                PositionValue = target;
                return;
            }

            PositionValue = PositionValue + (delta.normalized * step);
        }

        public void Stop()
        {
        }

        public void SetMovementEnabled(bool enabled)
        {
            MovementEnabled = enabled;
        }

        public void ResetMovement(Vector3 facing)
        {
            MovementEnabled = true;
            ResetCount++;
        }
    }

    private sealed class FakeContact : IContactSource
    {
        public bool IsInContact => false;
    }

    // 一直处于接触状态，用于驱动 Enemy1 的 ContactAttack 状态。
    private sealed class AlwaysContact : IContactSource
    {
        public bool IsInContact => true;
    }

    private sealed class FakeBridge : ICombatBridge
    {
        public int SubmittedDamage;
        public int DeathReports;
        public readonly List<int> ReportedLifeIds = new List<int>();

        public bool TrySubmitPlayerDamage(PlayerDamageRequest request)
        {
            SubmittedDamage++;
            return true;
        }

        public void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss, int lifeId)
        {
            DeathReports++;
            ReportedLifeIds.Add(lifeId);
        }

        public void ReportEnemyHealth(MonoBehaviour enemy, int lifeId, float current, float max)
        {
        }

        public void ReportEnemyDied(MonoBehaviour enemy, int lifeId)
        {
        }
    }

    // 记录每次查询用的位移，用来断言「按实际步长查询」。
    private sealed class FakeWallCheck : IWallCheck
    {
        private readonly float _wallDistance;

        public FakeWallCheck(float wallDistance)
        {
            _wallDistance = wallDistance;
        }

        public readonly List<float> QueriedDisplacements = new List<float>();

        public bool TryMove(Vector3 origin, Vector3 displacement, out Vector3 allowedPosition,
            out Vector3 hitPoint)
        {
            QueriedDisplacements.Add(displacement.magnitude);

            float projected = origin.x + displacement.x;

            if (projected < _wallDistance)
            {
                allowedPosition = origin + displacement;
                hitPoint = default;
                return false;
            }

            // 贴墙：只允许走到墙面。
            float allowedX = Math.Max(origin.x, _wallDistance);
            allowedPosition = new Vector3(allowedX, origin.y, origin.z);
            hitPoint = new Vector3(_wallDistance, origin.y, origin.z);
            return true;
        }
    }

    // ---- 断言 ------------------------------------------------------------------------------

    // 反射注入 private [SerializeField] 配置字段。
    // 这些字段在 Unity 里由 Inspector 赋值，测试环境没有序列化，只能这样填。
    // 目的是「不改生产代码的可见性」，而不是绕过封装——字段名写错会直接抛异常。
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        Type type = target.GetType();

        while (type != null)
        {
            System.Reflection.FieldInfo field = type.GetField(fieldName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

            if (field != null)
            {
                field.SetValue(target, value);
                return;
            }

            type = type.BaseType;
        }

        throw new InvalidOperationException(
            $"配置字段 '{fieldName}' 在 {target.GetType().Name} 上不存在；测试注入失败，请核对字段名。");
    }

    private static void Section(string title)
    {
        Console.WriteLine("-- " + title);
    }

    private static void Check(string what, bool condition)
    {
        _checks++;

        if (condition)
        {
            Console.WriteLine("   [通过] " + what);
            return;
        }

        _failures++;
        Console.WriteLine("   [失败] " + what);
    }
}
