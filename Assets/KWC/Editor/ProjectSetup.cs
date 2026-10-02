using System;
using System.IO;
using KWC.Data;
using KWC.GameSystem;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace KWC.Editor
{
    // One-time initializer. It refuses to overwrite an existing scaffold.
    public static class ProjectSetup
    {
        public const string Root = "Assets/KWC";
        public const string MenuScene = Root + "/Scenes/MainMenu.unity";
        public const string GameScene = Root + "/Scenes/Game.unity";
        public const string ConfigPath = Root + "/Data/DefaultGameConfig.asset";
        public const string ManagerPrefab = Root + "/Prefabs/GameSystem/GameManager.prefab";

        public static void CreateScaffold()
        {
            if (File.Exists(ConfigPath) || File.Exists(MenuScene) || File.Exists(GameScene))
            {
                throw new InvalidOperationException("Scaffold already exists. Edit assets normally; do not regenerate them.");
            }

            Directory.CreateDirectory(Root + "/Data");
            Directory.CreateDirectory(Root + "/Scenes");
            Directory.CreateDirectory(Root + "/Prefabs/GameSystem");
            AssetDatabase.Refresh();

            EditorSettings.serializationMode = SerializationMode.ForceText;
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode3D;
            EditorSettings.projectGenerationRootNamespace = "KWC";
            VersionControlSettings.mode = "Visible Meta Files";
            GraphicsSettings.defaultRenderPipeline = null;
            PlayerSettings.productName = "KWC";
            PlayerSettings.companyName = "KWC Team";

            var player = Create<PlayerBaseConfig>("PlayerBase");
            var weapon = Create<WeaponConfig>("BasicWeapon");
            var enemy = Create<Enemy1Config>("Enemy1");
            var boss = Create<BossConfig>("KnightWheelChair");
            var combat = Create<CombatRulesConfig>("CombatRules");
            var growth = Create<AttributeGrowthConfig>("AttributeGrowth");
            var waves = Create<WaveSetConfig>("Waves");
            var game = Create<GameConfig>("DefaultGameConfig");
            SetReference(growth, "playerBase", player);
            SetReference(game, "playerBase", player);
            SetReference(game, "weapon", weapon);
            SetReference(game, "enemy1", enemy);
            SetReference(game, "boss", boss);
            SetReference(game, "combatRules", combat);
            SetReference(game, "attributeGrowth", growth);
            SetReference(game, "waves", waves);

            var managerObject = new GameObject("GameManager");
            SetReference(managerObject.AddComponent<GameManager>(), "gameConfig", game);
            var prefab = PrefabUtility.SaveAsPrefabAsset(managerObject, ManagerPrefab);
            UnityEngine.Object.DestroyImmediate(managerObject);

            CreateScene(MenuScene, prefab, false);
            CreateScene(GameScene, prefab, true);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScene, true),
                new EditorBuildSettingsScene(GameScene, true)
            };

            // TMP essentials are imported separately with Unity's -importPackage flag.
            // ImportPackage() is asynchronous and must not be followed by batch -quit.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(MenuScene);
            Debug.Log("KWC_SCAFFOLD_CREATED: two scenes, one prefab, eight config assets.");
        }

        private static T Create<T>(string name) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, Root + "/Data/" + name + ".asset");
            return asset;
        }

        public static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void CreateScene(string path, GameObject managerPrefab, bool gameScene)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Preview Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 5, -10);
            cameraObject.transform.rotation = Quaternion.Euler(25, 0, 0);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.075f, 0.085f, 0.11f);

            if (gameScene)
            {
                var lightObject = new GameObject("Directional Light", typeof(Light));
                lightObject.GetComponent<Light>().type = LightType.Directional;
                lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
            }

            var manager = ((GameObject)PrefabUtility.InstantiatePrefab(managerPrefab))
                .GetComponent<GameManager>();
            var launcher = new GameObject("Development Launcher").AddComponent<DevelopmentLauncher>();
            SetReference(launcher, "gameManager", manager);
            var serialized = new SerializedObject(launcher);
            serialized.FindProperty("beginSessionOnStart").boolValue = gameScene;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, path);
        }
    }
}
