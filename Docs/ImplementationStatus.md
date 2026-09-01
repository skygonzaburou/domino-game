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

開発者から、「Step 7.7までは動作していたStart Area判定/READY/F→RUNNING/Overview Cameraが、Step 7.8時点の実機操作では動作しない」との報告があり、Placement System(`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`、Step 7.8で承認済みのUX)には一切手を加えないという制約のもとで調査しました。`git diff 67ec5c8..ce729df`(Step 7.7→Step 7.8)は`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`/Docsのみを含み、`GameFlowManager.cs`/`StartAreaZone.cs`/`ChallengeInput.cs`/`ThirdPersonCamera.cs`/`OverviewCameraPoint.cs`/`Player.prefab`はいずれも1バイトも変化していないため、この2コミット間の差分そのものには原因がありませんでした。**最終的な原因は、Step 7.7で`ContainsWorldPoint()`(Ready Re-arm Fix用に導入)へ実装した境界判定に、以前から存在していた許容誤差ゼロのバグでした**(下記「根本原因の結論」参照)。

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

### 途中経過: F キー入力自体はEditor特有の制約で不安定だった(参考)

上記の検証を1回目に試みた際、2回目以降のF キー入力が`wasPressedThisFrame`を検出できないという事象が再現しました。一時的な診断ログ(`ChallengeInput.Update()`内、コミット前に削除済み)を仕込んで原因を追ったところ、Unity Editor の Input System設定`editorInputBehaviorInPlayMode = PointersAndKeyboardsRespectGameViewFocus`(Game ViewがOSフォーカスを持っていない間、Keyboard/Mouse入力を無視・リセットする)が原因であることを特定しました。この設定自体はStep 7.7/7.8間で変更されておらず(プロジェクト設定であり、コード差分の対象外)、Step 2の`ImplementationStatus.md`(78行目)で既に類似の既知事象として記録されていたものと同根です。Game Viewへ明示的にフォーカスした状態であれば、この制約下でもF キー入力は正しく`RequestStartChallengeRpc()`まで到達することを確認していました。この時点では「コードは正しく動いている、開発者の実機では何らかの入力フォーカスの問題が起きているのでは」という仮説で報告しましたが、これは**誤りでした**。開発者から「2人目の接続時、`readyPlayers=1/2`のようなログすら一度も出ない」という追加報告を受け、Host+Client 2人が実際に接続した状態でライブにScene状態を観測し直したところ、以下の本当の原因が見つかりました。

### 根本原因の結論(訂正)

**`StartAreaZone.ContainsWorldPoint()`(Step 7.7でReady Re-arm Fixのために導入)の境界判定に、許容誤差(epsilon)が全く無いバグがありました。**

- Start Areaの`BoxCollider`は、Y方向のワールド半径がちょうど1.5m(Local size 1 × lossyScale 3 × 0.5)で、Zoneの下端がちょうど地面の高さ(World Y=0)に一致するよう配置されています。これは意図的な配置で、地面に立つPlayerがちょうどZoneの下端付近に来る設計です
- `ContainsWorldPoint()`は`Mathf.Abs(diff.y) <= halfSize.y`という**許容誤差ゼロの厳密な境界判定**でした。Playerの実際の静止位置(Y)は、Rigidbody物理演算のContact Offset(接触許容量)や、NetworkTransformの同期・補間によるごくわずかな数値誤差により、`0.000000`ちょうどではなく`-0.001`〜`-0.02`程度のわずかにマイナス側へ振れることが普通にあります。この場合、`local.y`が`halfSize.y`をほんのわずかに超えてしまい、**Playerが目視上・実質上は完全にZone内にいるにもかかわらず`ContainsWorldPoint()`が`False`を返す**ことを、Playモードで実際に数値を確認して再現しました(`world.y=0.0000`→`True`だが`world.y=-0.0010`→`False`)
- `ReevaluateReady()`は`NotifyPlayerEnteredStart()`(本物のTrigger Enterイベント)の直後に必ず`ReconcilePlayersInStartArea()`を呼び、`ContainsWorldPoint()`で全Playerを再検証します。そのため、Trigger Enterイベント自体は正しく発火して`_playersInStartArea`に一度追加されても、直後の再検証でY座標がわずかにマイナス側だと**即座に取り消され**、`_playersInStartArea`が実質的に更新されないまま終わる、という形で症状が現れていました。Phaseが一度も変化しないため、`SetPhase()`のログ(`readyPlayers=X/Y`を含む)も一切出力されません。「カウントのログが出ない」「READYにならない」という開発者の報告と完全に一致します
- **Step 7.8の私自身のRegression Testでこれを検出できなかった理由**: 自動テストでは`NetworkTransform.Teleport()`でPlayerを`Y=0.1`(境界から余裕のある高さ)へ配置しており、ちょうど境界に来る自然な物理静止(`Y≈0`)のケースを一度も踏んでいませんでした。1人プレイでのReflectionベースの直接呼び出しテストでも同様に、境界ぎりぎりの数値を踏むことはありませんでした
- F→RUNNING/Overview Cameraの実装そのものにバグは無く(前回報告の通り)、READY自体に到達できなかったことがすべての表面症状(Start Area判定・READY・F→RUNNING・Overview Camera)の単一の共通原因でした

### 根本原因その2: Reconciliationが時間ベースでは走らない(境界修正だけでは不十分だった)

境界許容誤差を修正した後も、開発者との2人プレイ実機テストでまだ症状が再現しました。ライブでScene状態を観測したところ、以下がわかりました。

- 修正後の`ContainsWorldPoint()`は、Host(Owner=0)・Client(Owner=1)いずれのPlayer位置に対しても正しく`True`を返していました(境界バグ自体は解消されていた)
- しかし`_playersInStartArea`にはHostのClientId(`0`)しか入っておらず、Client側のClientId(`1`)が実際にZone内にいるにもかかわらず含まれていませんでした
- `ReconcilePlayersInStartArea()`(自己修復ロジック)自体は正しく動作すること(Reflection経由で`ReevaluateReady()`を手動実行すると即座に`[1,0]`へ正しく更新され`READY`になること)を確認しましたが、**この自己修復は`NotifyPlayerEnteredStart`/`NotifyPlayerExitedStart`/`HandleClientDisconnected`という離散的なイベントからしか呼ばれておらず、時間経過だけで自動的に再実行されることはありませんでした**。Client(Owner=1)所有のPlayerは、Server側では`Kinematic Rigidbody`+`NetworkTransform`同期によって位置が反映されるため、Server自身の`OnTriggerEnter`が確実に発火するとは限らず(Step 7/7.7で既に確認していた「Colliderの破棄時にOnTriggerExitが確実に発火するとは限らない」という教訓の別バリエーション)、一度この「発火漏れ」が起きると、その後Client側で新たな出入りイベントが発生しない限り、`_playersInStartArea`は永久にズレたまま固定されてしまう構造でした

### 今後の予防策

- 境界判定・距離判定を書く際は、Rigidbody/NetworkTransformの実際の静止位置が理論値からわずかにズレることを前提に、意図的な許容誤差(epsilon)を必ず設ける
- 自動テストでPlayerを配置する際は、境界からわずかに余裕のある位置だけでなく、**実際に自然落下・物理静止させた座標**でも最低1回は検証する
- 「イベント発生時だけ自己修復する」設計は、そのイベント自体が確実に発火する保証がない場合(特にClient所有ObjectのServer側Trigger検知)、**時間ベースの定期実行を必ず併用する**。イベント駆動は応答性のためのFast Pathとして残しつつ、時間ベースのPollingを取りこぼしに対する最終的な保険として重ねる
- Console上の`[GameFlowManager] Phase -> ...`ログの有無(特に`readyPlayers=X/Y`のXが増えるかどうか)は、Phase遷移が実際に発生したかどうかを判別する確実な手がかりになるため、動作が疑わしい場合はまずこのログの有無を確認する
- 2人以上でのGameFlow検証は、可能な限り実際の2つのProcess(MPPM Host+Client、または2台のBuild)で行う。Host単独のテストでは、Client所有Objectに固有の同期・Trigger検知の問題を再現できない

### Fix

1. `Assets/_Project/Scripts/GameFlow/StartAreaZone.cs`の`ContainsWorldPoint()`に、`boundaryTolerance`(既定0.05m、Inspectorから調整可能)という許容誤差を追加。ワールド空間で意味のある値になるよう、`transform.lossyScale`で軸ごとに逆算してLocal空間の許容誤差へ変換しています。`world.y=-0.06`(意図的に許容誤差を超える位置)では引き続き`False`を返すことを確認しており、Zoneの形状自体を実質的に変えるものではなく、物理演算由来の数mm〜数cmのノイズだけを吸収する変更です
2. `Assets/_Project/Scripts/GameFlow/GameFlowManager.cs`の`Update()`に、`readyPollInterval`(既定0.5秒)ごとに`ReevaluateReady()`を呼ぶ定期Pollingを追加(Phase が`BUILDING`/`READY`の間のみ)。既存のイベント駆動のFast Path(`OnTriggerEnter`→`NotifyPlayerEnteredStart`)はそのまま残しており、これに加えて時間ベースの安全網を重ねる形です。接続中のPlayer数ぶんしかループしないため、負荷は無視できるレベルです
3. 両方の修正を適用した状態で、開発者ご本人による実機2人プレイ(Host+MPPM Client)で`READY`ログが正しく出力され、正常に動作することを確認していただきました
- Placement System(`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`)は本Stepを通じて一度も変更していません(`StartAreaZone.cs`/`GameFlowManager.cs`はいずれもGameFlow側のファイルであり、Placement Freezeの対象外です)

### Placement Freeze確認

- 上記ファイル群は今回のセッションを通じて一度も編集していません(`git diff --stat`が空であることがその直接的な証拠です)。Character/Nose正面基準のMarker、1.2m Placement Distance、Ground Raycast、Live Marker、Right-Click Lock、E限定のPlacement Mode、A/D Free Yaw Rotation、Ghost Wobble、Left-Click Confirm、Escape Cancel、Network Placement Request、Host-authoritative Spawnは、Step 7.8時点の実装から一切変更されていません

---

## Step 8A: Player Physics Polish

### 実装済み

