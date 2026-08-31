# ドミノ倒しゲーム 企画・仕様書（初版）

## 1. ゲーム概要

### 仮タイトル

**Domino Rush / Domino Relay**

### コンセプト

3Dマップ上をプレイヤーキャラクターが自由に歩き回り、自分の手でドミノを配置して、スタート地点から複数のチェックポイントを経由し、ゴールまでドミノ倒しをつなげるゲーム。

単純にドミノを並べるだけではなく、

* プレイヤー自身がドミノにぶつかると倒してしまう
* ドミノを配置するときの操作を失敗すると、その場で倒れてしまう
* 一度倒してしまうと、それまで並べたドミノまで連鎖してしまう可能性がある

という、実際のドミノ倒しに近い「壊してしまう緊張感」をゲーム性の中心にする。

---

# 2. 基本仕様

| 項目      | 内容                             |
| ------- | ------------------------------ |
| ジャンル    | 3Dドミノ構築・物理アクション                |
| 視点      | 三人称視点                          |
| プレイ人数   | 1人 / オンライン最大4人                 |
| マップ     | 初期版は1マップ                       |
| ゲームエンジン | Unity 6.3 LTS                  |
| 描画方式    | Universal Render Pipeline（URP） |
| 開発言語    | C#                             |
| 物理演算    | Unity Physics / Rigidbody      |
| オンライン   | Netcode for GameObjects        |
| 接続管理    | Unity Multiplayer Services     |
| 3Dモデル   | Meshy等                         |
| バージョン管理 | Git / GitHub                   |
| AI開発支援  | ChatGPT / Claude               |

---

# 3. マップ

ゲーム内には以下のエリアを設置する。

**START → CHECKPOINT 1 → CHECKPOINT 2 → … → GOAL**

ただし、決められた一本道を進むゲームではない。

プレイヤーはマップ内を自由に移動し、自分でドミノのルートを考える。

そのため、チェックポイントは「プレイヤーが考えるドミノルートをある程度誘導するためのポイント」とする。

各エリアは床に色付きの枠などで表示する。

例：

* Start Area：青
* Checkpoint：黄
* Goal Area：赤

初期版では平坦なマップを中心にし、将来的には坂道、橋、障害物、ジャンプ台なども追加できるようにする。

---

# 4. プレイヤー

プレイヤーは人型キャラクターを操作する。

通常時は三人称視点で、マップ内を自由に移動できる。

初期操作イメージ：

| 操作     | 動作                      |
| ------ | ----------------------- |
| WASD   | 移動                      |
| Mouse  | カメラ操作                   |
| Shift  | ダッシュ                    |
| Space  | ジャンプ                    |
| 配置ボタン  | Domino Placement Mode開始 |
| 決定ボタン  | ドミノ配置                   |
| Cancel | 配置キャンセル                 |

初期開発ではキャラクターモデルを作り込まず、CapsuleやUnity標準の簡易キャラクターを使用する。

ゲームシステム完成後、Meshy等で作成した人型キャラクターへ置き換える。

---

# 5. ドミノ

ドミノはUnityの物理演算を利用する。

基本構成：

Domino

* Mesh Renderer
* Box Collider
* Rigidbody
* Domino Controller
* Network Object
* Network Transform等

プレイヤーや別のドミノが接触すると、通常の物理演算によって倒れる。

そのため、

「プレイヤーが誤って並べたドミノに接触する」

ことでもドミノ倒しが開始してしまう。

この仕様は意図的に残し、ゲームの緊張感として利用する。

---

# 6. ドミノ配置システム

本ゲームの特徴的なゲームシステム。

## 通常状態

通常時は三人称カメラでプレイヤーを操作する。

## Placement Mode

特定のボタンを押すとDomino Placement Modeへ移行する。

その瞬間、

* カメラをプレイヤー前方へ寄せる
* プレイヤーの目の前に半透明の「Ghost Domino」を表示
* Ghost Dominoを左右にメトロノームのように揺らす

という演出を行う。

イメージ：

左傾き

＼

↓

垂直

｜

↓

右傾き

／

↓

再び左傾き

＼

という動きを繰り返す。

## 配置確定

プレイヤーが決定ボタンを押した瞬間の位置・角度でGhost Dominoを実体化する。

実体化されたDominoには即座にRigidbodyの重力が適用される。

したがって、垂直に近いタイミングで配置すれば、

「ストン」

と地面に立つ。

一方、斜めのタイミングで配置してしまうと、

「ストン → バタン」

とそのまま倒れる。

その結果、近くに既に並べられたドミノがあれば、それらを巻き込んで倒してしまう可能性がある。

角度を自動補正しないことが重要なゲームルールとなる。

---

# 7. ゲームフェーズ

ゲーム内部では明確に状態を管理する。

### BUILDING

プレイヤーが自由にドミノを配置する状態。

この状態ではCheckpointやGoalにドミノが到達してもクリア判定は行わない。

### READY

全プレイヤーがStart Areaへ戻った状態。

マルチプレイの場合、

