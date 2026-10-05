using UnityEngine;
using UnityEngine.InputSystem;

namespace KWC.GameSystem
{
    public class TestSpawnSchedulerDriver : MonoBehaviour
    {
        [SerializeField] private SpawnScheduler spawnScheduler;
        [SerializeField] private PrefabPool warningPool;
        [SerializeField] private int testFrameRate = 0;

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
            if (Keyboard.current == null || spawnScheduler == null)
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