- `PlayerMotor.cs`の`Rigidbody.constraints`を`FreezeRotationX | FreezeRotationZ`から`FreezeRotationX | FreezeRotationY | FreezeRotationZ`へ変更。Y軸も含めて全軸のPhysics駆動Rotationを禁止し、衝突後にPlayerが勝手に回転し続ける問題を修正。`Rigidbody.MoveRotation()`(Movement Logic側のYaw制御)はこの制約と競合しないことを確認済み(制約はPhysics Solverの衝突・トルク応答のみをブロックし、スクリプトからの明示的なMoveRotationは適用され続ける)
- `PlayerPushController.cs`(新規)を追加。Owner側の`OnCollisionEnter`(Player相手のみ)を起点に、Server-validatedな2段階RPC(`RequestPushRpc`→`ApplyPushRpc`)でPlayer同士のPushを実現。Owner Authorityは維持したまま、Push適用は常にTarget自身のOwnerで実行
- `PlayerMotor.cs`に`_pushVelocity`(Push由来の減衰する追加水平速度)を追加し、通常の移動速度へ加算。Start Area Constraint(`ConstrainToStartArea`)は合成後の速度に対して適用されるため、Push単体でStart Area外へ出ることはない
- `Player.prefab`へ`PlayerPushController`コンポーネントを追加(`pushForce=3.5`, `pushCooldown=0.4`, `maxValidationDistance=2.5`)

### 動作確認済み

- **Collision Spin Test**: `PlayerMotor`を一時的に無効化してPhysicsのみでPlayerをDomino列へ実際に衝突させ(複数のDominoが実際に倒れる=68°前後の傾斜を確認)、その後`angularVelocity=(0,0,0)`・`eulerAngles=(0,0,0)`(完全に直立)を維持することをPlay Modeで確認
- **Player Push(Host→Client)**: 実際の2人プレイ環境(Host+MPPM Client)で、Host Playerの`PlayerMotor`を一時的に無効化してPhysicsのみでClient Playerの位置へ物理的に衝突させ、本物の`OnCollisionEnter`→`RequestPushRpc`→Server Validation→`ApplyPushRpc`という経路を経て、Client Player(Owner=1)が実際に約0.67m押し出され、その後位置が安定(無限に加速・振動しない)することを確認
- **Push + Start Area Constraint**: RUNNING中、Start Areaの境界近く(margin込みの実効境界)にPlayerを配置し、境界の外側方向へ意図的に大きな(10 m/s、`maxPushSpeed`のクランプを超える)Pushを適用しても、Playerが境界を超えて押し出されないことを確認
- **既存機能回帰**: READY判定、Ready Re-arm(FAILED→BUILDING後の退出→再入場サイクル)、Failure Reset(FAILED→BUILDING)が、Player Physics変更後も引き続き正常に動作することを確認。Placement関連ファイルは本Stepを通じて一切変更していない(`git diff --stat`で確認)
- 検証全体を通じてConsole Errorは0件(1件、意図的な距離超過による`DominoSpawner`の想定内Rejection Warningのみ)

### 未実装/確認できなかった事項

- **Client → Host方向のPush**: Client Playerの`Rigidbody`はOwner Authorityのため、Server(Host)側から直接操作できません。MPPM Client側のウィンドウへの自動Keyboard入力は本セッションでも不安定であることが再確認されたため(Editor Play Modeの`editorInputBehaviorInPlayMode`設定、Step 7.8.1で既出の制約と同根)、Client自身が歩いてHost Playerへ衝突する方向のPushは自動化検証できていません。コード上はHost→Client方向と完全に対称な実装(`PlayerPushController`はOwner側で共通に動作)であり、Host→Client方向の成功はこの対称性を裏付けるものですが、開発者ご自身による実機確認を推奨します
- **F→RUNNINGの本セッションでの再確認**: `ChallengeInput.cs`はStep 8で一切変更していない(Step 7.8.1で本物の経路を用いて複数回確認済み)ため、Step 8Aの本セッションでも同じ経路が動作することが期待されますが、今回のセッション中はEditor Play ModeのGame Viewフォーカスが不安定で、Simulated Input Systemでの単独再確認はできませんでした。RUNNING状態自体は正規の`BeginRun()`経由で到達し、そこでのPlayer挙動(Push + Constraint等)は確認済みです

### 将来確認が必要な事項

- `pushForce`(既定3.5)・`pushCooldown`(既定0.4秒)・`maxPushSpeed`(既定6)・`pushVelocityDecay`(既定8)はいずれも初期値です。実際に複数人でプレイした際の「押される感覚」次第で調整が必要になる可能性があります

---

## Step 8B: Challenge Timer / Result

### 実装済み

- `ChallengeTimer.cs`(新規、`GameFlowManager` GameObjectへ追加)を作成。`GameFlowManager.CurrentPhase`を外側からPolling(Server限定`Update()`、Edge検出)し、`GameFlowManager.cs`自体には一切変更を加えていない
- `RunStartServerTime`(`NetworkVariable<double>`、RUNNING開始時に`NetworkManager.ServerTime.Time`を1回だけ書き込み)、`FinalElapsedSeconds`(同、SUCCESS確定時に1回だけ書き込み)の2つのみをNetwork同期。毎Frameの経過時間表示はHost/Client双方が完全にLocalで計算(`GetElapsedSeconds()`)し、Per-frameのNetwork通信は一切発生しない
- FAILED中はTimerを更新も確定もしない(`HasFinalTime`が`false`のまま)ため、そのAttemptがClear Timeとして扱われることはない。FAILED→BUILDING(Failure Reset完了)で`RunStartServerTime`/`FinalElapsedSeconds`を両方Resetし、次Attemptは必ず0から開始
- `GameplayHUD.cs`(新規、`Domino.UI`名前空間、`GameplayHUD.prefab`化)を作成。BUILDING/READY/RUNNING(Timer表示)/FAILED/SUCCESS(CLEAR!+Final Time)の5Panelを、`GameFlowManager.CurrentPhase`に応じて排他的に表示切り替え。UGUI(Canvas, Screen Space - Overlay) + TextMeshProを使用。`NetworkObject`を持たないPure LocalなComponentで、NetworkBootstrap/MPPM/Host起動処理とは一切結合していない
- プロジェクトにTMP Essential Resources(フォントアセット)が未導入だったため、`TMP Essential Resources.unitypackage`(`com.unity.ugui`パッケージ同梱)をインポートし、`LiberationSans SDF`を全Textへ割り当てた

### 動作確認済み

- Play Modeで、READY→(正規の`BeginRun()`経由で)RUNNING→Timer開始、SUCCESS→Timer停止・Final Time確定、という一連の流れをHost側で確認。Timer表示が`MM:SS.xx`形式(例: `01:52.84`、`02:14.31`)で正しくフォーマットされることをスクリーンショットで確認
- HUDの5Panelがそれぞれ正しいPhaseでのみ表示されることをスクリーンショットで確認(BUILDING→`BUILDING`、READY→`READY / Press F to Start`、RUNNING→現在Timer、SUCCESS→`CLEAR!` + `TIME` + Final Time)
- FAILED→BUILDING(Failure Reset)後、`RunStartServerTime`/`FinalElapsedSeconds`が両方Resetされ、次のRUNNINGで`GetElapsedSeconds()`が`0.000`から再スタートすることを確認(2回目Attemptの時間が1回目から持ち越されない)
- 検証全体を通じてConsole Errorは0件(1件、手動で`ChallengeTimer.Update()`をReflection経由で直接呼び出した際にのみ発生した`PlayerLoop internal function called recursively`という警告があったが、これはReflectionでUnityのライフサイクルメソッドを直接呼び出したこと自体が原因の一時的なArtifactであり、正規の経路(Unity自身のUpdate()呼び出し)だけを使ったクリーンな再テストではConsole Errorは一切発生しないことを確認済み)
- 既存機能回帰: Placement System(`git diff --stat`が空であることを確認)、Ready Re-arm、Console上の`[GameFlowManager] Phase -> ...`ログが引き続き正常に出力されることを確認

### 未実装/確認できなかった事項

- **Host/Client間でのFinal Time一致の直接確認**: `FinalElapsedSeconds`は`NetworkVariableReadPermission.Everyone`の`NetworkVariable`のため、設計上Host/Client双方が同一の値を読む(NGOの標準的な同期保証)ことは確実ですが、MPPM Client側のウィンドウを直接キャプチャしての目視確認までは、Step 7.8.1/Step 8Aで既出のClient Window自動化の制約により実施できていません。開発者ご自身による実機確認を推奨します
- 本セッション中、Unity Editor固有の「PlayModeが理由なく一時停止状態(`EditorApplication.isPaused`)になる」という、このProjectで以前から記録済みの既知Artifactが複数回発生しました。発生の都度検出・解除して検証を継続しており、コード自体の問題ではないことを確認しています

### 将来確認が必要な事項

- HUDのレイアウト(Panel位置・フォントサイズ・色)はプロトタイプとしての仮の値です。実際のプレイ画面で見やすいかどうか、開発者ご自身での確認を推奨します
- 2人以上でのRUNNING中、Overview Cameraの見下ろし視点においてRunning Timer PanelがDomino Chainの視認性を妨げないか、実プレイでの確認を推奨します

---

## Step 8.5: Build Time / READY Condition / Success Exploration / Retry

Step 8Bで実装したRUNNING時間計測のTimerを廃止し、「PlayerがDominoをBuildするために使った累計時間」を計測するBUILD TIMEへ全面変更しました。また、Failure後に一度Start Areaから退出しないとREADYにならない仕様(Ready Re-arm)を廃止し、「全PlayerがStart Area内 + Start Area内にStanding Dominoが1つ以上」という現在の状態だけで判定するREADY条件へ再設計しました。SUCCESS中はStart Area Constraintを解除してPlayerが自由に歩けるようにし、SUCCESS画面にRetry機能を追加しました。Placement System(`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`)およびStep 8Aで完成したPlayer Physics(Collision Spin Fix/Rigidbody Rotation Constraints/Player Push関連、`PlayerPushController.cs`)には一切変更を加えていません。

### 実装済み

**BUILD TIME**
- `ChallengeTimer.cs`を全面改修。`_timerStarted`/`_accumulatedBuildSeconds`/`_activeSegmentStartServerTime`/`_finalBuildSeconds`という4つの`NetworkVariable`のみを管理し(Per-frame同期は無し)、`GamePhase`がBUILDING/READYの間をActive(進行)、RUNNING/FAILEDの間をPaused(停止)として扱う
- `DominoSpawner.cs`のReal Domino Network Spawn成功直後に`ChallengeTimer.Instance?.NotifyDominoPlaced()`を追加。Marker/E/Ghost/Left Click/RPC送信だけでは開始せず、Host側の検証を通過した最初のSpawnでのみBUILD TIMEが開始する。`PlacementController.cs`(Freeze対象)には一切変更を加えていない
- FAILED→BUILDINGの度に、Pause前のAccumulated値からResumeする(0へは戻さない)ため、複数回Failureしても正味のBuild時間だけが積み上がる
- SUCCESS時、Pause済みの値をそのまま`FinalBuildSeconds`として確定
- Retry(後述)時のみ、`ChallengeTimer.Instance.ResetForRetry()`により完全リセット

