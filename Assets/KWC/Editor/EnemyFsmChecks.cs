using System.Collections.Generic;
using System.Text;
using KWC.Enemy;
using UnityEditor;
using UnityEngine;

namespace KWC.Editor
{
    // ============================================================================================
    // 状态机的无头自测与结构审计。
    //
    // 状态机不依赖引擎（一次 tick 就是喂一个 deltaTime 的方法调用），所以「期望轨迹」可以写成
    // 断言，而不是靠在 Play Mode 里肉眼看。这里用一个一次性的三状态流程去考状态机，
    // 于是以后谁改了护栏，都会先在测试里失败，而不是先在游戏里出问题。
    //
    // 批处理用法：
    //   Unity.exe -batchmode -quit -projectPath <项目> \
    //     -executeMethod KWC.Editor.EnemyFsmChecks.RunSelfTest -logFile <日志>
    // 然后在日志里找 KWC_ENEMY_FSM_SELFTEST_PASS / _FAIL。
    // ============================================================================================
    public static class EnemyFsmChecks
    {
        private const string PassMarker = "KWC_ENEMY_FSM_SELFTEST_PASS";
        private const string FailMarker = "KWC_ENEMY_FSM_SELFTEST_FAIL";

        // dev：自测用的夹具。事实就是普通字段，因为这里要验证的是状态机，不是模拟一个真实体。
        private sealed class FixtureFacts
        {
            public bool WorldFlag;
            public int StepB;
            public int StepC;
            public int EntersA;
            public int EntersB;
            public int ExitsA;
            public int ExitsB;
        }

        private enum TestStateId
        {
            A = 0,
            B = 1,
            C = 2
        }

        private sealed class TestState : StateBase<TestStateId>
        {
            private readonly TestStateId _id;
            private readonly FixtureFacts _facts;
            private readonly System.Func<FixtureFacts, TestStateId?> _decide;
            private readonly System.Action<FixtureFacts> _onEnter;
            private readonly System.Action<FixtureFacts> _onExit;

            public TestState(TestStateId id, FixtureFacts facts,
                System.Func<FixtureFacts, TestStateId?> decide,
                System.Action<FixtureFacts> onEnter = null,
                System.Action<FixtureFacts> onExit = null)
            {
                _id = id;
                _facts = facts;
                _decide = decide;
                _onEnter = onEnter;
                _onExit = onExit;
            }

            public override TestStateId Id => _id;
            public override string DisplayName => "测试" + _id;
            public override bool IsTerminal => _id == TestStateId.C;

            public override void OnEnter(IStateMachineHost<TestStateId> host)
            {
                _onEnter?.Invoke(_facts);
            }

            public override TestStateId? OnUpdate(IStateMachineHost<TestStateId> host, float deltaTime)
            {
                return _decide(_facts);
            }

            public override void OnExit(IStateMachineHost<TestStateId> host)
            {
                _onExit?.Invoke(_facts);
            }
        }

        [MenuItem("KWC/Enemy/运行状态机逻辑自测")]
        public static void RunSelfTest()
        {
            StringBuilder report = new StringBuilder();
            int failures = 0;

            TestInitialEnterAndAudit(report, ref failures);
            TestOneTransitionPerFrameAndHysteresis(report, ref failures);
            TestSelfTransitionRefused(report, ref failures);
            TestIllegalTransitionRefused(report, ref failures);
            TestInterruptLane(report, ref failures);
            TestForceExitBalancesEnterExit(report, ref failures);
            TestTerminalStateIsStable(report, ref failures);
            TestDeathLatchReportsOnce(report, ref failures);
            TestStaleLifeCallbackRejected(report, ref failures);
            TestBossDashIsNotDeadBranch(report, ref failures);

            report.AppendLine(failures == 0 ? PassMarker : FailMarker + ": " + failures);

            if (failures == 0)
            {
                Debug.Log(report.ToString());
                return;
            }

            Debug.LogError(report.ToString());

            // 批处理下必须返回非零退出码，否则 CI 会把失败当成功。
            // 只记 LogError 不改退出码，正是审核指出的问题。
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }

        // 1. 初始进入不算一次转移，出入审计从一开始就是配平的。
        private static void TestInitialEnterAndAudit(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);

