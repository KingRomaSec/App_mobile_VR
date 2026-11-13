# Application Mobile VR 360° Kiosque Hors-Ligne (Shinecon G06E / Google Cardboard XR)

Application Android VR autonome haute performance conçue pour les casques mobiles type **Shinecon G06E** avec lecteur vidéo 360° stéréoscopique **Side-by-Side (SBS)**, mode kiosque verrouillé (zéro barre de navigation), gestion du bouton tactile/carte son USB-C, et recentrage automatique.

## Prérequis

- **Unity 6** (`6000.0.58f2` ou plus récent) avec le module **Android Build Support** (SDK/NDK + OpenJDK).
- Un téléphone Android 7.0+ (API 24), ARM64.

## Installation (première ouverture)

1. Cloner le dépôt puis l'ouvrir via **Unity Hub > Add > Add project from disk**.
   Unity installe automatiquement les paquets listés dans `Packages/manifest.json`
   (dont le plugin Google Cardboard embarqué dans `Packages/com.google.xr.cardboard`).
2. **File > Build Profiles** : basculer la plateforme sur **Android**.
3. Menu **VR Kiosk > 1-Click Setup (Projet + Scène)**. Cet outil :
   - règle Android : IL2CPP, ARM64, API 24 minimum, paysage gauche, plein écran ;
   - passe *Active Input Handling* sur **Both** (le plugin Cardboard utilise le nouvel Input System) ;
   - active **Google Cardboard** dans *XR Plug-in Management* (Android) ;
   - génère et sauvegarde la scène `Assets/Scenes/VR360_Kiosk.unity` et l'ajoute aux Build Settings.
4. Si Unity demande de redémarrer (changement d'Input Handling), accepter puis relancer l'étape 3.
5. **Commiter** les fichiers générés (`Assets/Scenes`, `Assets/Materials`, `Assets/XR`, `ProjectSettings/`, `.meta`).

## Vidéo

Le lecteur cherche la vidéo dans cet ordre :

1. `/storage/emulated/0/Android/data/<package>/files/video.mp4` (ou le premier `.mp4` trouvé) — recommandé, à copier par USB/`adb push` ;
2. `Assets/StreamingAssets/video.mp4` (embarqué dans l'APK — non versionné dans git) ;
3. `/storage/emulated/0/Movies/video.mp4`.

Format attendu : MP4 H.264/H.265, équirectangulaire 360°, **Side-by-Side** (œil gauche à gauche).

## Structure

- `Assets/Shaders/Stereo360SBS.shader` : Shader Unlit stéréoscopique SBS (`unity_StereoEyeIndex`, `Cull Front`).
- `Assets/Scripts/VR360VideoPlayer.cs` : lecture MP4 locale en boucle, mode immersif, anti-rebond, Play/Pause (appui court), recentrage (appui long).
- `Assets/Scripts/Editor/VRSceneSetupHelper.cs` : outil de configuration en 1 clic (menu `VR Kiosk`).
- `Packages/com.google.xr.cardboard/` : Google Cardboard XR Plugin 1.36.0 (embarqué).
