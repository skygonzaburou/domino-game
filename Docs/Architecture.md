# Architecture(全体構成)

このドキュメントは、2026年時点でプロジェクトに**実際に実装されている内容**を、Project / Scene / Prefab / Script / Package の実体を確認した上でまとめたものです。`GameDesign.md` に書かれている将来仕様の多くはまだ実装されていません。ここに書かれているのは「Step 0〜Step 6」で作られた、オンライン対応の土台部分、Network化されたHost-Authoritative Domino物理、ネットワーク非対応のGhost Domino配置システム(Placement Marker含む)、そしてHost AuthorityのGameFlow(BUILDING/READY/RUNNING/SUCCESS)です。GameFlowの詳細は`GameFlow.md`、Stageの組み立て方は`StageAuthoring.md`を参照してください。

---

## 1. Project全体構成

```
Assets/
├─ _Project/                      … このプロジェクト専用のコード・アセット置き場
│  ├─ Scripts/
│  │  ├─ Core/                    … (空フォルダ。将来のゲーム全体管理スクリプト用に予約)
│  │  ├─ Player/
│  │  │  ├─ PlayerStats.cs
│  │  │  ├─ Movement/
│  │  │  │  └─ PlayerMotor.cs
│  │  │  ├─ Placement/             … Step 4で追加
│  │  │  │  ├─ PlacementController.cs
│  │  │  │  └─ PlacementMarker.cs  … Step 6で追加
│  │  │  ├─ ChallengeInput.cs      … Step 6で追加(Fキーで Start Challenge RPCを送信)
│  │  │  ├─ Visual/               … (空フォルダ。将来のAnimator接続・見た目切り替え用に予約)
│  │  │  └─ Network/              … (空フォルダ。現状ネットワーク処理はPlayerMotorに同居)
│  │  ├─ Domino/
│  │  │  ├─ GhostDomino.cs        … Step 4で追加
│  │  │  ├─ DominoSpawner.cs      … Step 5で追加(Host専用のValidation・Spawn処理)。Step 7でCleanup登録処理を追加
│  │  │  └─ DominoCleanupManager.cs … Step 7で追加(Host専用。BUILDING中に倒れたDominoを一定時間後にNetwork Despawn)
│  │  ├─ GameFlow/                … Step 6で追加
│  │  │  ├─ GamePhase.cs
│  │  │  ├─ GameFlowManager.cs
│  │  │  ├─ DominoFallenZone.cs   … Start/Checkpoint/Goalの共通基底クラス
│  │  │  ├─ StartAreaZone.cs
│  │  │  ├─ CheckpointZone.cs
│  │  │  └─ GoalAreaZone.cs
│  │  ├─ Networking/
│  │  │  ├─ NetworkBootstrap.cs
│  │  │  └─ GameplayPlayerSpawner.cs … Step 9.2で追加
│  │  └─ CameraSystem/
│  │     ├─ ThirdPersonCamera.cs  … Step 4でPlacement View機能を追加。Step 7.6でOverview Camera機能を追加
│  │     └─ OverviewCameraPoint.cs … Step 7.6で追加。Stage側に配置するOverview視点マーカー
│  ├─ Prefabs/
│  │  ├─ Player.prefab            … Step 4で PlacementController、Step 6で ChallengeInput を追加
│  │  ├─ Domino.prefab            … Step 3で作成。Step 5でNetwork化。Step 6でWidthを0.30→0.35に調整
│  │  ├─ GhostDomino.prefab       … Step 4で追加。Step 6でDomino.prefabに合わせWidthを0.35に調整
│  │  ├─ PlacementMarker.prefab   … Step 6で追加。ネットワーク非対応のLocal専用UI的表示
│  │  └─ GameFlow/                … Step 6で追加
│  │     ├─ StartArea.prefab
│  │     ├─ Checkpoint.prefab
│  │     └─ GoalArea.prefab
│  ├─ Materials/                  … Step 4で追加
│  │  ├─ GhostDominoMaterial.mat  … URP/Lit、半透明(Transparent Surface)
│  │  ├─ PlacementMarkerMaterial.mat  … Step 6で追加
│  │  ├─ StartAreaMaterial.mat        … Step 6で追加(青)
│  │  ├─ CheckpointMaterial.mat       … Step 6で追加(黄)
│  │  └─ GoalAreaMaterial.mat         … Step 6で追加(緑)
│  └─ Physics/                    … Step 3で追加
│     ├─ DominoPhysicsMaterial.physicMaterial
│     └─ GroundPhysicsMaterial.physicMaterial
├─ Scenes/
│  └─ SampleScene.unity           … Gameplay Scene(Build Index 1)
├─ _Project/Scenes/
│  └─ MainMenu.unity              … Menu/Session Scene(Step 9、Build Index 0)。NetworkManagerはここに常駐
├─ Settings/                      … URPテンプレート標準(Renderer / VolumeProfile等)
├─ TutorialInfo/                  … Unityテンプレート標準のReadme(未使用)
├─ InputSystem_Actions.inputactions … テンプレート標準のInput Actionアセット(現状未使用。入力は後述の通りKeyboard/Mouseを直接ポーリング)
└─ DefaultNetworkPrefabs.asset    … Netcode for GameObjectsが管理する「ネットワーク経由でSpawnできるPrefab一覧」。Player.prefab・Domino.prefab(Step 5で追加)が自動登録済み
```

