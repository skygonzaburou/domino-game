# ImplementationStatus(実装状況の整理)

Step 0〜Step 6.1について、「実装済み」「動作確認済み」「未実装」「将来確認が必要な事項」を整理します。ここでいう「動作確認済み」は、Unity Editor上での実機確認(スクリーンショット・ネットワーク状態の直接確認等)によって裏付けが取れているものだけを指し、コードを書いただけで未検証のものとは区別しています。

---

## Step 0: 開発環境・ネットワーク基盤の準備

### 実装済み
- プロジェクトのフォルダ構成作成(`Assets/_Project/Scripts/{Core, Player, Networking, CameraSystem}`, `Assets/_Project/Prefabs`)
- `com.unity.netcode.gameobjects` 2.13.2 導入(依存として `com.unity.transport`, `com.unity.collections`, `com.unity.burst`, `com.unity.mathematics` が自動導入)
- Scene内に `NetworkManager` GameObjectを作成し、`NetworkManager` コンポーネントと `UnityTransport` コンポーネントを付与、両者を接続

### 動作確認済み
- パッケージ導入後、プロジェクト全体がエラーなくコンパイルできること
- `NetworkManager`/`UnityTransport` の設定値がEditor上に正しく反映されていること

### 未実装
- (このStepの範囲に該当するものはなし。純粋な環境構築のみ)

### 将来確認が必要な事項
- 特になし

---

## Step 1: 1人プレイのキャラクター移動

### 実装済み
- `Ground`(Plane, 30m×30m)の作成
- `Player` GameObject(後にPrefab化): `Rigidbody`, `CapsuleCollider`, `PlayerStats`, `PlayerMotor`
- `PlayerMotor`: WASD移動、Shiftダッシュ、Spaceジャンプ、接地判定、カメラの向きを基準にした移動方向の計算、進行方向への体の回転
- `ThirdPersonCamera`: マウスによる視点操作(ヨー・ピッチ)、`SetTarget()` による追従対象の切り替え、タグ`"Player"`を使った自動追従(1人プレイ時の簡易フォールバック)

### 動作確認済み
- **重力と地面の当たり判定**: 高さ3の位置から落下させ、正確にY=0(Capsule底面が地面に接する高さ)で静止し、速度が0に収束することを確認(入力の影響を排除した単独テスト)
- **前進移動の方向と速度**: キー入力をシミュレートし、設定した `moveSpeed`(5)におおむね一致する速度で、カメラの向きに沿った方向へ移動することを複数回確認
- コンパイルエラー・実行時エラーが出ないこと

### 未実装
- (Step1の範囲としては移動・カメラの基本機能は一通り揃っている)

### 将来確認が必要な事項
- **実際のキーボード/マウス操作による「操作感」の確認**: 自動テストではUnity Editorの入力イベントキューに遅延があり、「キーを押している間だけ動き、離すと止まる」という正確な制御の自動検証が難しかったため、人の手による実際の操作確認がまだ行われていません(前回、開発者ご自身での確認手順をご案内済みです)
- ジャンプとダッシュについては、移動方向/速度ほど繰り返し検証できていません
- `PlayerMotor.groundMask` が現状「すべてのレイヤー」を対象にしており、専用の「Ground」レイヤーを切っていません。将来、判定を絞りたい場合は整理が必要です

---

## Step 2: オンライン化(Host + Client)

### 実装済み
- `Player.prefab` 化(Scene直置きを廃止し、`NetworkManager.NetworkConfig.PlayerPrefab` として登録。`DefaultNetworkPrefabs.asset` にも自動登録された)
- `Player.prefab` に `NetworkObject`, `NetworkTransform`(`AuthorityMode = Owner`), `NetworkRigidbody`(`UseRigidBodyForMotion = true`, `AutoUpdateKinematicState = true`)を追加
- `PlayerMotor` を `NetworkBehaviour` 化し、`Update()`/`FixedUpdate()`/`OnNetworkSpawn()` に `IsOwner` 判定を追加
- `NetworkBootstrap.cs` の作成(当初はコマンドライン引数 `-host`/`-client` によるStandalone Build用の自動起動)
- `com.unity.multiplayer.playmode`(MPPM)2.0.2 導入
- MPPMのMain Editorに Tag `"Host"`、Virtual Player(Player 2)に Tag `"Client"` を設定
- `NetworkBootstrap` をMPPM対応に拡張(`CurrentPlayer.Tags` を判定し、該当すれば自動的に `StartHost()`/`StartClient()` を呼ぶ。コマンドライン引数判定と共存)
- UnityTransportのポートを `7777` → `7778` に変更(検証中に7777がEditorプロセス内でリークしたため)

### 動作確認済み
- Host起動(`StartHost()` 呼び出し後、`IsListening == true` を確認)
- MPPM経由でHost(Player 1)+Client(Player 2)の2人が同一セッションに接続(`ConnectedClientsList.Count == 2`, `ConnectedClientsIds == [0, 1]`)
- 各Playerが1体ずつ、正しい `OwnerClientId`(Host=0, Client=1)でSpawnされること(Host側から確認)
- **入力の分離**: Host側でシミュレートしたキー入力が自分のPlayer(owner=0)のみを動かし、もう一方(owner=1)には一切影響しないこと(`IsOwner` ガードが正しく機能していることの確認)
- **カメラの分離**: Host側のカメラは常に自分のPlayer(owner=0)だけを追従し続け、Client側(Player 2の実画面をキャプチャして確認)のカメラは自分のPlayer(owner=1)だけを追従すること
- **画面間の同期の初期状態**: Client(Player 2)接続直後の実画面キャプチャで、Host側・Client側両方のPlayerが同一Scene内に表示されていることを確認
- Host側・Client側(Console表示)双方でErrorが出ていないこと

### 未実装
- Standalone Build(実際にビルドした.exeを複数起動する方式)での接続確認は、環境要因(Unity Editorプロセス内でのポートリーク、ビルドしたPlayerのログファイル書き込みの遅延、2つのUnityプロセスを同時実行することによるリソース競合)により、自動化された形での完全な確認はできませんでした。最終的にMPPMでの確認に切り替えて成功しています。Standalone Build自体のビルドと起動は成功しており、コードとしては両方の起動方法(MPPM/コマンドライン引数)に対応済みです
- Unity Multiplayer Services / Relay を使った、実際のインターネット越しの接続(GameDesign.md 13節の最終形)は未着手です。現状は同一PC内の `127.0.0.1` 通信のみです
- ロビー画面・Join Code発行などのUIは未実装です。現状はコードレベルでの自動起動のみで、ユーザーが操作するUIはありません
- 3人目・4人目のプレイヤーを同時接続させる確認は行っていません(2人での確認のみ)

### 将来確認が必要な事項
- **Owner AuthoritativeなPlayerと、将来実装するHost AuthoritativeなDominoとの物理衝突**が、実際のHost/Client環境で正しく成立するかどうか。`NetworkRigidbody` の設定(Kinematic化+MovePosition経由の反映)により理論上は成立する設計にしてありますが、実際にDominoを実装し、複数プロセス環境で衝突させて確認するまでは未検証です
- MPPMのVirtual Playerウィンドウが、**OSのフォーカスが外れていると描画やシミュレーションが停止しているように見える**挙動について、開発・デバッグ時にどの程度注意が必要か(実際のプレイヤー体験には影響しません。MPPMというテストツール特有の挙動と考えられます)
- Client同士(HostではないPlayer2とPlayer3など)の間での移動同期の相互確認(今回は2人=Host/Client 1組の組み合わせのみ確認)
- `NetworkConfig.ConnectionApproval` が現状無効(誰でも接続を許可する設定)になっている点。ローカルでのテスト目的では問題ありませんが、将来公開する際には接続の可否を判定する仕組みの検討が必要です

---

## Step 3: Domino Physics Prototype(ネットワーク非対応・ローカル物理のみ)

Step 3では意図的にネットワーク関連の設定・コードには一切手を加えていません(`NetworkBootstrap`、Port 7778、`NetworkManager`/`UnityTransport`、Playerの`NetworkObject`/`NetworkTransform`/`NetworkRigidbody`、Owner Authority設計はStep 2から変更なし)。Dominoは`NetworkObject`を持たない、純粋なローカルGameObjectです。

### 実装済み
- `Domino.prefab`(`Assets/_Project/Prefabs/Domino.prefab`)の作成: Cubeベース、Width0.30×Height1.00×Depth0.15。`MeshFilter`/`MeshRenderer`/`BoxCollider`/`Rigidbody`のみで構成し、PhysicsとVisualは分離していない(現時点で分離する明確な理由がないため)
- 専用の`PhysicsMaterial`を2種類新規作成(`Assets/_Project/Physics/`): `DominoPhysicsMaterial`(Domino同士・DominoとGround共通で使用)と`GroundPhysicsMaterial`(Groundの`MeshCollider`に使用)。最終的には両方とも同じ摩擦値(0.6/0.6/反発0)
- Domino物理パラメータの調整(詳細はDomino Prefabの実装報告を参照): Mass=1, LinearDamping=0.05, AngularDamping=0.05, CollisionDetection=Continuous Dynamic, Interpolation=Interpolate
- Scene(`SampleScene.unity`)内に`DominoLine`という親GameObjectを作成し、その下にDomino 20個(`Domino_00`〜`Domino_19`)を一直線上・中心間隔0.4mで配置(Playerのスポーン地点との干渉を避けるため、原点から離れた位置(6, 0, 0)にオフセット)
- `Domino_00`のみ意図的にX軸+15°傾けてあり、Play Modeに入るだけで自然に倒れてChain Reactionが始まるようにしてある(将来の正式なPlacement Systemとは無関係な、動作確認用の簡易トリガー)

### 動作確認済み
- **10個・20個でのChain Reaction**: 中心間隔0.4mの構成で、同一条件を2回連続実行しどちらも完全に一致する結果(全Dominoが約68°の角度で連鎖的に倒れ、列の最後のDomino だけが完全に水平になる)を確認。再現性のある安定した連鎖であることを確認済み
- **Player衝突によるChain Reaction**: HostのPlayer(Owner側)をDomino_00へ物理的に接触させることで、Player自身の操作以外の追加処理なしにChain Reactionが開始し、最後のDominoまで到達することを確認
- **不自然な跳ね・滑りの有無**: 静止後の各Dominoの水平方向(X)位置のズレはいずれも0.06m未満で、目立った横滑りは無し。Z方向の位置変化は倒れる回転に伴う自然な重心移動の範囲内であることを確認
- Console上にProject由来のError/Warningが一切出ていないこと

### 未実装
- Ghost Domino, Placement Mode, Placement UI(Step 4以降の範囲)
- DominoのNetworkObject化、NetworkTransform/NetworkRigidbodyの追加、RPC、Network Spawn、Host AuthorityによるDomino同期(Step 5以降の範囲)
- Start Area, Checkpoint, Goal, GamePhase, Ready判定, Fallen Domino Cleanup, Relay, Lobby(いずれも本Stepの対象外)
- 100〜数百個規模でのパフォーマンス検証(今回は10個・20個のみ)

### 将来確認が必要な事項
- **DominoのPhysics挙動はパラメータ境界付近で非決定的(カオス的)になりうる**: 中心間隔0.5m〜0.7mの範囲では、同一条件でPlay Modeを再実行しても「最後まで連鎖する回」と「途中で止まる回」が生じることを確認しました。これは浮動小数点演算の誤差が、不安定平衡(倒れ始めのDomino)を起点とする連鎖に対して大きく増幅されるためと考えられます。最終的に採用した間隔0.4mではこの問題は再現しませんでしたが、**将来Dominoのサイズや配置角度を変更する場合、同様の「たまに連鎖が止まる」現象が起こりうることを念頭に置き、必ず複数回の再現テストを行ってください**
- Step 4でGhost Domino / Placement Systemを実装する際、プレイヤーが手動で配置する間隔がこの0.4m基準からどの程度ズレても連鎖が成立するか(許容誤差)は未検証です
- DominoをNetwork化するStep 5では、Host権威の物理演算をClientにどう反映するかに加え、上記の「非決定性」がHost/Client間で異なる結果(Desync)を招く可能性があるため、特に注意して検証する必要があります
- BoxColliderにPhysics Materialを設定する際、`manage_prefabs`系のツールでPrefab変換時に物理マテリアルの参照が失われる場合があることが分かりました(`create_from_gameobject`実行時に警告なく発生)。Prefab化の後は必ず対象コンポーネントの値を読み直して確認する運用を徹底してください

---

## Step 4: Domino Placement System(Ghost Domino・配置システム、ネットワーク非対応・ローカルのみ)

Step 4でもネットワーク関連の設定・コードには一切手を加えていません(`NetworkBootstrap`、Port 7778、`NetworkManager`/`UnityTransport`、Playerの`NetworkObject`/`NetworkTransform`/`NetworkRigidbody`、Owner Authority設計はStep 2から変更なし)。Step 3で決定したDomino物理パラメータも変更していません。Ghost Domino・確定後のReal Dominoともに`NetworkObject`を持たない、純粋なローカルGameObjectです。

