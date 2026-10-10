using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 与 Combat 的契约（跨模块，须主程 Review）。
    // 放在单独文件，因为它是公共接口，不是实现细节。
    // ============================================================================================

    // 攻击种类。FSM 必须自己区分，因为表 N04 规定 Normal Contact Damage = 0、
    // Dash Hit Count = 1：冲刺可以打一次，普通接触一次都不许打。
    public enum AttackKind
    {
        Contact = 0,
        Dash = 1
    }

    // 待Owner与主程联调时确认 —— 这个结构体就是 Architecture 第 3 节明确留白的「命中载荷」。
    // 现在声明出来只是为了让 FSM 有东西可写，字段列表是提案，不是已定契约。
    //
    // 待确认清单（Combat Owner 葛亮亮 + 主程 王艺子杨）：
    //  1. 伤害请求是普通方法调用，还是必须携带命中点 / 方向？
    //  2. 目标怎么识别：直接传 Health 组件、接口、还是 id？
    //  3. Combat 怎么识别攻击方阵营（阵营识别）？
    //  4. 玩家 0.5 秒无敌由谁负责：Combat 还是请求方？
    //  5. Combat 返回结果（生效 / 被忽略 / 无敌中）还是只返回 bool？
    public readonly struct PlayerDamageRequest
    {
        public readonly MonoBehaviour Attacker;
        public readonly float Damage;
        public readonly Vector3 HitPoint;
        public readonly AttackKind Kind;

        public PlayerDamageRequest(MonoBehaviour attacker, float damage, Vector3 hitPoint, AttackKind kind)
        {
            Attacker = attacker;
            Damage = damage;
            HitPoint = hitPoint;
            Kind = kind;
        }
    }

    public interface ICombatBridge
    {
        // ---- 出向：Enemy -> Combat --------------------------------------------------------

        // 返回 true 只表示「请求已经交出去了」。真正的伤害结果归 Combat，
        // 所以返回 false 的含义是「没送达」，不是「没造成伤害」。
        bool TrySubmitPlayerDamage(PlayerDamageRequest request);

        // Enemy 上报自己的死亡，让流程去掉落和回收。
        //
        // 每条命只会调用一次（由 DeathReportLatch 保证，不依赖任何 OnExit 重新武装）。
        // lifeId 让流程识别迟到的跨生命回调：池化对象复用后 lifeId 会变，
        // 旧回调带着旧 lifeId 到达时应当被拒绝，否则会给新一轮实例重复掉落。
        void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss, int lifeId);

        // ---- 入向：Combat -> Enemy --------------------------------------------------------

        // 同步 HP 事实。只更新血量，**不推断死亡**。
        void ReportEnemyHealth(MonoBehaviour enemy, int lifeId, float current, float max);

        // Combat Health 在某个敌人的血量归零时调用，是这个敌人进入 Dead 的唯一入口。
        //
        // 必须显式通知，不能由本模块用「血量 <= 0」去推断：推断会重复触发，
        // 而事件带 lifeId 可以判重。lifeId 从 Enemy 侧的 LifeId 属性取得。
        // 对不上当前生命的通知会被拒绝（那是上一条命的迟到回调）。
        void ReportEnemyDied(MonoBehaviour enemy, int lifeId);
    }
}
