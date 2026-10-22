using UnityEngine;
using KWC.Data;
using KWC.Enemy;
using KWC.Combat;
using System.Collections.Generic;
using KWC.Core;
using UnityEngine.Serialization;

namespace KWC.GameSystem
{
    /// <summary>
    /// 敌人本局生命周期：从对象池生成 Enemy1、初始化其 Combat 组件、接收死亡上报，
    /// 并在 LateUpdate 统一执行“先掉落、后回收”。同时负责波末回收敌人与拾取物。
    /// 未就绪时 TryGenerateEnemy1 返回 false，不会创建半初始化对象。
    /// </summary>
    public class EnemyLifeCycle:MonoBehaviour
    {
        [Header("敌人接线引用")]
        [SerializeField] private PrefabPool enemyPool;
        private GameConfig enemyConfig;
        private IPlayerContext playerContext;

        [Header("战斗接线引用")]
        [SerializeField]private PlayerInitializer player;
        [SerializeField] private PrefabPool expOrbPool;
        [SerializeField] private PrefabPool healthPackPool;

        private List<GameObject> livedEnemy = new List<GameObject>();
        private Queue<QueueDeath> queueDeath = new Queue<QueueDeath>();

        /// <summary>由 TryInitialize 置位；调用方应按只读使用，写入方只有本组件。</summary>
        public bool isInitialized= false;

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

        /// <summary>
        /// 由 EnemyCombatBridge 在敌人进入 Dead 时调用；只入队，不立即回收。
        /// 处理时再次校验 lifeId，迟到或跨生命的上报会被拒绝。
        /// </summary>
        public void EnqueueDeath(GameObject enemy, int lifeId)
        {
            queueDeath.Enqueue(new QueueDeath(enemy,lifeId));
        }

        private void LateUpdate()
        {
            while (queueDeath.Count > 0)
            {
                var death = queueDeath.Dequeue();
                if (death.enemy == null)
                {
                    livedEnemy.Remove(death.enemy);
                    continue;
                }

                try
                {
                    EnemyDrops E1Drop;
                    if (death.enemy.TryGetComponent<EnemyDrops>(out E1Drop))
                        E1Drop.TryDrop(death.lifeId);
                }
                finally
                {
                    RecycleEnemy(death.enemy, death.lifeId);
                }
            }
        }

        /// <summary>
        /// 由流程层在玩家就绪后调用：校验配置、三个对象池和敌人 Prefab 的必需组件。
        /// 会先清除 isInitialized，校验失败时保持未就绪，不会生成敌人。
        /// </summary>
        public bool TryInitialize(GameConfig E1Config,IPlayerContext context)
        {
            isInitialized = false;

            #region 引用守卫

            if (E1Config == null || context == null)
            {
                Debug.LogError("初始化注入为空",this);
                return false;
            }

            if (E1Config.Enemy1==null || E1Config.CombatRules == null)
            {
                Debug.LogError("assign the config before starting a wave.", this);
                return false;
            }

            if (enemyPool == null)
            {
                Debug.LogError("assign the enemy pool before starting a wave.", this);
                return false;
            }

            if (enemyPool.Prefab == null)
            {
                Debug.LogError("assign the prefab in enemy pool before starting a wave.", this);
                return false;
            }

            if (expOrbPool == null)
            {
                Debug.LogError("assign the expOrbPool before starting a wave.", this);
                return false;
            }

            if (expOrbPool.Prefab == null)
            {
                Debug.LogError("assign the prefab in expOrbPool before starting a wave.", this);
                return false;
            }

            if (healthPackPool == null)
            {
                Debug.LogError("assign the healthPackPool before starting a wave.", this);
                return false;
            }

            if (healthPackPool.Prefab == null)
            {
                Debug.LogError("assign the prefab in healthPackPool before starting a wave.", this);
                return false;
            }

            if (player == null || !player.IsReady)
            {
                Debug.LogError("ensure the player is ready before starting a wave.", this);
                return false;
            }

            #endregion



            enemyConfig = E1Config;
            playerContext = context;

            #region 组件守卫

            GameObject enemy = enemyPool.Prefab;

            if (!enemy.TryGetComponent<Enemy1Controller>(out _))
            {
                Debug.LogError("need Enemy1Controller component before starting a wave.", this);
                return false;
            }

            if (!enemy.TryGetComponent<SimpleMovementHandler>(out _))
            {
                Debug.LogError("need SimpleMovementHandler component before starting a wave.", this);
                return false;
            }

            if (!enemy.TryGetComponent<PhysicsContactSource>(out _))
            {
                Debug.LogError("need PhysicsContactSource component before starting a wave.", this);
                return false;
            }

            if (!enemy.TryGetComponent<EnemyCombatBridge>(out _))
            {
                Debug.LogError("need EnemyCombatBridge component before starting a wave.", this);
                return false;
            }

            if(!enemy.TryGetComponent<Health>(out _))
            {
                Debug.LogError("need Health component before starting a wave.", this);
                return false;
            }

            if (!enemy.TryGetComponent<EnemyDrops>(out _))
            {
                Debug.LogError("need EnemyDrops component before starting a wave.", this);
                return false;
            }

            GameObject expOrb = expOrbPool.Prefab;
            if (!expOrb.TryGetComponent<ExpOrb>(out _))
            {
                Debug.LogError("need ExpOrb component before starting a wave.", this);
                return false;
            }

            GameObject healthPack = healthPackPool.Prefab;
            if (!healthPack.TryGetComponent<HealthPack>(out _))
            {
                Debug.LogError("need HealthPack component before starting a wave.", this);
                return false;
            }

            #endregion

            isInitialized = true;
            return true;
        }

