# Application Mobile VR 360° Kiosque Hors-Ligne (Shinecon G06E / Google Cardboard XR)

Application Android VR autonome haute performance conçue pour les casques mobiles type **Shinecon G06E** avec lecteur vidéo 360° stéréoscopique **Side-by-Side (SBS)**, mode kiosque verrouillé (zéro barre de navigation), gestion du bouton tactile/carte son USB-C, et recentrage automatique.

## Structure des Fichiers Créés

- `Assets/Shaders/Stereo360SBS.shader` : Shader Unlit stéréoscopique ultra-léger avec séparation œil gauche / œil droit (`unity_StereoEyeIndex`) et rendu intérieur de sphère (`Cull Front`).
- `Assets/Scripts/VR360VideoPlayer.cs` : Script principal gérant la lecture MP4 locale, la boucle infinie, le mode immersif Android Sticky, l'anti-rebond, le basculement Play/Pause et le recentrage Cardboard XR.
- `Assets/Scripts/Editor/VRSceneSetupHelper.cs` : Outil Unity Editor pour générer la scène en 1 clic via le menu `VR Kiosk > 1-Click Setup VR 360 Scene`.
- `Assets/StreamingAssets/` : Emplacement optionnel pour intégrer directement la vidéo MP4 dans l'APK.
