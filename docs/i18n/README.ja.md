![Galaxy on Fire 2 Remake メインメニュー](../../.github/header.webp)

<h1 align="center">Galaxy on Fire 2 Remake</h1>

<p align="center">
  FISHLABS による2010年の宇宙ゲーム <i>Galaxy on Fire 2</i> を Unity で作り直した、ファンメイドのリメイクです。<br>
  飛行、戦闘、交易、採掘、ステーション、バー、そして物語のすべてを、オリジナルに忠実に再現しています。
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
  <a href="README.ko.md">한국어</a> ·
  <b>日本語</b> ·
  <a href="README.ar.md">العربية</a>
</p>

<p align="center"><i>これは翻訳です。基準となるのは <a href="../../README.md">English README</a> で、そちらのほうが新しい場合があります。</i></p>

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
  <a href="#play"><b>ゲームを遊ぶ</b></a> ·
  <a href="#features">機能</a> ·
  <a href="#multiplayer">マルチプレイヤー</a> ·
  <a href="#mods">MOD</a> ·
  <a href="#controls">操作</a> ·
  <a href="#help"><b>プロジェクトに協力する</b></a> ·
  <a href="#credits">クレジット</a>
</p>

---

このリメイクは、オリジナルゲームのアセットと、逆コンパイルしたオリジナルのコードから移植したゲームロジックの上に作られています。

> [!NOTE]
> **現状：** メインキャンペーンと2つのアドオン **Valkyrie**・**Supernova** は最初から最後まで遊べます。アーケードモードの
> Supernova Challenge も同様です。マルチプレイヤー、協力キャンペーン、Mod Builder は実験的な機能です。VR は今のところ
> ヘッドセットなしでしかテストされていません。

## どのセクションを読めばいい？