`_Project` 配下だけがこのゲーム固有の実装で、それ以外(`Settings`, `TutorialInfo` など)はUnityの「Universal 3D」テンプレートが最初から用意していたものです。

`Visual` フォルダと `Network` フォルダは当初の設計案どおりに作成しましたが、現時点では中身を持つほど分離すべき処理がなかったため空のままです(詳細はPlayerSystem.mdの「Local/Remoteでの違い」を参照)。

---

## 2. Package構成

`Packages/manifest.json` に実際に記載されている依存関係のうち、このゲームに関係するものだけを抜粋します。

| Package | Version | 用途 |
|---|---|---|
| `com.unity.netcode.gameobjects` | 2.13.2 | オンラインマルチプレイの本体(NetworkManager, NetworkObject, NetworkTransform等) |
| `com.unity.transport` | 6.5.0(NGOの依存として自動導入) | 実際にUDP通信を行う低レベル通信層(UnityTransport) |
| `com.unity.multiplayer.playmode` | 2.0.2 | Unity Editor内で2つ以上のPlayerを同時起動してテストする機能(MPPM) |
| `com.unity.inputsystem` | 1.20.0 | キーボード/マウス入力の取得(`Keyboard.current`, `Mouse.current`) |
| `com.unity.render-pipelines.universal` | 17.5.0 | 描画パイプライン(URP)。テンプレート標準だが実際に使用中 |

以下はUniversal 3Dテンプレートに最初から含まれていたもので、**現状このゲームのコードからは使用していません**(将来UIやアニメーション制作で使う可能性はあります):
`com.unity.ai.navigation`, `com.unity.multiplayer.center`, `com.unity.collab-proxy`, `com.unity.ide.rider` / `ide.visualstudio`, `com.unity.test-framework`, `com.unity.timeline`, `com.unity.ugui`, `com.unity.visualscripting`, および各種 `com.unity.modules.*`。

また `com.coplaydev.unity-mcp`(MCP for Unity)は、Claude Codeなど AIツールからEditorを操作するための**開発補助ツール**であり、ゲーム本体の一部ではありません。

---

## 3. Scene構成

Step 9より、Scene は `MainMenu.unity`(Build Index 0)と `SampleScene.unity`(Build Index 1、Gameplay Scene)の2つで構成されています。起動時は必ずMainMenuから始まり(TITLE→MODE SELECT→…)、Player操作またはDevelopment Auto-start(MPPM Tag/CLI引数)経由でGameplay Sceneへ`NetworkManager.SceneManager.LoadScene(..., LoadSceneMode.Single)`により遷移します。詳細は`MenuFlow.md`を参照してください。

### MainMenu.unity(Step 9)

`Main Camera`(UI描画用の単純なCamera)・`EventSystem`(`InputSystemUIInputModule`)・`Canvas`(`MainMenuController`、TitlePanel等7Panel)に加え、**`NetworkManager`(`NetworkManager`/`UnityTransport`/`NetworkBootstrap`/`DominoSpawner`/`DominoCleanupManager`/`GameplayPlayerSpawner`)がこのSceneに常駐**しています(Step 9より前はSampleScene側にありました。`GameplayPlayerSpawner`はStep 9.2で追加)。`NetworkBootstrap.Awake()`で明示的に`DontDestroyOnLoad`されるため、Gameplay SceneへのScene遷移(`LoadSceneMode.Single`)を跨いでも同一インスタンスが生き続け、SampleScene側にNetworkManagerが重複生成されることはありません。`GameFlowManager`/`Domino`/`Player`等のGameplay Objectはこちらには一切配置されていません(`Player`自体はGameplay Scene Load完了時に`GameplayPlayerSpawner`がSpawnします、Step 9.2)。

### SampleScene.unity(Gameplay Scene)

Root GameObjectは次の12個です(Step 8Bで追加した`GameplayHUD`、Step 9.2で追加した`PlayerSpawnPoints`、Step 10Aで追加した`GameplayPauseManager`、Step 10Bで追加した`GameplayNavigationController`を含みます)。

