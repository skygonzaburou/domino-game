# ImplementationStatus(実装状況の整理)

Step 0〜Step 4について、「実装済み」「動作確認済み」「未実装」「将来確認が必要な事項」を整理します。ここでいう「動作確認済み」は、Unity Editor上での実機確認(スクリーンショット・ネットワーク状態の直接確認等)によって裏付けが取れているものだけを指し、コードを書いただけで未検証のものとは区別しています。

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
- Step 5でDominoをNetworkObject化する際、`PlacementController.ConfirmPlacement()`のローカル`Instantiate()`をHost側でのNetwork Spawn呼び出しに置き換える設計変更が必要になります
- Step 3で確認された「間隔0.5m〜0.7mでの非決定的なChain Reaction」の問題は、プレイヤーが手動で配置する間隔にも同様の影響を与えうるため、Step 5以降で配置間隔の目安をUI等でガイドすることを検討する価値があります
