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

Step 9A〜9Cを通じて、上図の全経路(Single Player: TITLE→MODE SELECT→TIME ATTACK→STAGE SELECT→GAMEPLAY / Online: TITLE→MODE SELECT→ONLINE PLAY→CREATE or JOIN→ROOM→(2人以上)→STAGE SELECT(Host-only)→GAMEPLAY)が実装済みです。Online側の詳細(Session API・Room・Host Authority・Late Join禁止等)は`Docs/OnlineSession.md`を参照してください。

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

## 8. NetworkBootstrap(Normal Startup と Development Auto-start Modeの分離、Step 9.1で修正)

### Normal Startup(通常起動)

**通常のApplication起動・通常のPlay Modeでは、必ずMainMenuのTITLE画面から始まります。** `NetworkBootstrap`はNetworkを一切自動開始せず、`NetworkManager.IsListening`は`false`のまま、Gameplay Sceneへの遷移も一切発生しません。Networkの開始は、常にUser Actionによって(`MainMenuController`/`StageSelectController`/`OnlineSessionController`経由で)行われます。

```text
Application Start
↓
MainMenu Scene Load
↓
TITLE(NetworkManager: not listening)
↓
User Action(TIME ATTACK選択 / CREATE ROOM / JOIN ROOM)によってのみNetwork開始
```

### バグ修正の記録: MainMenuの意図しない自動Skip(Step 9.1)

Step 9AでStage Selectを実装した際、`NetworkBootstrap.Start()`が**無条件に**`TryStartFromMultiplayerPlayModeTags()`を呼んでいました。このMethodは`CurrentPlayer.Tags`(MPPM Player識別用のTag)に`"Host"`が含まれているかどうかだけを見て`NetworkManager.StartHost()`+Gameplay Scene Auto Loadを実行する作りでした。本Projectでは開発テストの都合上、Main EditorにMPPM `"Host"` Tagが恒久的に設定されていたため、**普通にPlay Modeへ入っただけで**(Userが何も操作していなくても)、このTagが「Auto-startして良い」という意図的な合図であるかのように扱われ、即座にHost化+Gameplay Scene遷移が発生し、MainMenuのTITLE画面が完全にSkipされていました。

これはMPPM Tag自体が本来「Development上のPlayer識別情報」でしかなく、「今すぐAuto-startしてよい」という明示的な合図ではないにも関わらず、両者を区別せず同一視していたことが原因です。

### Development Auto-start Mode(Explicit Opt-in、Step 9.1で修正)

`NetworkBootstrap`に`developmentAutoStartViaMppmTag`(`bool`、既定値`false`)を追加しました。この値が`true`の場合のみ、MPPM Tagに応じたHost/Client Auto-start(+Hostの場合はGameplay Scene Auto Load)を行います。既定では`false`のため、**MPPM Host Tagが付与されていても、それだけではAuto-startしません**。

```csharp
private void Start()
{
    if (developmentAutoStartViaMppmTag && TryStartFromMultiplayerPlayModeTags()) return;
    StartFromCommandLineArgs();
}
```

過去のMPPMベースの高速な開発テストを完全に使えなくしたわけではなく、開発者がInspector上で`developmentAutoStartViaMppmTag`を明示的にONにした場合のみ、以前と同じAuto-start経路(Hostとして起動→即座に`developmentGameplaySceneName`へScene遷移)が働きます。「必要な時だけ開発者自身が明示的に有効化する」設計です。

CLI引数(`-host`/`-client`)による経路(`StartFromCommandLineArgs()`)はStep 9.1で変更していません。この経路は元々`if (Application.isEditor) return;`により、Standalone Buildを実際にコマンドラインから`-host`等の引数付きで起動した場合にのみ働く、既に十分Explicitな仕組みでした(通常のダブルクリック起動では引数が無いため発火しません)。

### Interactive Menu Mode(通常のUser操作)

`NetworkBootstrap`とは完全に独立して、`MainMenuController`/`StageSelectController`/`OnlineSessionController`がUser Actionに応じてNetworkを開始します(TIME ATTACK選択時の`StartHost()`、CREATE ROOM時の`CreateSessionAsync()`、JOIN ROOM時の`JoinSessionByCodeAsync()`)。`NetworkBootstrap`側のDevelopment Auto-start Modeが無効(既定)の場合、この経路が実質的に唯一のNetwork開始手段になります。

## 9. Online Play(Step 9B / 9C)

Online Play(Create Room / Join Room / Join Code / Online Room / Player Count / Host-only Online Stage Select / Selected Stage同期 / Network Scene Transition / Late Join禁止)はStep 9B・9Cで実装済みです。詳細は`Docs/OnlineSession.md`を参照してください。

Step 9の対象外(Friend機能・Random Match・Public Room List・Matchmaking・Host Migration・Reconnect・Spectator Join・Ranking等)は今後の別Stepの対象です。
