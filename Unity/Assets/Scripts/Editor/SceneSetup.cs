using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Fnmw.EditorTools
{
    /// <summary>Recreates Assets/Scenes/Main.unity (just a GameBootstrap) and makes it the only build scene.</summary>
    public static class SceneSetup
    {
        public const string MainScene = "Assets/Scenes/Main.unity";

        [MenuItem("Fnmw/Recreate Main Scene")]
        public static void CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("GameBootstrap").AddComponent<GameBootstrap>();
            EditorSceneManager.SaveScene(scene, MainScene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            Debug.Log("[SceneSetup] wrote " + MainScene);
        }
    }
}