| GameObject | 主なComponent | 役割 |
|---|---|---|
| **Main Camera** | Camera, AudioListener, `ThirdPersonCamera` | 三人称カメラ本体。Scene起動時から常に存在する |
| **Ground** | MeshFilter/MeshRenderer(Plane), MeshCollider | 30m×30mの床(Planeプリミティブをscale(3,1,3)で拡大)。MeshColliderには`GroundPhysicsMaterial`を設定 |
| **Directional Light** | Light | Universal 3Dテンプレート標準のライト |
| **Global Volume** | Volume | URPのポストプロセス設定(テンプレート標準) |
| **DominoLine** | Transform のみ(空の親) | Step 3で追加。`Domino.prefab` のインスタンス20個(`Domino_00`〜`Domino_19`)をまとめる親。Playerのスポーン地点と重ならないよう原点から離れた位置に配置。**Step 4時点では非アクティブ化**してあります(削除はしていません。Step 3の検証内容は保持したまま、通常プレイの邪魔にならないようにするための措置) |
| **GameFlowManager**(Step 6) | `NetworkObject`, `GameFlowManager`, `ChallengeTimer` | GamePhase・Checkpoint進捗をHost Authorityで管理するScene-placed NetworkObject。Playerのように動的生成されるのではなく、Scene読み込み時から存在する |
| **GameplayPauseManager**(Step 10A) | `NetworkObject`, `GameplayPauseManager` | Global PauseのServer Authoritative State(`NetworkVariable<bool>`)を持つScene-placed NetworkObject。`GameFlowManager`とは別GameObject(責務分離)。詳細は`PauseSystem.md`を参照 |
| **GameplayNavigationController**(Step 10B) | `GameplayNavigationController` | Gameplay → MainMenuへの遷移(Pause MenuのStage Select/Return to Title、SUCCESS Resultの同機能)を担う、通常の(NetworkObjectではない)`MonoBehaviour`。詳細は`MenuFlow.md`「10. Gameplay → MainMenu Navigation」を参照 |
| **GameFlowZones**(Step 6) | Transform のみ(空の親) | `StartArea` / `Checkpoint_01` / `Checkpoint_02` / `GoalArea` の4つのZoneインスタンスをまとめる親。実際のコース配置はStageごとに変わる想定で、詳細はStageAuthoring.mdを参照 |
| **CameraPoints**(Step 7.6) | Transform のみ(空の親) | `OverviewCameraPoint` をまとめる親。RUNNING/SUCCESS/FAILED中のOverview Camera視点をStage側で設定するためのマーカー。詳細はStageAuthoring.md第9章を参照 |
| **PlayerSpawnPoints**(Step 9.2) | Transform のみ(空の親) | `SpawnPoint_1`〜`SpawnPoint_4`(子Transform、Start Area内・互いに重ならない配置)をまとめる親。`GameplayPlayerSpawner`(MainMenu Scene側、後述)が`GameObject.Find("PlayerSpawnPoints")`でこのSceneロード後に検索し、接続Client順に(`clientIndex % 4`)割り当てる。`NetworkObject`は持たない、純粋なLocal配置マーカー。Step 10Aで、Restart/Retry時のPlayer再配置(`GameFlowManager.RepositionAllPlayersToSpawnPoints()`)にも同じ解決ロジックを再利用しています |
| **GameplayHUD**(Step 8B) | Canvas, CanvasScaler, GraphicRaycaster, `GameplayHUD` | Phase別のGameplay UI(BUILD TIME・READY・FAILED・SUCCESS Result等)。Step 10AでPause Panel・Confirmation Dialogを追加。詳細は`GameFlow.md`「Challenge Timer / Gameplay HUD」・`PauseSystem.md`を参照 |

**重要**: このScene自体には **Playerは1体も配置されていません**。Playerは接続時点ではなく、**このSceneのLoadが完了した時点で** `GameplayPlayerSpawner`(MainMenu Scene側常駐、後述)が`Player.prefab`を都度生成(Spawn)する形で登場します(Step 9.2、詳細はNetworking.md「Player Spawn Lifecycle」章を参照)。「Editor上で目に見える固定のキャラクター」ではなく「Gameplay Sceneのロードが完了した接続Clientの数だけ動的に生成されるキャラクター」として扱うためです。一方 `DominoLine` 配下のDominoは**Editor上に直接配置されたテスト用インスタンス**です。Step 3時点では`Domino.prefab`が`NetworkObject`を持たなかったため同期対象外でしたが、Step 5で`Domino.prefab`に`NetworkObject`が追加されたことで、これら20個も「In-Scene-Placed NetworkObject」という扱いを継承しています。現在`DominoLine`は非アクティブのため実際にSpawn/同期が走ることはありませんが、将来再度アクティブ化して使う場合はこの点に注意してください(詳細はImplementationStatus.mdのStep 5章を参照)。

Step 4で追加したGhost Dominoは、Playerと同様に**Scene上には直接配置されておらず**、`PlacementController`が実行時に`Instantiate()`する形で登場する、完全にLocal限定の見た目専用オブジェクトです(`NetworkObject`を持ちません)。Ghostは`PlacementController`ごとに1体だけ生成され、非表示⇄表示を繰り返して使い回されます(Placement Modeを抜けるたびに`Destroy`されるわけではありません)。一方、確定後のReal Dominoは、Step 4では確定したClient自身がローカルで`Instantiate()`していましたが、Step 5でこれをやめ、**必ずHost側の`DominoSpawner`が`NetworkObject.InstantiateAndSpawn()`で生成する**方式に変更しました(詳細はNetworking.mdの第17〜19章を参照)。