Player 1：Ready
Player 2：Ready
Player 3：Ready
Player 4：Ready

となった場合のみゲーム開始可能。

### RUNNING

正式なドミノ倒しを開始した状態。

この状態になって初めて、

Start
↓
Checkpoint
↓
Goal

の判定を開始する。

### SUCCESS

すべてのCheckpointを正しい順序で通り、Goalまでドミノ倒しが到達。

### FAILED

将来的に必要であれば追加。

例：

* 一定時間ドミノが動かない
* 制限時間超過
* 次のCheckpointに到達できない

など。

---

# 8. ゲーム開始条件

ドミノを作成している途中に偶然倒してしまい、そのドミノがGoalまで到達した場合、それをクリア扱いにはしない。

そのため、

**全プレイヤーがStart Area内にいる**

ことを正式なChallenge開始条件とする。

全プレイヤーがStart Areaに集合した後、

「Start Dominoを倒す」

操作によってRUNNINGへ移行する。

RUNNINGへ移行した後だけCheckpoint / Goal判定を有効にする。

---

# 9. チェックポイント

各Checkpointは順番を持つ。

例：

Checkpoint 1
↓
Checkpoint 2
↓
Checkpoint 3
↓
Goal

内部的には、

CurrentCheckpoint = 0

から開始。

Checkpoint 1到達

CurrentCheckpoint = 1

Checkpoint 2到達

CurrentCheckpoint = 2

というように進める。

Checkpoint 2を先に倒しても無効。

最終Checkpointを通過した後にGoalへ到達した場合のみクリアとする。

---

# 10. 「偶然クリア」の防止

単純にGoal Areaへ倒れたDominoが入っただけではクリアにしない。

最低でも以下の条件を満たす必要がある。

RUNNING状態であること。

Startから正式にChallengeが開始されていること。

Checkpointを正しい順序ですべて通過していること。

その後Goalへ到達すること。

将来的には、必要であれば「どのドミノからどのドミノへ衝突が伝播したか」を記録し、実際にStart Dominoからつながった連鎖かどうかまで検証するシステムへ拡張できる。

初期版ではそこまで複雑にせず、GameState + Checkpoint順序で判定する。

---

# 11. 倒れたドミノの削除

BUILDING中に誤って倒れたドミノを永久に残すとマップが散らかるため、自動削除する。

例：

Dominoが一定角度以上傾く
↓
Fallen判定
↓
5～10秒待機
↓
自動削除

ただしRUNNING状態では削除しない。

正式なドミノ倒しを実行している最中にドミノが消えてしまわないため。

具体的な秒数はプレイテスト後に決定する。

---

# 12. 一人プレイ

一人プレイはTime Attackを基本とする。

Challenge開始からGoal到達までの時間を計測する。

Result例：

CLEAR TIME：01:42.38
DOMINOES USED：126
FAILED PLACEMENTS：8
RANK：A

将来的には、

* ベストタイム
* 使用ドミノ数
* ミス回数
* オンラインランキング

などを追加する。

---

# 13. マルチプレイ

最大4人のオンライン協力プレイを想定する。

非常に重要な設計方針として、

**今回は開発初期からオンラインを前提とする。**

一人プレイを完成させてからオンライン対応を追加する方式にはしない。

ネットワーク構成は初期版では、

**Host + Client方式**

を採用する。

Player 1がHostとなり、Player 2～4がClientとして接続する。

インターネット越しの接続にはUnity Multiplayer Services / Relayを利用する。

最終的には、

Create Game
↓
Join Code発行
↓
友人がコード入力
↓
Join

程度のシンプルな仕組みを目指す。

---

# 14. ネットワーク上のドミノ

このゲームで技術的に最も難しい部分。

例えば、

4人 × 100個

配置すると400個のRigidbodyが存在する。

すべてのクライアントが別々に物理演算をすると、

Player 1では倒れた
Player 2では倒れていない

という状態になる可能性がある。

そのため基本方針として、

**Host側を物理演算の正解とする**

設計を採用する。

Host

Physics Simulation
↓
Domino Position / Rotation
↓
Network
↓
Clientへ同期

という構造を基本とする。

---

# 15. ドミノ配置のネットワーク処理

Placement Mode中のGhost Dominoは各Playerの画面だけに存在する。

そのためGhost Dominoの左右の揺れをネットワークへ送信する必要はない。

Playerが「配置確定」を押した瞬間のみ、

位置
角度

をHostへ送信する。

Host側で正式なDominoを生成し、他のPlayerへ同期する。

つまり、

Ghost Domino
＝ Local処理

Real Domino
＝ Network Object

と明確に分ける。

---

# 16. 将来的なキャラクターカスタマイズ

将来的にはキャラクターの着せ替えを追加する。

例：

* Head
* Hair
* Body
* Shoes
* Accessory

プレイヤーデータとして保存する。

---

# 17. 将来的な装備システム

装備品によってPlayer能力を変更できるようにする。

例：

Running Shoes
Move Speed +10%

