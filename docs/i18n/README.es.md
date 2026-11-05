![Galaxy on Fire 2 Remake, menú principal](../../.github/header.webp)

<h1 align="center">Galaxy on Fire 2 Remake</h1>

<p align="center">
  Un remake hecho por fans del juego espacial de 2010 <i>Galaxy on Fire 2</i> de FISHLABS, reconstruido en Unity.<br>
  Vuelo, combate, comercio, minería, estaciones, el bar y toda la historia, fiel al original.
</p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.fr.md">Français</a> ·
  <b>Español</b> ·
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

<p align="center"><i>Esta es una traducción; el <a href="../../README.md">README en inglés</a> es la referencia y puede estar más actualizado.</i></p>

<p align="center">
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?label=release"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/total"></a>
  <img alt="Platforms" src="https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20Android%20%7C%20UWP-blue">
  <img alt="Unity" src="https://img.shields.io/badge/Unity-7000.0.0a12-black?logo=unity">
  <a href="../../LICENSE.md"><img alt="License" src="https://img.shields.io/badge/license-Apache%202.0-green"></a>
  <br>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/stargazers"><img alt="Stars" src="https://img.shields.io/github/stars/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/forks"><img alt="Forks" src="https://img.shields.io/github/forks/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake?style=flat&logo=github"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues"><img alt="Open issues" src="https://img.shields.io/github/issues/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/pulls"><img alt="Open pull requests" src="https://img.shields.io/github/issues-pr/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors"><img alt="Contributors" src="https://img.shields.io/github/contributors/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
  <a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/commits/main"><img alt="Last commit" src="https://img.shields.io/github/last-commit/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake"></a>
</p>

<p align="center">
  <a href="#play"><b>Jugar</b></a> ·
  <a href="#features">Características</a> ·
  <a href="#multiplayer">Multijugador</a> ·
  <a href="#mods">Mods</a> ·
  <a href="#controls">Controles</a> ·
  <a href="#help"><b>Colaborar con el proyecto</b></a> ·
  <a href="#credits">Créditos</a>
</p>

---

El remake se basa en los recursos del juego original y en la lógica del juego portada a partir del código decompilado del original.

> [!NOTE]
> **Estado:** la campaña principal y las dos expansiones, **Valkyrie** y **Supernova**, se pueden jugar de principio a fin,
> igual que el modo arcade Supernova Challenge. El multijugador, la campaña cooperativa y el Mod Builder son experimentales.
> La RV (realidad virtual) solo se ha probado sin visor hasta ahora.

## ¿Qué sección necesito?

