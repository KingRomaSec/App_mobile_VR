using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Lecteur Vidéo 360° Stéréoscopique Kiosque Hors-Ligne pour Casque VR Shinecon G06E
/// Compatible Google Cardboard XR Plugin - Android / Unity
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

    // Composants
    private VideoPlayer _videoPlayer;
    private AudioSource _audioSource;

    // État des entrées
    private float _pressStartTime = 0f;
    private bool _isPressed = false;
    private float _lastActionTime = 0f;

    // Cache Reflection pour Google Cardboard XR
    private MethodInfo _cardboardRecenterMethod;
    private MethodInfo _cardboardTriggerMethod;

    private void Awake()
    {
        _videoPlayer = GetComponent<VideoPlayer>();
        _audioSource = GetComponent<AudioSource>();

        // 1. Configuration Kiosque & Écran
        SetupKioskDisplay();

        // 2. Initialisation du pont Cardboard XR (Safe Reflection)
        InitCardboardApi();

        // 3. Configuration du VideoPlayer
        ConfigureVideoPlayer();
    }

    private void Start()
    {
        // 4. Résolution du chemin et chargement de la vidéo
        ResolveAndLoadVideo();
    }

    private void Update()
    {
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

        // Cacher la barre d'état et forcer le plein écran
        Screen.fullScreen = true;

        // Activer le mode immersif Android Sticky
        ApplyAndroidImmersiveMode();
    }

    /// <summary>
    /// Force le mode Immersif Sticky sous Android (supprime navigation bar et status bar)
    /// </summary>
    private void ApplyAndroidImmersiveMode()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var decorView = window.Call<AndroidJavaObject>("getDecorView"))
            {
                // SYSTEM_UI_FLAG_FULLSCREEN (4)
                // SYSTEM_UI_FLAG_HIDE_NAVIGATION (2)
                // SYSTEM_UI_FLAG_IMMERSIVE_STICKY (4096)
                // SYSTEM_UI_FLAG_LAYOUT_STABLE (256)
                // SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION (512)
                // SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN (1024)
                int flags = 4 | 2 | 4096 | 256 | 512 | 1024;
                decorView.Call("setSystemUiVisibility", flags);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Kiosk] Impossible d'appliquer le mode immersif : " + ex.Message);
        }
