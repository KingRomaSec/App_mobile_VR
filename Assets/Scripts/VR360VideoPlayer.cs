using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Google.XR.Cardboard;
using UnityEngine;
using UnityEngine.Video;
using UnityEngine.XR.Management;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Lecteur Vidéo 360° Stéréoscopique Kiosque Hors-Ligne pour Casque VR Shinecon G06E
/// Compatible Google Cardboard XR Plugin - Android / Unity 6
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VR360VideoPlayer : MonoBehaviour
{
    [Header("Configuration Vidéo")]
    [Tooltip("Nom du fichier vidéo dans le stockage interne (ex: video.mp4)")]
    [SerializeField] private string videoFileName = "video.mp4";

    [Tooltip("Sphère d'affichage 360 portant le matériau avec le shader SBS")]
    [SerializeField] private Renderer targetSphereRenderer;

    [Header("Paramètres Contrôles")]
    [Tooltip("Temps d'appui minimum en secondes pour déclencher le recentrage plutôt que Play/Pause")]
    [SerializeField] private float longPressDuration = 1.2f;

    [Tooltip("Délai anti-rebond en secondes pour les boutons mécaniques")]
    [SerializeField] private float debounceTime = 0.35f;

    [Header("Casque (Cardboard)")]
    [Tooltip("URL du QR code du casque (ex: https://google.com/cardboard/cfg?p=...). " +
             "Si vide, l'appli demande de scanner le QR code du Shinecon G06E au 1er lancement (mémorisé ensuite).")]
    [SerializeField] private string deviceParamsUri = "";

    [Tooltip("Autorise la roue dentée Cardboard à relancer le scan du QR code (désactivé en kiosque)")]
    [SerializeField] private bool allowGearRescan = false;

    [Header("Kiosque")]
    [Tooltip("Épingle l'application (Android Lock Task). Sans appareil 'Device Owner', Android demande une confirmation une fois.")]
    [SerializeField] private bool enableScreenPinning = true;

    // Composants
    private VideoPlayer _videoPlayer;
    private AudioSource _audioSource;

    // État des entrées
    private float _pressStartTime = 0f;
    private bool _isPressed = false;
    private float _lastActionTime = 0f;

    private bool _pinningRequested = false;

    // Bouton casque audio (positionnés par OnHeadsetButton depuis Java)
    private bool _headsetDown = false;
    private bool _headsetUp = false;

    private void Awake()
    {
        _videoPlayer = GetComponent<VideoPlayer>();
        _audioSource = GetComponent<AudioSource>();

        // 1. Configuration Kiosque & Écran
        SetupKioskDisplay();

        // 2. Configuration du VideoPlayer
        ConfigureVideoPlayer();
    }

    private void Start()
    {
        // 3. Paramètres optiques du casque (QR code Cardboard)
        InitCardboardDeviceParams();

        // 4. Bouton Play/Pause du casque audio (USB-C / jack)
        StartHeadsetButtonBridge();

        // 5. Résolution du chemin et chargement de la vidéo
        StartCoroutine(ResolveAndLoadVideo());
    }

    private void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var bridge = new AndroidJavaClass("com.vrkiosk.HeadsetButtonBridge"))
            {
                bridge.CallStatic("stop");
            }
        }
        catch (Exception) { }
#endif
    }

    /// <summary>
    /// Démarre la MediaSession Android qui capte le bouton Play/Pause (HEADSETHOOK / MEDIA_PLAY_PAUSE)
    /// et le renvoie à OnHeadsetButton("down"/"up").
    /// </summary>
    private void StartHeadsetButtonBridge()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var bridge = new AndroidJavaClass("com.vrkiosk.HeadsetButtonBridge"))
            {
                bridge.CallStatic("start", activity, gameObject.name, nameof(OnHeadsetButton));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Contrôles] Bouton casque audio indisponible : " + ex.Message);
        }
