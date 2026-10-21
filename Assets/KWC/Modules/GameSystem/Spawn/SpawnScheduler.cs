using System.Collections.Generic;
using KWC.Data;
using KWC.Enemy;
using UnityEngine;
using System;
using KWC.Combat;

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

        [Header("战斗接线引用")]
        [SerializeField]private PlayerInitializer playerInitializer;

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

        private void LateUpdate()
        {
            while (queueDeath.Count > 0)
            {
                var death = queueDeath.Dequeue();
                if (death.enemy != null)
                {
                    RecycleEnemy(death.enemy, death.lifeId);
                }
            }
        }

        private void OnDisable()
        {
            StopWave();
        }

        public void InitializeWave()
        {
            #region 引用守卫

            if (waveSetConfig == null)
            {
                Debug.LogError("assign the Waves config before starting a wave.", this);
                return;
            }

            if (enemy1Config == null)
            {
                Debug.LogError("assign the E1 config before starting a wave.", this);
                return;
            }

            if (warningPool == null)
            {
                Debug.LogError("assign the warning pool before starting a wave.", this);
                return;
            }

            if (warningPool.Prefab == null)
            {
                Debug.LogError("assign the prefab in warning pool before starting a wave.", this);
                return;
            }

            if (enemyPool == null)
            {
                Debug.LogError("assign the enemy pool before starting a wave.", this);
                return;
            }

            if (enemyPool.Prefab == null)
            {
                Debug.LogError("assign the prefab in enemy pool before starting a wave.", this);
                return;
            }

            if (playerContext == null)
            {
                Debug.LogError("assign the player context before starting a wave.", this);
                return;
            }

            if (playerInitializer == null)
            {
                Debug.LogError("assign the player initializer before starting a wave.", this);
                return;
            }

            #endregion

            #region 组件守卫

            GameObject enemy = enemyPool.Prefab;

            if (!enemy.TryGetComponent<Enemy1Controller>(out _))
            {
                Debug.LogError("need Enemy1Controller component before starting a wave.", this);
                return;
            }

            if (!enemy.TryGetComponent<SimpleMovementHandler>(out _))
            {
                Debug.LogError("need SimpleMovementHandler component before starting a wave.", this);
                return;
            }

            if (!enemy.TryGetComponent<PhysicsContactSource>(out _))
            {
                Debug.LogError("need PhysicsContactSource component before starting a wave.", this);
                return;
            }

            if (!enemy.TryGetComponent<EnemyCombatBridge>(out _))
            {
                Debug.LogError("need EnemyCombatBridge component before starting a wave.", this);
                return;
            }

            if(!enemy.TryGetComponent<Health>(out _))
            {
                Debug.LogError("need Health component before starting a wave.", this);
                return;
            }

            GameObject warning = warningPool.Prefab;
            if (!warning.TryGetComponent<SpawnWarning>(out _))
            {
                Debug.LogError("need SpawnWarning component before starting a wave.", this);
                return;
            }

            #endregion

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

                            GameObject enemy = null;

                            try
                            {
                                enemy = enemyPool.Rent(go.transform.position, go.transform.rotation, InitializeEnemy1);
                            }
                            finally
                            {
                                warningPool.Return(go);
                                livedSpawnWarnings.Remove(spawnWarning);
                            }

                            livedEnemy.Add(enemy);
                            generatedEnemy++;
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

        //纯测试,combat那边完善就删
        public void TestKillOneEnemy()
        {
            if (livedEnemy.Count == 0)
                return;

            GameObject enemy = livedEnemy[0];
            var controller = enemy.GetComponent<Enemy1Controller>();
            int lifeId = controller.LifeId;

            var health =enemy.GetComponent<Health>();
            health.TryApplyDamage(health.CurrentHp,lifeId);
        }

        public void TestKillAllEnemies()
        {
            if (livedEnemy.Count == 0)
                return;

            for (int i = 0; i < livedEnemy.Count; i++)
            {
                GameObject enemy = livedEnemy[i];
                var controller = enemy.GetComponent<Enemy1Controller>();
                int lifeId = controller.LifeId;

                var health =enemy.GetComponent<Health>();
                health.TryApplyDamage(health.CurrentHp,lifeId);
            }
        }

        //测试
        public void TestRecycle()
        {
            if (livedEnemy.Count == 0)
                return;

            GameObject enemy = livedEnemy[0];
            var controller = enemy.GetComponent<Enemy1Controller>();
            int testRecycleLifeId = controller.LifeId - 1;
            int countBefore = livedEnemy.Count;

            bool accepted = RecycleEnemy(enemy, testRecycleLifeId);
            bool pass = !accepted && livedEnemy.Count == countBefore && enemy.activeSelf;
            Debug.Log($"迟到回收测试：testRecycleLifeId={testRecycleLifeId}，当前 lifeId={controller.LifeId}，accepted={accepted}（应为 false）；"+
                      $"{(pass ? "Success" : "Fail")}");
        }
        //处理敌人死亡
        public bool RecycleEnemy(GameObject enemy,int lifeId)
        {
            int id = enemy.GetComponent<Enemy1Controller>().LifeId;
            if (id != lifeId)
            {
                Debug.LogWarning("回收时出现life id错误");
                return false;
            }

            if (!livedEnemy.Remove(enemy))
                return false;

            EnemyReset(enemy);
            return true;
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
            queueDeath.Clear();
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
            var bridge     = enemyGo.GetComponent<EnemyCombatBridge>();
            var contact    = enemyGo.GetComponent<PhysicsContactSource>();
            var health     = new FactHealthSource(waveHp);
            var E1Health   = enemyGo.GetComponent<Health>();

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

            E1Health.Initialize(waveHp,waveHp,controller.LifeId);
            bridge.Initialize(controller,E1Health,playerInitializer,this);
        }

        private struct QueueDeath
        {
            public readonly GameObject enemy;
            public readonly int lifeId;

            public QueueDeath(GameObject Enemy, int LifeId)
            {
                enemy = Enemy;
                lifeId = LifeId;
            }
        }
        private Queue<QueueDeath> queueDeath = new Queue<QueueDeath>();
        public void EnqueueDeath(GameObject enemy, int lifeId)
        {
            queueDeath.Enqueue(new QueueDeath(enemy,lifeId));
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