Step 6で追加したPlacement Markerも、Ghostと同じく`PlacementController`が実行時に`Instantiate()`する完全Local限定オブジェクトです(`NetworkObject`を持ちません)。Ghost/Markerとの違いはNetworking.mdではなくPlayerSystem.mdの「Placement Mode」章で詳しく説明しています。

---

## 4. GameObject構成の詳細

### Main Camera
`ThirdPersonCamera` コンポーネントが付いており、初期値は `distance=4, pitchMin=-30, pitchMax=60, sensitivity=2, lockCursor=true` です。起動直後はまだ誰も追従対象になっていませんが、Playerがスポーンすると自動的にそのPlayerを追うようになります(詳細はPlayerSystem.md)。

### NetworkManager
6つのコンポーネントが同じGameObjectに載っています。
- `NetworkManager`: Netcode for GameObjects本体の管理者
- `UnityTransport`: 実際の通信を行う部品(接続先 `127.0.0.1:7778`)
- `NetworkBootstrap`: 誰がHostで誰がClientかを判断して自動的に起動処理を行う自作スクリプト
- `DominoSpawner`(Step 5で追加): Host上でのみ、Domino配置要求の検証とSpawnを行う自作スクリプト。`NetworkBehaviour`ではなく普通の`MonoBehaviour`で、`PlacementController`からのRPCを受けて呼び出される
- `DominoCleanupManager`(Step 7で追加): Host上でのみ、BUILDING中に倒れたまま一定時間経過したDominoをNetwork Despawnする自作スクリプト。`DominoSpawner`と同じく普通の`MonoBehaviour`で、`DominoSpawner`がSpawn直後に明示的に`Register()`したDominoだけを追跡する
- `GameplayPlayerSpawner`(Step 9.2で追加): Host上でのみ、Gameplay SceneのLoad完了(`NetworkSceneManager.OnLoadEventCompleted`)を検知して`Player.prefab`を明示的にSpawnする自作スクリプト。詳細はNetworking.md「Player Spawn Lifecycle」章を参照

この6つの関係は Networking.md で詳しく説明しています。

### GameFlowManager(Step 6)
`NetworkObject`と`GameFlowManager`の2つが載っています。`NetworkManager`とは別のGameObjectで、Scene読み込み時から存在する「Scene-placed NetworkObject」です(Playerのように接続の都度動的生成されるのではありません)。`GamePhase`・Checkpoint進捗などを`NetworkVariable`で全Clientへ同期する司令塔で、詳細はGameFlow.mdを参照してください。

---

## 5. Script一覧と役割

