![Galaxy on Fire 2 Remake Hauptmenü](../../.github/header.webp)

<h1 align="center">Galaxy on Fire 2 Remake</h1>

<p align="center">
  Ein Fan-Remake des Weltraumspiels <i>Galaxy on Fire 2</i> von FISHLABS aus dem Jahr 2010, neu aufgebaut in Unity.<br>
  Flug, Kampf, Handel, Erzabbau, Stationen, die Bar und die ganze Geschichte, originalgetreu.
</p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <b>Deutsch</b> ·
  <a href="README.fr.md">Français</a> ·
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
  <i>Dies ist eine Übersetzung; die <a href="../../README.md">englische README</a> ist maßgeblich und kann neuer sein.</i>
</p>

<p align="center">
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest"><img alt="Neueste Version" src="https://img.shields.io/github/v/release/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?label=release"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/total"></a>
  <img alt="Plattformen" src="https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20Android%20%7C%20UWP-blue">
  <img alt="Unity" src="https://img.shields.io/badge/Unity-7000.0.0a12-black?logo=unity">
  <a href="../../LICENSE.md"><img alt="Lizenz" src="https://img.shields.io/badge/license-Apache%202.0-green"></a>
  <br>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/stargazers"><img alt="Sterne" src="https://img.shields.io/github/stars/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/forks"><img alt="Forks" src="https://img.shields.io/github/forks/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues"><img alt="Offene Issues" src="https://img.shields.io/github/issues/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/pulls"><img alt="Offene Pull Requests" src="https://img.shields.io/github/issues-pr/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors"><img alt="Mitwirkende" src="https://img.shields.io/github/contributors/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/commits/main"><img alt="Letzter Commit" src="https://img.shields.io/github/last-commit/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
</p>

<p align="center">
  <a href="#play"><b>Das Spiel spielen</b></a> ·
  <a href="#features">Funktionen</a> ·
  <a href="#multiplayer">Mehrspieler</a> ·
  <a href="#mods">Mods</a> ·
  <a href="#controls">Steuerung</a> ·
  <a href="#help"><b>Beim Projekt mithelfen</b></a> ·
  <a href="#credits">Danksagung</a>
</p>

---

Das Remake baut auf den Assets des Originalspiels und auf Spiellogik auf, die aus dem dekompilierten Originalcode übernommen wurde.

> [!NOTE]
> **Stand:** Die Hauptkampagne und beide Add-ons, **Valkyrie** und **Supernova**, lassen sich von Anfang bis Ende
> durchspielen, ebenso der Arcade-Modus Supernova Challenge. Mehrspieler, die Koop-Kampagne und der Mod Builder sind
> experimentell. VR wurde bisher nur ohne Headset getestet.

## Welcher Abschnitt ist der richtige?

