using System;
using KWC.Core;
using KWC.Data;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 玩家初始属性的只读视图，实现 Core 的 IPlayerStats，供其他模块读取。
    /// 当前阶段只建立初始状态，经验结算、升级和属性购买尚未接入。
    /// </summary>
    public sealed class PlayerStats : MonoBehaviour, IPlayerStats
    {
        // HP 由 Health 唯一维护，这里只持有引用，避免保存两份会失去同步的血量。
        private Health health;
        // 记录初始化时绑定的生命，防止对象复用后把旧属性与新生命混用。
        private int healthLifeId;
        private bool initialized;
        // 从配置复制的运行时属性快照，初始化后不持续读取或改写共享的配置资产。
        private float playerAttack;
        private float playerMovement;
        private float attackSpeed;
        private int exp;
        private int playerLevel;
        private int growthPoints;

        // 本组件和 Health 都有效，且仍属于同一条命，才能对外提供属性。
        public bool IsInitialized => initialized && health != null &&
            health.IsInitialized && health.LifeId == healthLifeId;

        // 每次读取 HP 都取 Health 的最新值；其余属性读取本次初始化保存的值。
        public float CurrentHp { get { EnsureInitialized(); return health.CurrentHp; } }
        public float MaxHp { get { EnsureInitialized(); return health.MaxHp; } }
        public float PlayerAttack { get { EnsureInitialized(); return playerAttack; } }
        public float PlayerMovement { get { EnsureInitialized(); return playerMovement; } }
        public float AttackSpeed { get { EnsureInitialized(); return attackSpeed; } }
        public int Exp { get { EnsureInitialized(); return exp; } }
        public int PlayerLevel { get { EnsureInitialized(); return playerLevel; } }
        public int GrowthPoints { get { EnsureInitialized(); return growthPoints; } }

        /// <summary>
        /// 在玩家 Health 初始化后建立属性视图，属性购买次数从零开始。
        /// 初始等级、经验、成长点尚无已确认的默认规则，因此要求调用方显式传入。
        /// 当前不根据初始经验自动升级，也不支持从已购买属性的存档恢复。
        /// </summary>
        public void Initialize(PlayerBaseConfig config, Health playerHealth,
            int initialLevel, int initialExp, int initialGrowthPoints)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (playerHealth == null || !playerHealth.IsInitialized)
                throw new ArgumentException("Initialize the player Health first.", nameof(playerHealth));
            if (initialLevel < 1) throw new ArgumentOutOfRangeException(nameof(initialLevel));
            if (initialExp < 0) throw new ArgumentOutOfRangeException(nameof(initialExp));
            if (initialGrowthPoints < 0) throw new ArgumentOutOfRangeException(nameof(initialGrowthPoints));
            // 接线和初始进度通过校验后再写入状态；配置值由配置方保证有效。
            health = playerHealth;
            healthLifeId = playerHealth.LifeId;
            playerAttack = config.PlayerAttack;
            playerMovement = config.PlayerMovement;
            attackSpeed = config.AttackSpeed;
            playerLevel = initialLevel;
            exp = initialExp;
            growthPoints = initialGrowthPoints;
            initialized = true;
        }

        /// <summary>
        /// 查询单项属性的提升次数，与 PlayerLevel 玩家等级不同。
        /// 当前阶段尚未实现属性购买，因此四项合法属性都为零阶。
        /// </summary>
        public int GetAttributeRank(AttributeType attribute)
        {
            EnsureInitialized();
            switch (attribute)
            {
                case AttributeType.Hp:
                case AttributeType.Attack:
                case AttributeType.Movement:
                case AttributeType.AttackSpeed:
                    return 0; // 四项属性目前均使用配置中的基础值。
                default:
                    throw new ArgumentOutOfRangeException(nameof(attribute));
            }
        }

        // 错误接线或过期访问直接报错，避免向 UI 等模块静默提供无效的默认数值。
        private void EnsureInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Initialize PlayerStats for the current player lifetime first.");
        }

        // 停用后使视图失效；再次启用时须为当前玩家生命重新初始化。
        private void OnDisable()
        {
            initialized = false;
        }
    }
}