**READY Condition Redesign**
- `GameFlowManager.cs`から`_readyRearmPending`フィールドおよび関連するGate処理を完全に削除(Dead Stateとして残していない)
- `DominoFallenZone.cs`に`CountStandingInside()`を追加。各Zoneが既存の`_dominoesInside`(Trigger追跡、null pruning込み)から、`fallenAngleThreshold`(既定60°)未満の(倒れていない)Real/Network Spawned Dominoの数を数える。Scene全体のFindObjectsは行わない
- `GameFlowManager.ReevaluateReady()`のREADY条件を`AllConnectedPlayersInStartArea() && startArea.CountStandingInside() > 0`へ変更。過去のFailure履歴・退出履歴を一切参照しない、現在の状態だけによる毎回ゼロからの判定
- `GameFlowManager.RequestStartChallengeRpc()`にも同じStanding Domino条件のDefensive Checkを追加。READY条件だけを信用せず、実際にRUNNINGへ入る直前にもう一度Server側の実データで再検証する

**SUCCESS Exploration**
- `GameFlowManager.cs`に`IsMovementConfined`(`RUNNING || FAILED`)を新設し、Camera用の`IsChallengeActive`(`RUNNING || SUCCESS || FAILED`、無変更)から分離
- `PlayerMotor.IsConfinedToStartArea()`の参照先を`IsChallengeActive`から`IsMovementConfined`へ変更(この1行のみ。Step 8AのSpin Fix/Push関連コードには触れていない)。SUCCESS中はStart Area Constraintが外れ、Overview Cameraのまま自由に移動できる。Screen-relative MovementはCamera基準で元々Phase分岐が無いため無変更で動作

**Retry**
- `GameFlowManager.cs`に`RetryRequestRpc()`(`[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]`)と`RetrySequence()`コルーチンを追加。`CurrentPhase == SUCCESS`をServer側で再検証し、`_isRetrying`フラグで二重実行を防止した上で、全Domino Despawn→`ResetRunState()`→`ChallengeTimer.ResetForRetry()`→`SetPhase(BUILDING)`という順で実行
- `GameplayHUD.cs`を改修。BUILD TIME表示をPhaseに関わらず常時表示するよう変更(旧`RunningTimerPanel`を`BuildTimePanel`へ改名・再利用)。`ResultActionContainer`(画面右下)+`RetryButton`(UnityEngine.UI.Button)を追加し、SUCCESS中のみ表示。Mouse Click(`Button.onClick`)とKeyboard(`R`キー、`wasPressedThisFrame`)の両方に対応
- `GameplayHUD.prefab`をHeadless Prefab編集(`manage_prefabs`の`modify_contents`)で更新。TMP Essential Resourcesは既にStep 8Bでインポート済みのものを再利用

### 動作確認済み

Play Modeで、以下をすべて実機確認しました(すべて`ChallengeTimer.GetBuildSeconds()`/`GameFlowManager.CurrentPhase`等の実際の値、または本物のInput System経路・UI Click経路で確認)。

- **BUILD TIME Start条件**: Domino未配置の間`HasStarted=False`(HUDは`--:--.--`)であること、Host側のReal Domino Spawn成功の瞬間に`HasStarted=True`となり0から計測開始することを確認
- **BUILDING/READY中は進行**: READYへ遷移してもBUILD TIMEが停止しないことを確認
- **RUNNING/FAILED中はPause**: F押下でRUNNINGへ入った直後の値が、その後時間が経過しても一切変化しない(複数回確認)ことを確認。FAILEDへ遷移した後も同様
- **FAILED→BUILDINGでResume**: Failure Reset完了(BUILDING)後、Pause時の値から継続して増加し始めることを確認(0へリセットされない)
- **SUCCESSでFinal確定**: SUCCESS遷移時の`FinalBuildSeconds`が、RUNNING開始時にPauseされていた値と完全に一致することを確認
- **READY新条件**: 「全PlayerがStart Area内+Domino 0個」→BUILDINGのまま、「+倒れたDomino 1個」→BUILDINGのまま、「+Standing Domino 1個」→READY、の3パターンをすべて実機確認。READY中にStanding Dominoを倒す→即座にBUILDINGへ戻ることも確認
- **Ready Re-arm廃止の確認**: FAILED→BUILDING(Domino Despawn済み、Standing Domino=0)の直後、Playerが一度もStart Areaから退出していない状態のまま新しいDominoを配置し、Exit/Re-enter無しで即座にREADYへ遷移することを確認(旧仕様なら`_readyRearmPending`によりBUILDINGに留まっていたはずの状況)
- **F→RUNNING(本物のFキー入力)**: `InputSystem.QueueStateEvent`経由の本物のFキー入力で、新しいDefensive Check込みのRUNNING遷移が正常に動作することを確認
- **SUCCESS Exploration**: SUCCESS中に`IsMovementConfined=False`となること、実際にPlayerをStart Area範囲外(境界から8m以上)へ移動させても引き戻されず静止したままであることを確認。Camera位置が`OverviewCameraPoint`と一致し続けること(Overview維持)、`PlacementController._markerActive`が`False`のまま(Placement不可)であることも確認。スクリーンショットで、BUILD TIME・CLEAR!・Retry Button・Start Area外に立つPlayer・維持されたDomino Chainが同時に表示されることを確認
- **Retry(Mouse Click)**: `UnityEngine.EventSystems.ExecuteEvents.Execute(..., pointerClickHandler)`による本物のButton Click Event経由で、SUCCESS→BUILDINGへ遷移し、Domino全削除・BUILD TIME完全Reset(`HasStarted=False`)・Camera Third Person復帰(Player近傍へ)を確認
- **Retry(Rキー)**: 別Attemptで、本物のInput System経由のRキー入力でも同じRetry Flowが成立することを確認(2回とも1回目の入力で成功)
- **Double Retry防止**: SUCCESS中に`RetryRequestRpc()`を3回連続で呼び出しても、Domino DespawnとState Resetが1回だけ実行され、正しくBUILDINGへ到達することを確認(`_isRetrying`ガード)
- **Full Retry Loop**: 最初のDomino配置→BUILD TIME開始→READY→本物のF→RUNNING(Pause)→SUCCESS(Final確定)→Retry(Click)→BUILDING(Timer完全Reset)→新しい最初のDomino配置→新しいBUILD TIME開始(0から)→新しいREADY、という一連の流れをクリーンなセッションで確認
- 上記すべてを通じて、Console Errorは0件(クリーンな最終セッションで確認。検証中一時的に、意図的なReflection経由のメソッド直接呼び出し等に起因する`PlayerLoop internal function called recursively`という無害な警告が出たことがあるが、Project由来のErrorではない)
- **既存機能回帰**: Placement System・`PlayerPushController.cs`は本Stepを通じて一切変更していない(`git diff --stat`で確認)。Player Rigidbodyの`constraints`(`FreezeRotation`)・`PlayerPushController`の存在も変化がないことを確認

### 未実装/確認できなかった事項

- **Host/Client間でのFinal BUILD TIME一致・Client側からのRetryの直接確認**: いずれも設計上(NetworkVariableの同期保証、Server側でのPhase再検証)は保証されますが、MPPM Client Windowへの自動入力/自動操作という、これまでのStepでも繰り返し確認されてきた制約により、Client側の画面を直接キャプチャしての目視確認・Client側からのRetry Request送信の直接実行は本セッションでは実施できていません。開発者ご自身による実機確認を推奨します
- 本セッション中も、Unity Editor固有の「PlayModeが理由なく一時停止状態になる」既知Artifactが複数回発生しましたが、発生の都度検出・解除して検証を継続しており、コード自体の問題ではないことを確認しています

### 将来確認が必要な事項

- Retryを`Host-only`に制限するかどうかはStep 9で検討します(現時点ではPrototypeとしてHost/Clientどちらからも要求可能)
- Result Action Container(現在Retry Buttonのみ)は、将来Next Course/Character Change/Return to Title等を追加しやすい配置を意図していますが、実際に追加する際にレイアウトの再調整が必要になる可能性があります

---

## Step 8.6: Failure Detection Stability / Partial Failure Cleanup

Step 8.6では2点のみを対象としました。(1)RUNNING中、Chainがまだ明らかに進行しているのにFAILEDになる「False FAILED」のRoot Cause特定と修正、(2)FAILED後のCleanupを「登録済み全Domino削除」から「倒れたDominoのみ削除、Standing Dominoは維持」へ変更。Placement System・BUILD TIME(第8.5章の意味論自体)・READY条件・SUCCESS Exploration・Retry・Player Physics/Pushには一切変更を加えていません(`git diff --stat`で確認)。

### Root Cause(実機調査で確認した事実)

Debug LogとRuntime直接読み取り(Rigidbody速度・Tilt Angle・Registry内容)による実際のPlay Mode調査の結果、単一の原因ではなく、以下4点がいずれも実際に再現するFalse FAILEDの原因になり得ることを確認しました。

1. **Velocity閾値だけでは遅い動きを見逃す**: あるDominoが`Tilt Angle=89.8°`まで倒れかけている最中、`linearVelocity=0.0015`(閾値0.02未満)・`angularVelocity=0.0375`(閾値0.05未満)という、明らかにまだ動いている途中の瞬間を直接観測しました。旧`AnyTrackedDominoActive()`はVelocityのみで判定していたため、この瞬間は「非Active」と誤判定されうる状態でした
2. **Stage-authored DominoがRegistryに存在しない**: `DominoSpawner`経由でPlayerが配置したDomino以外(Scene上に直接配置されたDomino)は、旧`DominoCleanupManager`の追跡リストに一切含まれておらず、Activity検知の対象外でした。現在の実プレイ経路(Playerが全て配置する)ではこの経路は直接の原因ではありませんでしたが、一般的な正しさとして修正しました
3. **Fresh Fallen EventがFailure Timerをリセットしていなかった**: `OnZoneFallen()`でCheckpoint/Goal進行が認められた瞬間でも、Chain Stop Failure Monitor側の「最後に活動を検知した時刻」が更新されない実装になっており、Progress自体は正しく進んでいるのにFailure Timerだけが古い基準で進み続けるケースがありました
4. **副次的に発見: Physics Explosion(暴走Domino)によるActivity検知の永久汚染**: Playerが近接した間隔(約0.4〜0.45m、Domino幅約0.35mに近い)で連続配置すると、Rigidbody同士のOverlapをPhysics Solverが解消しようとして、最大で約190〜196 m/sの速度で1個のDominoが吹き飛び、Ground Colliderを貫通してY=-9853やY=-24093まで落下し続ける現象を2回直接観測しました。このDominoは`IsSpawned=true`のままRegistryに残り続けるため、速度・角度いずれの判定にも異常値として混入し、以後のActivity判定を恒久的に汚染する経路になり得ることを確認しました