| Ich möchte... | Weiter zu |
|---|---|
| **Das Spiel spielen** auf dem PC oder Handy | [Das Spiel spielen](#play): eine Version herunterladen, Unity ist nicht nötig |
| Mit Freunden spielen oder einen Server betreiben | [Mehrspieler](#multiplayer) |
| Schiffe, Gegenstände, Systeme oder Quests hinzufügen | [Mods](#mods): im Spiel oder als Datendateien gebaut, Unity ist nicht nötig |
| **Code oder Assets des Spiels ändern** oder einen Pull Request schicken | [Beim Projekt mithelfen](#help): das Repository klonen und in Unity öffnen |

---

<a id="play"></a>

## 🎮 Das Spiel spielen

Zum Spielen brauchst du weder Unity noch Git noch dieses Repository. Lade den Build für deine Plattform von der
**[neuesten Version](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest)** herunter:

| Plattform | Download | Installation |
|---|---|---|
| **Windows** 10 / 11 (64 Bit) | `GoF2Remake-Windows-<version>.zip` | Entpacken, dann `GoF2Remake.exe` im Ordner `GoF2Remake` starten |
| **Linux** (x86_64) | `GoF2Remake-Linux-<version>.zip` | Entpacken, dann `GoF2Remake.x86_64` starten (startet es nicht: `chmod +x GoF2Remake.x86_64`) |
| **Android** (ARM64) | `GoF2Remake-Android-<version>.apk` | Die APK aufs Handy kopieren und öffnen; Installationen aus dieser Quelle erlauben, wenn du gefragt wirst |
| **Windows (UWP-App)** | `GoF2Remake-UWP-<version>.zip` | Entpacken, Rechtsklick auf `Install.ps1` > **Mit PowerShell ausführen** (es fragt, ob du dem Testzertifikat des Pakets vertraust) |
| Dedizierter Server | `GoF2Remake-WindowsServer-…` / `-LinuxServer-…` | Siehe [SERVER.md](../../SERVER.md); auch als Docker-Image `ghcr.io/joppietoppie/gof2-server` |

- **Version:** Die Version jedes Builds ist Datum und Uhrzeit seiner Veröffentlichung (zum Beispiel `2026.11.02.0622`),
  auf allen Plattformen gleich, im Hauptmenü zu sehen. Mehrspieler funktioniert nur zwischen Spielern mit derselben Version.
- **Aktualisieren:** Das Hauptmenü zeigt **Update verfügbar**, sobald eine neuere Version erschienen ist. Ab Builds nach
  2026.11.02 aktualisiert dieser Knopf das Spiel an Ort und Stelle und lädt nur herunter, was sich geändert hat (Windows,
  Linux und Android; die UWP-App wird über den App-Installer von Windows aktualisiert). Builds von 2026.11.02 und älter
  brauchen noch einmal einen kompletten Download.
- **Spielstände** werden getrennt von den Spieldateien gespeichert, sie bleiben also beim Aktualisieren oder Neuinstallieren
  erhalten. Unter **Spiel laden** kannst du alle Spielstände als eine Datei exportieren und importieren, um sie auf einen
  anderen PC oder ein anderes Handy mitzunehmen.
- **Die UWP-Version** hat keinen Discord-Status, kein Controller-Gyro, keinen Dateidialog zum Speichern, kein VR und keinen
  dedizierten Server.

> [!TIP]
> Eine neue Installation spielt sich wie das **Originalspiel**. Optionen > Gameplay > **Spielerlebnis** schaltet die
> Erweiterungen des Remakes auf einen Schlag ein: *Original*, *Kosmetisch* (Hangarflüge und animierte Dialoge) oder
> *Remake* (alle Erweiterungen). Jede Option lässt sich auch einzeln ändern.

### Linux und Steam Deck

- **Controller:** Xbox-, PlayStation- und Switch-Pro-Controller funktionieren direkt. Auf einem Steam Deck im Desktop-Modus
  oder mit einem Controller, den das Spiel nicht erkennt, füge das Spiel zu Steam hinzu (*Non-Steam-Spiel hinzufügen*) und
  starte es von dort: Steam Input stellt es dann als Xbox-Controller dar.
- **Von Steam auf einem Steam Deck gestartet:** Steam setzt `SteamDeck=1`, wodurch der Unity-Linux-Player bei ~0 FPS
  hängen bleibt ([#232](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues/232)). Das Spiel startet
  sich einmal selbst ohne diese Variable neu (das Fenster kann zweimal aufgehen). Hängt es trotzdem, trage bei den
  Startoptionen der Verknüpfung `env -u SteamDeck %command%` ein.

---

<a id="features"></a>

## ✨ Funktionen

- **Story:** die komplette Hauptkampagne mit Prolog, dem Void und dem Ende, dazu die Add-ons Valkyrie und Supernova.
  Vertonte Dialoge (Englisch oder Deutsch), Porträts, Funkgeplauder und Zwischensequenzen.
- **Flug:** das originale Flugmodell und die Verfolgerkamera, eine Cockpitansicht und ein optionaler Freiflugmodus im Stil
  von EVERSPACE 2. Die Station-Orbits sind exakt wie im Original nachgebaut, mit ihren Himmeln, Planeten, Sonnen und
  Asteroidenfeldern.
- **Reisen:** Autopilot, Planetensprünge, Vorspulen, die Sternenkarte, Sprungtore, der Khador-Antrieb und das Wurmloch des
  Void. Die **Sprungtor-Routenplanung** fliegt dich Tor für Tor in jedes System, auf der schnellsten oder der sichersten
  Route. Der optionale **Khador Slipstream** ist Schnellreise innerhalb eines Systems und zwischen Systemen.
- **Kampf:** jede Waffe (Raketen, Strahlen, EMP, Minen, Geschütztürme, Sentry Guns, der Liberator...), NPC-Verkehr und KI,
  Großkampfschiffe, Piratenbasen, Specters und die Most Wanted. Tarnkappe, Notfallsystem, Zeitdehner, Reparaturstrahlen
  und Gamma-Schilde. Der Arcade-Modus **Supernova Challenge** mit Kombo-Punktzahl und Highscore.
- **Wirtschaft und Stationen:** Hangars, Läden und Schiffshändler, die Space Lounge mit Bar-Agenten und jeder Art von
  Freelance-Mission, Baupläne, Wingmen, Medaillen und der Kaamo Club.
- **Erzabbau:** Asteroidenabbau mit dem Bohr-Minispiel, die Mido **Mining Plant** (Erz zu Kernen, Weltraumschrott
  sortieren), Gaswolken, Hacken und Andocken an Objekten.
- **Koop-Kampagne (experimentell):** Spielt die Story zusammen, standardmäßig 2 bis 4 Spieler, über einen eigenen Knopf im
  Hauptmenü. Der Host setzt einen Einzelspieler-Spielstand fort oder startet ein neues Spiel; die Gegner können pro
  Spieler stärker werden.
- **Mehrspieler (experimentell):** ein gemeinsames Universum mit Squads, Fraktionen, Arena-Kämpfen, Handel und
  Event-Spielmodi. Siehe [Mehrspieler](#multiplayer).
- **Mods:** neue Gegenstände, Schiffe, Systeme, Stationen, Figuren, Quests und ganze Kampagnen, gebaut mit dem
  **Mod Builder** im Spiel. Siehe [Mods](#mods).
- **VR (experimentell):** PC-VR über OpenXR. Siehe [VR](#vr).
- **Speedrun-Timer:** Splits pro Story-Kapitel oder Schritt, LiveSplit-Split-Dateien und eine LiveSplit-Anbindung.
- **Steuerung und Bildschirme:** Touch, Neigung, Tastatur und Maus sowie Controller (Xbox- oder PlayStation-Tasten, automatisch
  gewählt), alles frei belegbar. Gyro-Steuerung mit DualSense, DualShock 4 oder Switch Pro unter Windows; Haptik auf
  Controllern und Android-Handys. Querformat-Bildschirme von 4:3 bis 32:9.
- **Sprachen:** die Sprachen des Originals plus niederländische, vereinfacht-chinesische und Hindi-Übersetzungen, dazu
  vervollständigtes Koreanisch, Japanisch und Arabisch (insgesamt 14), und deutsche oder englische Sprachausgabe.

<details>
<summary><b>Extras des Remakes</b> (die meisten sind Optionen unter Optionen > Gameplay)</summary>

- **Hangar und Präsentation:** Lande- und Startflüge im Hangar, weiche Schiffsschatten, Tiefenschärfe,
  **Schiff ansehen** (eine Orbitkamera um dein Schiff), animierter Dialogtext und der Bloom im Stil des Originals.
- **Verbesserungen der Großkampfschiffe:** Eskorten, stärkere Geschütztürme und Raketensalven; der Träger und das
  Vossk-Schlachtschiff lassen sich zerstören, es gibt seltene Flottenschlachten zwischen ihnen, und der Träger verkauft
  Vorräte an Piloten, denen er vertraut.
- **Piraten-Events:** In riskanten und gefährlichen Systemen kann ein Orbit einen Piratenaußenposten oder einen
  namentlich bekannten Piratenboss mit Kopfgeld und Bedrohungsstufe enthalten, aufgelistet auf der Tafel
  Piratenaktivität im Missionen-Fenster.
- **Klügeres NPC-Zielen:** Gegnerische Jäger greifen das nächste feindliche Schiff an, statt alle auf dich loszugehen.
  **EMP enttarnt**, und der Disruptor richtet doppelten Schaden an Schilden an, wie es seine Beschreibung sagt.
- **Kollision auf den echten Formen:** Schüsse treffen, was sie berühren, und du gleitest an Stationen und Frachtern
  entlang. *Schiffskollision*: Exakte Form, Gemischt (Jäger behalten die größere Trefferbox des Originals) oder Original.
- **Eine Raketenwarnung**; Boosten oder Tarnen schüttelt zielsuchende Raketen ab. Eine Scharfschaltdistanz für Bomben.
- **Neues Spiel+:** noch einmal von vorn beginnen, mit Credits, Bauplänen, Medaillen und Schiffen eines beendeten Spiels.
- **Optionen für neue Spiele:** der Kaamo Club von Anfang an, **Hardcore** (Permadeath), ein Spiel ohne Medaillen, die
  Tutorial-Hinweise und die Wahl zwischen der PC- und der Android-Wirtschaft.
- **Vier Schwierigkeitsgrade** (Einfach, Normal, Schwierig, Extrem), während eines Spiels änderbar.
- **Komfort:** Alles verkaufen / Alles kaufen, eine clevere Zielerfassung mit der Steuerung Ziel freigeben, die Karte
  im Missionen-Fenster kann zum Ziel einer Mission reisen, eine Sternenkarte mit Vollansicht, Medaillen als kurze
  Einblendung, ein Flug-HUD Voll / Minimal / Aus (F1), ferne Schiffsmarkierungen mit Entfernungen, Abklingsymbole, ein
  FPS-Zähler und der eigene Schussklang jeder Waffe.
- **Grafik:** Voreinstellungen Niedrig / Mittel / Hoch, Detailstufen für ferne Schiffe, Stationen und Asteroiden,
  SMAA / FXAA / TAA und Upscaling (FSR 1 und STP überall, NVIDIA DLSS und AMD FSR 2 / 3 / 4 unter Windows).
- **Fotomodi** (Action Freeze im Flug, Schiff ansehen im Hangar): freie Kamera, Sichtfeld, Rollen, Tiefenschärfe,
  Verschlusswinkel und Langzeitbelichtungen, Zoom-Unschärfe, Filmlooks, Belichtung, Farbe, Rahmen und Screenshots bis zum
  4-Fachen der Bildschirmgröße.
- **Spielstände:** einen Android- oder PC-Spielstand des Originalspiels importieren, Spielstände für die Community-Builds
  des Originals exportieren und ein Speicherordner deiner Wahl (erreichbar für Sync-Werkzeuge wie Syncthing).
- **Debug-Werkzeuge** (Optionen > Gameplay): zu jedem Story-Schritt springen, Cheats, Gegenstände geben, Schiffe und
  Objekte spawnen, jede Hülle fliegen, Schiffsausrüstungen speichern und laden. Der aktuelle Story-Schritt und die Station
  können unten rechts erscheinen, sodass ein Bug-Screenshot zeigt, wo es passiert ist.
- **Discord Rich Presence** unter Windows.

</details>

---

<a id="multiplayer"></a>

## 🌐 Mehrspieler

Mehrspieler ist experimentell. Alle in einer Sitzung teilen ein Universum in der Welt des beendeten Spiels: Ihr seht euch
im Weltraum und in den Hangars, bildet Squads, fliegt gemeinsam Bar-Missionen, teilt euch die Läden und den NPC-Verkehr,
gründet Fraktionen, denen Stationen gehören, handelt mit Credits und Fracht und kämpft in Arena-Kämpfen (oder überall,
wenn der Host PvP wählt). Beitreten kannst du über die Serverliste (mit Region und Ping jedes Spiels), einen Beitrittscode
oder das lokale Netzwerk.

Ein neuer Pilot startet angedockt in Dis in einer Betty mit 10 000 Credits. Ein dedizierter Server (oder die persistente
Welt eines Hosts) speichert den Fortschritt jedes Spielers; deine Einzelspieler-Spielstände werden nie angerührt. Nur die
**exakt gleiche Spielversion** kann zusammen spielen.

| Anleitung | Was drinsteht |
|---|---|
| **[Zusammen spielen](../MULTIPLAYER.md)** | Beitreten und hosten, Chat, Squads, Missionen, Läden, Fraktionen, Stationen besitzen, Arena-Kämpfe, Admins, jeder Chat-Befehl |
| **[Einen dedizierten Server betreiben](../../SERVER.md)** | Die Server-Downloads für Windows / Linux und das Docker-Image, Optionen, Konsole, Nachricht des Tages, Admin werden, die Dateien |
| **[Wie Mehrspieler funktioniert](../MULTIPLAYER-DEV.md)** | Der Code, für Programmierer |

---

<a id="mods"></a>

## 🧩 Mods

Mods fügen Inhalte des Spiels als Daten hinzu oder ändern sie, ohne Code und ohne Unity: Gegenstände (mit eigenen
Waffeneffekten und Sounds), Schiffe aus GLB-Modellen, Skins, Sternsysteme und Stationen (mit eigenen Modellen, Planeten,
Sonnen, Himmeln, Hangars und Bars), Figuren mit Porträts und Sprachausgabe, Musik und Soundeffekte, Baupläne, Quests und
Bar-Missionen sowie ganze Kampagnen, die unter Neues Spiel erscheinen.

**Eine Mod installieren:** Drücke im Bildschirm **Mods** des Hauptmenüs **Mod importieren** und wähle die `.zip` der Mod
(auch unter Android). Oder lege die Mod (einen Ordner oder ihre `.zip`) selbst in den Mods-Ordner und schalte sie dann im
Mods-Bildschirm ein. Der Knopf **Mods-Ordner öffnen** des Bildschirms öffnet ihn:

| Plattform | Mods-Ordner |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods` oder ein Ordner `Mods` neben `GoF2Remake.exe` |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods` oder ein Ordner `Mods` neben dem Spiel |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods` (per USB erreichbar) |

Ein Spielstand, der mit Mods gemacht wurde, merkt sie sich; lädst du ihn ohne sie, werden ihre Gegenstände entfernt
(erstattet). Im Mehrspielermodus entscheidet der Host, ob Mods erlaubt sind, und beitretende Spieler brauchen dieselben
Mods.

**Eine Mod machen:**

- **[Mod Builder](../notes/mod-builder.md)** (Mods > Mod Builder, experimentell): baut Schiffe, Gegenstände, Skins,
  Figuren, Quests (mit Knotengraph), Systeme, Stationen, Hangars und Bars, Waffeneffekte, Audio, Baupläne, Kampagnen und
  Schiffs-Kits im Spiel, mit Rückgängig, Testflügen und Probespielen, und exportiert die fertige Mod. Er verkleinert auch
  die Modelle und Texturen einer Mod und importiert Community-Mods, die aus den Modellen des Originals gemacht wurden
  (`.aem` / `.aei`). Dein eigener KI-Assistent kann über seinen lokalen MCP-Server darin arbeiten, mit Berechtigungen, die
  du wählst.
- **Von Hand:** der Modder-Leitfaden [Modding/README.md](../../Modding/README.md), mit Beispiel-Mods in
  [Modding/Examples](../../Modding/Examples).
- **Mit einem KI-Assistenten:** [Modding/ai](../../Modding/ai) enthält einen Leitfaden für Claude, ChatGPT und andere.

---

<a id="controls"></a>

## 🕹️ Steuerung

<details>
<summary><b>Tastatur und Maus</b></summary>

| Taste | Aktion |
|---|---|
| Pfeiltasten | Steuern |
| W | Boost |
| S | Bremsen (die Triebwerke stehen, solange gehalten) |
| `]` / `/` oder das Mausrad | Schub |
| Leertaste / linke Maustaste | Primärwaffen |
| R / rechte Maustaste | Sekundärwaffe |
| G | Sekundärwaffe wechseln |
| A / D | Ausweichen nach links / rechts |
| 1 / 3 | Rollen nach links / rechts |
| 2 | Ausrichten |
| F / Enter | Aktion (andocken, Autopilot, abbauen, springen) |
| Q | Autopilot-Menü |
| E | Aktionsmenü (Sekundärwaffen, Wingmen, Tarnkappe, Khador-Antrieb) |
| Tab | Vorspulen |
| T | Kamera: Verfolger, Cockpit, Turmansicht, freie Sicht |
| V / K / C / X | Wingmen / Khador-Antrieb / Tarnkappe / Zeitdehner |
| M oder mittlere Maustaste | Maussteuerung umschalten |
| U | Ziel freigeben |
| B / N | Chat / Mehrspielerfenster (Mehrspieler) |
| F1 | Flug-HUD: voll, minimal, aus |
| F12 | Screenshot |
| Esc | Pause |

</details>

Jede Flugsteuerung lässt sich unter Optionen > Tastenbelegung neu belegen (je zwei Tasten für Tastatur / Maus und eine
Controller-Taste). Controller und Touch werden voll unterstützt, und die Hinweise im Spiel zeigen die Tasten des Geräts,
das du zuletzt benutzt hast.

<a id="vr"></a>

### VR (experimentell)

Starte den Windows-Build mit `-vr` (zum Beispiel über eine Verknüpfung zu `GoF2Remake.exe -vr`), mit angeschlossenem
PC-VR-Headset und aktiver OpenXR-Runtime (SteamVR, Meta Quest Link, Windows Mixed Reality). Ohne Headset startet das Spiel
ganz normal.

<details>
<summary>So sieht VR aus</summary>

- **Flug:** Du sitzt in einem Cockpit. Schild, Hülle und Fracht sind auf seinen Anzeigen, das Radar in der Mitte des
  Armaturenbretts, der Rest des HUD auf der Kanzel. Zwischensequenzen halten den Horizont waagerecht und blenden zwischen
  den Einstellungen über.
- **Stationen:** Du stehst im Hangar neben deinem Schiff (greife es mit dem Griff und ziehe, um es zu drehen) und in der
  Bar (zeige auf einen Besucher und drücke den Trigger, um zu reden).
- **Menüs** erscheinen auf einem schwebenden Bildschirm; zeige mit dem rechten Controller darauf und drücke den Trigger.
- **Steuerung:** Die Controller funktionieren wie ein Gamepad (Sticks, Trigger, A / B / X / Y, die Griffe als LB / RB). Mit
  Optionen > Steuerung > „VR-Flug: Steuerknüppel und Schubhebel greifen“ fliegst du stattdessen mit dem Sidestick und dem
  Schubhebel des Cockpits.

Das Cockpit ist vorerst für jedes Schiff dasselbe.

</details>

---

<a id="help"></a>

## 🛠️ Beim Projekt mithelfen

Dieser Abschnitt ist für alle, die das Spiel selbst ändern wollen: seinen Code, seine Szenen und Assets, oder eigene Kopien
bauen. **Zum Spielen nimm eine [Version](#play); um Inhalte hinzuzufügen, reicht meist eine [Mod](#mods).**

### Voraussetzungen

- **Unity 7000.0.0a12** (eine Alpha, über den Unity Hub installiert). Füge die Build-Support-Module für die Plattformen
  hinzu, die du baust: *Windows Build Support (IL2CPP)*, *Linux Build Support (IL2CPP)*, *Android Build Support* und die
  *Dedicated Server*-Module für Server-Builds.
- **Git** mit **[Git LFS](https://git-lfs.com)**. Die Modelle, Texturen und Sounds sind etwa 2,1 GB groß in LFS und werden
  vom projekteigenen LFS-Server geholt (eingestellt in [.lfsconfig](../../.lfsconfig)).
- Für **Windows-Builds:** Visual Studio 2022 oder neuer mit der Workload *Desktopentwicklung mit C++* und einem Windows-10-
  / 11-SDK (IL2CPP). Der UWP-Build braucht außerdem *C++-Tools für die Universelle Windows-Plattform*.

### Das Projekt zum Laufen bringen

```bash
git lfs install
```

```bash
git clone https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake.git
```

1. Öffne den geklonten Ordner im Unity Hub (*Add > Add project from disk*) mit Unity 7000.0.0a12. Der erste Import dauert
   eine Weile.
2. Öffne `Assets/Scenes/Splash.unity` (oder `MainMenu.unity`, um die Logos zu überspringen) und drücke **Play**.

Die Szenen sind `Splash` (Build-Index 0), `MainMenu`, `Space` (das Flug-Level) und `Station` (angedockt). Die generierten
Assets (Prefabs, Materialien, Icons, HUD-Bilder) liegen schon im Repository; das Menü **GoF2** im Editor baut sie neu,
und **GoF2 > Tools Overview** listet jedes Werkzeug auf.

> [!IMPORTANT]
> Generiere kein Licht für die Szenen. Sie haben mit Absicht kein gebackenes Licht: Das Spiel baut seine Level zur
> Laufzeit, und jedes System hat seinen eigenen Himmel (`SkyReflection` aktualisiert das Umgebungslicht daraus). Eine
> gebackene Probe wäre ein fester Himmel für jedes System.

### Bauen

- **Wechsle immer das aktive Build-Profil** (File > Build Profiles > Switch Profile), bevor du für eine andere Plattform
  baust. URP hält Shader-Varianten nur für die aktive Plattform vor: Ein Android-Build, der bei aktivem Windows gemacht
  wurde, rendert schwarz, darum lehnt ihn das Editor-Skript `BuildTargetGuard` ab.
- `BuildVersionStamp` gibt jedem Build seine Version aus Datum und Uhrzeit seines Git-Commits. Setze für eine Version die
  Umgebungsvariable `GOF2_BUILD_VERSION` des Editor-Prozesses, damit alle Plattformen dieselbe bekommen.
- Dedizierte Server: **GoF2 > Build > Linux Dedicated Server** / **Windows Dedicated Server**. Der Ordner `docker/`
  enthält das Server-Image.
- Update-Patches für den Updater im Spiel: **GoF2 > Build > Update Patch** nach den Builds.

### Tests

Etwa 3000 Edit-Mode-Tests decken die Regeln und Daten ab, dazu Play-Mode-Smoke-Tests der Szenen, Story-Level und einer
gehosteten Sitzung. Starte sie über Window > General > Test Runner oder mit der angeschlossenen `unity`-CLI:

```bash
python -I Reference/tools/tests/run_tests.py
```

Siehe [Assets/Scripts/Tests/README.md](../../Assets/Scripts/Tests/README.md).

### Projektaufbau

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

### Dokumentation

- [CLAUDE.md](../../CLAUDE.md): die projektweiten Regeln (Konventionen, Koordinaten, Daten) und das Verzeichnis der
  technischen Notizen.
- [docs/notes/](../notes): eine Datei pro Bereich des Spiels (Flug, Kampf, Story, Shop, Mehrspieler, Mods...), mit den
  Originalfunktionen, auf denen jedes System beruht, und den Entscheidungen des Remakes.
- [Assets/Scripts/Runtime/README.md](../../Assets/Scripts/Runtime/README.md): die Assembly-Schichten und ihre Hooks.
- [Reference/README.md](../../Reference/README.md): der dekompilierte Code und die Werkzeuge, die ihn lesen.

### Mitmachen

- **Einen Bug gefunden?** Eröffne ein [Issue](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues) mit
  deiner Plattform, der Version des Spiels (im Hauptmenü zu sehen) und einem Screenshot. Optionen > Gameplay kann den
  aktuellen Story-Schritt und die Station unten rechts anzeigen, das verrät uns, wo es passiert ist.
- **Pull Requests sind willkommen.** Forke das Repository, mache deine Änderung auf einem Branch, führe die
  Edit-Mode-Tests aus und eröffne einen Pull Request gegen `main`. Lies vor der Arbeit an einem Bereich seine Datei in
  [docs/notes/](../notes); die Konventionen stehen in [CLAUDE.md](../../CLAUDE.md).
- **Gefällt dir das Projekt?** Gib ihm einen ⭐ auf GitHub, das hilft anderen, es zu finden.

---

<a id="credits"></a>

## 🙏 Danksagung

**Galaxy on Fire 2 Remake** von JoppieToppie, mit Pull Requests von KiritoJPK, DuIslingr, Zippadelic, FantasyToLife und
anderen.

Das Remake baut auf der **FULL-HD-Version und den Modifikationen von KiritoJPK** auf, dank der Galaxy on Fire 2™- und
4PDA-Community: <https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

Schriften: die Oberflächenschrift des Originalspiels und Inter (SIL Open Font License). Controller-Gyro:
[JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary) von Julian Smart (MIT-Lizenz). Gebaut mit Unity, der
Universal Render Pipeline, Netcode for GameObjects und OpenXR.

### Mitwirkende

<a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors">
  <img alt="Mitwirkende" src="https://contrib.rocks/image?repo=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake">
</a>

## ⭐ Star-Verlauf

<a href="https://star-history.com/#JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&Date">
  <img alt="Diagramm des Star-Verlaufs" src="https://api.star-history.com/svg?repos=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&type=Date">
</a>

## ⚖️ Rechtliches

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ und ABYSS®
sind eingetragene Marken der FISHLABS Entertainment GmbH. Alle Rechte vorbehalten. Dies ist ein inoffizielles Fanprojekt,
das weder mit FISHLABS oder Deep Silver verbunden ist noch von ihnen unterstützt wird.

Der eigene Code des Remakes steht unter der [Apache License 2.0](../../LICENSE.md).