### 実装済み
- 新Input System(`Keyboard.current`/`Mouse.current`)を直接ポーリングする方式で、`E`キー(Placement Mode開始)、左クリック(確定)、右クリック/`Escape`(キャンセル)を実装。キー割り当ては`PlacementController`のInspectorフィールド(`enterPlacementKey`, `cancelKey`)で変更可能
- `PlacementController.cs`(`Domino.Player`名前空間, `NetworkBehaviour`)を新規作成し、`Player.prefab`に追加。`OnNetworkSpawn()`で非Owner側は`enabled = false`にして無効化(`PlayerMotor`と同じ`IsOwner`ガードパターンを踏襲)
- `GhostDomino.cs`(`Domino.DominoSystem`名前空間, 通常の`MonoBehaviour`)を新規作成。`NetworkObject`を持たず、メトロノーム状の首振り(サイン波)を`Update()`で計算するだけの純粋にローカルな見た目専用コンポーネント
- `PlayerMotor.SetMovementEnabled(bool)`を追加。Placement Mode中は水平方向の速度を1回だけゼロにして入力読み取りをスキップするだけで、**Rigidbodyの`isKinematic`化やコンポーネント無効化は行わない**(Playerは物理オブジェクトのまま)
- `ThirdPersonCamera.cs`を拡張し、既存の三人称視点はそのままに、`EnterPlacementView()`/`ExitPlacementView()`による専用ビューへの遷移を追加。Cinemachine等の新規パッケージは導入せず、既存スクリプトの拡張のみで対応(`distance/pitch`ベースの通常カメラ計算と、Ghost基準のサイドビュー計算を`Time.deltaTime`ベースで線形/球面補間してブレンド)
- `Assets/_Project/Prefabs/GhostDomino.prefab`(新規)を作成: `Domino.prefab`と同じ見た目サイズ(0.3×1.0×0.15)、`MeshFilter`/`MeshRenderer`/`GhostDomino`のみで構成し、**Collider・Rigidbodyを持たない**(Physics非干渉を構造的に保証)
- `Assets/_Project/Materials/GhostDominoMaterial.mat`(新規)を作成: URP/Litシェーダーを半透明(Transparent Surface)設定に変更し、水色・不透明度40%程度の見た目に調整
- Ghost位置の決定パラメータ(`forwardDistance`, `hoverHeight`, `groundRaycastRange`, `groundMask`)と、Wobbleのパラメータ(`maxTiltAngle`, `periodSeconds`, `tiltAxis`)をすべてInspector調整可能な`[SerializeField]`として実装(ハードコード無し)
- `SampleScene.unity`内の`DominoLine`(Step 3のテスト用20個のDomino)を非アクティブ化。削除はしておらず、再度アクティブ化すればいつでも参照可能

### 動作確認済み
すべてUnity Editor上でHost自身のPlayerとして実機検証(New Input Systemの`QueueStateEvent`によるキー/マウス入力シミュレーションと、`execute_code`による内部状態の直接読み取りを併用)。

- **Placement Mode開始(Eキー)**: Ghost Domino が`forwardDistance=1.2`/`hoverHeight=0.15`の設定通り、Player前方・地面から浮いた位置に出現することを確認。Player速度が同時にゼロになること、Rigidbodyが非Kinematicのまま維持されることも確認
- **Ghostの半透明表示・Physics非干渉**: 半透明の水色マテリアルで正しく描画されること(スクリーンショットで視覚確認)、`Collider`/`Rigidbody`を持たないためPhysicsに一切参加しないことをPrefab構成の確認で担保
- **Wobble(首振り)**: 実測で約9°〜25°の範囲で角度が周期的に変化することを複数回のサンプリングで確認(設定値`maxTiltAngle=25`と整合)
- **カメラのPlacement View遷移**: Ghost出現時にカメラが`(2.20, 2.05, 1.20)`・回転`(30.6°, 270°, 0°)`へ正しく遷移することを、設計値からの計算結果と実機の値が一致することで確認
- **左クリックでの確定 — 角度の無補正**: `GhostDomino`の内部状態を意図的に「完全に垂直(0.0000°)」に固定した状態で`ConfirmPlacement()`を実行し、生成されたReal Dominoの角度が寸分違わず`0.0000°`であることを確認。以後の自然なWobble状態からの確定でも、角度に対する補正処理が一切行われていないことをコード上・実測の両方で確認
- **確定直後の物理有効化**: 確定と同時にReal Dominoの`Rigidbody`(`useGravity=true`, `isKinematic=false`)と`Collider`(`enabled=true`)が即座に有効な状態であることを確認
- **垂直配置→起立/傾斜配置→即倒壊**: 上記の「垂直固定」テストでは、物理シミュレーション後もDominoが起立したまま静止(`Y=0.500`, 垂直からの角度`0.000°`)。一方、Wobble中の傾いたタイミング(約19°)で確定したケースでは、直後に完全に横倒しになって静止(`Y=0.075`, 垂直からの角度`90.000°`)。両ケースとも「角度をそのまま採用し、Unity標準の物理演算に委ねる」という設計通りの結果
- **キャンセル(Escape/右クリック)**: 両方とも新規Dominoを生成せず、Placement Modeを終了し、カメラ・移動を通常状態に復帰することを確認
- **複数回のPlacementサイクル**: Confirm/Cancelを繰り返しても内部状態(`_isPlacing`)やGhostインスタンスの使い回しが壊れないことを確認。テレポートによる疑似的な「歩いて次の場所へ移動」を挟んだ2回の追加配置を含め、合計5個のDominoを連続して配置することに成功
- **Host/Client間の分離(構造面)**: MPPM経由でHost+Client 2人を接続した状態で、Host側から見て非Owner側(Client)の`PlacementController.enabled`が`false`であることを確認。`PlayerMotor`と全く同じ`IsOwner`ガードパターンであり、Step 2で既に実証済みの仕組みと同一
- **Host→Client方向の非干渉(実機確認)**: Host側で行った一連のPlacement Mode操作(Ghost表示・カメラ遷移・複数回のDomino確定)の最中および前後で、Client仮想プレイヤーの実画面をキャプチャして確認したところ、Ghostは一切表示されず、カメラアングルにも影響がないことを視覚的に確認(Client画面にはHost/Client双方のPlayerカプセルのみが表示)
- Console: 本Stepの実装コード(`PlacementController`/`GhostDomino`/`PlayerMotor`/`ThirdPersonCamera`)由来のError/Warningは検証全体を通じて0件

### 未実装/確認できなかった事項
- **Client自身がEキーを押してPlacement Modeに入る側の直接確認**: MPPMの仮想Player(Client)プロセスは専用のUnityMCPブリッジを持たないため、OSレベルの`SendInput`(仮想キーコード方式・Scan Code方式の両方)によるキーボード入力シミュレーションを試みましたが、同じ手法で送ったマウス入力(カメラ回転)は届く一方、キーボード入力だけがこのプロセスに届かず、直接の実機確認はできませんでした。コード上はHost側と完全に対称な`IsOwner`ガード・完全ローカルなGhost設計のため同じ結果になるはずですが、この一点のみ未確認です
- 意図的に間隔を制御した「連続配置によるChain Reaction成立」の検証(Step 3の0.4m基準との整合性)は今回行っていません
- Ghost位置決定のためのRaycastは平坦な地面でのみ検証しており、傾斜地(将来の拡張)では未検証です

### 将来確認が必要な事項
- **Client側でのPlacement Mode操作の実機確認**: 開発者ご自身の手元での操作確認をお勧めします(手順はMultiplayerTesting.mdと同様、MPPMでClientをアクティブ化した上でClientウィンドウ側でEキー/クリックを操作するだけです)
- **`NetworkTransform`付きオブジェクトへの直接`transform.position`代入は反映されない**: デバッグ作業中に判明した挙動で、Rigidbodyの物理演算側の位置が優先され、直接代入した値は次のフレームで巻き戻ります。将来リスポーン等でPlayerやDominoの位置を強制的に変更する処理を実装する場合は、必ず`NetworkTransform.Teleport(position, rotation, scale)`を使う必要があります
- 検証ログに残った2件のConsole警告("Setting linear velocity of a kinematic body is not supported"等)は、検証用の一時的なテストコード(Player位置の強制リセット処理)が原因であり、`PlacementController`/`GhostDomino`/`PlayerMotor`/`ThirdPersonCamera`などの本実装コードからは発生していません
- Step 5でDominoをNetworkObject化する際、`PlacementController.ConfirmPlacement()`のローカル`Instantiate()`をHost側でのNetwork Spawn呼び出しに置き換える設計変更が必要になります(→ Step 5で実施)
- Step 3で確認された「間隔0.5m〜0.7mでの非決定的なChain Reaction」の問題は、プレイヤーが手動で配置する間隔にも同様の影響を与えうるため、Step 5以降で配置間隔の目安をUI等でガイドすることを検討する価値があります

---

## Step 6: Game Flow / Placement UX改善 / Stage Authoring

Step 6では、Step 5までのNetwork Physics Architecture(`NetworkBootstrap`/Port 7778/Player・DominoのNetworkObject構成/`DominoSpawner`/Placement Request RPC/Placement Validation/Ghost DominoのLocal Only/Step 5で修正したReal DominoのSpawn Height処理)には一切変更を加えていません。今回追加したのは(1)Host AuthorityのGameFlow(BUILDING/READY/RUNNING/SUCCESS)、(2)Placement Position Marker、(3)Domino Widthの軽微な調整、(4)Stage Authoring構成、の4点です。

### 実装済み

**Placement UX**
- `PlacementMarker.cs`(新規, `Domino.Player`名前空間, 通常の`MonoBehaviour`)と`PlacementMarker.prefab`(新規)を作成。床に半透明の円盤を表示するだけの完全ローカル・見た目専用オブジェクト
- `PlacementController.cs`を変更: Marker/Ghost/Real Dominoの位置計算を`ComputeGroundAnchor()`という単一メソッドに統一(Single Source of Truth化)。右クリックでMarker表示・更新、`E`キーでMarker位置(無ければその場で計算した位置)からPlacement Mode開始、`Escape`のみでキャンセルするよう入力を再整理(右クリックのキャンセル機能は廃止)
- `PlacementController.Update()`の先頭で`GameFlowManager.CurrentPhase == GamePhase.BUILDING`を確認し、BUILDING以外では配置操作を一切受け付けないよう変更。Placement Mode中にPhaseが変化した場合は自動的にCancel処理が走る

**Domino Width**
- `Domino.prefab`・`GhostDomino.prefab`のWidthを0.30→**0.35**に変更(Height 1.00・Depth 0.15は変更なし)。BoxCollider・Rigidbody・NetworkObject関連の設定は無変更

**Game Flow**
- `GamePhase.cs`(enum: BUILDING/READY/RUNNING/SUCCESS)を新規作成
- `GameFlowManager.cs`(新規, `NetworkBehaviour`)を作成し、Scene内に新規GameObject `GameFlowManager`(Scene-placed `NetworkObject`)として配置。`NetworkVariable<GamePhase>`・`NetworkVariable<int>`(CheckpointProgress)・`NetworkVariable<bool>`(StartTriggered)の3つでHost Authorityの状態を全Clientへ同期
- `DominoFallenZone.cs`(新規, abstract `MonoBehaviour`)を作成し、「Zone内でDominoが倒れたか」の判定を共通化。`StartAreaZone.cs`・`CheckpointZone.cs`・`GoalAreaZone.cs`(いずれも新規)がこれを継承
- `ChallengeInput.cs`(新規, `NetworkBehaviour`)を作成し、Player.prefabへ追加。`F`キー押下を`GameFlowManager`への`[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]`呼び出しに変換
- `Domino.prefab`に`"Domino"`タグを新規追加(Zoneのトリガー判定で、対象がDominoかどうかを判別するため)

**Stage Authoring**
- `Assets/_Project/Prefabs/GameFlow/`に`StartArea.prefab`・`Checkpoint.prefab`・`GoalArea.prefab`を作成。いずれもRoot(BoxCollider Trigger + 判定Script)とVisual子オブジェクト(半透明の薄いパッド、床に沿って表示)で構成
- Scene内に`GameFlowZones`という空の親GameObjectを作成し、`StartArea`・`Checkpoint_01`(`CheckpointIndex=0`)・`Checkpoint_02`(`CheckpointIndex=1`)・`GoalArea`の4インスタンスを配置。`GameFlowManager`のInspectorへこれら4つを明示的に登録
- `Docs/StageAuthoring.md`を新規作成

### 動作確認済み

Host自身のPlayerでの操作をNew Input Systemの`QueueStateEvent`でシミュレートし、Unity Editor上のPlay Modeで確認しました。

