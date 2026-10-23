using System;
using KWC.Core;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 血包的消费与回收。按拾取时玩家最大HP的配置比例治疗，满血也能消费。
    /// 绑定PlayerPickupCollector后自动检测靠近；不自然消失，波末由GameSystem归还对象池。
    /// </summary>
    public sealed class HealthPack : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float pickupRadius = 1f;
        private PlayerPickupCollector collector;
        private IObjectPool pool;
        private float healRatio;
        private bool available;
        private int spawnId;

        public int SpawnId => spawnId;
        public bool CanCollect => available;
        public float PickupRadius => pickupRadius;

        public void Initialize(IObjectPool ownerPool, float healthRatio, PlayerPickupCollector pickupCollector = null)
        {
            if (ownerPool == null) throw new ArgumentNullException(nameof(ownerPool));
            if (!CombatMath.IsFinite(healthRatio) || healthRatio <= 0f || healthRatio > 1f)
                throw new ArgumentOutOfRangeException(nameof(healthRatio));
            pool = ownerPool;
            healRatio = healthRatio;
            collector = pickupCollector;
            spawnId = checked(spawnId + 1);
            available = true;
        }

        private void Update()
        {
            if (collector != null) collector.TryCollect(this);
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
            collector = null;
        }
    }
}
