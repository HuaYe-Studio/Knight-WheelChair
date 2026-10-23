using System;
using KWC.Core;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 血包的消费与回收。按拾取时玩家最大HP的配置比例治疗，满血也能消费。
    /// 不自然消失，波末由GameSystem归还对象池；拾取范围由后续接线确定。
    /// </summary>
    public sealed class HealthPack : MonoBehaviour
    {
        private IObjectPool pool;
        private float healRatio;
        private bool available;
        private int spawnId;

        public int SpawnId => spawnId;
        public bool CanCollect => available;

        public void Initialize(IObjectPool ownerPool, float healthRatio)
        {
            if (ownerPool == null) throw new ArgumentNullException(nameof(ownerPool));
            if (!CombatMath.IsFinite(healthRatio) || healthRatio <= 0f || healthRatio > 1f)
                throw new ArgumentOutOfRangeException(nameof(healthRatio));
            pool = ownerPool;
            healRatio = healthRatio;
            spawnId = checked(spawnId + 1);
            available = true;
        }

        /// <summary>
        /// 对玩家Health结算一次治疗并回收血包。expectedSpawnId是检测拾取时的血包编号。
        /// true表示血包已消费；满血时即使HP没变化也返回true，死亡时拒绝拾取。
        /// </summary>
        public bool TryCollect(Health playerHealth, int expectedSpawnId)
        {
            if (!available || spawnId != expectedSpawnId || playerHealth == null ||
                !playerHealth.IsInitialized || playerHealth.IsDead)
                return false;

            available = false;
            // TryHeal负责血量上限；返回false也可能只是满血，不能因此保留血包。
            playerHealth.TryHeal(playerHealth.MaxHp * healRatio, playerHealth.LifeId);
            if (spawnId == expectedSpawnId)
                pool.Return(gameObject);
            return true;
        }

        private void OnDisable()
        {
            available = false;
        }
    }
}
