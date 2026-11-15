using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace Fnmw.XR.EditorTools
{
    /// <summary>
    /// One-click Quest setup and build. Menu: Fnmw > Quest > ...; batch mode:
    /// <c>-executeMethod Fnmw.XR.EditorTools.QuestSetup.Configure</c> and <c>... .BuildApk</c>.
    ///
    /// Configure is idempotent. It only touches the Android target: OpenXR as the Android XR loader, the
    /// Meta Quest feature (Quest 2 and newer), the Oculus Touch controller profile, multiview, and the
    /// Android player settings Meta requires. Standalone (desktop) gets no XR loader, so the desktop
    /// build never starts XR and plays exactly as before.
    /// </summary>
    public static class QuestSetup
    {
        public const string ApkPath = "Builds/Quest/FiveNightsAtMarshallWace-Quest.apk";
        public const string AndroidId = "com.fnmw.fivenights";
        const string OpenXRLoader = "UnityEngine.XR.OpenXR.OpenXRLoader";

        [MenuItem("Fnmw/Quest/Configure project for Quest")]
        public static void Configure()
        {
            bool android = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);
            ConfigurePlayer();
            if (!android)
            {
                // OpenXR keeps no Android settings (features, render mode) until the Android module exists, and XR
                // Plug-in Management will not assign a loader to it. Nothing else to do yet; BuildApk runs this again.
                AssetDatabase.SaveAssets();
                Debug.LogWarning("[QuestSetup] Android Build Support is not installed for this Editor: set the Android player settings only. " +
                                 "Install it (Unity Hub > Installs > 6000.6.9f1 > Add modules), then run Fnmw > Quest > Configure project for Quest.");
                return;
            }
            ConfigureXrManagement();
            ConfigureOpenXr();
            AssetDatabase.SaveAssets();
            Debug.Log("[QuestSetup] configured: OpenXR loader on Android, Meta Quest feature + Oculus Touch profile, multiview, Android player settings. Standalone has no XR loader.");
        }

        static XRGeneralSettingsPerBuildTarget PerTarget()
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget s);
            if (s) return s;
            var guids = AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget");
            if (guids.Length > 0) s = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (!s)
            {
                Directory.CreateDirectory("Assets/XR");
                s = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(s, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, s, true);
            return s;
        }

        static void ConfigureXrManagement()
        {
            var per = PerTarget();
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                if (!per.HasSettingsForBuildTarget(group)) per.CreateDefaultSettingsForBuildTarget(group);
                if (!per.HasManagerSettingsForBuildTarget(group)) per.CreateDefaultManagerSettingsForBuildTarget(group);
            }
            var android = per.SettingsForBuildTarget(BuildTargetGroup.Android);
            android.InitManagerOnStart = true;
            if (!android.Manager.activeLoaders.Any(l => l && l.GetType().FullName == OpenXRLoader))
                if (!XRPackageMetadataStore.AssignLoader(android.Manager, OpenXRLoader, BuildTargetGroup.Android))
                    throw new Exception("could not assign the OpenXR loader to Android");
            // Desktop stays flat: no loader, no XR start.
            var desk = per.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            foreach (var l in desk.Manager.activeLoaders.ToList())
                if (l) XRPackageMetadataStore.RemoveLoader(desk.Manager, l.GetType().FullName, BuildTargetGroup.Standalone);
            desk.InitManagerOnStart = false;
            EditorUtility.SetDirty(android); EditorUtility.SetDirty(android.Manager);
            EditorUtility.SetDirty(desk); EditorUtility.SetDirty(desk.Manager);
            EditorUtility.SetDirty(per);
        }

        static void ConfigureOpenXr()
        {
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var s = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (!s) throw new Exception("no OpenXR settings for Android");
            s.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;   // multiview on Quest
            var quest = s.GetFeature<MetaQuestFeature>();
            if (!quest) throw new Exception("Meta Quest feature missing from OpenXR");
            quest.enabled = true;
            var touch = s.GetFeature<OculusTouchControllerProfile>();
            if (!touch) throw new Exception("Oculus Touch controller profile missing from OpenXR");
            touch.enabled = true;
            EditorUtility.SetDirty(quest); EditorUtility.SetDirty(touch); EditorUtility.SetDirty(s);
        }

        /// <summary>
        /// Compiles the headset/Touch pose source for QS telemetry into Android builds. It only chooses where poses
        /// come from: the sender still needs qs.json and the candidate's consent before anything is sent.
        /// </summary>
        public const string QsXrDefine = "FNMW_QS_XR";

        static void AddDefine(NamedBuildTarget target, string define)
        {
            var defines = PlayerSettings.GetScriptingDefineSymbols(target)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList();
            if (defines.Contains(define)) return;
            defines.Add(define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
        }

        static void ConfigurePlayer()
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, AndroidId);
            AddDefine(android, QsXrDefine);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;   // Horizon OS (Quest 2 included) is Android 12L / API 32+
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;   // installed with Unity's Android module; fine for sideloading (minSdk 32 keeps Quest 2)
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;   // OpenXR validation for Unity 6
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            // Meta's store/release-channel validator rejects anything else
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        }

        /// <summary>
        /// Release signing for store/release-channel uploads (Meta rejects debug-signed APKs). Set FNMW_KEYSTORE,
        /// FNMW_KEYSTORE_PASS and FNMW_KEY_ALIAS (FNMW_KEY_PASS defaults to the keystore password) before building;
        /// without them the APK is debug-signed, which is fine for sideloading. FNMW_VERSION_CODE bumps the version code.
        /// </summary>
        static void ConfigureSigning()
        {
            string ks = Environment.GetEnvironmentVariable("FNMW_KEYSTORE");
            if (string.IsNullOrEmpty(ks)) { PlayerSettings.Android.useCustomKeystore = false; return; }
            string pass = Environment.GetEnvironmentVariable("FNMW_KEYSTORE_PASS");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = ks;
            PlayerSettings.Android.keystorePass = pass;
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("FNMW_KEY_ALIAS");
            PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("FNMW_KEY_PASS") ?? pass;
            if (int.TryParse(Environment.GetEnvironmentVariable("FNMW_VERSION_CODE"), out var code)) PlayerSettings.Android.bundleVersionCode = code;
        }

        /// <summary>
        /// Puts the signing settings back to "debug-signed, no keystore" after a build, so the machine-specific
        /// keystore path and alias never get saved into ProjectSettings.asset and committed.
        /// </summary>
        public static void ClearSigning()
        {
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.Android.keystoreName = "";
            PlayerSettings.Android.keystorePass = "";
            PlayerSettings.Android.keyaliasName = "";
            PlayerSettings.Android.keyaliasPass = "";
        }

        [MenuItem("Fnmw/Quest/Build APK")]
        public static void BuildApk()
        {
            bool batch = Application.isBatchMode;
            try
            {
                if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                    throw new Exception("Android Build Support is not installed for this Editor (Unity Hub > Installs > 6000.6.9f1 > Add modules)");
                Configure();
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
                Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
                var scenes = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
                bool dev = Environment.GetCommandLineArgs().Contains("-development");
                ConfigureSigning();
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes, locationPathName = ApkPath, target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android,
                    options = dev ? BuildOptions.Development : BuildOptions.None,
                });
                ClearSigning();
                AssetDatabase.SaveAssets();
                if (report.summary.result != BuildResult.Succeeded)
                    throw new Exception($"build {report.summary.result}: {report.summary.totalErrors} errors");
                Debug.Log($"[QuestSetup] built {ApkPath} ({report.summary.totalSize / (1024 * 1024)} MB). Install: adb install -r \"{ApkPath}\"");
                if (batch) EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                ClearSigning();
                Debug.LogError("[QuestSetup] " + ex.Message);
                if (batch) EditorApplication.Exit(1);
                else throw;
            }
        }
    }
}
