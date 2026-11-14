using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Google.XR.Cardboard;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using UnityEngine.XR.Management;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// Formats vidéo pris en charge.
/// </summary>
public enum VideoFormat
{
    Mono360 = 0,
    Stereo360SBS = 1,
    Stereo360TB = 2,
    Stereo180SBS = 3,
    Flat2D = 4,
    Flat3DSBS = 5,
}

/// <summary>
/// Application VR (Google Cardboard / Shinecon G06E) :
///  - Bibliothèque : liste des vidéos de la galerie du téléphone (miniatures, pages)
///  - Lecteur : 360°, 180°, écran plat, en 2D ou 3D (côte à côte / haut-bas)
///  - Commandes au regard : viser un bouton + appui sur le bouton du casque, ou fixer 2 s
/// </summary>
[RequireComponent(typeof(VideoPlayer))]
public class VR360VideoPlayer : MonoBehaviour
{
    [Header("Affichage")]
    [Tooltip("Sphère d'affichage 360 (créée automatiquement si vide)")]
    [SerializeField] private Renderer targetSphereRenderer;

    [Header("Contrôles")]
    [Tooltip("Durée d'appui (s) pour recentrer la vue")]
    [SerializeField] private float longPressDuration = 1.2f;

    [Tooltip("Clic automatique en fixant un bouton")]
    [SerializeField] private bool gazeDwellClick = true;

    [Tooltip("Durée de fixation (s) pour le clic automatique")]
    [SerializeField] private float gazeDwellTime = 2f;

    [Tooltip("Masquage automatique des commandes pendant la lecture (s)")]
    [SerializeField] private float controlsAutoHide = 5f;

    [Header("Casque (Cardboard)")]
    [Tooltip("URL du QR code du casque (https://google.com/cardboard/cfg?p=...). Vide = scan au 1er lancement.")]
    [SerializeField] private string deviceParamsUri = "";

    [Header("Mode kiosque (désactivé par défaut)")]
    [Tooltip("Épingle l'application et empêche d'en sortir (✕ et Retour ne quittent plus).")]
    [SerializeField] private bool kioskMode = false;

    private const int TilesPerPage = 6;
    private const string FormatPrefsPrefix = "vrkiosk.format.";

    private static readonly string[] FormatNames =
    {
        "360° 2D",
        "360° 3D côte à côte",
        "360° 3D haut/bas",
        "180° 3D côte à côte",
        "Écran 2D",
        "Écran 3D côte à côte",
    };

    private enum AppState { Library, Player }

    // Composants
    private VideoPlayer _videoPlayer;
    private AudioSource _audioSource;
    private Camera _camera;
    private VRGazeController _gaze;

    // Rendu vidéo
    private RenderTexture _videoTexture;
    private Material _sphereMaterial;
    private Material _screenMaterial;
    private Renderer _screenRenderer;

    // État
    private AppState _state = AppState.Library;
    private List<VideoEntry> _videos = new List<VideoEntry>();
    private int _page;
    private VideoEntry _current;
    private VideoFormat _format = VideoFormat.Stereo360SBS;
    private string _playerMessage = "";
    private int _volumePercent = -1;
    private bool _permissionAsked;
    private float _controlsHideAt;
    private readonly Dictionary<long, Texture2D> _thumbnails = new Dictionary<long, Texture2D>();

    // Entrées
    private bool _pressed;
    private bool _pressFromHeadset;
    private bool _longFired;
    private float _pressStart;
    private bool _headsetDown;
    private bool _headsetUp;

    // Interface : bibliothèque
    private Canvas _libraryCanvas;
    private Text _libraryStatus;
    private Text _pageLabel;
    private GazeButton _permissionButton;
    private readonly List<GazeButton> _tileButtons = new List<GazeButton>();
    private readonly List<RawImage> _tileThumbs = new List<RawImage>();
    private readonly List<Text> _tileNames = new List<Text>();
    private readonly List<Text> _tileInfos = new List<Text>();

    // Interface : lecteur
    private Canvas _controlsCanvas;
    private Text _videoTitle;
    private Text _timeLabel;
    private Text _infoLabel;
    private Image _progressFill;
    private GazeButton _playPauseButton;

    #region CYCLE DE VIE

