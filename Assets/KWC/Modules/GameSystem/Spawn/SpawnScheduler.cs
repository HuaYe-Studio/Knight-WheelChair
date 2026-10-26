using System.Collections.Generic;
using KWC.Data;
using UnityEngine;
using System;

namespace KWC.GameSystem
{
    /// <summary>
    /// 波次调度：按 WaveSetConfig（波次集合配置）计时、租用预警并按批次生成敌人。
    /// 不拥有敌人生命周期：生成交给 EnemyLifeCycle（敌人生命周期），波末清理也委托它执行。
    /// StopWave 用于中止（玩家死亡、场景卸载），不会自增波号，也不触发 WaveEnded。
    /// </summary>
    public class SpawnScheduler:MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PrefabPool warningPool;
        private GameConfig waveConfig;
        [SerializeField] private EnemyLifeCycle enemyLifeCycle;

        [Header("生成测试区")]
        [SerializeField] private int waveIndex = 1;
        private int runIndex = 1;
        private WaveDefinition wave = null;
        private int spawnCount;
        [SerializeField] private bool useTestWaveTime = false;
        [SerializeField] private float testWaveTime = 30f;

        private int generatedEnemy = 0;
        private float generateIntervalTimer = 0f;
        private int preparedEnemy = 0;
        private float waveTimer = 0f;

        private List<SpawnWarning> livedSpawnWarnings = new List<SpawnWarning>();

        [SerializeField] private bool isInWave = false;
        [SerializeField] private bool isInBossWave = false;
        private bool isInitialized = false;

        /// <summary>波次真正开始时触发，参数为当前波号；只表示进度，不代表击杀数。</summary>
        public event Action<int> WaveChanged;

        /// <summary>
        /// 普通波次正常结束时触发（计时到，或计划敌人全部生成并已回收）。
        /// 触发时清理已完成且 waveIndex 已指向下一波；StopWave 不触发本事件。
        /// </summary>
        public event Action WaveEnded;

        private void Update()
        {
            InWave();
        }

        private void OnDisable()
        {
            StopWave();
        }

        /// <summary>
        /// 由流程层在场景启用时调用一次：缓存配置并校验预警池。
        /// 返回 false 时不要继续调用 InitializeWave。
        /// </summary>
        public bool Initialize(GameConfig waveSetConfig)
        {
            isInitialized = false;
            waveConfig = waveSetConfig;

            if (warningPool == null)
            {
                Debug.LogError("assign the warning pool before starting a wave.", this);
                return false;
            }

            if (warningPool.Prefab == null)
            {
                Debug.LogError("assign the prefab in warning pool before starting a wave.", this);
                return false;
            }

            GameObject warning = warningPool.Prefab;
            if (!warning.TryGetComponent<SpawnWarning>(out _))
            {
                Debug.LogError("need SpawnWarning component before starting a wave.", this);
                return false;
            }

            isInitialized = true;
            return isInitialized;
        }

        /// <summary>
        /// 由流程层在玩家和敌人就绪后调用。未在波中时按 waveIndex 查找定义并开始；
        /// 重复调用在波中会被忽略。Boss 波当前仅记录占位日志，不会进入波次状态。
        /// </summary>
        public void InitializeWave()
        {
            if(!isInitialized)
                return;

            if (waveConfig == null || waveConfig.Waves == null)
            {
                Debug.LogError("assign the Waves config before starting a wave.", this);
                return;
            }

            if (enemyLifeCycle == null || !enemyLifeCycle.isInitialized)
            {
                Debug.LogError("assign initialized enemyLifeCycle before starting a wave.");
                return;
            }

            if (!isInWave)
            {
                WaveDefinition found = null;
                foreach (WaveDefinition waves in waveConfig.Waves.Waves)
                {
                    if (waves.WaveIndex == waveIndex)
                    {
                        found = waves;
                        break;
                    }
                }

                if (found == null)
                {
                    Debug.LogError($"找不到 Wave{waveIndex}");
                    return;
                }

                if (found.IsBossWave)
                {
                    EnterBossWave();
                    return;
                }

                if (!found.TryGetActualSpawnCount(out int total))
                {
                    Debug.LogError($"Wave{waveIndex} 的生成数量未确认，暂不开波");
                    return;
                }

                wave = found;
                spawnCount = total;

                generatedEnemy = 0;
                preparedEnemy = 0;
                generateIntervalTimer = 0f;
                waveTimer = useTestWaveTime ? testWaveTime : waveConfig.Waves.NormalWaveDuration;
                livedSpawnWarnings.Clear();
                isInWave = true;
                isInBossWave = false;
                WaveChanged?.Invoke(waveIndex);
            }
        }

