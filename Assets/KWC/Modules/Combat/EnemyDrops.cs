using System;
using KWC.Core;
using KWC.Data;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 普通敌人的死亡掉落。流程层先让Enemy进入Dead，再调用TryDrop，最后回收敌人。
    /// 本组件不订阅死亡事件，避免用订阅顺序决定掉落和回收的先后。
    /// </summary>
    public sealed class EnemyDrops : MonoBehaviour
    {
        private Health enemyHealth;
        private int lifeId;
        private bool isBoss;
        private bool initialized;
        private bool dropped;
        private IObjectPool expOrbPool;
        private IObjectPool healthPackPool;
        private int dropCountMin;
        private int dropCountMax;
        private int expPerOrb;
        private float healthPackDropRate;
        private float healthPackHealRatio;

        /// <summary>
        /// 在敌人Health初始化后调用，两个池分别对应ExpOrb和HealthPack的Prefab。
        /// 当前只实现普通敌人掉落；Boss传true时不使用这套普通敌人奖励。
        /// </summary>
        public void Initialize(Health health, bool boss, CombatRulesConfig rules,
            IObjectPool orbPool, IObjectPool packPool)
        {
            if (health == null || !health.IsInitialized)
                throw new ArgumentException("Initialize the enemy Health first.", nameof(health));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (!boss && (orbPool == null || packPool == null))
                throw new ArgumentException("Normal enemy drops need both pickup pools.");
            // 同一条命不能重新初始化来清掉已掉落标记；池化复用要先让Health开始新生命。
            if (enemyHealth == health && lifeId == health.LifeId)
                throw new InvalidOperationException("Drops are already initialized for this lifetime.");

            enemyHealth = health;
            lifeId = health.LifeId;
            isBoss = boss;
            expOrbPool = orbPool;
            healthPackPool = packPool;
            // 数值从已有配置复制，本组件不另设默认概率、数量或奖励。
            dropCountMin = rules.DropCountMin;
            dropCountMax = rules.DropCountMax;
            expPerOrb = rules.ExpPerOrb;
            healthPackDropRate = rules.HealthPackDropRate;
            healthPackHealRatio = rules.HealthPackHealRatio;
            dropped = false;
            initialized = true;
        }

        /// <summary>
        /// 对指定生命处理一次普通敌人掉落。已接受返回true，重复、过期或非死亡请求返回false。
        /// 必须在敌人Health.EndLife或对象回收之前调用；波末清理不调用本方法。
        /// </summary>
        public bool TryDrop(int expectedLifeId)
        {
            if (dropped || isBoss || !IsCurrentDeath(enemyHealth, expectedLifeId))
                return false;

            // 先记下已经处理，再申请对象，防止生成过程中的回调重复触发掉落。
            dropped = true;
            Health health = enemyHealth;
            Vector3 position = health.transform.position;
            // int版Range的上限不包含在结果内，+1才能等概率取到配置中的最大数量。
            int count = UnityEngine.Random.Range(dropCountMin, dropCountMax + 1);
            bool dropHealthPack = healthPackDropRate >= 1f ||
                UnityEngine.Random.value < healthPackDropRate;

            // 本轮使用死亡位置，不额外规定随机散落半径或运动表现。
            for (int i = 0; i < count; i++)
            {
                expOrbPool.Rent(position, Quaternion.identity, InitializeExpOrb);

                // Rent会激活掉落物并执行回调；若期间清场或复用了敌人，停止旧生命剩余的掉落。
                if (!IsCurrentDeath(health, expectedLifeId))
                    return true;
            }

            // 血包独立判定，不会替换或减少经验球。
            if (dropHealthPack)
            {
                healthPackPool.Rent(position, Quaternion.identity, InitializeHealthPack);
            }
            return true;
        }

        // 检查这次死亡是否仍有效：没有清场，也没有换成另一对象或下一条命。
        private bool IsCurrentDeath(Health health, int expectedLifeId)
        {
            return initialized && health != null && enemyHealth == health && health.IsDead &&
                lifeId == expectedLifeId && health.LifeId == expectedLifeId;
        }

        // 把方法传给 Rent，池会在启用物品前调用它，为这次生成填入奖励。
        private void InitializeExpOrb(GameObject instance)
        {
            ExpOrb orb = instance.GetComponent<ExpOrb>();
            orb.Initialize(expOrbPool, expPerOrb);
        }

        private void InitializeHealthPack(GameObject instance)
        {
            HealthPack pack = instance.GetComponent<HealthPack>();
            pack.Initialize(healthPackPool, healthPackHealRatio);
        }

        // 非击杀清理只使当前掉落入口失效，不生成奖励。
        private void OnDisable()
        {
            initialized = false;
        }
    }
}