    private void Awake()
    {
        _videoPlayer = GetComponent<VideoPlayer>();
        _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();

        _camera = Camera.main;
        if (_camera == null) _camera = FindFirstObjectByType<Camera>();

        SetupKioskDisplay();
        ConfigureVideoPlayer();
        SetupVideoSurfaces();
        BuildLibraryUI();
        BuildControlsUI();

        _gaze = new VRGazeController(_camera) { DwellEnabled = gazeDwellClick, DwellTime = gazeDwellTime };
        foreach (var b in _tileButtons) _gaze.Register(b);
        RegisterAllButtons();
    }

    private void Start()
    {
        InitCardboardDeviceParams();
        StartHeadsetButtonBridge();
        if (!kioskMode) AndroidGallery.StopLockTask();

        ShowLibrary();
        StartCoroutine(PlacePanelsNextFrame());
        StartCoroutine(InitialLibraryLoad());
    }

    private void Update()
    {
        UpdateCardboard();
#if UNITY_EDITOR
        EditorMouseLook();
#endif
        _gaze.Update();
        HandleInputs();

        if (_state == AppState.Player) UpdatePlayerUI();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) return;
        ApplyAndroidImmersiveMode();
        _volumePercent = AndroidGallery.VolumePercent();

        // Retour des réglages Android : la permission a pu être accordée entre-temps
        if (_state == AppState.Library && _permissionAsked) RefreshLibrary();
    }

    private void OnApplicationPause(bool isPaused)
    {
        if (isPaused)
        {
            if (_state == AppState.Player && _videoPlayer.isPlaying) _videoPlayer.Pause();
        }
        else
        {
            ApplyAndroidImmersiveMode();
        }
    }

    private void OnDestroy()
    {
        StopHeadsetButtonBridge();
        if (_videoTexture != null) _videoTexture.Release();
    }

    #endregion

    #region KIOSQUE & ANDROID

    private void SetupKioskDisplay()
    {
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.brightness = 1.0f;
        Screen.fullScreen = true;
        ApplyAndroidImmersiveMode();
    }

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
                    try { action(activity); }
                    catch (Exception ex) { Debug.LogWarning("[Android] " + ex.Message); }
                }));
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Android] Activity inaccessible : " + ex.Message);
        }
#endif
    }

    /// <summary>Masque les barres système (mode immersif sticky).</summary>
    private static void ApplyAndroidImmersiveMode()
    {
        RunOnAndroidUiThread(activity =>
        {
            using (var window = activity.Call<AndroidJavaObject>("getWindow"))
            using (var decorView = window.Call<AndroidJavaObject>("getDecorView"))
            {
                int flags = 4 | 2 | 4096 | 256 | 512 | 1024;
                decorView.Call("setSystemUiVisibility", flags);
            }
        });
    }

    private void StartScreenPinning()
    {
        if (!kioskMode) return;
        RunOnAndroidUiThread(activity => activity.Call("startLockTask"));
    }

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

    private void StopHeadsetButtonBridge()
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

    /// <summary>Appelé depuis Java : "down" ou "up" (bouton Play/Pause d'un casque audio).</summary>
    public void OnHeadsetButton(string state)
    {
        if (state == "down") _headsetDown = true;
        else if (state == "up") _headsetUp = true;
    }

    #endregion

    #region CARDBOARD

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
            Debug.LogWarning("[Cardboard] Loader inactif (éditeur ou XR désactivé) : rendu non stéréo.");
            return;
        }

        if (Api.HasDeviceParams())
        {
            StartScreenPinning();
        }
        else if (!string.IsNullOrWhiteSpace(deviceParamsUri))
        {
            Api.SaveDeviceParams(deviceParamsUri.Trim());
            Api.ReloadDeviceParams();
            StartScreenPinning();
        }
        else
        {
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
        Api.UpdateScreenParams();
    }

    #endregion

    #region ENTRÉES

    private void HandleInputs()
    {
        // Touche Retour Android (= Échap)
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            GoBack();
            return;
        }

        // Boutons du widget Cardboard : ✕ = retour / quitter, ⚙ = scanner le QR code du casque
        if (IsCardboardActive)
        {
            if (Api.IsCloseButtonPressed)
            {
                GoBack();
                return;
            }
            if (Api.IsGearButtonPressed)
            {
                Api.ScanDeviceParams();
                return;
            }
        }

        bool down = false, up = false, headset = false;

        // Bouton du casque VR (touche l'écran) / doigt / souris
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began) down = true;
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) up = true;
        }
        else
        {
            if (Input.GetMouseButtonDown(0)) down = true;
            if (Input.GetMouseButtonUp(0)) up = true;
        }

        // Bouton Play/Pause d'un casque audio (USB-C / jack / Bluetooth)
        if (_headsetDown) { down = true; headset = true; _headsetDown = false; }
        if (_headsetUp) { up = true; headset = true; _headsetUp = false; }

        // Clavier (éditeur)
        if (Input.GetKeyDown(KeyCode.Space)) down = true;
        if (Input.GetKeyUp(KeyCode.Space)) up = true;

        if (down && !_pressed)
        {
            _pressed = true;
            _longFired = false;
            _pressFromHeadset = headset;
            _pressStart = Time.unscaledTime;
        }

        // Appui long : recentrer la vue
        if (_pressed && !_longFired && Time.unscaledTime - _pressStart >= longPressDuration)
        {
            _longFired = true;
            RecenterView();
        }

        if (up && _pressed)
        {
            _pressed = false;
            if (!_longFired) OnShortPress(_pressFromHeadset);
        }
    }

    private void OnShortPress(bool fromHeadsetAudioButton)
    {
        if (_state == AppState.Player && fromHeadsetAudioButton)
        {
            TogglePlayPause();
            ShowControls();
            return;
        }

        if (_gaze.Click())
        {
            if (_state == AppState.Player) ShowControls();
            return;
        }

        // Appui sans viser de bouton pendant la lecture : affiche / masque les commandes
        if (_state == AppState.Player)
        {
            if (_controlsCanvas.gameObject.activeSelf) HideControls();
            else ShowControls();
        }
    }

    /// <summary>Retour : lecteur → bibliothèque, bibliothèque → quitter l'application.</summary>
    private void GoBack()
    {
        if (_state == AppState.Player)
        {
            StopVideo();
            ShowLibrary();
        }
        else if (!kioskMode)
        {
            Debug.Log("[App] Fermeture.");
            Application.Quit();
        }
    }

