#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

public class VRSceneSetupHelper : MonoBehaviour
{
    [MenuItem("VR Kiosk/1-Click Setup VR 360 Scene")]
    public static void SetupVRScene()
    {
        // 1. Configurer ou trouver la caméra
        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            GameObject camGo = new GameObject("Main Camera");
            mainCam = camGo.AddComponent<Camera>();
            camGo.tag = "MainCamera";
        }

        mainCam.transform.position = Vector3.zero;
        mainCam.transform.rotation = Quaternion.identity;
        mainCam.clearFlags = CameraClearFlags.SolidColor;
        mainCam.backgroundColor = Color.black;
        mainCam.nearClipPlane = 0.1f;
        mainCam.farClipPlane = 200f;

        // 2. Créer le matériau avec le shader VR/Stereo360_SideBySide
        Shader sbsShader = Shader.Find("VR/Stereo360_SideBySide");
        if (sbsShader == null)
        {
            Debug.LogError("Shader 'VR/Stereo360_SideBySide' introuvable ! Vérifiez le dossier Assets/Shaders.");
            return;
        }

        Material sbsMaterial = new Material(sbsShader);
        AssetDatabase.CreateAsset(sbsMaterial, "Assets/Materials/M_Stereo360_SBS.mat");
        AssetDatabase.SaveAssets();

        // 3. Créer la sphère 360 inversée
        GameObject sphere = GameObject.Find("VR360_Sphere");
        if (sphere == null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "VR360_Sphere";
        }

        sphere.transform.position = Vector3.zero;
        sphere.transform.rotation = Quaternion.identity;
        // Échelle 50 pour envelopper la caméra confortablement
        sphere.transform.localScale = new Vector3(50f, 50f, 50f);

        // Supprimer le collider inutile
        Collider col = sphere.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        Renderer sphereRenderer = sphere.GetComponent<Renderer>();
        sphereRenderer.sharedMaterial = sbsMaterial;

        // 4. Configurer le gestionnaire vidéo VR360VideoPlayer
        GameObject manager = GameObject.Find("VR_Video_Manager");
        if (manager == null)
        {
            manager = new GameObject("VR_Video_Manager");
        }

        VideoPlayer vp = manager.GetComponent<VideoPlayer>();
        if (vp == null) vp = manager.AddComponent<VideoPlayer>();

        AudioSource audioSource = manager.GetComponent<AudioSource>();
        if (audioSource == null) audioSource = manager.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        VR360VideoPlayer playerScript = manager.GetComponent<VR360VideoPlayer>();
        if (playerScript == null) playerScript = manager.AddComponent<VR360VideoPlayer>();

        // Assigner la référence du renderer via SerializedObject
        SerializedObject so = new SerializedObject(playerScript);
        SerializedProperty targetRendererProp = so.FindProperty("targetSphereRenderer");
        if (targetRendererProp != null)
        {
            targetRendererProp.objectReferenceValue = sphereRenderer;
            so.ApplyModifiedProperties();
        }

        EditorUtility.DisplayDialog("Succès", 
            "La scène VR 360 Kiosk a été générée avec succès !\n\n" +
            "- Sphère 360 inversée avec Shader SBS configurée.\n" +
            "- Caméra centrée à (0,0,0).\n" +
            "- VideoPlayer et script VR360VideoPlayer prêts.", "OK");

        Selection.activeGameObject = manager;
    }
}
#endif
