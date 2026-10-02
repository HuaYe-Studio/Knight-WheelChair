using KWC.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KWC.GameSystem
{
    // Temporary project-entry check. UI Owner replaces this with the real menu/HUD.
    public sealed class DevelopmentLauncher : MonoBehaviour
    {
        [SerializeField] private GameManager gameManager;
        [SerializeField] private bool beginSessionOnStart;

        private void Start()
        {
            if (beginSessionOnStart)
            {
                gameManager.BeginSession();
            }
        }

        private void OnGUI()
        {
            if (gameManager == null)
            {
                return;
            }

            GUILayout.BeginArea(new Rect(24, 24, 500, 220), GUI.skin.box);
            GUILayout.Label("KWC / Knight WheelChair - Project Scaffold");
            GUILayout.Label("Initialization stage. Gameplay is not implemented.");
            GUILayout.Label("State: " + gameManager.CurrentState);
            GUILayout.Space(12);

            if (gameManager.CurrentState == GameState.MainMenu)
            {
                if (GUILayout.Button("Start - load Game scaffold", GUILayout.Height(36)))
                {
                    gameManager.StartGame();
                }
            }
            else
            {
                GUILayout.Label("Owner modules are ready for implementation and wiring.");
                if (GUILayout.Button("Reload development scene", GUILayout.Height(36)))
                {
                    SceneManager.LoadScene(GameManager.GameSceneName, LoadSceneMode.Single);
                }
            }

            GUILayout.EndArea();
        }
    }
}
