#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.XR.Management;

/// <summary>
/// Outil de configuration en 1 clic du projet VR 360 Kiosque :
///  - Réglages Player Android (IL2CPP / ARM64 / API 24+ / paysage / Input System "Both")
///  - Activation du loader Google Cardboard dans XR Plug-in Management (Android)
///  - Génération et sauvegarde de la scène Assets/Scenes/VR360_Kiosk.unity
///  - Ajout de la scène dans les Build Settings
/// </summary>
public static class VRSceneSetupHelper
{
    private const string ScenesFolder = "Assets/Scenes";
    private const string MaterialsFolder = "Assets/Materials";
    private const string XRFolder = "Assets/XR";
    private const string ScenePath = ScenesFolder + "/VR360_Kiosk.unity";
    private const string MaterialPath = MaterialsFolder + "/M_Stereo360_SBS.mat";
    private const string CardboardLoaderType = "Google.XR.Cardboard.XRLoader";

    [MenuItem("VR Kiosk/1-Click Setup (Projet + Scène)", priority = 0)]
    public static void SetupAll()
    {
        bool inputChanged = ConfigurePlayerSettings();
        bool xrOk = ConfigureCardboardLoader();
        bool sceneOk = SetupVRScene();

        string msg =
            "Configuration VR 360 Kiosk terminée.\n\n" +
            "- Réglages Android : IL2CPP, ARM64, API 24+, paysage gauche.\n" +
            (xrOk ? "- Google Cardboard activé dans XR Plug-in Management (Android).\n"
                  : "- ⚠ Cardboard NON activé automatiquement : cochez-le dans Project Settings > XR Plug-in Management > Android.\n") +
            (sceneOk ? $"- Scène générée et sauvegardée : {ScenePath}\n" : "- ⚠ Échec de génération de la scène (voir Console).\n") +
            (inputChanged ? "\n⚠ 'Active Input Handling' passé sur 'Both' : REDÉMARREZ Unity pour l'appliquer." : "");

        EditorUtility.DisplayDialog("VR Kiosk", msg, "OK");
    }

    #region Player Settings

    /// <returns>true si l'Active Input Handling a été modifié (redémarrage requis)</returns>
    private static bool ConfigurePlayerSettings()
    {
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.Android.startInFullscreen = true;
        PlayerSettings.Android.renderOutsideSafeArea = true;

        // Active Input Handling = Both (2)
        // Requis : le plugin Cardboard utilise le nouvel Input System, VR360VideoPlayer l'ancien.
        bool changed = false;
        var playerSettings = Resources.FindObjectsOfTypeAll<PlayerSettings>().FirstOrDefault();
        if (playerSettings != null)
        {
            var so = new SerializedObject(playerSettings);
            var prop = so.FindProperty("activeInputHandler");
            if (prop != null && prop.intValue != 2)
            {
                prop.intValue = 2;
                so.ApplyModifiedProperties();
                changed = true;
            }
        }
        else
        {
            Debug.LogWarning("[VR Kiosk] PlayerSettings introuvable : réglez 'Active Input Handling' sur 'Both' manuellement.");
        }

        AssetDatabase.SaveAssets();
        return changed;
    }

    #endregion

    #region XR Plug-in Management

    private static bool ConfigureCardboardLoader()
    {
        EnsureFolder(XRFolder);

        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, XRFolder + "/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }

        XRGeneralSettings androidSettings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (androidSettings == null)
        {
            androidSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            androidSettings.name = "Android Settings";
            AssetDatabase.AddObjectToAsset(androidSettings, perTarget);
            perTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, androidSettings);
        }

        if (androidSettings.Manager == null)
        {
            var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            manager.name = "Android Providers";
            AssetDatabase.AddObjectToAsset(manager, perTarget);
            androidSettings.Manager = manager;
        }

        androidSettings.InitManagerOnStart = true;

        bool ok = androidSettings.Manager.activeLoaders.Any(l => l != null && l.GetType().FullName == CardboardLoaderType)
                  || XRPackageMetadataStore.AssignLoader(androidSettings.Manager, CardboardLoaderType, BuildTargetGroup.Android);