        // 尾批不足单批数量时，暂定只发剩余数量；正式规则待策划确认（见模块 README 未决项）
        private void InWave()
        {
            if (!isInWave)
                return;

            if(isInBossWave)
                return;

            waveTimer -= Time.deltaTime;
            if (waveTimer <= 0f)
            {
                EndWave();
                return;
            }

            if (generatedEnemy == spawnCount && enemyLifeCycle.GetLivedEnemyCount() == 0)
            {
                Debug.Log("提前杀死所有敌人，波次结束");
                EndWave();
                return;
            }

            generateIntervalTimer -= Time.deltaTime;
            if (generateIntervalTimer <= 0f && preparedEnemy < spawnCount)
            {
                generateIntervalTimer += wave.SpawnInterval;
                for (int count = 0; count < wave.SpawnBatchCount && preparedEnemy < spawnCount; count++)
                {
                    float hp = wave.E1Hp;
                    float attack = wave.E1Attack;

                    Vector3 testSpawnPosition = GetTestSpawnPosition();
                    warningPool.Rent(testSpawnPosition, Quaternion.identity, go =>
                    {
                        int runId = runIndex;
                        var spawnWarning = go.GetComponent<SpawnWarning>();
                        preparedEnemy++;
                        livedSpawnWarnings.Add(spawnWarning);
                        spawnWarning.InitializeWarning(waveConfig.Waves.EnemySpawnWarningTime, () =>
                        {
                            if(runId != runIndex)
                                return;
                            Debug.Log("Spawn warning Done,enemy is coming!");

                            GameObject enemy = null;

                            try
                            {
                                bool success = enemyLifeCycle.TryGenerateEnemy1(out enemy,go.transform.position,go.transform.rotation, hp, attack);
                                if(success)
                                    generatedEnemy++;
                            }
                            finally
                            {
                                warningPool.Return(go);
                                livedSpawnWarnings.Remove(spawnWarning);
                            }

                            if (generatedEnemy == spawnCount)
                            {
                                Debug.Log("Enemy Totally generated!");
                            }

                        });
                    });
                }
            }
        }

        private void EndWave()
        {
            if(!isInWave)
                return;

            ClearSpawnState();
            int endWaveIndex = waveIndex;

            waveIndex++;

            WaveEnded?.Invoke();
            Debug.Log($"Wave{endWaveIndex} Finished!");

        }

        /// <summary>
        /// 中止当前波：取消未完成的预警、回收在场敌人与拾取物。
        /// 不会自增 waveIndex，也不触发 WaveEnded；用于玩家死亡或场景卸载。
        /// </summary>
        public void StopWave()
        {
            if(!isInWave)
                return;

            ClearSpawnState();
            Debug.Log($"Wave{waveIndex} Stopped!");
        }

        private Vector3 GetTestSpawnPosition()
        {
            //随便写的范围，等待map确定
            float x = UnityEngine.Random.Range(-10f, 10f);
            float z = UnityEngine.Random.Range(-10f, 10f);
            return new Vector3(x, 0f, z);
        }

        //占位暂时不改动运行状态，将来与 isInWave / waveTimer=Infinity 一起提交
        private void EnterBossWave()
        {
            //isInBossWave = true;

            //此处逻辑暂未完善
            // generatedEnemy = 0;
            // preparedEnemy = 0;
            // generateIntervalTimer = 0f;
            // waveTimer = Mathf.Infinity;
            // livedSpawnWarnings.Clear();
            // livedEnemy.Clear();
            // isInWave = true;

            Debug.Log("Boss波次未实现");
        }

        //改为了回收
        private void ClearSpawnState()
        {
            try
            {
                if (warningPool != null)
                {
                    foreach (var livedWarning in livedSpawnWarnings)
                    {
                        if (livedWarning == null)
                            continue;

                        livedWarning.CancelWarning();
                        warningPool.Return(livedWarning.gameObject);
                    }
                }
            }
            finally
            {
                livedSpawnWarnings.Clear();
                if(enemyLifeCycle!=null)
                    enemyLifeCycle.ClearEnemySpawnState();
                runIndex++;
                isInWave = false;

                //待boss逻辑完善修改位置
                isInBossWave = false;
            }
        }

        #region 对外只读接口

        /// <summary>下一次 InitializeWave 使用的波号；EndWave 触发后已自增。</summary>
        public int GetWaveIndex()
        {
            return waveIndex;
        }

        /// <summary>当前是否处于普通波次进行中。</summary>
        public bool IsInWave()
        {
            return isInWave;
        }

        /// <summary>Boss 波是否进行中；Boss 波尚未实现，当前始终为 false。</summary>
        public bool IsInBossWave()
        {
            return isInBossWave;
        }

        /// <summary>剩余波次时间；未开波返回 -1，Boss 波（实现后）返回 Infinity。</summary>
        public float GetWaveTime()
        {
            if (!isInWave)
            {
                return -1f;
            }

            if (isInBossWave)
            {
                return Mathf.Infinity;
            }
            else
            {
                return waveTimer;
            }
        }
        #endregion
    }
}