これらのうち、実際にどれがユーザー様の観測した具体的な発生ケースかは1つに断定できませんでしたが(調査時点でChain経路上のReal DominoはすべてPlayer配置のものであり、原因2は今回のケースの直接原因ではなさそうでした)、1・3・4はいずれも実機で再現を確認した実在のバグであり、「単純にSettle Durationを伸ばすだけ」ではこれらのうち1つも解決しないため、それぞれ個別に対処しました。

### New Failure Detection(Meaningful Activity)

- `DominoCleanupManager.AnyTrackedDominoActive()`を`HasMeaningfulActivity(linearVelocityThreshold, angularVelocityThreshold)`へ置き換え。判定条件を「Velocity閾値超過」**OR**「前回Poll時からのTilt Angle変化が`meaningfulTiltDelta`(既定2°)を超過」のいずれかに拡張(対策1)。Tilt Angleは絶対角度ではなく**変化量**で見るため、傾いたまま静止しているDominoは変化が無くなれば正しく非Activeと判定され、最終的にFAILEDになります(恒久的に傾いているだけでActive扱いされ続けることはありません)
- 新設した`_sceneAuthored`リストで、Scene起動時に一度だけ(Server限定`Update()`初回Tickのみ、以後は毎フレーム検索しない)`FindGameObjectsWithTag("Domino")`を実行し、`DominoSpawner`経由で登録されていないDominoをActivity検知専用リストへ追加(対策2)。このリストはいかなるDespawn経路(BUILDING Cleanup/FAILED/Retry)にも使われず、Stage-authoredの永続オブジェクトとして扱います
- `GameFlowManager.OnZoneFallen()`の順序検証通過直後に`_settledSince = -1f`を追加し、正当なFresh Fallen Event(Checkpoint通過含む)がFailure Monitorの静止タイマーを必ずリセットするようにしました(対策3)
- `DominoCleanupManager`に`outOfBoundsY`(既定-10)を追加。毎回のPoll(`checkInterval`, 既定0.2秒)で、Y座標がこの値を下回ったDominoを即座に(Phase・Fallen判定に関わらず)Despawnし、Registryから除去(対策4、Physics Explosion由来の暴走Dominoに対する安全網)
- Poll間隔ベースの判定を維持しており(既存の`checkInterval`をそのまま使用)、毎フレームのScene全体検索やPer-frame RPCは追加していません

### Fallen-only Cleanup(FAILED後の部分削除)

- `DominoCleanupManager`に、既存の`_tracked`リストと`fallenAngleThreshold`(既存の単一設定、新規追加なし)を共有する形で、`DespawnAllRegistered(Action)`(Retry用、既存動作のまま全削除)と新設`DespawnFallenRegistered(Action)`(FAILED用、`Vector3.Angle(transform.up, Vector3.up) >= fallenAngleThreshold`のもののみ削除)を、共通の`DespawnRoutine(list, allowStanding, onComplete)`コルーチン(既存のBatch Despawnパターン、`despawnBatchSize`ごとに1Frame空ける、Transport Queue設定は無変更)経由で実装
- `GameFlowManager.FailureResetSequence()`が`DespawnAllRegistered`から`DespawnFallenRegistered`へ変更。`RetrySequence()`は`DespawnAllRegistered`のまま無変更(2つのDespawn経路は完全に独立したAPIとして分離)
- Fallen-only削除後、`ResetRunState()`(→各Zoneの`OnRunStart()`)は削除完了後に実行されるため、生き残ったStanding Dominoは実行時点で「倒れていない」状態であり、除外リストへは一切入りません。次のAttemptでStanding Dominoに対するFresh Fallen Eventは追加のコード変更なしに正しく動作します

### Failure Recovery Test(実機確認)

- **False FAILED再現テストの再確認**: 遅い倒れかけ(Tilt変化のみでVelocity閾値未満)のケースで、`HasMeaningfulActivity`がTilt Delta判定によりActive=trueを返し続け、FAILEDにならないことを確認
- **Real Stop**: 完全に静止したChain(Velocity・Tilt変化とも無し)が、`checkInterval`×設定回数分のPollを経て正しくFAILEDになることを確認(Settle Duration自体は変更していません)
- **Leaning Stop**: 傾いたまま(例: 30°)静止させたDominoが、Tilt変化が止まった後は正しくFAILEDになる(永久にActive扱いされない)ことを確認
- **Fresh Fallen Event経由のTimerリセット**: Checkpoint通過の瞬間に`_settledSince`が`-1`にリセットされることを直接確認
- **Out-of-Bounds安全網**: Physics Explosionを意図的に再現(近接配置)し、暴走Dominoが`outOfBoundsY`を下回った直後にDespawn・Registry除去されること、以後のActivity判定が汚染されないことを確認
- **Fallen-only Despawn**: 10個配置(4個Fallen/6個Standing相当の構成)でFailureさせ、Fallen判定を満たすもののみが削除され、Standing Dominoが画面上・Registry上ともに残ることをHost側で確認
- **Standing Domino再利用**: Fallen-only削除後、残ったStanding Dominoを含む形で不足分を再配置し、2回目のREADY→RUNNING→SUCCESSまで到達することを、Fail→Rebuild→Successのサイクルを2回繰り返して確認。Standing Dominoの過去のFallen Event/Progress状態が残って次のAttemptを阻害することはありませんでした
- **BUILD TIME Resume確認**: Fallen-only Cleanup後もBUILD TIMEが0にリセットされず、Pause前の値からResumeすることを、複数回のFailureを跨いで確認
- **READY即時再遷移**: Failure後、Standing DominoがStart Area内に残っており全PlayerもStart Area内にいる状態から、追加の退出/再入場なしに即座にREADYへ遷移することを確認(旧Ready Re-arm方式は復活させていません)
- **Retryとの区別**: 同じセッション内でRetry(SUCCESS→BUILDING)を実行し、Standing Dominoも含め登録済み全Dominoが削除されることを確認(`DespawnFallenRegistered`とは独立した`DespawnAllRegistered`が正しく呼ばれている)

### Regression(既存機能の回帰確認)

- Placement System(`PlacementController.cs`/`GhostDomino.cs`/`PlacementMarker.cs`/`PlacementMarker.prefab`)は本Stepを通じて一切変更していません(`git diff --stat`で確認)
- Player Physics(Rigidbody全軸Rotation Freeze、Spin無し)・Player Push(`PlayerPushController.cs`)は無変更、動作に影響がないことを確認
- READY条件(全PlayerがStart Area内+Standing Domino1つ以上)・SUCCESS Exploration(Overview Camera維持+自由移動)・Retry(全Domino削除+BUILD TIME完全Reset)は、いずれも第8.5章の仕様のまま動作することを確認

### Console

検証全体を通じて、Project由来のConsole Errorは0件でした。調査目的で一時追加したDebug LogおよびDiagnosticメソッドは、修正完了後にすべて削除済みです(`git diff`で`Diag`/`TEMP`等の残存が無いことを確認)。

### Git

コミットメッセージ: `Step 8.6: Stabilize chain failure detection and preserve standing dominoes`。詳細はコミット履歴を参照してください。

### 未実装/確認できなかった事項

- **Host/Client間での視覚的一致の直接確認**: Fallen-only Cleanup後にStanding DominoがHost/Client双方の画面で一致して残ることは、これまでのStepと同じMPPM Client Window自動化の制約により、Client側の直接キャプチャでの確認はできていません。Host側の`NetworkObject`一覧・`IsNetworkVisibleTo()`による間接確認と、Host自身の画面キャプチャによる確認に留まります
- Physics Explosion自体の発生原因(近接Placementでのoverlap)を防ぐ側の対策(Placement時の最小間隔バリデーション等)は、Placement Systemが本Stepの変更対象外(Freeze)であるため、今回は対応していません。`outOfBoundsY`による事後の安全網のみで対応しています

### 将来確認が必要な事項

- Placement時の近接配置によるPhysics Explosionそのものを未然に防ぐバリデーション(最小配置間隔チェック等)の追加は、将来のStepでの検討対象です
- `meaningfulTiltDelta`(既定2°)・`outOfBoundsY`(既定-10)はいずれも今回のテストで問題なく機能した初期値です。実際のStageレイアウトやDomino挙動次第で、将来調整が必要になる可能性があります
- Step 9(未着手)に進む前に、開発者ご自身による複数回の実プレイでの最終確認を推奨します

---

## Step 9A: Main Menu / Stage Select / Single Player

Step 9は「Gameplayそのもの」ではなく「PlayerがGameplayへ入るまでのFlow」を作るStepです。9Aでは、TITLE→MODE SELECT→TIME ATTACK(Single Player)→STAGE SELECT→GAMEPLAYの経路を実装しました。既存のGameplay System(Placement/BUILD TIME/READY/RUNNING/Strict Clear/Failure Detection/Fallen-only Cleanup/SUCCESS/Retry/Player Push/Spin Fix)には一切変更を加えていません(`git diff --stat`で確認)。詳細な設計は`Docs/MenuFlow.md`を参照してください。

### 実装済み

**Main Menu Scene**
- `Assets/_Project/Scenes/MainMenu.unity`(新規)を作成し、Build Settingsの先頭(Index 0)に登録。Gameplay Sceneは既存の`Assets/Scenes/SampleScene.unity`をそのまま使用(Index 1)し、Single Player/Online用に別Gameplay Sceneを新設していません
- MainMenu Sceneは`GameFlowManager`/`Domino`/`Player`等のGameplay Objectを一切持たず、`Main Camera`・`EventSystem`(`InputSystemUIInputModule`、Active Input Handlingが`Input System Package`のみのプロジェクトのため)・`Canvas`(Screen Space - Overlay, CanvasScaler 1920x1080 Scale With Screen Size、GameplayHUDと同じ設定)・移動してきた`NetworkManager`のみで構成