#if UNITY_EDITOR
    private float _yaw, _pitch;

    /// <summary>Dans l'éditeur : clic droit maintenu pour tourner la tête.</summary>
    private void EditorMouseLook()
    {
        if (!Input.GetMouseButton(1)) return;
        _yaw += Input.GetAxis("Mouse X") * 3f;
        _pitch = Mathf.Clamp(_pitch - Input.GetAxis("Mouse Y") * 3f, -85f, 85f);
        _camera.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }
#endif

    #endregion

    #region RECENTRAGE

    public void RecenterView()
    {
        if (IsCardboardActive) Api.Recenter();

        var inputSubsystems = new List<UnityEngine.XR.XRInputSubsystem>();
        SubsystemManager.GetSubsystems(inputSubsystems);
        foreach (var subsystem in inputSubsystems) subsystem.TryRecenter();

        // Replace l'interface devant l'utilisateur une fois la nouvelle orientation appliquée
        StartCoroutine(PlacePanelsNextFrame());
    }

    private IEnumerator PlacePanelsNextFrame()
    {
        yield return null;
        yield return null;
        PlaceInFront(_libraryCanvas.transform, 2.4f, 0f);
        PlaceInFront(_controlsCanvas.transform, 2.0f, -0.75f);
        PlaceScreen();
    }

    /// <summary>Place un objet face à l'utilisateur, dans la direction horizontale du regard.</summary>
    private void PlaceInFront(Transform t, float distance, float height)
    {
        Vector3 head = _camera.transform.position;
        Vector3 forward = _camera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 pos = head + forward * distance + Vector3.up * height;
        t.position = pos;
        t.rotation = Quaternion.LookRotation(pos - head, Vector3.up);
    }

    #endregion

    #region SURFACES VIDÉO

    private void SetupVideoSurfaces()
    {
        // Sphère 360/180
        if (targetSphereRenderer == null)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "VR360_Sphere";
            Destroy(sphere.GetComponent<Collider>());
            targetSphereRenderer = sphere.GetComponent<Renderer>();
        }
        targetSphereRenderer.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        targetSphereRenderer.transform.localScale = Vector3.one * 50f;

        Shader shader = targetSphereRenderer.sharedMaterial != null
            ? targetSphereRenderer.sharedMaterial.shader
            : Shader.Find("VR/Stereo360_SideBySide");

        _sphereMaterial = new Material(shader);
        _sphereMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Front);
        targetSphereRenderer.sharedMaterial = _sphereMaterial;
        targetSphereRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Écran plat
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "VR_FlatScreen";
        Destroy(screen.GetComponent<Collider>());
        _screenRenderer = screen.GetComponent<Renderer>();
        _screenMaterial = new Material(shader);
        _screenMaterial.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        _screenMaterial.SetFloat("_Projection", 2f);
        _screenRenderer.sharedMaterial = _screenMaterial;
        _screenRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        targetSphereRenderer.gameObject.SetActive(false);
        screen.SetActive(false);
    }

    private void ApplyFormat()
    {
        bool flat = _format == VideoFormat.Flat2D || _format == VideoFormat.Flat3DSBS;
        float projection = _format == VideoFormat.Stereo180SBS ? 1f : 0f;
        float layout;
        switch (_format)
        {
            case VideoFormat.Stereo360SBS:
            case VideoFormat.Stereo180SBS:
            case VideoFormat.Flat3DSBS:
                layout = 1f;
                break;
            case VideoFormat.Stereo360TB:
                layout = 2f;
                break;
            default:
                layout = 0f;
                break;
        }

        _sphereMaterial.SetFloat("_Projection", projection);
        _sphereMaterial.SetFloat("_Layout", layout);
        _screenMaterial.SetFloat("_Layout", layout);

        bool inPlayer = _state == AppState.Player;
        targetSphereRenderer.gameObject.SetActive(inPlayer && !flat);
        _screenRenderer.gameObject.SetActive(inPlayer && flat);
        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = inPlayer && !flat ? Color.black : new Color(0.05f, 0.06f, 0.09f);

        if (flat) PlaceScreen();
    }

    /// <summary>Taille et place l'écran plat (largeur 4 m à 4 m de distance).</summary>
    private void PlaceScreen()
    {
        if (_screenRenderer == null) return;
        float aspect = 16f / 9f;
        if (_videoPlayer.width > 0 && _videoPlayer.height > 0)
        {
            aspect = (float)_videoPlayer.width / _videoPlayer.height;
            if (_format == VideoFormat.Flat3DSBS) aspect *= 0.5f;
        }
        PlaceInFront(_screenRenderer.transform, 4f, 0f);
        _screenRenderer.transform.localScale = new Vector3(4f, 4f / aspect, 1f);
    }

    private VideoFormat DetectFormat(VideoEntry entry, int width, int height)
    {
        string saved = PlayerPrefs.GetString(FormatPrefsPrefix + entry.path, "");
        if (Enum.TryParse(saved, out VideoFormat remembered)) return remembered;

        string n = (entry.name ?? "").ToLowerInvariant();
        float ratio = height > 0 ? (float)width / height : 2f;
        bool tb = n.Contains("_tb") || n.Contains("_ou") || n.Contains("topbottom") || n.Contains("top_bottom");
        bool sbs = n.Contains("sbs") || n.Contains("_lr") || n.Contains("3d");

        if (n.Contains("180")) return VideoFormat.Stereo180SBS;
        if (tb) return n.Contains("360") || ratio < 1.2f ? VideoFormat.Stereo360TB : VideoFormat.Flat2D;
        if (Mathf.Abs(ratio - 1f) < 0.1f) return VideoFormat.Stereo360TB;          // carré = 360° haut/bas
        if (ratio > 3.2f) return VideoFormat.Flat3DSBS;                             // 32:9 = écran 3D
        if (Mathf.Abs(ratio - 2f) < 0.15f) return sbs ? VideoFormat.Stereo360SBS : VideoFormat.Mono360;
        if (n.Contains("360")) return sbs ? VideoFormat.Stereo360SBS : VideoFormat.Mono360;
        return sbs ? VideoFormat.Flat3DSBS : VideoFormat.Flat2D;                    // 16:9 = écran
    }

    #endregion

    #region LECTEUR VIDÉO

    private void ConfigureVideoPlayer()
    {
        _videoPlayer.playOnAwake = false;
        _videoPlayer.isLooping = true;
        _videoPlayer.source = VideoSource.Url;
        _videoPlayer.skipOnDrop = true;
        _videoPlayer.renderMode = VideoRenderMode.RenderTexture;

        _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        _videoPlayer.EnableAudioTrack(0, true);
        _videoPlayer.SetTargetAudioSource(0, _audioSource);
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f;

        _videoPlayer.prepareCompleted += OnVideoPrepared;
        _videoPlayer.errorReceived += OnVideoError;
    }

    private void PlayVideo(VideoEntry entry)
    {
        _current = entry;
        _state = AppState.Player;
        _libraryCanvas.gameObject.SetActive(false);

        _format = DetectFormat(entry, entry.width, entry.height);
        ApplyFormat();

        _playerMessage = "Chargement...";
        _videoTitle.text = entry.name;
        ShowControls();

        _videoPlayer.Stop();
        _videoPlayer.url = entry.path;
        _videoPlayer.Prepare();
        Debug.Log("[Video] Ouverture : " + entry.path);
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        int w = (int)source.width, h = (int)source.height;
        if (w <= 0 || h <= 0) { w = 1920; h = 1080; }

        if (_videoTexture == null || _videoTexture.width != w || _videoTexture.height != h)
        {
            if (_videoTexture != null) _videoTexture.Release();
            _videoTexture = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { name = "VideoTexture" };
            _videoTexture.Create();
        }
        source.targetTexture = _videoTexture;
        _sphereMaterial.mainTexture = _videoTexture;
        _screenMaterial.mainTexture = _videoTexture;

        // Les dimensions réelles sont connues : affine le format si rien n'a été mémorisé
        if (_current != null && (_current.width <= 0 || _current.height <= 0))
        {
            _format = DetectFormat(_current, w, h);
        }
        ApplyFormat();

        _playerMessage = "";
        source.Play();
        RecenterView();
        _controlsHideAt = Time.unscaledTime + controlsAutoHide;
        Debug.Log($"[Video] Lecture {w}x{h}, format {FormatNames[(int)_format]}");
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError("[Video] Erreur : " + message);
        _playerMessage = "Lecture impossible : " + message;
        ShowControls();
    }

    private void StopVideo()
    {
        _videoPlayer.Stop();
        _current = null;
    }

    public void TogglePlayPause()
    {
        if (!_videoPlayer.isPrepared) return;
        if (_videoPlayer.isPlaying) _videoPlayer.Pause();
        else _videoPlayer.Play();
    }

    private void Seek(double seconds)
    {
        if (!_videoPlayer.isPrepared || !_videoPlayer.canSetTime) return;
        double length = _videoPlayer.length;
        _videoPlayer.time = Math.Max(0, length > 0 ? Math.Min(length - 0.5, seconds) : seconds);
    }

    private void CycleFormat()
    {
        _format = (VideoFormat)(((int)_format + 1) % FormatNames.Length);
        if (_current != null) PlayerPrefs.SetString(FormatPrefsPrefix + _current.path, _format.ToString());
        PlayerPrefs.Save();
        ApplyFormat();
    }

    private void ChangeVolume(int direction)
    {
        _volumePercent = AndroidGallery.AdjustVolume(direction);
    }

    #endregion

    #region BIBLIOTHÈQUE

    private IEnumerator InitialLibraryLoad()
    {
        if (!AndroidGallery.HasPermission())
        {
            _libraryStatus.text = "Autorisez l'accès aux vidéos sur l'écran du téléphone...";
            yield return RequestGalleryPermission();
        }
        RefreshLibrary();
    }

    private IEnumerator RequestGalleryPermission()
    {
        _permissionAsked = true;
#if UNITY_ANDROID && !UNITY_EDITOR
        string permission = AndroidGallery.MediaPermission;
        if (Permission.HasUserAuthorizedPermission(permission)) yield break;

        bool done = false;
        bool blocked = false;
        var callbacks = new PermissionCallbacks();
        callbacks.PermissionGranted += _ => done = true;
        callbacks.PermissionDenied += _ => done = true;
#pragma warning disable 0618
        callbacks.PermissionDeniedAndDontAskAgain += _ => { done = true; blocked = true; };
#pragma warning restore 0618
        Permission.RequestUserPermission(permission, callbacks);

        float timeout = Time.realtimeSinceStartup + 120f;
        while (!done && Time.realtimeSinceStartup < timeout) yield return null;

        ApplyAndroidImmersiveMode();
        if (blocked) AndroidGallery.OpenAppSettings();
#else
        yield break;
#endif
    }

    private void ShowLibrary()
    {
        _state = AppState.Library;
        HideControls();
        ApplyFormat();
        _libraryCanvas.gameObject.SetActive(true);
        PlaceInFront(_libraryCanvas.transform, 2.4f, 0f);
        RenderLibraryPage();
    }

    private void RefreshLibrary()
    {
        _videos = AndroidGallery.LoadVideos();
        _page = Mathf.Clamp(_page, 0, Mathf.Max(0, PageCount - 1));
        RenderLibraryPage();
    }

    private int PageCount => Mathf.Max(1, Mathf.CeilToInt(_videos.Count / (float)TilesPerPage));

    private void RenderLibraryPage()
    {
        bool hasPermission = AndroidGallery.HasPermission();
        _permissionButton.Rect.gameObject.SetActive(!hasPermission && _videos.Count == 0);

        if (_videos.Count == 0)
        {
            _libraryStatus.text = hasPermission
                ? "Aucune vidéo trouvée. Ajoutez des vidéos dans la galerie du téléphone puis « Actualiser »."
                : "L'application n'a pas accès à vos vidéos.";
        }
        else
        {
            _libraryStatus.text = $"{_videos.Count} vidéo(s) — visez une vidéo puis appuyez sur le bouton du casque (ou fixez-la 2 s).";
        }
        _pageLabel.text = $"Page {_page + 1} / {PageCount}";

        for (int i = 0; i < TilesPerPage; i++)
        {
            int index = _page * TilesPerPage + i;
            bool visible = index < _videos.Count;
            _tileButtons[i].Rect.gameObject.SetActive(visible);
            if (!visible) continue;

            VideoEntry v = _videos[index];
            _tileNames[i].text = v.name;
            _tileInfos[i].text = FormatInfo(v);

            if (v.id >= 0 && _thumbnails.TryGetValue(v.id, out Texture2D tex) && tex != null)
            {
                _tileThumbs[i].texture = tex;
                _tileThumbs[i].color = Color.white;
            }
            else
            {
                _tileThumbs[i].texture = null;
                _tileThumbs[i].color = new Color(0.1f, 0.1f, 0.12f, 1f);
                if (v.id >= 0 && !_thumbnails.ContainsKey(v.id))
                {
                    _thumbnails[v.id] = null; // demande en cours
                    AndroidGallery.RequestThumbnail(v, gameObject.name, nameof(OnThumbnailReady));
                }
            }
        }
    }

    /// <summary>Appelé depuis Java : "id|chemin_jpg".</summary>
    public void OnThumbnailReady(string message)
    {
        int sep = message.IndexOf('|');
        if (sep <= 0 || !long.TryParse(message.Substring(0, sep), out long id)) return;
        string path = message.Substring(sep + 1);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        try
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (tex.LoadImage(File.ReadAllBytes(path)))
            {
                _thumbnails[id] = tex;
                if (_state == AppState.Library) RenderLibraryPage();
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Galerie] Miniature illisible : " + ex.Message);
        }
    }

    private void OnTileClicked(int slot)
    {
        int index = _page * TilesPerPage + slot;
        if (index >= 0 && index < _videos.Count) PlayVideo(_videos[index]);
    }

    private static string FormatInfo(VideoEntry v)
    {
        var parts = new List<string>();
        if (v.duration > 0) parts.Add(FormatTime(v.duration / 1000.0));
        if (v.width > 0 && v.height > 0) parts.Add($"{v.width}x{v.height}");
        if (v.size > 0) parts.Add($"{v.size / (1024f * 1024f):0} Mo");
        return string.Join("  ·  ", parts);
    }

    private static string FormatTime(double seconds)
    {
        if (double.IsNaN(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
    }

    #endregion

    #region INTERFACE

    private readonly List<GazeButton> _pendingButtons = new List<GazeButton>();

    private GazeButton Track(GazeButton b)
    {
        _pendingButtons.Add(b);
        return b;
    }

    private void RegisterAllButtons()
    {
        foreach (var b in _pendingButtons) _gaze.Register(b);
        _pendingButtons.Clear();
    }

    private void BuildLibraryUI()
    {
        _libraryCanvas = VRUI.CreateCanvas("Library", new Vector2(1600, 1000), 0.0015f);
        Transform root = _libraryCanvas.transform;

        VRUI.Fill(VRUI.CreateImage("Background", root, 0, 0, 1600, 1000, new Color(0.06f, 0.07f, 0.10f, 0.92f)).rectTransform);
        VRUI.CreateText("Title", root, 60, 30, 1000, 80, "Mes vidéos", 60, TextAnchor.MiddleLeft, Color.white);
        _libraryStatus = VRUI.CreateText("Status", root, 60, 110, 1480, 50, "", 30, TextAnchor.MiddleLeft,
                                         new Color(0.75f, 0.8f, 0.9f));

        // Grille 3 × 2
        const float tileW = 460, tileH = 330, gap = 50, startX = 60, startY = 180, rowGap = 30;
        for (int i = 0; i < TilesPerPage; i++)
        {
            int slot = i;
            float x = startX + (i % 3) * (tileW + gap);
            float y = startY + (i / 3) * (tileH + rowGap);

            var tile = VRUI.CreateButton(root, x, y, tileW, tileH, "", 26, () => OnTileClicked(slot));
            tile.Label.gameObject.SetActive(false);

            var thumbRt = VRUI.CreateRect("Thumb", tile.Rect, 10, 10, tileW - 20, (tileW - 20) * 9f / 16f);
            var thumb = thumbRt.gameObject.AddComponent<RawImage>();
            thumb.raycastTarget = false;

            float textY = 10 + (tileW - 20) * 9f / 16f + 6;
            var tileName = VRUI.CreateText("Name", tile.Rect, 14, textY, tileW - 28, 40, "", 28, TextAnchor.UpperLeft, Color.white);
            tileName.horizontalOverflow = HorizontalWrapMode.Wrap;
            var info = VRUI.CreateText("Info", tile.Rect, 14, textY + 38, tileW - 28, 34, "", 22, TextAnchor.UpperLeft,
                                       new Color(0.7f, 0.75f, 0.85f));

            _tileButtons.Add(tile);
            _tileThumbs.Add(thumb);
            _tileNames.Add(tileName);
            _tileInfos.Add(info);
        }

        _permissionButton = Track(VRUI.CreateButton(root, 400, 420, 800, 140, "Autoriser l'accès aux vidéos", 40, () =>
        {
            StartCoroutine(PermissionThenRefresh());
        }));
        _permissionButton.Normal = new Color(0.12f, 0.45f, 0.25f, 0.95f);
        _permissionButton.Background.color = _permissionButton.Normal;

        // Barre du bas
        const float barY = 890, barH = 80;
        Track(VRUI.CreateButton(root, 60, barY, 220, barH, "< Précédent", 30, () =>
        {
            if (_page > 0) { _page--; RenderLibraryPage(); }
        }));
        _pageLabel = VRUI.CreateText("Page", root, 290, barY, 200, barH, "", 30, TextAnchor.MiddleCenter, Color.white);
        Track(VRUI.CreateButton(root, 500, barY, 220, barH, "Suivant >", 30, () =>
        {
            if (_page < PageCount - 1) { _page++; RenderLibraryPage(); }
        }));
        Track(VRUI.CreateButton(root, 780, barY, 230, barH, "Actualiser", 30, RefreshLibrary));
        Track(VRUI.CreateButton(root, 1030, barY, 230, barH, "Recentrer", 30, RecenterView));
        var quit = Track(VRUI.CreateButton(root, 1280, barY, 260, barH, "Quitter", 30, () =>
        {
            if (!kioskMode) Application.Quit();
        }));
        quit.Normal = new Color(0.45f, 0.12f, 0.12f, 0.95f);
        quit.Background.color = quit.Normal;
        quit.AllowDwell = false; // évite de quitter par erreur en fixant le bouton
        quit.Rect.gameObject.SetActive(!kioskMode);
    }

    private IEnumerator PermissionThenRefresh()
    {
        yield return RequestGalleryPermission();
        RefreshLibrary();
    }

    private void BuildControlsUI()
    {
        _controlsCanvas = VRUI.CreateCanvas("PlayerControls", new Vector2(1600, 430), 0.0013f);
        Transform root = _controlsCanvas.transform;

        VRUI.Fill(VRUI.CreateImage("Background", root, 0, 0, 1600, 430, new Color(0.06f, 0.07f, 0.10f, 0.88f)).rectTransform);
        _videoTitle = VRUI.CreateText("Title", root, 30, 15, 1540, 55, "", 34, TextAnchor.MiddleLeft, Color.white);

        // Barre de progression cliquable
        var progress = Track(VRUI.CreateButton(root, 30, 85, 1540, 36, "", 20, null));
        progress.Normal = new Color(0.25f, 0.27f, 0.32f, 1f);
        progress.Hover = new Color(0.32f, 0.35f, 0.42f, 1f);
        progress.Background.color = progress.Normal;
        progress.AllowDwell = false;
        progress.OnClick = pos =>
        {
            if (_videoPlayer.isPrepared && _videoPlayer.length > 0) Seek(pos.x * _videoPlayer.length);
        };
        _progressFill = VRUI.CreateImage("Fill", progress.Rect, 0, 0, 1540, 36, new Color(0.2f, 0.6f, 1f, 1f));
        _progressFill.type = Image.Type.Filled;
        _progressFill.fillMethod = Image.FillMethod.Horizontal;
        // Un sprite est requis pour le remplissage partiel
        _progressFill.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f));
        _progressFill.fillAmount = 0f;

        _timeLabel = VRUI.CreateText("Time", root, 30, 125, 1540, 45, "", 28, TextAnchor.MiddleCenter, Color.white);

        // Boutons
        const float y = 185, h = 105, w = 180, step = 195, x0 = 30;
        Track(VRUI.CreateButton(root, x0 + step * 0, y, w, h, "Vidéos", 30, () => { StopVideo(); ShowLibrary(); }));
        Track(VRUI.CreateButton(root, x0 + step * 1, y, w, h, "-10 s", 32, () => Seek(_videoPlayer.time - 10)));
        _playPauseButton = Track(VRUI.CreateButton(root, x0 + step * 2, y, w, h, "Pause", 32, TogglePlayPause));
        Track(VRUI.CreateButton(root, x0 + step * 3, y, w, h, "+10 s", 32, () => Seek(_videoPlayer.time + 10)));
        Track(VRUI.CreateButton(root, x0 + step * 4, y, w, h, "Vol -", 32, () => ChangeVolume(-1)));
        Track(VRUI.CreateButton(root, x0 + step * 5, y, w, h, "Vol +", 32, () => ChangeVolume(1)));
        Track(VRUI.CreateButton(root, x0 + step * 6, y, w, h, "Format", 32, CycleFormat));
        Track(VRUI.CreateButton(root, x0 + step * 7, y, w, h, "Recentrer", 28, RecenterView));

        _infoLabel = VRUI.CreateText("Info", root, 30, 310, 1540, 100, "", 28, TextAnchor.MiddleCenter,
                                     new Color(0.75f, 0.8f, 0.9f));

        _controlsCanvas.gameObject.SetActive(false);
    }

    private void ShowControls()
    {
        if (!_controlsCanvas.gameObject.activeSelf)
        {
            _controlsCanvas.gameObject.SetActive(true);
            PlaceInFront(_controlsCanvas.transform, 2.0f, -0.75f);
        }
        _controlsHideAt = Time.unscaledTime + controlsAutoHide;
    }

    private void HideControls()
    {
        _controlsCanvas.gameObject.SetActive(false);
    }

    private void UpdatePlayerUI()
    {
        if (!_controlsCanvas.gameObject.activeSelf) return;

        bool prepared = _videoPlayer.isPrepared;
        double length = prepared ? _videoPlayer.length : 0;
        double time = prepared ? _videoPlayer.time : 0;

        _progressFill.fillAmount = length > 0 ? (float)(time / length) : 0f;
        _timeLabel.text = length > 0 ? $"{FormatTime(time)} / {FormatTime(length)}" : "";
        _playPauseButton.SetLabel(_videoPlayer.isPlaying ? "Pause" : "Lecture");

        string volume = _volumePercent >= 0 ? $"Volume {_volumePercent} %" : "";
        string info = $"Format : {FormatNames[(int)_format]}    {volume}";
        _infoLabel.text = string.IsNullOrEmpty(_playerMessage)
            ? info + "\nAppui long sur le bouton du casque : recentrer · X : retour aux vidéos"
            : _playerMessage;

        // Masquage automatique pendant la lecture, sauf si l'utilisateur regarde les commandes
        bool lookingAtControls = _gaze.Hovered != null && _gaze.Hovered.Rect.IsChildOf(_controlsCanvas.transform);
        if (lookingAtControls) _controlsHideAt = Time.unscaledTime + controlsAutoHide;
        if (_videoPlayer.isPlaying && string.IsNullOrEmpty(_playerMessage) && Time.unscaledTime > _controlsHideAt)
        {
            HideControls();
        }
    }

    #endregion
}