#endif
    }

    #endregion

    #region 2. CONFIGURATION DU LECTEUR VIDÉO

    private void ConfigureVideoPlayer()
    {
        _videoPlayer.playOnAwake = false;
        _videoPlayer.isLooping = true; // Boucle infinie demandée
        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.aspectRatio = VideoAspectRatio.FitHorizontally;

        // Configuration du rendu direct sur le matériau de la sphère
        if (targetSphereRenderer != null)
        {
            _videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
            _videoPlayer.targetMaterialRenderer = targetSphereRenderer;
            _videoPlayer.targetMaterialProperty = "_MainTex";
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

    private void ResolveAndLoadVideo()
    {
        string finalPath = "";

        // 1. Vérifier dans Application.persistentDataPath (Stockage interne accessible sans root)
        // Dossier : /storage/emulated/0/Android/data/<package_name>/files/
        string persistentPath = Path.Combine(Application.persistentDataPath, videoFileName);
        if (File.Exists(persistentPath))
        {
            finalPath = persistentPath;
        }
        else
        {
            // Recherche du premier fichier .mp4 présent dans persistentDataPath
            if (Directory.Exists(Application.persistentDataPath))
            {
                string[] mp4Files = Directory.GetFiles(Application.persistentDataPath, "*.mp4");
                if (mp4Files.Length > 0)
                {
                    finalPath = mp4Files[0];
                    Debug.Log($"[Video] Fichier MP4 automatique détecté : {finalPath}");
                }
            }
        }

        // 2. Si non trouvé, vérifier dans StreamingAssets (fichiers inclus dans l'APK ou OBB)
        if (string.IsNullOrEmpty(finalPath))
        {
            string streamingPath = Path.Combine(Application.streamingAssetsPath, videoFileName);
#if UNITY_ANDROID && !UNITY_EDITOR
            // Sur Android, StreamingAssets est dans l'APK compressé, VideoPlayer accepte le chemin direct
            finalPath = streamingPath;
#else
            if (File.Exists(streamingPath))
            {
                finalPath = streamingPath;
            }
#endif
        }

        // 3. Fallback sur chemin public Movies/DCIM si présent
        if (string.IsNullOrEmpty(finalPath) || !File.Exists(finalPath))
        {
            string publicMoviesPath = Path.Combine("/storage/emulated/0/Movies", videoFileName);
            if (File.Exists(publicMoviesPath))
            {
                finalPath = publicMoviesPath;
            }
        }

        Debug.Log($"[Video] Chemin vidéo cible : {finalPath}");
        _videoPlayer.url = finalPath;
        _videoPlayer.Prepare();
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

    #region 3. CONTRÔLE PHYSIQUE (BOUTON SHINECON & USB-C)

    private void HandleInputs()
    {
        bool isTriggerDown = false;
        bool isTriggerUp = false;

        // A. Événement tactile central (Bouton mécanique capacitif du Shinecon G06E)
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began) isTriggerDown = true;
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) isTriggerUp = true;
        }

        // Clic souris (simulé sous Android ou éditeur)
        if (Input.GetMouseButtonDown(0)) isTriggerDown = true;
        if (Input.GetMouseButtonUp(0)) isTriggerUp = true;

        // B. Bouton multifonction casque (Carte son USB-C ou jack 3.5mm)
        // KeyCode.HeadsetHook est le code standard Android pour le bouton Play/Pause du casque
        if (Input.GetKeyDown(KeyCode.HeadsetHook) ||
            Input.GetKeyDown(KeyCode.MediaPlayPause) ||
            Input.GetKeyDown(KeyCode.Space))
        {
            isTriggerDown = true;
        }

        if (Input.GetKeyUp(KeyCode.HeadsetHook) ||
            Input.GetKeyUp(KeyCode.MediaPlayPause) ||
            Input.GetKeyUp(KeyCode.Space))
        {
            isTriggerUp = true;
        }

        // C. Interrogation de l'API Cardboard (si présent)
        if (IsCardboardTriggerPressed())
        {
            isTriggerDown = true;
        }

        // Gestion de la logique Appui Court (Play/Pause) vs Appui Long (Recentrage manuel)
        if (isTriggerDown && !_isPressed)
        {
            _isPressed = true;
            _pressStartTime = Time.unscaledTime;
        }

        if (_isPressed && Time.unscaledTime - _pressStartTime >= longPressDuration)
        {
            // Appui long détecté (> 1.2s) -> Recentrage manuel
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

    #region 4. RECENTRAGE VUE VR (CARDBOARD XR)

    private void InitCardboardApi()
    {
        try
        {
            Type cardboardApi = Type.GetType("Google.XR.Cardboard.Api, Google.XR.Cardboard");
            if (cardboardApi != null)
            {
                _cardboardRecenterMethod = cardboardApi.GetMethod("Recenter", BindingFlags.Public | BindingFlags.Static);
                _cardboardTriggerMethod = cardboardApi.GetMethod("get_IsTriggerPressed", BindingFlags.Public | BindingFlags.Static);
                Debug.Log("[Cardboard] Google Cardboard XR Plugin détecté et lié avec succès.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Cardboard] Initialisation Cardboard API via reflection ignorée : " + ex.Message);
        }
    }

    private bool IsCardboardTriggerPressed()
    {
        if (_cardboardTriggerMethod != null)
        {
            try
            {
                return (bool)_cardboardTriggerMethod.Invoke(null, null);
            }
            catch { }
        }
        return false;
    }

    /// <summary>
    /// Recentrage automatique de l'orientation de la caméra VR
    /// </summary>
    public void RecenterView()
    {
        // 1. Appel natif Cardboard XR
        if (_cardboardRecenterMethod != null)
        {
            try
            {
                _cardboardRecenterMethod.Invoke(null, null);
                Debug.Log("[VR] Recentrage Cardboard XR exécuté.");
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VR] Erreur lors de l'appel Recenter Cardboard : " + ex.Message);
            }
        }

        // 2. Appel standard Unity XR (sous-systèmes d'entrée XR actifs)
        var inputSubsystems = new System.Collections.Generic.List<UnityEngine.XR.XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputSubsystems);
        foreach (var subsystem in inputSubsystems)
        {
            subsystem.TryRecenter();
        }
        Debug.Log("[VR] Recentrage Unity XR exécuté.");
    }

    #endregion
}