**Menu Architecture**
- `MenuState.cs`(enum: Title/ModeSelect/OnlineSelect/OnlineCreate/OnlineJoin/OnlineRoom/StageSelect)
- `MainMenuController.cs`(Canvasにアタッチ): 7つのPanelをDictionaryで管理し、`ShowState()`で排他的に表示切り替え。`Stack<MenuState>`による簡易Back履歴、Escapeキー(`Keyboard.current.escapeKey`)とPanelごとのBackボタン両方から`OnBackClicked()`を呼び出し
- `StageSelectController.cs`(StageSelectPanelにアタッチ): Stage Data/Networkingを扱う。`MainMenuController`はPanel切り替えのみを担当し、Stage DataやNetwork処理を一切知らない責務分離構成(1つの巨大な`MenuManager.cs`にしていません)
- Canvas階層: `TitlePanel`/`ModeSelectPanel`/`OnlineSelectPanel`/`OnlineCreatePanel`/`OnlineJoinPanel`/`OnlineRoomPanel`/`StageSelectPanel`の7Panel。Online系4Panelは9Aでは「(Step 9B)」のプレースホルダ表示のみ(Create/Join/Roomボタン自体はOnlineSelectPanelに実装済みで、Panel間遷移は機能する)

**TITLE / MODE SELECT**
- TitlePanel: 「DOMINO」+ STARTボタン。ModeSelectPanel: 「SELECT MODE」+ TIME ATTACK/ONLINE PLAYボタン+ BACKボタン

**Single Player(Local Host)Architecture**
- 既存GameplayがNGO/NetworkBehaviour前提であるため、Single Player専用の非Network実装は作らず、「Local Host 1人」としてGameplayを開始する方式を採用(`StageSelectController.StartLocalHostAndLoadGameplay()`): `NetworkManager.Singleton.StartHost()`(Relay不使用)→ `NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single)`。既存GameFlowがConnected Players = 1でもそのまま動作することを利用しており、Single Player専用のGameplayロジック分岐は追加していません
- `StartHost()`が失敗した場合(検証環境で実際にPort競合により発生を確認)、`SceneManager.LoadScene()`を呼ばずにエラーログを出して中断するガードを追加。当初この分岐が無く、StartHost失敗後もSceneManager.LoadScene()を無条件で呼んでいたため、Networking未起動状態でのScene操作という未定義動作を引き起こし、StageSelectPanelのGrid Cardが消失する実害のあるバグとして実機で発見・修正しました

**Stage Catalog**
- `StageDefinition.cs`(ScriptableObject: Stage ID/Display Name/Gameplay Scene Name)・`StageCatalog.cs`(ScriptableObject: StageDefinitionのList)を新規作成
- `Assets/_Project/Data/Stages/Stage_01.asset`〜`Stage_08.asset`(Stage ID 1〜8、Display Name "STAGE 1"〜"STAGE 8"、Gameplay Scene Name はすべて`"SampleScene"`)、および`Assets/_Project/Data/StageCatalog.asset`(8つのStageDefinitionを参照)を作成
- `StageSelectController`は8回ハードコードする代わりに、Panel内の非表示`StageCardTemplate`(Button + TextMeshProUGUI)を`catalog.Stages`の数だけ`Instantiate`してGridLayoutGroup(4列×2段、`GridLayoutGroup.Constraint.FixedColumnCount`)へ追加する、データ駆動の構成
- 将来Stage 2以降に別のGameplay Sceneを割り当てる場合、対応する`StageDefinition`アセットの`gameplaySceneName`を変更するだけでよく、コード変更は不要なArchitecture

**NetworkManager Lifecycle(Menu ⇄ Gameplay永続化)**
- 既存の「NetworkManager」GameObject(`NetworkManager`/`UnityTransport`/`NetworkBootstrap`/`DominoSpawner`/`DominoCleanupManager`)をSampleSceneからMainMenu Sceneへ移動(`move_to_scene`)。SampleScene側には以後`NetworkManager`を一切配置していません(Duplicate NetworkManager防止)
- `NetworkBootstrap.Awake()`に明示的な`DontDestroyOnLoad(gameObject)`を追加(NetworkManager自体も内部的に同様の処理を行いますが、それに暗黙的に依存せず明示化)。`LoadSceneMode.Single`によるScene遷移時、MainMenu Sceneの`Canvas`等は正しく破棄され、NetworkManager側のみ生き残ることを実機で確認
- `GameFlowManager`(Scene-placed NetworkObject、Zone参照を持つ)はGameplay固有のためSampleScene側に残置

**NetworkBootstrap(Development Auto-start Modeの分離)**
- 既存のMPPM Tag(`Host`/`Client`)・CLI引数(`-host`/`-client`)によるDevelopment Auto-startのロジックは削除せず維持。Hostとして起動した場合のみ、直後に`NetworkManager.Singleton.SceneManager.LoadScene(developmentGameplaySceneName, LoadSceneMode.Single)`(既定値"SampleScene"、Inspector調整可能)を呼ぶよう追加し、開発テスト時に毎回手動でMenu操作する必要がないようにしました
- MPPM Tag/CLI引数のいずれも無い通常起動時はこの経路が早期returnし、MainMenuのTITLE画面が通常通り表示される、Interactive Menu ModeとDevelopment Auto-start Modeの分離を確認

**GameSessionContext**
- `GameSessionContext.cs`(通常のstatic class、MonoBehaviourではない): `SelectedStageId`・`IsOnline`を保持。Sceneをまたいでも静的フィールドは保持され続けるため、DontDestroyOnLoad等の仕組みを追加で用意する必要がありませんでした

### 動作確認済み

MPPM Tag(Main EditorへHost Tagが付与されているため、通常のPlay Mode開始が即座にDevelopment Auto-startでGameplayへ入ってしまう)の影響を避けるため、検証中は`NetworkBootstrap`コンポーネントを一時的にInspector上でDisableした状態でPlay Modeに入り、Menu Flow自体を実機確認しました(検証専用の一時措置で、コード自体は変更していません)。

- **TITLE表示**: Play Mode開始直後、「DOMINO」+ STARTボタンが正しく表示されることをスクリーンショットで確認。7Panel中TitlePanelのみがactiveであることを確認(初期実装時、7Panel全てが同時にactiveになっており、最後のPanelが画面全体を覆ってTitlePanelが見えなくなるバグを発見・修正済み)
- **本物のClick経路**: `UnityEngine.EventSystems.ExecuteEvents.Execute(button, pointerData, ExecuteEvents.pointerClickHandler)`(Step 8.5のRetry Buttonと同じ検証手法)経由で、START→TIME ATTACK→Stage 3クリックまでの一連の操作が実際のButton Click Eventとして正しく処理されることを確認。本プロジェクトで初めて`EventSystem`/`InputSystemUIInputModule`を追加したため、これが本Stepで初めて機能するUI Click経路になります
- **ModeSelect表示**: 「SELECT MODE」+ TIME ATTACK/ONLINE PLAYボタンが正しい間隔で(重ならずに)表示されることを確認。初期実装ではY軸のAnchor差分が不足しており2ボタンが視覚的に重なって表示されるバグを発見・修正済み
- **Stage Grid(4×2)**: `StageCatalog`から8枚のCard("STAGE 1"〜"STAGE 8")が動的生成され、指定通り4列×2段で正しく配置・全て画面内に収まって表示されることを確認。初期実装では`GridLayoutGroup.Constraint`に`FixedRowCount`(値2)を誤って設定しており2列×4段になっていたバグ、および Cell/Container サイズがCanvas幅を超えて左右にはみ出るバグの2つを発見・修正済み
- **Single Player Start(Stage 3選択)**: Stage 3のCardクリックにより、`GameSessionContext.SelectedStageId = 3`が設定され、`NetworkManager.StartHost()`→`SceneManager.LoadScene("SampleScene", Single)`が実行され、実際にSampleSceneへ遷移することを確認(`SceneManager.GetActiveScene().name == "SampleScene"`)
- **Gameplay Regression(Single Player)**: Menu経由でSampleSceneに入った後、Player 1人がSpawnされていること、`DominoSpawner.TryPlaceDomino()`によるDomino配置が成功すること、`ChallengeTimer.HasStarted`がDomino配置直後に`true`になりBUILD TIMEが実際にカウントアップすること(`00:00.02`等)をスクリーンショットと直接の値確認の両方で確認。Placement/BUILD TIME/GameFlowの各コンポーネントがNetworkManagerのScene移動後も正しく解決・連携することを確認
- **StartHost失敗時の安全な中断**: 検証環境で実際にPort競合(同一プロセス内での前回Play SessionのUDP Socketが解放されず`Address already in use`)が発生し、修正前のコードでは失敗後も`SceneManager.LoadScene()`が実行されてStageSelectPanelのGrid Cardが消失する実害を確認。修正後は`StartHost()`失敗時に即座にエラーログを出して中断し、Grid Cardの消失が再発しないことを確認
- Console: 本Step実装コード由来のError/Warningは最終確認時点で0件

### 未実装/確認できなかった事項

- **MPPM Virtual Player側でのTITLE〜STAGE SELECT操作の直接確認**: これまでのStepと同じ制約(MPPM仮想Playerウィンドウへの自動キーボード入力送信不可)により、Client側の実機操作は確認できていません。ただしMenu側のUIはOwner概念を持たないローカルUIであり、Development Auto-startのTagがMain Editor側にのみ設定されているため、この制約自体はStep 9Aの検証に実害はありませんでした
- Online系4Panel(OnlineSelect/OnlineCreate/OnlineJoin/OnlineRoom)は、Panel遷移(Back込み)のみ確認済みで、実際のNetworking機能はStep 9Bで実装します

### 将来確認が必要な事項

- **NetworkTransportのUDP Socketが同一プロセス内で解放されないまま残ることがある**(今回の検証で複数回発生): Play ModeをStopしてもUDP Socketが即座には解放されず、次のStartHost()が`Address already in use`で失敗するケースを確認しました。原因はUnity Transport側のネイティブSocket解放タイミングに起因すると考えられ、本Stepのコード自体のバグではありませんが、開発中に同様の現象が起きた場合はUnity Editorの再起動が必要になる可能性があります
- Stage 2〜8に実際に異なるGameplay Sceneを割り当てる作業は将来のStepの対象です

---

## Step 9B: Online Create / Join / Room

Step 9Bでは、Online Play(Create Room / Join Room / Join Code / Online Room / Player Count / Host-only Stage Selectへの遷移)を実装しました。Step 9AのSingle Player経路・既存Gameplay Systemには一切変更を加えていません。詳細は`Docs/OnlineSession.md`を参照してください。

### 実装済み

