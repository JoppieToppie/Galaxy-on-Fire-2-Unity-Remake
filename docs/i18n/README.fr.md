![Menu principal de Galaxy on Fire 2 Remake](../../.github/header.webp)

<h1 align="center">Galaxy on Fire 2 Remake</h1>

<p align="center">
  Un remake amateur du jeu spatial <i>Galaxy on Fire 2</i> (2010) de FISHLABS, reconstruit avec Unity.<br>
  Vol, combat, commerce, minage, stations, bar et toute l'histoire, fidèles à l'original.
</p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <b>Français</b> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.pl.md">Polski</a> ·
  <a href="README.ru.md">Русский</a> ·
  <a href="README.pt-BR.md">Português (Brasil)</a> ·
  <a href="README.nl.md">Nederlands</a> ·
  <a href="README.zh-CN.md">简体中文</a> ·
  <a href="README.hi.md">हिन्दी</a> ·
  <a href="README.ko.md">한국어</a> ·
  <a href="README.ja.md">日本語</a> ·
  <a href="README.ar.md">العربية</a>
</p>

<p align="center">
  <i>Ceci est une traduction ; le <a href="../../README.md">README en anglais</a> fait référence et peut être plus récent.</i>
</p>

<p align="center">
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest"><img alt="Dernière version" src="https://img.shields.io/github/v/release/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?label=release"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases"><img alt="Téléchargements" src="https://img.shields.io/github/downloads/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/total"></a>
  <img alt="Plateformes" src="https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20Android%20%7C%20UWP-blue">
  <img alt="Unity" src="https://img.shields.io/badge/Unity-7000.0.0a12-black?logo=unity">
  <a href="../../LICENSE.md"><img alt="Licence" src="https://img.shields.io/badge/license-Apache%202.0-green"></a>
  <br>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/stargazers"><img alt="Étoiles" src="https://img.shields.io/github/stars/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/forks"><img alt="Forks" src="https://img.shields.io/github/forks/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues"><img alt="Issues ouvertes" src="https://img.shields.io/github/issues/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/pulls"><img alt="Pull requests ouvertes" src="https://img.shields.io/github/issues-pr/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors"><img alt="Contributeurs" src="https://img.shields.io/github/contributors/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/commits/main"><img alt="Dernier commit" src="https://img.shields.io/github/last-commit/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
</p>

<p align="center">
  <a href="#play"><b>Jouer</b></a> ·
  <a href="#features">Fonctionnalités</a> ·
  <a href="#multiplayer">Multijoueur</a> ·
  <a href="#mods">Mods</a> ·
  <a href="#controls">Commandes</a> ·
  <a href="#help"><b>Aider le projet</b></a> ·
  <a href="#credits">Crédits</a>
</p>

---

Le remake repose sur les ressources du jeu original et sur une logique de jeu portée depuis le code décompilé de l'original.

> [!NOTE]
> **État :** la campagne principale et les deux extensions, **Valkyrie** et **Supernova**, se jouent du début à la fin, tout
> comme le mode arcade Supernova Challenge. Le multijoueur, la campagne coop et le Mod Builder sont expérimentaux. La VR
> n'a été testée jusqu'ici que sans casque.

## Quelle section me faut-il ?

