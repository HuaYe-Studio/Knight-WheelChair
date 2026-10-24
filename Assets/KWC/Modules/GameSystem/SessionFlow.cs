using KWC.Core;
using UnityEngine;

namespace KWC.GameSystem
{
    /// <summary>
    /// 场景内本局流程：校验引用、订阅波末/玩家死亡/状态事件，
    /// 在进入 Playing 时按“玩家初始化 → 敌人生命周期就绪 → 开始波次”的顺序组织调用，
    /// 并在 LateUpdate 处理玩家死亡（先停波，再提交 GameOver）。
    /// 它不拥有波次、敌人或战斗数据，只负责调用顺序与生命周期边界。
    /// </summary>
    public class SessionFlow:MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private SpawnScheduler spawnScheduler;
        [SerializeField] private PlayerInitializer player;
        [SerializeField] private TestPlayerContext testPlayerContext;

        [SerializeField] private TestPickupCollector testPickupCollector;
        [SerializeField] private EnemyLifeCycle enemyLifeCycle;

        private bool isPlayerDead = false;
        private int playerDiedLifeId = -1;
        private bool isInitialized = false;
        private bool hasStarted = false;

        private void Start()
        {
            if(isInitialized)
                HandleStarted(gameManager.CurrentState);
        }
        private void LateUpdate()
        {
            if (!isInitialized)
                return;

            if (!player.PlayerHealth.IsDead || !isPlayerDead)
                return;
            if(playerDiedLifeId != player.PlayerHealth.LifeId)
                return;

            spawnScheduler.StopWave();
            gameManager.CompleteSession(GameState.GameOver);
            isPlayerDead = false;
        }

        private void OnEnable()
        {
            if (gameManager == null || gameManager.Config == null || !gameManager.Config.HasAllReferences)
            {
                Debug.LogError("assign the game manager with config before starting playing.", this);
                return;
            }

            if (spawnScheduler==null || !spawnScheduler.Initialize(gameManager.Config))
            {
                Debug.LogError("assign the initialized spawn scheduler before starting a wave.", this);
                return;
            }

            if (testPlayerContext == null)
            {
                Debug.LogError("assign the test player context before starting playing.", this);
                return;
            }

            if (testPickupCollector == null || !testPickupCollector.Initialize(testPlayerContext))
            {
                Debug.LogError("assign the initialized pickup collector before starting playing.", this);
                return;
            }

            if (player == null || player.PlayerHealth == null || player.PlayerStats == null)
            {
                Debug.LogError("assign the player Initializer before starting a wave.", this);
                return;
            }

            spawnScheduler.WaveEnded -= HandleWaveEnded;
            spawnScheduler.WaveEnded += HandleWaveEnded;

            player.PlayerHealth.Died -= HandlePlayerDied;
            player.PlayerHealth.Died += HandlePlayerDied;

            gameManager.GameStateChanged -= HandleStarted;
            gameManager.GameStateChanged += HandleStarted;

            isPlayerDead = false;
            playerDiedLifeId = -1;

            isInitialized = true;
        }

        private void OnDisable()
        {
            if (spawnScheduler != null)
            {
                spawnScheduler.WaveEnded -= HandleWaveEnded;
            }

            if (player != null && player.PlayerHealth!=null)
            {
                player.PlayerHealth.Died -= HandlePlayerDied;
            }

            if (gameManager != null)
            {
                gameManager.GameStateChanged -= HandleStarted;
            }

            isInitialized = false;
            hasStarted = false;
        }

        private void HandleStarted(GameState gameState)
        {
            if(gameState != GameState.Playing)
                return;

            if(!isInitialized )
                return;

            if (hasStarted)
                return;

            if(!player.TryInitialize(gameManager.Config))
                return;

            if (enemyLifeCycle == null || !enemyLifeCycle.TryInitialize(gameManager.Config, testPlayerContext))
            {
                Debug.LogError("assign the initialized enemyLifeCycle before starting playing.", this);
                return;
            }

            spawnScheduler.InitializeWave();
            hasStarted = true;
            Debug.Log("Game system started successfully");
        }

        private void HandleWaveEnded()
        {
            if (isInitialized)
            {
                player.PlayerHealth.TryHeal(player.PlayerHealth.MaxHp,player.PlayerHealth.LifeId);
            }
        }

        private void HandlePlayerDied(int lifeId)
        {
            playerDiedLifeId = lifeId;
            isPlayerDead = true;
        }


    }
}
