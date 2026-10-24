using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KWC.Combat;
using KWC.GameSystem;
using TMPro;

namespace KWC.UI
{
    public class HUD : MonoBehaviour
    {
        #region HUD 控件
        [Header("玩家状态")]
        [Tooltip("玩家HP")]
        public TMP_Text playerHealthText;
        [Tooltip("玩家EXP")]
        public TMP_Text playerExperienceText;
        [Tooltip("玩家等级")]
        public TMP_Text playerLevelText;
        [Tooltip("玩家攻击力")]
        public TMP_Text playerAttackText;
        [Tooltip("玩家攻速")]
        public TMP_Text playerAttackSpeedText;
        [Tooltip("玩家移速")]
        public TMP_Text playerMoveSpeedText;

        [Header("局内数据")]
        [Tooltip("波次")]
        public TMP_Text waveIndexText;
        [Tooltip("计时")]
        public TMP_Text waveTimeText;
        #endregion

        
        [SerializeField]private Health playerHealth;
        [SerializeField]private PlayerStats playerStats;
        [SerializeField] private SpawnScheduler spawnScheduler;
        

        #region 初始化
        // 于 Start 再进行初始化
        void InitializeUI()
        {
            if (playerHealth == null || playerStats == null) return;

            playerHealth.HealthChanged += OnHealthChanged;
            playerHealth.Died += OnDied;
            playerStats.ExperienceChanged += OnExpChanged;
            playerStats.LevelUp += OnLevelUp;
            playerStats.StatsChanged += OnStatsChanged;
        }
        #endregion

        private void OnDisable()
        {
            if (playerHealth != null)
            {
                playerHealth.HealthChanged -= OnHealthChanged;
                playerHealth.Died -= OnDied;
            }
            if (playerStats != null)
            {
                playerStats.ExperienceChanged -= OnExpChanged;
                playerStats.LevelUp -= OnLevelUp;
                playerStats.StatsChanged -= OnStatsChanged;
            }
        }

        #region Combat 回调
        private void OnHealthChanged(int lifeId, float current, float max)
        {
            playerHealthText.text = "HP: " + playerHealth.CurrentHp;
        }

        private void OnDied(int lifeId)
        {
            
        }

        private void OnExpChanged()
        {
            playerExperienceText.text = "EXP: " + playerStats.Exp + "/" + playerStats.ExpToNextLevel;
        }

        private void OnLevelUp(int finalLevel)
        {
            playerLevelText.text = "LV: " + playerStats.PlayerLevel;
        }

        private void OnStatsChanged()
        {
            playerAttackText.text = "ATK: " + playerStats.PlayerAttack;
            playerAttackSpeedText.text = "ATKSpeed: " + playerStats.AttackSpeed;
            playerMoveSpeedText.text = "MoveSpeed: " + playerStats.PlayerMovement;
        }
        #endregion

        void Update()
        {
            waveIndexText.text = "WaveIndex: " + spawnScheduler.GetWaveIndex();
            waveTimeText.text = "WaveTime: " + spawnScheduler.GetWaveTime();
        }
    }
}