**Online Service Architecture**
- `com.unity.services.multiplayer`(2.3.1、Multiplayer Services SDK)を新規導入。導入前に`Packages/packages-lock.json`の解決済み依存関係を確認し、`com.unity.services.relay`/`com.unity.services.lobby`という独立したPackageが存在しないこと(現在の統一Session APIが両者を内部に統合していること)を確認した上で、Session APIのみを使用するArchitectureを採用しました(Lobby/Relayを個別に手動配線する古い方式は不採用)
- API調査はWeb Docs(一部404で不安定)ではなく`unity_reflect`によるUnity Editorへの直接Reflectionを優先し、`IMultiplayerService`/`SessionOptions`/`SessionOptionsExtensions`/`ISession`/`IHostSession`/`IAuthenticationService`/`UnityServices`の実際のAPI Surfaceを確認した上でコードを書きました
- `UnityServices.InitializeAsync()` + `AuthenticationService.Instance.SignInAnonymouslyAsync()`(匿名ログインのみ、User Account画面なし)を、Online機能に初めて触れた時点で遅延初期化

**OnlineSessionController.cs(新規)**
- Create Room: `MultiplayerService.Instance.CreateSessionAsync(new SessionOptions { MaxPlayers = 4 }.WithRelayNetwork())`。**`CreateSessionAsync`完了直後に`NetworkManager.StartHost()`相当が自動的に実行され、`NetworkManager.Singleton.IsHost`/`IsListening`が`true`になることを実機確認**(NGOがProjectに存在することをSDK側が検出し、明示的な`StartHost()`呼び出しなしにHost化される)
- Join Room: `MultiplayerService.Instance.JoinSessionByCodeAsync(code, null)`。`TMP_InputField`(Placeholder/Text/Viewportを含む完全なUGUI構成)からJoin Codeを読み取り
- Leave: `ISession.LeaveAsync()` → `NetworkManager.Shutdown()` → `OnlineSelectPanel`へ復帰
- すべての操作を`try/catch`で保護し、UIには短い一般的なMessage(例: `"Could not join the room.\nPlease check the Join Code."`)のみを表示、詳細な例外はConsoleへ`Debug.LogError`で出力(第26章の要件どおり)

**NetworkSessionState.cs(新規)**
- MainMenu.unityにScene配置した`NetworkObject`(`GameFlowManager`と同じ「Scene-placed NetworkObject」パターン)。`NetworkVariable<SessionPhase>`(Room/StageSelect/StartingGame/InGame)をHost Authorityで管理
- `HostAdvanceToStageSelect(int connectedPlayerCount)`: Host-onlyかつPlayer数2人以上の場合のみPhaseを進める、Server側再検証込みのMethod。Room画面の`STAGE SELECT`ボタンから呼ばれる
- `PhaseChanged`イベントを`OnlineSessionController`が購読し、Phaseが`StageSelect`になった瞬間に**接続中の全員**の`MainMenuController.ShowState(MenuState.StageSelect)`を呼ぶことで、Host/Client問わずLocal Menu UIが同時に切り替わる仕組み

**Online Room UI**
- `OnlineJoinPanel`: Header、`TMP_InputField`(Join Code入力)、JOINボタン、BACKボタン、Error Text
- `OnlineRoomPanel`: Header、Join Code表示(実際にServiceが発行した値をそのまま表示、桁数のハードコードなし)、Player Count(`"1 / 4"`形式)、Status Text(Host/Client別文言)、STAGE SELECTボタン(Host-only、`interactable`制御)、LEAVEボタン
- `OnlineCreatePanel`: "Creating room..."のLoading表示のみ(Step 9Aで作成済みのPlaceholderを転用)

**バグ修正: LeaveAsync()のTimeout Guard**
- 実機検証中、`ISession.LeaveAsync()`が(Local PlayerがSessionの唯一のMemberだった状況で)応答なく停止する事象を1度観測しました。後からConsoleに出力された`"Received error message from Relay: player timed out due to inactivity"`から、検証環境でのRelay接続自体の劣化が示唆されますが、原因を完全には特定できていません。原因の断定有無に関わらず「外部Service呼び出しがUIを無期限にブロックしてはならない」という判断のもと、`Task.WhenAny(_session.LeaveAsync(), Task.Delay(5秒))`によるTimeout Guardを追加しました。Timeoutした場合もLocal側のNetworkManager Shutdown・Panel遷移は必ず実行されます

### 動作確認済み

Step 9Aと同様、Main EditorのMPPM Host Tagによる自動Gameplay突入を避けるため、検証中は`NetworkBootstrap`を一時的にDisableした状態でPlay Modeに入り、実機で確認しました。

- **Create Room(実際のUnity Relay/Session使用)**: CREATE ROOM実行後、実際にUnity Cloud/Relayサービスへ接続し(Console上でQoS(Quality of Service)による各Relay Region候補への実際の通信ログを確認)、本物のJoin Code(例: `6LM9H8`)が発行され、`OnlineRoomPanel`へ正しく遷移することを確認。`NetworkManager.Singleton.IsHost == true` / `IsListening == true`になることも確認
- **Room UI表示**: Join Code・Players `1 / 4`・"Waiting for players..."がスクリーンショットで正しく表示されることを確認
- **2人未満でのSTAGE SELECT無効化**: Player 1人の状態で`StageSelectButton.interactable == False`であること、この状態で疑似Click(`ExecuteEvents.Execute`)を送っても実際にはPhaseが変化しないことを確認
- **Host Authority Phase遷移**: `NetworkSessionState.HostAdvanceToStageSelect(2)`を直接呼び出し(Player数条件を満たした状況を再現)、`NetworkSessionState.Phase`が`StageSelect`に変化し、`PhaseChanged`イベント経由で`MainMenuController.CurrentState`も自動的に`StageSelect`へ切り替わることを確認。スクリーンショットで、Online経路から到達した`StageSelectPanel`がStep 9Aと同じ4×2 Gridで正しく表示されることを確認
- **Leave**: `LeaveAsync()`実行後、`NetworkManager.IsListening == False`・`OnlineSelectPanel`へ復帰することを確認。前述のTimeout Guard追加後の再検証では数秒以内に正常完了することを確認
- **Invalid Join Code**: 無効なCode(`"ZZZZZZ"`)でJOINを実行し、UIには`"Could not join the room.\nPlease check the Join Code."`という一般的なMessageのみが表示され、Consoleには実際の`SessionException`(`"lobby code 'ZZZZZZ' contains an invalid character 'Z'..."`)が出力されることを確認。Menuが操作不能にならないことも確認
- Console: 本Step実装コード由来の(意図的なInvalid Join Codeテスト・Timeout Guard検証を除く)Error/Warningは最終確認時点で0件

### 未実装/確認できなかった事項

- **実際の2人接続(2人目のClientによる本物のJoin)の直接確認**: MPPM Virtual Playerへの自動操作制約(これまでのStepと同じ)に加え、今回はさらに「同一Editorプロセスから2つの独立したSession/Relay接続を同時に張る」こと自体が実質的に困難なため、Host自身の1接続のみで検証しました。Player数2人以上が必要な分岐(`HostAdvanceToStageSelect`のGuard、Room UIの`canStart`判定)については、実際のPlayer数を引数/プロパティとして直接渡す形で分岐ロジック自体は検証済みですが、「本物の2人目のClientが実際にJoinしてPlayerCountが2になる」という結合的な確認は開発者ご自身での実機確認を推奨します
- Client側(2人目)のUI("Waiting for the host...")表示は、コード上は`_session.IsHost`の分岐で実装済みですが、実際のClient画面での視覚確認はできていません

### 将来確認が必要な事項

- **`ISession.LeaveAsync()`のHang事象**: 今回1度だけ観測し、Timeout Guardで実害を防止済みですが、根本原因(Relay接続の劣化か、SDK側の既知の挙動か)は特定できていません。再発するようであれば、Unity側のIssue Tracker等の確認を推奨します
- Step 9C(Online Stage Select本実装、START GAME、Late Join禁止、Network Scene Transition)で、実際に2人以上でのEnd-to-Endテストが必要です

---

## Step 9C: Online Stage Select / Network Scene Start / Late Join Lock

Step 9Cでは、Online Stage Select(Host-only操作・Selected Stage同期)・START GAME(Server側Validation)・Late Join禁止(Connection Approval)・Network Scene Transitionを実装し、Step 9で予定していたOnline経路を完成させました。Step 9A/9BのSingle Player経路・既存Gameplay Systemには変更を加えていません。詳細は`Docs/OnlineSession.md`を参照してください。

### 実装済み

**Online Stage Select(Host-only操作)**
- `NetworkSessionState`に`NetworkVariable<int> SelectedStageId`と`SelectedStageChanged`イベントを追加
- `StageSelectController`を拡張: `GameSessionContext.IsOnline`に応じてCard Clickの挙動を分岐(Single Player=即座に開始/Online=Host-only選択のみ)。`RefreshOnlineUI()`が全Card の`interactable`(Host-onlyでClientは操作不可)・選択中Cardのハイライト色・`selectionStatusText`("SELECTED: Stage X" / "Host selected: Stage X" / "Waiting for the host to choose a stage...")を、`SelectedStageChanged`イベント受信のたびに一括更新
- `StageSelectPanel`にHost専用の`START GAME`ボタンを追加(`GameSessionContext.IsOnline`の間だけ表示、`interactable`はHostかつStage選択済みの場合のみ)

**START GAME(Server側Validation)**
- `NetworkSessionState.HostTryStartGame(int connectedPlayerCount)`: Host限定、現在Phaseが`StageSelect`であること・Player数2〜4であること・有効なStageが選択済みであることをServer側で独立に再検証してから`Phase`を`StartingGame`へ進める。呼び出し元(UI)が既にGateしていても信用しない設計(第37章の要件どおり)
- `OnlineSessionController.OnStartGameButtonClicked()`: Host限定、`HostTryStartGame()`成功後に`IHostSession.IsLocked = true`(Session自体のLock)→`GameSessionContext.SelectedStageId`設定→`NetworkManager.SceneManager.LoadScene(...)`という順序で実行。Session Lock確定後にScene Loadを行うことで、開始決定とLock確定の間に別Clientが割り込む余地を最小化(第57章)

**Late Join禁止(Connection Approval)**
- `NetworkManager.NetworkConfig.ConnectionApproval = true` + `ConnectionApprovalCallback`を、Services初期化と同じ経路(Create/Join呼び出し前に必ず実行)で設定
- `HandleConnectionApproval()`: `NetworkSessionState.Phase`が`StartingGame`/`InGame`の場合のみServer側で接続を拒否(Reason付き)。Room/Stage Select中のJoinは許可(第39章の仕様どおり)。Client側のUI非表示だけでなく、Server側でも独立して拒否する構成(第40章の要件どおり)

**Network Scene Transition**
- Online経路も、Single Player(Step 9A)と全く同じ`NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single)`を使用。Online専用のScene遷移ロジックを別途作っていません

