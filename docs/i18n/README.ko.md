![Galaxy on Fire 2 Remake 메인 메뉴](../../.github/header.webp)

<h1 align="center">Galaxy on Fire 2 Remake</h1>

<p align="center">
  FISHLABS가 2010년에 만든 우주 게임 <i>Galaxy on Fire 2</i>를 Unity로 다시 만든 팬 리메이크입니다.<br>
  비행, 전투, 거래, 채굴, 정거장, 바, 그리고 전체 스토리까지 원작에 충실하게 담았습니다.
</p>

<p align="center">
  <a href="../../README.md">English</a> ·
  <a href="README.de.md">Deutsch</a> ·
  <a href="README.fr.md">Français</a> ·
  <a href="README.es.md">Español</a> ·
  <a href="README.it.md">Italiano</a> ·
  <a href="README.pl.md">Polski</a> ·
  <a href="README.ru.md">Русский</a> ·
  <a href="README.pt-BR.md">Português (Brasil)</a> ·
  <a href="README.nl.md">Nederlands</a> ·
  <a href="README.zh-CN.md">简体中文</a> ·
  <a href="README.hi.md">हिन्दी</a> ·
  <b>한국어</b> ·
  <a href="README.ja.md">日本語</a> ·
  <a href="README.ar.md">العربية</a>
</p>

<p align="center"><i>이 문서는 번역본입니다. 기준이 되는 문서는 <a href="../../README.md">English README</a>이며, 더 최신일 수 있습니다.</i></p>

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
  <a href="#play"><b>게임 플레이</b></a> ·
  <a href="#features">기능</a> ·
  <a href="#multiplayer">멀티플레이어</a> ·
  <a href="#mods">모드</a> ·
  <a href="#controls">조작</a> ·
  <a href="#help"><b>프로젝트 참여</b></a> ·
  <a href="#credits">크레딧</a>
</p>

---

이 리메이크는 원작 게임의 에셋과, 디컴파일한 원작 코드에서 옮겨 온 게임 로직을 바탕으로 만들어졌습니다.

> [!NOTE]
> **현재 상태:** 메인 캠페인과 두 확장팩 **Valkyrie**, **Supernova**를 처음부터 끝까지 플레이할 수 있고, Supernova Challenge
> 아케이드 모드도 마찬가지입니다. 멀티플레이어, 협동 캠페인, Mod Builder는 아직 실험 단계입니다. VR은 지금까지 헤드셋 없이만
> 테스트했습니다.

## 어떤 섹션을 보면 될까요?

