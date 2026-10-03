using UnityEngine;
using UnityEngine.InputSystem;


namespace KWC.GameSystem
{
    public class TestWarningTrigger: MonoBehaviour
    {
        private GameObject rentedWarning;
        [SerializeField]PrefabPool warningPool;

        private void Update()
        {
            if (Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                warningPool.Rent(new Vector3(0, 0, 0), Quaternion.identity, go =>
                {
                    var spawnWarning = go.GetComponent<SpawnWarning>();
                    rentedWarning = go;
                    spawnWarning.InitializeWarning(0.5f, () =>
                    {
                        Debug.Log("Spawn warning Done,enemy is coming!");
                        warningPool.Return(go);
                        rentedWarning = null;
                    });
                });
            }
            if (Keyboard.current.cKey.wasPressedThisFrame)
            {
                if (rentedWarning == null)
                    return;
                rentedWarning.GetComponent<SpawnWarning>().CancelWarning();
                warningPool.Return(rentedWarning);
                rentedWarning = null;
            }
        }
    }
}
