#if UNITY_EDITOR && UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Ajoute au manifeste Android les permissions de lecture des vidéos du dossier public Movies :
///  - READ_MEDIA_VIDEO (Android 13+)
///  - READ_EXTERNAL_STORAGE (Android 12 et inférieur)
/// </summary>
public class AndroidManifestPermissions : IPostGenerateGradleAndroidProject
{
    private const string AndroidNs = "http://schemas.android.com/apk/res/android";

    public int callbackOrder => 100;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        string manifestPath = Path.Combine(unityLibraryPath, "src", "main", "AndroidManifest.xml");
        if (!File.Exists(manifestPath))
        {
            Debug.LogWarning("[VR Kiosk] AndroidManifest.xml introuvable : " + manifestPath);
            return;
        }

        var doc = new XmlDocument();
        doc.Load(manifestPath);
        XmlElement manifest = doc.DocumentElement;

        AddPermission(doc, manifest, "android.permission.READ_MEDIA_VIDEO", null);
        AddPermission(doc, manifest, "android.permission.READ_EXTERNAL_STORAGE", "32");

        doc.Save(manifestPath);
    }

    private static void AddPermission(XmlDocument doc, XmlElement manifest, string name, string maxSdk)
    {
        foreach (XmlNode node in manifest.SelectNodes("uses-permission"))
        {
            if (node is XmlElement el && el.GetAttribute("name", AndroidNs) == name) return;
        }

        XmlElement perm = doc.CreateElement("uses-permission");
        perm.SetAttribute("name", AndroidNs, name);
        if (maxSdk != null) perm.SetAttribute("maxSdkVersion", AndroidNs, maxSdk);
        manifest.PrependChild(perm);
    }
}
#endif