| Quiero... | Ir a |
|---|---|
| **Jugar** en mi PC o móvil | [Jugar](#play): descargar una versión, sin necesidad de Unity |
| Jugar con amigos o montar un servidor | [Multijugador](#multiplayer) |
| Añadir naves, objetos, sistemas o misiones | [Mods](#mods): hechos en el juego o como archivos de datos, sin necesidad de Unity |
| **Cambiar el código o los recursos del juego**, o enviar un pull request | [Colaborar con el proyecto](#help): clonar el repositorio y abrirlo en Unity |

---

<a id="play"></a>

## 🎮 Jugar

No necesitas Unity, Git ni este repositorio para jugar. Descarga la versión para tu plataforma desde la
**[última versión publicada](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest)**:

| Plataforma | Descarga | Cómo instalar |
|---|---|---|
| **Windows** 10 / 11 (64 bits) | `GoF2Remake-Windows-<version>.zip` | Descomprime y ejecuta `GoF2Remake.exe` dentro de la carpeta `GoF2Remake` |
| **Linux** (x86_64) | `GoF2Remake-Linux-<version>.zip` | Descomprime y ejecuta `GoF2Remake.x86_64` (si no arranca: `chmod +x GoF2Remake.x86_64`) |
| **Android** (ARM64) | `GoF2Remake-Android-<version>.apk` | Copia el APK al móvil y ábrelo; permite instalar desde este origen cuando lo pida |
| **Windows (app UWP)** | `GoF2Remake-UWP-<version>.zip` | Descomprime, clic derecho en `Install.ps1` > **Ejecutar con PowerShell** (pide confiar en el certificado de prueba del paquete) |
| Servidor dedicado | `GoF2Remake-WindowsServer-…` / `-LinuxServer-…` | Consulta [SERVER.md](../../SERVER.md); también la imagen de Docker `ghcr.io/joppietoppie/gof2-server` |

- **Versión:** la versión de cada compilación es la fecha y hora de su publicación (por ejemplo `2026.11.02.0622`), la
  misma en todas las plataformas, y se muestra en el menú principal. El multijugador solo funciona entre jugadores con la
  misma versión.
- **Actualizar:** el menú principal muestra **Actualización disponible** cuando hay una versión más reciente. En las
  compilaciones posteriores a 2026.11.02, ese botón actualiza el juego en el mismo sitio y descarga solo lo que ha cambiado
  (Windows, Linux y Android; la app UWP se actualiza mediante el Instalador de aplicaciones de Windows). Las compilaciones
  de 2026.11.02 y anteriores necesitan una última descarga completa.
- **Las partidas guardadas** se conservan aparte de los archivos del juego, así que actualizar o reinstalar no las borra.
  Exporta e importa todas las partidas en un solo archivo desde **Cargar partida**, para llevarlas a otro PC o móvil.
- **La versión UWP** no tiene estado de Discord, giroscopio del mando, diálogo de guardado de archivos, RV ni servidor
  dedicado.

> [!TIP]
> Una instalación nueva se juega como el **juego original**. Opciones > Jugabilidad > **Experiencia de juego** activa de
> golpe las novedades del remake: *Original*, *Cosmético* (vuelos en el hangar y diálogos animados) o *Remake* (todas las
> novedades). Cada opción también se puede cambiar por separado.

### Linux y Steam Deck

- **Mandos:** los mandos de Xbox, PlayStation y Switch Pro funcionan directamente. En una Steam Deck en modo escritorio,
  o con un mando que el juego no reconoce, añade el juego a Steam (*Añadir un juego que no es de Steam*) e inícialo desde
  ahí: Steam Input lo presentará como un mando de Xbox.
- **Iniciado desde Steam en una Steam Deck:** Steam define `SteamDeck=1`, lo que hace que el reproductor de Unity para
  Linux se quede a ~0 FPS ([#232](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues/232)). El juego se
  reinicia una vez por sí mismo sin esa variable (la ventana puede abrirse dos veces). Si aun así se queda bloqueado,
  pon como opciones de lanzamiento del acceso directo `env -u SteamDeck %command%`.

---

<a id="features"></a>

## ✨ Características

- **Historia:** la campaña principal completa con el prólogo, el Void y el final, además de las expansiones Valkyrie y
  Supernova. Diálogos con voz (en inglés o alemán), retratos, charla por radio y escenas cinemáticas.
- **Vuelo:** el modelo de vuelo y la cámara de seguimiento originales, una vista de cabina y un modo opcional de vuelo
  libre al estilo de EVERSPACE 2. Las órbitas de las estaciones se reconstruyen exactamente como en el original, con sus
  cielos, planetas, soles y campos de asteroides.
- **Viajes:** piloto automático, saltos a planetas, avance rápido, el mapa estelar, las puertas de salto, el motor Khador
  y el agujero de gusano del Void. La **planificación de rutas de puertas de salto** te lleva puerta a puerta a cualquier
  sistema, por la ruta más rápida o la más segura. El **Khador Slipstream** opcional es un viaje rápido dentro de un
  sistema y entre sistemas.
- **Combate:** todas las armas (misiles, rayos, EMP, minas, torretas, cañones centinela, el Liberator...), tráfico e IA
  de los NPC, naves capitales, bases piratas, Specters y los Más Buscados. Camuflaje, sistema de emergencia, extensor de
  tiempo, rayos reparadores y escudos gamma. El modo arcade **Supernova Challenge** con puntuación de combos y récord.
- **Economía y estaciones:** hangares, tiendas y concesionarios de naves, el salón espacial con los agentes del bar y todos
  los tipos de misiones freelance, planos, compañeros de ala, medallas y el Kaamo Club.
- **Minería:** minería de asteroides con el minijuego de perforación, la **planta minera** de Mido (mineral convertido en
  núcleos, clasificación de Space Waste), nubes de gas, hackeo y acoplamiento a objetos.
- **Campaña cooperativa (experimental):** juega la historia en equipo, de 2 a 4 jugadores por defecto, desde su propio
  botón en el menú principal. El anfitrión continúa una partida de un jugador o empieza una nueva; los enemigos pueden
  hacerse más fuertes con cada jugador.
- **Multijugador (experimental):** un universo compartido con escuadrones, facciones, partidas de arena, comercio y modos
  de juego de eventos. Consulta [Multijugador](#multiplayer).
- **Mods:** nuevos objetos, naves, sistemas, estaciones, personajes, misiones y campañas enteras, creados con el
  **Mod Builder** del propio juego. Consulta [Mods](#mods).
- **RV (experimental):** RV para PC mediante OpenXR. Consulta [RV](#vr).
- **Cronómetro de speedrun:** parciales por capítulo o paso de la historia, archivos de parciales de LiveSplit y un enlace
  con LiveSplit.
- **Controles y pantallas:** táctil, inclinación, teclado y ratón, y mandos (botones de Xbox o PlayStation, elegidos
  automáticamente), todo reasignable. Control por giroscopio con DualSense, DualShock 4 o Switch Pro en Windows; vibración
  háptica en mandos y móviles Android. Pantallas apaisadas desde 4:3 hasta 32:9.
- **Idiomas:** los idiomas del original más las traducciones al neerlandés, chino simplificado e hindi, con el coreano, el
  japonés y el árabe completados (14 en total), y voces en alemán o inglés.

<details>
<summary><b>Extras del remake</b> (la mayoría son opciones en Opciones > Jugabilidad)</summary>

- **Hangar y presentación:** vuelos de llegada y despegue en el hangar, sombras suaves de las naves, profundidad de campo,
  **Inspeccionar nave** (una cámara orbital alrededor de tu nave), texto de diálogo animado y el bloom al estilo del original.
- **Mejoras de las naves capitales:** escoltas, torretas más potentes y salvas de misiles; el portaaviones y el acorazado
  Vossk se pueden destruir, hay batallas de flotas poco frecuentes entre ellos, y el portaaviones vende suministros a los
  pilotos en los que confía.
- **Eventos piratas:** en los sistemas Arriesgados y Peligrosos, una órbita puede albergar un puesto pirata o un jefe
  pirata con nombre, con recompensa y nivel de amenaza, que aparece en el tablón de Actividad pirata de la ventana de
  Misiones.
- **Objetivos de los NPC más inteligentes:** los cazas enemigos atacan a la nave hostil más cercana en lugar de ir todos a
  por ti. **El EMP rompe los camuflajes**, y el Disruptor hace el doble de daño a los escudos, como dice su descripción.
- **Colisiones con las formas reales:** los disparos impactan en lo que tocan y te deslizas a lo largo de estaciones y
  cargueros. *Colisión de naves*: Forma exacta, Mixta (los cazas conservan la caja de impacto más grande del original) u
  Original.
- **Un aviso de misiles**; acelerar o camuflarte despista a los misiles guiados. Una distancia de armado para las bombas.
- **New Game+:** empieza de nuevo con los créditos, planos, medallas y naves de una partida terminada.
- **Opciones de nueva partida:** el Kaamo Club desde el principio, **Hardcore** (muerte permanente), una partida sin
  medallas, los avisos del tutorial y la elección entre las economías de PC y de Android.
- **Cuatro dificultades** (Fácil, Normal, Difícil, Extrema), que se pueden cambiar durante la partida.
- **Comodidad:** Vender todo / Comprar todo, un fijado de blanco inteligente con un control para Soltar blanco, el mapa de
  la ventana de Misiones puede viajar hasta el objetivo de una misión, una vista general del mapa estelar a pantalla
  completa, las medallas como aviso breve, una interfaz de vuelo Completo / Mínimo / Desactivado (F1), marcadores de naves
  lejanas con distancias, iconos de enfriamiento, un contador de FPS y el sonido de disparo propio de cada arma.
- **Gráficos:** preajustes Bajo / Medio / Alto, niveles de detalle para naves, estaciones y asteroides lejanos, SMAA / FXAA /
  TAA, y escalado (FSR 1 y STP en todas partes, NVIDIA DLSS y AMD FSR 2 / 3 / 4 en Windows).
- **Modos foto** (Action Freeze en vuelo, Inspeccionar nave en el hangar): cámara libre, campo de visión, alabeo,
  profundidad de campo, ángulo de obturación y exposiciones largas, desenfoque de zoom, estilos de película, exposición,
  color, marcos y capturas de hasta 4 veces el tamaño de la pantalla.
- **Partidas guardadas:** importa una partida de Android o PC del juego original, exporta partidas para las compilaciones
  de la comunidad del original, y una carpeta de guardado a tu elección (accesible para herramientas de sincronización
  como Syncthing).
- **Herramientas de depuración** (Opciones > Jugabilidad): saltar a cualquier paso de la historia, trucos, dar objetos,
  generar naves y objetos, pilotar cualquier casco, guardar y cargar configuraciones de nave. El paso de la historia y la
  estación actuales pueden mostrarse abajo a la derecha, así que una captura de un fallo indica dónde ocurrió.
- **Discord Rich Presence** en Windows.

</details>

---

<a id="multiplayer"></a>

## 🌐 Multijugador

El multijugador es experimental. Todos los de una sesión comparten un universo en el mundo del juego terminado: os veis en
el espacio y en los hangares, formáis escuadrones, hacéis juntos misiones del bar, compartís las tiendas y el tráfico de
NPC, fundáis facciones que poseen estaciones, intercambiáis créditos y carga, y combatís en partidas de arena (o en
cualquier sitio, cuando el anfitrión elige PvP). Únete mediante el navegador de servidores (con la región y el ping de cada
partida), un código de unión o la red local.

Un piloto nuevo empieza acoplado en Dis a bordo de una Betty con 10 000 créditos. Un servidor dedicado (o el mundo
persistente de un anfitrión) conserva el progreso de cada jugador; tus partidas de un jugador nunca se tocan. Solo se
puede jugar junto con la **misma versión exacta del juego**.

| Guía | Qué contiene |
|---|---|
| **[Jugar juntos](../MULTIPLAYER.md)** | Unirse y organizar, chat, escuadrones, misiones, tiendas, facciones, poseer estaciones, combates de arena, administradores, todos los comandos de chat |
| **[Montar un servidor dedicado](../../SERVER.md)** | Las descargas del servidor para Windows / Linux y la imagen de Docker, opciones, consola, mensaje del día, hacerse administrador, sus archivos |
| **[Cómo funciona el multijugador](../MULTIPLAYER-DEV.md)** | El código, para programadores |

---

<a id="mods"></a>

## 🧩 Mods

Los mods añaden o cambian el contenido del juego mediante datos, sin código y sin Unity: objetos (con sus propios efectos
de arma y sonidos), naves a partir de modelos GLB, skins, sistemas estelares y estaciones (con sus propios modelos,
planetas, soles, cielos, hangares y bares), personajes con retratos y doblaje, música y efectos de sonido, planos,
misiones y misiones de bar, y campañas enteras que aparecen en Nueva partida.

**Instalar un mod:** en la pantalla **Mods** del menú principal, pulsa **Importar mod** y elige el `.zip` del mod (también
en Android). O coloca tú mismo el mod (una carpeta o su `.zip`) en la carpeta Mods y actívalo en la pantalla Mods. El botón
**Abrir carpeta de mods** de esa pantalla la abre:

| Plataforma | Carpeta de mods |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods`, o una carpeta `Mods` junto a `GoF2Remake.exe` |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods`, o una carpeta `Mods` junto al juego |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods` (accesible por USB) |

Una partida guardada con mods los recuerda; al cargarla sin ellos se quitan sus objetos (con reembolso). En multijugador,
el anfitrión decide si se permiten mods, y los jugadores que se unan necesitan los mismos mods.

**Crear un mod:**

- **[Mod Builder](../notes/mod-builder.md)** (Mods > Mod Builder, experimental): crea dentro del juego naves, objetos,
  skins, personajes, misiones (con un grafo de nodos), sistemas, estaciones, hangares y bares, efectos de armas, audio,
  planos, campañas y kits de nave, con deshacer, vuelos de prueba y partidas de prueba, y exporta el mod terminado.
  También reduce los modelos y texturas de un mod e importa mods de la comunidad hechos a partir de los modelos del
  original (`.aem` / `.aei`). Tu propio asistente de IA puede trabajar en él mediante su servidor MCP local, con los
  permisos que tú elijas.
- **A mano:** la guía para modders [Modding/README.md](../../Modding/README.md), con mods de ejemplo en
  [Modding/Examples](../../Modding/Examples).
- **Con un asistente de IA:** [Modding/ai](../../Modding/ai) tiene una guía para Claude, ChatGPT y otros.

---

<a id="controls"></a>

## 🕹️ Controles

<details>
<summary><b>Teclado y ratón</b></summary>

| Tecla | Acción |
|---|---|
| Flechas | Dirigir |
| W | Impulso |
| S | Freno (los motores se detienen mientras se mantiene) |
| `]` / `/` o la rueda del ratón | Aceleración |
| Espacio / botón izquierdo del ratón | Armas primarias |
| R / botón derecho del ratón | Arma secundaria |
| G | Cambiar de arma secundaria |
| A / D | Desplazamiento lateral izquierda / derecha |
| 1 / 3 | Alabeo izquierda / derecha |
| 2 | Nivelar |
| F / Intro | Acción (acoplar, piloto automático, minar, saltar) |
| Q | Menú del piloto automático |
| E | Menú de acciones (armas secundarias, compañeros de ala, camuflaje, motor Khador) |
| Tab | Avance rápido |
| T | Cámara: seguimiento, cabina, vista de torreta, mirada libre |
| V / K / C / X | Compañeros de ala / motor Khador / camuflaje / extensor de tiempo |
| M o botón central del ratón | Activar o desactivar el control con el ratón |
| U | Soltar blanco |
| B / N | Chat / ventana de multijugador (multijugador) |
| F1 | Interfaz de vuelo: completo, mínimo, desactivado |
| F12 | Captura de pantalla |
| Esc | Pausa |

</details>

Todos los controles de vuelo se pueden reasignar en Opciones > Controles (dos teclas de teclado / ratón y un botón de mando
para cada uno). Los mandos y el táctil están totalmente soportados, y las indicaciones del juego muestran los botones del
dispositivo de entrada que usaste por última vez.

<a id="vr"></a>

### RV (experimental)

Inicia la versión de Windows con `-vr` (por ejemplo, un acceso directo a `GoF2Remake.exe -vr`) con un visor de RV para PC
conectado y un runtime de OpenXR activo (SteamVR, Meta Quest Link, Windows Mixed Reality). Sin visor, el juego arranca con
normalidad.

<details>
<summary>Cómo es la RV</summary>

- **Vuelo:** te sientas en una cabina. El escudo, el casco y la carga están en sus pantallas, el radar en el centro del
  salpicadero y el resto de la interfaz en la cúpula. Las escenas cinemáticas mantienen el horizonte nivelado y se
  funden entre planos.
- **Estaciones:** estás de pie en el hangar junto a tu nave (agárrala con el grip y tira para girarla) y en el bar
  (apunta a un visitante y aprieta el gatillo para hablar).
- **Los menús** aparecen en una pantalla flotante; apunta con el mando derecho y aprieta el gatillo.
- **Controles:** los mandos funcionan como un gamepad (palancas, gatillos, A / B / X / Y, los grips como LB / RB). Con
  Opciones > Controles > "Vuelo en RV: agarrar la palanca y el acelerador" pilotas con la palanca lateral y la palanca de
  aceleración de la cabina.

Por ahora la cabina es la misma para todas las naves.

</details>

---

<a id="help"></a>

## 🛠️ Colaborar con el proyecto

Esta sección es para cambiar el juego en sí: su código, escenas y recursos, o compilar tus propias copias. **Para jugar
sin más, usa una [versión publicada](#play); para añadir contenido, normalmente basta un [mod](#mods).**

### Requisitos

- **Unity 7000.0.0a12** (una alfa, instalada con Unity Hub). Añade los módulos de compatibilidad con las plataformas que
  compiles: *Windows Build Support (IL2CPP)*, *Linux Build Support (IL2CPP)*, *Android Build Support*, y los módulos
  *Dedicated Server* para compilar servidores.
- **Git** con **[Git LFS](https://git-lfs.com)**. Los modelos, texturas y sonidos ocupan unos 2,1 GB en LFS y se descargan
  del servidor LFS propio del proyecto (configurado en [.lfsconfig](../../.lfsconfig)).
- Para **compilar en Windows:** Visual Studio 2022 o posterior con la carga de trabajo *Desarrollo para el escritorio con
  C++* y un SDK de Windows 10 / 11 (IL2CPP). La compilación UWP también necesita *Herramientas de la Plataforma universal
  de Windows para C++*.

### Poner el proyecto en marcha

```bash
git lfs install
```

```bash
git clone https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake.git
```

1. Abre la carpeta clonada en Unity Hub (*Add > Add project from disk*) con Unity 7000.0.0a12. La primera importación
   tarda un rato.
2. Abre `Assets/Scenes/Splash.unity` (o `MainMenu.unity` para saltarte los logos) y pulsa **Play**.

Las escenas son `Splash` (índice de compilación 0), `MainMenu`, `Space` (el nivel de vuelo) y `Station` (acoplado). Los
recursos generados (prefabs, materiales, iconos, imágenes de la interfaz) ya están en el repositorio; el menú **GoF2** del
Editor los reconstruye, y **GoF2 > Tools Overview** lista todas las herramientas.

> [!IMPORTANT]
> No generes iluminación para las escenas. Carecen de iluminación precalculada a propósito: el juego construye sus niveles
> en tiempo de ejecución y cada sistema tiene su propio cielo (`SkyReflection` actualiza la luz ambiental a partir de él).
> Una sonda precalculada sería un único cielo fijo para todos los sistemas.

### Compilar

- **Cambia siempre el perfil de compilación activo** (File > Build Profiles > Switch Profile) antes de compilar para otra
  plataforma. URP conserva las variantes de shader solo de la plataforma activa: una compilación de Android hecha con
  Windows activo se ve negra, así que el script de Editor `BuildTargetGuard` la rechaza.
- `BuildVersionStamp` da a cada compilación su versión a partir de la fecha y hora de su commit de git. Para una versión
  publicada, define la variable de entorno `GOF2_BUILD_VERSION` del proceso del Editor para que todas las plataformas
  reciban la misma.
- Servidores dedicados: **GoF2 > Build > Linux Dedicated Server** / **Windows Dedicated Server**. La carpeta `docker/`
  contiene la imagen del servidor.
- Parches de actualización para el actualizador del juego: **GoF2 > Build > Update Patch** después de las compilaciones.

### Pruebas

Unas 3000 pruebas en modo edición cubren las reglas y los datos, además de pruebas de humo en modo juego de las escenas,
los niveles de la historia y una sesión alojada. Ejecútalas desde Window > General > Test Runner, o con la CLI de `unity`
conectada:

```bash
python -I Reference/tools/tests/run_tests.py
```

Consulta [Assets/Scripts/Tests/README.md](../../Assets/Scripts/Tests/README.md).

### Estructura del proyecto

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

### Documentación

- [CLAUDE.md](../../CLAUDE.md): las reglas generales del proyecto (convenciones, coordenadas, datos) y el índice de las
  notas técnicas.
- [docs/notes/](../notes): un archivo por área del juego (vuelo, combate, historia, tienda, multijugador, mods...), con las
  funciones originales en las que se basa cada sistema y las decisiones que tomó el remake.
- [Assets/Scripts/Runtime/README.md](../../Assets/Scripts/Runtime/README.md): las capas de ensamblados y sus hooks.
- [Reference/README.md](../../Reference/README.md): el código decompilado y las herramientas que lo leen.

### Contribuir

- **¿Has encontrado un fallo?** Abre un [issue](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues) con
  tu plataforma, la versión del juego (se muestra en el menú principal) y una captura de pantalla. Opciones > Jugabilidad
  puede mostrar el paso de la historia y la estación actuales abajo a la derecha, lo que nos dice dónde ocurrió.
- **Los pull requests son bienvenidos.** Haz un fork del repositorio, haz tu cambio en una rama, ejecuta las pruebas en
  modo edición y abre un pull request contra `main`. Antes de trabajar en un área, lee su archivo en
  [docs/notes/](../notes); las convenciones están en [CLAUDE.md](../../CLAUDE.md).
- **¿Te gusta el proyecto?** Dale una ⭐ en GitHub, ayuda a que otros lo encuentren.

---

<a id="credits"></a>

## 🙏 Créditos

**Galaxy on Fire 2 Remake** de JoppieToppie, con pull requests de KiritoJPK, DuIslingr, Zippadelic, FantasyToLife y
otros.

El remake se basa en la **versión FULL HD y las modificaciones de KiritoJPK**, gracias a la comunidad de Galaxy on Fire 2™
y 4PDA: <https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

Fuentes: la tipografía de la interfaz del juego original e Inter (SIL Open Font License). Giroscopio del mando:
[JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary) de Julian Smart (licencia MIT). Hecho con Unity, el
Universal Render Pipeline, Netcode for GameObjects y OpenXR.

### Colaboradores

<a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors">
  <img alt="Colaboradores" src="https://contrib.rocks/image?repo=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake">
</a>

## ⭐ Historial de estrellas

<a href="https://star-history.com/#JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&Date">
  <img alt="Gráfico del historial de estrellas" src="https://api.star-history.com/svg?repos=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&type=Date">
</a>

## ⚖️ Aviso legal

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ y ABYSS®
son marcas registradas de FISHLABS Entertainment GmbH. Todos los derechos reservados. Este es un proyecto no oficial de
fans, sin afiliación ni respaldo de FISHLABS o Deep Silver.

El código propio del remake está bajo la [Apache License 2.0](../../LICENSE.md).