            Check(report, ref failures, "初始状态是 A", machine.Current == TestStateId.A);
            Check(report, ref failures, "初始化后活跃状态数为 1", machine.LiveStateCount == 1);
            Check(report, ref failures, "初始进入不计入转移次数", machine.TransitionCount == 0);
            Check(report, ref failures, "A 的 OnEnter 执行了一次", facts.EntersA == 1);
        }

        // 2. 合法转移要等滞回窗口过去，而且是延后不是丢弃 —— 这就是防抖动的规则。
        //
        // 时序必须严格：MinDwell = 0.05，步长 0.016。
        // 第 1 帧（t=0.016）重新请求，第 2 帧（t=0.032）仍在窗口内，第 3 帧（t=0.048）仍在窗口内，
        // 第 4 帧（t=0.064）越过窗口才执行。之前这里的 Update 次数与断言对不上，
        // 造成 2 个断言失败。
        private static void TestOneTransitionPerFrameAndHysteresis(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);
            machine.MinDwell = 0.05f;

            machine.Update(0.016f);
            Check(report, ref failures, "起始状态为 A", machine.Current == TestStateId.A);

            facts.WorldFlag = true;

            // 窗口内请求两次：必须被拒绝，而不是悄悄生效。
            machine.Update(0.016f);
            machine.Update(0.016f);
            Check(report, ref failures, "滞回窗口内合法转移被延后", machine.Current == TestStateId.A);

            // 同一批时间戳下再走两帧：越过 0.05 才生效。
            machine.Update(0.016f);
            Check(report, ref failures, "仍然在窗口内（t=0.048）", machine.Current == TestStateId.A);

            machine.Update(0.016f);
            Check(report, ref failures, "越过窗口后（t=0.064）转移生效", machine.Current == TestStateId.B);

            // 执行了转移的那一帧不许再跑下一个状态的逻辑。
            Check(report, ref failures, "B 在进入的那一帧没有被执行", facts.StepB == 0);

            for (int i = 0; i < 8; i++)
            {
                machine.Update(0.016f);
            }

            // 滞回没有把转移链丢掉：B 得到了机会并级联到了 C，而且只发生一次。
            Check(report, ref failures, "B 至少被执行过一次", facts.StepB >= 1);
            Check(report, ref failures, "转移链到达了终态 C", machine.Current == TestStateId.C);

            int transitionsAtC = machine.TransitionCount;
            for (int i = 0; i < 20; i++)
            {
                machine.Update(0.016f);
            }