        EditorUtility.SetDirty(androidSettings);
        EditorUtility.SetDirty(perTarget);
        AssetDatabase.SaveAssets();

        if (!ok) Debug.LogWarning("[VR Kiosk] Impossible d'assigner le loader Cardboard automatiquement.");
        return ok;
    }

    #endregion

    #region Scène

    [MenuItem("VR Kiosk/Générer uniquement la scène", priority = 20)]
    public static void SetupVRSceneMenu()
    {
        if (SetupVRScene())
            EditorUtility.DisplayDialog("VR Kiosk", $"Scène générée et sauvegardée : {ScenePath}", "OK");
    }

    private static bool SetupVRScene()
    {
        Shader sbsShader = Shader.Find("VR/Stereo360_SideBySide");
        if (sbsShader == null)
        {
            Debug.LogError("[VR Kiosk] Shader 'VR/Stereo360_SideBySide' introuvable ! Vérifiez Assets/Shaders.");
            return false;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return false;

        EnsureFolder(ScenesFolder);
        EnsureFolder(MaterialsFolder);

        // Ouvrir la scène existante ou en créer une vide
        Scene scene = File.Exists(ScenePath)
            ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
            : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 1. Caméra
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            mainCam = camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
        }

        mainCam.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = Color.black;
        mainCam.nearClipPlane = 0.1f;
        mainCam.farClipPlane = 200f;

        // Suivi de la tête (rotation seule) via le Tracked Pose Driver de legacyinputhelpers
        if (mainCam.GetComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>() == null)
        {
            var tpd = mainCam.gameObject.AddComponent<UnityEngine.SpatialTracking.TrackedPoseDriver>();
            tpd.SetPoseSource(UnityEngine.SpatialTracking.TrackedPoseDriver.DeviceType.GenericXRDevice,
                              UnityEngine.SpatialTracking.TrackedPoseDriver.TrackedPose.Center);
            tpd.trackingType = UnityEngine.SpatialTracking.TrackedPoseDriver.TrackingType.RotationOnly;
        }

        // 2. Matériau SBS (réutilisé s'il existe déjà)
        Material sbsMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (sbsMaterial == null)
        {
            sbsMaterial = new Material(sbsShader);
            AssetDatabase.CreateAsset(sbsMaterial, MaterialPath);
        }
        else
        {
            sbsMaterial.shader = sbsShader;
        }

        // 3. Sphère 360
        GameObject sphere = GameObject.Find("VR360_Sphere");
        if (sphere == null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "VR360_Sphere";
        }

        sphere.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        sphere.transform.localScale = new Vector3(50f, 50f, 50f);

        Collider col = sphere.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        Renderer sphereRenderer = sphere.GetComponent<Renderer>();
        sphereRenderer.sharedMaterial = sbsMaterial;
        sphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sphereRenderer.receiveShadows = false;

        // 4. Gestionnaire vidéo
        GameObject manager = GameObject.Find("VR_Video_Manager");
        if (manager == null) manager = new GameObject("VR_Video_Manager");

        if (manager.GetComponent<VideoPlayer>() == null) manager.AddComponent<VideoPlayer>();

        AudioSource audioSource = manager.GetComponent<AudioSource>();
        if (audioSource == null) audioSource = manager.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        VR360VideoPlayer playerScript = manager.GetComponent<VR360VideoPlayer>();
        if (playerScript == null) playerScript = manager.AddComponent<VR360VideoPlayer>();

        var so = new SerializedObject(playerScript);
        var targetRendererProp = so.FindProperty("targetSphereRenderer");
        if (targetRendererProp != null)
        {
            targetRendererProp.objectReferenceValue = sphereRenderer;
            so.ApplyModifiedProperties();
        }

        // 5. Sauvegarde + Build Settings
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AddSceneToBuildSettings(ScenePath);

        Selection.activeGameObject = manager;
        return saved;
    }

    private static void AddSceneToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    #endregion

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        string name = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
#endif
