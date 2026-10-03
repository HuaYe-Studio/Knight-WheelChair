using UnityEngine;

namespace KWC.Enemy
{
    // ============================================================================================
    // 与 Combat 的唯一通道。Enemy 不写 HP、不读 Combat 内部，只提交请求 + 上报自己的死亡。
    // 放在单独文件，因为它是跨模块契约，必须由主程 Review（Architecture 第 14 节）。
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
        // 返回 true 只表示「请求已经交出去了」。真正的伤害结果归 Combat，
        // 所以这里返回 false 的含义是「没送达」，不是「没造成伤害」。
        bool TrySubmitPlayerDamage(PlayerDamageRequest request);

        // Enemy 上报自己的死亡，让流程去掉落和回收。
        // 要求：每条命只上报一次（Architecture 第 5 节「一次性死亡通知」）。
        void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss);
    }

    // --------------------------------------------------------------------------------------------
    // 开发期占位实现。
    //
    // 存在的意义：在伤害契约谈定之前，FSM 就能写完、能进 Play Mode、能 Review。
    // 它绝不伪造伤害、也绝不编造伤害数字，只记录「发生过一次请求」并上报一次，
    // 这样接线错误会表现为一条警告，而不是「玩家莫名其妙没掉血」。
    //
    // 契约谈定后换成真实实现即可，Enemy 模块其他任何文件都不用改。
    // --------------------------------------------------------------------------------------------
    public sealed class LogOnlyCombatBridge : MonoBehaviour, ICombatBridge
    {
        private int _submittedDamageCount;
        private int _reportedDeathCount;
        private bool _warned;

        // 调试面板用：请求次数与死亡上报次数。两者分开，因为「提交了」和「打中了」不是一回事。
        public int SubmittedDamageCount => _submittedDamageCount;
        public int ReportedDeathCount => _reportedDeathCount;
        public bool IsStub => true;

        public bool TrySubmitPlayerDamage(PlayerDamageRequest request)
        {
            _submittedDamageCount++;

            if (!_warned)
            {
                _warned = true;
                Debug.LogWarning("[Enemy] ICombatBridge 目前是「只记日志」的占位实现：伤害请求没有送达 Combat。" +
                                 "签名仍为 待Owner与主程联调时确认。");
            }

            Debug.Log("[Enemy] 伤害请求（未送达）种类=" + request.Kind +
                      " 伤害=" + request.Damage +
                      " 来源=" + (request.Attacker != null ? request.Attacker.name : "<null>") +
                      " 位置=" + request.HitPoint);

            // 故意返回 true：请求被本桥接处理了。它不表示玩家掉血。
            // FSM 的防抖靠自己的节流层，不靠这个返回值。
            return true;
        }

        public void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss)
        {
            _reportedDeathCount++;
            Debug.Log("[Enemy] 死亡上报（占位）isBoss=" + isBoss +
                      " 对象=" + (enemy != null ? enemy.name : "<null>"));
        }
    }
}
