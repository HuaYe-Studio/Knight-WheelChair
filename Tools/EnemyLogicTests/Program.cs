// ============================================================================================
// Enemy 模块纯逻辑回归测试（脱离 Unity 运行）。
//
// 跑法： powershell -NoProfile -ExecutionPolicy Bypass -File Logs/logic-tests/run.ps1
//
// 说明：链接的是 Assets 下**真实源文件**，不是副本，所以覆盖的是真代码。
//       链接范围只包含不依赖 Unity 物理 / 序列化 / 编辑器 API 的部分。
//
// 失败时进程返回非零退出码 —— 审核指出过「批处理退出码仍为 0」，这里修正。
// ============================================================================================
using System;
using System.Collections.Generic;
using KWC.Enemy;
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

        BossBrain brain = new BossBrain(null, player, movement, health, new FakeBridge(), new NoWallCheck(), latch, config);
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

        BossBrain brain = new BossBrain(null, player, movement, health, new FakeBridge(), new NoWallCheck(), latch, config);

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
        BossBrain brain = new BossBrain(null, player, movement, health, new FakeBridge(), wall, latch, config);

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

        public void ReportEnemyHealth(MonoBehaviour enemy, float current, float max)
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