#endif
    }

    /// <summary>
    /// Appelé depuis Java (UnitySendMessage) : "down" ou "up"
    /// </summary>
    public void OnHeadsetButton(string state)
    {
        if (state == "down") _headsetDown = true;
        else if (state == "up") _headsetUp = true;
    }

    private void Update()
    {
        UpdateCardboard();
        HandleInputs();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            ApplyAndroidImmersiveMode();
        }
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (!isPaused)
        {
            ApplyAndroidImmersiveMode();
        }
    }

    #region 1. KIOSQUE & AFFICHAGE ANDROID

    private void SetupKioskDisplay()
    {
        // Empêcher l'extinction de l'écran dans le casque
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.brightness = 1.0f;

        // Cacher la barre d'état et forcer le plein écran
        Screen.fullScreen = true;

        // Activer le mode immersif Android Sticky
        ApplyAndroidImmersiveMode();
    }

    /// <summary>
    /// Exécute une action sur le thread UI Android (obligatoire pour toucher aux Views / à l'Activity)
    /// </summary>
    private static void RunOnAndroidUiThread(Action<AndroidJavaObject> action)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            {
                AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                activity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        action(activity);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Kiosk] Erreur sur le thread UI Android : " + ex.Message);
                    }
                }));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Kiosk] Impossible d'accéder à l'Activity Android : " + ex.Message);
        }
#endif
    }

    /// <summary>
    /// Force le mode Immersif Sticky sous Android (supprime navigation bar et status bar)
    /// </summary>
    private static void ApplyAndroidImmersiveMode()
    {
        RunOnAndroidUiThread(activity =>
        {
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var decorView = window.Call<AndroidJavaObject>("getDecorView"))
            {
                // FULLSCREEN (4) | HIDE_NAVIGATION (2) | IMMERSIVE_STICKY (4096)
                // LAYOUT_STABLE (256) | LAYOUT_HIDE_NAVIGATION (512) | LAYOUT_FULLSCREEN (1024)
                int flags = 4 | 2 | 4096 | 256 | 512 | 1024;
                decorView.Call("setSystemUiVisibility", flags);
            }
        });
    }

    /// <summary>
    /// Épingle l'application (Lock Task) : bloque Accueil / Récents tant que l'app est épinglée.
    /// </summary>
    private void StartScreenPinning()
    {
        if (!enableScreenPinning || _pinningRequested) return;
        _pinningRequested = true;

        RunOnAndroidUiThread(activity => activity.Call("startLockTask"));
        Debug.Log("[Kiosk] Épinglage de l'écran demandé.");
    }

    #endregion

    #region 2. CONFIGURATION DU LECTEUR VIDÉO

    private void ConfigureVideoPlayer()
    {
        _videoPlayer.playOnAwake = false;
        _videoPlayer.isLooping = true; // Boucle infinie demandée
        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.skipOnDrop = true;

        // Configuration du rendu direct sur le matériau de la sphère
        if (targetSphereRenderer != null)
        {
            _videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
            _videoPlayer.targetMaterialRenderer = targetSphereRenderer;
            _videoPlayer.targetMaterialProperty = "_MainTex";
        }
        else
        {
            Debug.LogError("[Video] 'targetSphereRenderer' non assigné : lancez le menu VR Kiosk > 1-Click Setup.");
        }

        // Configuration Audio (Direct ou AudioSource)
        if (_audioSource != null)
        {
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _videoPlayer.EnableAudioTrack(0, true);
            _videoPlayer.SetTargetAudioSource(0, _audioSource);
        }
        else
        {
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
            _videoPlayer.EnableAudioTrack(0, true);
        }

        // Événements
        _videoPlayer.prepareCompleted += OnVideoPrepared;
        _videoPlayer.errorReceived += OnVideoError;
    }

    private IEnumerator ResolveAndLoadVideo()
    {
        string finalPath = null;

        // 1. Application.persistentDataPath (/storage/emulated/0/Android/data/<package>/files/) - aucune permission requise
        string persistentPath = Path.Combine(Application.persistentDataPath, videoFileName);
        if (File.Exists(persistentPath))
        {
            finalPath = persistentPath;
        }
        else if (Directory.Exists(Application.persistentDataPath))
        {
            string[] mp4Files = Directory.GetFiles(Application.persistentDataPath, "*.mp4");
            if (mp4Files.Length > 0)
            {
                Array.Sort(mp4Files, StringComparer.OrdinalIgnoreCase);
                finalPath = mp4Files[0];
                Debug.Log($"[Video] Fichier MP4 automatique détecté : {finalPath}");
            }
        }

        // 2. StreamingAssets (embarqué dans l'APK)
        if (finalPath == null)
        {
            string streamingPath = Path.Combine(Application.streamingAssetsPath, videoFileName);
            if (StreamingAssetExists(streamingPath))
            {
                finalPath = streamingPath;
            }
        }

        // 3. Dossier public Movies (permission de lecture des médias requise)
        if (finalPath == null)
        {
            yield return EnsureMediaReadPermission();

            string publicMoviesPath = Path.Combine("/storage/emulated/0/Movies", videoFileName);
            if (File.Exists(publicMoviesPath))
            {
                finalPath = publicMoviesPath;
            }
        }

        if (finalPath == null)
        {
            Debug.LogError($"[Video] Aucune vidéo trouvée ('{videoFileName}'). Copiez-la dans : {Application.persistentDataPath}");
            yield break;
        }

        Debug.Log($"[Video] Chemin vidéo cible : {finalPath}");
        _videoPlayer.url = finalPath;
        _videoPlayer.Prepare();
    }

    /// <summary>
    /// Vérifie la présence d'un fichier StreamingAssets (sur Android il est dans l'APK, File.Exists ne marche pas).
    /// </summary>
    private static bool StreamingAssetExists(string path)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            string relative = path.Substring(Application.streamingAssetsPath.Length).TrimStart('/');
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var assets = activity.Call<AndroidJavaObject>("getAssets"))
            using (var stream = assets.Call<AndroidJavaObject>("open", relative))
            {
                stream.Call("close");
                return true;
            }
        }
        catch
        {
            return false;
        }
