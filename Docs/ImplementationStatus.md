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