| 하고 싶은 일 | 이동할 곳 |
|---|---|
| PC나 휴대폰에서 **게임 플레이** | [게임 플레이](#play): 릴리스를 내려받으면 되며 Unity는 필요 없습니다 |
| 친구와 함께 플레이하거나 서버 운영 | [멀티플레이어](#multiplayer) |
| 함선, 아이템, 시스템, 퀘스트 추가 | [모드](#mods): 게임 안에서 또는 데이터 파일로 만들며 Unity는 필요 없습니다 |
| **게임의 코드나 에셋 수정** 또는 풀 리퀘스트 보내기 | [프로젝트 참여](#help): 저장소를 클론해서 Unity로 엽니다 |

---

<a id="play"></a>

## 🎮 게임 플레이

게임을 하는 데 Unity, Git, 이 저장소는 필요 없습니다. **[최신 릴리스](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest)**에서
사용하는 플랫폼용 빌드를 내려받으세요.

| 플랫폼 | 다운로드 | 설치 방법 |
|---|---|---|
| **Windows** 10 / 11 (64비트) | `GoF2Remake-Windows-<version>.zip` | 압축을 풀고 `GoF2Remake` 폴더의 `GoF2Remake.exe`를 실행합니다 |
| **Linux** (x86_64) | `GoF2Remake-Linux-<version>.zip` | 압축을 풀고 `GoF2Remake.x86_64`를 실행합니다(실행되지 않으면 `chmod +x GoF2Remake.x86_64`) |
| **Android** (ARM64) | `GoF2Remake-Android-<version>.apk` | APK를 휴대폰에 복사해 열고, 안내가 나오면 이 출처의 설치를 허용합니다 |
| **Windows (UWP 앱)** | `GoF2Remake-UWP-<version>.zip` | 압축을 풀고 `Install.ps1`을 우클릭해 > **PowerShell로 실행**을 고릅니다(패키지의 테스트 인증서를 신뢰할지 묻습니다) |
| 전용 서버 | `GoF2Remake-WindowsServer-…` / `-LinuxServer-…` | [SERVER.md](../../SERVER.md)를 참고하세요. Docker 이미지 `ghcr.io/joppietoppie/gof2-server`도 있습니다 |

- **버전:** 모든 빌드의 버전은 릴리스 날짜와 시각입니다(예: `2026.11.02.0622`). 모든 플랫폼이 같고 메인 메뉴에 표시됩니다.
  멀티플레이어는 같은 버전을 쓰는 플레이어끼리만 가능합니다.
- **업데이트:** 새 릴리스가 나오면 메인 메뉴에 **업데이트 있음**이 표시됩니다. 2026.11.02 이후 빌드부터는 이 버튼이 게임을 그 자리에서
  업데이트하며 바뀐 부분만 내려받습니다(Windows, Linux, Android. UWP 앱은 Windows의 앱 설치 관리자로 업데이트됩니다).
  2026.11.02 이하 빌드는 마지막으로 한 번 전체를 다시 내려받아야 합니다.
- **세이브**는 게임 파일과 따로 보관되므로 업데이트하거나 다시 설치해도 남아 있습니다. **게임 로딩**에서 모든 세이브를 파일 하나로
  내보내고 가져올 수 있어 다른 PC나 휴대폰으로 옮길 수 있습니다.
- **UWP 버전**에는 Discord 상태 표시, 컨트롤러 자이로, 저장 파일 대화상자, VR, 전용 서버가 없습니다.

> [!TIP]
> 새로 설치하면 **원작 게임**과 똑같이 플레이됩니다. 옵션 > 게임플레이 > **게임 경험**에서 리메이크가 더한 요소를 한꺼번에 켤 수
> 있습니다. *오리지널*, *연출 강화*(격납고 비행과 움직이는 대화), *리메이크*(모든 추가 요소) 중에서 고르세요.
> 각 옵션을 하나씩 따로 바꿀 수도 있습니다.

### Linux와 Steam Deck

- **컨트롤러:** Xbox, PlayStation, Switch Pro 컨트롤러는 바로 작동합니다. 데스크톱 모드의 Steam Deck이나 게임이 인식하지 못하는
  컨트롤러라면 게임을 Steam에 추가(*Non-Steam 게임 추가*)한 뒤 거기서 실행하세요. 그러면 Steam Input이 Xbox 컨트롤러로
  인식시킵니다.
- **Steam Deck에서 Steam으로 실행할 때:** Steam이 `SteamDeck=1`을 설정하는데, 이 때문에 Unity Linux 플레이어가 약 0 FPS로 멈춥니다
  ([#232](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues/232)). 게임은 이 값 없이 스스로 한 번 다시
  시작합니다(창이 두 번 열릴 수 있습니다). 그래도 멈춘다면 바로가기의 실행 옵션을 `env -u SteamDeck %command%`로 설정하세요.

---

<a id="features"></a>

## ✨ 기능

- **스토리:** 프롤로그, Void, 엔딩까지 담은 전체 메인 캠페인과 Valkyrie, Supernova 확장팩. 음성 대사(영어 또는 독일어), 초상화,
  무전 대화, 컷신이 있습니다.
- **비행:** 원작의 비행 모델과 추적 카메라, 조종석 시점, 그리고 EVERSPACE 2 스타일의 선택형 자유 비행 모드. 정거장 궤도는 하늘, 행성,
  태양, 소행성대까지 원작과 똑같이 다시 만들었습니다.
- **이동:** 자동 조종, 행성 점프, 빨리 감기, 성계 지도, 점프게이트, Khador Drive, Void의 웜홀. **점프게이트 경로 계획**은 가장 빠른
  경로나 가장 안전한 경로로 어느 시스템이든 게이트를 하나씩 거쳐 데려다 줍니다. 선택 기능인 **Khador Slipstream**은 시스템 안과
  시스템 사이를 빠르게 이동하는 방법입니다.
- **전투:** 모든 무기(미사일, 빔, EMP, 기뢰, 터렛, 센트리 건, Liberator...), NPC 교통과 AI, 대형 함선, 해적 기지, Specter, 일급 현상
  수배범. 클로킹, 비상 시스템, 시간 확장기, 수리 빔, 감마 실드. 콤보 점수와 최고 기록이 있는 **Supernova Challenge** 아케이드
  모드.
- **경제와 정거장:** 격납고, 상점과 함선 상인, 바 에이전트와 모든 종류의 프리랜서 미션이 있는 스페이스 라운지, 블루프린트, 윙맨,
  메달, 카아모 클럽.
- **채굴:** 드릴링 미니게임을 이용한 소행성 채굴, Mido의 **채굴 공장**(광석을 코어로 가공, 우주 쓰레기 분류), 가스 구름, 해킹과 오브젝트
  도킹.
- **협동 캠페인(실험적):** 스토리를 함께 플레이합니다. 기본 2~4명이며 메인 메뉴의 전용 버튼으로 시작합니다. 호스트는 싱글 플레이
  세이브를 이어 하거나 새 게임을 시작하고, 적은 플레이어 수에 따라 더 강해질 수 있습니다.
- **멀티플레이어(실험적):** 분대, 팩션, 아레나 대전, 거래, 이벤트 게임 모드가 있는 공유 우주. [멀티플레이어](#multiplayer)를
  참고하세요.
- **모드:** 새 아이템, 함선, 시스템, 정거장, 캐릭터, 퀘스트, 캠페인 전체를 게임 안의 **Mod Builder**로 만들 수 있습니다.
  [모드](#mods)를 참고하세요.
- **VR(실험적):** OpenXR을 통한 PC VR. [VR](#vr)을 참고하세요.
- **스피드런 타이머:** 스토리 챕터나 단계별 스플릿, LiveSplit 스플릿 파일과 LiveSplit 연동.
- **조작과 화면:** 터치, 기울이기, 키보드와 마우스, 컨트롤러(Xbox 또는 PlayStation 버튼을 자동으로 선택). 모두 키 설정을 바꿀 수 있습니다.
  Windows에서는 DualSense, DualShock 4, Switch Pro로 자이로 조종이 가능하고, 컨트롤러와 Android 휴대폰에서는 햅틱을 지원합니다.
  4:3부터 32:9까지의 가로 화면을 지원합니다.
- **언어:** 원작이 지원하던 언어에 더해 네덜란드어, 중국어 간체, 힌디어 번역이 있고 한국어, 일본어, 아랍어도 완성되어(총 14개)
  독일어 또는 영어 음성을 지원합니다.

<details>
<summary><b>리메이크 추가 요소</b> (대부분 옵션 > 게임플레이에 있는 옵션입니다)</summary>

- **격납고와 연출:** 격납고에서의 입항과 출항 비행, 부드러운 함선 그림자, 피사계 심도, **함선 살펴보기**(내 함선 주위를 도는 궤도
  카메라), 움직이는 대화 텍스트, 원작 스타일의 블룸.
- **대형 함선 강화:** 호위함, 더 강한 터렛, 미사일 일제 사격. 항공모함과 Vossk 전함은 파괴할 수 있고, 둘 사이에 드물게 함대전이
  벌어지며, 항공모함은 신뢰하는 파일럿에게 보급품을 팔기도 합니다.
- **해적 이벤트:** 위험도가 Risky와 Dangerous인 시스템의 궤도에는 해적 전초기지나 현상금과 위협 등급이 붙은 이름 있는 해적 두목이
  나타날 수 있으며, 미션 창의 해적 활동 게시판에 표시됩니다.
- **더 똑똑한 NPC 조준:** 적 전투기가 모두 나만 노리지 않고 가장 가까운 적대 함선을 공격합니다. **EMP는 클로킹을 해제**시키고,
  Disruptor는 설명대로 실드에 두 배의 피해를 줍니다.
- **실제 형태 기준 충돌:** 총알이 닿은 곳에 맞고, 정거장과 화물선 표면을 따라 미끄러집니다. *함선 충돌*: 실제 형태, 혼합(전투기는
  원작의 더 큰 히트 박스 유지), 오리지널.
- **미사일 경고.** 부스트나 클로킹으로 유도 미사일을 따돌릴 수 있습니다. 폭탄에는 기폭 거리가 있습니다.
- **새 게임+:** 클리어한 게임의 크레딧, 블루프린트, 메달, 함선을 가지고 다시 시작합니다.
- **새 게임 옵션:** 처음부터 카아모 클럽, **하드코어**(영구 사망), 메달 없는 게임, 튜토리얼 팝업, PC 경제와 Android 경제 중 선택.
- **네 가지 난이도**(쉬움, 중간, 어려움, 익스트림). 게임 도중에도 바꿀 수 있습니다.
- **편의 기능:** 모두 판매 / 모두 구매, 목표 고정 해제 조작이 있는 스마트 목표 고정, 미션 창의 지도에서 미션 목표로 이동, 성계 지도
  전체 보기, 짧은 토스트로 표시되는 메달, 전체 / 최소 / 끄기 비행 HUD(F1), 거리가 표시되는 먼 함선 마커, 쿨다운 아이콘, FPS 표시,
  무기마다 고유한 발사음.
- **그래픽:** 낮음 / 중간 / 높음 프리셋, 먼 함선과 정거장, 소행성의 LOD, SMAA / FXAA / TAA, 업스케일링(FSR 1과 STP는 어디서나,
  NVIDIA DLSS와 AMD FSR 2 / 3 / 4는 Windows).
- **사진 모드**(비행 중에는 Action Freeze, 격납고에서는 함선 살펴보기): 자유 카메라, 시야각, 기울기, 피사계 심도, 셔터 각도와 장노출,
  줌 블러, 필름 룩, 노출, 색상, 프레임, 화면 크기의 최대 4배까지 스크린샷.
- **세이브:** 원작의 Android나 PC 세이브 가져오기, 원작 커뮤니티 빌드용 세이브 내보내기, 원하는 세이브 폴더 지정(Syncthing 같은
  동기화 도구로 접근 가능).
- **디버그 도구**(옵션 > 게임플레이): 원하는 스토리 단계로 이동, 치트, 아이템 지급, 함선과 오브젝트 생성, 어떤 선체든 조종, 함선
  장비 구성 저장과 불러오기. 현재 스토리 단계와 정거장을 오른쪽 아래에 표시할 수 있어서, 버그 스크린샷에 어디서 일어났는지
  나타납니다.
- **Discord Rich Presence** (Windows).

</details>

---

<a id="multiplayer"></a>

## 🌐 멀티플레이어

멀티플레이어는 실험 단계입니다. 세션에 참여한 모두가 클리어한 게임의 세계에서 하나의 우주를 공유합니다. 우주와 격납고에서 서로를
보고, 분대를 만들고, 바 미션을 함께 하고, 상점과 NPC 교통을 공유하고, 정거장을 소유하는 팩션을 세우고, 크레딧과 화물을 거래하고,
아레나 대전(호스트가 PvP를 고르면 어디서든)에서 싸울 수 있습니다. 서버 브라우저(각 게임의 지역과 핑 표시), 참가 코드, 로컬 네트워크로
참여할 수 있습니다.

새 파일럿은 10 000 크레딧과 Betty를 가지고 Dis에 도킹한 상태로 시작합니다. 전용 서버(또는 호스트의 지속 월드)가 각 플레이어의 진행
상황을 보관하며, 싱글 플레이 세이브는 절대 건드리지 않습니다. **정확히 같은 게임 버전**끼리만 함께 플레이할 수 있습니다.

| 가이드 | 내용 |
|---|---|
| **[함께 플레이하기](../MULTIPLAYER.md)** | 참여와 호스팅, 채팅, 분대, 미션, 상점, 팩션, 정거장 소유, 아레나 대전, 관리자, 모든 채팅 명령어 |
| **[전용 서버 운영](../../SERVER.md)** | Windows / Linux 서버 다운로드와 Docker 이미지, 옵션, 콘솔, 오늘의 메시지, 관리자 되는 방법, 서버 파일 |
| **[멀티플레이어의 작동 방식](../MULTIPLAYER-DEV.md)** | 프로그래머를 위한 코드 설명 |

---

<a id="mods"></a>

## 🧩 모드

모드는 코드나 Unity 없이 데이터만으로 게임의 콘텐츠를 추가하거나 바꿉니다. 아이템(고유한 무기 효과와 사운드 포함), GLB 모델로 만든
함선, 스킨, 성계와 정거장(고유한 모델, 행성, 태양, 하늘, 격납고, 바 포함), 초상화와 음성이 있는 캐릭터, 음악과 효과음, 블루프린트,
퀘스트와 바 미션, 그리고 새 게임에 나타나는 캠페인 전체를 다룹니다.

**모드 설치:** 메인 메뉴의 **모드** 화면에서 **모드 가져오기**를 누르고 모드의 `.zip`을 고르세요(Android에서도 가능). 또는 모드(폴더 또는
`.zip`)를 직접 모드 폴더에 넣은 뒤 모드 화면에서 켜세요. 화면의 **모드 폴더 열기** 버튼으로 폴더를 열 수 있습니다.

| 플랫폼 | 모드 폴더 |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods` 또는 `GoF2Remake.exe` 옆의 `Mods` 폴더 |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods` 또는 게임 옆의 `Mods` 폴더 |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods` (USB로 접근 가능) |

모드를 켜고 만든 세이브는 그 모드를 기억합니다. 모드 없이 불러오면 모드 아이템이 제거됩니다(환불됩니다). 멀티플레이어에서는 호스트가
모드 허용 여부를 정하며, 참여하는 플레이어도 같은 모드가 필요합니다.

**모드 만들기:**

- **[Mod Builder](../notes/mod-builder.md)** (모드 > Mod Builder, 실험적): 함선, 아이템, 스킨, 캐릭터, 퀘스트(노드 그래프),
  시스템, 정거장, 격납고와 바, 무기 효과, 오디오, 블루프린트, 캠페인, 함선 키트를 게임 안에서 만들고, 실행 취소, 시험 비행,
  플레이 테스트를 지원하며 완성된 모드를 내보냅니다. 모드의 모델과 텍스처를 줄여 주고, 원작 모델로 만든 커뮤니티 모드(`.aem` /
  `.aei`)도 가져옵니다. 직접 쓰는 AI 어시스턴트가 로컬 MCP 서버를 통해, 원하는 권한만 주고 이 안에서 작업하게 할 수도 있습니다.
- **직접 만들기:** 모더용 가이드 [Modding/README.md](../../Modding/README.md)와 예제 모드
  [Modding/Examples](../../Modding/Examples).
- **AI 어시스턴트와 함께:** [Modding/ai](../../Modding/ai)에 Claude, ChatGPT 등을 위한 가이드가 있습니다.

---

<a id="controls"></a>

## 🕹️ 조작

<details>
<summary><b>키보드와 마우스</b></summary>

| 키 | 동작 |
|---|---|
| 방향키 | 조종 |
| W | 부스트 |
| S | 브레이크(누르고 있는 동안 엔진이 멈춤) |
| `]` / `/` 또는 마우스 휠 | 스로틀 |
| Space / 마우스 왼쪽 | 주무기 |
| R / 마우스 오른쪽 | 보조 무기 |
| G | 보조 무기 전환 |
| A / D | 좌 / 우 스트레이프 |
| 1 / 3 | 좌 / 우 롤 |
| 2 | 수평 맞추기 |
| F / Enter | 동작(도킹, 자동 조종, 채굴, 점프) |
| Q | 자동 조종 메뉴 |
| E | 동작 메뉴(보조 무기, 윙맨, 클로킹, Khador Drive) |
| Tab | 빨리 감기 |
| T | 카메라: 추적, 조종석, 터렛 시점, 자유 시점 |
| V / K / C / X | 윙맨 / Khador Drive / 클로킹 / 시간 확장기 |
| M 또는 마우스 가운데 버튼 | 마우스 조종 전환 |
| U | 목표 고정 해제 |
| B / N | 채팅 / 멀티플레이어 창(멀티플레이어) |
| F1 | 비행 HUD: 전체, 최소, 끄기 |
| F12 | 스크린샷 |
| Esc | 일시 정지 |

</details>

비행 조작은 모두 옵션 > 키 설정에서 바꿀 수 있습니다(각각 키보드 / 마우스 키 두 개와 컨트롤러 버튼 하나). 컨트롤러와 터치도 완전히
지원하며, 게임 안의 힌트에는 마지막으로 사용한 입력 장치의 버튼이 표시됩니다.

<a id="vr"></a>

### VR (실험적)

PC VR 헤드셋을 연결하고 OpenXR 런타임(SteamVR, Meta Quest Link, Windows Mixed Reality)이 켜져 있는 상태에서 Windows 빌드를 `-vr`로
시작하세요(예: `GoF2Remake.exe -vr`로 가는 바로가기). 헤드셋이 없으면 게임은 평소처럼 시작합니다.

<details>
<summary>VR은 이런 모습입니다</summary>

- **비행:** 조종석에 앉습니다. 실드, 선체, 화물은 조종석 디스플레이에, 레이더는 계기판 가운데에, 나머지 HUD는 캐노피에 표시됩니다.
  컷신에서는 수평선이 유지되고 장면 사이는 페이드로 전환됩니다.
- **정거장:** 격납고에서는 내 함선 옆에 서고(그립으로 잡고 당겨서 돌릴 수 있습니다), 바에서는 손님을 가리키고 트리거를 당겨 말을 겁니다.
- **메뉴**는 떠 있는 화면에 나타납니다. 오른쪽 컨트롤러로 가리키고 트리거를 당깁니다.
- **조작:** 컨트롤러는 게임패드처럼 작동합니다(스틱, 트리거, A / B / X / Y, 그립은 LB / RB). 옵션 > 콘트롤 > "VR 비행: 조종간과
  스로틀 잡기"를 켜면 대신 조종석의 사이드 스틱과 스로틀 레버로 비행합니다.

조종석은 아직 모든 함선이 같습니다.

</details>

---

<a id="help"></a>

## 🛠️ 프로젝트 참여

이 섹션은 게임 자체(코드, 씬, 에셋)를 바꾸거나 직접 빌드하려는 분을 위한 것입니다. **게임만 하려면 [릴리스](#play)를 쓰세요. 콘텐츠를
추가하는 데는 보통 [모드](#mods)로 충분합니다.**

### 요구 사항

- **Unity 7000.0.0a12**(알파 버전, Unity Hub로 설치). 빌드할 플랫폼의 빌드 지원 모듈을 추가하세요: *Windows Build Support (IL2CPP)*,
  *Linux Build Support (IL2CPP)*, *Android Build Support*, 서버 빌드에는 *Dedicated Server* 모듈.
- **Git**과 **[Git LFS](https://git-lfs.com)**. 모델, 텍스처, 사운드는 LFS에 약 2.1 GB이며, 프로젝트 자체의 LFS 서버에서 받아옵니다
  ([.lfsconfig](../../.lfsconfig)에 설정되어 있습니다).
- **Windows 빌드:** *C++를 사용한 데스크톱 개발* 워크로드가 있는 Visual Studio 2022 이상과 Windows 10 / 11 SDK(IL2CPP). UWP 빌드에는
  *C++ 유니버설 Windows 플랫폼 도구*도 필요합니다.

### 프로젝트 실행하기

```bash
git lfs install
```

```bash
git clone https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake.git
```

1. Unity Hub에서 클론한 폴더를 Unity 7000.0.0a12로 엽니다(*Add > Add project from disk*). 처음 임포트는 시간이 걸립니다.
2. `Assets/Scenes/Splash.unity`(로고를 건너뛰려면 `MainMenu.unity`)를 열고 **Play**를 누릅니다.

씬은 `Splash`(빌드 인덱스 0), `MainMenu`, `Space`(비행 레벨), `Station`(도킹 상태)입니다. 생성된 에셋(프리팹, 머티리얼, 아이콘, HUD 이미지)은
이미 저장소에 들어 있으며, 에디터의 **GoF2** 메뉴로 다시 만들 수 있고 **GoF2 > Tools Overview**에 모든 도구가 나열되어 있습니다.

> [!IMPORTANT]
> 씬의 라이팅을 생성하지 마세요. 씬에는 의도적으로 베이크된 라이팅이 없습니다. 게임이 런타임에 레벨을 만들고 시스템마다 고유한 하늘이
> 있기 때문입니다(`SkyReflection`이 그 하늘로 환경광을 갱신합니다). 베이크된 프로브는 모든 시스템에 하나의 고정된 하늘을 쓰게 됩니다.

### 빌드

- 다른 플랫폼을 빌드하기 전에는 항상 **활성 빌드 프로필을 전환**하세요(File > Build Profiles > Switch Profile). URP는 활성 플랫폼의 셰이더
  베리언트만 유지합니다. Windows가 활성인 상태에서 만든 Android 빌드는 검게 렌더링되므로, 에디터 스크립트 `BuildTargetGuard`가
  이를 막습니다.
- `BuildVersionStamp`는 git 커밋의 날짜와 시각으로 빌드마다 버전을 붙입니다. 릴리스에서는 모든 플랫폼이 같은 버전을 갖도록 에디터
  프로세스의 `GOF2_BUILD_VERSION` 환경 변수를 설정하세요.
- 전용 서버: **GoF2 > Build > Linux Dedicated Server** / **Windows Dedicated Server**. `docker/` 폴더에 서버 이미지가 있습니다.
- 게임 내 업데이터용 업데이트 패치: 빌드를 마친 뒤 **GoF2 > Build > Update Patch**.

### 테스트

약 3000개의 에디트 모드 테스트가 규칙과 데이터를 검증하며, 씬, 스토리 레벨, 호스팅된 세션에 대한 플레이 모드 스모크 테스트도
있습니다. Window > General > Test Runner에서 실행하거나, `unity` CLI를 연결한 상태에서 다음처럼 실행합니다.

```bash
python -I Reference/tools/tests/run_tests.py
```

[Assets/Scripts/Tests/README.md](../../Assets/Scripts/Tests/README.md)를 참고하세요.

### 프로젝트 구조

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

### 문서

- [CLAUDE.md](../../CLAUDE.md): 프로젝트 전반의 규칙(컨벤션, 좌표, 데이터)과 기술 노트 색인.
- [docs/notes/](../notes): 게임 영역별 파일 하나씩(비행, 전투, 스토리, 상점, 멀티플레이어, 모드...). 각 시스템의 바탕이 된 원작
  함수와 리메이크가 내린 선택이 담겨 있습니다.
- [Assets/Scripts/Runtime/README.md](../../Assets/Scripts/Runtime/README.md): 어셈블리 계층과 그 훅.
- [Reference/README.md](../../Reference/README.md): 디컴파일한 코드와 그것을 읽는 도구.

### 기여하기

- **버그를 찾았나요?** 플랫폼, 게임 버전(메인 메뉴에 표시), 스크린샷과 함께 [이슈](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues)를
  열어 주세요. 옵션 > 게임플레이에서 현재 스토리 단계와 정거장을 오른쪽 아래에 표시할 수 있어서, 어디서 일어났는지 알 수 있습니다.
- **풀 리퀘스트를 환영합니다.** 저장소를 포크하고, 브랜치에서 변경한 뒤, 에디트 모드 테스트를 실행하고, `main`에 풀 리퀘스트를 여세요.
  어떤 영역을 작업하기 전에는 [docs/notes/](../notes)의 해당 파일을 읽어 주세요. 컨벤션은 [CLAUDE.md](../../CLAUDE.md)에 있습니다.
- **프로젝트가 마음에 드나요?** GitHub에서 ⭐를 눌러 주세요. 다른 사람들이 프로젝트를 찾는 데 도움이 됩니다.

---

<a id="credits"></a>

## 🙏 크레딧

**Galaxy on Fire 2 Remake**는 JoppieToppie가 만들었고, KiritoJPK, DuIslingr, Zippadelic, FantasyToLife 등의 풀 리퀘스트가 함께했습니다.

이 리메이크는 **KiritoJPK의 FULL HD 버전과 수정본**을 바탕으로 하며, Galaxy on Fire 2™와 4PDA 커뮤니티 덕분입니다:
<https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

폰트: 원작 게임의 인터페이스 서체와 Inter(SIL Open Font License). 컨트롤러 자이로:
Julian Smart의 [JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary)(MIT License). Unity, Universal Render Pipeline,
Netcode for GameObjects, OpenXR로 만들었습니다.

### 기여자

<a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors">
  <img alt="Contributors" src="https://contrib.rocks/image?repo=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake">
</a>

## ⭐ 스타 히스토리

<a href="https://star-history.com/#JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&Date">
  <img alt="Star history chart" src="https://api.star-history.com/svg?repos=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&type=Date">
</a>

## ⚖️ 법적 고지

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ and ABYSS®
are registered trademarks of FISHLABS Entertainment GmbH. All rights reserved. 이 프로젝트는 비공식 팬 프로젝트이며 FISHLABS나
Deep Silver와 제휴하거나 그들의 승인을 받은 것이 아닙니다.

리메이크 자체의 코드는 [Apache License 2.0](../../LICENSE.md)에 따라 라이선스가 부여됩니다.
