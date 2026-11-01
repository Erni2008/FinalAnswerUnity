using System;
using System.IO;
using FinalAnswer.App;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace FinalAnswer.EditorTools
{
    /// <summary>
    /// Reproducible project configuration. Run from the menu or headless:
    /// Unity -batchmode -projectPath unity/FinalAnswer -executeMethod FinalAnswer.EditorTools.ProjectSetup.Run -quit
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/FinalAnswerShell/";
        const string SettingsFolder = "Assets/FinalAnswerShell/Settings";
        const string RendererPath = SettingsFolder + "/FinalAnswer_Renderer.asset";
        const string PipelinePath = SettingsFolder + "/FinalAnswer_URP.asset";
        const string BootstrapScene = "Assets/FinalAnswerShell/Scenes/Shell.unity";
        const string FontPath = "Assets/FinalAnswerShell/Fonts/NotoSans-Regular.ttf";

        [MenuItem("Final Answer Shell/Configure Project")]
        public static void Run()
        {
            ConfigurePlayer();
            ConfigurePipeline();
            AssetDatabase.Refresh();
            // Re-import so ImportRules applies even to files that arrived before the postprocessor compiled.
            foreach (var folder in new[] { "Assets/FinalAnswerShell/Art", "Assets/FinalAnswerShell/Audio" })
                AssetDatabase.ImportAsset(folder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            var catalog = BuildCatalog();
            CreateBootstrapScene(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalAnswer] Project configured");
        }

        [MenuItem("Final Answer Shell/Build macOS")]
        public static void BuildMacOS() => Build(BuildTarget.StandaloneOSX, "Builds/macOS-Shell/FinalAnswer.app");

        /// <summary>Development player: F1 solo sandbox and F3 debug overlay are enabled (Debug.isDebugBuild).</summary>
        [MenuItem("Final Answer Shell/Build macOS (Developer)")]
        public static void BuildMacOSDeveloper() => Build(BuildTarget.StandaloneOSX, "Builds/macOS-Shell-Dev/FinalAnswer Dev.app", BuildOptions.Development);

        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows-Shell/FinalAnswer.exe");

        static void ConfigurePlayer()
        {
            // Non-destructive inside FinalAnswerUnity: identity, input handling and color space stay as configured.
            PlayerSettings.runInBackground = true;
        }

        static void ConfigurePipeline()
        {
            // The project already has a tuned URP asset (Bloom/ACES/SSAO for the 3D machine): never replace it.
            if (GraphicsSettings.defaultRenderPipeline != null) return;
            Directory.CreateDirectory(SettingsFolder);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            var current = QualitySettings.GetQualityLevel();
            for (var i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
        }

        const string CatalogPath = "Assets/FinalAnswerShell/Settings/ArtCatalog.asset";
        static readonly string[] CatalogFolders = { "Art", "Audio", "Fonts", "Localization", "Data", "UI/Shaders" };

        /// <summary>Serialized references for every shell asset; keys = path under Assets/FinalAnswer without extension (TextAssets keep it).</summary>
        public static FinalAnswer.UI.ArtCatalog BuildCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<FinalAnswer.UI.ArtCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<FinalAnswer.UI.ArtCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            var entries = new System.Collections.Generic.List<FinalAnswer.UI.ArtCatalog.Entry>();
            foreach (var folder in CatalogFolders)
            {
                foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets/FinalAnswerShell/" + folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path)) continue;
                    var asset = AssetDatabase.LoadMainAssetAtPath(path);
                    if (asset == null) continue;
                    var relative = path.Substring("Assets/FinalAnswerShell/".Length);
                    var key = asset is TextAsset ? relative : relative.Substring(0, relative.Length - Path.GetExtension(relative).Length);
                    if (asset is Texture2D || asset is AudioClip || asset is Font || asset is TextAsset || asset is Shader)
                        entries.Add(new FinalAnswer.UI.ArtCatalog.Entry { key = key, asset = asset });
                }
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            catalog.SetEntries(entries);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log("[FinalAnswer] Art catalog: " + entries.Count + " assets");
            return catalog;
        }

        static void CreateBootstrapScene(FinalAnswer.UI.ArtCatalog catalog)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BootstrapScene));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("AppRoot");
            var app = root.AddComponent<AppRoot>();
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (font == null) throw new InvalidOperationException("Missing UI font " + FontPath);
            var serialized = new SerializedObject(app);
            serialized.FindProperty("uiFont").objectReferenceValue = font;
            // Reload by path: the instance from BuildCatalog may not survive NewScene's unload.
            var catalogAsset = AssetDatabase.LoadAssetAtPath<FinalAnswer.UI.ArtCatalog>(CatalogPath);
            serialized.FindProperty("art").objectReferenceValue = catalogAsset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (catalogAsset == null || new SerializedObject(app).FindProperty("art").objectReferenceValue == null)
                throw new InvalidOperationException("ArtCatalog was not assigned to AppRoot");
            EditorSceneManager.SaveScene(scene, BootstrapScene);
            // Shell first (it is the entry point); keep every existing scene of the project after it.
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(BootstrapScene, true) };
            foreach (var existing in EditorBuildSettings.scenes) if (existing.path != BootstrapScene) scenes.Add(existing);
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static string[] AllScenes()
        {
            var list = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes) if (scene.enabled) list.Add(scene.path);
            if (!list.Contains(BootstrapScene)) list.Insert(0, BootstrapScene);
            return list.ToArray();
        }

        static void Build(BuildTarget target, string output, BuildOptions options = BuildOptions.None)
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = AllScenes(),
                locationPathName = output,
                target = target,
                options = options,
            });
            var summary = report.summary;
            Debug.Log("[FinalAnswer] Build " + summary.result + " · " + summary.totalErrors + " errors · " + summary.outputPath);
            if (summary.result != BuildResult.Succeeded && UnityEngine.Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
