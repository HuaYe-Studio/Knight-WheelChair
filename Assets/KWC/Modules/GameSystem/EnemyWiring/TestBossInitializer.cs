using UnityEngine;
using KWC.Enemy;
using KWC.Data;

namespace KWC.GameSystem
{
    //暂时的脚本，等待其余owner完善相关逻辑接入该初始化
    //隔壁敌人初始化脚本已经并入直接波次调度
    public class TestBossInitializer: MonoBehaviour
    {
        [SerializeField] private GameObject bossGo;
        [SerializeField] private TestPlayerContext playerContext;
        [SerializeField] private BossConfig knightWheelChairConfig;
        [SerializeField] private int testSeed = 12345;

        private void Start()
        {
            var boss = bossGo.GetComponent<BossController>();
            boss.Initialize(
                playerContext,
                bossGo.GetComponent<SimpleMovementHandler>(),
                bossGo.GetComponent<LogOnlyCombatBridge>(),
                new NoWallCheck(),                                    // 待 Map 交付
                new RandomWanderPointSource(testSeed),
                knightWheelChairConfig,                               // Data/KnightWheelChair.asset
                new FactHealthSource(knightWheelChairConfig.BossHp),
                knightWheelChairConfig.BossHp,
                testSeed
            );
        }
    }
}
