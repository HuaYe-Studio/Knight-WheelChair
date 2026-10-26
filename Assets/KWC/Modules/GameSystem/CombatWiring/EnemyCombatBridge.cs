using KWC.Enemy;
using KWC.Combat;
using UnityEngine;

namespace KWC.GameSystem
{
    /// <summary>
    /// 挂在 Enemy1 Prefab 上的 ICombatBridge（战斗桥接接口）实现：
    /// 把 Combat Health 的变化/死亡转成 Enemy 状态机的通知，把状态机的伤害/死亡上报转给流程层。
    /// 每个实例只服务自己的 Enemy1Controller；每次从池中取出时重新 Initialize。
    /// </summary>
    public class EnemyCombatBridge : MonoBehaviour,ICombatBridge
    {
        private Enemy1Controller E1Controller;
        private Health E1Health;
        private PlayerInitializer playerInitializer;
        private EnemyLifeCycle enemyLifeCycle;

        /// <summary>
        /// 由 EnemyLifeCycle 在每次租出敌人时调用：保存本实例的控制器、Health 与流程引用。
        /// 任一引用为空时直接拒绝(报错由上游负责)；先解绑再绑定 Health 事件，避免池化复用后重复订阅。
        /// </summary>
        public void Initialize(Enemy1Controller  controller,Health health,PlayerInitializer player,EnemyLifeCycle enemy)
        {
            if(controller == null||health==null||player==null||enemy==null)
                return;

            E1Controller = controller;
            E1Health = health;
            playerInitializer = player;
            enemyLifeCycle = enemy;

            E1Health.HealthChanged -= HandleHealthChanged;
            E1Health.HealthChanged += HandleHealthChanged;

            E1Health.Died -= HandleDied;
            E1Health.Died += HandleDied;
        }

        private void OnDisable()
        {
            if (E1Health == null)
                return;

            E1Health.HealthChanged -= HandleHealthChanged;
            E1Health.Died -= HandleDied;
        }

        private void HandleHealthChanged(int lifeId, float currentHP, float maxHP)
        {
            ReportEnemyHealth(E1Controller,lifeId, currentHP, maxHP);
        }

        private void HandleDied(int lifeId)
        {
            ReportEnemyDied(E1Controller,lifeId);
        }

        /// <summary>
        /// Combat → Enemy 的 HP 事实同步。只同步数值，不推断死亡；
        /// lifeId 与当前生命不符时由控制器拒绝。
        /// </summary>
        public void ReportEnemyHealth(MonoBehaviour enemy, int lifeId, float currentHealth, float maxHealth)
        {
            if(E1Controller == null)
                return;

            if(E1Controller == enemy)
                E1Controller.OnHealthReported(lifeId, currentHealth, maxHealth);
        }

        /// <summary>
        /// Combat → Enemy 的死亡唯一入口，触发状态机进入 Dead。
        /// lifeId 不符的迟到通知会被控制器拒绝。
        /// </summary>
        public void ReportEnemyDied(MonoBehaviour enemy,int lifeId)
        {
            if(E1Controller == enemy && E1Controller != null)
                E1Controller.ReportEnemyDied(lifeId);
        }

        /// <summary>
        /// Enemy → 流程的伤害请求。只接受本实例的控制器。
        /// 返回值只表示请求已送达玩家承伤入口，不表示实际扣血：
        /// 玩家无敌/未开放时仍返回 true，攻击节流照常计一次，避免逐决策帧重试。
        /// </summary>
        public bool TrySubmitPlayerDamage(PlayerDamageRequest request)
        {
            if (request.Attacker != E1Controller)
                return false;

            if(playerInitializer == null)
                return false;

            playerInitializer.TryReceiveDamage(request);
            return true;
        }

        /// <summary>
        /// Enemy → 流程的死亡上报。死亡闸门保证每条命最多一次；带 lifeId 供回收时判重。
        /// Boss 分支当前只记录日志，尚未接入正式结算。
        /// </summary>
        public void NotifyEnemyDied(MonoBehaviour enemy, bool isBoss, int lifeId)
        {
            if(E1Controller != enemy)
                return;

            if (isBoss)
            {
                Debug.Log("Boss未实现");
                return;
            }

            enemyLifeCycle.EnqueueDeath(E1Controller.gameObject, lifeId);
        }
    }
}