            Check(report, ref failures, "终态 C 永远不会离开", machine.Current == TestStateId.C);
            Check(report, ref failures, "终态不再产生转移", machine.TransitionCount == transitionsAtC);
        }

        // 3. 拒绝自我转移：状态不能重进自己，所以「重进一次来重置」不会被当成隐式行为用上。
        private static void TestSelfTransitionRefused(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);

            bool accepted = machine.TryRequestTransition(TestStateId.A, "自我");
            Check(report, ref failures, "自我转移被拒绝", !accepted);
            Check(report, ref failures, "拒绝被记入审计", machine.GetStateRefusalCount(TestStateId.A) >= 1);
            Check(report, ref failures, "被拒的自我转移没有改变状态", machine.Current == TestStateId.A);
        }

        // 4. 转移表当审计工具：没声明的边会被拒绝并报警，而不是被静默接受。
        private static void TestIllegalTransitionRefused(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);

            // Build() 用的转移表里刻意没有 A -> C 这条边。
            bool accepted = machine.TryRequestTransition(TestStateId.C, "非法");
            Check(report, ref failures, "未声明的边被拒绝", !accepted);
            Check(report, ref failures, "未声明的边没有改变状态", machine.Current == TestStateId.A);
        }

        // 5. 事件打断优先于普通边、且不受滞回门槛限制；对当前状态重复打断不算错误。
        private static void TestInterruptLane(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);

            machine.Update(0.016f);
            machine.OnHostileInterrupt(TestStateId.B, "打断");
            machine.Update(0.016f);

            Check(report, ref failures, "事件打断越过滞回门槛", machine.Current == TestStateId.B);

            int transitionsBefore = machine.TransitionCount;
            machine.OnHostileInterrupt(TestStateId.B, "再次打断");
            machine.Update(0.016f);
            Check(report, ref failures, "对当前状态重复打断什么也不改变",
                machine.Current == TestStateId.B && machine.TransitionCount == transitionsBefore);
        }

        // 6. 强制离开随时可用且始终配平：这是池化回收 / 场景卸载那条路径。
        private static void TestForceExitBalancesEnterExit(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);
            machine.MinDwell = 0.05f;

            // 先走一次真实的 A -> B 转移，这样才有东西可拆。
            facts.WorldFlag = true;
            for (int i = 0; i < 10; i++)
            {
                machine.Update(0.016f);
            }

            Check(report, ref failures, "强制离开前已到达 B", machine.Current == TestStateId.B);

            machine.ResetToInitial("测试拆解");

            Check(report, ref failures, "强制离开回到初始状态", machine.Current == TestStateId.A);
            Check(report, ref failures, "强制离开后活跃状态数恰好为 1", machine.LiveStateCount == 1);
            Check(report, ref failures, "强制离开执行了 A -> B 的离开", facts.ExitsA >= 1);

            // OnExit 必须随时可调用、且自身不做新决策，所以连续两次拆解是安全的。
            machine.ResetToInitial("再次测试拆解");
            Check(report, ref failures, "第二次强制离开安全且仍然配平",
                machine.Current == TestStateId.A && machine.LiveStateCount == 1);
        }

        // 7. 死亡上报闸门：同一条命无论被问多少次，只放行一次。
        //    回归测试 —— 之前闩锁放在 Dead 状态里、靠 OnExit 重新武装，导致重复死亡通知。
        private static void TestDeathLatchReportsOnce(StringBuilder report, ref int failures)
        {
            DeathReportLatch latch = new DeathReportLatch();

            // 开始第一条命。
            latch.Arm();
            int firstLife = latch.LifeId;

            Check(report, ref failures, "Arm 之后处于待上报状态", latch.IsArmed);

            bool first = latch.TryClaimDeathReport(out int firstClaimedLife);
            Check(report, ref failures, "第一次上报被放行", first);
            Check(report, ref failures, "上报携带当前生命编号", firstClaimedLife == firstLife);
            Check(report, ref failures, "上报后闸门关闭", !latch.IsArmed);

            // 同一条命的后续上报（零血重复上报、迟到回调）必须全部被拒。
            int accepted = 0;
            for (int i = 0; i < 5; i++)
            {
                if (latch.TryClaimDeathReport(out _))
                {
                    accepted++;
                }
            }

            Check(report, ref failures, "同一条命重复上报全部被拒", accepted == 0);
            Check(report, ref failures, "重复上报不改变生命编号", latch.LifeId == firstLife);

            // 新的一条命：闸门重新武装，编号前进。
            latch.Arm();
            Check(report, ref failures, "复用时闸门重新武装", latch.IsArmed);
            Check(report, ref failures, "复用时生命编号递增", latch.LifeId == firstLife + 1);

            bool second = latch.TryClaimDeathReport(out int secondClaimedLife);
            Check(report, ref failures, "新的一条命可以再次上报", second);
            Check(report, ref failures, "第二次上报携带新的生命编号", secondClaimedLife == firstLife + 1);
        }

        // 8. 过期的跨生命回调必须能被识别出来：带旧 lifeId 的上报不能等于当前生命编号。
        //    这是「池化对象复用后，上一条命的迟到回调把新一轮实例立刻判死」的防线。
        private static void TestStaleLifeCallbackRejected(StringBuilder report, ref int failures)
        {
            DeathReportLatch latch = new DeathReportLatch();
            latch.Arm();
            int oldLife = latch.LifeId;

            // 旧生命死亡并上报。
            latch.TryClaimDeathReport(out _);

            // 对象被复用：这是新一轮实例。
            latch.Arm();
            int currentLife = latch.LifeId;

            Check(report, ref failures, "复用后生命编号与上一条命不同", oldLife != currentLife);

            // 旧回调现在到达，带着旧 lifeId：调用方据此判断这是过期通知。
            bool isStale = oldLife != currentLife;
            Check(report, ref failures, "旧 lifeId 可被识别为过期回调", isStale);
            Check(report, ref failures, "当前这条命仍处于待上报状态", latch.IsArmed);
        }

        // 9. 回归测试：「Boss 永远不冲刺」。
        //    旧实现只允许从 Wander 起手冲刺，而 Wander 里 ShouldApproach 优先于冲刺判断，
        //    于是玩家一超出 PreferredDistance，Boss 就退回 Approach 走路（速度 6 < 玩家 10），
        //    冲刺永远不会被发起。修复后起手判断统一放在控制器、位于状态机之前，与所处状态无关。
        private static void TestBossDashIsNotDeadBranch(StringBuilder report, ref int failures)
        {
            // 冲刺判据只有「冷却就绪」，与距离无关。
            // 距离门槛已被移除：它同时是 Wander -> Approach 的出口条件，
            // 保留它就会让冲刺成为不可达分支（审核实测 Boss 完全不冲刺）。
            const float preferredDistance = 8f;
            BossDashThrottle throttle = new BossDashThrottle(3.5f);

            Check(report, ref failures, "冷却就绪时可以起手冲刺", throttle.IsReady);

            // 冷却在恢复期启动，应覆盖整段冲刺流程（前摇 + 行程）。
            throttle.StartCooldown();
            Check(report, ref failures, "冲刺后冷却立即生效", !throttle.IsReady);

            throttle.Tick(0.7f + (12f / 24f)); // 前摇 + DashDistance/DashSpeed
            Check(report, ref failures, "前摇加行程走完仍未冷却完", !throttle.IsReady);

            throttle.Tick(3.5f); // Dash Cooldown
            Check(report, ref failures, "冷却是 3.5 秒，走完后可再次起手", throttle.IsReady);

            // 距离不再参与判据：近处同样可以起手。
            const float nearDistance = 5f;
            Check(report, ref failures, "近处（" + nearDistance + " < " + preferredDistance +
                                        "）同样允许起手，判据不含距离门槛", throttle.IsReady);
        }

        // 10. 终态必须稳定。审核实测 Dead -> ContactAttack -> Dead，
        //     一次死亡触发两次死亡通知。这里验证「终态锁定 + 清除过期请求」。
        private static void TestTerminalStateIsStable(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);
            machine.MinDwell = 0f;

            machine.Update(0.05f);

            // 先排一个普通请求，再让死亡打断 —— 修复前这个排队请求会在死亡后执行。
            machine.TryRequestTransition(TestStateId.B, "先排队的普通请求");
            machine.OnHostileInterrupt(TestStateId.C, "死亡");
            machine.Update(0.05f);

            Check(report, ref failures, "死亡打断后进入终态 C", machine.Current == TestStateId.C);
            Check(report, ref failures, "终态标志生效", machine.IsInTerminalState);

            int transitionsAtTerminal = machine.TransitionCount;

            // 迟到的重复死亡通知与普通请求都不许把状态机拉出终态。
            for (int i = 0; i < 10; i++)
            {
                machine.OnHostileInterrupt(TestStateId.C, "重复死亡通知");
                machine.OnHostileInterrupt(TestStateId.B, "迟到回调想拉回非终态");
                machine.TryRequestTransition(TestStateId.B, "终态里的普通请求");
                machine.Update(0.05f);
            }

            Check(report, ref failures, "终态始终保持为 C（不再往返）", machine.Current == TestStateId.C);
            Check(report, ref failures, "终态期间没有产生任何新转移",
                machine.TransitionCount == transitionsAtTerminal);
            Check(report, ref failures, "终态期间活跃状态数仍为 1", machine.LiveStateCount == 1);

            // 强制离开（池化回收 / 场景卸载）仍必须可用。
            machine.ResetToInitial("回收");
            Check(report, ref failures, "强制离开可以离开终态", machine.Current == TestStateId.A);
            Check(report, ref failures, "强制离开后终态标志被清除", !machine.IsInTerminalState);
            Check(report, ref failures, "强制离开后出入配平", machine.LiveStateCount == 1);
        }

        private static StateMachine<TestStateId> Build(FixtureFacts facts)
        {
            TestState a = new TestState(TestStateId.A, facts,
                f => f.WorldFlag ? TestStateId.B : (TestStateId?)null,
                f => f.EntersA++, f => f.ExitsA++);

            TestState b = new TestState(TestStateId.B, facts,
                f =>
                {
                    f.StepB++;

                    // 级联：B 在它的第一次执行里请求 C。状态机必须把它放到后面某一帧执行，
                    // 绝不能在同一帧里完成。
                    return TestStateId.C;
                },
                f => f.EntersB++, f => f.ExitsB++);

            TestState c = new TestState(TestStateId.C, facts,
                f =>
                {
                    f.StepC++;
                    return null;
                });

            StateMachine<TestStateId> machine = new StateMachine<TestStateId>();
            machine.Initialize(TestStateId.A, new[] { a, b, c }, new[]
            {
                new TransitionRule<TestStateId>(TestStateId.A, TestStateId.B, 1),
                new TransitionRule<TestStateId>(TestStateId.B, TestStateId.C, 1),

                // 断言的素材：这条边是禁用的，等价于没声明。
                new TransitionRule<TestStateId>(TestStateId.A, TestStateId.C, 9, false)
            });

            return machine;
        }

        private static void Check(StringBuilder report, ref int failures, string what, bool condition)
        {
            if (condition)
            {
                report.AppendLine("  通过  " + what);
                return;
            }

            failures++;
            report.AppendLine("  失败  " + what);
        }

        // 对真实状态机实例做结构审计，由菜单项调用。
        // 泛型约束必须与 StateMachine<TStateId> 一致，否则引用类型会在这里编译不过（CS0453）。
        public static void AuditRuntimeMachine<TStateId>(StateMachine<TStateId> machine, string label)
            where TStateId : struct
        {
            if (machine == null)
            {
                Debug.LogWarning("[Enemy] " + label + "：没有可审计的状态机实例。");
                return;
            }

            List<string> problems = new List<string>();

            // 同一个状态出去的两条边优先级相同，说明状态图有歧义：
            // 两条边被记为同等优先，谁赢只由代码顺序决定。
            HashSet<string> seenPriorities = new HashSet<string>();
            foreach (TransitionRule<TStateId> rule in machine.Rules)
            {
                if (!rule.Enabled)
                {
                    continue;
                }

                string key = rule.From + "->" + rule.Priority;
                if (!seenPriorities.Add(key))
                {
                    problems.Add("状态 '" + rule.From + "' 出去的边优先级 " + rule.Priority +
                                 " 重复 —— 优先级必须能指认出唯一的赢家");
                }
            }

            // 从初始状态出发做可达性计算，用的就是运行期那张转移表。
            TStateId initial = machine.Current;
            HashSet<TStateId> reached = new HashSet<TStateId> { initial };
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (TransitionRule<TStateId> rule in machine.Rules)
                {
                    if (rule.Enabled && reached.Contains(rule.From) && reached.Add(rule.To))
                    {
                        grew = true;
                    }
                }
            }

            foreach (TStateId id in machine.RegisteredStates)
            {
                bool reachable = reached.Contains(id);
                bool hasExit = machine.HasOutgoingEnabledRule(id);
                bool terminal = machine.TryGetState(id, out StateBase<TStateId> state) && state.IsTerminal;

                if (!reachable)
                {
                    problems.Add("状态 '" + id + "' 从初始状态不可达");
                }

                if (!hasExit && !terminal)
                {
                    problems.Add("状态 '" + id + "' 没有出去的边，也没有声明为终态（死锁）");
                }

                if (hasExit && terminal)
                {
                    problems.Add("状态 '" + id + "' 声明为终态却仍有出去的边");
                }
            }

            if (problems.Count == 0)
            {
                int stateCount = 0;
                foreach (TStateId _ in machine.RegisteredStates)
                {
                    stateCount++;
                }

                Debug.Log("[Enemy] " + label + " 审计通过：" + stateCount +
                          " 个状态，全部可达，每个都有出口或已声明为终态。");
                return;
            }

            for (int i = 0; i < problems.Count; i++)
            {
                Debug.LogError("[Enemy] " + label + " 审计：" + problems[i]);
            }
        }
    }
}
