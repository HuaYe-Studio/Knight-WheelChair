using System;
using KWC.Data;
using UnityEngine;

namespace KWC.Combat
{
    /// <summary>
    /// 玩家受伤入口：首笔有效伤害触发无敌，暂停或升级时禁止受伤并冻结无敌时间。
    /// 命中由调用方确认，阶段许可由 GameSystem 接线；HP 仍由 Health 唯一维护。
    /// </summary>
    public sealed class PlayerDamageReceiver : MonoBehaviour
    {
        private Health health;
        private int lifeId;
        private bool initialized;
        private bool damageEnabled;
        private float invulnerabilityDuration;
        private float invulnerabilityEndsAt;
        private float pausedInvulnerabilityRemaining;

        public bool IsInitialized => initialized && health != null &&
            health.IsInitialized && health.LifeId == lifeId;

        public float InvulnerabilityRemaining
        {
            get
            {
                if (!IsInitialized || health.IsDead) return 0f;
                if (!damageEnabled) return pausedInvulnerabilityRemaining;
                return Mathf.Max(0f, invulnerabilityEndsAt - Time.time);
            }
        }

        public bool CanReceiveDamage => IsInitialized && isActiveAndEnabled &&
            damageEnabled && !health.IsDead && InvulnerabilityRemaining <= 0f;

        /// <summary>
        /// 在玩家 Health 初始化后调用。每条生命初始化一次，初始关闭受伤许可。
        /// 同一条命不能靠重复初始化清除无敌；复用时先让 Health 开始新生命。
        /// </summary>
        public void Initialize(Health playerHealth, CombatRulesConfig rules)
        {
            if (playerHealth == null || !playerHealth.IsInitialized)
                throw new ArgumentException("Initialize the player Health first.", nameof(playerHealth));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (health == playerHealth && lifeId == playerHealth.LifeId)
                throw new InvalidOperationException("Damage receiver is already initialized for this lifetime.");

            health = playerHealth;
            lifeId = playerHealth.LifeId;
            invulnerabilityDuration = rules.PlayerInvulnerabilityTime;
            invulnerabilityEndsAt = 0f;
            pausedInvulnerabilityRemaining = 0f;
            damageEnabled = false;
            initialized = true;
        }

        /// <summary>
        /// 战斗时开启，暂停、Upgrade 或结束本局时关闭。重复设置不会重置计时。
        /// 这只是本组件的开关，不改变 GameState 或 Time.timeScale。
        /// </summary>
        public void SetDamageEnabled(bool enabled)
        {
            if (!IsInitialized || damageEnabled == enabled) return;

            if (enabled)
            {
                // 恢复时，从保存的剩余时间继续等待。
                invulnerabilityEndsAt = Time.time + pausedInvulnerabilityRemaining;
            }
            else
            {
                // 关闭前保存剩余时间；关闭期间不再按时钟扣减。
                pausedInvulnerabilityRemaining = InvulnerabilityRemaining;
            }
            damageEnabled = enabled;
        }

        /// <summary>
        /// true 表示实际扣血。无敌、未开放、死亡、旧生命或无效伤害返回 false。
        /// expectedLifeId 必须是产生请求时记录的玩家生命编号。
        /// </summary>
        public bool TryApplyDamage(float amount, int expectedLifeId)
        {
            if (!CanReceiveDamage || expectedLifeId != lifeId) return false;

            // Health 会同步发事件，先占住无敌时间，避免事件回调再次扣血。
            invulnerabilityEndsAt = Time.time + invulnerabilityDuration;
            if (health.TryApplyDamage(amount, expectedLifeId)) return true;

            // 没有实际扣血（例如伤害为零）就不触发无敌。
            invulnerabilityEndsAt = 0f;
            return false;
        }

        private void OnDisable()
        {
            initialized = false;
            damageEnabled = false;
            invulnerabilityEndsAt = 0f;
            pausedInvulnerabilityRemaining = 0f;
        }
    }
}
