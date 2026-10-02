using System;
using KWC.Core;
using KWC.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KWC.GameSystem
{
    // One per scene. No singleton and no gameplay state stored in this component.
    [DisallowMultipleComponent]
    public sealed class GameManager : MonoBehaviour
    {
        public const string GameSceneName = "Game";

        [SerializeField] private GameConfig gameConfig;

        public GameConfig Config => gameConfig;
        public GameState CurrentState { get; private set; } = GameState.MainMenu;
        public event Action<GameState> GameStateChanged;

        public void StartGame()
        {
            if (CurrentState == GameState.MainMenu)
            {
                SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
            }
        }

        // Called after scene initialization. The scaffold initializes no gameplay yet.
        public bool BeginSession()
        {
            if (CurrentState != GameState.MainMenu)
            {
                return false;
            }

            if (gameConfig == null || !gameConfig.HasAllReferences)
            {
                Debug.LogError("KWC: assign a complete GameConfig before starting a session.", this);
                return false;
            }

            ChangeState(GameState.Playing);
            return true;
        }

        public bool EnterUpgrade()
        {
            if (CurrentState != GameState.Playing)
            {
                return false;
            }

            ChangeState(GameState.Upgrade);
            return true;
        }

        // Caller must first resolve the pending simultaneous-death/ending-order rules.
        // Do not wire competing Health callbacks straight into this method.
        public bool CompleteSession(GameState result)
        {
            if (result != GameState.Victory && result != GameState.GameOver)
            {
                throw new ArgumentOutOfRangeException(nameof(result));
            }

            if (CurrentState == GameState.MainMenu ||
                CurrentState == GameState.Victory || CurrentState == GameState.GameOver)
            {
                return false;
            }

            ChangeState(result);
            return true;
        }

        public void RestartGame()
        {
            if (CurrentState == GameState.Victory || CurrentState == GameState.GameOver)
            {
                SceneManager.LoadScene(GameSceneName, LoadSceneMode.Single);
            }
        }

        private void ChangeState(GameState next)
        {
            CurrentState = next;
            GameStateChanged?.Invoke(next);
        }

        // TODO(Game System): pause/resume and Upgrade continuation after design approval.
        // Intentionally no Time.timeScale policy or automatic wave scheduling here.
    }
}
