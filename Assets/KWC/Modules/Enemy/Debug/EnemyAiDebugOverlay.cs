using System.Collections.Generic;
using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 开发期运行时可视化。
    //
    // 存在的理由：「某个状态从没进入过」说明有一条边写错了，「某状态停留时长异常短」说明在抖动。
    // 这两个信号比任何日志都直观，而且几乎不花成本。
    //
    // 这是开发面板，不是正式 UI —— 正式 HUD 归 UI Owner。
    // ============================================================================================
    [DisallowMultipleComponent]
    public sealed class EnemyAiDebugOverlay : MonoBehaviour
    {
        [SerializeField] private Enemy1Controller enemy1;
        [SerializeField] private BossController boss;
        [SerializeField] private bool show;
        [SerializeField] private bool showTrace;
        [SerializeField] private int traceLines = 6;

        private GUIStyle _style;
        private readonly List<string> _lines = new List<string>();

        public bool Show
        {
            get => show;
            set => show = value;
        }

        // dev：目前手动在 prefab 上连线；最终的组合步骤归 Game System。
        public void Bind(Enemy1Controller enemy1Controller, BossController bossController)
        {
            enemy1 = enemy1Controller;
            boss = bossController;
        }

        private void OnGUI()
        {
            if (!show)
            {
                return;
            }

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    richText = false
                };
            }

            _lines.Clear();

            if (enemy1 != null && enemy1.IsInitialized)
            {
                AppendEnemy1(enemy1);
            }

            if (boss != null && boss.IsInitialized)
            {
                AppendBoss(boss);
            }

            if (_lines.Count == 0)
            {
                _lines.Add("没有已初始化并连线的敌人控制器");
            }

            GUILayout.BeginArea(new Rect(8f, 8f, 520f, 22f * (_lines.Count + 1)));
            GUILayout.BeginVertical(GUI.skin.box);

            for (int i = 0; i < _lines.Count; i++)
            {
                GUILayout.Label(_lines[i], _style);
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        private void AppendEnemy1(Enemy1Controller controller)
        {
            StateMachine<Enemy1StateId> machine = controller.Machine;
            string stateName = machine.TryGetDisplayName(machine.Current, out string name)
                ? name
                : machine.Current.ToString();

            _lines.Add("Enemy1  " + stateName +
                       "  停留 " + controller.TimeInState.ToString("0.00") + "s" +
                       "  转移 " + controller.TransitionCount);

            // 活跃状态数在运行时必须是 1。其他值就是状态泄漏或幽灵状态。
            _lines.Add("  审计  活跃状态=" + controller.LiveStateCount +
                       "  决策间隔=" + controller.DecisionInterval.ToString("0.00") + "s" +
                       "  可攻击=" + (controller.AttackThrottle != null && controller.AttackThrottle.IsReady));

            if (showTrace && machine.Trace.Count > 0)
            {
                AppendTrace(machine.Trace, "  ");
            }
        }

        private void AppendBoss(BossController controller)
        {
            StateMachine<BossStateId> machine = controller.Machine;
            string stateName = machine.TryGetDisplayName(machine.Current, out string name)
                ? name
                : machine.Current.ToString();

            _lines.Add("Boss  " + stateName +
                       "  停留 " + controller.TimeInState.ToString("0.00") + "s" +
                       "  转移 " + controller.TransitionCount);

            _lines.Add("  审计  活跃状态=" + controller.LiveStateCount +
                       "  可冲刺=" + (controller.DashThrottle != null && controller.DashThrottle.IsReady) +
                       "  冷却=" + (controller.DashThrottle != null
                           ? controller.DashThrottle.TimeUntilReady.ToString("0.0") + "s"
                           : "无"));

            // 两个还没接线的能力，直接显示出来而不是记在心里。
            _lines.Add("  未接线  撞墙检测=" + (controller.IsWallCheckStub ? "占位实现" : "已接线") +
                       "  冲刺快照=" + (controller.DashContext != null && controller.DashContext.HasSnapshot));

            if (showTrace && machine.Trace.Count > 0)
            {
                AppendTrace(machine.Trace, "  ");
            }
        }

        private void AppendTrace(IReadOnlyList<string> trace, string indent)
        {
            int count = Mathf.Clamp(traceLines, 1, 32);
            int start = Mathf.Max(0, trace.Count - count);

            for (int i = start; i < trace.Count; i++)
            {
                _lines.Add(indent + trace[i]);
            }
        }
    }
}