Heavy Boots
Move Speed -5%
Stability +20%

将来的には、

* 移動速度
* ジャンプ力
* ドミノ配置速度
* ドミノ配置時の揺れ速度
* プレイヤーの衝突性能

なども調整可能。

ただしゲームバランスへの影響が大きいため、初期開発では実装しない。

---

# 18. Meshyの利用

Meshyなどの3D生成AIは、主にゲームの見た目を作る段階で使用する。

使用候補：

* Player Character
* Domino Skin
* 建物
* 木
* 岩
* 家具
* 障害物
* マップ装飾

ただし開発初期はAIモデルを使用しない。

Player = Capsule
Domino = Cube
Map = Plane

程度でゲームシステムを完成させる。

見た目よりゲーム性を先に確認する。

---

# 19. AIの利用

ChatGPT / Claudeは主に、

* C#実装
* Unity操作方法
* バグ調査
* クラス設計
* ネットワーク設計
* Physics調整
* Shader
* UI
* Git
* 仕様整理

などの開発支援として利用する。

一つのAIにすべて任せるのではなく、コードレビューや別案確認にも利用する。

---

# 20. 初期技術構成

推奨構成：

Unity：6.3 LTS
Template：Universal 3D / URP
Language：C#
Networking：Netcode for GameObjects
Online Services：Unity Multiplayer Services
Connection：Relay
Version Control：Git + GitHub
3D Model：Meshy
IDE：Visual Studio / Rider等

---

# 21. 開発方針

今回最も重要なのは、

**「一人用ゲームを完成させてからオンライン化しない」**

ことである。

ただし、最初からオンラインゲームを完全に作るわけでもない。

開発初期の非常に早い段階で、

Player 1
Player 2

の2人が同じマップに接続し、互いの移動が見える状態を作る。

その後、すべての主要ゲーム機能を

「Network Gameで動くか？」

を確認しながら追加していく。

---

# 22. 開発ロードマップ

## Phase 0：開発環境

Unity Hubインストール

Unity 6.3 LTSインストール

Universal 3D Project作成

Git / GitHub設定

プロジェクトFolder構成作成

---

## Phase 1：最小オンライン環境

Plane作成

Player作成

三人称移動

Camera作成

Netcode for GameObjects導入

Multiplayer Services導入

Host / Client起動

2人のPlayerが同じSceneに入る

Playerの移動を同期

**ここまでをかなり早い段階で完成させる。**

---

## Phase 2：Domino Prototype

CubeからDomino Prefab作成

BoxCollider設定

Rigidbody設定

ドミノ同士の衝突確認

10～20個程度を手動配置

ドミノ倒しが正常に連鎖することを確認

Playerが接触すると倒れることを確認

---

## Phase 3：Placement System

Player前方へGhost Domino表示

半透明表示

Placement Camera

左右に揺れる処理

Confirm

Real Domino生成

斜め配置した場合に倒れる

Cancel

連続配置

---

## Phase 4：Network Domino

ClientからPlacement Request

Host側でDomino生成

他Clientへ同期

Domino PhysicsをHost側で管理

複数Playerから同時配置

オンライン状態でドミノ倒し

---

## Phase 5：ゲームルール

Start Area

Checkpoint

Goal Area

BUILDING

READY

RUNNING

SUCCESS

クリア判定

倒れたDominoのCleanup

---

## Phase 6：ゲーム化

Time Attack

Timer UI

Result画面

Domino Count

ミス回数

Restart

Lobby

Join Code

最大4人接続

---

## Phase 7：見た目

MeshyによるCharacter作成

Map作成

Domino Model

Animation

Sound Effect

BGM

Particle Effect

UIデザイン

---

## Phase 8：将来機能

キャラクター着せ替え

装備

能力値

複数Map

ランキング

Steam対応

より高度なMatchmaking

追加Game Mode

など。

---

# 23. MVP（最初の完成版）

最初のゴールは大きくしない。

「完成した」とみなす最初のバージョンは、

* 2人がオンライン接続できる
* 3Dマップを歩ける
* Ghost Dominoを左右に振れる
* ボタンを押してDominoを配置できる
* Dominoが物理演算で倒れる
* Playerが触るとDominoが倒れる
* Startがある
* Checkpointがある
* Goalがある
* Start → Checkpoint → Goalでクリアできる

状態とする。

キャラクターデザイン、豪華なマップ、装備、ランキングなどはMVPには含めない。

---

# 24. 実現可能性

本ゲームはUnityで十分実現可能。

難易度としては、

基本3D移動：低
ドミノPhysics：低～中
Placement System：中
Checkpoint / Clear判定：中
1人Time Attack：低～中
オンラインPlayer同期：中
大量のDomino Physics同期：高

となる。

最も技術的に注意が必要なのは、

**大量のRigidbodyをオンラインで安定して同期させる部分**

である。

逆に言えば、ここを最初から意識した設計にしておけば、他のゲームシステムはUnityの標準機能で十分実装できる。

本プロジェクトは、個人開発としても段階的に開発可能な規模である。