| ファイル | クラス | 継承元 | 役割(一言で) |
|---|---|---|---|
| [PlayerStats.cs](../Assets/_Project/Scripts/Player/PlayerStats.cs) | `Domino.Player.PlayerStats` | `MonoBehaviour` | 移動速度などの数値を保持するだけの「データの入れ物」。ロジックは一切持たない |
| [PlayerMotor.cs](../Assets/_Project/Scripts/Player/Movement/PlayerMotor.cs) | `Domino.Player.PlayerMotor` | `NetworkBehaviour` | キーボード入力を読み取り、Rigidbodyを動かす。ネットワーク上で「自分のキャラクターかどうか」の判定もここで行う。Step 4で`SetMovementEnabled()`を追加(Placement Mode中の入力一時停止用)。Step 7.6でStart Area拘束(`ConstrainToStartArea()`)を追加。Step 7.7で移動基準を常にCamera相対(Third Person/Overview自動切替)へ統一し直した(Step 7.6のPlayer相対化は撤回)。Step 10Aで`ServerTeleportRpc()`を追加(Restart/RetryのPlayer Spawn Point再配置、`ApplyPushRpc`と同じOwner-Rpcパターン) |
| [PlacementController.cs](../Assets/_Project/Scripts/Player/Placement/PlacementController.cs) | `Domino.Player.PlacementController` | `NetworkBehaviour` | Step 4で追加。Placement Modeの状態管理(開始/確定/キャンセル)とGhost Dominoの生成を行う。Step 5で確定時の処理を「ローカルInstantiate」から「HostへのRPC送信」に変更。Step 7.7でAnchor計算を一時的にCamera Aim基準へ変更したが、Step 7.8でCharacter正面基準へ復帰し、代わりにPlacement Mode中の自由なDomino Yaw回転(A/D)を追加。Step 10AでPause中の入力ブロックと、読み取り専用の`IsPlacing`プロパティ(Escapeキーの優先順位判定用)を追加 |
| [GhostDomino.cs](../Assets/_Project/Scripts/Domino/GhostDomino.cs) | `Domino.DominoSystem.GhostDomino` | `MonoBehaviour` | Step 4で追加。配置プレビュー用のメトロノーム状の首振りアニメーションのみを行う。`NetworkObject`を持たない完全ローカルな見た目専用コンポーネント。Step 7.8で`SetBaseRotation()`を追加し、`PlacementController`からPlacement Mode中いつでもYawを更新できるようにした(Wobble計算自体は無変更) |
| [DominoSpawner.cs](../Assets/_Project/Scripts/Domino/DominoSpawner.cs) | `Domino.DominoSystem.DominoSpawner` | `MonoBehaviour` | Step 5で追加。Host上でのみ、`PlacementController`からのRPCを受けて配置要求を検証し、正式なDomino`NetworkObject`をSpawnする。Step 7でSpawn直後の`DominoCleanupManager.Register()`呼び出しを追加 |
| [DominoCleanupManager.cs](../Assets/_Project/Scripts/Domino/DominoCleanupManager.cs) | `Domino.DominoSystem.DominoCleanupManager` | `MonoBehaviour` | Step 7で追加。Host上でのみ、BUILDING中に倒れたまま`Cleanup Delay`秒経過したDominoを`NetworkObject.Despawn()`する。`DominoSpawner`が登録したDominoのみを一定間隔(`Check Interval`)でまとめて確認する(Domino単位の重い`Update()`は持たない) |
| [ThirdPersonCamera.cs](../Assets/_Project/Scripts/CameraSystem/ThirdPersonCamera.cs) | `Domino.CameraSystem.ThirdPersonCamera` | `MonoBehaviour` | マウスでのカメラ操作。ネットワーク非対応(あくまでローカルの見た目の話なので不要)。Step 4で`EnterPlacementView()`/`ExitPlacementView()`によるPlacement用ビューへの遷移を追加。Step 7.6で`GameFlowManager.IsChallengeActive`に連動したOverview Cameraへのブレンド(Position/Rotation/FOV)を追加 |
| [OverviewCameraPoint.cs](../Assets/_Project/Scripts/CameraSystem/OverviewCameraPoint.cs) | `Domino.CameraSystem.OverviewCameraPoint` | `MonoBehaviour` | Step 7.6で追加。Stage側に配置するOverview Camera視点のマーカー。Position/RotationはこのGameObjectのTransformそのもの、Field of ViewはInspector値。`NetworkObject`を持たない完全ローカルのStage設定用コンポーネント |
| [NetworkBootstrap.cs](../Assets/_Project/Scripts/Networking/NetworkBootstrap.cs) | `Domino.Networking.NetworkBootstrap` | `MonoBehaviour` | Scene起動時に「自分はHostなのかClientなのか」を判断し、`NetworkManager` に開始指示を出す |
| [GameplayPlayerSpawner.cs](../Assets/_Project/Scripts/Networking/GameplayPlayerSpawner.cs) | `Domino.Networking.GameplayPlayerSpawner` | `MonoBehaviour` | Step 9.2で追加。Host上でのみ、Gameplay SceneのLoad完了を検知し、接続中の各ClientにつきPlayerを1体、Scene内の`PlayerSpawnPoints`へ明示的にSpawnする |
| [PlacementMarker.cs](../Assets/_Project/Scripts/Player/Placement/PlacementMarker.cs) | `Domino.Player.PlacementMarker` | `MonoBehaviour` | Step 6で追加。床に表示するPlacement Markerの見た目(サイズ・不透明度)のみを扱う、完全ローカルの表示専用コンポーネント |
| [ChallengeInput.cs](../Assets/_Project/Scripts/Player/ChallengeInput.cs) | `Domino.Player.ChallengeInput` | `NetworkBehaviour` | Step 6で追加。`F`キー押下を`GameFlowManager`へのRPC呼び出しに変換する単機能スクリプト。Step 10AでPause中の入力ブロックを追加 |
| [GamePhase.cs](../Assets/_Project/Scripts/GameFlow/GamePhase.cs) | `Domino.GameFlow.GamePhase` | enum | Step 6で追加。BUILDING/READY/RUNNING/SUCCESSの4状態を定義 |
| [GameFlowManager.cs](../Assets/_Project/Scripts/GameFlow/GameFlowManager.cs) | `Domino.GameFlow.GameFlowManager` | `NetworkBehaviour` | Step 6で追加。GamePhase/Checkpoint進捗をHost Authorityで管理し、NetworkVariableで同期する司令塔。Step 10AでRetry/Restartの共有Reset処理(`FullStageResetSequence()`)とPlayer Spawn Point再配置を追加 |
| [GameplayPauseManager.cs](../Assets/_Project/Scripts/GameFlow/GameplayPauseManager.cs) | `Domino.GameFlow.GameplayPauseManager` | `NetworkBehaviour` | Step 10Aで追加。Global Pauseの Server Authoritative State(`NetworkVariable<bool>`)、`Time.timeScale`の適用、`ChallengeTimer`への同期的なPause通知を担う。詳細は`PauseSystem.md` |
| [DominoFallenZone.cs](../Assets/_Project/Scripts/GameFlow/DominoFallenZone.cs) | `Domino.GameFlow.DominoFallenZone` | `MonoBehaviour`(abstract) | Step 6で追加。Zone内でDominoが倒れたかどうかを判定する共通基底クラス |
| [StartAreaZone.cs](../Assets/_Project/Scripts/GameFlow/StartAreaZone.cs) | `Domino.GameFlow.StartAreaZone` | `DominoFallenZone` | Step 6で追加。Start Domino判定に加え、Player Ready判定(全員がStart Areaに入ったか)を行う。Step 7.6で`ClampToLocalBounds()`を追加(`PlayerMotor`のStart Area拘束が利用するLocal空間クランプ)。Step 7.7で`ContainsWorldPoint()`を追加(`GameFlowManager`のReady Re-arm自己修復が利用するGround Truth判定) |
| [CheckpointZone.cs](../Assets/_Project/Scripts/GameFlow/CheckpointZone.cs) | `Domino.GameFlow.CheckpointZone` | `DominoFallenZone` | Step 6で追加。Checkpoint判定と、明示的な通過順序(`CheckpointIndex`)を持つ |
| [GoalAreaZone.cs](../Assets/_Project/Scripts/GameFlow/GoalAreaZone.cs) | `Domino.GameFlow.GoalAreaZone` | `DominoFallenZone` | Step 6で追加。Goal判定を行う |
| [PlayerPushController.cs](../Assets/_Project/Scripts/Player/Movement/PlayerPushController.cs) | `Domino.Player.PlayerPushController` | `NetworkBehaviour` | Step 8Aで追加。Player同士の衝突を検出し、Server-validatedなRPCでPlayer-vs-Player Pushを実現する |
| [ChallengeTimer.cs](../Assets/_Project/Scripts/GameFlow/ChallengeTimer.cs) | `Domino.GameFlow.ChallengeTimer` | `NetworkBehaviour` | Step 8Bで追加。`GameFlowManager.CurrentPhase`を外側からPollingし、RUNNING開始時刻とFinal Timeのみを1回ずつ同期するHost Authoritativeなタイマー |
| [GameplayHUD.cs](../Assets/_Project/Scripts/UI/GameplayHUD.cs) | `Domino.UI.GameplayHUD` | `MonoBehaviour` | Step 8Bで追加。`GameFlowManager`/`ChallengeTimer`の状態をLocalで読み取り、Phase別のCanvas UIを表示するだけの純粋な表示コンポーネント(NetworkObjectではない) |

