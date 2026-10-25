using System;
using KWC.Core;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 玩家靠近拾取的入口。物品请求检测，距离满足后交给原组件结算和回收。
    /// 流程层提供同一玩家的位置、属性与生命，并控制拾取许可。
    /// </summary>
    public sealed class PlayerPickupCollector : MonoBehaviour
    {
        private IPlayerContext playerContext;
        private PlayerStats playerStats;
        private Health playerHealth;
        private int lifeId;
        private bool initialized;
        private bool pickupEnabled;

        public bool CanCollect => initialized && isActiveAndEnabled && pickupEnabled &&
            playerHealth != null && playerHealth.IsInitialized &&
            playerHealth.LifeId == lifeId && !playerHealth.IsDead &&
            playerStats != null && playerStats.IsInitialized &&
            playerContext.EntityTransform != null && playerContext.EntityTransform.gameObject.activeInHierarchy;

        /// <summary>
        /// 在玩家Health和PlayerStats初始化后调用。初始化后默认关闭拾取。
        /// </summary>
        public void Initialize(IPlayerContext context, PlayerStats stats, Health health)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (stats == null || !stats.IsInitialized)
                throw new ArgumentException("Initialize the player stats first.", nameof(stats));
            if (health == null || !health.IsInitialized)
                throw new ArgumentException("Initialize the player Health first.", nameof(health));

            playerContext = context;
            playerStats = stats;
            playerHealth = health;
            lifeId = health.LifeId;
            pickupEnabled = false;
            initialized = true;
        }

        // 战斗时开启；暂停、升级、结束本局时关闭。不自行读取GameState或改变时间流速。
        public void SetPickupEnabled(bool enabled)
        {
            pickupEnabled = enabled;
        }

        public bool TryCollect(ExpOrb orb)
        {
            if (!CanCollect || orb == null || !orb.isActiveAndEnabled || !orb.CanCollect)
                return false;
            if (!IsInRange(orb.transform.position, orb.PickupRadius)) return false;

            // 立即结算本次生成的物品；奖励、重复保护和回收仍由ExpOrb负责。
            return orb.TryCollect(playerStats, orb.SpawnId);
        }

        public bool TryCollect(HealthPack pack)
        {
            if (!CanCollect || pack == null || !pack.isActiveAndEnabled || !pack.CanCollect)
                return false;
            if (!IsInRange(pack.transform.position, pack.PickupRadius)) return false;

            return pack.TryCollect(playerHealth, pack.SpawnId);
        }

        private bool IsInRange(Vector3 itemPosition, float radius)
        {
            // 三维直线距离包含高度差，不假定玩家在XY或XZ平面移动。
            return Vector3.Distance(playerContext.Position, itemPosition) <= radius;
        }

        private void OnDisable()
        {
            // 停用表示结束本次绑定；暂停只关许可，恢复后会重新检查当前距离。
            initialized = false;
            pickupEnabled = false;
        }
    }
}