| Je veux... | Aller à |
|---|---|
| **Jouer** sur mon PC ou mon téléphone | [Jouer](#play) : télécharger une version, sans Unity |
| Jouer avec des amis ou héberger un serveur | [Multijoueur](#multiplayer) |
| Ajouter des vaisseaux, objets, systèmes ou quêtes | [Mods](#mods) : créés dans le jeu ou sous forme de fichiers de données, sans Unity |
| **Modifier le code ou les ressources du jeu**, ou envoyer une pull request | [Aider le projet](#help) : cloner le dépôt et l'ouvrir dans Unity |

---

<a id="play"></a>

## 🎮 Jouer

Nul besoin d'Unity, de Git ni de ce dépôt pour jouer. Téléchargez la version de votre plateforme depuis la
**[dernière release](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest)** :

| Plateforme | Téléchargement | Installation |
|---|---|---|
| **Windows** 10 / 11 (64 bits) | `GoF2Remake-Windows-<version>.zip` | Décompressez, puis lancez `GoF2Remake.exe` dans le dossier `GoF2Remake` |
| **Linux** (x86_64) | `GoF2Remake-Linux-<version>.zip` | Décompressez, puis lancez `GoF2Remake.x86_64` (s'il ne démarre pas : `chmod +x GoF2Remake.x86_64`) |
| **Android** (ARM64) | `GoF2Remake-Android-<version>.apk` | Copiez l'APK sur le téléphone et ouvrez-le ; autorisez les installations depuis cette source quand on vous le demande |
| **Windows (application UWP)** | `GoF2Remake-UWP-<version>.zip` | Décompressez, clic droit sur `Install.ps1` > **Exécuter avec PowerShell** (il demande d'approuver le certificat de test du paquet) |
| Serveur dédié | `GoF2Remake-WindowsServer-…` / `-LinuxServer-…` | Voir [SERVER.md](../../SERVER.md) ; aussi l'image Docker `ghcr.io/joppietoppie/gof2-server` |

- **Version :** la version de chaque build correspond à la date et à l'heure de sa publication (par exemple
  `2026.11.02.0622`), identique sur toutes les plateformes et affichée dans le menu principal. Le multijoueur ne
  fonctionne qu'entre joueurs ayant la même version.
- **Mises à jour :** le menu principal affiche **Mise à jour disponible** quand une release plus récente existe. À partir
  des builds postérieurs à 2026.11.02, ce bouton met le jeu à jour sur place et ne télécharge que ce qui a changé
  (Windows, Linux et Android ; l'application UWP se met à jour via l'App Installer de Windows). Les builds de 2026.11.02
  et antérieurs nécessitent un dernier téléchargement complet.
- **Les sauvegardes** sont conservées à part des fichiers du jeu : une mise à jour ou une réinstallation ne les touche pas.
  Exportez et importez toutes vos sauvegardes en un seul fichier sous **Charger une partie**, pour les déplacer vers un
  autre PC ou téléphone.
- **La version UWP** n'a ni statut Discord, ni gyroscope de manette, ni boîte de dialogue de sauvegarde, ni VR, ni serveur
  dédié.

> [!TIP]
> Une nouvelle installation se joue comme le **jeu original**. Options > Jouabilité > **Expérience de jeu** active d'un coup
> les ajouts du remake : *Original*, *Cosmétique* (vols dans le hangar et dialogues animés) ou *Remake* (tous les ajouts).
> Chaque option peut aussi être changée séparément.

### Linux et Steam Deck

- **Manettes :** les manettes Xbox, PlayStation et Switch Pro fonctionnent directement. Sur un Steam Deck en mode bureau,
  ou avec une manette que le jeu ne reconnaît pas, ajoutez le jeu à Steam (*Ajouter un jeu non-Steam*) et lancez-le depuis
  Steam : Steam Input le présente alors comme une manette Xbox.
- **Lancé depuis Steam sur un Steam Deck :** Steam définit `SteamDeck=1`, ce qui fait tomber le lecteur Unity Linux à ~0 FPS
  ([#232](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues/232)). Le jeu se relance une fois de lui-même
  sans cette variable (la fenêtre peut s'ouvrir deux fois). Si cela bloque toujours, mettez `env -u SteamDeck %command%`
  dans les options de lancement du raccourci.

---

<a id="features"></a>

## ✨ Fonctionnalités

- **Histoire :** toute la campagne principale avec le prologue, le Void et la fin, plus les extensions Valkyrie et
  Supernova. Dialogues doublés (anglais ou allemand), portraits, bavardages radio et cinématiques.
- **Vol :** le modèle de vol et la caméra de poursuite de l'original, une vue cockpit et un mode de vol libre optionnel dans
  le style d'EVERSPACE 2. Les orbites des stations sont reconstruites exactement comme dans l'original, avec leurs ciels,
  planètes, soleils et champs d'astéroïdes.
- **Voyage :** pilote automatique, sauts planétaires, avance rapide, carte stellaire, portails, propulseur Khador et
  portail du Void. La **planification d'itinéraire par portails** vous mène de portail en portail jusqu'à n'importe quel
  système, par la route la plus rapide ou la plus sûre. Le **Khador Slipstream**, optionnel, est un voyage rapide au sein
  d'un système et entre les systèmes.
- **Combat :** toutes les armes (missiles, rayons, IEM, mines, tourelles, canons sentinelles, le Libérateur...), trafic et IA
  des PNJ, vaisseaux capitaux, bases pirates, Specters et Most Wanted. Camouflage, système d'urgence, ralentisseur de
  temps, rayons de réparation et boucliers gamma. Le mode arcade **Supernova Challenge** avec score de combo et meilleur
  score.
- **Économie et stations :** hangars, boutiques et concessionnaires de vaisseaux, le salon spatial avec ses agents de bar et
  tous les types de missions freelance, plans, ailiers, médailles et le Kaamo Club.
- **Minage :** minage d'astéroïdes avec le mini-jeu de forage, la **Mining Plant** de Mido (minerai en noyaux, tri des
  Space Waste), nuages de gaz, piratage et amarrage aux objets.
- **Campagne coop (expérimentale) :** jouez l'histoire ensemble, de 2 à 4 joueurs par défaut, depuis son propre bouton du
  menu principal. L'hôte continue une sauvegarde solo ou commence une nouvelle partie ; les ennemis peuvent se renforcer
  avec chaque joueur.
- **Multijoueur (expérimental) :** un univers partagé avec escouades, factions, matchs d'arène, commerce et modes de jeu
  événementiels. Voir [Multijoueur](#multiplayer).
- **Mods :** nouveaux objets, vaisseaux, systèmes, stations, personnages, quêtes et campagnes entières, créés avec le
  **Mod Builder** intégré au jeu. Voir [Mods](#mods).
- **VR (expérimentale) :** VR sur PC via OpenXR. Voir [VR](#vr).
- **Chronomètre de speedrun :** splits par chapitre ou par étape de l'histoire, fichiers de splits LiveSplit et lien vers
  LiveSplit.
- **Commandes et écrans :** tactile, inclinaison, clavier et souris, et manettes (boutons Xbox ou PlayStation, choisis
  automatiquement), tout est reconfigurable. Pilotage au gyroscope avec DualSense, DualShock 4 ou Switch Pro sous Windows ;
  retour haptique sur les manettes et les téléphones Android. Écrans paysage du 4:3 jusqu'au 32:9.
- **Langues :** les langues de l'original plus des traductions en néerlandais, chinois simplifié et hindi, avec le coréen,
  le japonais et l'arabe complétés (14 au total), et des voix allemandes ou anglaises.

<details>
<summary><b>Extras du remake</b> (la plupart sont des options sous Options > Jouabilité)</summary>

- **Hangar et présentation :** vols d'arrivée et de départ dans le hangar, ombres douces des vaisseaux, profondeur de champ,
  **Inspecter le vaisseau** (une caméra orbitale autour de votre vaisseau), texte de dialogue animé et bloom façon
  original.
- **Améliorations des vaisseaux capitaux :** escortes, tourelles plus puissantes et salves de missiles ; le porte-vaisseaux
  et le cuirassé vossk peuvent être détruits, de rares batailles de flottes les opposent, et le porte-vaisseaux vend des
  fournitures aux pilotes en qui il a confiance.
- **Événements pirates :** dans les systèmes Risqués et Dangereux, une orbite peut abriter un avant-poste pirate ou un chef
  pirate nommé avec une prime et un niveau de menace, listés sur le tableau Activité pirate de la fenêtre Missions.
- **Ciblage des PNJ plus malin :** les chasseurs ennemis attaquent le vaisseau hostile le plus proche au lieu de tous
  s'en prendre à vous. **L'IEM brise les camouflages**, et le Disrupteur fait deux fois plus de dégâts aux boucliers,
  comme l'indique sa description.
- **Collision sur les formes réelles :** les tirs touchent ce qu'ils frappent et vous glissez le long des stations et des
  cargos. *Collision des vaisseaux* : Forme exacte, Mixte (les chasseurs gardent la plus grande boîte de collision de
  l'original) ou Original.
- **Un avertissement de missile** ; accélérer ou se camoufler fait perdre les missiles à tête chercheuse. Une distance
  d'armement pour les bombes.
- **Nouvelle partie+ :** recommencez avec les crédits, plans, médailles et vaisseaux d'une partie terminée.
- **Options de nouvelle partie :** le Kaamo Club dès le départ, **Hardcore** (mort permanente), une partie sans médailles,
  les info-bulles du tutoriel, et le choix entre les économies PC et Android.
- **Quatre difficultés** (Facile, Normal, Difficile, Extrême), modifiables en cours de partie.
- **Confort de jeu :** Tout vendre / Tout acheter, un verrouillage de cible intelligent avec une commande Libérer la cible,
  la carte de la fenêtre Missions peut mener à la cible d'une mission, une vue d'ensemble plein écran de la carte
  stellaire, les médailles en bref message, un ATH de vol Complet / Minimal / Désactivé (F1), des marqueurs de vaisseaux
  lointains avec distances, des icônes de recharge, un compteur d'FPS et le son de tir propre à chaque arme.
- **Graphismes :** préréglages Bas / Moyen / Élevé, niveaux de détail pour les vaisseaux, stations et astéroïdes lointains,
  SMAA / FXAA / TAA, et upscaling (FSR 1 et STP partout, NVIDIA DLSS et AMD FSR 2 / 3 / 4 sous Windows).
- **Modes photo** (Action Freeze en vol, Inspecter le vaisseau au hangar) : caméra libre, champ de vision, roulis, profondeur
  de champ, angle d'obturation et poses longues, flou de zoom, rendus de film, exposition, couleur, cadres, et captures
  d'écran jusqu'à 4 fois la taille de l'écran.
- **Sauvegardes :** importez une sauvegarde Android ou PC du jeu original, exportez des sauvegardes pour les builds
  communautaires de l'original, et choisissez le dossier de sauvegarde (accessible à des outils de synchronisation comme
  Syncthing).
- **Outils de debug** (Options > Jouabilité) : sauter à n'importe quelle étape de l'histoire, triches, donner des objets,
  créer des vaisseaux et des objets, piloter n'importe quelle coque, enregistrer et charger des configurations de
  vaisseau. L'étape de l'histoire et la station en cours peuvent s'afficher en bas à droite, de sorte qu'une capture d'écran
  de bug montre où il s'est produit.
- **Discord Rich Presence** sous Windows.

</details>

---

<a id="multiplayer"></a>

## 🌐 Multijoueur

Le multijoueur est expérimental. Tous les joueurs d'une session partagent un univers, celui du monde du jeu terminé : vous
vous voyez dans l'espace et dans les hangars, formez des escouades, faites des missions de bar ensemble, partagez les
boutiques et le trafic des PNJ, fondez des factions qui possèdent des stations, échangez crédits et cargaisons, et vous
battez dans des matchs d'arène (ou n'importe où, quand l'hôte choisit le PvP). Rejoignez une partie par le navigateur de
serveurs (avec la région et le ping de chaque partie), par un code d'invitation ou par le réseau local.

Un nouveau pilote commence amarré à Dis dans un Betty avec 10 000 crédits. Un serveur dédié (ou le monde persistant d'un
hôte) conserve la progression de chaque joueur ; vos sauvegardes solo ne sont jamais touchées. Seule la **même version
exacte du jeu** permet de jouer ensemble.

| Guide | Contenu |
|---|---|
| **[Jouer ensemble](../MULTIPLAYER.md)** | Rejoindre et héberger, chat, escouades, missions, boutiques, factions, possession de stations, combats d'arène, admins, toutes les commandes de chat |
| **[Faire tourner un serveur dédié](../../SERVER.md)** | Les téléchargements du serveur Windows / Linux et l'image Docker, options, console, message du jour, devenir admin, ses fichiers |
| **[Comment fonctionne le multijoueur](../MULTIPLAYER-DEV.md)** | Le code, pour les programmeurs |

---

<a id="mods"></a>

## 🧩 Mods

Les mods ajoutent ou modifient le contenu du jeu sous forme de données, sans code et sans Unity : objets (avec leurs
propres effets d'armes et sons), vaisseaux à partir de modèles GLB, skins, systèmes stellaires et stations (avec leurs
propres modèles, planètes, soleils, ciels, hangars et bars), personnages avec portraits et voix, musique et effets sonores,
plans, quêtes et missions de bar, et campagnes entières qui apparaissent sous Nouvelle partie.

**Installer un mod :** dans l'écran **Mods** du menu principal, appuyez sur **Importer un mod** et choisissez le `.zip` du
mod (sur Android aussi). Ou placez vous-même le mod (un dossier ou son `.zip`) dans le dossier Mods, puis activez-le dans
l'écran Mods. Le bouton **Ouvrir le dossier des mods** de l'écran l'ouvre :

| Plateforme | Dossier des mods |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods`, ou un dossier `Mods` à côté de `GoF2Remake.exe` |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods`, ou un dossier `Mods` à côté du jeu |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods` (accessible par USB) |

Une sauvegarde faite avec des mods s'en souvient ; la charger sans eux retire leurs objets (remboursés). En multijoueur,
l'hôte décide si les mods sont autorisés, et les joueurs qui rejoignent ont besoin des mêmes mods.

**Créer un mod :**

- **[Mod Builder](../notes/mod-builder.md)** (Mods > Mod Builder, expérimental) : crée vaisseaux, objets, skins,
  personnages, quêtes (avec un graphe de nœuds), systèmes, stations, hangars et bars, effets d'armes, audio, plans,
  campagnes et kits de vaisseaux dans le jeu, avec annulation, vols d'essai et tests de jeu, puis exporte le mod terminé.
  Il allège aussi les modèles et textures d'un mod et importe les mods communautaires faits à partir des modèles de
  l'original (`.aem` / `.aei`). Votre propre assistant IA peut y travailler via son serveur MCP local, avec les
  permissions que vous choisissez.
- **À la main :** le guide des moddeurs [Modding/README.md](../../Modding/README.md), avec des mods d'exemple dans
  [Modding/Examples](../../Modding/Examples).
- **Avec un assistant IA :** [Modding/ai](../../Modding/ai) contient un guide pour Claude, ChatGPT et d'autres.

---

<a id="controls"></a>

## 🕹️ Commandes

<details>
<summary><b>Clavier et souris</b></summary>

| Touche | Action |
|---|---|
| Flèches | Diriger |
| W | Boost |
| S | Frein (les moteurs s'arrêtent tant que la touche est enfoncée) |
| `]` / `/` ou la molette | Poussée |
| Espace / clic gauche | Armes primaires |
| R / clic droit | Arme secondaire |
| G | Changer d'arme secondaire |
| A / D | Déplacement latéral gauche / droite |
| 1 / 3 | Roulis à gauche / à droite |
| 2 | Se redresser |
| F / Entrée | Action (s'amarrer, pilote automatique, miner, sauter) |
| Q | Menu du pilote automatique |
| E | Menu des actions (armes secondaires, ailiers, camouflage, propulseur Khador) |
| Tab | Avance rapide |
| T | Caméra : poursuite, cockpit, vue de tourelle, vue libre |
| V / K / C / X | Ailiers / propulseur Khador / camouflage / ralentisseur de temps |
| M ou clic molette | Activer / désactiver le pilotage à la souris |
| U | Libérer la cible |
| B / N | Chat / fenêtre multijoueur (multijoueur) |
| F1 | ATH de vol : complet, minimal, désactivé |
| F12 | Capture d'écran |
| Échap | Pause |

</details>

Toutes les commandes de vol peuvent être reconfigurées dans Options > Touches (deux touches clavier / souris et un bouton
de manette chacune). Les manettes et le tactile sont entièrement pris en charge, et les aides à l'écran affichent les
boutons de la dernière entrée utilisée.

<a id="vr"></a>

### VR (expérimentale)

Lancez la version Windows avec `-vr` (par exemple un raccourci vers `GoF2Remake.exe -vr`), avec un casque de VR sur PC
branché et un runtime OpenXR actif (SteamVR, Meta Quest Link, Windows Mixed Reality). Sans casque, le jeu démarre
normalement.

<details>
<summary>À quoi ressemble la VR</summary>

- **Vol :** vous êtes assis dans un cockpit. Boucliers, coque et soute sont sur ses écrans, le radar au milieu du tableau
  de bord, le reste de l'ATH sur la verrière. Les cinématiques gardent l'horizon droit et fondent d'un plan à l'autre.
- **Stations :** vous vous tenez dans le hangar à côté de votre vaisseau (saisissez-le avec la prise et tirez pour le
  faire tourner) et dans le bar (pointez un visiteur et pressez la gâchette pour lui parler).
- **Les menus** apparaissent sur un écran flottant ; pointez avec la manette droite et pressez la gâchette.
- **Commandes :** les manettes fonctionnent comme un gamepad (sticks, gâchettes, A / B / X / Y, les prises comme LB / RB).
  Avec Options > Commandes > « Vol en VR : saisir le manche et la manette des gaz », vous pilotez à la place avec le
  manche latéral et la manette des gaz du cockpit.

Le cockpit est le même pour tous les vaisseaux pour l'instant.

</details>

---

<a id="help"></a>

## 🛠️ Aider le projet

Cette section s'adresse à ceux qui veulent modifier le jeu lui-même : son code, ses scènes et ses ressources, ou compiler
leurs propres versions. **Pour simplement jouer, utilisez une [release](#play) ; pour ajouter du contenu, un
[mod](#mods) suffit généralement.**

### Prérequis

- **Unity 7000.0.0a12** (une alpha, installée via Unity Hub). Ajoutez les modules de prise en charge des plateformes pour
  lesquelles vous compilez : *Windows Build Support (IL2CPP)*, *Linux Build Support (IL2CPP)*, *Android Build Support*, et
  les modules *Dedicated Server* pour les builds de serveur.
- **Git** avec **[Git LFS](https://git-lfs.com)**. Les modèles, textures et sons représentent environ 2,1 Go dans LFS,
  récupérés depuis le serveur LFS propre au projet (défini dans [.lfsconfig](../../.lfsconfig)).
- Pour les **builds Windows :** Visual Studio 2022 ou plus récent avec la charge de travail *Développement Desktop en C++*
  et un SDK Windows 10 / 11 (IL2CPP). Le build UWP demande aussi les *outils C++ de la plateforme Windows universelle*.

### Lancer le projet

```bash
git lfs install
```

```bash
git clone https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake.git
```

1. Ouvrez le dossier cloné dans Unity Hub (*Add > Add project from disk*) avec Unity 7000.0.0a12. Le premier import prend un
   moment.
2. Ouvrez `Assets/Scenes/Splash.unity` (ou `MainMenu.unity` pour passer les logos) et appuyez sur **Play**.

Les scènes sont `Splash` (index de build 0), `MainMenu`, `Space` (le niveau de vol) et `Station` (amarré). Les ressources
générées (prefabs, matériaux, icônes, images d'ATH) sont déjà dans le dépôt ; le menu **GoF2** de l'éditeur les
reconstruit, et **GoF2 > Tools Overview** liste tous les outils.

> [!IMPORTANT]
> Ne générez pas l'éclairage des scènes. Elles n'ont volontairement aucun éclairage pré-calculé : le jeu construit ses
> niveaux à l'exécution et chaque système a son propre ciel (`SkyReflection` en déduit la lumière ambiante). Une sonde
> pré-calculée serait un ciel unique et figé pour tous les systèmes.

### Compiler

- **Changez toujours le profil de build actif** (File > Build Profiles > Switch Profile) avant de compiler pour une autre
  plateforme. URP ne garde les variantes de shaders que pour la plateforme active : un build Android fait pendant que
  Windows était actif s'affiche en noir, c'est pourquoi le script d'éditeur `BuildTargetGuard` le refuse.
- `BuildVersionStamp` donne à chaque build sa version d'après la date et l'heure de son commit git. Pour une release,
  définissez la variable d'environnement `GOF2_BUILD_VERSION` du processus de l'éditeur afin que toutes les plateformes
  aient la même.
- Serveurs dédiés : **GoF2 > Build > Linux Dedicated Server** / **Windows Dedicated Server**. Le dossier `docker/`
  contient l'image du serveur.
- Patchs de mise à jour pour le système de mise à jour intégré : **GoF2 > Build > Update Patch** après les builds.

### Tests

Environ 3000 tests en mode édition couvrent les règles et les données, plus des tests de fumée en mode lecture des scènes,
des niveaux de l'histoire et d'une session hébergée. Lancez-les depuis Window > General > Test Runner, ou avec la CLI
`unity` connectée :

```bash
python -I Reference/tools/tests/run_tests.py
```

Voir [Assets/Scripts/Tests/README.md](../../Assets/Scripts/Tests/README.md).

### Organisation du projet

```
Assets/Scripts/Runtime/   game code in layered assemblies: Core <- Game <- Space <- Net <- App
Assets/Scripts/Editor/    import settings, asset and prefab builders, the GoF2 menu
Assets/Scripts/Tests/     edit-mode and play-mode tests
Assets/Resources/         game data (JSON), assembled prefabs, sky / HUD / combat assets
Assets/UI/                UI Toolkit screens (UXML / USS)
Modding/                  the modders' guide and example mods
Reference/                decompiled original code, research notes and conversion tools
docs/                     multiplayer guides and the per-area technical notes
```

### Documentation

- [CLAUDE.md](../../CLAUDE.md) : les règles du projet (conventions, coordonnées, données) et l'index des notes techniques.
- [docs/notes/](../notes) : un fichier par domaine du jeu (vol, combat, histoire, boutique, multijoueur, mods...), avec les
  fonctions originales sur lesquelles repose chaque système et les choix faits par le remake.
- [Assets/Scripts/Runtime/README.md](../../Assets/Scripts/Runtime/README.md) : les couches d'assemblages et leurs hooks.
- [Reference/README.md](../../Reference/README.md) : le code décompilé et les outils qui le lisent.

### Contribuer

- **Un bug ?** Ouvrez une [issue](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues) avec votre
  plateforme, la version du jeu (affichée dans le menu principal) et une capture d'écran. Options > Jouabilité peut
  afficher l'étape de l'histoire et la station en cours en bas à droite, ce qui nous dit où cela s'est produit.
- **Les pull requests sont les bienvenues.** Forkez le dépôt, faites votre modification sur une branche, lancez les tests
  en mode édition et ouvrez une pull request vers `main`. Avant de travailler sur un domaine, lisez son fichier dans
  [docs/notes/](../notes) ; les conventions sont dans [CLAUDE.md](../../CLAUDE.md).
- **Le projet vous plaît ?** Donnez-lui une ⭐ sur GitHub, cela aide les autres à le trouver.

---

<a id="credits"></a>

## 🙏 Crédits

**Galaxy on Fire 2 Remake** par JoppieToppie, avec des pull requests de KiritoJPK, DuIslingr, Zippadelic, FantasyToLife et
d'autres.

Le remake repose sur la **version FULL HD et les modifications de KiritoJPK**, grâce à la communauté Galaxy on Fire 2™ et
4PDA : <https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

Polices : la police d'interface du jeu original, et Inter (SIL Open Font License). Gyroscope de manette :
[JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary) de Julian Smart (licence MIT). Réalisé avec Unity, le
Universal Render Pipeline, Netcode for GameObjects et OpenXR.

### Contributeurs

<a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors">
  <img alt="Contributeurs" src="https://contrib.rocks/image?repo=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake">
</a>

## ⭐ Historique des étoiles

<a href="https://star-history.com/#JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&Date">
  <img alt="Graphique de l'historique des étoiles" src="https://api.star-history.com/svg?repos=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&type=Date">
</a>

## ⚖️ Mentions légales

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ et ABYSS®
sont des marques déposées de FISHLABS Entertainment GmbH. Tous droits réservés. Ceci est un projet amateur non officiel,
non affilié à FISHLABS ou Deep Silver, et non approuvé par eux.

Le code propre au remake est placé sous [licence Apache 2.0](../../LICENSE.md).