**Placement UX**
- 右クリックでMarkerが正しい位置(Player前方+地面検出)に表示されることを確認
- `E`キーでGhostがMarkerと同じX/Z位置に表示されることを確認(数値上、完全一致)
- 確定後のReal DominoもMarker/Ghostと同じX/Z位置にSpawnすることを確認(数値上、完全一致)
- 右クリックせず`E`キーだけでもPlacement Modeに入れることを確認(フォールバック動作)
- `Escape`でキャンセルすると、Marker・Ghostともに非表示になり、`_isPlacing`が`false`に戻ることを確認
- READY Phaseへの遷移時、Marker表示中であれば自動的に非表示になることを確認

**Domino Width**
- Width 0.35のDominoが、垂直配置で安定して起立すること、傾斜配置で自然に倒れることを確認(いずれもStep 5と同じ精密検証手法(Ghost回転を厳密に0°/複数角度へ固定してConfirm)で確認)
- Width 0.35のDominoでも、10個の連続配置によるChain Reactionが成立することを確認(Step 3・5と同じ「約68°で連鎖、最後の1個が完全水平」というパターンを再現)

**Game Flow**
- **BUILDING → READY**: 接続中の全Player(この検証ではHost 1人、または疑似的にClient分も含めた判定ロジック)がStart Area内にいる場合にのみREADYへ遷移することを確認。1人でも外に出るとBUILDINGへ戻ることを確認
- **READY → RUNNING**: `F`キーで`RequestStartChallengeRpc()`が送信され、Host側でREADY状態かつ全員Start Area内であることを再検証した上でRUNNINGへ遷移することを確認。SUCCESS中に`RequestStartChallengeRpc()`を呼んでも何も起きないことを確認
- **BUILDING中のAccidental Fall**: BUILDING中にStart Area内でDominoを倒しても(90°まで完全に倒れることを確認済み)、`StartTriggered`・`GamePhase`ともに変化しないことを確認
- **RUNNING開始前から倒れていたDominoの除外**: BUILDING中に倒したDominoがStart Area内に残ったままRUNNINGへ遷移させても、そのDominoは除外リストに入り`StartTriggered`が`false`のままであることを確認。同じRUNNING中に**新しく**倒したDominoで正しく`StartTriggered=true`になることを確認
- **Checkpoint順序判定**: CP2(`CheckpointIndex=1`)を先に倒しても`CheckpointProgress`が0のままであることを確認。その後CP1(`CheckpointIndex=0`)を正しい順序で倒すと、既に倒れていたCP2の分もまとめて`CheckpointProgress`が2まで進む(いわゆる「追いつき」動作)ことを確認
- **Goal Before Checkpoint**: Checkpoint未通過の状態でGoal内のDominoを倒しても、Goal自体は「満たされた」状態になるが`GamePhase`は`SUCCESS`にならないことを確認。その後Checkpointを正しい順序で全て通過させると、既に倒れていたGoalの分もまとめて即座に`SUCCESS`へ遷移することを確認
- **Full Clear**: Start→CP1→CP2→Goalを正しい順序で倒すことで`GamePhase`が`RUNNING`→`SUCCESS`になることを確認
- Console: 検証中に発見した実装バグ(下記参照)を修正した上で、Project由来のError/Warningが0件であることを確認

### 未実装/確認できなかった事項

- **Client自身の操作によるREADY/RUNNING/Placementの直接確認**: Step 4・5から持ち越しの制約で、MPPMの仮想Player(Client)プロセスへOS経由でキーボード入力を送る手段が今回も見つかりませんでした。そのため、複数Playerが実際に同時にStart Areaへ歩いて入り、READYになる様子は自動テストできておらず、`GameFlowManager`の公開メソッド(`NotifyPlayerEnteredStart`/`NotifyPlayerExitedStart`)を実際に接続しているClientのIDで直接呼び出すことで、判定ロジック自体は検証しています。**開発者ご自身での複数Player実機確認を推奨します**(手順は`StageAuthoring.md`・`GameFlow.md`を参照)
- Domino Width 0.33 / 0.38との詳細な比較(見た目の並べ比べ)は行っていません。0.35が視覚的・物理的に問題なく機能することを確認した上で、ユーザー様の第一候補どおり採用しました
- 3個以上のCheckpointを持つStageの実機テストは行っていません(構造上対応していますが、Prototype Sceneでは2個のまま検証しました)

### 将来確認が必要な事項

- **Client自身の操作によるGame Flow確認**(上記参照)
- **バグ修正の記録**: 検証中、Play Modeを終了(Stop)する瞬間に、破棄されつつあるDominoのColliderが最後のTrigger Enter/Stay/Exitイベントを発生させ、その時点で既に`NetworkManager.Singleton`が`null`になっているために`NullReferenceException`が発生する不具合を発見しました。`DominoFallenZone.CheckDomino()`の先頭に`NetworkManager.Singleton == null`のガードを追加して修正済みです。今後、Play Mode終了直前に何らかの処理が走るコードを追加する際は、同様の「Singletonがnullになるタイミング」に注意してください
- Checkpointの`Fallen Angle Threshold`(既定60°)は、Domino Width変更後の重心・接触ジオメトリの変化を踏まえた再検証は簡易的なものに留まっています。Stageのレイアウトによっては調整が必要になる可能性があります
- READY判定は「Start Areaの範囲内にいるかどうか」のみで行っており、Playerがジャンプ中/落下中などでも範囲内なら即座にReadyとして扱われます。ゲームデザイン上の意図的な仕様か、将来調整すべき点かは要検討です

---

## Step 6.1: Placement Marker UX改善(Live Preview化)

Step 6.1では、Step 6で実装したGameFlow判定(BUILDING/READY/RUNNING/SUCCESS、Zone判定、Checkpoint順序判定等)、Domino Physics、Network Authority、DominoSpawner、Stage Authoring構成には一切変更を加えていません。変更したのはPlacement Markerの表示タイミングのみです。

### 実装済み

- `PlacementMarker.cs`を変更: `MarkerState`(`Preview`/`Locked`)enumを追加し、`SetState()`でMaterialの不透明度(`previewOpacity=0.2` / `lockedOpacity=0.45`)を切り替えられるようにしました。1つの`Material`インスタンスを`Awake()`でキャッシュし、Preview用・Locked用に別Prefabを作る必要がない構成にしています
- `PlacementController.cs`を変更: `_markerLocked`(bool)フィールドを新設し、Marker/Placementの状態を「BUILDING中・Placement Mode外・未Lock」の間は**毎フレーム**`ComputeGroundAnchor()`→`ShowMarker(Preview)`を呼ぶLive Preview方式に変更しました。右クリックで現在のPreview位置へLock(`ShowMarker(Locked)`)、再度右クリックすれば現在位置へ再Lock、`E`キー押下時に未Lockであればその場でLockしてからPlacement Modeへ入るようにしています。確定・キャンセル後は`_markerLocked`を`false`にリセットし、BUILDINGのままであれば次フレームから自動的にLive Previewが再開します
- 旧`UpdateMarker()`メソッドは`ShowMarker(MarkerState)`に統合・置き換えました

### 動作確認済み

Host自身のPlayerでの操作をNew Input Systemの`QueueStateEvent`でシミュレートし、Unity Editor上のPlay Modeで確認しました。

- **自動表示**: 右クリックしなくても、BUILDING中は薄いPreview状態のMarkerが自動的に表示されることを確認(材質のAlpha値が`previewOpacity=0.2`であることも直接確認)
- **移動追従**: Playerを離れた位置へテレポートすると、Marker位置がPlayerの新しい前方位置へ正しく追従することを確認(数値上、Player位置+forwardDistanceと完全一致)
- **回転追従**: Playerの向きを変えると、Marker位置もその新しい前方方向へ移ることを確認
- **Lock**: 右クリックでMarkerが現在の位置に固定され(材質Alpha値が`lockedOpacity=0.45`に変化)、その後Playerを大きく移動させてもMarker位置が動かないことを確認
- **Re-Lock**: Lock済みの状態でもう一度右クリックすると、その時点の新しいPlayer位置へMarkerが再配置されることを確認
- **Eのみでの配置**: 右クリックせず`E`だけを押した場合も、その瞬間のPreview位置がそのままLockされ、Ghostがその位置に正しく表示されることを確認(数値上、Marker/Ghostの X/Z が完全一致)
- **確定後の位置一致**: 確定したReal DominoのX/Z座標が、Lock時点のMarker/Ghostの座標と一致することを確認
- **Escapeキャンセル**: キャンセル後、Ghost・Markerともに非表示になり、`_isPlacing`が`false`に戻ることを確認
- **Live Previewの再開**: 確定・キャンセルいずれの場合も、BUILDINGが継続していれば直後にMarkerがPreview状態で再表示されることを確認
- **Phase変化での非表示**: READYへ遷移した瞬間、Markerが非表示になることを確認。BUILDINGへ戻ると自動的にLive Previewが再開することを確認
- Console: Project由来のError/Warningが0件であることを確認

### 未実装/確認できなかった事項

- **Client自身の操作によるLive Preview追従の直接確認**: Step 4〜6から持ち越しの制約(MPPM仮想Playerウィンドウへのキーボード入力自動送信不可)により、Client自身が実際に歩いてPreview Markerが追従する様子は自動テストできていません。コード上はHost自身のPlayerと全く同じ`IsOwner`ガードのローカル処理であり、`PlacementMarker`/`PlacementController`ともに`NetworkObject`を持たないため、対称的に同じ挙動になるはずです

### 将来確認が必要な事項

- Live Previewは毎フレームRaycastを1回実行します。現状Player 1人あたりの軽量な処理であり問題ありませんが、将来的にPlayer数が増えた場合(4人想定)は、4人分×毎フレームのRaycastが同時に発生することになります。現時点では許容範囲と考えていますが、将来的なパフォーマンス計測の対象として記録しておきます
- 「倒れていさえすればClear判定へ利用される」現在のGameFlow判定の限界(Chain Provenance判定が無いこと)は、ユーザー様のご指示どおり今回は変更していません。別Stepでの改善が予定されています

---

## Step 7: Fallen Domino Cleanup / Domino Lifecycle

Step 7では、BUILDING中に(誤操作等で)倒れたDominoを一定時間後に自動的にNetwork Despawnし、Playerが同じ場所に置き直せるようにする仕組みを追加しました。既存のGame Flow / Clear Judgement(Step 6で実装したBUILDING/READY/RUNNING/SUCCESS判定、Zone判定、Checkpoint順序判定、いわゆる「まとめて追いつく」仕様含む)、Domino Physics、Network Authority(Port 7778、Host-Authoritative Domino、Owner Authority Player)、Placement Marker/Ghost/Validationには一切変更を加えていません。ユーザー様からの明示的なご指示により、Clear Judgementの緩さ(`GameFlow.md`参照)は今回のStepでは意図的に手を付けていません。

### 実装済み

- `DominoCleanupManager.cs`(新規, `Domino.DominoSystem`名前空間, 通常の`MonoBehaviour`)を作成し、Scene内の`NetworkManager` GameObjectに追加(`NetworkBootstrap`/`DominoSpawner`と同じGameObject)。以下の3つのInspector調整可能パラメータを持ちます
  - `Fallen Angle Threshold`(既定60°): `Vector3.Angle(domino.transform.up, Vector3.up)`がこの値以上で「倒れた」と判定。GameFlow側の判定(Checkpointの`Fallen Angle Threshold`)とは独立した別設定で、同じ60°から出発していますが将来別々に調整可能です
  - `Cleanup Delay`(既定3秒): 倒れた状態がこの秒数連続したら削除。**連続**である点が重要で、削除条件を満たす前に起き上がる(角度が閾値未満に戻る)と即座にタイマーをリセットします(一時停止ではなく完全なリセット)
  - `Check Interval`(既定0.2秒): 登録済みDominoの状態確認を行う間隔。毎フレームではなくこの間隔でまとめて確認することで、Domino1個ごとの重い`Update()`処理を避けています
