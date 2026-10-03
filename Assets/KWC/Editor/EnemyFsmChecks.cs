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

            report.AppendLine(failures == 0 ? PassMarker : FailMarker + ": " + failures);

            if (failures == 0)
            {
                Debug.Log(report.ToString());
            }
            else
            {
                Debug.LogError(report.ToString());
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
        private static void TestOneTransitionPerFrameAndHysteresis(StringBuilder report, ref int failures)
        {
            FixtureFacts facts = new FixtureFacts();
            StateMachine<TestStateId> machine = Build(facts);
            machine.MinDwell = 0.05f;

            machine.Update(0.016f);
            facts.WorldFlag = true;

            // 在滞回窗口内请求两次：转移必须被拒绝，而不是悄悄生效。
            machine.Update(0.016f);
            machine.Update(0.016f);
            Check(report, ref failures, "滞回窗口内合法转移被延后", machine.Current == TestStateId.A);

            machine.Update(0.016f);
            machine.Update(0.016f);
            Check(report, ref failures, "滞回窗口过后合法转移最终生效", machine.Current == TestStateId.B);

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