各スクリプトのField/Methodの詳細は `PlayerSystem.md`(PlayerMotor / PlayerStats / ThirdPersonCamera / PlacementController / PlacementMarker / GhostDomino)、`Networking.md`(NetworkBootstrap / DominoSpawner)、`GameFlow.md`(GameFlowManager / 各Zone / ChallengeInput)にまとめています。

---

## 6. Prefab構成

`Player.prefab`(`Assets/_Project/Prefabs/Player.prefab`)の階層構造:

```
Player (Root)
├─ Rigidbody
├─ CapsuleCollider
├─ PlayerStats
├─ PlayerMotor
├─ NetworkObject
├─ NetworkTransform
├─ NetworkRigidbody
├─ PlacementController     … Step 4で追加
├─ ChallengeInput          … Step 6で追加
├─ Visual                 (子GameObject, ローカル座標 0, 1, 0)
│   ├─ MeshFilter (Capsule)
│   ├─ MeshRenderer
│   └─ Nose                … Step 7.7で追加。正面(+Z)方向を示す小さな赤い球(Sphere、Collider無し)
└─ CameraTarget            (子GameObject, ローカル座標 0, 1.6, 0)
```

`Player.prefab`のLayerはStep 7.7で`"Player"`(新設)に変更しました。Placement Live PreviewのCamera Aim Raycastが自分自身のCapsule Colliderに当たらないようにするためで、Physics Layer Collision Matrix(実際の物理衝突)は変更していません。

`PlacementController`はStep 6で`placementMarkerPrefab`フィールドが追加され、`GhostDomino.prefab`に加えて`PlacementMarker.prefab`への参照も持つようになりました。

物理判定(Rigidbody / CapsuleCollider)とネットワーク関連コンポーネントはすべて **Root** に集約し、見た目(Capsuleメッシュ)は **Visual** という別の子オブジェクトに分離してあります。これは「将来Capsuleを3Dモデルに差し替えるときに、Rootの物理・ネットワーク設定を一切変更せずに済む」ようにするための意図的な設計です。`CameraTarget` は三人称カメラが注視する頭の高さの目印用の空オブジェクトです。

詳しい各コンポーネントの役割は `PlayerSystem.md` を参照してください。

`Domino.prefab`(`Assets/_Project/Prefabs/Domino.prefab`)の階層構造:

```
Domino (Root, 単一GameObject)
├─ Transform      (localScale 0.35 x 1.0 x 0.15、Step 6でWidthを0.30→0.35に調整)
├─ MeshFilter     (Cube)
├─ MeshRenderer
├─ BoxCollider    (Material: DominoPhysicsMaterial)
├─ Rigidbody
├─ NetworkObject         … Step 5で追加
├─ NetworkTransform      … Step 5で追加(AuthorityMode = Server)
└─ NetworkRigidbody      … Step 5で追加(UseRigidBodyForMotion, AutoUpdateKinematicState)
```

Playerとは異なり、`Domino.prefab` は見た目(Visual)と物理(Physics)を分離せず単一のGameObjectにまとめています。Step 5で`NetworkObject`等を追加しましたが、これらもPlayerと同じくRoot直下に集約しており、「将来Meshyでモデルを差し替える」際にVisual部分だけを分離したくなったとしても、物理・ネットワーク関連の設定には影響しない構成です(詳細はImplementationStatus.mdのStep 3・Step 5を参照)。

`GhostDomino.prefab`(`Assets/_Project/Prefabs/GhostDomino.prefab`)の階層構造:

```
GhostDomino (Root, 単一GameObject)
├─ Transform      (localScale 0.35 x 1.0 x 0.15、Domino.prefabと同じ見た目サイズ)
├─ MeshFilter     (Cube)
├─ MeshRenderer   (Material: GhostDominoMaterial、半透明)
└─ GhostDomino    (首振りアニメーションのみ。Collider・Rigidbodyは持たない)
```

`Domino.prefab`と見比べると、`BoxCollider`と`Rigidbody`が意図的に存在しません。これによって「PhysicsやPlayer/他のDominoと一切干渉しない、見た目だけのプレビュー」であることをコードの分岐(`if`文)ではなく**コンポーネント構成そのもの**で保証しています。

`PlacementMarker.prefab`(`Assets/_Project/Prefabs/PlacementMarker.prefab`、Step 6で追加、Step 7.8でCube形状化)の階層構造:

```
PlacementMarker (Root, 単一GameObject)
├─ Transform      (Cubeプリミティブ、localScale 0.45 x 0.05 x 0.25 = 薄い長方形。Step 7.8以前はCylinderの円盤)
├─ MeshFilter     (Cube)
├─ MeshRenderer   (Material: PlacementMarkerMaterial、半透明)
└─ PlacementMarker (サイズ・不透明度をAwake()で反映するだけ。Collider・Rigidbody・NetworkObjectは持たない)
```

Step 7.8で円盤(Cylinder)から長方形(Cube)へ変更したのは、Placement Mode中に自由なYaw回転が追加されたことで、Markerの向き(=どちらへDominoを置こうとしているか)を視覚的に示す必要が生まれたためです。円形では回転しても見た目が変わらず、向きが伝わりませんでした。

GhostDominoと同じく、Collider・Rigidbody・NetworkObjectを一切持たない完全ローカルの見た目専用オブジェクトです。

`StartArea.prefab` / `Checkpoint.prefab` / `GoalArea.prefab`(`Assets/_Project/Prefabs/GameFlow/`、Step 6で追加)は3つとも同じ階層構造です:

```
<Zone> (Root)
├─ Transform       (Cubeプリミティブ相当のサイズ。BoxColliderの範囲を決める)
├─ BoxCollider      (isTrigger = true)
├─ <Zone>Zone        (StartAreaZone / CheckpointZone / GoalAreaZone)
└─ Visual            (子GameObject。ローカルY = -0.5、床面に薄いパッドとして表示するだけ)
    ├─ MeshFilter (Cube)
    └─ MeshRenderer (Material: StartArea/Checkpoint/GoalAreaMaterial)
```

RootのCollider(判定用、見えない)と、Visual子オブジェクトの薄いパッド(見た目用)を分離しているのは、Player.prefabの Root(物理・ネットワーク) / Visual(見た目) という分離パターンを踏襲したものです。CheckpointのみRootに`Checkpoint Index`フィールドを持ちます。いずれも`NetworkObject`を持たない、Host/Client共通のScene静的オブジェクトです(判定処理自体はHost側だけが行います。詳細はGameFlow.mdを参照)。

`GameplayHUD.prefab`(`Assets/_Project/Prefabs/GameplayHUD.prefab`、Step 8Bで追加)の階層構造:

```
GameplayHUD (Root)
├─ Canvas / CanvasScaler / GraphicRaycaster   (Screen Space - Overlay)
├─ GameplayHUD (表示ロジック本体、MonoBehaviour)
├─ BuildingPanel      (TextMeshProUGUI, 単体で"BUILDING"を表示)
├─ ReadyPanel         (TextMeshProUGUI, 単体で"READY / Press F to Start"を表示)
├─ RunningTimerPanel  (TextMeshProUGUI, GameplayHUD.csが毎Frame現在Elapsed Timeを書き込む)
├─ FailedPanel        (TextMeshProUGUI, 単体で"FAILED"を表示)
└─ ResultPanel
    ├─ ResultLabelText   (TextMeshProUGUI, 静的な"CLEAR!\nTIME")
    └─ ResultTimeText    (TextMeshProUGUI, GameplayHUD.csがSUCCESS確定時のFinal Timeを書き込む)
```