        /// <summary>
        /// 从敌人池取出并初始化一个 Enemy1；成功后计入存活列表并返回 true。
        /// 由 SpawnScheduler 的预警完成回调调用，HP/Attack 来自当前波次定义。
        /// </summary>
        public bool TryGenerateEnemy1(out GameObject enemyGo,Vector3 position,Quaternion rotation,float hp,float attack)
        {
            if (!isInitialized)
            {
                enemyGo =  null;
                return false;
            }
            enemyGo = enemyPool.Rent(position,rotation, (go)=> InitializeEnemy1(go,hp,attack));

            if(enemyGo==null)
                return false;

            livedEnemy.Add(enemyGo);
            return true;
        }

        // 顺序不可交换：controller.Initialize 先推进 LifeId，Health 才能用新编号建立生命；
        // EnemyDrops 依赖 Health 已初始化；桥接最后订阅，避免收到上一条命的事件。
        private void InitializeEnemy1(GameObject enemyGo,float hp,float attack)
        {
            var controller = enemyGo.GetComponent<Enemy1Controller>();
            var bridge     = enemyGo.GetComponent<EnemyCombatBridge>();
            var contact    = enemyGo.GetComponent<PhysicsContactSource>();
            var health     = new FactHealthSource(hp);
            var E1Health   = enemyGo.GetComponent<Health>();
            var E1Drop     = enemyGo.GetComponent<EnemyDrops>();

            contact.Initialize(playerContext.EntityTransform);

            controller.Initialize(
                playerContext,
                enemyGo.GetComponent<SimpleMovementHandler>(),
                contact,
                bridge,
                enemyConfig.Enemy1,
                health,
                hp,
                attack
            );

            E1Health.Initialize(hp,hp,controller.LifeId);
            E1Drop.Initialize(E1Health,false,enemyConfig.CombatRules,expOrbPool,healthPackPool);
            bridge.Initialize(controller,E1Health,player,this);
        }

        /// <summary>
        /// 波末/中止清理：复位并回收所有在场敌人，归还经验球与血包，清空死亡队列。
        /// 不触发掉落、经验或击杀计数。
        /// </summary>
        public void ClearEnemySpawnState()
        {
            queueDeath.Clear();

            if (enemyPool != null)
            {
                foreach (var enemy in livedEnemy)
                {
                    if(enemy == null)
                        continue;

                    EnemyReset(enemy);
                }
            }

            if (expOrbPool != null)
            {
                expOrbPool.ReturnAll();
            }

            if (healthPackPool != null)
            {
                healthPackPool.ReturnAll();
            }

            livedEnemy.Clear();
        }

        /// <summary>测试用：对存活列表第一个敌人造成致死伤害。正式战斗接入后删除。</summary>
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

        /// <summary>测试用：对当前存活列表中的敌人逐个造成致死伤害。正式战斗接入后删除。</summary>
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

        /// <summary>测试用：用上一条命的编号尝试回收，验证迟到回调被拒绝。</summary>
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

        /// <summary>
        /// 按 lifeId 校验后回收单个敌人；编号不匹配（迟到回调）或不在存活列表时返回 false。
        /// 回收前不执行掉落；掉落由 LateUpdate 在处理死亡队列时先调用 EnemyDrops。
        /// </summary>
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

        // 复位顺序：先清接触事实，再让控制器推进生命编号并复位状态机，最后归还池。
        private void EnemyReset(GameObject enemy)
        {
            enemy.GetComponent<PhysicsContactSource>().ResetContact();
            enemy.GetComponent<Enemy1Controller>().ResetForReuse();
            enemyPool.Return(enemy);
        }

        /// <summary>
        /// 当前仍在存活列表中的敌人数量；已死亡但尚未在 LateUpdate 回收的也计入，
        /// 因此可作为波次提前结束的“已全部回收”判定。
        /// </summary>
        public int GetLivedEnemyCount()
        {
            return livedEnemy.Count;
        }

    }
}
