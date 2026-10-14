using System;
using KWC.Core;
using KWC.Data;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 维护玩家属性与成长点，通过 Core 的 IPlayerStats 向其他模块提供只读状态。
    /// 已支持经验升级和攻击、移动、攻速购买；HP 购买等待规则确认。
    /// </summary>
    public sealed class PlayerStats : MonoBehaviour, IPlayerStats
    {
        // HP 由 Health 唯一维护，这里只持有引用，避免保存两份会失去同步的血量。
        private Health health;
        // 记录初始化时绑定的生命，防止对象复用后把旧属性与新生命混用。
        private int healthLifeId;
        private bool initialized;
        // 玩家当前的属性与进度。
        private float playerAttack;
        private float playerMovement;
        private float attackSpeed;
        private int exp;
        private int playerLevel;
        private int growthPoints;
        // 本局的经验门槛系数与每级奖励，从战斗规则配置复制。
        private float expA;
        private float expB;
        private float expC;
        private int growthPointsPerLevel;

        // 保存基础值，用总值公式重算购买结果，避免反复累加浮点增量产生误差。
        private float baseAttack;
        private float baseMovement;
        private float baseAttackSpeed;
        private AttributeGrowth attackGrowth;
        private AttributeGrowth movementGrowth;
        private AttributeGrowth attackSpeedGrowth;
        private int attackRank;
        private int movementRank;
        private int attackSpeedRank;
        private int attributeUpgradeCost;
        private bool purchasingEnabled;

        // 本组件和 Health 都有效，且仍属于同一条命，才能对外提供属性。
        public bool IsInitialized => initialized && health != null &&
            health.IsInitialized && health.LifeId == healthLifeId;

        // 购买阶段由流程层开关控制，死亡或生命失效后自动拒绝购买。
        public bool CanPurchaseAttributes => IsInitialized && !health.IsDead && purchasingEnabled;

        /// <summary>
        /// 成功购买或增加经验后通知状态已更新，监听者通过 IPlayerStats 重新读取状态。
        /// 这是同步通知，所有数值在通知前已写入；失败和初始化不发送通知。
        /// </summary>
        public event Action StatsChanged;

        /// <summary>
        /// 一次经验结算完成后通知一次，UI重新读取Exp和ExpToNextLevel刷新经验条。
        /// </summary>
        public event Action ExperienceChanged;

        /// <summary>
        /// 本次经验结算发生升级时通知一次，参数为最终等级。
        /// 连升多级也只通知一次，成长点已按实际升级次数全部发放。
        /// </summary>
        public event Action<int> LevelUp;

        // HP 读取 Health 的最新值，其余属性读取本组件维护的运行时状态。
        public float CurrentHp { get { EnsureInitialized(); return health.CurrentHp; } }
        public float MaxHp { get { EnsureInitialized(); return health.MaxHp; } }
        public float PlayerAttack { get { EnsureInitialized(); return playerAttack; } }
        public float PlayerMovement { get { EnsureInitialized(); return playerMovement; } }
        public float AttackSpeed { get { EnsureInitialized(); return attackSpeed; } }
        public int Exp { get { EnsureInitialized(); return exp; } }
        // 当前级升到下一级的完整门槛；例如Exp=5、门槛=40，经验条显示5/40。
        public int ExpToNextLevel
        {
            get
            {
                EnsureInitialized();
                return CombatMath.GetRequiredExp(checked(playerLevel + 1), expA, expB, expC);
            }
        }
        public int PlayerLevel { get { EnsureInitialized(); return playerLevel; } }
        public int GrowthPoints { get { EnsureInitialized(); return growthPoints; } }

        /// <summary>
        /// 在玩家 Health 初始化后建立属性视图，属性购买次数从零开始。
        /// 初始等级、经验、成长点尚无已确认的默认规则，因此要求调用方显式传入。
        /// initialExp是当前级内的经验，必须低于下一级门槛；初始化不发奖励或事件。
        /// 当前不支持从已购买属性的存档恢复。
        /// </summary>
        public void Initialize(PlayerBaseConfig config, Health playerHealth,
            AttributeGrowthConfig growthConfig, CombatRulesConfig rulesConfig,
            int initialLevel, int initialExp, int initialGrowthPoints)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (growthConfig == null) throw new ArgumentNullException(nameof(growthConfig));
            if (rulesConfig == null) throw new ArgumentNullException(nameof(rulesConfig));
            if (playerHealth == null || !playerHealth.IsInitialized)
                throw new ArgumentException("Initialize the player Health first.", nameof(playerHealth));
            if (initialLevel < 1) throw new ArgumentOutOfRangeException(nameof(initialLevel));
            if (initialExp < 0) throw new ArgumentOutOfRangeException(nameof(initialExp));
            if (initialGrowthPoints < 0) throw new ArgumentOutOfRangeException(nameof(initialGrowthPoints));
            if (rulesConfig.AttributeUpgradeCost <= 0)
                throw new ArgumentOutOfRangeException(nameof(rulesConfig), "Attribute upgrade cost must be positive.");
            if (rulesConfig.GrowthPointsPerLevel <= 0)
                throw new ArgumentOutOfRangeException(nameof(rulesConfig), "Growth points per level must be positive.");
            int initialThreshold = CombatMath.GetRequiredExp(checked(initialLevel + 1),
                rulesConfig.ExpA, rulesConfig.ExpB, rulesConfig.ExpC);
            if (initialExp >= initialThreshold)
                throw new ArgumentOutOfRangeException(nameof(initialExp), "Initial EXP must be below the next level threshold.");
            // 接线和初始进度通过校验后再写入状态；配置值由配置方保证有效。
            health = playerHealth;
            healthLifeId = playerHealth.LifeId;
            // 从配置复制本局数据，后续购买只改运行时状态，不持续读取或改写共享配置资产。
            playerAttack = config.PlayerAttack;
            playerMovement = config.PlayerMovement;
            attackSpeed = config.AttackSpeed;
            baseAttack = playerAttack;
            baseMovement = playerMovement;
            baseAttackSpeed = attackSpeed;
            attackGrowth = growthConfig.GetGrowth(AttributeType.Attack);
            movementGrowth = growthConfig.GetGrowth(AttributeType.Movement);
            attackSpeedGrowth = growthConfig.GetGrowth(AttributeType.AttackSpeed);
            attributeUpgradeCost = rulesConfig.AttributeUpgradeCost;
            attackRank = 0;
            movementRank = 0;
            attackSpeedRank = 0;
            purchasingEnabled = false;
            playerLevel = initialLevel;
            exp = initialExp;
            growthPoints = initialGrowthPoints;
            expA = rulesConfig.ExpA;
            expB = rulesConfig.ExpB;
            expC = rulesConfig.ExpC;
            growthPointsPerLevel = rulesConfig.GrowthPointsPerLevel;
            initialized = true;
        }

        /// <summary>
        /// 增加经验，保留超额经验并允许连续升级；每级按配置发放成长点。
        /// 正数经验成功结算返回true；未初始化、生命已失效、死亡或非正数请求返回false。
        /// 拾取逻辑负责一次拾取只调用一次，本方法不判断距离或销毁经验球。
        /// </summary>
        public bool AddExperience(int amount)
        {
            if (!IsInitialized || health.IsDead || amount <= 0)
                return false;

            // 先在局部变量中结算，用long容纳本次新增经验，避免相加时整数溢出。
            long remainingExp = (long)exp + amount;
            int nextLevel = playerLevel;
            int requiredExp = ExpToNextLevel;
            while (remainingExp >= requiredExp)
            {
                // 每升一级只扣掉该级门槛，剩余经验继续尝试下一次升级。
                remainingExp -= requiredExp;
                nextLevel = checked(nextLevel + 1);
                requiredExp = CombatMath.GetRequiredExp(checked(nextLevel + 1), expA, expB, expC);
            }

            int levelsGained = nextLevel - playerLevel;
            // checked防止异常大的进度把点数溢出成负数；失败时尚未修改玩家状态。
            int nextGrowthPoints = checked(growthPoints + levelsGained * growthPointsPerLevel);
            exp = (int)remainingExp;
            playerLevel = nextLevel;
            growthPoints = nextGrowthPoints;

            // 所有升级结算完成后再通知。监听者只读取和展示结果，不在回调里再次结算或初始化。
            ExperienceChanged?.Invoke();
            if (levelsGained > 0)
                LevelUp?.Invoke(nextLevel);
            StatsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// 查询单项属性的提升次数，与 PlayerLevel 玩家等级不同。
        /// 三项已实现的属性返回实际阶数；HP 购买未接入，暂时为零阶。
        /// </summary>
        public int GetAttributeRank(AttributeType attribute)
        {
            EnsureInitialized();
            switch (attribute)
            {
                case AttributeType.Hp:
                    return 0;
                case AttributeType.Attack:
                    return attackRank;
                case AttributeType.Movement:
                    return movementRank;
                case AttributeType.AttackSpeed:
                    return attackSpeedRank;
                default:
                    throw new ArgumentOutOfRangeException(nameof(attribute));
            }
        }

        /// <summary>
        /// 由 GameSystem 在进入升级阶段时开启，离开时关闭；UI 不负责设置阶段许可。
        /// 初始化及停用时默认关闭，避免复用对象继承上一次的购买权限。
        /// </summary>
        public void SetPurchasingEnabled(bool enabled)
        {
            purchasingEnabled = enabled && IsInitialized && !health.IsDead;
        }

        /// <summary>
        /// 购买一次属性提升，成功返回 true，并统一通知属性和点数变化。
        /// 未开放购买、点数不足、生命无效、HP 或未知属性均返回 false，不扣点。
        /// </summary>
        public bool TryUpgradeAttribute(AttributeType attribute)
        {
            // 不允许购买或点数不足时直接返回，属性和成长点都保持原值。
            if (!CanPurchaseAttributes || growthPoints < attributeUpgradeCost)
                return false;

            // 按下一阶的总值公式重算所选属性，其他属性的阶数不变。
            switch (attribute)
            {
                case AttributeType.Attack:
                    int nextAttackRank = attackRank + 1;
                    playerAttack = CombatMath.GetAttributeValue(
                        baseAttack, attackGrowth.B, attackGrowth.G, nextAttackRank);
                    attackRank = nextAttackRank;
                    break;
                case AttributeType.Movement:
                    int nextMovementRank = movementRank + 1;
                    playerMovement = CombatMath.GetAttributeValue(
                        baseMovement, movementGrowth.B, movementGrowth.G, nextMovementRank);
                    movementRank = nextMovementRank;
                    break;
                case AttributeType.AttackSpeed:
                    int nextAttackSpeedRank = attackSpeedRank + 1;
                    attackSpeed = CombatMath.GetAttributeValue(
                        baseAttackSpeed, attackSpeedGrowth.B, attackSpeedGrowth.G, nextAttackSpeedRank);
                    attackSpeedRank = nextAttackSpeedRank;
                    break;
                default:
                    return false;
            }

            // 只有支持的属性成功更新后才扣点，HP或未知属性在上方已经返回。
            growthPoints -= attributeUpgradeCost;

            // 先完成属性和点数结算，再发出一次通知，监听者不会读到只改了一半的状态。
            StatsChanged?.Invoke();
            return true;
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
            purchasingEnabled = false;
            initialized = false;
        }
    }
}