`NetworkObject`を一切持たないPure LocalなGameObjectで、Host/Client双方のSceneに同じPrefabがそのまま存在し、各自が自分のLocalな`GameFlowManager.CurrentPhase`/`ChallengeTimer`の読み取り結果だけで表示を切り替えます(詳細はGameFlow.md「Challenge Timer / Gameplay HUD」章を参照)。

---

## 7. Componentどうしの関係

```mermaid
graph TD
    NB[NetworkBootstrap] -->|StartHost / StartClient を呼ぶ| NM[NetworkManager]
    NM -->|接続許可のみ、CreatePlayerObject=false (Step 9.2)| GPS[GameplayPlayerSpawner (NetworkManager上, Hostのみ)]
    GPS -->|Gameplay Scene Load完了後にInstantiateAndSpawn| PF[Player.prefab]

    subgraph "Player.prefab 内部の依存関係"
        PM[PlayerMotor] -->|RequireComponent| RB[Rigidbody]
        PM -->|RequireComponent| PS[PlayerStats]
        PM -->|移動方向の計算に参照| TPC[ThirdPersonCamera.Instance]
        PM -->|回転を適用| RB
        PM -->|OnNetworkSpawn時にSetTargetを呼ぶ| TPC
        NT[NetworkTransform] -.->|Rigidbodyの位置を監視して同期| RB
        NRB[NetworkRigidbody] -.->|Owner以外ではRigidbodyをKinematic化| RB

        PC[PlacementController] -->|RequireComponent| PM
        PC -->|Eキーで移動を一時停止| PM
        PC -->|Instantiateして生成・使い回し| GD[GhostDomino.prefab]
        PC -->|EnterPlacementView/ExitPlacementViewを呼ぶ| TPC
        PC -->|左クリック確定時にRPC送信 (Step 5)| RPC[RequestPlacementRpc]
    end

    RPC -->|Server上でのみ実行される| DS[DominoSpawner (NetworkManager上, Hostのみ)]
    DS -->|Validation通過後にInstantiateAndSpawn| DP[Domino.prefab]
    DS -->|Spawn直後にRegister (Step 7)| DCM[DominoCleanupManager (NetworkManager上, Hostのみ)]
    DP -->|Server Authority, Host上でDynamic/Client上でKinematic| DPRB[Domino Rigidbody]
    DCM -->|BUILDING中、倒れて Cleanup Delay 経過したらDespawn| DP

    TPC -->|Main Cameraに付与, シングルトン| MC[Main Camera]
```

ポイントは以下の4つです。

1. **`PlayerMotor` は `ThirdPersonCamera` に依存しているが、`ThirdPersonCamera` は `PlayerMotor` を一切知らない。** カメラは「誰かが `SetTarget()` を呼んでくれるのを待つだけ」の受け身の存在で、Player側から一方的に登録される関係になっています。`PlacementController`も同様に、カメラに対しては`EnterPlacementView()`/`ExitPlacementView()`を一方的に呼ぶだけの関係です。
2. **`NetworkTransform` と `NetworkRigidbody` はコードから直接呼び出されることはなく、Netcode for GameObjectsの内部処理が自動的に働きます。** `PlayerMotor` は普通にRigidbodyを動かしているだけで、「ネットワークに送信する」という処理を自分で書く必要はありません。Domino側も同様です。
3. **`NetworkBootstrap` はScene内の唯一の「ネットワークの起動(StartHost/StartClient)の入口」です。** 一方、`PlacementController`のRPCは「起動後の、個々のゲームプレイ操作をネットワーク越しに要求する」ための入口であり、役割が異なります。
4. **`PlacementController` は `NetworkBehaviour` だが、`GhostDomino`は`NetworkObject`を持たない完全ローカル。** `PlacementController`自体はPlayerと同じ`IsOwner`ガードでHost/Client間の操作を分離しています。Step 4まではConfirm時にClient自身がローカルで`Domino.prefab`をInstantiateしていましたが、Step 5で`RequestPlacementRpc`経由のRequestに変更し、実際のInstantiate/Spawnは常に`DominoSpawner`(Hostのみ)が行うようになりました。これによりReal DominoはHost/Server Authorityの`NetworkObject`として全員に同期されます(詳細はNetworking.mdの第17〜19章を参照)。
5. **`DominoCleanupManager`(Step 7)は`DominoSpawner`から明示的に`Register()`されたDominoしか知らない。** Scene全体を検索する処理は一切持たず、`GameFlowManager.CurrentPhase`を読み取るだけでGameFlow側には何も書き込みません。BUILDING中に倒れて`Cleanup Delay`秒経過したDominoを`NetworkObject.Despawn()`するだけの、責務が明確に分離されたHost専用コンポーネントです(詳細はDominoLifecycle.mdを参照)。
