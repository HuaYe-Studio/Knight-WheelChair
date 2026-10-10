using UnityEngine;

namespace KWC.Enemy
{
    // --------------------------------------------------------------------------------------------
    // 开发期占位实现。
    //
    // 文件名必须与类名一致，否则 Prefab 保存后重导入会显示 Missing Script。
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
        private int _incomingDeathCount;
        private bool _warned;

        // 调试面板用。三者分开，因为「提交了」「上报了」「收到了」不是一回事。
        public int SubmittedDamageCount => _submittedDamageCount;
        public int ReportedDeathCount => _reportedDeathCount;
        public int IncomingDeathCount => _incomingDeathCount;
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

        public void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss, int lifeId)
        {
            _reportedDeathCount++;
            Debug.Log("[Enemy] 死亡上报（占位）isBoss=" + isBoss + " lifeId=" + lifeId +
                      " 对象=" + (enemy != null ? enemy.name : "<null>"));
        }

        public void ReportEnemyHealth(MonoBehaviour enemy, int lifeId, float current, float max)
        {
            // 占位：真实实现应把 current/max 转发给目标 Enemy1Controller.OnHealthReported
            // 或 BossController.OnHealthReported。这里只记录，避免伪造血量。
            Debug.Log("[Enemy] 收到 HP 同步（占位）enemy=" +
                      (enemy != null ? enemy.name : "<null>") + " lifeId=" + lifeId +
                      " current=" + current + " max=" + max);
        }

        public void ReportEnemyDied(MonoBehaviour enemy, int lifeId)
        {
            _incomingDeathCount++;
            Debug.Log("[Enemy] 收到死亡通知（占位）enemy=" +
                      (enemy != null ? enemy.name : "<null>") + " lifeId=" + lifeId +
                      "。真实实现应转发给目标控制器并校验 lifeId。");
        }
    }
}