| やりたいこと | 移動先 |
|---|---|
| PC やスマホで**ゲームを遊ぶ** | [ゲームを遊ぶ](#play)：リリースをダウンロードするだけ、Unity は不要 |
| 友達と遊ぶ、サーバーを立てる | [マルチプレイヤー](#multiplayer) |
| 船・アイテム・星系・クエストを追加する | [MOD](#mods)：ゲーム内で作るか、データファイルで作成。Unity は不要 |
| **ゲームのコードやアセットを変更する**、プルリクエストを送る | [プロジェクトに協力する](#help)：リポジトリをクローンして Unity で開く |

---

<a id="play"></a>

## 🎮 ゲームを遊ぶ

遊ぶのに Unity、Git、このリポジトリは必要ありません。お使いのプラットフォーム用のビルドを
**[最新リリース](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/releases/latest)** からダウンロードしてください。

| プラットフォーム | ダウンロード | インストール方法 |
|---|---|---|
| **Windows** 10 / 11（64ビット） | `GoF2Remake-Windows-<version>.zip` | 展開して、`GoF2Remake` フォルダ内の `GoF2Remake.exe` を実行 |
| **Linux**（x86_64） | `GoF2Remake-Linux-<version>.zip` | 展開して `GoF2Remake.x86_64` を実行（起動しない場合：`chmod +x GoF2Remake.x86_64`） |
| **Android**（ARM64） | `GoF2Remake-Android-<version>.apk` | APK をスマホにコピーして開く。確認が出たら、この提供元からのインストールを許可 |
| **Windows（UWPアプリ）** | `GoF2Remake-UWP-<version>.zip` | 展開して、`Install.ps1` を右クリック > **PowerShell で実行**（パッケージのテスト証明書を信頼するか尋ねられます） |
| 専用サーバー | `GoF2Remake-WindowsServer-…` / `-LinuxServer-…` | [SERVER.md](../../SERVER.md) を参照。Docker イメージ `ghcr.io/joppietoppie/gof2-server` もあります |

- **バージョン：** どのビルドのバージョンも、リリースの日付と時刻です（例：`2026.11.02.0622`）。全プラットフォーム共通で、
  メインメニューに表示されます。マルチプレイヤーは同じバージョンのプレイヤー同士でしか動きません。
- **アップデート：** 新しいリリースが出ると、メインメニューに**アップデートがあります**と表示されます。2026.11.02 より後の
  ビルドでは、このボタンでゲームがその場で更新され、変更点だけがダウンロードされます（Windows、Linux、Android。UWP アプリは
  Windows のアプリインストーラー経由で更新）。2026.11.02 以前のビルドは、最後にもう一度フルダウンロードが必要です。
- **セーブ**はゲームファイルとは別に保存されるので、アップデートや再インストールをしても残ります。**ロード**画面で全セーブを
  1つのファイルとしてエクスポート／インポートすれば、別の PC やスマホへ移せます。
- **UWP 版**には、Discord ステータス、コントローラーのジャイロ、セーブのファイルダイアログ、VR、専用サーバーがありません。

> [!TIP]
> 新規インストールは**オリジナルのゲーム**と同じように遊べます。オプション > ゲームプレイ > **ゲーム体験**で、リメイクの追加要素を
> まとめてオンにできます：*オリジナル*、*コスメティック*（格納庫での飛行とアニメーションする会話）、*リメイク*（すべての追加要素）。
> 各オプションは個別に変更することもできます。

### Linux と Steam Deck

- **コントローラー：** Xbox、PlayStation、Switch Pro のコントローラーはそのまま使えます。デスクトップモードの Steam Deck や、
  ゲームが認識しないコントローラーの場合は、ゲームを Steam に追加（*Add a Non-Steam Game*）してそこから起動してください。
  Steam Input が Xbox コントローラーとして扱ってくれます。
- **Steam Deck で Steam から起動した場合：** Steam が `SteamDeck=1` を設定するため、Unity の Linux プレイヤーが約 0 FPS で
  止まってしまいます（[#232](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues/232)）。ゲームはそれを外して
  自動で1回再起動します（ウィンドウが2回開くことがあります）。それでも止まる場合は、ショートカットの起動オプションに
  `env -u SteamDeck %command%` を設定してください。

---

<a id="features"></a>

## ✨ 機能

- **ストーリー：** プロローグ、ヴォイド、エンディングまで含むメインキャンペーン全編と、Valkyrie・Supernova のアドオン。
  ボイス付きの会話（英語またはドイツ語）、ポートレート、無線のやり取り、カットシーン。
- **飛行：** オリジナルの飛行モデルとチェイスカメラ、コックピット視点、そして EVERSPACE 2 風のオプションのフリーフライトモード。
  ステーション軌道は、空、惑星、太陽、小惑星帯まで含めてオリジナルどおりに再現しています。
- **移動：** 自動操縦、惑星ジャンプ、早送り、スターマップ、ジャンプゲート、カドーアドライブ、ヴォイドのワームホール。
  **ジャンプゲートのルート計画**は、最速または最も安全なルートで、ゲートを一つずつ経由してどの星系へでも飛んでくれます。
  オプションの **Khador Slipstream** は、星系内と星系間の高速移動です。
- **戦闘：** あらゆる武器（ミサイル、ビーム、EMP、機雷、タレット、セントリーガン、リベレーターなど）、NPC の交通と AI、
  主力艦、海賊基地、スペクター、重要指名手配犯。クローク、緊急システム、時間延長機、修理ビーム、ガンマシールド。
  コンボスコアとハイスコア付きのアーケードモード **Supernova Challenge**。
- **経済とステーション：** 格納庫、ショップと船のディーラー、バーのエージェントがいる宇宙ラウンジとあらゆる種類のフリーランス
  ミッション、設計図、操縦士、メダル、カーモクラブ。
- **採掘：** 掘削ミニゲームによる小惑星の採掘、Mido の**採掘プラント**（鉱石からコアへ、宇宙ゴミの仕分け）、ガス雲、
  ハッキング、オブジェクトへのドッキング。
- **協力キャンペーン（実験的）：** メインメニュー専用のボタンから、ストーリーを一緒にプレイ。デフォルトで2〜4人です。
  ホストは1人用のセーブを続けるか、新しいゲームを始めます。敵はプレイヤーごとに強くすることもできます。
- **マルチプレイヤー（実験的）：** 部隊、派閥、アリーナ戦、交易、イベントゲームモードのある共有ユニバース。
  [マルチプレイヤー](#multiplayer)を参照。
- **MOD：** 新しいアイテム、船、星系、ステーション、キャラクター、クエスト、そしてキャンペーン丸ごとを、ゲーム内の
  **Mod Builder** で作成。[MOD](#mods)を参照。
- **VR（実験的）：** OpenXR による PC VR。[VR](#vr)を参照。
- **スピードランタイマー：** ストーリーの章やステップごとのスプリット、LiveSplit のスプリットファイル、LiveSplit 連携。
- **操作と画面：** タッチ、傾き、キーボードとマウス、コントローラー（Xbox または PlayStation のボタン、自動で切り替え）に
  対応し、すべて割り当て変更が可能。Windows では DualSense、DualShock 4、Switch Pro によるジャイロ操縦に対応。
  コントローラーと Android スマホでは振動フィードバックもあります。4:3 から 32:9 までの横長画面に対応。
- **言語：** オリジナルの言語に加え、オランダ語、簡体字中国語、ヒンディー語の翻訳があり、韓国語、日本語、アラビア語も
  完成しました（全14言語）。音声はドイツ語または英語です。

<details>
<summary><b>リメイクの追加要素</b>（ほとんどはオプション > ゲームプレイで設定できます）</summary>

- **格納庫と演出：** 格納庫での到着・出発の飛行、やわらかな船の影、被写界深度、**船を眺める**（自分の船の周りを回るカメラ）、
  アニメーションする会話テキスト、オリジナル風のブルーム。
- **主力艦の強化：** 護衛、強力になったタレット、ミサイルの一斉射撃。空母とヴォスク戦艦は破壊でき、両者の間でまれに艦隊戦が
  起こり、空母は信頼したパイロットに補給品を売ってくれます。
- **海賊イベント：** 危険度が「危険」や「非常に危険」の星系では、軌道に海賊の前哨基地や、賞金と脅威ランクが付いた名のある
  海賊のボスが現れることがあります。ミッション画面の**海賊の動き**ボードに一覧表示されます。
- **賢くなった NPC のターゲット選び：** 敵の戦闘機は全員があなたを狙うのではなく、最も近い敵対艦を攻撃します。
  **EMP でクロークが解除**され、ディスラプターは説明どおりシールドに2倍のダメージを与えます。
- **実際の形状での当たり判定：** 弾は触れたものに当たり、ステーションや貨物船には沿って滑ります。*艦船の当たり判定*：
  実形状、混合（戦闘機はオリジナルの大きめの当たり判定を維持）、オリジナル。
- **ミサイル警告**。ブーストやクロークで追尾ミサイルを振り切れます。爆弾には起爆距離があります。
- **強くてニューゲーム：** クリア済みゲームのクレジット、設計図、メダル、船を引き継いで最初からやり直せます。
- **ニューゲームのオプション：** 最初からカーモクラブ、**ハードコア**（パーマデス）、メダルなしのゲーム、チュートリアルの
  ポップアップ、PC 版と Android 版の経済の選択。
- **4つの難易度**（簡単、普通、困難、極度）。ゲーム中に変更できます。
- **使いやすさ：** すべて売却／すべて購入、ロック解除の操作付きのスマートなターゲットロック、ミッション画面のマップから
  ミッションの目標へ移動、全体表示のスターマップ、短いトーストで出るメダル、フル／最小／オフを選べる飛行 HUD（F1）、
  距離つきの遠くの船マーカー、クールダウンアイコン、FPS カウンター、武器ごとの専用の発射音。
- **グラフィックス：** 低／中／高のプリセット、遠くの船・ステーション・小惑星のレベルオブディテール、SMAA / FXAA / TAA、
  アップスケーリング（FSR 1 と STP は全環境、NVIDIA DLSS と AMD FSR 2 / 3 / 4 は Windows）。
- **フォトモード**（飛行中の Action Freeze、格納庫の船を眺める）：フリーカメラ、視野角、傾き、被写界深度、シャッター角と
  長時間露光、ズームブラー、フィルム風の色調、露出、色、フレーム、画面サイズの最大4倍までのスクリーンショット。
- **セーブ：** オリジナルゲームの Android 版や PC 版のセーブのインポート、オリジナルのコミュニティビルド向けのセーブの
  エクスポート、好きな場所へのセーブフォルダ指定（Syncthing などの同期ツールから参照可能）。
- **デバッグツール**（オプション > ゲームプレイ）：任意のストーリーステップへのジャンプ、チート、アイテムの付与、船や
  オブジェクトの出現、任意の船体での飛行、船の装備構成の保存と読み込み。現在のストーリーステップとステーションを右下に
  表示できるので、バグ報告のスクリーンショットで発生場所がわかります。
- Windows の **Discord Rich Presence**。

</details>

---

<a id="multiplayer"></a>

## 🌐 マルチプレイヤー

マルチプレイヤーは実験的な機能です。セッションの全員が、クリア後のゲーム世界の同じユニバースを共有します。宇宙でも格納庫でも
お互いの姿が見え、部隊を組み、バーのミッションを一緒にこなし、ショップや NPC の交通を共有し、ステーションを所有する派閥を
作り、クレジットや貨物を取引し、アリーナ戦で戦えます（ホストが PvP を選べば、どこでも戦えます）。参加はサーバーブラウザ
（各ゲームの地域と ping つき）、参加コード、またはローカルネットワークから行えます。

新しいパイロットは、ベティ号に乗り、10 000 クレジットを持った状態で Dis にドッキングして始まります。専用サーバー（またはホストの
永続ワールド）が各プレイヤーの進行状況を保存します。1人用のセーブには一切触れません。一緒に遊べるのは**まったく同じゲーム
バージョン**だけです。

| ガイド | 内容 |
|---|---|
| **[一緒に遊ぶ](../MULTIPLAYER.md)** | 参加とホスト、チャット、部隊、ミッション、ショップ、派閥、ステーションの所有、アリーナ戦、管理者、すべてのチャットコマンド |
| **[専用サーバーを動かす](../../SERVER.md)** | Windows / Linux のサーバーのダウンロードと Docker イメージ、オプション、コンソール、今日のお知らせ、管理者になる方法、ファイル構成 |
| **[マルチプレイヤーのしくみ](../MULTIPLAYER-DEV.md)** | プログラマー向けのコード解説 |

---

<a id="mods"></a>

## 🧩 MOD

MOD は、コードも Unity も使わず、データとしてゲームの内容を追加・変更します：アイテム（独自の武器エフェクトとサウンド付き）、
GLB モデルから作る船、スキン、星系とステーション（独自のモデル、惑星、太陽、空、格納庫、バー付き）、ポートレートとボイス付きの
キャラクター、音楽と効果音、設計図、クエストとバーのミッション、そしてニューゲームに表示されるキャンペーン丸ごとです。

**MOD のインストール：** メインメニューの **MOD** 画面で **MODをインポート**を押し、MOD の `.zip` を選びます（Android も同様）。
または MOD（フォルダまたはその `.zip`）を自分で Mods フォルダに置いてから、MOD 画面でオンにします。画面の
**MODフォルダを開く**ボタンでそのフォルダが開きます。

| プラットフォーム | Mods フォルダ |
|---|---|
| Windows | `%USERPROFILE%\AppData\LocalLow\JoppieToppie\Galaxy on Fire 2\Mods`、または `GoF2Remake.exe` と同じ場所の `Mods` フォルダ |
| Linux | `~/.config/unity3d/JoppieToppie/Galaxy on Fire 2/Mods`、またはゲームと同じ場所の `Mods` フォルダ |
| Android | `Android/data/com.joppietoppie.gof2remake/files/Mods`（USB 経由でアクセス可能） |

MOD を使って作ったセーブはそれを記憶していて、MOD なしでロードするとそのアイテムは削除されます（返金されます）。マルチプレイヤーでは、
MOD を許可するかどうかはホストが決め、参加するプレイヤーも同じ MOD が必要です。

**MOD の作り方：**

- **[Mod Builder](../notes/mod-builder.md)**（MOD > Mod Builder、実験的）：船、アイテム、スキン、キャラクター、クエスト（ノード
  グラフ）、星系、ステーション、格納庫とバー、武器エフェクト、オーディオ、設計図、キャンペーン、船のキットをゲーム内で作成でき、
  元に戻す機能、テスト飛行、プレイテストを備え、完成した MOD を書き出せます。MOD のモデルとテクスチャの縮小や、オリジナルの
  モデルから作られたコミュニティ MOD（`.aem` / `.aei`）のインポートもできます。お使いの AI アシスタントも、ローカルの MCP
  サーバー経由で、好きな権限を与えて作業できます。
- **手作業で：** モッダー向けガイド [Modding/README.md](../../Modding/README.md) と、[Modding/Examples](../../Modding/Examples) の
  サンプル MOD。
- **AI アシスタントで：** [Modding/ai](../../Modding/ai) に、Claude、ChatGPT などのためのガイドがあります。

---

<a id="controls"></a>

## 🕹️ 操作

<details>
<summary><b>キーボードとマウス</b></summary>

| キー | 操作 |
|---|---|
| 矢印キー | 操縦 |
| W | ブースト |
| S | ブレーキ（押している間エンジン停止） |
| `]` / `/` またはマウスホイール | スロットル |
| Space / 左クリック | 主武器 |
| R / 右クリック | 副武器 |
| G | 副武器の切り替え |
| A / D | 左／右へ平行移動 |
| 1 / 3 | 左／右へロール |
| 2 | 水平に戻す |
| F / Enter | アクション（ドッキング、自動操縦、採掘、ジャンプ） |
| Q | 自動操縦メニュー |
| E | アクションメニュー（副武器、操縦士、クローク、カドーアドライブ） |
| Tab | 早送り |
| T | カメラ：チェイス、コックピット、タレット視点、フリールック |
| V / K / C / X | 操縦士 / カドーアドライブ / クローク / 時間延長機 |
| M またはマウス中ボタン | マウス操縦の切り替え |
| U | ロック解除 |
| B / N | チャット / マルチプレイヤーウィンドウ（マルチプレイヤー） |
| F1 | 飛行 HUD：フル、最小、オフ |
| F12 | スクリーンショット |
| Esc | ポーズ |

</details>

飛行の操作はすべて、オプション > キー設定で割り当て変更できます（キーボード／マウスのキー2つとコントローラーのボタン1つ）。
コントローラーとタッチ操作にも完全に対応していて、ゲーム内のヒントには、最後に使った入力のボタンが表示されます。

<a id="vr"></a>

### VR（実験的）

PC VR ヘッドセットを接続し、OpenXR ランタイム（SteamVR、Meta Quest Link、Windows Mixed Reality）を動かした状態で、
Windows ビルドを `-vr` 付きで起動します（例：`GoF2Remake.exe -vr` へのショートカット）。ヘッドセットがなければ、ゲームは
普通に起動します。

<details>
<summary>VR ではこうなります</summary>

- **飛行：** コックピットに座ります。シールド、船体、貨物はコックピットのディスプレイに、レーダーはダッシュボードの中央に、
  残りの HUD はキャノピーに表示されます。カットシーンでは水平を保ち、ショット間はフェードで切り替わります。
- **ステーション：** 格納庫では自分の船の横に立ち（グリップでつかんで引くと船が回ります）、バーでは訪問者を指してトリガーを
  引くと話しかけられます。
- **メニュー**は空中に浮かぶ画面に表示されます。右のコントローラーで指して、トリガーを引きます。
- **操作：** コントローラーはゲームパッドとして働きます（スティック、トリガー、A / B / X / Y、グリップは LB / RB）。
  オプション > コントロール > 「VR飛行：操縦桿とスロットルをつかむ」をオンにすると、代わりにコックピットのサイドスティックと
  スロットルレバーで飛ばせます。

コックピットは今のところ、どの船でも同じです。

</details>

---

<a id="help"></a>

## 🛠️ プロジェクトに協力する

このセクションは、ゲーム本体（コード、シーン、アセット）を変更したい方や、自分でビルドしたい方向けです。**遊ぶだけなら
[リリース](#play)を、コンテンツを追加するだけなら通常は [MOD](#mods) で十分です。**

### 必要なもの

- **Unity 7000.0.0a12**（アルファ版。Unity Hub からインストール）。ビルドするプラットフォーム用のビルドサポートモジュールを
  追加してください：*Windows Build Support (IL2CPP)*、*Linux Build Support (IL2CPP)*、*Android Build Support*、サーバービルド用の
  *Dedicated Server* モジュール。
- **Git** と **[Git LFS](https://git-lfs.com)**。モデル、テクスチャ、サウンドは LFS 上で約 2.1 GB あり、プロジェクト専用の LFS
  サーバーから取得されます（[.lfsconfig](../../.lfsconfig) で設定）。
- **Windows ビルド**には：Visual Studio 2022 以降（*C++ によるデスクトップ開発*ワークロード）と Windows 10 / 11 SDK（IL2CPP）。
  UWP ビルドにはさらに *C++ ユニバーサル Windows プラットフォーム ツール*が必要です。

### プロジェクトを動かす

```bash
git lfs install
```

```bash
git clone https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake.git
```

1. クローンしたフォルダを Unity 7000.0.0a12 で Unity Hub から開きます（*Add > Add project from disk*）。最初のインポートには
   時間がかかります。
2. `Assets/Scenes/Splash.unity`（ロゴを飛ばすなら `MainMenu.unity`）を開いて **Play** を押します。

シーンは `Splash`（ビルドインデックス 0）、`MainMenu`、`Space`（飛行レベル）、`Station`（ドッキング中）です。生成されるアセット
（プレハブ、マテリアル、アイコン、HUD 画像）はすでにリポジトリに入っています。エディターの **GoF2** メニューで再生成でき、
**GoF2 > Tools Overview** にすべてのツールが一覧されています。

> [!IMPORTANT]
> シーンのライティングは生成しないでください。意図的にベイクしていません：ゲームはレベルを実行時に構築し、各星系が独自の空を
> 持っています（`SkyReflection` がそこから環境光を更新します）。ベイクしたプローブでは、全星系が同じ固定の空になってしまいます。

### ビルド

- 別のプラットフォームをビルドする前には、必ず**アクティブなビルドプロファイルを切り替えて**ください（File > Build Profiles >
  Switch Profile）。URP はシェーダーバリアントをアクティブなプラットフォーム分しか保持しません。Windows がアクティブなまま作った
  Android ビルドは真っ黒に描画されるため、エディタースクリプトの `BuildTargetGuard` がそれを拒否します。
- `BuildVersionStamp` は、git コミットの日付と時刻から各ビルドのバージョンを決めます。リリースでは、全プラットフォームが同じ
  バージョンになるよう、Editor プロセスの環境変数 `GOF2_BUILD_VERSION` を設定してください。
- 専用サーバー：**GoF2 > Build > Linux Dedicated Server** / **Windows Dedicated Server**。`docker/` フォルダにサーバーの
  イメージがあります。
- ゲーム内アップデーター用のアップデートパッチ：ビルド後に **GoF2 > Build > Update Patch**。

### テスト

約3000件のエディットモードテストがルールとデータをカバーし、さらにシーン、ストーリーレベル、ホストしたセッションのプレイモード
スモークテストがあります。Window > General > Test Runner から、または `unity` CLI を接続して実行します：

```bash
python -I Reference/tools/tests/run_tests.py
```

[Assets/Scripts/Tests/README.md](../../Assets/Scripts/Tests/README.md) を参照してください。

### プロジェクト構成

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

### ドキュメント

- [CLAUDE.md](../../CLAUDE.md)：プロジェクト全体のルール（規約、座標、データ）と、技術ノートの索引。
- [docs/notes/](../notes)：ゲームの分野ごとに1ファイル（飛行、戦闘、ストーリー、ショップ、マルチプレイヤー、MOD など）。各
  システムの元になったオリジナルの関数と、リメイクでの判断を記しています。
- [Assets/Scripts/Runtime/README.md](../../Assets/Scripts/Runtime/README.md)：アセンブリの層とそのフック。
- [Reference/README.md](../../Reference/README.md)：逆コンパイルしたコードと、それを読むツール。

### コントリビュート

- **バグを見つけたら？** プラットフォーム、ゲームのバージョン（メインメニューに表示）、スクリーンショットを添えて
  [issue](https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/issues) を立ててください。オプション > ゲームプレイで
  現在のストーリーステップとステーションを右下に表示できるので、発生場所がわかります。
- **プルリクエスト歓迎です。** リポジトリをフォークし、ブランチで変更を加え、エディットモードテストを実行して、`main` に対して
  プルリクエストを開いてください。ある分野に取り組む前に、[docs/notes/](../notes) のその分野のファイルを読んでください。規約は
  [CLAUDE.md](../../CLAUDE.md) にあります。
- **気に入ったら？** GitHub で ⭐ をお願いします。ほかの人がプロジェクトを見つけやすくなります。

---

<a id="credits"></a>

## 🙏 クレジット

**Galaxy on Fire 2 Remake** 制作：JoppieToppie。プルリクエストの提供：KiritoJPK、DuIslingr、Zippadelic、FantasyToLife ほか。

このリメイクは、**KiritoJPK による FULL HD 版と改造**の上に作られています。Galaxy on Fire 2™ と 4PDA のコミュニティに感謝します：
<https://github.com/KiritoJPK/Galaxy-on-Fire-2-FULL-HD-Android>

フォント：オリジナルゲームのインターフェース書体と、Inter（SIL Open Font License）。コントローラーのジャイロ：
Julian Smart による [JoyShockLibrary](https://github.com/JibbSmart/JoyShockLibrary)（MIT License）。Unity、Universal Render
Pipeline、Netcode for GameObjects、OpenXR で構築されています。

### コントリビューター

<a href="https://github.com/JoppieToppie/Galaxy-on-Fire-2-Unity-Remake/graphs/contributors">
  <img alt="コントリビューター" src="https://contrib.rocks/image?repo=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake">
</a>

## ⭐ Star 履歴

<a href="https://star-history.com/#JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&Date">
  <img alt="Star 履歴のグラフ" src="https://api.star-history.com/svg?repos=JoppieToppie/Galaxy-on-Fire-2-Unity-Remake&type=Date">
</a>

## ⚖️ 法的事項

© 2011 Designed and developed by FISHLABS Entertainment GmbH, powered by ABYSS® Game Engine. Galaxy on Fire 2™ and ABYSS®
are registered trademarks of FISHLABS Entertainment GmbH. All rights reserved.
本プロジェクトは非公式のファンプロジェクトであり、FISHLABS および Deep Silver とは提携しておらず、その承認も受けていません。

このリメイク独自のコードは [Apache License 2.0](../../LICENSE.md) でライセンスされています。
