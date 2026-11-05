![Menu principal do Galaxy on Fire 2 Remake](../../.github/header.webp)

<h1 align="center">Galaxy on Fire 2 Remake</h1>

<p align="center">
  Um remake feito por fãs do jogo espacial <i>Galaxy on Fire 2</i> (2010), da FISHLABS, reconstruído no Unity.<br>
  Voo, combate, comércio, mineração, estações, o bar e a história inteira, fiéis ao original.
</p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.pl.md">Polski</a> ·
  <a href="README.ru.md">Русский</a> ·
  <b>Português (Brasil)</b> ·
  <a href="README.nl.md">Nederlands</a> ·
  <a href="README.zh-CN.md">简体中文</a> ·
  <a href="README.hi.md">हिन्दी</a> ·
  <a href="README.ko.md">한국어</a> ·
  <a href="README.ja.md">日本語</a> ·
  <a href="README.ar.md">العربية</a>
</p>

<p align="center"><i>Esta é uma tradução; o <a href="../../README.md">README em inglês</a> é a referência e pode estar mais atualizado.</i></p>

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
  <a href="#play"><b>Jogue o jogo</b></a> ·
  <a href="#features">Recursos</a> ·
  <a href="#multiplayer">Multijogador</a> ·
  <a href="#mods">Mods</a> ·
  <a href="#controls">Controles</a> ·
  <a href="#help"><b>Ajude no projeto</b></a> ·
  <a href="#credits">Créditos</a>
</p>

---

O remake é construído sobre os assets do jogo original e sobre a lógica de jogo portada do código descompilado do original.

> [!NOTE]
> **Situação:** a campanha principal e as duas expansões, **Valkyrie** e **Supernova**, podem ser jogadas do começo ao
> fim, assim como o modo arcade Supernova Challenge. O multijogador, a campanha cooperativa e o Criador de mods (Mod
> Builder) são experimentais. O VR só foi testado sem headset até agora.

## Qual seção me interessa?

