using System;
using KWC.Core;
using KWC.Combat;
using KWC.Data;
using KWC.Enemy;
using UnityEngine;

namespace KWC.GameSystem
{
    /// <summary>
    /// 玩家本局的初始化与承伤入口：把 GameConfig 的基础值写入 Combat 的 Health/PlayerStats，
    /// 并在流程层组织下先于敌人初始化。0.5 秒无敌目前在此暂管，规则确认后交回 Combat。
    /// </summary>
    public class PlayerInitializer:MonoBehaviour
    {
        [Header("Player Status")]
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerStats playerStats;

        //以下都是测试值
        [SerializeField] private int initialLevel = 1;
        [SerializeField] private int initialExp = 0;
        [SerializeField] private int initialGrowthPoints = 0;

        private float nextDamageAllowedTime = 0;
        private GameConfig config;
        /// <summary>由 TryInitialize 置位；外部判断玩家是否可用请使用 IsReady。</summary>
        public bool isInitialized = false;

        /// <summary>已初始化、Health 与 PlayerStats 有效且玩家未死亡；敌人初始化与承伤都以它为前置条件。</summary>
        public bool IsReady => isInitialized &&
                               playerHealth != null && playerHealth.IsInitialized && !playerHealth.IsDead &&
                               playerStats != null && playerStats.IsInitialized;

        /// <summary>供流程层读取的 Combat 组件引用；状态仍由组件唯一维护，不在这里复制。</summary>
        public PlayerStats PlayerStats => playerStats;

        /// <summary>供流程层读取的 Combat 组件引用；状态仍由组件唯一维护，不在这里复制。</summary>
        public Health PlayerHealth => playerHealth;

        /// <summary>
        /// 由流程层在进入 Playing 时调用；幂等，已就绪直接返回 true。
        /// 先初始化 Health，再用同一 lifeId 建立 PlayerStats，失败时保持未就绪。
        /// </summary>
        public bool TryInitialize(GameConfig gameConfig)
        {
            if(IsReady)
                return true;

            config = gameConfig;

            if (playerHealth == null)
            {
                Debug.LogError(" assign the playerHealth before starting initialization.", this);
                return false;
            }

            if (playerStats == null)
            {
                Debug.LogError(" assign the playerStats before starting initialization.", this);
                return false;
            }

            if (config == null)
            {
                Debug.LogError(" assign the config before starting initialization.", this);
                return false;
            }

            if (initialLevel < 1 || initialExp < 0 || initialGrowthPoints < 0)
            {
                Debug.LogError(" Initial Level/exp/growthPoint is invalid.", this);
                return false;
            }

            int lifeId = playerHealth.LifeId + 1;

            playerHealth.Initialize(config.PlayerBase.PlayerHp,config.PlayerBase.PlayerHp, lifeId);
            playerStats.Initialize(config.PlayerBase, playerHealth, config.AttributeGrowth, config.CombatRules,initialLevel, initialExp, initialGrowthPoints);

            nextDamageAllowedTime = 0;

            Debug.Log($"player初始化完成，当前HP为{playerHealth.CurrentHp},等级为{initialLevel},经验为{initialExp},成长点为{initialGrowthPoints}");
            isInitialized = true;

            return IsReady;
        }

        //接入死亡流程用
        /// <summary>
        /// 敌人经 EnemyCombatBridge 提交的承伤入口。校验攻击方、就绪状态与无敌窗口，
        /// 返回是否实际扣血；伤害数值与死亡事件仍由 Combat Health 结算。
        /// </summary>
        public bool TryReceiveDamage(PlayerDamageRequest request)
        {
            if(!IsReady)
                return false;

            if (request.Attacker == null)
                return false;

            if(Time.time < nextDamageAllowedTime)
                return false;

            if (playerHealth.TryApplyDamage(request.Damage,playerHealth.LifeId))
            {
                //测试无敌,等待接入combat无敌逻辑
                nextDamageAllowedTime = Time.time + config.CombatRules.PlayerInvulnerabilityTime;

                return true;
            }

            return false;
        }
    }
}