- **登録方式**: `DominoCleanupManager`は`FindObjectsByType`等によるScene全体の検索を一切行いません。代わりに`DominoSpawner.TryPlaceDomino()`が`NetworkObject.InstantiateAndSpawn()`直後に`DominoCleanupManager.Instance.Register(spawned)`を明示的に呼び出す方式です。これにより、Step 3の`DominoLine`配下にScene配置されている20個のテスト用Dominoは(`DominoSpawner`経由でSpawnされたものではないため)自動的にCleanup対象から除外されます
- **Phase連動**: `Update()`内で`GameFlowManager.Instance.CurrentPhase == GamePhase.BUILDING`のときのみ「倒れたかどうか」の判定・タイマー進行を行います。BUILDING以外(READY/RUNNING/SUCCESS)の間は、登録済み全Dominoの`FallenSince`(倒れ始めた時刻)を`-1`(未倒壊扱い)にリセットし続けるだけで、削除は一切行いません。BUILDINGに戻った時点で、まだ倒れていれば0秒からCleanup Delayの計測が再開されます
- **Host専用ガード**: `DominoCleanupManager`は`NetworkBehaviour`ではなく通常の`MonoBehaviour`のため、`Update()`冒頭で`NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer`を確認し、Host以外では一切動作しません(Step 6の`DominoSpawner`と同じガードパターン)。Despawn(`NetworkObject.Despawn(true)`)はHost/Server上でのみ呼ばれ、Clientは独自のCleanup判定を一切行いません(Clientでも`DominoCleanupManager`インスタンス自体は存在しますが、上記ガードにより`Update()`の実処理が最初の行で必ず`return`します)
- **GameFlowとの責務分離**: `DominoCleanupManager`は`GamePhase`/`StartTriggered`/`CheckpointProgress`のいずれも一切読み書きしません(読み取るのは`GameFlowManager.Instance.CurrentPhase`のみ)。逆に`GameFlowManager`側にもCleanup関連のコードは一切追加していません
- **失効した参照の自動除去**: 毎回の`Update()`スキャン時に、`NetworkObject == null || !NetworkObject.IsSpawned`のエントリを追跡リストから取り除きます。何らかの理由でDominoが他の経路でDespawn/破棄された場合でも、参照が残り続けることはありません
- `DominoSpawner.cs`を変更: `TryPlaceDomino()`が`InstantiateAndSpawn()`の戻り値(`NetworkObject`)を受け取り、`DominoCleanupManager.Instance.Register()`へ渡すよう変更(Cleanup非導入時と比べてValidationロジック自体には変更なし)

### 動作確認済み

Host自身のPlayerでの操作、および`DominoSpawner.TryPlaceDomino()`の直接呼び出し(Step 5から続く確立済みの代理検証手法)を組み合わせ、Unity Editor上のPlay Modeで確認しました。

- **単体の倒れたDomino**: 傾斜配置で完全に倒れたDominoが、Cleanup Delay(検証時は数秒〜数百秒へInspector値を一時的に変更し、余裕を持って確認)経過後に正しくNetwork Despawnされ、Host/Client双方のScene(`NetworkObject`一覧)から消えることを確認
- **起立したDominoは削除されない**: 垂直配置で起立したままのDominoが、Cleanup Delayを大きく超える時間が経過しても削除されないことを確認
- **一時的な傾き(Reset挙動)**: `Rigidbody.rotation`を直接操作してDominoを70°→75°まで傾け、Cleanup Delayが満了する前に40°(閾値未満)まで戻すと、`FallenSince`が`-1`(未倒壊)にリセットされ、その後再度倒しても0秒から改めてカウントが始まることを確認(一時停止ではなく完全リセットであることを`FallenSince`の値を直接読んで確認)
- **複数Dominoの連鎖倒壊**: 複数のDominoを近接配置してChain Reactionを起こし、各Dominoが(同一フレームである必要はなく)それぞれ自分自身の`FallenSince`に基づいて個別にCleanupされることを確認。20個規模のPerformanceテスト(下記)でも同様の挙動を確認
- **BUILDING → READY(保留)**: BUILDING中に倒したDominoのタイマーが進行中の状態でREADYへ遷移させると、削除が行われないことを確認(`FallenSince`が`-1`にリセットされる実装のため、"保留中に削除される"事態が構造的に起こり得ないことをコード・実行結果の両方で確認)
- **READY → BUILDING(再開)**: 上記の状態からBUILDINGへ戻すと、Dominoが依然として倒れていれば0秒からCleanup Delayの計測が再スタートすることを確認
- **RUNNING中は完全に無効**: RUNNING中、Start→Checkpoint 1→Checkpoint 2→Goalの一連の流れで複数のDominoが倒れた状態でも、Cleanupによる削除が一切発生しないこと、かつGameFlow自身の`StartTriggered`/`CheckpointProgress`判定がCleanup導入前と同様に正しく動作し続けることを確認
- **SUCCESS中も無効**: SUCCESS到達後も同様にCleanupが発生しないことを確認
- **Cleanup後の再配置**: Cleanupで削除されたDominoと同じ座標へ、実際のE→左クリックのPlacement UIフロー(Input Systemの`QueueStateEvent`による模擬入力)を通じて新しいDominoを問題なく再配置できることを確認。この検証の過程で、複数の異なるテスト実施上の落とし穴(下記「テスト手法上の教訓」参照)を発見・整理しました
- **Multiplayer(Host + Client接続)**: `NetworkManager.ConnectedClients.Count == 2`(Host + Client)の状態でCleanupが正しく機能することを確認。Cleanup自体はHost専用ガードにより構造的にHost上でのみ実行され、Client側で独自のCleanup Timerが走ることはありません(コード上の`IsServer`ガードにより保証されるため、Client側の内部状態を直接検証する必要はないと判断しました)
- **Performance(20 Domino)**: 20個のDominoを直接Spawnし(半数を起立、半数を閾値超えの傾きで配置)、密接配置による物理連鎖も発生させた上で、過剰なログ出力やDomino単位のRPCが一切発生しないこと(`DominoCleanupManager`のコード自体にRPCが存在しないため構造的に保証)、Consoleにエラー・想定外のWarningが出力されないことを確認
- Console: Step 7の実装コード(`DominoCleanupManager`/`DominoSpawner`関連)由来のError/Warningは0件(デバッグ用に一時追加した`Debug.Log`は検証完了後に削除済み)

### テスト手法上の教訓(今回発見・対処した誤検知の原因)

検証の過程で、Domino配置後にDominoがSpawnされず、Consoleにも何のログも出ないように見える事象が複数回発生しましたが、詳しく調査した結果、いずれも**実装のバグではなく、テスト手法側の問題**であることが判明しました。今後の自動検証のために記録しておきます。

1. **`FindFirstObjectByType<PlacementController>()`が意図しない方のPlayerを返すことがある**: Host + Client接続時、Scene内には`PlacementController`インスタンスが2つ存在し(自分の分＋相手の分)、非OwnerのインスタンスはStep 4時点の設計通り`OnNetworkSpawn()`で`enabled = false`にされています。`FindFirstObjectByType`はどちらを返すか保証されないため、無効化されている側を掴んでしまうと、入力をいくら送っても何も起こりません。`IsOwner == true`の個体を明示的に選別する必要があります
2. **`EditorApplication.isPaused`が意図せず`true`になっていた**: 長時間の自動検証中に何らかの操作でPlay ModeがPauseされ、`Time.frameCount`が完全に停止していたことがありました。Pause中は`Update()`が呼ばれないため、キー入力を送っても一切反応しません。`Application.isPlaying`だけでなく`EditorApplication.isPaused`も確認する必要があります
3. **Stage範囲外の座標でのテスト**: Ground/Stageの実際の範囲を超えた座標でテスト用に直接配置すると、Dominoが正常にSpawnした直後に何らかの理由で消失する(範囲外での物理的な不整合等)ことがありました。実際のPlacement UIから離れた極端な座標は避け、Stage内の座標で検証することを推奨します

これらはいずれも自動テストの実施方法に起因するものであり、`PlacementController`/`DominoSpawner`/`DominoCleanupManager`本体のロジックには問題がないことを、上記1〜3を踏まえた再検証(Cleanup Delayを一時的に長く設定した上での、正規のRPC経路を通した配置)で確認しています。

### 未実装/確認できなかった事項

- **Client自身の操作による直接確認**: Step 4から持ち越しの制約(MPPM仮想Playerウィンドウへのキーボード入力自動送信不可)により、Client自身が実際にEキー→クリックでDomino配置し、それがCleanup対象になる様子を自動テストできていません。コード上、Cleanup対象になるかどうかはSpawn経路(`DominoSpawner`経由かどうか)のみに依存し、誰が配置したか(Host/Client)には依存しないため、Host自身の配置での確認結果がClient配置にも同様に当てはまるはずです
- Object Poolingは今回導入していません(ユーザー様のご指示通り、まず通常のNetwork Spawn/Despawn Lifecycleの動作確認を優先しました)

### 将来確認が必要な事項

- **Clear Judgementの「まとめて追いつく」既知の課題**: `GameFlow.md`の第5章に記録した通り、Step 7では意図的に変更していません
- 大量(数百個規模)のDominoを扱うようになった場合、Object Poolingの導入や、Step 5から持ち越しのUnityTransport受信キューサイズ(既定128)の見直しが必要になる可能性があります(いずれも今回のStepの対象外)
- `Fallen Angle Threshold`/`Cleanup Delay`/`Check Interval`はいずれもStageごとの調整余地があります。今回は既定値(60°/3秒/0.2秒)のみで検証しました

---

## Step 7.1: Regression Fix / Stability Check

Step 7完了後、ユーザー様ご自身の実プレイにより2件の問題が報告されました。Clear Judgement厳密化・FAILED Phase等のStep 7.5範囲には一切着手せず、この2件の調査・修正のみを行いました。「READY中はPlacement不可」は既存の意図した仕様(Step 6)であることを確認し、変更していません。

### 問題1: READY → RUNNINGへ遷移しない(Regression)

**原因**: Step 7で追加したBUILDING中のDomino自動Cleanupにより、Start/Checkpoint/Goal Area内で倒れたまま放置されたDominoがNetwork Despawnされるようになりましたが、UnityはColliderが破棄される際に`OnTriggerExit`を確実には発火させないため、`DominoFallenZone._dominoesInside`(Zone内にいるDominoのTransform参照を保持するリスト)に**破棄済みのTransform参照**が残ってしまうケースがありました。この状態でRUNNINGが開始されると、`DominoFallenZone.OnRunStart()`が破棄済み参照の`.up`にアクセスして`MissingReferenceException`を送出し、`GameFlowManager.BeginRun()`が`SetPhase(GamePhase.RUNNING)`に到達する前に処理が中断していました。RPCハンドラ内の未処理例外はNGOが内部でログするのみで、`GamePhase`はREADYのまま変化しないため、「Fキーを押しても反応がない」という形で症状が現れていました(実際にはConsoleに`MissingReferenceException`のError、および`[Netcode] Unhandled RPC exception!`が出力されていました)。

**修正内容**: `DominoFallenZone.cs`のみを変更しました。`OnRunStart()`の先頭で`_dominoesInside.RemoveWhere(t => t == null)`により破棄済み参照を除去し、`IsFallen()`自体にも`if (t == null) return false;`のnullガードを追加しました。`DominoCleanupManager`側のコードは一切変更していません(Cleanup自体は正しく動作しており、問題はGameFlow側が「削除されたDominoの後始末」に対応していなかった点にありました)。Start/Checkpoint/Goalは共通の基底クラス`DominoFallenZone`を継承しているため、この修正は3種のZoneすべてに適用されます。詳細な原因分析は`GameFlow.md`第4章、`DominoLifecycle.md`第7.5章にも記録しました。

**Host側テスト結果**: BUILDING中にStart Area内で倒したDominoをCleanupにより実際にDespawnさせた上で、両Player(Host+Client、`NetworkManager.ConnectedClientsIds`経由の代理呼び出し)をStart Areaに揃えてREADYへ遷移させ、genuine(実際のInput System経由の)Fキー入力で`RequestStartChallengeRpc`を呼び出したところ、修正前は無反応(Console上に例外)、修正後は`GamePhase`が正しく`RUNNING`へ遷移し、Consoleにも`[GameFlowManager] Phase -> RUNNING`以外のログが一切出ないことを確認しました。続けてCP1→CP2→Goalまで一連の流れを検証し、`SUCCESS`まで正常に到達することも確認しました。

**Client Request側の確認結果**: RPC(`RequestStartChallengeRpc`)自体の宣言(`[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]`)はStep 6から変更しておらず、Host/Clientどちらからでも呼び出せる設計を維持しています。ただしStep 4以降持ち越しの制約により、Client自身の実機キー入力を自動テストする手段は今回も見つかっていません。開発者ご自身でのClient側F操作の実機確認を推奨します。

### 問題2: Live Preview MarkerがPlayer周囲を不自然に振れ回る

**原因**: `PlayerStats.rotationSpeed`が`720度/秒`と非常に高速に設定されており、Playerが移動キーを押しながらCameraを大きく動かすと、`PlayerMotor.ApplyMovement()`がCamera相対の移動方向へ向けてPlayerの体をほぼ瞬時に回転させます。Step 6.1時点の`PlacementController.ComputeGroundAnchor()`はこの`transform.forward`を**無条件かつ毎フレームそのまま**Anchor計算に使っていたため、Playerの体が高速回転する場面でLive Preview Markerがそれにつられて素早く振れ回り、「Markerがぐるぐる回る」ように見えていました。Camera Transform自体を直接Anchorの基準にしていたわけではなく(Anchorの基準は最初からPlayer基準のままです)、あくまでPlayer本体の高速回転を減速なしに反映していたことが原因でした。

調査の過程で、別の要因も1件発見しました。`groundMask`が既定で「すべてのLayer」を対象にしていたため(Ground/Dominoが共に`Default` Layerだったため)、Live Previewの地面検出Raycastが既に設置済みの他のReal Dominoにも当たり、Anchor Y座標がDomino1個分(約1m)浮いてしまう可能性がありました。こちらは「ぐるぐる回る」の直接原因ではありませんが、ユーザー様のご指示に基づき合わせて調査・修正しました。

