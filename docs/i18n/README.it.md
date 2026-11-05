![Galaxy on Fire 2 Remake, menu principale](../../.github/header.webp)

<h1 align="center">Galaxy on Fire 2 Remake</h1>

<p align="center">
  Un remake amatoriale dello spaziale del 2010 <i>Galaxy on Fire 2</i> di FISHLABS, ricostruito in Unity.<br>
  Volo, combattimento, commercio, estrazione mineraria, stazioni, il bar e tutta la storia, fedele all'originale.
</p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.es.md">Español</a> ·
  <b>Italiano</b> ·
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

<p align="center"><i>Questa è una traduzione; il <a href="../../README.md">README in inglese</a> è il riferimento e potrebbe essere più recente.</i></p>

<p align="center">
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest"><img alt="Ultima release" src="https://img.shields.io/github/v/release/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?label=release"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases"><img alt="Download" src="https://img.shields.io/github/downloads/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/total"></a>
  <img alt="Piattaforme" src="https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20Android%20%7C%20UWP-blue">
  <img alt="Unity" src="https://img.shields.io/badge/Unity-7000.0.0a12-black?logo=unity">
  <a href="../../LICENSE.md"><img alt="Licenza" src="https://img.shields.io/badge/license-Apache%202.0-green"></a>
  <br>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/stargazers"><img alt="Stelle" src="https://img.shields.io/github/stars/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/forks"><img alt="Fork" src="https://img.shields.io/github/forks/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues"><img alt="Issue aperte" src="https://img.shields.io/github/issues/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/pulls"><img alt="Pull request aperte" src="https://img.shields.io/github/issues-pr/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors"><img alt="Collaboratori" src="https://img.shields.io/github/contributors/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/commits/main"><img alt="Ultimo commit" src="https://img.shields.io/github/last-commit/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
</p>

<p align="center">
  <a href="#play"><b>Gioca</b></a> ·
  <a href="#features">Funzionalità</a> ·
  <a href="#multiplayer">Multiplayer</a> ·
  <a href="#mods">Mod</a> ·
  <a href="#controls">Comandi</a> ·
  <a href="#help"><b>Contribuisci al progetto</b></a> ·
  <a href="#credits">Crediti</a>
</p>

---

Il remake si basa sugli asset del gioco originale e sulla logica di gioco portata dal codice originale decompilato.

> [!NOTE]
> **Stato:** la campagna principale e entrambe le espansioni, **Valkyrie** e **Supernova**, si possono giocare dall'inizio
> alla fine, così come la modalità arcade Supernova Challenge. Il multiplayer, la campagna cooperativa e il Costruttore di
> mod sono sperimentali. La VR finora è stata provata solo senza visore.

## Quale sezione mi serve?

