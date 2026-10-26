using KWC.Combat;
using KWC.Core;
using KWC.Data;
using KWC.Enemy;
using UnityEngine;

namespace KWC.GameSystem
{
    /// <summary>
    /// 玩家本局的初始化与承伤入口：把 GameConfig 的基础值写入 Combat 的 Health/PlayerStats，
    /// 并用同一 lifeId 初始化 PlayerDamageReceiver（玩家受伤入口）与 PlayerPickupCollector（玩家拾取入口）。
    /// 承伤统一委托给 PlayerDamageReceiver，本组件不再维护第二套无敌计时。
    /// </summary>
    public class PlayerInitializer:MonoBehaviour
    {
        [Header("Player Status")]
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private PlayerDamageReceiver playerDamageReceiver;
        [SerializeField] private PlayerPickupCollector playerPickupCollector;

        //以下都是测试值
        [SerializeField] private int initialLevel = 1;
        [SerializeField] private int initialExp = 0;
        [SerializeField] private int initialGrowthPoints = 0;

        private GameConfig config;
        /// <summary>由 TryInitialize 置位；外部判断玩家是否可用请使用 IsReady。</summary>
        public bool isInitialized = false;

        /// <summary>已初始化、Health/PlayerStats/PlayerDamageReceiver 有效、PlayerPickupCollector 已注入且玩家未死亡；敌人初始化与承伤都以它为前置条件。</summary>
        public bool IsReady => isInitialized &&
                               playerHealth != null && playerHealth.IsInitialized && !playerHealth.IsDead &&
                               playerStats != null && playerStats.IsInitialized &&
                               playerDamageReceiver!=null && playerDamageReceiver.IsInitialized &&
                               playerPickupCollector != null;

        /// <summary>供流程层读取的 Combat 组件引用；状态仍由组件唯一维护，不在这里复制。</summary>
        public PlayerStats PlayerStats => playerStats;

        /// <summary>供流程层读取的 Combat 组件引用；状态仍由组件唯一维护，不在这里复制。</summary>
        public Health PlayerHealth => playerHealth;
        public PlayerDamageReceiver PlayerDamageReceiver => playerDamageReceiver;
        public PlayerPickupCollector PlayerPickupCollector => playerPickupCollector;

        /// <summary>
        /// 由流程层在进入 Playing 时调用；幂等，已就绪直接返回 true。
        /// 顺序为 Health → PlayerStats → PlayerDamageReceiver → PlayerPickupCollector，
        /// 任一失败保持未就绪；承伤/拾取的阶段许可由 SessionFlow 控制。
        /// </summary>
        public bool TryInitialize(GameConfig gameConfig,IPlayerContext testPlayerContext)
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

            if (playerDamageReceiver == null)
            {
                Debug.LogError(" assign the playerDamageReceiver before starting initialization.", this);
                return false;
            }

            if (playerPickupCollector == null)
            {
                Debug.LogError(" assign the playerPickupCollector before starting initialization.", this);
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
            playerDamageReceiver.Initialize(playerHealth,config.CombatRules);
            playerPickupCollector.Initialize(testPlayerContext,playerStats,playerHealth);

            Debug.Log($"player初始化完成，当前HP为{playerHealth.CurrentHp},等级为{initialLevel},经验为{initialExp},成长点为{initialGrowthPoints}");
            isInitialized = true;

            return IsReady;
        }

        /// <summary>
        /// 敌人经 EnemyCombatBridge 提交的承伤入口。当前为同步送达：
        /// 直接使用调用时的玩家 LifeId 交给 PlayerDamageReceiver 判定。
        /// 返回是否实际扣血；若以后改成排队/延迟请求，必须把请求发生时的 LifeId 一并带入。
        /// </summary>
        public bool TryReceiveDamage(PlayerDamageRequest request)
        {
            //用health保证同步送达当前玩家id
            //之后排队请求再把玩家id带入请求
            return playerDamageReceiver.TryApplyDamage(request.Damage,playerHealth.LifeId);
        }
    }
}
