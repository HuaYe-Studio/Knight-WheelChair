using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KWC.GameSystem
{
    public class SpawnScheduler:MonoBehaviour
    {
        [SerializeField] private PrefabPool warningPool;

        [Header("测试参数区域")]
        [SerializeField] private int totalEnemyCount = 5;
        [SerializeField] private float enemyInterval = 5f;
        [SerializeField] private int generateCount = 1;
        [SerializeField] private float spawnWarningTime = 0.5f;
        [SerializeField] private float waveTime = 30f;
        [SerializeField] private GameObject testEnemy;

        private int generatedEnemy = 0;
        private float generateIntervalTimer = 0f;
        private int preparedEnemy = 0;
        private float waveTimer = 0f;

        private List<SpawnWarning> livedSpawnWarnings = new List<SpawnWarning>();
        private List<GameObject> livedEnemy = new List<GameObject>();

        private bool isInWave = false;

        private void Update()
        {
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
                generatedEnemy = 0;
                preparedEnemy = 0;
                generateIntervalTimer = 0f;
                waveTimer = waveTime;
                livedSpawnWarnings.Clear();
                livedEnemy.Clear();
                isInWave = true;
            }
        }

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
            if (generateIntervalTimer <= 0f && preparedEnemy < totalEnemyCount)
            {
                generateIntervalTimer = enemyInterval;
                for (int count = 0; count < generateCount && preparedEnemy < totalEnemyCount; count++)
                {
                    Vector3 testSpawnPosition = GetTestSpawnPosition();
                    warningPool.Rent(testSpawnPosition, Quaternion.identity, go =>
                    {
                        var spawnWarning = go.GetComponent<SpawnWarning>();
                        preparedEnemy++;
                        livedSpawnWarnings.Add(spawnWarning);
                        spawnWarning.InitializeWarning(spawnWarningTime, () =>
                        {
                            Debug.Log("Spawn warning Done,enemy is coming!");
                            GameObject enemy = Instantiate(testEnemy,go.transform.position, go.transform.rotation);
                            livedEnemy.Add(enemy);
                            generatedEnemy++;
                            if (generatedEnemy == totalEnemyCount)
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
        // 尾批不足单批数量时，暂定只发剩余数量；正式规则待策划确认（见模块 README 未决项）

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
            Debug.Log("Wave Finished!");
        }

        private Vector3 GetTestSpawnPosition()
        {
            float x = Random.Range(-10f, 10f);
            float z = Random.Range(-10f, 10f);
            return new Vector3(x, 0f, z);
        }
    }
}
