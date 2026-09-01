# MenuFlow(Main Menu / Session Flow)

Step 9で追加した、「Gameplayそのもの」ではなく「PlayerがGameplayへ入るまでのFlow」を扱う仕組みについて記載します。Gameplay自体の仕様(Placement/BUILD TIME/READY/RUNNING/SUCCESS/FAILED等)は`GameFlow.md`を参照してください。

## 1. 全体像

```text
TITLE
  ↓
MODE SELECT
  ├───────────────────────┐
  │                       │
TIME ATTACK            ONLINE PLAY
(Single Player)           │
  │                       ↓
  │                 CREATE / JOIN         (Step 9B)
  │                       ↓
  │                     ROOM              (Step 9B)
  │                       ↓
  │             2 Players以上集合          (Step 9B)
  │                       ↓
  │                  STAGE SELECT
  │                       │
  ↓                       ↓
STAGE SELECT          HostがStage選択     (Step 9C)
  │                       │
  └──────────┬────────────┘
             ↓
         GAMEPLAY
```

Step 9AではTITLE→MODE SELECT→TIME ATTACK→STAGE SELECT→GAMEPLAYの、Single Player経路のみが完全に機能します。ONLINE PLAY側のパネル(CREATE ROOM/JOIN ROOM/ROOM)はStep 9Bで、Online専用のStage Select挙動(Host-only操作)はStep 9Cで実装します。

## 2. Scene構成

```text
Assets/_Project/Scenes/MainMenu.unity   (Step 9で新規、Build Index 0)
Assets/Scenes/SampleScene.unity          (既存Gameplay Scene、Build Index 1)
```

MainMenu Sceneは`MODE SELECT`等のUI PanelとNetworkManagerのみを持ち、`GameFlowManager`/`Domino`/`Player`等のGameplay Objectは一切含みません。Gameplay Sceneは既存のSampleSceneをそのまま使用し、Single Player/Onlineいずれの経路でも別のGameplay Sceneを新設していません(第9章参照)。

## 3. Menu Architecture(1 Scene + Panel切り替え)

MainMenu Scene内は、Sceneを毎回切り替えるのではなく、単一の`Canvas`の下に以下7つのPanelを持ち、`MainMenuController`が`MenuState` Enumに応じて表示切り替えを行う方式です。

```text
Canvas (MainMenuController)
├─ TitlePanel
├─ ModeSelectPanel
├─ OnlineSelectPanel
├─ OnlineCreatePanel      (Step 9Bで機能実装、9Aはプレースホルダ)
├─ OnlineJoinPanel        (Step 9Bで機能実装、9Aはプレースホルダ)
├─ OnlineRoomPanel        (Step 9B/9Cで機能実装、9Aはプレースホルダ)
└─ StageSelectPanel
```

`MainMenuController.ShowState(MenuState)`が全Panelを一括で非表示にしてから対象のPanelのみ表示します。Backナビゲーション用に`Stack<MenuState>`の簡易履歴を持ち、`OnBackClicked()`(各PanelのBackボタン、およびEscapeキー両方から呼ばれる)で1つ前のStateへ戻ります。

責務分離(第51章の指示どおり):
- `MainMenuController`: Panel表示切り替え・Back履歴・Escapeキー入力のみを扱う。Stage DataやNetworkingは一切知らない。
- `StageSelectController`(StageSelectPanelにアタッチ): `StageCatalog`からGrid Cardを動的生成し、クリックされたStageでSingle Player経路のSession開始(Local Host + Scene Load)を行う。
- `OnlineSessionController`等(Step 9Bで追加予定): Online CreateRoom/JoinRoom/Room/Networkingを扱う。

## 4. TITLE / MODE SELECT

`TitlePanel`は起動時に表示される最初のPanelです。`START`ボタン(`MainMenuController.OnStartClicked`)で`ModeSelectPanel`へ遷移します。

`ModeSelectPanel`は`TIME ATTACK`(`OnTimeAttackClicked`)と`ONLINE PLAY`(`OnOnlinePlayClicked`)の2ボタンを持ちます。`OnTimeAttackClicked`は`GameSessionContext.IsOnline = false`を設定した上で`StageSelectPanel`へ遷移します。

## 5. Single Player Architecture(Local Host)

既存のGameplayはNGO / NetworkBehaviour前提のため、Single Player専用にNetwork処理を取り除いた別実装は作っていません。TIME ATTACKは内部的に「Local Hostとして1人でGameplayを開始する」方式です。

```text
Single Player
↓
NetworkManager.Singleton.StartHost()   (Relayへは接続しない)
↓
Connected Player = 1
↓
NetworkManager.Singleton.SceneManager.LoadScene(sceneName, LoadSceneMode.Single)
↓
Gameplay (既存GameFlowがConnected Players = 1のままそのまま動作)
```

