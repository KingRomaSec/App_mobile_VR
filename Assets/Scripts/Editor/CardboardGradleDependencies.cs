#if UNITY_EDITOR && UNITY_ANDROID
using System.IO;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Ajoute au projet Gradle les dépendances Java requises par le plugin Google Cardboard
/// (sans elles, l'application plante au démarrage) :
///  - androidx.appcompat, material : thème et interface du scanner de QR code
///  - play-services-vision : lecture du QR code du casque
///  - protobuf-javalite : décodage des paramètres du casque
/// Équivalent automatique du "Custom Main Gradle Template" du guide Cardboard.
/// </summary>
public class CardboardGradleDependencies : IPostGenerateGradleAndroidProject
{
    private const string Marker = "// VR Kiosk: dépendances Google Cardboard";

    private static readonly string[] Dependencies =
    {
        "androidx.appcompat:appcompat:1.6.1",
        "com.google.android.gms:play-services-vision:20.1.3",
        "com.google.android.material:material:1.6.1",
        "com.google.protobuf:protobuf-javalite:3.19.4",
    };

    public int callbackOrder => 90;

    public void OnPostGenerateGradleAndroidProject(string unityLibraryPath)
    {
        string groovy = Path.Combine(unityLibraryPath, "build.gradle");
        string kotlin = Path.Combine(unityLibraryPath, "build.gradle.kts");

        if (File.Exists(groovy))
        {
            AppendDependencies(groovy, d => $"    implementation '{d}'");
        }
        else if (File.Exists(kotlin))
        {
            AppendDependencies(kotlin, d => $"    implementation(\"{d}\")");
        }
        else
        {
            Debug.LogError("[VR Kiosk] build.gradle introuvable dans " + unityLibraryPath +
                           " : dépendances Cardboard non ajoutées, l'application plantera.");
        }

        EnsureGradleProperty(Path.Combine(Directory.GetParent(unityLibraryPath).FullName, "gradle.properties"),
                             "android.useAndroidX", "true");
    }

    private static void AppendDependencies(string buildFile, System.Func<string, string> format)
    {
        string content = File.ReadAllText(buildFile);
        if (content.Contains(Marker)) return;

        var block = new System.Text.StringBuilder();
        block.AppendLine();
        block.AppendLine(Marker);
        block.AppendLine("dependencies {");
        foreach (string dep in Dependencies) block.AppendLine(format(dep));
        block.AppendLine("}");

        File.AppendAllText(buildFile, block.ToString());
        Debug.Log("[VR Kiosk] Dépendances Cardboard ajoutées à " + buildFile);
    }

    private static void EnsureGradleProperty(string propertiesFile, string key, string value)
    {
        string content = File.Exists(propertiesFile) ? File.ReadAllText(propertiesFile) : "";
        if (content.Contains(key + "=")) return;
        File.AppendAllText(propertiesFile, $"\n{key}={value}\n");
    }
}
#endif
