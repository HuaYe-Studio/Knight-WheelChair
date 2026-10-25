using UnityEngine;
using KWC.Combat;
using KWC.GameSystem;
using TMPro;

namespace KWC.UI
{
    /// <summary>
    /// 局内 HUD。只读取 Combat / Game System 的只读状态并显示，不修改任何玩法数据。
    /// 订阅放在 OnEnable、退订放在 OnDisable，保证隐藏再显示后仍然刷新。
    /// </summary>
    public class HUD : MonoBehaviour
    {
        #region HUD 控件
        [Header("玩家状态")]
        [Tooltip("玩家HP")]
        [SerializeField] private TMP_Text playerHealthText;
        [Tooltip("玩家EXP")]
        [SerializeField] private TMP_Text playerExperienceText;
        [Tooltip("玩家等级")]
        [SerializeField] private TMP_Text playerLevelText;
        [Tooltip("玩家攻击力")]
        [SerializeField] private TMP_Text playerAttackText;
        [Tooltip("玩家攻速")]
        [SerializeField] private TMP_Text playerAttackSpeedText;
        [Tooltip("玩家移速")]
        [SerializeField] private TMP_Text playerMoveSpeedText;

        [Header("局内数据")]
        [Tooltip("波次")]
        [SerializeField] private TMP_Text waveIndexText;
        [Tooltip("计时")]
        [SerializeField] private TMP_Text waveTimeText;
        #endregion

        [SerializeField] private Health playerHealth;
        [SerializeField] private PlayerStats playerStats;
        [SerializeField] private SpawnScheduler spawnScheduler;

        private int lastWaveIndex = int.MinValue;
        private int lastWaveTimeKey = int.MinValue;
        private bool snapshotTaken;
        private bool subscribed;

        #region 初始化与订阅
        void OnEnable()
        {
            Subscribe();
            TakeSnapshot();
        }

        private void OnDisable()
        {
            Unsubscribe();

            // 复位状态：重新启用时要重新订阅并重新读快照（可能已经进入新的玩家生命）。
            subscribed = false;
            snapshotTaken = false;
            lastWaveIndex = int.MinValue;
            lastWaveTimeKey = int.MinValue;
        }

        /// <summary>
        /// 由流程在完成 Health.Initialize / PlayerStats.Initialize 之后主动调用，作为兜底刷新。
        /// 数据尚未就绪时不会抛异常，Update 里会继续重试。
        /// </summary>
        public void Refresh()
        {
            TakeSnapshot();
        }

        private void Subscribe()
        {
            if (playerHealth == null || playerStats == null)
            {
                Debug.LogWarning("HUD 初始化失败：无法获取 PlayerHealth / PlayerStats");
                return;
            }

            // 先移除再添加，避免同一组件被重复订阅后事件执行多次。
            playerHealth.HealthChanged -= OnHealthChanged;
            playerHealth.Died -= OnDied;
            playerStats.ExperienceChanged -= OnExpChanged;
            playerStats.LevelUp -= OnLevelUp;
            playerStats.StatsChanged -= OnStatsChanged;

            playerHealth.HealthChanged += OnHealthChanged;
            playerHealth.Died += OnDied;
            playerStats.ExperienceChanged += OnExpChanged;
            playerStats.LevelUp += OnLevelUp;
            playerStats.StatsChanged += OnStatsChanged;

            subscribed = true;
        }

        private void Unsubscribe()
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

        // PlayerStats 的读取属性在未初始化时会抛 InvalidOperationException，读快照前必须先确认。
        private bool PlayerDataReady =>
            playerHealth != null && playerStats != null &&
            playerHealth.IsInitialized && playerStats.IsInitialized;