#else
        return File.Exists(path);
#endif
    }

    /// <summary>
    /// Demande la permission de lecture des vidéos (READ_MEDIA_VIDEO sur Android 13+, READ_EXTERNAL_STORAGE avant).
    /// </summary>
    private IEnumerator EnsureMediaReadPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        int sdkInt;
        using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
        {
            sdkInt = version.GetStatic<int>("SDK_INT");
        }

        string permission = sdkInt >= 33 ? "android.permission.READ_MEDIA_VIDEO" : Permission.ExternalStorageRead;
        if (Permission.HasUserAuthorizedPermission(permission)) yield break;

        bool done = false;
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => done = true;
        callbacks.PermissionDenied += _ => done = true;
#pragma warning disable 0618
        callbacks.PermissionDeniedAndDontAskAgain += _ => done = true;
#pragma warning restore 0618
        Permission.RequestUserPermission(permission, callbacks);

        float timeout = Time.realtimeSinceStartup + 60f;
        while (!done && Time.realtimeSinceStartup < timeout) yield return null;

        // Le dialogue système a pu réafficher les barres : on remet le mode immersif
        ApplyAndroidImmersiveMode();
#else
        yield break;
#endif
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        Debug.Log("[Video] Préparation terminée. Lancement de la lecture.");
        source.Play();

        // Recentrage automatique de la vue au lancement
        RecenterView();
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"[Video] Erreur de lecture vidéo : {message}");
    }

    #endregion

    #region 3. CARDBOARD (PARAMÈTRES CASQUE SHINECON G06E)

    /// <summary>
    /// true si le loader Cardboard est initialisé (faux dans l'éditeur ou si XR est désactivé)
    /// </summary>
    private static bool IsCardboardActive
    {
        get
        {
            var settings = XRGeneralSettings.Instance;
            return settings != null && settings.Manager != null &&
                   settings.Manager.activeLoader is Google.XR.Cardboard.XRLoader;
        }
    }

    private void InitCardboardDeviceParams()
    {
        if (!IsCardboardActive)
        {
            Debug.LogWarning("[Cardboard] Loader Cardboard inactif (éditeur ou XR désactivé) : rendu non stéréo.");
            return;
        }

        if (Api.HasDeviceParams())
        {
            StartScreenPinning();
            return;
        }

        if (!string.IsNullOrWhiteSpace(deviceParamsUri))
        {
            // Profil du casque fourni dans l'inspecteur : aucun scan nécessaire
            Api.SaveDeviceParams(deviceParamsUri.Trim());
            Api.ReloadDeviceParams();
            Debug.Log("[Cardboard] Paramètres du casque appliqués depuis l'URL configurée.");
            StartScreenPinning();
        }
        else
        {
            // 1er lancement : scan du QR code du casque (mémorisé par le SDK Cardboard)
            // L'épinglage est différé après le scan pour ne pas bloquer l'activité de scan.
            Debug.Log("[Cardboard] Aucun profil casque : lancement du scan du QR code.");
            Api.ScanDeviceParams();
        }
    }

    private void UpdateCardboard()
    {
        if (!IsCardboardActive) return;

        if (Api.HasNewDeviceParams())
        {
            Api.ReloadDeviceParams();
            StartScreenPinning();
        }

        // Requis à chaque frame par le plugin (rotation / taille d'écran)
        Api.UpdateScreenParams();
    }

    #endregion

    #region 4. CONTRÔLE PHYSIQUE (BOUTON SHINECON & USB-C)

    private void HandleInputs()
    {
        // Widget Cardboard : en kiosque, le bouton "X" ne quitte pas l'application,
        // et la roue dentée n'est active que si allowGearRescan est coché.
        if (Api.IsCloseButtonPressed)
        {
            return;
        }

        if (Api.IsGearButtonPressed)
        {
            if (allowGearRescan) Api.ScanDeviceParams();
            return;
        }

        bool isTriggerDown = false;
        bool isTriggerUp = false;

        // A. Événement tactile central (Bouton mécanique capacitif du Shinecon G06E)
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began) isTriggerDown = true;
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) isTriggerUp = true;
        }
        else
        {
            // Clic souris (éditeur) - ignoré si un toucher est actif pour éviter le double comptage
            if (Input.GetMouseButtonDown(0)) isTriggerDown = true;
            if (Input.GetMouseButtonUp(0)) isTriggerUp = true;
        }

        // B. Bouton multifonction casque audio (USB-C / jack 3.5mm), reçu via la MediaSession Android
        if (_headsetDown) { isTriggerDown = true; _headsetDown = false; }
        if (_headsetUp) { isTriggerUp = true; _headsetUp = false; }

        // C. Barre d'espace (tests dans l'éditeur / clavier)
        if (Input.GetKeyDown(KeyCode.Space)) isTriggerDown = true;
        if (Input.GetKeyUp(KeyCode.Space)) isTriggerUp = true;

        // Gestion de la logique Appui Court (Play/Pause) vs Appui Long (Recentrage manuel)
        if (isTriggerDown && !_isPressed)
        {
            _isPressed = true;
            _pressStartTime = Time.unscaledTime;
        }

        if (_isPressed && Time.unscaledTime - _pressStartTime >= longPressDuration)
        {
            // Appui long détecté -> Recentrage manuel
            _isPressed = false;
            _lastActionTime = Time.unscaledTime;
            Debug.Log("[Contrôles] Appui long détecté -> Recentrage manuel de la vue.");
            RecenterView();
        }

        if (isTriggerUp && _isPressed)
        {
            _isPressed = false;
            float duration = Time.unscaledTime - _pressStartTime;

            // Appui court validé (avec anti-rebond)
            if (duration < longPressDuration && (Time.unscaledTime - _lastActionTime > debounceTime))
            {
                _lastActionTime = Time.unscaledTime;
                TogglePlayPause();
            }
        }
    }

    /// <summary>
    /// Bascule nativement entre Play et Pause sans fermer ni réinitialiser la vidéo
    /// </summary>
    public void TogglePlayPause()
    {
        if (_videoPlayer == null || !_videoPlayer.isPrepared)
        {
            Debug.LogWarning("[Video] Lecteur non prêt pour Play/Pause.");
            return;
        }

        if (_videoPlayer.isPlaying)
        {
            _videoPlayer.Pause();
            Debug.Log("[Video] État : PAUSE");
        }
        else
        {
            _videoPlayer.Play();
            Debug.Log("[Video] État : LECTURE");
        }
    }

    #endregion

    #region 5. RECENTRAGE VUE VR

    /// <summary>
    /// Recentrage de l'orientation de la caméra VR
    /// </summary>
    public void RecenterView()
    {
        // 1. Cardboard XR
        if (IsCardboardActive) Api.Recenter();

        // 2. Sous-systèmes d'entrée Unity XR
        var inputSubsystems = new List<UnityEngine.XR.XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputSubsystems);
        foreach (var subsystem in inputSubsystems)
        {
            subsystem.TryRecenter();
        }

        Debug.Log("[VR] Recentrage exécuté.");
    }

    #endregion
}