| Voglio... | Vai a |
|---|---|
| **Giocare** sul mio PC o sul telefono | [Gioca](#play): scarica una release, Unity non serve |
| Giocare con gli amici o gestire un server | [Multiplayer](#multiplayer) |
| Aggiungere navi, oggetti, sistemi o missioni | [Mod](#mods): create nel gioco o come file di dati, Unity non serve |
| **Modificare il codice o gli asset del gioco**, o inviare una pull request | [Contribuisci al progetto](#help): clona il repository e aprilo in Unity |

---

<a id="play"></a>

## 🎮 Gioca

Per giocare non servono Unity, Git né questo repository. Scarica la build per la tua piattaforma dall'**[ultima
release](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest)**:

| Piattaforma | Download | Come installare |
|---|---|---|
| **Windows** 10 / 11 (64 bit) | `GoF2Remake-Windows-<version>.zip` | Decomprimi, poi avvia `GoF2Remake.exe` nella cartella `GoF2Remake` |
| **Linux** (x86_64) | `GoF2Remake-Linux-<version>.zip` | Decomprimi, poi avvia `GoF2Remake.x86_64` (se non parte: `chmod +x GoF2Remake.x86_64`) |
| **Android** (ARM64) | `GoF2Remake-Android-<version>.apk` | Copia l'APK sul telefono e aprilo; quando richiesto, consenti le installazioni da questa fonte |
| **Windows (app UWP)** | `GoF2Remake-UWP-<version>.zip` | Decomprimi, clic destro su `Install.ps1` > **Esegui con PowerShell** (chiede di considerare attendibile il certificato di prova del pacchetto) |
| Server dedicato | `GoF2Remake-WindowsServer-…` / `-LinuxServer-…` | Vedi [SERVER.md](../../SERVER.md); c'è anche l'immagine Docker `ghcr.io/joppietoppie/gof2-server` |

- **Versione:** la versione di ogni build è la data e l'ora della sua release (per esempio `2026.11.02.0622`), uguale su
  tutte le piattaforme e mostrata nel menu principale. Il multiplayer funziona solo tra giocatori con la stessa versione.
- **Aggiornamenti:** il menu principale mostra **Aggiornamento disponibile** quando esce una release più recente. Dalle
  build successive alla 2026.11.02, quel pulsante aggiorna il gioco sul posto e scarica solo ciò che è cambiato (Windows,
  Linux e Android; l'app UWP si aggiorna tramite App Installer di Windows). Le build del 2026.11.02 e precedenti
  richiedono un ultimo download completo.
- **I salvataggi** sono tenuti separati dai file del gioco, quindi aggiornare o reinstallare non li perde. Esporta e
  importa tutti i salvataggi come un unico file da **Carica gioco**, per spostarli su un altro PC o telefono.
- **La versione UWP** non ha stato Discord, giroscopio del controller, finestra di salvataggio file, VR né server
  dedicato.

> [!TIP]
> Una nuova installazione si gioca come il **gioco originale**. Opzioni > Gioco > **Esperienza di gioco** attiva in un
> colpo solo le aggiunte del remake: *Originale*, *Estetica* (voli nell'hangar e dialoghi animati) o *Remake* (tutte le
> aggiunte). Ogni opzione si può cambiare anche singolarmente.

### Linux e Steam Deck

- **Controller:** i controller Xbox, PlayStation e Switch Pro funzionano direttamente. Su uno Steam Deck in modalità
  desktop, o con un controller che il gioco non riconosce, aggiungi il gioco a Steam (*Aggiungi un gioco non Steam*) e
  avvialo da lì: Steam Input lo presenta come un controller Xbox.
- **Avviato da Steam su uno Steam Deck:** Steam imposta `SteamDeck=1`, che fa bloccare il player Unity per Linux a ~0 FPS
  ([#232](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues/232)). Il gioco si riavvia da solo una
  volta senza questa variabile (la finestra potrebbe aprirsi due volte). Se si blocca ancora, imposta le Opzioni di avvio
  del collegamento su `env -u SteamDeck %command%`.

---

<a id="features"></a>

## ✨ Funzionalità

- **Storia:** l'intera campagna principale con il prologo, il Void e il finale, più le espansioni Valkyrie e Supernova.
  Dialoghi doppiati (inglese o tedesco), ritratti, comunicazioni radio e filmati.
- **Volo:** il modello di volo e la telecamera di inseguimento originali, una visuale dalla cabina e una modalità di volo
  libero opzionale nello stile di EVERSPACE 2. Le orbite delle stazioni sono ricostruite esattamente come nell'originale,
  con cieli, pianeti, soli e campi di asteroidi.
- **Viaggio:** autopilota, salti planetari, avanzamento rapido, la mappa stellare, i portali di salto, il motore Khador e
  il wormhole del Void. La **pianificazione della rotta tra portali** ti porta portale per portale in qualsiasi sistema,
  con la rotta più rapida o la più sicura. Il **Khador Slipstream** opzionale è un viaggio veloce dentro un sistema e tra
  sistemi.
- **Combattimento:** tutte le armi (missili, raggi, EMP, mine, torrette, cannoni di guardia, il Liberator...), traffico e
  IA degli NPC, navi capitali, basi pirata, Specter e i Most Wanted. Occultamento, sistema di emergenza, estensore del
  tempo, raggi riparatori e scudi gamma. La modalità arcade **Supernova Challenge** con punteggio a combo e record.
- **Economia e stazioni:** hangar, negozi e concessionari di navi, il salotto spaziale con gli agenti del bar e ogni tipo
  di missione freelance, progetti, gregari, medaglie e il Kaamo Club.
- **Estrazione mineraria:** estrazione di asteroidi con il minigioco della trivella, l'**Impianto di estrazione** di Mido
  (minerale in nuclei, smistamento dei Rifiuti spaziali), nubi di gas, hacking e attracco agli oggetti.
- **Campagna cooperativa (sperimentale):** gioca la storia insieme, da 2 a 4 giocatori per impostazione predefinita, da un
  pulsante apposito nel menu principale. L'host continua un salvataggio single player o inizia una nuova partita; i
  nemici possono diventare più forti per ogni giocatore.
- **Multiplayer (sperimentale):** un universo condiviso con squadre, fazioni, partite in arena, commercio e modalità di
  gioco a eventi. Vedi [Multiplayer](#multiplayer).
- **Mod:** nuovi oggetti, navi, sistemi, stazioni, personaggi, missioni e intere campagne, creati con il
  **Costruttore di mod** nel gioco. Vedi [Mod](#mods).
- **VR (sperimentale):** VR per PC tramite OpenXR. Vedi [VR](#vr).
- **Cronometro speedrun:** parziali per capitolo o passo della storia, file di parziali LiveSplit e un collegamento a
  LiveSplit.
- **Comandi e schermi:** tocco, inclinazione, tastiera e mouse, e controller (pulsanti Xbox o PlayStation, scelti
  automaticamente), tutti riassegnabili. Guida con il giroscopio di DualSense, DualShock 4 o Switch Pro su Windows;
  feedback aptico su controller e telefoni Android. Schermi orizzontali dal 4:3 al 32:9.
- **Lingue:** le lingue dell'originale più le traduzioni in olandese, cinese semplificato e hindi, con coreano, giapponese
  e arabo completati (14 in tutto), e voci in tedesco o inglese.

<details>
<summary><b>Extra del remake</b> (la maggior parte sono opzioni in Opzioni > Gioco)</summary>

- **Hangar e presentazione:** voli di arrivo e di decollo nell'hangar, ombre morbide delle navi, profondità di campo,
  **Ispeziona nave** (una telecamera orbitale intorno alla tua nave), testo dei dialoghi animato e bloom in stile
  originale.
- **Potenziamenti delle navi capitali:** scorte, torrette più potenti e salve di missili; la portaerei e la corazzata
  vossk si possono distruggere, ci sono rare battaglie tra flotte e la portaerei vende rifornimenti ai piloti di cui si
  fida.
- **Eventi pirata:** nei sistemi Rischiosi e Pericolosi un'orbita può ospitare un avamposto pirata o un boss pirata con
  nome, taglia e livello di minaccia, elencati nella bacheca Attività pirata della finestra Missioni.
- **Mira degli NPC più intelligente:** i caccia nemici attaccano la nave ostile più vicina invece di puntare tutti su di
  te. **L'EMP rompe l'occultamento**, e il Disruptor fa doppio danno agli scudi, come dice la sua descrizione.
- **Collisioni sulle forme reali:** i colpi colpiscono ciò che toccano e scivoli lungo stazioni e cargo. *Collisione delle
  navi*: Forma esatta, Mista (i caccia mantengono l'area d'impatto più grande dell'originale) o Originale.
- **Un avviso di missile**; potenziare i motori o occultarsi scrolla di dosso i missili a ricerca. Una distanza di innesco
  per le bombe.
- **Nuova partita+:** ricomincia con i crediti, i progetti, le medaglie e le navi di un gioco finito.
- **Opzioni di nuova partita:** il Kaamo Club fin dall'inizio, **Hardcore** (morte permanente), un gioco senza medaglie, i
  popup del tutorial e la scelta tra le economie PC e Android.
- **Quattro difficoltà** (Facile, Normale, Difficile, Estrema), modificabili durante una partita.
- **Comodità:** Vendi tutto / Compra tutto, un blocco bersaglio intelligente con un comando Sgancia bersaglio, la mappa
  della finestra Missioni che può viaggiare fino al bersaglio di una missione, una panoramica della mappa stellare a
  schermo intero, le medaglie come breve notifica, un HUD di volo Completo / Minimo / Spento (F1), indicatori di navi
  lontane con le distanze, icone di ricarica, un contatore degli FPS e il suono di sparo specifico di ogni arma.
- **Grafica:** preset Basso / Medio / Alto, livelli di dettaglio per navi, stazioni e asteroidi lontani, SMAA / FXAA / TAA
  e upscaling (FSR 1 e STP ovunque, NVIDIA DLSS e AMD FSR 2 / 3 / 4 su Windows).
- **Modalità foto** (Action Freeze in volo, Ispeziona nave nell'hangar): una telecamera libera, campo visivo, rollio,
  profondità di campo, angolo dell'otturatore ed esposizioni lunghe, effetto zoom, look cinematografici, esposizione,
  colore, cornici e screenshot fino a 4 volte la dimensione dello schermo.
- **Salvataggi:** importa un salvataggio Android o PC del gioco originale, esporta i salvataggi per le build della
  community dell'originale e una cartella dei salvataggi a tua scelta (raggiungibile da strumenti di sincronizzazione
  come Syncthing).
- **Strumenti di debug** (Opzioni > Gioco): vai a qualsiasi passo della storia, trucchi, aggiungi oggetti, crea navi e
  oggetti, pilota qualsiasi scafo, salva e carica gli equipaggiamenti delle navi. Il passo della storia e la stazione
  attuali possono comparire in basso a destra, così uno screenshot di un bug mostra dove è successo.
- **Discord Rich Presence** su Windows.

</details>

---

<a id="multiplayer"></a>

## 🌐 Multiplayer

Il multiplayer è sperimentale. Tutti i partecipanti a una sessione condividono un universo nel mondo del gioco finito: vi
vedete nello spazio e negli hangar, formate squadre, affrontate insieme le missioni del bar, condividete negozi e traffico
di NPC, fondate fazioni che possiedono stazioni, scambiate crediti e merci e combattete in partite in arena (o ovunque,
quando l'host sceglie il PvP). Ci si unisce tramite il browser dei server (con regione e ping di ogni partita), un codice
d'accesso o la rete locale.

Un nuovo pilota parte attraccato a Dis su una Betty con 10 000 crediti. Un server dedicato (o il mondo persistente di un
host) conserva i progressi di ogni giocatore; i tuoi salvataggi single player non vengono mai toccati. Si può giocare
insieme solo con la **stessa identica versione del gioco**.

| Guida | Cosa contiene |
|---|---|
| **[Giocare insieme](../MULTIPLAYER.md)** | Unirsi e ospitare, chat, squadre, missioni, negozi, fazioni, possedere stazioni, scontri in arena, admin, tutti i comandi della chat |
| **[Gestire un server dedicato](../../SERVER.md)** | I download del server per Windows / Linux e l'immagine Docker, opzioni, console, messaggio del giorno, diventare admin, i suoi file |
| **[Come funziona il multiplayer](../MULTIPLAYER-DEV.md)** | Il codice, per i programmatori |

---

<a id="mods"></a>

## 🧩 Mod

Le mod aggiungono o cambiano i contenuti del gioco come dati, senza codice e senza Unity: oggetti (con effetti delle armi e
suoni propri), navi da modelli GLB, skin, sistemi stellari e stazioni (con modelli, pianeti, soli, cieli, hangar e bar
propri), personaggi con ritratti e doppiaggio, musica ed effetti sonori, progetti, missioni e missioni del bar, e intere
campagne che compaiono in Inizia nuovo gioco.

**Installare una mod:** nella schermata **Mod** del menu principale premi **Importa mod** e scegli il `.zip` della mod
(anche su Android). Oppure metti tu stesso la mod (una cartella o il suo `.zip`) nella cartella Mods, poi attivala nella
schermata Mod. Il pulsante **Apri la cartella delle mod** della schermata la apre:

| Piattaforma | Cartella delle mod |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods`, oppure una cartella `Mods` accanto a `GoF2Remake.exe` |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods`, oppure una cartella `Mods` accanto al gioco |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods` (raggiungibile via USB) |

Un salvataggio fatto con delle mod le ricorda; caricarlo senza di esse ne rimuove gli oggetti (rimborsati). Nel multiplayer
è l'host a decidere se le mod sono consentite, e i giocatori che si uniscono hanno bisogno delle stesse mod.

**Creare una mod:**

- **[Costruttore di mod](../notes/mod-builder.md)** (Mod > Costruttore di mod, sperimentale): crea nel gioco navi,
  oggetti, skin, personaggi, missioni (con un grafo di nodi), sistemi, stazioni, hangar e bar, effetti delle armi, audio,
  progetti, campagne e kit per navi, con annulla, voli di prova e prove di gioco, ed esporta la mod finita. Riduce anche i
  modelli e le texture di una mod e importa le mod della community realizzate con i modelli dell'originale (`.aem` /
  `.aei`). Il tuo assistente IA può lavorarci tramite il suo server MCP locale, con i permessi che scegli tu.
- **A mano:** la guida per i modder [Modding/README.md](../../Modding/README.md), con mod di esempio in
  [Modding/Examples](../../Modding/Examples).
- **Con un assistente IA:** [Modding/ai](../../Modding/ai) contiene una guida per Claude, ChatGPT e altri.

---

<a id="controls"></a>

## 🕹️ Comandi

<details>
<summary><b>Tastiera e mouse</b></summary>

| Tasto | Azione |
|---|---|
| Frecce | Sterza |
| W | Potenziamento dei motori (boost) |
| S | Freno (i motori si fermano finché è premuto) |
| `]` / `/` o la rotella del mouse | Manetta |
| Spazio / tasto sinistro del mouse | Armi primarie |
| R / tasto destro del mouse | Arma secondaria |
| G | Cambia arma secondaria |
| A / D | Spostamento laterale a sinistra / destra |
| 1 / 3 | Rollio a sinistra / destra |
| 2 | Raddrizza |
| F / Invio | Azione (attracco, autopilota, estrazione, salto) |
| Q | Menu dell'autopilota |
| E | Menu delle azioni (armi secondarie, gregari, occultamento, motore Khador) |
| Tab | Avanzamento rapido |
| T | Telecamera: inseguimento, cabina, visuale dalla torretta, visuale libera |
| V / K / C / X | Gregari / motore Khador / occultamento / estensore del tempo |
| M o tasto centrale del mouse | Attiva / disattiva la guida con il mouse |
| U | Sgancia bersaglio |
| B / N | Chat / finestra multiplayer (multiplayer) |
| F1 | HUD di volo: completo, minimo, spento |
| F12 | Screenshot |
| Esc | Pausa |

</details>

Ogni comando di volo si può riassegnare in Opzioni > Comandi (due tasti tastiera / mouse e un pulsante del controller per
ciascuno). Controller e tocco sono pienamente supportati, e i suggerimenti nel gioco mostrano i pulsanti dell'ultimo
dispositivo di input che hai usato.

<a id="vr"></a>

### VR (sperimentale)

Avvia la build per Windows con `-vr` (per esempio un collegamento a `GoF2Remake.exe -vr`) con un visore VR per PC
collegato e un runtime OpenXR attivo (SteamVR, Meta Quest Link, Windows Mixed Reality). Senza visore il gioco si avvia
normalmente.

<details>
<summary>Com'è la VR</summary>

- **Volo:** sei seduto in una cabina. Scudo, scafo e stiva sono sui suoi display, il radar al centro del cruscotto, il
  resto dell'HUD sul tettuccio. I filmati mantengono l'orizzonte livellato e sfumano tra le inquadrature.
- **Stazioni:** stai in piedi nell'hangar accanto alla tua nave (afferrala con il grilletto di presa e tira per farla
  ruotare) e nel bar (punta un visitatore e premi il grilletto per parlare).
- **I menu** appaiono su uno schermo fluttuante; punta con il controller destro e premi il grilletto.
- **Comandi:** i controller funzionano come un gamepad (stick, grilletti, A / B / X / Y, i grilletti di presa come LB /
  RB). Con Opzioni > Comandi > "Volo in VR: afferra cloche e manetta" voli invece con la cloche laterale e la leva della
  manetta della cabina.

Per ora la cabina è uguale per ogni nave.

</details>

---

<a id="help"></a>

## 🛠️ Contribuisci al progetto

Questa sezione serve a modificare il gioco stesso: il suo codice, le scene e gli asset, o a compilare le tue copie. **Per
giocare e basta, usa una [release](#play); per aggiungere contenuti, di solito basta una [mod](#mods).**

### Requisiti

- **Unity 7000.0.0a12** (una alpha, installata tramite Unity Hub). Aggiungi i moduli di supporto alla build per le
  piattaforme che compili: *Windows Build Support (IL2CPP)*, *Linux Build Support (IL2CPP)*, *Android Build Support* e i
  moduli *Dedicated Server* per le build del server.
- **Git** con **[Git LFS](https://git-lfs.com)**. I modelli, le texture e i suoni occupano circa 2,1 GB in LFS, scaricati
  dal server LFS del progetto (impostato in [.lfsconfig](../../.lfsconfig)).
- Per le **build per Windows:** Visual Studio 2022 o successivo con il carico di lavoro *Sviluppo di applicazioni desktop
  con C++* e un SDK di Windows 10 / 11 (IL2CPP). La build UWP richiede anche gli *strumenti C++ per la piattaforma UWP
  (Universal Windows Platform)*.

### Far partire il progetto

```bash
git lfs install
```

```bash
git clone https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake.git
```

1. Apri la cartella clonata in Unity Hub (*Add > Add project from disk*) con Unity 7000.0.0a12. Il primo import richiede
   un po' di tempo.
2. Apri `Assets/Scenes/Splash.unity` (o `MainMenu.unity` per saltare i loghi) e premi **Play**.

Le scene sono `Splash` (indice di build 0), `MainMenu`, `Space` (il livello di volo) e `Station` (attraccato). Gli asset
generati (prefab, materiali, icone, immagini dell'HUD) sono già nel repository; il menu **GoF2** nell'Editor li
ricostruisce, e **GoF2 > Tools Overview** elenca ogni strumento.

> [!IMPORTANT]
> Non generare l'illuminazione per le scene. Non hanno illuminazione precalcolata di proposito: il gioco costruisce i suoi
> livelli a runtime e ogni sistema ha il proprio cielo (`SkyReflection` aggiorna da esso la luce ambientale). Una sonda
> precalcolata sarebbe un unico cielo fisso per tutti i sistemi.

### Compilare

- **Cambia sempre il profilo di build attivo** (File > Build Profiles > Switch Profile) prima di compilare per un'altra
  piattaforma. URP conserva le varianti degli shader solo per la piattaforma attiva: una build Android fatta con Windows
  attivo viene resa nera, quindi lo script dell'editor `BuildTargetGuard` la rifiuta.
- `BuildVersionStamp` dà a ogni build la sua versione dalla data e dall'ora del suo commit git. Per una release, imposta
  la variabile d'ambiente `GOF2_BUILD_VERSION` del processo dell'Editor, così tutte le piattaforme ricevono la stessa.
- Server dedicati: **GoF2 > Build > Linux Dedicated Server** / **Windows Dedicated Server**. La cartella `docker/`
  contiene l'immagine del server.
- Patch di aggiornamento per l'updater nel gioco: **GoF2 > Build > Update Patch** dopo le build.

### Test

Circa 3000 test in modalità edit coprono le regole e i dati, più test di fumo in modalità play delle scene, dei livelli
della storia e di una sessione ospitata. Eseguili da Window > General > Test Runner, o con la CLI `unity` collegata:

```bash
python -I Reference/tools/tests/run_tests.py
```

Vedi [Assets/Scripts/Tests/README.md](../../Assets/Scripts/Tests/README.md).

### Struttura del progetto

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

### Documentazione

- [CLAUDE.md](../../CLAUDE.md): le regole valide per tutto il progetto (convenzioni, coordinate, dati) e l'indice delle
  note tecniche.
- [docs/notes/](../notes): un file per ogni area del gioco (volo, combattimento, storia, negozio, multiplayer, mod...), con
  le funzioni originali su cui si basa ogni sistema e le scelte fatte dal remake.
- [Assets/Scripts/Runtime/README.md](../../Assets/Scripts/Runtime/README.md): i livelli degli assembly e i loro hook.
- [Reference/README.md](../../Reference/README.md): il codice decompilato e gli strumenti che lo leggono.

### Contribuire

- **Hai trovato un bug?** Apri una [issue](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues) con la
  tua piattaforma, la versione del gioco (mostrata nel menu principale) e uno screenshot. Opzioni > Gioco può mostrare il
  passo della storia e la stazione attuali in basso a destra, il che ci dice dove è successo.
- **Le pull request sono benvenute.** Fai il fork del repository, apporta la modifica su un branch, esegui i test in
  modalità edit e apri una pull request verso `main`. Prima di lavorare su un'area, leggi il suo file in
  [docs/notes/](../notes); le convenzioni sono in [CLAUDE.md](../../CLAUDE.md).
- **Ti piace il progetto?** Lascia una ⭐ su GitHub, aiuta gli altri a trovarlo.

---

<a id="credits"></a>

## 🙏 Crediti

**Galaxy on Fire 2 Remake** di JoppieToppie, con pull request di KiritoJPK, DuIslingr, Zippadelic, FantasyToLife e altri.

Il remake si basa sulla **versione FULL HD e sulle modifiche di KiritoJPK**, grazie alla community di Galaxy on Fire 2™ e
di 4PDA: <https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

Font: il carattere dell'interfaccia del gioco originale e Inter (SIL Open Font License). Giroscopio del controller:
[JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary) di Julian Smart (licenza MIT). Realizzato con Unity, la
Universal Render Pipeline, Netcode for GameObjects e OpenXR.

### Collaboratori

<a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors">
  <img alt="Collaboratori" src="https://contrib.rocks/image?repo=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake">
</a>

## ⭐ Cronologia delle stelle

<a href="https://star-history.com/#JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&Date">
  <img alt="Grafico della cronologia delle stelle" src="https://api.star-history.com/svg?repos=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&type=Date">
</a>

## ⚖️ Note legali

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ e ABYSS®
sono marchi registrati di FISHLABS Entertainment GmbH. Tutti i diritti riservati. Questo è un progetto amatoriale non
ufficiale, non affiliato né approvato da FISHLABS o Deep Silver.

Il codice del remake è concesso in licenza con la [Apache License 2.0](../../LICENSE.md).
