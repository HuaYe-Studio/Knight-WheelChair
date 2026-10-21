using System;
using KWC.Core;
using KWC.Combat;
using KWC.Enemy;
using UnityEngine;
using UnityEngine.Serialization;

namespace KWC.GameSystem
{
    public class PlayerInitializer:MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;

        [Header("Player Status")]
        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerStats playerStats;

        //测试值
        [SerializeField] private int initialLevel = 1;
        [SerializeField] private int initialExp = 0;
        [SerializeField] private int initialGrowthPoints = 0;

        private float nextDamageAllowedTime = 0;

        private void OnEnable()
        {
            if (gameManager == null)
            {
                Debug.LogError(" assign the gameManager before starting initialization.", this);
                return;
            }

            gameManager.GameStateChanged -= HandleGameStateChanged;
            gameManager.GameStateChanged += HandleGameStateChanged;

            HandleGameStateChanged(gameManager.CurrentState);
        }

        private void OnDisable()
        {
            if (gameManager == null)
                return;

            gameManager.GameStateChanged -= HandleGameStateChanged;
        }

        private void HandleGameStateChanged(GameState gameState)
        {
            switch (gameState)
            {
                case GameState.Playing:
                    TryInitialize();
                    break;
            }
        }

        private void TryInitialize()
        {
            #region 引用守卫区

            if (gameManager == null)
            {
                Debug.LogError(" assign the gameManager before starting initialization.", this);
                return;
            }

            if (playerHealth == null)
            {
                Debug.LogError(" assign the playerHealth before starting initialization.", this);
                return;
            }

            if (playerStats == null)
            {
                Debug.LogError(" assign the playerStats before starting initialization.", this);
                return;
            }

            if (gameManager.Config == null)
            {
                Debug.LogError(" assign the gameManager.Config before starting initialization.", this);
                return;
            }

            #endregion

            if (playerStats.IsInitialized)
                return;

            if (!gameManager.Config.HasAllReferences)
            {
                Debug.LogError(" ensure the gameManager.config has all reference before starting initialization.", this);
                return;
            }

            if (initialLevel < 1 || initialExp < 0 || initialGrowthPoints < 0)
            {
                Debug.LogError(" Initial Level/exp/growthPoint is invalid.", this);
                return;
            }

            int lifeId = playerHealth.IsInitialized ? playerHealth.LifeId + 1 : 1;

            playerHealth.Initialize(gameManager.Config.PlayerBase.PlayerHp,gameManager.Config.PlayerBase.PlayerHp, lifeId);
            playerStats.Initialize(gameManager.Config.PlayerBase, playerHealth, gameManager.Config.AttributeGrowth, gameManager.Config.CombatRules,initialLevel, initialExp, initialGrowthPoints);

            nextDamageAllowedTime = 0;

            Debug.Log($"player初始化完成，当前HP为{playerHealth.CurrentHp},等级为{initialLevel},经验为{initialExp},成长点为{initialGrowthPoints}");

        }

        public bool TryReceiveDamage(PlayerDamageRequest request)
        {
            if(!playerHealth.IsInitialized || playerHealth.IsDead)
                return false;

            if (request.Attacker == null)
                return false;

            if(Time.time < nextDamageAllowedTime)
                return false;

            if (playerHealth.TryApplyDamage(request.Damage,playerHealth.LifeId))
            {
                //测试无敌
                nextDamageAllowedTime = Time.time + gameManager.Config.CombatRules.PlayerInvulnerabilityTime;

                return true;
            }

            return false;
        }
    }
}