**修正前の計算**: `ComputeGroundAnchor()`は`transform.forward`をそのまま`Vector3.ProjectOnPlane`した値を毎フレーム使用しており、Playerの体の回転速度をそのまま継承していました。

**修正後の計算**: `PlacementController`に減速版の方向ベクトル`_previewForward`を追加し、Playerの生の向き(`targetFlatForward`)へ`Vector3.RotateTowards()`で`previewFollowSpeed`(既定240度/秒、Inspector調整可能。Player本体の720度/秒よりあえて低く設定)を上限に追従させるよう変更しました。Lock/Cancel/Confirm/Phase変化(BUILDING以外への遷移)でLive Previewが再開する瞬間は`_previewForwardValid`をリセットし、その場でPlayerの実際の向きへ即座に一致させます(遅れて追いつくような不自然な動きが再開直後に出ないようにするため)。X/Z位置・Y座標・地面法線の計算方法自体は変更していません。

Raycast対象については、`Domino.prefab`専用の新しいUnity Layer`"Domino"`を追加し、`PlacementController.groundMask`(既定値)から明示的に除外しました。Physics Layer Collision Matrixは変更しておらず、`Domino`↔`Domino`・`Domino`↔`Default`(Ground/Player)の物理衝突は引き続き有効なままです(Raycastの対象からのみ除外)。

**Raycast対象**: Ghost Domino・Placement Markerはいずれも`Collider`を持たない完全ローカルの見た目専用オブジェクトのため、そもそもRaycastに拾われることはありません(`Architecture.md`第6章参照)。Player自身の自己Hitも、`forwardDistance`(1.2m)によりRaycast原点が既にPlayer本体のCapsule範囲外にあるため、通常のPlayerサイズでは発生しないことを確認しています。

**Parent/World Transformの扱い**: Marker(`_markerInstance`)は`Instantiate(placementMarkerPrefab)`でParentを指定せずSceneルートに生成されており、`SetPositionAndRotation()`はWorld座標系で扱われています。Playerへの誤ったParent付けや二重Transformの問題は見つかりませんでした。

**Preview/Lock/Re-Lockテスト結果**: Playerを開けた場所へ配置しCamera Yawをリセットした上で、移動キーを押しながら大きなMouse Deltaを与えてPlayer本体を高速回転させ、`_previewForward`が最終的にPlayerの真の向きへ正しく収束すること(発散しないこと)を確認しました。また、設置済みのDominoの真上にAnchorが来る状況を作り、修正後は`_anchorGroundPosition.y`が正しく地面の高さ(Domino頂点の高さではなく)になることを、Playerを意図的に地面から浮かせた状態でも同様にAnchor Yが地面の高さに収束することの2通りで確認し、Raycast自体が(Fallback値に頼らず)正しく機能していることを確認しました。Preview(自動表示・追従)→Right Click Lock(固定)→E(Placement Mode開始)→左クリック確定、という一連の流れをgenuine Input System経由の操作で実行し、Real Dominoが期待通りの位置(角度0.03°、正しく起立)で生成されることを確認しました。

**発見・修正時の注意点(このStepでの反省点)**: 修正作業の途中、`LayerMask`型のSerializedFieldを`SerializedProperty.FindPropertyRelative("m_Bits")`経由で書き換えようとした際、実際には何も書き込まれず`m_Bits: 0`(Nothing = 何も検出しない設定)のままPrefabへ保存してしまうミスが一度ありました。`LayerMask`は`SerializedPropertyType.LayerMask`という特別な型として扱われ、`m_Bits`という子プロパティ経由ではなく**プロパティ自身の`.intValue`に直接アクセスする**必要があることが原因でした。この間違いに気づかず一度Play Modeでテストしてしまいましたが、たまたまPlayerもGroundもY=0の平地だったため、Raycastが完全に無効(`groundMask=0`)でも「地面のY座標にPlayer自身のY座標を代入するFallback処理」が偶然正しい値と一致し、不具合が発覚しませんでした。再発防止のため、Prefab上のフィールドをスクリプトで書き換えた際は、**保存後に必ずPrefabを再読み込みして値を読み戻す**、または**傾斜や高低差のある状況でテストする**(平地での一致に頼らない)ことを徹底する必要があります。この間違いは検証の途中で自己発見し、正しい方法(プロパティ自身の`.intValue`)で修正・再検証済みです。

### Regression確認

- **Placement**: Preview/Lock/Re-Lock/E/Escape/確定後の位置一致、いずれも正常であることを確認
- **Cleanup**: BUILDING中の単体Domino自動Despawn(Cleanup Delay経過後)が引き続き正常に動作することを確認
- **Network Domino**: Domino Physics・Chain Reaction挙動に変更がないことを確認(Layer変更はRaycast対象のみに影響し、Physics Collision Matrixは変更していません)
- **GameFlow**: BUILDING → 全PlayerがStart Area → READY → F → RUNNING → CP1 → CP2 → Goal → SUCCESSの一連の流れが正常に動作することを確認。RUNNING中のPlacement不可・Cleanup無効も引き続き正常
- **SUCCESS**: 到達後もCleanupが発生しないことを確認(Step 7から変更なし)

### Console

- Error: 0件(修正前に発生していた`MissingReferenceException`・`[Netcode] Unhandled RPC exception!`は、修正後の再検証で発生しないことを確認)
- Warning: 0件(Step 7から持ち越しのUnityTransport Receive Queue警告等は今回のテスト範囲では発生せず)

### 今回変更しなかったもの

Clear Judgementの厳密化、FAILED Phase、Failure判定、Time Attack、Result UI、Retry/Restart、Lobby/Relay、RUNNING中のPlayer移動固定・Overview Cameraには一切着手していません。これらはStep 7.5以降の範囲です。

---

## Step 7.5: Strict Clear / Failure / Reset

Step 7.5では、Step 6以来の「Zoneが一度Satisfiedになれば、あとから前段の条件が揃った時点でProgressが追いつく」というClear判定の緩さ(`GameFlow.md`に記録していた既知の課題)を廃止し、`GamePhase.FAILED`を新設した上で、正しい順序判定・Chain Stop Failure判定・Failure後のDomino一括削除とProgress Reset・Ready Re-armまでを実装しました。Placement/Marker/Ground Raycast(Step 7.1)、BUILDING中の個別Cleanup(Step 7)、Domino Physics、Network Authorityには一切変更を加えていません。「READY中はPlacement不可」も既存仕様のまま維持しています。

### 実装済み

- `GamePhase.cs`に`FAILED`を追加(`BUILDING`/`READY`/`RUNNING`/`SUCCESS`/`FAILED`の5値)
- `GameFlowManager.cs`を全面的に書き換え
  - `_orderedZones`(`[Start, CP0, CP1, ..., Goal]`)と`_expectedZoneIndex`を新設。旧`OnZoneStateChanged()`(全Zone再スキャン)を廃止し、`OnZoneFallen(DominoFallenZone zone)`に置き換え。発火したZoneが`_expectedZoneIndex`と一致すれば1つ進め、一致しなければ即`TriggerFailure()`
  - Chain Stop Failure監視用の`Update()`を新設(`StartTriggered`後のみ動作)。`DominoCleanupManager.AnyTrackedDominoActive()`を`failureCheckInterval`(既定0.25秒)おきに呼び、全Dominoが`failureSettleDuration`(既定1.75秒)連続で静止したら`TriggerFailure()`
  - `TriggerFailure()` → `SetPhase(FAILED)` → `failureDisplayDuration`(既定1.75秒)待機 → `DominoCleanupManager.DespawnAllRegistered()`完了待ち → `ResetRunState()`(Progress/StartTriggered/ExpectedZoneIndex/全ZoneのRunStart状態をリセット) → `_readyRearmPending = true` → `SetPhase(BUILDING)` → `ReevaluateReady()`、という一連のコルーチン(`FailureResetSequence()`)を追加
  - `ReevaluateReady()`に`_readyRearmPending`のガードを追加。Failure Reset後、`_playersInStartArea.Count`が一度`0`になるまで`READY`へ進めないようにした
  - `BeginRun()`/Failure Resetの両方から共通の`ResetRunState()`を呼ぶよう整理(進捗リセットロジックの重複を排除)
- `DominoFallenZone.cs`: `CheckDomino()`の通知先を`GameFlowManager.Instance.OnZoneStateChanged()`から`GameFlowManager.Instance.OnZoneFallen(this)`に変更(Zone自身の`IsSatisfied`ラッチ・`OnRunStart()`の除外ロジック自体は無変更)
- `DominoCleanupManager.cs`に2つのAPIを追加
  - `AnyTrackedDominoActive(linearVelocityThreshold, angularVelocityThreshold)`: 登録済みDominoのRigidbody速度を閾値と比較するだけの読み取り専用メソッド
  - `DespawnAllRegistered(Action onComplete)`: 登録済み全DominoをBatch Despawnするコルーチン(`despawnBatchSize`既定10個ごとに1フレーム分`yield return null`)。合わせて`TrackedDomino`に`Rigidbody`参照をキャッシュ(`Register()`時に`GetComponent`)し、`Update()`の`Transform`アクセスと同じパターンで揃えた

### 動作確認済み

Host自身の操作(genuine Input System経由のFキー含む)と`DominoSpawner.TryPlaceDomino()`の直接呼び出しを組み合わせ、Unity Editor上のPlay Modeで確認しました。テストごとに`failureSettleDuration`/`failureDisplayDuration`/`cleanupDelay`を一時的に調整し、余裕を持って検証しています。

- **正常ルート**: RUNNING→START→CP1→CP2→GOALの正しい順序でSUCCESSに到達し、SUCCESS後もDominoが4個とも削除されずに残ることを確認
- **CP2 First(Wrong Order)**: STARTの後、CP1を飛ばしてCP2で新たにDominoを倒すと即`FAILED`になり、`failureDisplayDuration`経過後に全Domino削除・Progress Reset・`BUILDING`復帰まで自動で完了することを確認
- **Before START**: STARTがまだ倒れていない状態でCP1のFresh Fallen Eventが発生すると`FAILED`になることを確認
- **Pre-Fallen(除外)**: BUILDING中にCP2内へ倒れた状態のDominoを事前に置いた上でRUNNINGへ入っても、即座に`FAILED`にはならず(`_excludedDominoes`により除外)、`GamePhase`が`RUNNING`のまま`CheckpointProgress=0`を維持することを確認
- **Chain Stop Failure**: STARTのみ倒し、後続のDominoを一切置かずに放置すると、`failureSettleDuration`経過後に`FAILED`へ遷移し、Reset完了まで自動で進むことを確認。`AnyTrackedDominoActive()`が実際に落下中のDominoを「Active」と検出すること自体は、物理シミュレーションが往復呼び出しの間に完了してしまい直接確認できませんでしたが、静止後に確実に`FAILED`が発生することは複数回確認しました
- **Ready Re-arm**: Failure Reset直後、`_playersInStartArea`に要素が残っている間は`BUILDING`から`READY`へ進まないこと、全ClientIdを`NotifyPlayerExitedStart`で退出させると`_readyRearmPending`が解除されること、その後再度`NotifyPlayerEnteredStart`で全員入れると`READY`へ正しく遷移することを確認
- **F → RUNNING(genuine input)**: Input SystemのQueueStateEvent経由の実際のFキー入力で`READY → RUNNING`へ遷移することを確認(Step 7.1のバグ修正が引き続き有効であることも合わせて確認)
- **Placement / BUILDING Cleanup / RUNNING時Cleanup無効**: いずれも従来通り動作することを確認(Placement ConfirmはRPC経由のため、既知のテストハーネス起因の間欠的な無反応が一度発生しましたが、直接`TryPlaceDomino()`呼び出しで根本経路は健全であることを確認済み。詳細はStep 7の記録を参照)
- Console: Error 0件

### 未実装/確認できなかった事項

- **Client自身の操作による直接確認**: 従来通りの制約(MPPM仮想Playerウィンドウへのキーボード入力自動送信不可)により、Client自身がCheckpointを間違った順序で倒す/Chainを止める操作を自動テストできていません。判定ロジック自体は「誰が倒したか」に依存しない設計のため、Host側の確認結果がClientにも同様に当てはまるはずです
- `AnyTrackedDominoActive()`が「動いている」と判定する瞬間そのものを、往復呼び出しのタイミングの都合上ピンポイントで観測できませんでした(常に「まだ0速度」か「もう静止済み」のいずれかを観測)。閾値比較のロジック自体はシンプルであり、静止後に確実にFailureが発生する結果からも機能していると判断していますが、開発者ご自身での実プレイ確認(ゆっくり途中で止まるChainを作る等)を推奨します

### 将来確認が必要な事項

