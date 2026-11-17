using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RiftWard.EditorTools
{
    /// Batch-mode entry points: scene setup and Windows builds. Everything else is created from code at runtime.
    public static class BuildTools
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("RiftWard/Setup Project")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            IncludeShader("Standard");
            IncludeShader("Sprites/Default");
            PlayerSettings.companyName = "RiftWard";
            PlayerSettings.productName = "RiftWard";
            PlayerSettings.bundleVersion = RiftWard.Sim.Defs.Version;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            // The account service speaks plain HTTP on the LAN / home server.
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            AssetDatabase.SaveAssets();
            Debug.Log("RIFTWARD_SETUP_DONE");
        }

        static void IncludeShader(string name)
        {
            var shader = Shader.Find(name);
            var settings = AssetDatabase.LoadAssetAtPath<GraphicsSettings>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(settings);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
        }

        [MenuItem("RiftWard/Build Windows")]
        public static void BuildWindows()
        {
            Setup();
            string output = System.Environment.GetEnvironmentVariable("RIFTWARD_BUILD_DIR");
            if (string.IsNullOrEmpty(output)) output = "Build/Windows";
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(output, "RiftWard.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });
            Debug.Log("RIFTWARD_BUILD " + report.summary.result + " size=" + report.summary.totalSize);
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
