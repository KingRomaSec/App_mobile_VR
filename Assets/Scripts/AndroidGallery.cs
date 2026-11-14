using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Une vidéo de la bibliothèque (galerie Android ou dossier local).
/// </summary>
[Serializable]
public class VideoEntry
{
    public long id = -1;        // identifiant MediaStore (-1 = fichier local hors galerie)
    public string name;
    public string path;
    public long duration;       // millisecondes (0 = inconnu)
    public int width;
    public int height;
    public long size;
}

[Serializable]
internal class VideoEntryList
{
    public VideoEntry[] items;
}

/// <summary>
/// Accès à la galerie vidéo du téléphone (via GalleryBridge.java) et au volume système.
/// Dans l'éditeur : liste les vidéos du dossier "Vidéos" de l'ordinateur.
/// </summary>
public static class AndroidGallery
{
    private static readonly string[] Extensions = { ".mp4", ".m4v", ".mkv", ".webm", ".mov", ".3gp" };

    public static int SdkInt
    {
        get
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    return version.GetStatic<int>("SDK_INT");
                }
            }
            catch (Exception)
            {
                return 0;
            }
#else
            return 0;
#endif
        }
    }

    /// <summary>Permission de lecture des vidéos selon la version d'Android.</summary>
    public static string MediaPermission =>
        SdkInt >= 33 ? "android.permission.READ_MEDIA_VIDEO" : "android.permission.READ_EXTERNAL_STORAGE";

    public static bool HasPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return UnityEngine.Android.Permission.HasUserAuthorizedPermission(MediaPermission);
#else
        return true;
#endif
    }

    /// <summary>Liste toutes les vidéos accessibles (galerie + dossier de l'application).</summary>
    public static List<VideoEntry> LoadVideos()
    {
        var list = new List<VideoEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var bridge = new AndroidJavaClass("com.vrkiosk.GalleryBridge"))
            using (var activity = GetActivity())
            {
                string json = bridge.CallStatic<string>("queryVideos", activity);
                var parsed = JsonUtility.FromJson<VideoEntryList>(json);
                if (parsed?.items != null)
                {
                    foreach (var entry in parsed.items)
                    {
                        if (!string.IsNullOrEmpty(entry.path) && seen.Add(entry.path)) list.Add(entry);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Galerie] Lecture de la galerie impossible : " + ex.Message);
        }
#else
        AddFolder(list, seen, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
        AddFolder(list, seen, Application.streamingAssetsPath);
#endif
        // Vidéos copiées dans le dossier de l'application (aucune permission requise)
        AddFolder(list, seen, Application.persistentDataPath);

        return list;
    }

    private static void AddFolder(List<VideoEntry> list, HashSet<string> seen, string folder)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(Extensions, ext) < 0 || !seen.Add(file)) continue;
                list.Add(new VideoEntry
                {
                    name = Path.GetFileName(file),
                    path = file,
                    size = new FileInfo(file).Length
                });
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Galerie] Dossier illisible {folder} : {ex.Message}");
        }
    }

    /// <summary>
    /// Demande la miniature d'une vidéo de la galerie. La réponse arrive via
    /// UnitySendMessage(gameObjectName, method, "id|chemin_jpg").
    /// </summary>
    public static void RequestThumbnail(VideoEntry entry, string gameObjectName, string method)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (entry.id < 0) return;
        try
        {
            string outPath = Path.Combine(Application.temporaryCachePath, "thumbs", entry.id + ".jpg");
            using (var bridge = new AndroidJavaClass("com.vrkiosk.GalleryBridge"))
            using (var activity = GetActivity())
            {
                bridge.CallStatic("requestThumbnail", activity, entry.id, outPath, gameObjectName, method);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Galerie] Miniature impossible : " + ex.Message);
        }
#endif
    }

    /// <summary>Monte (+1) ou baisse (-1) le volume. Retourne le volume en %.</summary>
    public static int AdjustVolume(int direction)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var bridge = new AndroidJavaClass("com.vrkiosk.GalleryBridge"))
            using (var activity = GetActivity())
            {
                return bridge.CallStatic<int>("adjustVolume", activity, direction);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Volume] " + ex.Message);
            return -1;
        }
#else
        AudioListener.volume = Mathf.Clamp01(AudioListener.volume + 0.1f * Mathf.Sign(direction));
        return Mathf.RoundToInt(AudioListener.volume * 100f);
#endif
    }

    public static int VolumePercent()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var bridge = new AndroidJavaClass("com.vrkiosk.GalleryBridge"))
            using (var activity = GetActivity())
            {
                return bridge.CallStatic<int>("getVolumePercent", activity);
            }
        }
        catch (Exception)
        {
            return -1;
        }
#else
        return Mathf.RoundToInt(AudioListener.volume * 100f);
#endif
    }

    /// <summary>Ouvre les réglages de l'application (permission refusée définitivement).</summary>
    public static void OpenAppSettings()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var bridge = new AndroidJavaClass("com.vrkiosk.GalleryBridge"))
            using (var activity = GetActivity())
            {
                bridge.CallStatic("openAppSettings", activity);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Galerie] " + ex.Message);
        }
#endif
    }

    /// <summary>Désépingle l'application si elle était épinglée.</summary>
    public static void StopLockTask()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (var bridge = new AndroidJavaClass("com.vrkiosk.GalleryBridge"))
            using (var activity = GetActivity())
            {
                bridge.CallStatic("stopLockTask", activity);
            }
        }
        catch (Exception) { }
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject GetActivity()
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        {
            return unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        }
    }
#endif
}
