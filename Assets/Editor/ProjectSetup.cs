using System;
using System.IO;
using Horizonte.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Horizonte.EditorTools
{
    /// <summary>Project configuration and builds (menu Horizonte). Builds also run from the command line in batch mode.</summary>
    public static class ProjectSetup
    {
        public const string BundleId = "com.lfaz.horizonte";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string PipelinePath = "Assets/Settings/UniversalRP.asset";
        const string VolumePath = "Assets/Settings/HorizonteVolume.asset";
        const string RendererPath = "Assets/Settings/UniversalRenderer3D.asset";
        const string MaterialFolder = "Assets/Resources/Materials";

        [MenuItem("Horizonte/Configurar proyecto")]
        public static void Run()
        {
            CreateIcon();
            ConfigurePlayer();
            ConfigurePipeline();
            CreateMaterials();
            BuildingBaker.Bake();
            CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("Horizonte: project configured.");
        }

        static void ConfigurePlayer()
        {
            PlayerSettings.productName = "Horizonte";
            PlayerSettings.companyName = "LF'AZ";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, BundleId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
            PlayerSettings.iOS.appleEnableAutomaticSigning = false;
            PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
            PlayerSettings.colorSpace = ColorSpace.Linear;

            // City builders are played sideways.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Icon/AppIcon.png");
            if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 739;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }

        const string IconPath = "Assets/Icon/AppIcon.png";

        /// <summary>Placeholder app icon (sky, sun and skyline) until a designed one replaces Assets/Icon/AppIcon.png.</summary>
        static void CreateIcon()
        {
            if (File.Exists(IconPath)) return;
            const int size = 1024;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var px = new Color[size * size];
            var top = new Color(.13f, .32f, .62f);
            var horizon = new Color(.98f, .67f, .38f);
            var city = new Color(.09f, .12f, .2f);
            var rng = new System.Random(7);
            var heights = new int[size];
            for (int x = 0; x < size;)
            {
                int w = rng.Next(50, 120), h = rng.Next(260, 560);
                if (x > 380 && x < 560) h = 700;
                for (int k = 0; k < w && x < size; k++, x++) heights[x] = h;
                x += 10;
            }
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float t = y / (float)size;
                var c = Color.Lerp(horizon, top, Mathf.SmoothStep(.15f, 1, t));
                float dx = x - 700, dy = y - 560;
                float sun = Mathf.Clamp01(1 - (Mathf.Sqrt(dx * dx + dy * dy) - 120) / 8);
                c = Color.Lerp(c, new Color(1, .93f, .7f), sun);
                if (y < heights[x]) c = city;
                if (y < 190) c = Color.Lerp(city, new Color(.2f, .35f, .25f), .5f);
                px[y * size + x] = c;
            }
            tex.SetPixels(px);
            Directory.CreateDirectory("Assets/Icon");
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(IconPath);
        }

        static void ConfigurePipeline()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null) throw new InvalidOperationException($"{PipelinePath} is missing.");
            pipeline.msaaSampleCount = 4;
            pipeline.supportsHDR = true;
            pipeline.shadowDistance = 260;
            pipeline.shadowCascadeCount = 4;
            pipeline.shadowDepthBias = .6f;
            pipeline.shadowNormalBias = .5f;
            var pipelineData = new SerializedObject(pipeline);
            pipelineData.FindProperty("m_SoftShadowsSupported").boolValue = true;
            pipelineData.ApplyModifiedPropertiesWithoutUndo();
            pipeline.renderScale = 1;
            EditorUtility.SetDirty(pipeline);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = pipeline;
            }
            QualitySettings.SetQualityLevel(current, false);
            RemoveAmbientOcclusion();
        }

        /// <summary>
        /// Screen-space ambient occlusion is left out: at a cost the phone can afford it showed as flickering grey blotches.
        /// Removes it from renderers set up by earlier versions.
        /// </summary>
        static void RemoveAmbientOcclusion()
        {
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null) throw new InvalidOperationException($"{RendererPath} is missing.");
            var ao = renderer.rendererFeatures.Find(f => f is ScreenSpaceAmbientOcclusion);
            if (ao == null) return;
            int index = renderer.rendererFeatures.IndexOf(ao);
            var data = new SerializedObject(renderer);
            data.FindProperty("m_RendererFeatures").DeleteArrayElementAtIndex(index);
            data.FindProperty("m_RendererFeatureMap").DeleteArrayElementAtIndex(index);
            data.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.RemoveObjectFromAsset(ao);
            UnityEngine.Object.DestroyImmediate(ao, true);
            EditorUtility.SetDirty(renderer);
            AssetDatabase.SaveAssets();
        }

        static void CreateMaterials()
        {
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();
            foreach (var e in Palette.Entries)
            {
                string path = $"{MaterialFolder}/{e.Name}.mat";
                var fresh = Palette.Create(e);
                var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (existing == null) AssetDatabase.CreateAsset(fresh, path);
                else
                {
                    existing.shader = fresh.shader;
                    existing.CopyPropertiesFromMaterial(fresh);
                    existing.shaderKeywords = fresh.shaderKeywords;
                    existing.renderQueue = fresh.renderQueue;
                    EditorUtility.SetDirty(existing);
                }
            }
        }

        static VolumeProfile CreateVolumeProfile()
        {
            AssetDatabase.DeleteAsset(VolumePath);
            VolumeProfile profile;
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, VolumePath);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1f);
            bloom.intensity.Override(.3f);
            bloom.scatter.Override(.6f);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(.45f);
            color.contrast.Override(18);
            color.saturation.Override(20);
            var split = profile.Add<SplitToning>(true);
            split.shadows.Override(new Color(.42f, .5f, .62f));
            split.highlights.Override(new Color(.62f, .55f, .44f));
            split.balance.Override(-15);
            var white = profile.Add<WhiteBalance>(true);
            white.temperature.Override(9);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.18f);
            vignette.smoothness.Override(.5f);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = new Color(.66f, .78f, .9f);

            var cameraGo = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" };
            var cam = cameraGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            cam.allowHDR = true;
            var camData = cameraGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.renderShadows = true;

            var sunGo = new GameObject("Sol", typeof(Light));
            var sun = sunGo.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, .93f, .8f);
            sun.intensity = 1.75f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .9f;
            sunGo.transform.rotation = Quaternion.Euler(47, -38, 0);
            sunGo.AddComponent<UniversalAdditionalLightData>();

            var volumeGo = new GameObject("Postproceso", typeof(Volume));
            var volume = volumeGo.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = CreateVolumeProfile();

            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            new GameObject("Juego", typeof(GameController));

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.56f, .66f, .82f);
            RenderSettings.ambientEquatorColor = new Color(.5f, .55f, .52f);
            RenderSettings.ambientGroundColor = new Color(.32f, .3f, .26f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .0013f;
            RenderSettings.fogColor = sky;
            RenderSettings.sun = sun;

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        // ---------------------------------------------------------------- builds

        [MenuItem("Horizonte/Generar proyecto de Xcode")]
        public static void BuildIosProject() => Build(BuildTarget.iOS, "Builds/iOS", BuildOptions.None);

        [MenuItem("Horizonte/Compilar prueba para Windows")]
        public static void BuildWindowsDev() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Horizonte.exe", BuildOptions.Development);

        static void Build(BuildTarget target, string path, BuildOptions options)
        {
            Run();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = path,
                target = target,
                options = options,
            });
            Debug.Log($"Horizonte build {target}: {report.summary.result}, {report.summary.totalErrors} errors, {report.summary.totalSize / (1024 * 1024)} MB");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