| Eu quero... | Ir para |
|---|---|
| **Jogar o jogo** no meu PC ou celular | [Jogue o jogo](#play): baixe uma release, sem precisar do Unity |
| Jogar com amigos ou rodar um servidor | [Multijogador](#multiplayer) |
| Adicionar naves, itens, sistemas ou missões | [Mods](#mods): feitos no jogo ou como arquivos de dados, sem precisar do Unity |
| **Mudar o código ou os assets do jogo**, ou enviar um pull request | [Ajude no projeto](#help): clone o repositório e abra no Unity |

---

<a id="play"></a>

## 🎮 Jogue o jogo

Você não precisa do Unity, do Git nem deste repositório para jogar. Baixe a versão para a sua plataforma na
**[última release](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest)**:

| Plataforma | Download | Como instalar |
|---|---|---|
| **Windows** 10 / 11 (64 bits) | `GoF2Remake-Windows-<version>.zip` | Descompacte e execute `GoF2Remake.exe` na pasta `GoF2Remake` |
| **Linux** (x86_64) | `GoF2Remake-Linux-<version>.zip` | Descompacte e execute `GoF2Remake.x86_64` (se não iniciar: `chmod +x GoF2Remake.x86_64`) |
| **Android** (ARM64) | `GoF2Remake-Android-<version>.apk` | Copie o APK para o celular e abra-o; permita instalações desta fonte quando pedido |
| **Windows (app UWP)** | `GoF2Remake-UWP-<version>.zip` | Descompacte, clique com o botão direito em `Install.ps1` > **Executar com o PowerShell** (ele pede para confiar no certificado de teste do pacote) |
| Servidor dedicado | `GoF2Remake-WindowsServer-…` / `-LinuxServer-…` | Veja [SERVER.md](../../SERVER.md); também há a imagem Docker `ghcr.io/joppietoppie/gof2-server` |

- **Versão:** a versão de cada build é a data e a hora da sua release (por exemplo `2026.11.02.0622`), igual em todas as
  plataformas e exibida no menu principal. O multijogador só funciona entre jogadores na mesma versão.
- **Atualização:** o menu principal mostra **Atualização disponível** quando há uma release mais nova. Nas builds
  posteriores a 2026.11.02, esse botão atualiza o jogo no lugar e baixa só o que mudou (Windows, Linux e Android; o app
  UWP é atualizado pelo App Installer do Windows). As builds de 2026.11.02 e anteriores precisam de mais um download
  completo.
- Os **jogos salvos** ficam separados dos arquivos do jogo, então atualizar ou reinstalar não os apaga. Exporte e importe
  todos os saves num único arquivo em **Carregar jogo**, para levá-los a outro PC ou celular.
- **A versão UWP** não tem status do Discord, giroscópio do controle, diálogo de salvar arquivo, VR nem servidor dedicado.

> [!TIP]
> Uma instalação nova joga como o **jogo original**. Opções > Jogabilidade > **Experiência de jogo** liga de uma vez as
> novidades do remake: *Original*, *Cosmético* (voos no hangar e diálogos animados) ou *Remake* (todas as novidades).
> Cada opção também pode ser alterada separadamente.

### Linux e Steam Deck

- **Controles:** os controles de Xbox, PlayStation e Switch Pro funcionam diretamente. No Steam Deck em modo desktop, ou
  com um controle que o jogo não reconhece, adicione o jogo ao Steam (*Adicionar um jogo que não é da Steam*) e inicie-o
  por lá: o Steam Input o apresenta então como um controle de Xbox.
- **Iniciado pelo Steam num Steam Deck:** o Steam define `SteamDeck=1`, o que faz o player Linux do Unity travar em ~0 FPS
  ([#232](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues/232)). O jogo reinicia sozinho uma vez
  sem isso (a janela pode abrir duas vezes). Se ainda travar, coloque nas Opções de inicialização do atalho
  `env -u SteamDeck %command%`.

---

<a id="features"></a>

## ✨ Recursos

- **História:** a campanha principal completa, com o prólogo, o Void e o final, além das expansões Valkyrie e Supernova.
  Diálogos dublados (em inglês ou alemão), retratos, conversas de rádio e cutscenes.
- **Voo:** o modelo de voo e a câmera de perseguição do original, uma visão de cabine e um modo opcional de voo livre ao
  estilo de EVERSPACE 2. As órbitas das estações são reconstruídas exatamente como no original, com seus céus, planetas,
  sóis e campos de asteroides.
- **Viagem:** piloto automático, saltos para planetas, avanço rápido, o mapa estelar, portais, o Khador Drive e o buraco
  de minhoca do Void. O **planejamento de rotas por portais** leva você de portal em portal até qualquer sistema, pela
  rota mais rápida ou mais segura. O **Khador Slipstream**, opcional, é uma viagem rápida dentro de um sistema e entre
  sistemas.
- **Combate:** todas as armas (mísseis, feixes, EMP, minas, torretas, canhões de sentinela, o Liberator...), tráfego e IA
  de NPCs, naves capitais, bases piratas, Specters e os Procurados. Camuflagem, sistema de emergência, extensor de tempo,
  feixes de reparo e escudos gama. O modo arcade **Supernova Challenge**, com pontuação de combo e recorde.
- **Economia e estações:** hangares, lojas e revendedores de naves, a Sala Espacial com agentes de bar e todos os tipos
  de missão freelance, projetos, parceiros de ala, medalhas e o Clube Kaamo.
- **Mineração:** mineração de asteroides com o minigame de perfuração, a **Mineradora** de Mido (minério em núcleos,
  separação de lixo espacial), nuvens de gás, hacking e atracação em objetos.
- **Campanha cooperativa (experimental):** jogue a história em conjunto, de 2 a 4 jogadores por padrão, com um botão
  próprio no menu principal. O anfitrião continua um save do modo solo ou começa um novo jogo; os inimigos podem ficar
  mais fortes por jogador.
- **Multijogador (experimental):** um universo compartilhado com esquadrões, facções, partidas de arena, comércio e modos
  de jogo com eventos. Veja [Multijogador](#multiplayer).
- **Mods:** novos itens, naves, sistemas, estações, personagens, missões e campanhas inteiras, feitos com o
  **Criador de mods** (Mod Builder) do próprio jogo. Veja [Mods](#mods).
- **VR (experimental):** VR de PC via OpenXR. Veja [VR](#vr).
- **Cronômetro de speedrun:** splits por capítulo ou etapa da história, arquivos de splits do LiveSplit e uma conexão com
  o LiveSplit.
- **Controles e telas:** toque, inclinação, teclado e mouse, e controles (botões de Xbox ou PlayStation, escolhidos
  automaticamente), todos reconfiguráveis. Pilotagem por giroscópio com DualSense, DualShock 4 ou Switch Pro no Windows;
  vibração háptica em controles e celulares Android. Telas horizontais de 4:3 até 32:9.
- **Idiomas:** os idiomas do original mais traduções para neerlandês, chinês simplificado e híndi, com coreano, japonês e
  árabe completados (14 no total), e vozes em alemão ou inglês.

<details>
<summary><b>Extras do remake</b> (a maioria são opções em Opções > Jogabilidade)</summary>

- **Hangar e apresentação:** voos de chegada e de decolagem no hangar, sombras suaves das naves, profundidade de campo,
  **Inspecionar nave** (uma câmera orbital em volta da sua nave), texto de diálogo animado e o bloom no estilo do original.
- **Melhorias das naves capitais:** escoltas, torretas mais fortes e salvas de mísseis; o porta-naves e o encouraçado
  vossk podem ser destruídos, há raras batalhas de frotas entre eles, e o porta-naves vende suprimentos aos pilotos em
  quem confia.
- **Eventos piratas:** em sistemas Arriscados e Perigosos, uma órbita pode ter um posto avançado pirata ou um chefe pirata
  nomeado, com recompensa e nível de ameaça, listados no quadro Atividade pirata da janela Missões.
- **Mira de NPCs mais inteligente:** os caças inimigos atacam a nave hostil mais próxima em vez de todos irem atrás de
  você. **O EMP desfaz a camuflagem**, e o Disruptor causa o dobro de dano aos escudos, como diz a sua descrição.
- **Colisão nas formas reais:** os tiros acertam o que tocam e você desliza ao longo de estações e cargueiros. *Colisão de
  naves*: Forma exata, Mista (os caças mantêm a caixa de colisão maior do original) ou Original.
- **Um aviso de míssil**; acelerar ou se camuflar despista mísseis teleguiados. Uma distância de armamento para as bombas.
- **Novo Jogo+:** recomece com os créditos, projetos, medalhas e naves de um jogo concluído.
- **Opções de novo jogo:** o Clube Kaamo desde o início, o modo **Hardcore** (morte permanente), um jogo sem medalhas, os
  pop-ups do tutorial, e a escolha entre as economias de PC e de Android.
- **Quatro dificuldades** (Fácil, Normal, Difícil, Extremo), que podem ser mudadas durante o jogo.
- **Qualidade de vida:** Vender tudo / Comprar tudo, uma trava de alvo inteligente com um controle de Soltar alvo, o mapa
  da janela Missões que pode viajar até o alvo de uma missão, uma visão geral do mapa estelar em tela cheia, medalhas
  como um aviso curto, um HUD de voo Completo / Mínimo / Desligado (F1), marcadores de naves distantes com distâncias,
  ícones de recarga, um contador de FPS e o som de tiro próprio de cada arma.
- **Gráficos:** predefinições Baixo / Médio / Alto, níveis de detalhe para naves, estações e asteroides distantes,
  SMAA / FXAA / TAA e upscaling (FSR 1 e STP em todo lugar, NVIDIA DLSS e AMD FSR 2 / 3 / 4 no Windows).
- **Modos foto** (Action Freeze em voo, Inspecionar nave no hangar): câmera livre, campo de visão, rotação, profundidade
  de campo, ângulo do obturador e longas exposições, desfoque de zoom, estilos de filme, exposição, cor, molduras e
  capturas de tela de até 4x o tamanho da tela.
- **Saves:** importe um save de Android ou PC do jogo original, exporte saves para as builds da comunidade do original e
  use uma pasta de saves à sua escolha (acessível por ferramentas de sincronização como o Syncthing).
- **Ferramentas de debug** (Opções > Jogabilidade): ir para qualquer etapa da história, trapaças, dar itens, criar naves
  e objetos, pilotar qualquer casco, salvar e carregar configurações de naves. A etapa da história e a estação atuais
  podem aparecer no canto inferior direito, para que uma captura de tela de um bug mostre onde ele aconteceu.
- **Discord Rich Presence** no Windows.

</details>

---

<a id="multiplayer"></a>

## 🌐 Multijogador

O multijogador é experimental. Todos numa sessão compartilham um universo no mundo do jogo concluído: vocês se veem no
espaço e nos hangares, formam esquadrões, fazem missões de bar juntos, dividem as lojas e o tráfego de NPCs, fundam
facções que possuem estações, trocam créditos e carga, e lutam em partidas de arena (ou em qualquer lugar, quando o
anfitrião ativa o PvP). Entre pelo navegador de servidores (com a região e o ping de cada jogo), por um código de acesso
ou pela rede local.

Um piloto novo começa atracado em Dis numa Betty com 10 000 créditos. Um servidor dedicado (ou o mundo persistente de um
anfitrião) guarda o progresso de cada jogador; seus saves do modo solo nunca são tocados. Só a **mesma versão exata do
jogo** pode jogar junto.

| Guia | O que contém |
|---|---|
| **[Jogando juntos](../MULTIPLAYER.md)** | Entrar e hospedar, chat, esquadrões, missões, lojas, facções, posse de estações, lutas de arena, admins, todos os comandos de chat |
| **[Rodando um servidor dedicado](../../SERVER.md)** | Os downloads do servidor para Windows / Linux e a imagem Docker, opções, console, mensagem do dia, tornar-se admin, seus arquivos |
| **[Como o multijogador funciona](../MULTIPLAYER-DEV.md)** | O código, para programadores |

---

<a id="mods"></a>

## 🧩 Mods

Os mods adicionam ou mudam o conteúdo do jogo como dados, sem código e sem Unity: itens (com seus próprios efeitos de arma
e sons), naves a partir de modelos GLB, skins, sistemas estelares e estações (com seus próprios modelos, planetas, sóis,
céus, hangares e bares), personagens com retratos e dublagem, música e efeitos sonoros, projetos, missões e missões de
bar, e campanhas inteiras que aparecem em Novo jogo.

**Instalando um mod:** na tela **Mods** do menu principal, pressione **Importar mod** e escolha o `.zip` do mod (também
no Android). Ou coloque o mod (uma pasta ou o `.zip`) você mesmo na pasta de mods e ative-o na tela Mods. O botão
**Abrir pasta de mods** da tela a abre:

| Plataforma | Pasta de mods |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods`, ou uma pasta `Mods` ao lado de `GoF2Remake.exe` |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods`, ou uma pasta `Mods` ao lado do jogo |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods` (acessível por USB) |

Um save feito com mods se lembra deles; carregá-lo sem eles remove os itens deles (com reembolso). No multijogador, o
anfitrião decide se os mods são permitidos, e os jogadores que entram precisam dos mesmos mods.

**Fazendo um mod:**

- **[Criador de mods](../notes/mod-builder.md)** (Mods > Criador de mods, experimental; Mod Builder): cria naves, itens,
  skins, personagens, missões (com um grafo de nós), sistemas, estações, hangares e bares, efeitos de armas, áudio,
  projetos, campanhas e kits de naves dentro do jogo, com desfazer, voos de teste e testes de jogo, e exporta o mod
  pronto. Ele também reduz os modelos e texturas de um mod e importa mods da comunidade feitos a partir dos modelos do
  original (`.aem` / `.aei`). O seu próprio assistente de IA pode trabalhar nele pelo servidor MCP local, com as
  permissões que você escolher.
- **À mão:** o guia para criadores de mods [Modding/README.md](../../Modding/README.md), com mods de exemplo em
  [Modding/Examples](../../Modding/Examples).
- **Com um assistente de IA:** [Modding/ai](../../Modding/ai) tem um guia para o Claude, o ChatGPT e outros.

---

<a id="controls"></a>

## 🕹️ Controles

<details>
<summary><b>Teclado e mouse</b></summary>

| Tecla | Ação |
|---|---|
| Setas | Direção |
| W | Turbo |
| S | Freio (os motores param enquanto pressionada) |
| `]` / `/` ou a roda do mouse | Aceleração |
| Espaço / botão esquerdo do mouse | Armas primárias |
| R / botão direito do mouse | Arma secundária |
| G | Trocar de arma secundária |
| A / D | Deslocamento lateral para a esquerda / direita |
| 1 / 3 | Rolagem para a esquerda / direita |
| 2 | Nivelar |
| F / Enter | Ação (atracar, piloto automático, minerar, saltar) |
| Q | Menu do piloto automático |
| E | Menu de ações (armas secundárias, parceiros de ala, camuflagem, Khador Drive) |
| Tab | Avanço rápido |
| T | Câmera: perseguição, cabine, visão da torreta, visão livre |
| V / K / C / X | Parceiros de ala / Khador Drive / camuflagem / extensor de tempo |
| M ou botão do meio do mouse | Liga / desliga a pilotagem com mouse |
| U | Soltar alvo |
| B / N | Chat / janela do multijogador (multijogador) |
| F1 | HUD de voo: completo, mínimo, desligado |
| F12 | Captura de tela |
| Esc | Pausa |

</details>

Todos os controles de voo podem ser reconfigurados em Opções > Teclas (duas teclas de teclado / mouse e um botão de
controle para cada um). Controles e toque são totalmente suportados, e as dicas no jogo mostram os botões da entrada que
você usou por último.

<a id="vr"></a>

### VR (experimental)

Inicie a build do Windows com `-vr` (por exemplo, um atalho para `GoF2Remake.exe -vr`) com um headset de VR de PC
conectado e um runtime OpenXR ativo (SteamVR, Meta Quest Link, Windows Mixed Reality). Sem headset, o jogo inicia
normalmente.

<details>
<summary>Como é o VR</summary>

- **Voo:** você fica sentado numa cabine. Escudo, casco e carga estão nos mostradores dela, o radar no meio do painel e o
  resto do HUD na cúpula. As cutscenes mantêm o horizonte nivelado e fazem transição suave entre os planos.
- **Estações:** você fica em pé no hangar ao lado da sua nave (segure-a com o botão de pegada e puxe para girá-la) e no
  bar (aponte para um frequentador e puxe o gatilho para falar).
- **Os menus** aparecem numa tela flutuante; aponte com o controle direito e puxe o gatilho.
- **Controles:** os controles funcionam como um gamepad (analógicos, gatilhos, A / B / X / Y, os botões de pegada como
  LB / RB). Com Opções > Controle > "Voo em VR: segurar o manche e o acelerador", você pilota com o manche lateral e a
  alavanca de aceleração da cabine.

A cabine é a mesma para todas as naves por enquanto.

</details>

---

<a id="help"></a>

## 🛠️ Ajude no projeto

Esta seção é para mudar o próprio jogo: seu código, cenas e assets, ou compilar suas próprias cópias. **Para só jogar,
use uma [release](#play); para adicionar conteúdo, um [mod](#mods) costuma bastar.**

### Requisitos

- **Unity 7000.0.0a12** (uma alpha, instalada pelo Unity Hub). Adicione os módulos de suporte de build das plataformas
  para as quais você compila: *Windows Build Support (IL2CPP)*, *Linux Build Support (IL2CPP)*, *Android Build Support*
  e os módulos *Dedicated Server* para builds de servidor.
- **Git** com **[Git LFS](https://git-lfs.com)**. Os modelos, texturas e sons somam cerca de 2,1 GB no LFS, buscados do
  servidor LFS do próprio projeto (definido em [.lfsconfig](../../.lfsconfig)).
- Para **builds de Windows:** Visual Studio 2022 ou mais recente com a carga de trabalho *Desenvolvimento para desktop com
  C++* e um SDK do Windows 10 / 11 (IL2CPP). A build UWP também precisa das *Ferramentas da Plataforma Universal do
  Windows para C++*.

### Colocando o projeto para rodar

```bash
git lfs install
```

```bash
git clone https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake.git
```

1. Abra a pasta clonada no Unity Hub (*Add > Add project from disk*) com o Unity 7000.0.0a12. A primeira importação
   demora um pouco.
2. Abra `Assets/Scenes/Splash.unity` (ou `MainMenu.unity` para pular os logotipos) e pressione **Play**.

As cenas são `Splash` (índice de build 0), `MainMenu`, `Space` (o nível de voo) e `Station` (atracado). Os assets gerados
(prefabs, materiais, ícones, imagens do HUD) já estão no repositório; o menu **GoF2** do Editor os recria, e
**GoF2 > Tools Overview** lista todas as ferramentas.

> [!IMPORTANT]
> Não gere a iluminação das cenas. Elas não têm iluminação pré-calculada (baked) de propósito: o jogo monta seus níveis em
> tempo de execução e cada sistema tem o seu próprio céu (o `SkyReflection` atualiza a luz ambiente a partir dele). Uma
> sonda pré-calculada seria um único céu fixo para todos os sistemas.

### Compilando

- Sempre **troque o perfil de build ativo** (File > Build Profiles > Switch Profile) antes de compilar para outra
  plataforma. O URP guarda variantes de shader só para a plataforma ativa: uma build de Android feita com o Windows ativo
  fica toda preta, por isso o script de editor `BuildTargetGuard` a recusa.
- O `BuildVersionStamp` dá a cada build a sua versão a partir da data e da hora do commit do git. Para uma release,
  defina a variável de ambiente `GOF2_BUILD_VERSION` do processo do Editor, para que todas as plataformas recebam a mesma.
- Servidores dedicados: **GoF2 > Build > Linux Dedicated Server** / **Windows Dedicated Server**. A pasta `docker/` tem a
  imagem do servidor.
- Patches de atualização para o atualizador do jogo: **GoF2 > Build > Update Patch**, depois das builds.

### Testes

Cerca de 3000 testes em modo de edição cobrem as regras e os dados, além de testes de fumaça em modo de jogo das cenas,
dos níveis da história e de uma sessão hospedada. Execute-os em Window > General > Test Runner, ou com a CLI `unity`
conectada:

```bash
python -I Reference/tools/tests/run_tests.py
```

Veja [Assets/Scripts/Tests/README.md](../../Assets/Scripts/Tests/README.md).

### Estrutura do projeto

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

### Documentação

- [CLAUDE.md](../../CLAUDE.md): as regras do projeto como um todo (convenções, coordenadas, dados) e o índice das notas
  técnicas.
- [docs/notes/](../notes): um arquivo por área do jogo (voo, combate, história, loja, multijogador, mods...), com as
  funções originais em que cada sistema se baseia e as escolhas que o remake fez.
- [Assets/Scripts/Runtime/README.md](../../Assets/Scripts/Runtime/README.md): as camadas de assemblies e seus hooks.
- [Reference/README.md](../../Reference/README.md): o código descompilado e as ferramentas que o leem.

### Contribuindo

- **Achou um bug?** Abra uma [issue](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues) com a sua
  plataforma, a versão do jogo (mostrada no menu principal) e uma captura de tela. Opções > Jogabilidade pode mostrar a
  etapa da história e a estação atuais no canto inferior direito, o que nos diz onde aconteceu.
- **Pull requests são bem-vindos.** Faça um fork do repositório, faça a sua mudança numa branch, execute os testes em
  modo de edição e abra um pull request para a `main`. Antes de trabalhar numa área, leia o arquivo dela em
  [docs/notes/](../notes); as convenções estão no [CLAUDE.md](../../CLAUDE.md).
- **Gostou do projeto?** Dê uma ⭐ no GitHub, isso ajuda outras pessoas a encontrá-lo.

---

<a id="credits"></a>

## 🙏 Créditos

**Galaxy on Fire 2 Remake** por JoppieToppie, com pull requests de KiritoJPK, DuIslingr, Zippadelic, FantasyToLife e
outros.

O remake é construído sobre a **versão FULL HD e as modificações feitas por KiritoJPK**, graças à comunidade do Galaxy on
Fire 2™ e do 4PDA: <https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

Fontes: a fonte da interface do jogo original e a Inter (SIL Open Font License). Giroscópio do controle:
[JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary), de Julian Smart (licença MIT). Feito com Unity, o
Universal Render Pipeline, o Netcode for GameObjects e o OpenXR.

### Contribuidores

<a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors">
  <img alt="Contribuidores" src="https://contrib.rocks/image?repo=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake">
</a>

## ⭐ Histórico de estrelas

<a href="https://star-history.com/#JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&Date">
  <img alt="Gráfico do histórico de estrelas" src="https://api.star-history.com/svg?repos=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&type=Date">
</a>

## ⚖️ Aviso legal

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ and ABYSS®
are registered trademarks of FISHLABS Entertainment GmbH. All rights reserved. Este é um projeto não oficial feito por
fãs, sem afiliação nem aprovação da FISHLABS ou da Deep Silver.

O código próprio do remake é licenciado sob a [Apache License 2.0](../../LICENSE.md).
