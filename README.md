# Application Mobile VR — Lecteur vidéo 360° (Shinecon G06E / Google Cardboard XR)

Application Android VR pour casques mobiles type **Shinecon G06E** : bibliothèque des vidéos de la galerie du
téléphone, lecture en 360°, 180° ou écran plat, en 2D ou 3D, et commandes au regard.

## Prérequis

- **Unity 6** (`6000.0.58f2` ou plus récent) avec le module **Android Build Support** (SDK/NDK + OpenJDK).
- Un téléphone Android 8.0+ (API 26), ARM64.

## Installation (première ouverture)

1. Cloner le dépôt puis l'ouvrir via **Unity Hub > Ajouter > Ajouter un projet depuis le disque**.
2. **File > Build Profiles** : basculer la plateforme sur **Android**.
3. Menu **VR Kiosk > 1-Click Setup (Projet + Scène)**. Cet outil :
   - règle Android : IL2CPP, ARM64, API 26 minimum, paysage, point d'entrée *Activity* ;
   - passe *Active Input Handling* sur **Both** ;
   - active **Google Cardboard** dans *XR Plug-in Management* (Android) ;
   - génère la scène `Assets/Scenes/VR360_Kiosk.unity` et l'ajoute aux Build Settings.
4. Si Unity demande de redémarrer, accepter puis relancer l'étape 3.
5. **File > Build Profiles > Build And Run**.

Mise à jour du projet : fermer Unity puis `git pull`.

## Utilisation

### Bibliothèque (au lancement)
- Au 1er lancement, Android demande l'**accès aux vidéos** : acceptez (sinon bouton « Autoriser l'accès aux vidéos »).
- Les vidéos de la **galerie du téléphone** s'affichent avec miniature, durée et taille, 6 par page.
- **Viser** une vidéo avec le point blanc au centre de la vue, puis **appuyer sur le bouton du casque**
  (ou **fixer 2 secondes** : l'anneau bleu se remplit) pour la lancer.
- Boutons : `< Précédent` / `Suivant >`, `Actualiser`, `Recentrer`, `Quitter`.

### Lecteur
| Action | Effet |
|---|---|
| Appui sur le bouton du casque (sans viser de bouton) | Affiche / masque les commandes |
| Viser un bouton + appui (ou le fixer 2 s) | Active le bouton |
| Appui long (> 1,2 s) | Recentre la vue |
| Bouton Play/Pause d'écouteurs (USB-C, jack, Bluetooth) | Lecture / Pause |
| **✕** du widget Cardboard ou touche **Retour** | Retour à la bibliothèque (depuis la bibliothèque : quitter) |
| **⚙** du widget Cardboard | Scanner le QR code du casque |

Commandes : `Vidéos`, `-10 s`, `Lecture/Pause`, `+10 s`, `Vol -`, `Vol +`, `Format`, `Recentrer`,
et barre de progression (viser l'endroit voulu + appui).

### Formats vidéo (bouton « Format », mémorisé pour chaque vidéo)
360° 2D · 360° 3D côte à côte · 360° 3D haut/bas · 180° 3D côte à côte · Écran 2D · Écran 3D côte à côte.
Détection automatique au 1er lancement selon les proportions et le nom du fichier
(`360`, `180`, `3D`, `SBS`, `_LR`, `_TB`, `_OU`).

## Casque Shinecon G06E (calibration Cardboard)

- Au 1er lancement, le scanner Cardboard s'ouvre : scanner le **QR code fourni avec le casque**
  (mémorisé ensuite). Le bouton **⚙** permet de le refaire.
- Pour éviter le scan sur un parc de téléphones : renseigner l'URL du QR code dans **Device Params Uri**
  du composant `VR360VideoPlayer` (objet `VR_Video_Manager` de la scène).

## Mode kiosque (optionnel, désactivé par défaut)

Cocher **Kiosk Mode** sur le composant `VR360VideoPlayer` pour épingler l'application, empêcher de quitter
via ✕ / Retour et masquer « Quitter ». Pour désépingler : glisser vers le haut depuis le bas de l'écran et maintenir.

## Structure

- `Assets/Scripts/VR360VideoPlayer.cs` : application (bibliothèque, lecteur, commandes, formats, Cardboard).
- `Assets/Scripts/VRGazeUI.cs` : interface VR au regard (boutons, viseur, clic par fixation).
- `Assets/Scripts/AndroidGallery.cs` + `Assets/Plugins/Android/GalleryBridge.java` : galerie, miniatures, volume.
- `Assets/Plugins/Android/HeadsetButtonBridge.java` : bouton Play/Pause des écouteurs.
- `Assets/Shaders/Stereo360SBS.shader` : shader vidéo multi-format (360°/180°/écran, mono/côte à côte/haut-bas).
- `Assets/Scripts/Editor/VRSceneSetupHelper.cs` : configuration en 1 clic (menu `VR Kiosk`).
- `Assets/Scripts/Editor/CardboardGradleDependencies.cs` : bibliothèques Java requises par Cardboard.
- `Assets/Scripts/Editor/AndroidManifestPermissions.cs` : permissions de lecture vidéo.
- `Packages/com.google.xr.cardboard/` : Google Cardboard XR Plugin 1.36.0 (embarqué).
