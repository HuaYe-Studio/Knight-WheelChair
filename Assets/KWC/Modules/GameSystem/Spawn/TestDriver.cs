using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace KWC.GameSystem
{
    /// <summary>
    /// 开发期测试按键驱动：F1 开波、F2 停波、F3 预警池往返测试、
    /// F4 击杀（Inspector 开关决定单个/全部）、F5 迟到回收测试。不属于正式玩法。
    /// </summary>
    public class TestDriver : MonoBehaviour
    {
        [SerializeField] private SpawnScheduler spawnScheduler;
        [SerializeField] private EnemyLifeCycle enemyLifeCycle;
        [SerializeField] private PrefabPool warningPool;
        [SerializeField] private int testFrameRate = 0;
        [SerializeField] private bool wantKillAllEnemies = false;

        private void Start()
        {
            if (testFrameRate > 0)
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = testFrameRate;
            }
        }

        private void Update()
        {
            if (Keyboard.current == null || spawnScheduler == null || enemyLifeCycle == null)
                return;

            if (Keyboard.current.f1Key.wasPressedThisFrame)
            {
                spawnScheduler.InitializeWave();
            }

            if (Keyboard.current.f2Key.wasPressedThisFrame)
            {
                spawnScheduler.StopWave();
            }

            if (Keyboard.current.f3Key.wasPressedThisFrame)
            {
                TestPr2();
            }

            if (Keyboard.current.f4Key.wasPressedThisFrame)
            {
                if (!wantKillAllEnemies)
                {
                    enemyLifeCycle.TestKillOneEnemy();
                }
                else
                {
                    enemyLifeCycle.TestKillAllEnemies();
                }
            }

            if (Keyboard.current.f5Key.wasPressedThisFrame)
            {
                enemyLifeCycle.TestRecycle();
            }
        }

        private void TestPr2()
        {
            if (warningPool == null)
                return;

            warningPool.Rent(Vector3.zero, Quaternion.identity, go1 =>
            {
                var warning1 = go1.GetComponent<SpawnWarning>();
                warning1.InitializeWarning(0.5f, () =>
                {
                    Debug.Log("Round1 Done");

                    warningPool.Return(go1);
                    warningPool.Rent(Vector3.zero, Quaternion.identity, go2 =>
                    {
                        var warning2 = go2.GetComponent<SpawnWarning>();
                        Debug.Log("Same:" + (go1 == go2));
                        warning2.InitializeWarning(0.5f, () =>
                        {
                            Debug.Log("Round2 Done");
                            warningPool.Return(go2);
                        });
                    });
                });
            });
        }
    }
}
