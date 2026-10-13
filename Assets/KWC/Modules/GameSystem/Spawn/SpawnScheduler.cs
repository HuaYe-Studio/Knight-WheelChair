using System.Collections.Generic;
using KWC.Data;
using KWC.Enemy;
using UnityEngine;
using System;

namespace KWC.GameSystem
{
    public class SpawnScheduler:MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PrefabPool warningPool;
        [SerializeField] private WaveSetConfig waveSetConfig;

        [Header("敌人接线引用")]
        [SerializeField] private PrefabPool enemyPool;
        [SerializeField] private Enemy1Config enemy1Config;
        [SerializeField] private TestPlayerContext playerContext;

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
        private List<GameObject> livedEnemy = new List<GameObject>();

        [SerializeField] private bool isInWave = false;
        [SerializeField] private bool isInBossWave = false;

        public event Action<int> WaveChanged;
        //仅处理波次正常结束
        public event Action WaveEnded;

        private void Update()
        {
            InWave();
        }

        private void OnDisable()
        {
            StopWave();
        }

        public void InitializeWave()
        {
            if (waveSetConfig == null)
            {
                Debug.LogError("assign the Waves config before starting a wave.", this);
                return;
            }

            if (warningPool == null)
            {
                Debug.LogError("assign the warning pool before starting a wave.", this);
                return;
            }

            if (enemyPool == null)
            {
                Debug.LogError("assign the enemy pool before starting a wave.", this);
                return;
            }

            if (!isInWave)
            {
                WaveDefinition found = null;
                foreach (WaveDefinition waves in waveSetConfig.Waves)
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
                waveTimer = useTestWaveTime ? testWaveTime : waveSetConfig.NormalWaveDuration;
                livedSpawnWarnings.Clear();
                livedEnemy.Clear();
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

            if (generatedEnemy == spawnCount && livedEnemy.Count == 0)
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
                    Vector3 testSpawnPosition = GetTestSpawnPosition();
                    warningPool.Rent(testSpawnPosition, Quaternion.identity, go =>
                    {
                        int runId = runIndex;
                        var spawnWarning = go.GetComponent<SpawnWarning>();
                        preparedEnemy++;
                        livedSpawnWarnings.Add(spawnWarning);
                        spawnWarning.InitializeWarning(waveSetConfig.EnemySpawnWarningTime, () =>
                        {
                            if(runId != runIndex)
                                return;
                            Debug.Log("Spawn warning Done,enemy is coming!");

                            GameObject enemy = enemyPool.Rent(go.transform.position, go.transform.rotation, InitializeEnemy1);
                            livedEnemy.Add(enemy);
                            generatedEnemy++;
                            if (generatedEnemy == spawnCount)
                            {
                                Debug.Log("Enemy Totally generated!");
                            }
                            warningPool.Return(go);
                            livedSpawnWarnings.Remove(spawnWarning);
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
            WaveEnded?.Invoke();
            Debug.Log($"Wave{waveIndex} Finished!");
            waveIndex++;
        }

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

        private void EnterBossWave()
        {
            isInBossWave = true;

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

        //纯测试,combat那边完善就删
        public void TestKillOneEnemy()
        {
            if (livedEnemy.Count == 0)
                return;

            GameObject enemy = livedEnemy[0];
            var controller = enemy.GetComponent<Enemy1Controller>();
            controller.ReportEnemyDied(controller.LifeId);
            RecycleEnemy(enemy);
        }

        //处理敌人死亡
        public void RecycleEnemy(GameObject enemy)
        {
            if (!livedEnemy.Remove(enemy))
                return;

            EnemyReset(enemy);
        }

        private void EnemyReset(GameObject enemy)
        {
            enemy.GetComponent<PhysicsContactSource>().ResetContact();
            enemy.GetComponent<Enemy1Controller>().ResetForReuse();
            enemyPool.Return(enemy);
        }

        //改为了回收
        private void ClearSpawnState()
        {
            if (warningPool != null)
            {
                foreach (var livedWarning in livedSpawnWarnings)
                {
                    if(livedWarning == null)
                        continue;

                    livedWarning.CancelWarning();
                    warningPool.Return(livedWarning.gameObject);
                }
            }

            if (enemyPool != null)
            {
                foreach (var enemy in livedEnemy)
                {
                    if(enemy == null)
                        continue;

                    EnemyReset(enemy);
                }
            }

            livedSpawnWarnings.Clear();
            livedEnemy.Clear();
            runIndex++;
            isInWave = false;

            //待boss逻辑完善修改位置
            isInBossWave = false;
        }

        private void InitializeEnemy1(GameObject enemyGo)
        {
            float waveHp = wave.E1Hp;
            float waveAttack = wave.E1Attack;

            var controller = enemyGo.GetComponent<Enemy1Controller>();
            var bridge     = enemyGo.GetComponent<LogOnlyCombatBridge>();
            var contact    = enemyGo.GetComponent<PhysicsContactSource>();
            var health     = new FactHealthSource(waveHp);

            contact.Initialize(playerContext.EntityTransform);

            controller.Initialize(
                playerContext,
                enemyGo.GetComponent<SimpleMovementHandler>(),
                contact,
                bridge,
                enemy1Config,
                health,
                waveHp,
                waveAttack
            );
        }


        #region 对外只读接口

        public int GetWaveIndex()
        {
            return waveIndex;
        }

        public bool IsInWave()
        {
            return isInWave;
        }

        public bool IsInBossWave()
        {
            return isInBossWave;
        }

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

        public int GetLivedEnemyCount()
        {
            return livedEnemy.Count;
        }

        #endregion
    }
}