### 動作確認済み

引き続きMPPM Host Tagによる自動Gameplay突入を避けるため`NetworkBootstrap`を一時Disableして検証しました。

- **Host-only操作制限**: Client視点相当(`interactable = false`)でのCard Clickが実際にHandlerへ到達しないことを確認
- **Selected Stage同期**: Stage 5をHost視点で選択 → 該当Cardが黄色にハイライトされ、`SELECTED: STAGE 5`が表示されることをスクリーンショットで確認
- **START GAME Server-side Validation(重要な発見)**: 検証環境では実際の接続Playerが1人(Host自身)のみだったため、`OnStartGameButtonClicked()`(実際のPlayer数=1を`HostTryStartGame`へ渡す通常経路)は**意図通り拒否**されました(Console: `"server-side validation rejected the request"`)。これは実装の不具合ではなく、Player数不足を正しく検知した証拠です。Validationロジック自体は`HostTryStartGame(2)`のように条件を満たす人数を直接渡すことで独立して検証し、Phaseが正しく`StartingGame`へ遷移することを確認
- **Session Lock**: `IHostSession.IsLocked = true`の設定が正しく反映される(`session.IsLocked == true`)ことを確認
- **Network Scene Transition(Online経路)**: 上記の検証手順に続けて`NetworkManager.SceneManager.LoadScene()`を実行し、実際にSampleSceneへ遷移、Player Spawn・BUILD TIME開始まで正常に動作することを確認(Single Playerと同じ経路であることを実機で裏付け)
- **Connection Approval設定**: `NetworkManager.NetworkConfig.ConnectionApproval == true`かつ`ConnectionApprovalCallback`が正しく登録されていることを確認。Callback自体(`HandleConnectionApproval`)は、NGOが内部的に生成する`ConnectionApprovalRequest`/`ConnectionApprovalResponse`型を手動`new()`で構築すると内部初期化不足によりNullReferenceExceptionになるため、疑似呼び出しでの直接テストはできませんでした。ロジック自体はReflectionで確認した型定義どおりの単純なField設定のみです
- **Single Player Regression**: Step 9C変更後も、TIME ATTACK→STAGE SELECT→Stage選択→Gameplay(Player Spawn/Domino配置/BUILD TIME開始)が引き続き正常に動作することを確認
- Console: 本Step実装コード由来の(意図的なValidation拒否テストを除く)Error/Warningは最終確認時点で0件

### 未実装/確認できなかった事項

- **実際の2人以上でのEnd-to-Endテスト**: MPPM Virtual Player自動操作の制約に加え、同一Editorプロセスから2つの独立したRelay接続を同時に張ること自体が困難なため、Host自身の1接続のみで検証しました。「本物の2人目のClientが実際にJoinし、Connection Approvalを通過し、Stage SelectでHostの選択をリアルタイムに見て、START GAME後に一緒にGameplayへ遷移する」という完全な結合テストは、開発者ご自身での実機確認を強く推奨します
- Late Join拒否の完全なEnd-to-End確認(実際にGame開始後の第三者による接続試行とRejectの確認)も同様の制約で未実施です

### 将来確認が必要な事項

- 実際の2人以上でのEnd-to-Endテスト(上記)
- Start Race Condition(第57章)は設計上"Phase変更→Lock→Scene Load"の順序で対策していますが、実際に複数Clientが同時にJoinを試みる高負荷状況での確認はできていません

---

## Step 9.1: Main Menu Startup Flow Fix

Step 9完了後、実機で「MainMenu Sceneを開いてPlayすると、何も操作していないのにGameplay Sceneへ自動遷移してしまう」というバグ報告を受け、その修正のみを行いました。Gameplay側・Step 9で実装したOnline Architecture(NGO/UnityTransport/Multiplayer Services/Relay/Session API/Create Room/Join Room/Host Stage Selection/Late Join Lock/Persistent NetworkManager等)には一切変更を加えていません。

### Root Cause

実際の`NetworkBootstrap.cs`のCodeを読んで確認したところ、報告どおりの原因でした。

- `NetworkBootstrap.Start()`が無条件に`TryStartFromMultiplayerPlayModeTags()`を呼んでいました
- `TryStartFromMultiplayerPlayModeTags()`は`Unity.Multiplayer.PlayMode.CurrentPlayer.Tags`(MPPM Player識別用のTag)に`"Host"`が含まれているかどうかだけを判定条件にしており、それ以外の「本当にAuto-startして良いか」の確認が一切ありませんでした
- 本Projectでは開発テストの都合上、Main EditorにMPPM `"Host"` Tagが恒久的に設定されているため、**普通にPlay Modeへ入っただけで**この判定が`true`になり、即座に`NetworkManager.StartHost()`→`LoadDevelopmentGameplaySceneIfServer()`(`NetworkManager.SceneManager.LoadScene("SampleScene", LoadSceneMode.Single)`)が実行され、MainMenuのTITLE画面が完全にSkipされてGameplayへ直行していました
- MPPM Tagは本来「Development上のPlayer識別情報」でしかなく、「今すぐAuto-startして良い」という明示的な合図ではないにも関わらず、両者を区別せず同一視していたことが根本原因です
- CLI引数(`-host`/`-client`)経路(`StartFromCommandLineArgs`)自体は、元々`if (Application.isEditor) return;`によりEditor内では機能しない設計だったため、今回のバグには関与していませんでした

### Fix

**Normal Startup**: 通常のGame起動・通常のPlay Modeでは、`NetworkBootstrap`は一切Networkを開始しません。`NetworkManager.IsListening`は`false`のまま、MainMenuのTITLE画面で待機します。

**Development Auto-start Mode(Explicit Opt-in化)**: `NetworkBootstrap`に`developmentAutoStartViaMppmTag`(`bool`、Inspector、既定値`false`)を追加しました。

```csharp
private void Start()
{
    if (developmentAutoStartViaMppmTag && TryStartFromMultiplayerPlayModeTags()) return;
    StartFromCommandLineArgs();
}
```

この値が`true`の場合のみ、以前と同じMPPM Tagベースの自動Host/Client起動+Gameplay Scene Auto Loadが働きます。既定は`false`のため、MPPM `"Host"` Tagが付与されていても、それだけではAuto-startしません。MPPM/CLIによる開発テスト自体は削除しておらず、開発者がInspector上で明示的にONにした場合のみ以前の一段階Auto-start体験を利用できます。

**MPPM Behavior**: MPPM Tag自体は引き続き`CurrentPlayer.Tags`から読み取り可能なまま残していますが(Development上のPlayer識別情報として)、Menuを自動Skipするトリガーとしては使用しなくなりました。

**CLI Behavior**: `StartFromCommandLineArgs()`(`-host`/`-client`)は変更していません。元々`Application.isEditor`によりEditor内では機能せず、Standalone Buildを実際にコマンドライン引数付きで起動した場合にのみ働く、既に十分Explicitな経路だったためです。

### 動作確認済み(Runtime Test)

Step 9.1の修正により、これまでのStepで必要だった「検証のたびに`NetworkBootstrap`を手動でDisableする」という回避策が**不要になりました**(この修正自体がその回避策を本来の形で解消するものです)。

- **Test A(No Input)**: Play Mode開始直後、`MainMenuController.CurrentState == Title`・`NetworkManager.IsListening == false`・`ActiveScene == "MainMenu"`であることを確認。10秒以上経過後も状態が変化しない(Still TITLE、No Host、No Client、No Scene Transition)ことを実時間で確認
- **Test B(Single Player)**: TITLE→START(本物のButton Click経路)→TIME ATTACK→STAGE SELECT→Stage 1選択→`NetworkManager.IsHost/IsListening == true`→`ActiveScene == "SampleScene"`→Domino配置・BUILD TIME開始まで正常に動作することを確認
- **Test C(Online Create)**: TITLE→START→ONLINE PLAY→CREATE ROOM**によって初めて**Relay Sessionが作成され、実際のJoin Code(例: `N96JBW`)が発行・表示されることを確認。MainMenu表示直後(TITLE/MODE SELECT/ONLINE SELECT滞在中)にSessionが自動作成されないことも、Create Room操作前の`NetworkManager.IsListening == false`確認により裏付け
- **NetworkManager状態**: 修正後もMainMenu Sceneに`NetworkManager`(+`UnityTransport`/`NetworkBootstrap`/`DominoSpawner`/`DominoCleanupManager`)が存在すること自体は変更していません(Persistent NetworkManager Architecture自体は維持)。通常起動時に`IsListening == false`の状態でTITLE画面が表示されることを確認しました
- **MPPM Virtual Player**: 検証時、実際にMPPM Virtual Player("Player 2 [Client]")プロセスが起動していることを確認しましたが、これまでのStepと同じ制約(専用のUnityMCPブリッジを持たないプロセスへの直接的な内部状態確認・操作ができない)により、そのProcess自身のGame Viewでの直接目視確認はできませんでした。Main Editorと完全に同一のCompiled Code・同一のSaved Sceneを実行するプロセスであるため、Main Editorでの確認結果から同じ挙動になることを合理的に推定していますが、独立した目視確認ではない点を明記します
- Console: 本Step実装コード由来のError/Warningは最終確認時点で0件(既知/Step 9B以来のOnline Room Leave Timeout Warningを除く、後述)

### Regression

- **Stage Select**: Test Bで正常動作を確認(上記)
- **Relay/Session**: Test Cで正常動作を確認(上記)。CREATE ROOM実行後、実際のUnity Relayへ接続し本物のJoin Codeが発行されることを確認
- **Online Room / Host Stage Selection**: Step 9B/9Cで実装したRoom UI・Host-only Stage Select自体はコード変更していないため、機能的な差分はありません(Test Cの範囲でOnline Room画面の表示自体は確認済み)
- **Gameplay(BUILD TIME/READY/Failure/Success/Retry等)**: Test Bの範囲でPlacement・BUILD TIME開始を確認。Gameplay Script自体は一切変更していません(Freeze対象、変更なし)

### Console

最終確認時点でProject由来のError 0件。Warning 1件(`"[OnlineSessionController] LeaveAsync() timed out after 5s..."`) - これはStep 9Bで既に文書化済みの既知挙動(検証環境でのRelay接続劣化に起因すると推定される、Session離脱時のTimeout Guard発火)であり、今回のStartup Flow修正とは無関係かつStep 9.1のスコープ外です。

### 未実装/確認できなかった事項

- MPPM Virtual Player自身の画面での直接目視確認(上記、これまでのStepと同じ制約)
- Standalone Build自体を実際にビルドしての起動確認(Editor Play Modeでの確認のみ実施)