        /// <summary>
        /// 读一次状态快照。数据尚未初始化时返回 false 且不抛异常。
        /// 初始化不发事件，所以订阅后必须主动读一次，否则界面会停留在 Prefab 里的占位文本。
        /// </summary>
        private bool TakeSnapshot()
        {
            if (!subscribed || !PlayerDataReady) return false;

            RenderHealth(playerHealth.CurrentHp, playerHealth.MaxHp);
            RenderExp();
            RenderLevel(playerStats.PlayerLevel);
            RenderStats();
            RenderWave(true);

            snapshotTaken = true;
            return true;
        }
        #endregion

        #region Combat 回调
        private void OnHealthChanged(int lifeId, float current, float max)
        {
            RenderHealth(current, max);
        }

        private void OnExpChanged()
        {
            RenderExp();
        }

        private void OnLevelUp(int finalLevel)
        {
            RenderLevel(finalLevel);
        }

        private void OnStatsChanged()
        {
            RenderStats();
        }

        /// <summary>
        /// 玩家死亡只做显示层面的收尾；胜负裁决与结算界面归 Game System。
        /// </summary>
        private void OnDied(int lifeId)
        {
            if (playerHealth != null)
            {
                RenderHealth(0f, playerHealth.MaxHp);
            }
        }
        #endregion

        void Update()
        {
            // 玩家数据可能在 HUD 启用之后才初始化，而初始化不发事件：这里补一次快照。
            if (!snapshotTaken && TakeSnapshot())
            {
                return;
            }

            RenderWave(false);
        }

        #region 显示
        private void RenderHealth(float current, float max)
        {
            SetText(playerHealthText, $"HP: {FormatValue(current)}/{FormatValue(max)}");
        }

        private void RenderExp()
        {
            SetText(playerExperienceText, $"EXP: {playerStats.Exp}/{playerStats.ExpToNextLevel}");
        }

        private void RenderLevel(int level)
        {
            SetText(playerLevelText, $"LV: {level}");
        }

        private void RenderStats()
        {
            SetText(playerAttackText, $"ATK: {FormatValue(playerStats.PlayerAttack)}");
            SetText(playerAttackSpeedText, $"ATKSpeed: {FormatValue(playerStats.AttackSpeed)}");
            SetText(playerMoveSpeedText, $"MoveSpeed: {FormatValue(playerStats.PlayerMovement)}");
        }

        /// <summary>
        /// 波次号与倒计时。force 为 true 时无条件写一次（首次快照 / 强制刷新）。
        /// </summary>
        private void RenderWave(bool force)
        {
            if (spawnScheduler == null) return;

            // 只在真正处于波次中时显示编号：波间的 GetWaveIndex() 已经指向下一波。
            int waveIndex = spawnScheduler.IsInWave() ? spawnScheduler.GetWaveIndex() : -1;
            if (force || waveIndex != lastWaveIndex)
            {
                lastWaveIndex = waveIndex;
                SetText(waveIndexText, waveIndex < 0 ? "WaveIndex: --" : $"WaveIndex: {waveIndex}");
            }

            float waveTime = spawnScheduler.GetWaveTime();
            int timeKey = WaveTimeKey(waveTime);
            if (force || timeKey != lastWaveTimeKey)
            {
                lastWaveTimeKey = timeKey;
                SetText(waveTimeText, FormatWaveTime(waveTime));
            }
        }

        private static int WaveTimeKey(float waveTime)
        {
            if (waveTime < 0f) return -1;
            if (float.IsInfinity(waveTime)) return int.MaxValue;
            return Mathf.FloorToInt(waveTime * 10f);
        }

        private static string FormatWaveTime(float waveTime)
        {
            if (waveTime < 0f) return "WaveTime: --";
            if (float.IsInfinity(waveTime)) return "WaveTime: Boss";
            return $"WaveTime: {waveTime:F1}";
        }

        private static string FormatValue(float value)
        {
            return value.ToString("0.#");
        }

        // 接线不全时只跳过显示，不抛 NullReferenceException。
        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
            {
                target.text = value;
            }
        }
        #endregion
    }
}
