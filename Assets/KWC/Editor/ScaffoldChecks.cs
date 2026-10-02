using System;
using System.IO;
using KWC.Core;
using KWC.Data;
using KWC.GameSystem;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KWC.Editor
{
    // Small integration checks for the scaffold, not tests for unimplemented gameplay.
    // SessionState survives the domain reloads when entering/leaving Play Mode.
    [InitializeOnLoad]
    public static class ScaffoldChecks
    {
        private const string Running = "KWC.Smoke.Running";
        private const string Phase = "KWC.Smoke.Phase";
        private const string Deadline = "KWC.Smoke.Deadline";
        private const string PreviousManager = "KWC.Smoke.PreviousManager";
        private const string RuntimeError = "KWC.Smoke.RuntimeError";

        static ScaffoldChecks()
        {
            if (SessionState.GetBool(Running, false))
            {
                Attach();
            }
        }

        public static void ValidateProject()
        {
            Require(Application.unityVersion == "2022.3.62f3c1", "Unexpected Unity version.");
            Require(EditorSettings.serializationMode == SerializationMode.ForceText, "Force Text is required.");
            Require(VersionControlSettings.mode == "Visible Meta Files", "Visible Meta Files is required.");
            Require(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline == null, "Expected Built-in pipeline.");
            Require(AssetDatabase.LoadAssetAtPath<TMPro.TMP_Settings>(
                "Assets/TextMesh Pro/Resources/TMP Settings.asset") != null, "Import TMP Essential Resources.");

            GameConfig config = AssetDatabase.LoadAssetAtPath<GameConfig>(ProjectSetup.ConfigPath);
            Require(config != null && config.HasAllReferences, "Missing configuration reference.");
            Require(config.AttributeGrowth.PlayerBase == config.PlayerBase, "S0 must use the same PlayerBase asset.");
            Require(config.Waves.Waves.Count == 10, "Expected ten wave definitions.");
            for (int i = 0; i < 10; i++)
            {
                WaveDefinition wave = config.Waves.Waves[i];
                Require(wave != null && wave.WaveIndex == i + 1, "Invalid wave ordering.");
                Require(wave.IsBossWave == (i == 9), "Only Wave 10 is the Boss wave.");
                Require(wave.SpawnInterval > 0 && wave.SpawnBatchCount > 0 && wave.E1Hp > 0,
                    "Invalid wave configuration.");
                if (i < 9)
                {
                    Require(wave.TryGetActualSpawnCount(out int count) && count > 0,
                        "Normal wave spawn count must be configured.");
                }
            }

            var scenes = EditorBuildSettings.scenes;
            Require(scenes.Length == 2 && scenes[0].enabled && scenes[1].enabled &&
                scenes[0].path == ProjectSetup.MenuScene && scenes[1].path == ProjectSetup.GameScene,
                "Build scene order must be MainMenu, Game.");

            foreach (var entry in scenes)
            {
                Scene scene = EditorSceneManager.OpenScene(entry.path);
                var managers = UnityEngine.Object.FindObjectsOfType<GameManager>();
                Require(managers.Length == 1 && managers[0].Config == config,
                    "Each scene needs exactly one configured GameManager.");
                Require(UnityEngine.Object.FindObjectsOfType<DevelopmentLauncher>().Length == 1,
                    "Missing development entry point.");
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        Require(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) == 0,
                            "Missing script in " + entry.path);
                    }
                }
            }

            EditorSceneManager.OpenScene(ProjectSetup.MenuScene);
            Debug.Log("KWC_STRUCTURE_PASS: version, settings, config references, scene wiring.");
        }

        // Command line only; omit -quit because this method exits after Play Mode checks.
        public static void StartPlayModeSmokeTest()
        {
            Require(Application.isBatchMode, "Run smoke checks from the documented batch command.");
            ValidateProject();
            SessionState.SetInt(Phase, 0);
            SessionState.SetString(RuntimeError, "");
            SessionState.SetFloat(Deadline, (float)EditorApplication.timeSinceStartup + 120f);
            SessionState.SetBool(Running, true);
            Attach();
            EditorApplication.isPlaying = true;
        }

        private static void Attach()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceived -= CaptureError;
            Application.logMessageReceived += CaptureError;
        }

        private static void CaptureError(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                SessionState.SetString(RuntimeError, message);
            }
        }

        private static void Tick()
        {
            try
            {
                Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Deadline, 0),
                    "Play Mode smoke check timed out.");
                Require(string.IsNullOrEmpty(SessionState.GetString(RuntimeError, "")),
                    SessionState.GetString(RuntimeError, ""));

                int phase = SessionState.GetInt(Phase, 0);
                if (phase == 3 && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    Finish(true, "KWC_PLAYMODE_PASS: MainMenu -> Game, state events, fresh restart, pool lifecycle.");
                    return;
                }

                if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
                {
                    return;
                }

                GameManager manager = UnityEngine.Object.FindObjectOfType<GameManager>();
                if (manager == null)
                {
                    return;
                }

                if (phase == 0 && SceneManager.GetActiveScene().name == "MainMenu")
                {
                    Require(manager.CurrentState == GameState.MainMenu, "Menu must begin in MainMenu.");
                    SessionState.SetInt(Phase, 1);
                    manager.StartGame();
                }
                else if (phase == 1 && SceneManager.GetActiveScene().name == "Game" &&
                    manager.CurrentState == GameState.Playing)
                {
                    TestPoolLifecycle();
                    int changes = 0;
                    Action<GameState> listener = state => changes++;
                    manager.GameStateChanged += listener;
                    Require(manager.EnterUpgrade(), "Wave end must enter Upgrade.");
                    Require(!manager.EnterUpgrade(), "Repeated Upgrade entry must be rejected.");
                    Require(manager.CompleteSession(GameState.GameOver), "Session completion failed.");
                    Require(!manager.CompleteSession(GameState.Victory), "Terminal state must be stable.");
                    Require(changes == 2, "Duplicate or missing state notifications.");
                    manager.GameStateChanged -= listener;
                    SessionState.SetInt(PreviousManager, manager.GetInstanceID());
                    SessionState.SetInt(Phase, 2);
                    manager.RestartGame();
                }
                else if (phase == 2 && SceneManager.GetActiveScene().name == "Game" &&
                    manager.CurrentState == GameState.Playing)
                {
                    Require(manager.GetInstanceID() != SessionState.GetInt(PreviousManager, 0),
                        "Restart must create a fresh scene manager.");
                    Require(UnityEngine.Object.FindObjectsOfType<GameManager>().Length == 1,
                        "Duplicate managers after restart.");
                    SessionState.SetInt(Phase, 3);
                    EditorApplication.isPlaying = false;
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void TestPoolLifecycle()
        {
            var template = new GameObject("Smoke Pool Template");
            var root = new GameObject("Smoke Pool");
            var otherRoot = new GameObject("Smoke Other Pool");
            var pool = root.AddComponent<PrefabPool>();
            var otherPool = otherRoot.AddComponent<PrefabPool>();
            ProjectSetup.SetReference(pool, "prefab", template);

            var first = pool.Rent(Vector3.one, Quaternion.identity,
                instance => Require(!instance.activeInHierarchy, "Initialization must precede activation."));
            Require(first.activeInHierarchy && first.transform.position == Vector3.one, "Rent placement failed.");
            Require(!otherPool.Return(first), "Another pool must not accept this object.");
            Require(pool.Return(first) && !pool.Return(first), "Double return must be rejected.");
            var second = pool.Rent(Vector3.zero, Quaternion.identity,
                instance => Require(!instance.activeInHierarchy, "Reuse must initialize while inactive."));
            Require(second == first && pool.BorrowedCount == 1, "Object was not reused.");
            pool.ReturnAll();
            Require(pool.BorrowedCount == 0 && !second.activeInHierarchy, "Cleanup failed.");

            bool failed = false;
            try
            {
                pool.Rent(Vector3.zero, Quaternion.identity,
                    instance => throw new InvalidOperationException("Expected initializer failure"));
            }
            catch (InvalidOperationException)
            {
                failed = true;
            }
            Require(failed && pool.BorrowedCount == 0, "Failed initialization leaked a rental.");
            Require(pool.Rent(Vector3.zero, Quaternion.identity, null) == first,
                "Failed initialization lost the reusable object.");
            pool.ReturnAll();
            UnityEngine.Object.Destroy(root);
            UnityEngine.Object.Destroy(otherRoot);
            UnityEngine.Object.Destroy(template);
        }

        private static void Finish(bool success, string message)
        {
            SessionState.SetBool(Running, false);
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= CaptureError;
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/playmode-result.txt", message + Environment.NewLine);
            if (success) Debug.Log(message);
            else Debug.LogError(message);
            // Exit after this editor update finishes, not from inside its callback.
            EditorApplication.delayCall += () => EditorApplication.Exit(success ? 0 : 1);
        }

        public static void BuildWindows()
        {
            ValidateProject();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MenuScene, ProjectSetup.GameScene },
                locationPathName = "Builds/Windows/KWC.exe",
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            });
            Require(report.summary.result == BuildResult.Succeeded, "Windows build failed.");
            Debug.Log("KWC_BUILD_PASS: Windows x86_64 development scaffold.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