- `linearVelocityThreshold`(既定0.02)/`angularVelocityThreshold`(既定0.05)/`failureSettleDuration`(既定1.75秒)/`failureCheckInterval`(既定0.25秒)/`failureDisplayDuration`(既定1.75秒)/`despawnBatchSize`(既定10)はいずれもStageの規模・Domino密度に応じた調整余地があります
- SUCCESS後のRetry/Next Stageは未実装のままです(Failure Resetとは別の仕組みとして将来実装予定)
- 大量(数百個規模)のDominoでのBatch Despawnの実際のパフォーマンスは、今回のPrototype Stage規模(数個〜20個程度)でのみ確認しています

---

## Step 7.6: RUNNING Player Constraint / Overview Camera

Step 7.6では、RUNNING/SUCCESS/FAILED中(`GameFlowManager.IsChallengeActive`)、Playerの移動をStart Area内に制限し、各Clientのローカルカメラを固定視点のOverview Cameraへ自動的に切り替える機能を追加しました。Placement/Marker/Ground Raycast(Step 7.1)、Strict Clear判定・Failure・Reset(Step 7.5)、Domino Physics、Network Authorityには変更を加えていません。

### 実装済み

- `GameFlowManager.cs`に`public bool IsChallengeActive => phase is RUNNING/SUCCESS/FAILED`を追加。Player拘束・移動基準切り替え・Overview Cameraの3箇所すべてがこの1つのプロパティを共通の判定源として参照するようにした
- `GameFlowManager.cs`に`public StartAreaZone StartArea => startArea`を追加(`PlayerMotor`がStart Areaの境界情報を参照するため)
- `StartAreaZone.cs`に`ClampToLocalBounds(Vector3 worldPosition)`を追加。`Transform.InverseTransformPoint`/`TransformPoint`を使い、Start AreaのPosition/Rotation/Scaleをすべて反映したLocal空間で、指定した座標を`Player Radius Margin`(既定0.5m)ぶん内側に縮小したBoxCollider範囲内へクランプする
- `PlayerMotor.cs`の`ApplyMovement()`を変更
  - `IsChallengeActive`中は移動基準をCamera相対からPlayer自身の向き(`transform`)へ切り替え(BUILDING/READY中の操作感は無変更)
  - `IsChallengeActive`中は`ConstrainToStartArea()`で水平方向の目標速度を制限。次のFixedUpdateでの到達予定位置を`ClampToLocalBounds()`でクランプし、そこから逆算した速度を採用することで、Teleportを一切使わずに境界での自然な減速・スライドを実現。RUNNING開始時にPlayerが境界のすぐ外側にいた場合も同じ処理が自然に押し戻すため、専用の補正コードは持たない
- `ThirdPersonCamera.cs`にOverview Camera機能を追加
  - `Camera.fieldOfView`を制御対象に追加(既存のPlacement Viewブレンドは位置・回転のみでFOVは触れていなかった)
  - `_overviewBlend`(`Mathf.MoveTowards`、既定`overviewBlendSpeed=2`で約0.5秒遷移)を追加し、`IsChallengeActive`かつ`OverviewCameraPoint.Instance`が存在する間、Position/Rotation/FOVを`OverviewCameraPoint`の値へブレンド
- `OverviewCameraPoint.cs`(新規, `Domino.CameraSystem`名前空間)を作成。Stage側に配置するTransform + `Field Of View`のみを持つマーカーコンポーネント。`ThirdPersonCamera`はこの値をそのまま参照するだけで、Overview視点の実体はStage側の配置だけで決まる
- Scene内に`CameraPoints`(空の親)→`OverviewCameraPoint`を新規配置。Start→CP1→CP2→Goalのルート全体を見渡せる位置・角度に設定(`Field Of View = 65`)

### 動作確認済み

Play Mode上で、Phase遷移(`BeginRun()`/`TriggerFailure()`の直接呼び出し、および一部genuine Fキー入力)と`execute_code`によるスクリーンショット・状態読み取りを組み合わせて確認しました。

- **Overview Cameraへの遷移**: BUILDING中はScreenshotで通常のThird Person(Playerに肉薄した視点)であることを確認。RUNNINGへ遷移後、`_overviewBlend`が`0→1`まで正しく進み、Screenshotで実際にStage全体(Start/Checkpoint/Goal)を見渡せる俯瞰視点へ切り替わっていることを視覚的に確認。`Camera.fieldOfView`が`OverviewCameraPoint`の設定値(65)へ正しく反映されることも確認
- **Player Constraint(境界での停止)**: `_moveAxis`を直接操作してPlayerをStart Areaの境界(Z方向)へ向けて移動させ続けたところ、境界(実測値: Zone中心から4mの半分=2m、`Player Radius Margin`0.5m差し引いた**Z=-8.50で正確に一致**)で完全に停止し、それ以上は一切外側へ進まないことを複数回の追加入力で確認。座標がピタリと同じ値で安定し、Jitter(振動)は発生しませんでした
- **Player Constraint(範囲内の自由移動)**: 境界から離れる方向への移動は制限なく機能することを確認(Z=-8.50から-10.05まで問題なく戻れた)
- **Start Dominoを倒せること**: Start Area内でPlayerをDominoへ向けて移動させ、実際に衝突・転倒させ、`StartTriggered`が正しく`True`になることを確認。移動制限の追加がStart Dominoへの到達を妨げないことを確認
- **FAILED → BUILDING復帰**: Failureをトリガーし、Reset完了(`BUILDING`)後に`_overviewBlend`が`0`、`PlayerMotor`の拘束判定(`IsConfinedToStartArea()`)が`False`に戻ること、Screenshotで実際にThird Person Cameraへ戻っていることを確認
- **既存機能回帰**: genuine Fキー入力での`READY → RUNNING`、RUNNING遷移時にLive Preview Marker(`_markerActive`)が正しく非表示になること、Console Errorが0件であることを確認

### 未実装/確認できなかった事項

- **Client自身の操作による直接確認**: 従来通りの制約により、Client自身がRUNNING中にStart Area境界へ向かって歩く操作や、Overview Cameraの見え方をClient画面上で直接確認することはできていません。Constraint・Camera切り替えロジックはいずれも各PlayerのOwner Authority/ローカル処理(`IsOwner`・各Clientの`ThirdPersonCamera`)にのみ依存し、「誰が操作しているか」に関係なく同一のコードパスを通るため、Host側の確認結果がClientにも同様に当てはまるはずですが、**開発者ご自身での複数Player実機確認を推奨します**
- Overview CameraのPosition/Rotation/FOVは、今回のPrototype Stageの1ルートに対して手動調整した1セットのみを検証しています。将来的に大きく異なるレイアウトのStageを作る際は、`StageAuthoring.md`第9章の手順に沿った再調整が必要です

### 将来確認が必要な事項

- Camera Auto Tracking・Dynamic Zoom・Cinematic Camera Path等は今回実装していません(意図的にStep 7.6の対象外としました)
- RUNNING中にStart Area内で複数Playerが密集した場合の押し合い・Constraint同士の相互作用は、2人プレイの範囲でのみ確認しています(将来4人プレイ対応時に再確認が必要です)
- `overviewBlendSpeed`(既定2)は今回のPrototype Stageの距離感で違和感のない値を選んだもので、Stageの規模によっては調整が必要になる可能性があります

---

## Step 7.7: Gameplay Control / Placement UX / Ready Re-arm Fix

Step 7.7では、Step 7.6完了後の実プレイフィードバックを受け、3件の改善(Ready Re-armの根本バグ修正、RUNNING中Screen-relative Movement、Placement UXのCamera Aim化)と、Prototype CharacterへのNose追加を行いました。Strict Clear判定・Failure検出・Failure Reset(Step 7.5)、Start Area拘束・Overview Camera自体の仕組み(Step 7.6)には変更を加えていません。

### 実装済み

**Ready Re-arm根本修正**
- `StartAreaZone.cs`に`ContainsWorldPoint(Vector3)`を追加。`OnTriggerEnter`/`OnTriggerExit`の履歴に一切関係なく、指定した座標が今この瞬間BoxCollider範囲内にあるかどうかだけを判定する
- `GameFlowManager.cs`に`ReconcilePlayersInStartArea()`を追加し、`ReevaluateReady()`の冒頭で必ず呼ぶようにした。現在接続中の全PlayerについてActual位置と`_playersInStartArea`の追跡状態を突き合わせ、ズレていれば追加/削除して自己修復する。Trigger Eventによる即時反映(Fast Path)は維持しつつ、Ready判定の直前に必ずGround Truthで検証し直す形にした

**RUNNING中Screen-relative Movement**
- `PlayerMotor.ApplyMovement()`を変更: Step 7.6で導入した「confined中はPlayer自身の向きを移動基準にする」分岐を撤回し、常に現在アクティブな`ThirdPersonCamera.transform`(BUILDING中はThird Person、RUNNING/SUCCESS/FAILED中はOverviewへ自動ブレンド)を基準にするよう統一。Cameraの地面投影Forwardがほぼ0になる(真下/真上を見る)場合は、Forwardと直交するUpベクトルの地面投影をFallbackとして使用
- Character Rotationの仕様(移動入力がある時だけ移動方向を向く)は無変更

**Placement UXのCamera Aim化**
- `PlacementController.ComputeGroundAnchor()`を全面的に再設計: Player前方距離基準から、Camera(画面中央)からのRaycast + Player中心からの`maxPlacementRange`(既定2.7m)でのRange Clampへ変更
- Step 7.1で追加した`_previewForward`/`previewFollowSpeed`(Player向きの急変化を減速させる仕組み)は、Anchorの基準がPlayer向きに依存しなくなったため削除
- 新しいUnity Layer`"Player"`を追加し、`Player.prefab`に割り当て。`PlacementController.groundMask`から`"Domino"`(Step 7.1)に加えて`"Player"`も除外(Camera Aimの光線が自分自身のCapsuleに当たってしまうのを防ぐため)。Physics Layer Collision Matrixは変更しておらず、Player↔Domino/Ground/Player同士の物理衝突は引き続き有効
- `PlacementController.OnGUI()`を追加し、BUILDING中のみ画面中央に`+`のReticleを表示(Canvas/UI Toolkit不使用の最小実装)

**Prototype Character Nose**
- `Player.prefab`の`Visual`配下に`Nose`(Sphere、Collider無し、`NoseMaterial`使用)を追加。ローカル座標`(0, 0.6, 0.5)`、`PlayerMotor`が前進方向として扱う+Z軸と一致

### 動作確認済み

Play Mode上で、Phase遷移の直接操作とgenuine Input System入力(`QueueStateEvent`)を組み合わせて確認しました。

- **Ready Re-arm(3サイクル)**: BUILDING→READY→RUNNING→FAILED→BUILDING→(全員Start Area退出)→Re-arm解除→(全員再入場)→READYという一連の流れを、Host自身の**genuine Teleportによる物理的な入退場**(NetworkTransform.Teleport、実際のOnTriggerEnter/Exitイベント経由)で3回連続実行し、いずれも正しくREADYへ到達することを確認。合わせて、`ReconcilePlayersInStartArea()`が両方向に正しく機能することを個別に確認: (1) 実際にはいないPlayerの追跡エントリ(Proxy呼び出しで人為的に混入させたもの)が次のReady判定で正しく除去されること、(2) 物理的にはStart Area内にいるPlayerの追跡エントリを人為的に削除しても、次のReady判定で正しく復元されること
- **Screen-relative Movement**: RUNNING中、Overview Cameraのground-projected ForwardがおよそStage奥方向(+Z)を向いている状態で、`_moveAxis=(0,1)`(W)を与えるとPlayerが+Z方向(画面上方向)へ、`_moveAxis=(1,0)`(D)を与えると+X方向(画面右方向)へ正しく移動することを確認。移動のたびにCharacterの向きも移動方向へ回転することを確認。Start Area Constraintは同じ座標(境界Z=-8.50)で引き続き正しく機能することを確認(X/Z独立軸のClampがそれぞれ機能し、境界に沿ったスライドも可能)
- **Placement UX**: BUILDING中、画面中央に`+`のReticleが表示されることをScreenshotで確認。Playerの向きを変えずにCameraだけを回すと(`playerForward`は不変のまま)Marker位置が正しく変化し、Player中心から`maxPlacementRange`ちょうどの距離でClampされることを数値上確認。Right Click LockとE-only Lock、Lock後にCameraを回してもMarker位置が固定されたままであること、Ghost/Real DominoがLock位置と一致することをそれぞれ確認
- **Nose**: `Player.prefab`のPrefab Stage上でColliderを持たないことを確認。実行時、Player前方からのScreenshotで赤いNoseが正面(+Z側)に表示されることを視覚的に確認
- **既存機能回帰**: BUILDING中のDomino Cleanup、Start→CP1→CP2→Goal→SUCCESSの正常ルート(Strict Clear判定含む)が引き続き正常に動作することを確認。Console Errorは検証全体を通じて0件

### 未実装/確認できなかった事項