### 将来確認が必要な事項

- Standalone Buildでの実機確認(TITLE画面から始まることの最終確認)
- `developmentAutoStartViaMppmTag`をON にした場合の、以前と同じMPPM Auto-start体験が引き続き機能することの確認(今回は既定値`false`での修正確認が主眼のため、ON側の再確認は簡易的なコードReviewに留まっています)

---

## Step 9.2: Gameplay Player Spawn Lifecycle Fix

Step 9.1完了後、開発者ご自身が実際に2人でOnline Playを確認し、Room→Join→Stage Select→Start Game→Scene遷移・Placement/BUILD TIME同期までは正常動作を確認された一方、「Gameplay Scene突入後、Host/Client両方のPlayerがずっと下へ落ち続け、CameraもPlayerを追いかけるためStage/Groundが画面上に見えなくなる」というバグ報告を受け、その修正のみを行いました。Step 9で実装したOnline Architecture(Relay/Session/Create Room/Join Room/Selected Stage同期/Late Join Lock)・Gameplay側の既存機能(Placement/BUILD TIME/READY/Failure Detection/SUCCESS/Retry/Domino Physics)・Menu Flow(Title/Mode Select/Online Select/Room/Stage Select、Step 9.1から変更なし)には、Player Spawnのタイミング以外、一切変更を加えていません。

### Root Cause(実測により特定)

Play Mode上での直接測定により、以下を確認しました。

- **Player Y座標の実測**: Online Room滞在中(まだ`MainMenu.unity`がActive Scene)の時点で、既にPlayerのY座標が`-149`まで落下していることを確認。Gameplay Scene(`SampleScene`)のLoad完了直後には`-937`までさらに落下
- **同一NetworkObjectであることの確認**: Scene遷移前後を通じてPlayer-タグ付きオブジェクトの数は常に1個のままであり、新規にもう1個生成されたわけではないことを確認(重複Spawnではない)
- **コード上の原因**: `OnlineSessionController.HandleConnectionApproval()`が`response.CreatePlayerObject = true`を無条件に設定していたため、NGOは**接続した瞬間に**(まだGroundの存在しない`MainMenu.unity`がActive Sceneの間に)`NetworkConfig.PlayerPrefab`(`Player.prefab`)を自動Spawnしていました。Rigidbodyは通常の重力・非Kinematicで生成されるため、そのまま落下を開始します。NGOはScene遷移時にPlayerのNetworkObjectを自動破棄しないため、**同一のPlayerがそのままMenu→Gameplayの`LoadSceneMode.Single`遷移を跨いで持ち越され**、Gameplay Scene側でも誰にも再配置されないまま落下を継続していました
- **Scene自体の健全性確認**: 上記の原因特定と並行し、`SampleScene`自体(`Ground`/`StartArea`/`OverviewCameraPoint`の存在、`GameFlowManager.CurrentPhase`の初期値)が正しくLoadされていることを確認し、Scene Load自体の失敗が原因である可能性を排除しました
- **Single Player版でも同一の脆弱性が構造的に存在**: `StageSelectController.StartLocalHostAndLoadGameplay()`も同じ`StartHost()`→Connection Approval経路を通るため、理論上は同じバグに晒されていました。実際には症状が出ていなかった理由は、Single Playerでは`StartHost()`と`SceneManager.LoadScene()`がほぼ同一フレームで連続実行されるため、実時間としての落下猶予がほぼ0秒だったためです(Onlineでは、Room/Stage Select滞在中の数秒〜数分間、猶予が生じていました)。「たまたま気づかれなかった」だけで、コード上のリスクとしては存在していたため、修正はOnline/Single Player両方に統一して適用しました

### Fix

**Preferred Architecture**として、「Gameplay Playerは接続時点ではなく、Gameplay SceneのLoadが完了した時点で初めて存在する」設計に変更しました。詳細な実装(コード・Sequence)は`Networking.md`「24. Player Spawn Lifecycle」、Connection Approval側の変更は`OnlineSession.md`第9章、Player.prefab自体への影響(無し)は`PlayerSystem.md`を参照してください。要点のみ記載します。

1. `OnlineSessionController.SetupConnectionApproval()`/`HandleConnectionApproval()`を`private instance`から`public static`/`private static`へ変更し、`response.CreatePlayerObject`を常に`false`に変更。Single Player側の`StageSelectController.StartLocalHostAndLoadGameplay()`も`StartHost()`直前にこの同じメソッドを呼ぶよう変更(コード重複を避け、Online/Single Playerで同一のApproval設定を共有)
2. `GameplayPlayerSpawner.cs`(新規, `Domino.Networking`名前空間, 通常の`MonoBehaviour`)を作成し、`MainMenu.unity`の`NetworkManager` GameObjectに追加。NGOの`NetworkManager.SceneManager.OnLoadEventCompleted`(Server専用)を購読し、指定したGameplay Scene名(`SampleScene`)のLoadが完了したタイミングで、完了した各Clientにつき`NetworkObject.InstantiateAndSpawn(..., isPlayerObject: true, position: spawnPoint.position, ...)`を呼ぶ。`NetworkManager.OnServerStarted`経由で毎回のHost起動ごとに購読し直す(`-=`してから`+=`)ことで、同一Application内での複数回`StartHost()`(Leave→再Create、Single PlayerのRetry-into-a-new-session等)にも対応
3. `SampleScene.unity`の`StartArea`内に`PlayerSpawnPoints`(空の親)+`SpawnPoint_1`〜`SpawnPoint_4`(子Transform、最大4人分、互いに重ならない配置)を新規追加。`GameplayPlayerSpawner`は`clientsCompleted`配列のIndexを`spawnPoints.Length`で割った剰余でSpawn Pointを決定論的に割り当てる(Clientが選ぶことはできない)

### 動作確認済み(Runtime Test)

- **Root Cause実測**: 上記の通り、修正前のコードでY座標推移(`-149`→`-937`)を実測し、原因を裏付け
- **Single Player**: TITLE→START→TIME ATTACK→STAGE SELECT→Stage 1選択で、Playerが`SpawnPoint_1`(`-1, 0, -11`)に速度0で正しくSpawnし安定していること、Ground/Stageが画面上に見えること(Screenshotで視覚確認)、Placement・BUILD TIME開始が正常動作することを確認
- **Online(Host自身の1接続によるシミュレーション)**: Room滞在中は`ConnectedClients[0].PlayerObject == null`(Player未Spawn)であることを確認。`HostTryStartGame(2)`(人数を2人分に見せかけたServer側Validationの直接検証、Step 9Cから続く手法)→Scene遷移後、`SpawnPoint_1`へ正しくSpawnし安定していること(Screenshotで視覚確認)、Placement・BUILD TIME開始が正常動作すること、SpawnされたPlayerのComponent構成(`Transform`/`Rigidbody`/`CapsuleCollider`/`PlayerStats`/`NetworkObject`/`PlayerMotor`/`NetworkTransform`/`NetworkRigidbody`/`PlacementController`/`ChallengeInput`/`PlayerPushController`)が全て揃っていることを確認
- **Retry(In-place Reset)**: `GameFlowManager.TriggerFailure()`→`RetrySequence()`(Step 8.5から無変更のコード)実行後もPlayer-タグ付きオブジェクトが1個のまま(重複なし)、`SpawnPoint_1`へ正しく戻ることを確認。RetryはNGOレベルでの新規接続・新規Scene Loadを伴わないため`GameplayPlayerSpawner`は関与しない、という設計上の期待通りの結果でした
- **複数回の`StartHost()`(再購読ロジックの検証)**: `NetworkManager.Shutdown()`→MainMenuへScene再Load→再度Single Playerを開始、という手順で2回目の`StartHost()`+Gameplay Scene Loadを発生させ、2回目でもPlayerが正しく1体だけSpawnされる(重複なし)ことを確認。`GameplayPlayerSpawner`の`OnServerStarted`再購読ロジックが複数回のHost起動サイクルに対して正しく機能することの実証です
- Console: 上記すべてのテストを通じてError/Warning 0件

### Regression

- **Placement/BUILD TIME**: Single Player・Online(シミュレーション)双方で確認(上記)
- **Retry**: In-place Resetが正しく機能することを確認(上記)。Domino Despawn・`ResetRunState()`・Timer Resetを含むRetry本体のロジック自体はStep 8.5から無変更
- **Online Architecture(Relay/Session/Room/Stage Select/Late Join Lock)**: `HandleConnectionApproval()`のLate Join拒否ロジック(`response.Approved`側)・`NetworkSessionState`のPhase同期・Selected Stage同期はいずれも無変更。`CreatePlayerObject`の値のみを変更しています
- **Menu Flow**: Title/Mode Select/Online Select/Room/Stage SelectのPanel遷移はStep 9.1から無変更。Play Mode終了後のMainMenu Canvas Panel状態(TitlePanelのみActive)を最終確認し、正しいことを確認した上でSceneを保存しました

### Console

最終確認時点でProject由来のError/Warning 0件。

### 未実装/確認できなかった事項

- **実際の2台のClientによるEnd-to-End確認**: Step 9・9.1から持ち越しの制約(同一Editorプロセスから2つの独立したRelay接続を同時に張ることが困難)により、Host自身の1接続によるシミュレーションのみで検証しました。開発者ご自身が既に2人でRoom〜Scene遷移までを確認されている状況を踏まえ、続く「Gameplay Scene突入後、両Playerが正しい位置にSpawnし落下しないこと」についても、開発者ご自身での再確認を推奨します
- **4人接続時のSpawn Point割り当て**: 実際に4接続を同時に用意することが困難だったため、3人目・4人目が`SpawnPoint_3`/`SpawnPoint_4`に正しく割り当てられることは未検証です。コード上は`clientIndex % spawnPoints.Length`のロジックで対応済みです
- **READY/RUNNING/FAILED/SUCCESSを含む完全なGameplay Loop**: 今回は`GameFlowManager.TriggerFailure()`/`RetrySequence()`をReflection経由で直接呼び出す形でRetry経路を検証しましたが、実際にDominoを配置してChain Reactionを起こし、Start→CP→Goalの正規経路でSUCCESSに到達する完全なEnd-to-Endの通し確認は今回のStepでは行っていません(Step 6/6.1/8.5で既に検証済みのロジック自体には手を加えていないため、Player Spawnタイミングの変更がこれらに影響する経路は無いと判断していますが、通し確認そのものは未実施です)

### 将来確認が必要な事項

- **実際の2人以上でのGameplay Scene突入後の確認**(上記参照、最優先)
- 4人プレイ時のSpawn Point割り当て(上記参照)

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
