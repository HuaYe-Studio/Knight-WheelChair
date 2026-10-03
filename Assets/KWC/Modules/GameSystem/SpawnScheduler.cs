using System.Collections.Generic;
using KWC.Data;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace KWC.GameSystem
{
    public class SpawnScheduler:MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private PrefabPool warningPool;
        [SerializeField] private WaveSetConfig waveSetConfig;

        [Header("生成测试区")]
        private WaveDefinition wave = null;
        private int spawnCount;
        [SerializeField] private int waveIndex = 1;
        [SerializeField] private GameObject testEnemy;
        [SerializeField] private bool useTestWaveTime = false;
        [SerializeField] private float testWaveTime = 30f;

        private int generatedEnemy = 0;
        private float generateIntervalTimer = 0f;
        private int preparedEnemy = 0;
        private float waveTimer = 0f;

        private List<SpawnWarning> livedSpawnWarnings = new List<SpawnWarning>();
        private List<GameObject> livedEnemy = new List<GameObject>();

        private bool isInWave = false;

        private void Update()
        {
            //test用
            if (Keyboard.current.enterKey.wasPressedThisFrame)
            {
                InitializeWave();
            }
            InWave();
        }
        public void InitializeWave()
        {
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
                generateIntervalTimer = wave.SpawnInterval;
                for (int count = 0; count < wave.SpawnBatchCount && preparedEnemy < spawnCount; count++)
                {
                    Vector3 testSpawnPosition = GetTestSpawnPosition();
                    warningPool.Rent(testSpawnPosition, Quaternion.identity, go =>
                    {
                        var spawnWarning = go.GetComponent<SpawnWarning>();
                        preparedEnemy++;
                        livedSpawnWarnings.Add(spawnWarning);
                        spawnWarning.InitializeWarning(waveSetConfig.EnemySpawnWarningTime, () =>
                        {
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

        //这里暂时全部清理
        private void EndWave()
        {
            foreach (var livedWarning in livedSpawnWarnings)
            {
                livedWarning.CancelWarning();
                warningPool.Return(livedWarning.gameObject);
            }

            foreach (var enemy in livedEnemy)
            {
                Destroy(enemy);
            }
            livedSpawnWarnings.Clear();
            livedEnemy.Clear();
            isInWave = false;
            Debug.Log($"Wave{waveIndex} Finished!");
            waveIndex++;
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
    }
}