- **Client自身の操作による直接確認**: 従来通りの制約により、Client自身のRUNNING中Screen-relative Movement、Placement UX(Camera Aim)を実機で直接確認することはできていません。いずれもOwner Authority/ローカル処理(`IsOwner`ガード)にのみ依存し、Host/Clientで同一のコードパスを通るため、Host側の確認結果が同様に当てはまるはずですが、開発者ご自身での実機確認を推奨します
- **`maxPlacementRange`の余白(0.3m)が実際に十分か**: 今回の検証で1回だけ、Rangeちょうどの位置での配置がHost側の距離Validationに弾かれる事象が見つかり、`maxPlacementRange`を`DominoSpawner.maxPlacementDistance`より0.3m小さくすることで解消しました。この余白が全ての実プレイ状況(高速移動中の確定等)で十分かは、開発者ご自身での実機確認を推奨します

### 将来確認が必要な事項

- `ReconcilePlayersInStartArea()`は現在接続中のPlayer数に比例したコストです。将来的にPlayer数が大きく増える場合(4人超)は、実測でのPerformance確認が必要です
- Reticleは現状固定の`+`のみで、Placement Range外を狙っている場合の視覚的フィードバック(色を変える等)は今回実装していません
- Nose用の`NoseMaterial`は仮の色・仮の形状です。正式なCharacter Modelへの置き換え時に一緒に見直す想定です

---

## Step 7.8: Front Placement / Free Domino Rotation

Step 7.8では、実プレイのゲームデザイン判断により、Step 7.7で導入したCamera Aim方式のPlacement位置決定を撤回し、Step 4〜7.1と同じ「Character正面基準」へ戻しました。代わりに、Placement Mode中にDominoの向き(Yaw)をPlayerが自由に操作できる機能を新設し、「どこに置くか(Character正面で固定)」と「どの向きに置くか(Yawで自由に選択)」を分離しました。Strict Clear判定・Failure・Reset・Ready Re-arm(Step 7.5/7.7)、Start Area拘束・Overview Camera・Screen-relative Movement(Step 7.6/7.7)には一切変更を加えていません。

### 実装済み

- `PlacementController.ComputeGroundAnchor()`をStep 7.7以前の実装へ戻し、`Player Position + Player Forward * placementDistance`→Ground Raycastという計算に復帰。Camera Aim専用だった`maxPlacementRange`/`cameraAimRange`フィールド、画面中央Reticleの`OnGUI()`はいずれも削除
- `PlacementController`に`rotateLeftKey`(既定A)/`rotateRightKey`(既定D)/`yawRotationSpeed`(既定135度/秒)を追加。Placement Mode中(`_isPlacing == true`)、A/Dの`isPressed`を毎フレーム確認し、`_yawOffsetDegrees`(float、0〜360への正規化や角度制限は一切なし)を加減算する`ApplyPlacementRotation()`を新設
- `EnterPlacementMode()`で`_yawOffsetDegrees = 0`・`_placementBaseRotation = _anchorFlatRotation`(Character正面)をリセット。`ExitPlacementMode()`(Confirm/Cancelいずれも通る共通処理)でも`_yawOffsetDegrees = 0`にリセットするため、次のPlacement Mode開始時は必ずCharacter正面から始まる
- `GhostDomino.cs`に`SetBaseRotation(Quaternion)`を追加。既存のWobble計算(`_timer`・`Quaternion.AngleAxis(wobbleAngle, Vector3.right)`)には一切手を加えず、外部(`PlacementController`)がYaw込みの`_baseRotation`を随時更新できるようにしただけで、YawとWobble Tiltがローカル軸基準で自然に合成されるようにした
- `PlacementMarker.prefab`のMeshを`Cylinder`から`Cube`へ変更し、`PlacementMarker.cs`のFieldを`size`(円直径)から`width`/`depth`(既定0.45/0.25、実際のDomino 0.35×0.15に近い長方形)へ変更。Lock中はYaw操作のたびに`ApplyPlacementRotation()`がMarkerの回転(位置は不変)も更新する
- `ConfirmPlacement()`自体は無変更(元々`_ghostDomino.CurrentRotation`をそのままRPCへ渡す設計だったため、Yaw+Wobble Tilt込みの最終回転が自動的にそのまま送信される)

### 動作確認済み

Play Modeで、Phase遷移の直接操作とgenuine Input System入力、および`PlacementController.Update()`の直接呼び出し(下記「テスト手法上の教訓」参照)を組み合わせて確認しました。

- **Character正面基準への復帰**: Cameraのみを大きく回転させても(`playerForward`は不変)、Anchor位置が完全に不変であることを数値上確認。Player位置(0,0,0)・Forward(0,0,1)に対し、Anchorが常に`placementDistance`(既定1.2m)先の(0,0,1.2)になることを確認
- **Yaw Rotation**: `_yawOffsetDegrees`が0から開始し(Initial Yaw = Character正面)、A/Dで正しく加減算されること、130フレーム分の連続回転で290°を超えても特に制限なく回転し続けること(360°以上の自由回転)を確認
- **Wobble Tiltとの合成**: `GhostDomino._baseRotation`のY成分が`_yawOffsetDegrees`と常に一致すること(Yawが正しくGhostへ伝わっている)、`CurrentRotation`(Wobble込み)はこれと異なる値になること(Wobbleが正しく重ねて適用され続けている)を確認。MarkerのY回転も`_yawOffsetDegrees`と一致することを確認(Yaw+Wobbleが正しく分離されている証拠)
- **Confirm**: Ghostの`CurrentRotation`(Yaw 291.71° + Tilt 23°相当のEuler)をそのまま`DominoSpawner.TryPlaceDomino()`へ渡した結果、Real Dominoが寸分違わず同じ回転(Euler 23.00, 291.71, 0.00)でSpawnされることを確認。自動補正が一切行われていないことの直接証拠
- **Yaw Adjustment Reset**: Confirm後、`_yawOffsetDegrees`が`0`に戻ることを確認
- **Reticle削除**: `PlacementController`に`OnGUI`メソッドが存在しないこと(削除済み)をReflectionで確認
- **既存機能回帰**: BUILDING中のDomino Cleanup、Start→CP1→CP2→Goal→SUCCESSの正常ルート(Strict Clear判定含む)が引き続き正常に動作することを確認。Console Errorは検証全体を通じて0件

### テスト手法上の教訓

検証の過程で、Input Systemの`QueueStateEvent`でA/Dキーの「押しっぱなし」状態(`isPressed`)を模擬しても、`PlacementController.Update()`が実際に呼ばれるタイミングまでにその状態が失われてしまい、`_yawOffsetDegrees`が加算されないという事象が発生しました。`Keyboard.current.dKey.isPressed`を直後に確認すると`True`が返るにもかかわらず、Unity自身のフレームスケジューリングで`Update()`が呼ばれるまでの間に模擬入力の状態が失われている(この環境固有の制約)ことが原因と判断しました。これはこれまでのStepで確認してきた「1回のQueueStateEventによる模擬Hold状態がTool呼び出しの往復時間をまたいで安定して継続するとは限らない」という既知の制約の一種です。`PlacementController.Update()`をReflection経由で直接呼び出し、`isPressed=True`を確認した直後に同期的に実行することで、ロジック自体が正しく動作することを確認しました(`yawOffset`が期待通りに`135度/秒 × Time.deltaTime`ぶん加算されることを確認)。実際のプレイヤーによる本物のキー入力はUnityのInput Systemへ継続的に供給されるため、この制約が実プレイに影響することはありません。

### 未実装/確認できなかった事項

- **Client自身の操作による直接確認**: 従来通りの制約により、Client自身のPlacement操作(Character正面基準・Yaw回転)を実機で直接確認することはできていません。`IsOwner`ガードによりHost/Clientで同一のコードパスを通るため、Host側の確認結果が同様に当てはまるはずですが、開発者ご自身での実機確認を推奨します
- 上記の理由により、**genuine(模擬ではない)Input Systemの`isPressed`ポーリングによる連続回転の様子を、実際のフレームスケジューリング下で直接観測することはできませんでした**。ロジック自体の正しさはReflection経由の直接呼び出しで確認済みです

### 将来確認が必要な事項

- `yawRotationSpeed`(既定135度/秒)は仮の値です。実際にプレイして「速すぎる/遅すぎる」と感じた場合は調整が必要です
- Marker Footprint(Width 0.45 / Depth 0.25)のサイズ感も仮のものです。実際のStageで密に配置した際の見やすさは、開発者ご自身での確認を推奨します

---

## Step 7.8.1: GameFlow Regression Restoration

開発者から、「Step 7.7までは動作していたStart Area判定/READY/F→RUNNING/Overview Cameraが、Step 7.8時点の実機操作では動作しない」との報告があり、Placement System(`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`、Step 7.8で承認済みのUX)には一切手を加えないという制約のもとで調査しました。結論として、**コードレベルの回帰(Regression)は見つかりませんでした**。`git diff 67ec5c8..ce729df`(Step 7.7→Step 7.8)は`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`/Docsのみを含み、`GameFlowManager.cs`/`StartAreaZone.cs`/`ChallengeInput.cs`/`ThirdPersonCamera.cs`/`OverviewCameraPoint.cs`/`Player.prefab`はいずれも1バイトも変化していません。このStepでの最終的なコード差分は**ゼロ**です(`git diff --stat`で確認)。

### 調査方法

コードの差分が無いことだけをもって「無関係」と判断せず、以下をPlay Modeで実機的に検証しました(開発者からの「Reflectionでメソッドを直接呼んで成功しただけではRegression Passとしない」というご指示に従い、可能な限り本物の仕組みを経由させています)。

- **Player.prefabの構成**: `ChallengeInput`(enabled=True, IsOwner=True)、`CapsuleCollider`(isTrigger=False)、Tag=`Player`、Layer=`Player`(9)を確認
- **StartAreaZone(Scene上の`StartArea`)の構成**: `BoxCollider`(isTrigger=True, enabled=True)、Tag=`Untagged`、Layer=`Default`(0)を確認
- **Physics Layer Collision Matrix**: `Player`レイヤーと`Default`/`Domino`/`Player`レイヤーの間で`IgnoreCollision`が すべて`False`(=衝突・Trigger判定は妨げられていない)ことを確認
- **`GameFlowManager`のZone参照**(`SerializedObject`経由): `startArea`→`StartArea`、`goalArea`→`GoalArea`、`checkpoints[0]`→`Checkpoint_01`、`checkpoints[1]`→`Checkpoint_02`、すべて非NULLかつ重複無しを確認
- **本物のUnity Trigger callback経由でのStart Area検知**: `NetworkTransform.Teleport()`でHost Playerを物理的にStart Area範囲内へ移動させ、`StartAreaZone.OnTriggerEnter`→`GameFlowManager.NotifyPlayerEnteredStart()`が実際に発火し、`_playersInStartArea`が正しく更新されることを確認(Reflectionで直接メソッドを呼んだのではなく、Unity自身のPhysics Engineが発火させたイベントを観測)
- **本物のInput System経由でのF→RUNNING**: `InputSystem.QueueStateEvent`でF キーイベントをキューし、Unity自身の`Update()`スケジューリングが`ChallengeInput.Update()`→`wasPressedThisFrame`→`RequestStartChallengeRpc()`→`GameFlowManager.BeginRun()`→`SetPhase(RUNNING)`という一連の経路を実際に通ることを、Console上の`[GameFlowManager] Phase -> RUNNING`ログで複数回確認
- **Overview Cameraの実際の遷移**: RUNNING中、Main Cameraの実際のPosition/Rotation/FOVが`OverviewCameraPoint`の設定値(`(-8,12,-14)`, `(40.6,0,0)`, FOV 65)と一致することを確認。READY/BUILDINGに戻るとThird Personカメラ(Player追従、距離約4.7)へ正しく復帰することも確認
- **FAILED→BUILDING Reset、Ready Re-arm Fix(Step 7.7)の健全性**: Player がStart Areaに留まったままFAILEDへ遷移させ、`_readyRearmPending=True`によって即READYへ戻らないこと(Step 7.7の修正が退行していないこと)、Playerが一度退出→再入場することで正しくREADYへ戻ることを、**3回連続の Attempt サイクル**で確認(すべて`[GameFlowManager] Phase -> ...`ログで裏付け)
- 上記すべてのテストを通じて、Console上のError/Warningは**0件**

### なぜStep 7.8ではなく、より詳しい調査が必要だったか(参考)

上記の検証を1回目に試みた際、2回目以降のF キー入力が`wasPressedThisFrame`を検出できないという事象が再現しました。一時的な診断ログ(`ChallengeInput.Update()`内、コミット前に削除済み)を仕込んで原因を追ったところ、Unity Editor の Input System設定`editorInputBehaviorInPlayMode = PointersAndKeyboardsRespectGameViewFocus`(Game ViewがOSフォーカスを持っていない間、Keyboard/Mouse入力を無視・リセットする)が原因であることを特定しました。Game Viewへ明示的に`Focus()`した直後であれば、F キー入力は毎回・即座(キュー投入の翌フレーム以内)に本物の`RequestStartChallengeRpc()`呼び出しへつながることを2回、独立して確認しています。この設定自体はStep 7.7/7.8間で変更されておらず(そもそもプロジェクト設定であり、コード差分の対象外)、Step 2の`ImplementationStatus.md`(78行目)で既に「MPPMのVirtual PlayerウィンドウがOSフォーカスを失うと停止して見える」という類似の既知事象として記録されていたものと同根です。

