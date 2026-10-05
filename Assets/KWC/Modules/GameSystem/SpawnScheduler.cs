using System.Collections.Generic;
using KWC.Data;
using UnityEngine;

namespace KWC.GameSystem
{
    public class SpawnScheduler:MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PrefabPool warningPool;
        [SerializeField] private WaveSetConfig waveSetConfig;

        [Header("生成测试区")]
        [SerializeField] private int waveIndex = 1;
        private int runIndex = 1;
        private WaveDefinition wave = null;
        private int spawnCount;
        [SerializeField] private GameObject testEnemy;
        [SerializeField] private bool useTestWaveTime = false;
        [SerializeField] private float testWaveTime = 30f;

        private int generatedEnemy = 0;
        private float generateIntervalTimer = 0f;
        private int preparedEnemy = 0;
        private float waveTimer = 0f;

        private List<SpawnWarning> livedSpawnWarnings = new List<SpawnWarning>();
        private List<GameObject> livedEnemy = new List<GameObject>();

        [SerializeField] private bool isInWave = false;

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
            }
        }

        // 尾批不足单批数量时，暂定只发剩余数量；正式规则待策划确认（见模块 README 未决项）
        private void InWave()
        {
            if (!isInWave)
                return;

            waveTimer -= Time.deltaTime;
            if (waveTimer <= 0f)
            {
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
                            GameObject enemy = Instantiate(testEnemy,go.transform.position, go.transform.rotation);
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
            float x = Random.Range(-10f, 10f);
            float z = Random.Range(-10f, 10f);
            return new Vector3(x, 0f, z);
        }

        private void EnterBossWave()
        {
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

        //这里暂时全部清理,加入了判空检查
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

            foreach (var enemy in livedEnemy)
            {
                if(enemy == null)
                    continue;

                Destroy(enemy);
            }
            livedSpawnWarnings.Clear();
            livedEnemy.Clear();
            runIndex++;
            isInWave = false;
        }


    }
}