`StageSelectController.StartLocalHostAndLoadGameplay()`がこの2行を担っています。`StartHost()`が失敗した場合(実装確認時に検証環境のPort競合で実際に確認済み)は、`SceneManager.LoadScene()`を呼ばずにエラーログを出して中断します - 呼び出し元の状態に関わらずScene遷移だけ実行してしまうと、Networkingが起動していない状態でのSceneManager呼び出しという未定義動作になるため、これは明示的に防いでいます。

## 6. Stage Catalog

`Docs/ImplementationStatus.md`のStep 9Aエントリ、および以下のAsset群を参照してください。

```text
Assets/_Project/Scripts/Menu/StageDefinition.cs   (ScriptableObject: Stage ID / Display Name / Gameplay Scene Name)
Assets/_Project/Scripts/Menu/StageCatalog.cs      (ScriptableObject: StageDefinitionのList)
Assets/_Project/Data/Stages/Stage_01.asset 〜 Stage_08.asset
Assets/_Project/Data/StageCatalog.asset
```

Step 9時点では8Stageすべてが`gameplaySceneName = "SampleScene"`を指しており、実際のStageは1つだけです。将来的に`Stage_02`が別のScene名を持つよう変更するだけで、コード変更なしに個別Stageを追加できるArchitectureです。`StageSelectController`はUIへ8回ハードコードする代わりに、`StageCardTemplate`(StageGrid内の非表示テンプレート、Button+TextMeshProUGUI)を`catalog.Stages`の数だけ`Instantiate`して構築しています。

## 7. NetworkManager Lifecycle(Menu ⇄ Gameplay間の永続化)

Step 9より前は、`NetworkManager`(+`UnityTransport`/`NetworkBootstrap`/`DominoSpawner`/`DominoCleanupManager`)がGameplay Scene(SampleScene)に直接配置されていました。Step 9でMainMenu Sceneへ移動し、`NetworkBootstrap.Awake()`で明示的に`DontDestroyOnLoad(gameObject)`を呼ぶことで、Menu→Gameplayの Scene遷移(`NetworkManager.SceneManager.LoadScene(..., LoadSceneMode.Single)`)を跨いで同一インスタンスが生き続けるようにしています。

SampleScene側には、Step 9以降`NetworkManager`は一切配置されていません(Duplicate NetworkManager防止、第46章)。`GameFlowManager`(Scene-placed NetworkObject、Start/Checkpoint/Goal Zoneへの参照を持つ)はGameplay固有のためSampleScene側に残しています。

`LoadSceneMode.Single`によるScene遷移は、`DontDestroyOnLoad`されたGameObject(NetworkManager等)を除く現在ロード中の全Sceneをアンロードしてから対象Sceneをロードします。そのためMainMenu Sceneの`Canvas`等(DontDestroyOnLoad対象外)は正しく破棄され、NetworkManager側だけが生き残ります。

## 8. NetworkBootstrap(Development Auto-start Modeの分離)

`NetworkBootstrap.cs`は、これまで通りMPPM Tag(`Host`/`Client`)およびCLI引数(`-host`/`-client`)による開発用Auto-startを担当します(削除していません)。Step 9での変更点は、Auto-start経路がHostとして起動した場合、直後に`NetworkManager.Singleton.SceneManager.LoadScene(developmentGameplaySceneName, LoadSceneMode.Single)`を呼ぶようになったことだけです。

以前はMPPM/CLIによる開発テストがそのままGameplay Scene起動を意味していましたが、Step 9でMenuが手前に入ったため、この1行が無いと開発テスト時に毎回手動でMenuを操作する必要が生じてしまいます。Client側のMPPM Virtual Playerには対応する処理は不要です(`NetworkConfig.EnableSceneManagement = true`により、ServerがLoadSceneした瞬間にNGOが自動的に同期させるため)。

通常の(MPPM Tag/CLI引数のいずれも無い)起動時は、この経路は何もせず早期returnするため、MainMenuのTITLE画面が通常通り表示されます - Interactive Menu ModeとDevelopment Auto-start Modeは、同じコンポーネント内で条件分岐により明確に分離されています。

## 9. 未実装(Step 9B / 9Cで対応)

- Online Play(Create Room / Join Room / Join Code / Online Room / Player Count)
- Host-only Online Stage Select、Selected Stageの同期
- Network Scene Transition(Online向け、Session Lock、Late Join禁止)

詳細は`Docs/OnlineSession.md`(Step 9Bで作成)を参照してください。