### 根本原因の結論

- **Start Area検知/READY/F→RUNNING/Overview Cameraのいずれについても、コード・Prefab・Scene参照・Layer設定に問題は見つかりませんでした**。すべて本物の仕組み(Physics Trigger、Input System、RPC、Camera Blend)を経由して正常に動作することを確認しています
- 開発者が実機で遭遇した不具合の技術的な原因は特定できていませんが、有力な仮説は「F キー入力(あるいはそれ以前のWASD移動)を送った瞬間、Game ViewウィンドウがOSフォーカスを持っていなかった」というものです。上記`editorInputBehaviorInPlayMode`設定は、Game Viewが非フォーカスの間キーボード入力を丸ごと無視するため、Start Area進入のための移動そのものが実際にはPlayerへ届いていなかった場合、Start Area判定/READY/F/Overview Cameraの4つすべてが「動いていないように見える」という今回の報告と矛盾なく一致します
- **今回、私(Claude)側では「Step 7.8のRegression Testでは成功と報告したが実際は動いていなかった」原因を再現できませんでした**。Step 7.8時点のRegression Testは、GameFlow部分についてReflection経由の直接呼び出しに依存する割合が高く(Placement部分のgenuine Input検証に比べて手薄でした)、これは実プレイでの検証としては不十分だったという点は事実であり、今回それを是正し、genuineな経路での検証に置き換えました

### 今後の予防策

- GameFlow関連のRegression Testでは、今後もReflection経由の直接呼び出しのみに頼らず、可能な限り本物のPhysics Trigger/Input System/RPCの経路を経由させて確認する
- 手動プレイ時にF キーやWASDが反応しない場合は、まずGame Viewウィンドウを一度クリックしてOSフォーカスを与えてから再試行することを推奨する(Editor Play Mode特有の制約であり、実際にビルドしたPlayerでは発生しません)
- Console上の`[GameFlowManager] Phase -> ...`ログは、Phase遷移が実際に発生したかどうかを判別する確実な手がかりになるため、動作が疑わしい場合はまずこのログの有無を確認する

### Fix

- コード・Prefab・Scene設定への変更は行っていません(`git diff --stat`が空であることを確認済み)。調査のために`ChallengeInput.cs`へ一時的な診断ログを追加しましたが、検証完了後に完全に元の内容へ復元し、`git diff`で元のコミット内容と完全一致することを確認しています
- Placement System(`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`)は本Stepを通じて一度も変更していません

### Placement Freeze確認

- 上記ファイル群は今回のセッションを通じて一度も編集していません(`git diff --stat`が空であることがその直接的な証拠です)。Character/Nose正面基準のMarker、1.2m Placement Distance、Ground Raycast、Live Marker、Right-Click Lock、E限定のPlacement Mode、A/D Free Yaw Rotation、Ghost Wobble、Left-Click Confirm、Escape Cancel、Network Placement Request、Host-authoritative Spawnは、Step 7.8時点の実装から一切変更されていません

---

## Step 5: Networked Domino Placement / Host-Authoritative Domino Physics

Step 5では、Step 4までLocal専用だったReal Dominoの配置をNetwork化しました。Ghost Dominoは引き続き完全にLocal専用のままです(`NetworkObject`等は一切追加していません)。既存のNetwork基盤(`NetworkBootstrap`、Port 7778、`NetworkManager`/`UnityTransport`、PlayerのOwner Authority関連コンポーネント)、Step 3のDomino物理パラメータ、Step 4のPlacement Mode基本操作(E/LMB/RMB/Escape、Ghost Wobble、Placement Camera)には変更を加えていません。

### 実装済み
- `Domino.prefab` に `NetworkObject`・`NetworkTransform`(`AuthorityMode = Server`)・`NetworkRigidbody`(`UseRigidBodyForMotion = true`, `AutoUpdateKinematicState = true`)を追加。Domino同士・Player-Domino用の`BoxCollider`/`Rigidbody`設定(Mass, Damping, PhysicsMaterial等)はStep 3から一切変更していません
- `DominoSpawner.cs`(新規, `Domino.DominoSystem`名前空間, 通常の`MonoBehaviour`)を作成し、Scene内の`NetworkManager` GameObjectに追加。Host上でのみ、配置要求の検証(送信者確認・NaN/Infinity確認・距離確認)と`NetworkObject.InstantiateAndSpawn()`によるSpawnを担当
- `PlacementController.cs`を変更: `ConfirmPlacement()`のローカル`Instantiate()`を廃止し、`[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]`属性を持つ`RequestPlacementRpc(Vector3, Quaternion, RpcParams)`を新設。ConfirmしたClientはHostへ配置を要求するだけになり、Real Dominoの生成は必ずHostが行う
- **バグ修正(Step 4由来)**: `ConfirmPlacement()`が、Ghostのメトロノーム首振り用ピボット高さ(`hoverHeight`)をそのままReal Dominoの生成Y座標に使っていたため、Real Dominoが地面に約35cm埋まった状態でSpawnされ、まれに垂直配置でも倒れてしまう不具合を発見・修正しました。詳細は本章末の「重要な発見」を参照
- `Domino.prefab`をNetwork Prefabとして登録(`DefaultNetworkPrefabs.asset`)。Prefab Stageを一度開いて保存することでNGOの自動登録の仕組みが働き、`GlobalObjectIdHash`の採番も含めて正しく登録されました(Player.prefabと同じ仕組み)

### 動作確認済み
すべてHost自身のPlayerでの操作をNew Input Systemの`QueueStateEvent`でシミュレートし、Multiplayer Play Modeで実際に接続したClient(Player 2)がいる状態で検証しました。Client側は、専用のUnityMCPブリッジを持たないため`execute_code`による直接確認はできず、Hostから見た`NetworkObject.IsNetworkVisibleTo()`/`GetObservers()`(NGO自身が管理する購読者リスト)による確認と、可能な範囲でのスクリーンショットで確認しています。

- **Host Placement → Client表示**: Hostで配置したDominoが正式な`NetworkObject`としてSpawnし(`OwnerClientId = 0`)、`IsNetworkVisibleTo(clientId)`でClientにも配信されていることを確認
- **ClientはReal Dominoを直接Local Instantiateしていない**: `PlacementController`のコード上、ローカル`Instantiate()`を完全に削除し、RPC経由のリクエストのみになっていることをコードレベルで確認
- **DominoはServer/Host Authority**: `NetworkTransform.AuthorityMode = Server`、Host上の`Rigidbody.isKinematic = false`(Dynamic)を確認
- **Client側DominoはKinematic**: Client(Player 2)から見て非Ownerとなる全てのDominoで、`AutoUpdateKinematicState`によって自動的にKinematic化される設計(Playerと同じ仕組み、第7章参照)。個々のDominoについてClient側から直接`isKinematic`を確認することはできませんでしたが、Host自身のPlayerとClientのPlayer双方についてこの仕組みが正しく機能していることは既にStep 2で実証済みであり、Domino側も同一コンポーネント構成のため同様に機能すると判断しています
- **垂直配置 → 立つ**: Ghostの回転を厳密に0°に固定した状態で確定し、物理シミュレーション後もDomino が起立したまま静止(角度0.03°〜0.09°程度)することを複数回確認。上記のバグ修正後に確認したものです
- **傾斜配置 → Host Physicsで倒れ、Clientに同期**: 19°・30°など複数の傾斜角度で確定したDominoが即座に完全に倒れる(垂直角度90°)ことを確認。結果はClient側の観測用購読者リストにも反映されていることを確認
- **Host Player → Domino衝突**: HostのPlayerを歩かせてNetwork化されたDominoに接触させ、Dominoが倒れて押し出される(位置がpushされる)ことを確認
- **Client Placement(代理確認)**: Client(Player 2)自身のE/クリック操作によるRPC送信は今回自動化できませんでした(詳細は「未実装/確認できなかった事項」参照)。代わりに、実際に接続しているClient(clientId=1)のPlayerObject位置を基準とした座標を使い、Hostのサーバー処理コード(`DominoSpawner.TryPlaceDomino`)を`senderClientId=1`で直接呼び出す形で検証しました。これにより「HostがClientの要求を受理し、正しいOwnership(Server=0)でSpawnし、Host/Client双方から観測可能になる」という一連の処理を実クライアントの識別子で確認できています
- **Validation(検証処理)**: 3種類の不正リクエストで正しく拒否されることを確認 — (1)実際のPlayer位置から70m以上離れた位置への配置要求 → 拒否、(2)NaN値を含む座標 → 拒否、(3)接続していないClient ID(999)からの要求 → 拒否。いずれもSpawnされず、理由を示すWarningがConsoleに出力されることを確認
- **Network Chain Reaction**: Ground範囲内(±15m)に0.4m間隔で10個のDominoをHost側テストコードでSpawnし(本番の`PlacementController`/`DominoSpawner`コードは変更せず、`execute_code`による純粋なテスト専用の直接Spawnで実施)、1個目に28°のトリガー角を与えたところ、Step 3と同じ「約68°で連鎖的に倒れ、最後の1個だけが完全水平(90°)になる」パターンが10個全てに再現されました。全10個が`IsNetworkVisibleTo(client)`でClientにも観測可能であることを確認
- Console: 本Stepの実装コード(`PlacementController`/`DominoSpawner`/`Domino.prefab`関連)由来のErrorは0件

### 未実装/確認できなかった事項
- **Client自身の操作によるRPC送信の直接確認**: Step 4から持ち越しの制約で、MPPMの仮想Player(Client)プロセスへOS経由でキーボード入力を送る手段が今回も見つかりませんでした(マウスの絶対座標クリックは届くのに対し、キーボードは`SendInput`のVirtual-Key方式・Scan Code方式のいずれでも届かないことを、Windowの実際のフォーカス確認込みで再確認しました)。そのため「Clientが実際にEキー→クリックの操作を行った結果としてRPCが送信される」経路そのものは自動テストできておらず、上記の通りHost側から`senderClientId=1`を指定した代理呼び出しで検証しています
- **Client PlayerがHost Authority Dominoを押し倒す確認**: **今回のStep 5で最も重要とされていた検証項目ですが、確認できませんでした。** 理由は上記と同じ入力の制約です(Client自身を歩かせる操作ができない)。Host側でClientのPlayerの複製(Kinematic)を直接`Rigidbody.MovePosition()`で動かして代用しようとしましたが、`NetworkTransform`が実際に接続しているClientの本当の位置へ絶えず補正をかけ直すため、この代用方法では意味のあるデータが得られませんでした(詳細下記)。**開発者ご自身での実機確認を強くお勧めします**(手順: MPPMでClientをアクティブ化し、Client側のウィンドウでE→クリックでDominoを設置後、そのDominoにClientのPlayerで歩いて接触する)
- Client側のPlacementController/GhostDominoの内部状態(`_isPlacing`等)は、専用のUnityMCPブリッジがないため直接確認できていません

### 将来確認が必要な事項
- **Client PlayerとHost Authority Dominoの衝突確認**(上記参照、最優先)
- **Chain Reactionの間隔依存の非決定性は依然として残っています**: 本Stepのテストでは28°という明確なトリガー角度を選んだことで10個の連鎖に成功しましたが、Step 3同様、境界に近い条件では「途中で止まる」結果になることを実際に確認しました(0.4m間隔・28°より小さいトリガー角度で複数回試行した際に、1〜2個目までしか連鎖が伝播しないケースが発生)。これはNetwork化の有無に関わらない、Domino物理そのものの既知の特性です。Host/Client間でこの非決定性が異なる結果(Desync)を生まないかは、大規模なChain Reactionを実装する際に改めて注意が必要です
- **UnityTransportの受信キュー枯渇警告**: 10個のDominoをテストコードで同時にSpawnした際、Console に `"Receive queue is full, some packets could be dropped, consider increase its size (128)"` という警告(Unity側の分類ではError扱い)が2回出力されました。実際には10個全てが正しくClientへ配信されたことを確認しており実害は無かったものの、将来100〜数百個のDominoを扱う際にはこの受信キューサイズ(既定128)を明示的に増やすか、Spawnを複数フレームに分散させる対策を検討する必要があります。プレイヤーが1個ずつ手動で配置する通常のペースでは問題にならない想定です
- `Domino.prefab`にNetworkObjectを追加したことで、Step 3の`DominoLine`配下の20個(Editor上に直接配置されたインスタンス)も同じPrefabを継承するため、`NetworkObject`を持つことになりました。現在`DominoLine`は非アクティブのため実害はありませんが、将来これを再度使う場合は「In-Scene-Placed NetworkObject」としての扱われ方(Scene読み込み時の自動同期)に注意が必要です
- Step 5では`maxPlacementDistance`(既定3m)や`DominoSpawner`の検証ロジックはMVP水準です。将来的な不正対策の強化が必要になった場合は改めて設計が必要です
