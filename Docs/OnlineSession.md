# OnlineSession(Online Play / Session Networking)

Step 9Bで実装した、Online Play(Create Room / Join Room / Room / Host-authority Phase遷移)について記載します。Menu全体のPanel構成は`MenuFlow.md`を、Gameplay自体は`GameFlow.md`を参照してください。

## 1. Online Service構成(採用したArchitecture)

### 検討

現在Installされていた関連Packageを確認したところ、`com.unity.services.*`系は一切未導入でした。「最小の公式構成」を選ぶにあたり、まず`com.unity.services.multiplayer`(Multiplayer Services SDK)をインストールし、解決された依存関係(`Packages/packages-lock.json`)を確認しました。

```text
com.unity.services.multiplayer 2.3.1
├─ com.unity.services.core
├─ com.unity.services.authentication
├─ com.unity.services.qos
├─ com.unity.services.wire
└─ com.unity.services.deployment
```

**`com.unity.services.relay`・`com.unity.services.lobby`という独立したPackageは一切導入されていません。** これは、現在の`com.unity.services.multiplayer`(統一されたSession API)が、以前は別々に扱われていたLobby/Relay/Matchmakerの機能を内部に統合していることを示しています。

### 採用したArchitecture

**Session APIのみ**(`Unity.Services.Multiplayer.MultiplayerService.Instance`)を使用し、Relay/Lobbyを個別に手動配線する古いTutorial的な方式は採用していません。

```csharp
var options = new SessionOptions { MaxPlayers = 4 }.WithRelayNetwork();
IHostSession session = await MultiplayerService.Instance.CreateSessionAsync(options);
string joinCode = session.Code;
```

```csharp
ISession session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, null);
```

`WithRelayNetwork()`拡張メソッド(`SessionOptionsExtensions`)がRelay経由のNetwork構成を選択します。Public Room List / Matchmakerは一切使用していません(`QuerySessionsAsync`/`MatchmakeSessionAsync`等のAPIは存在を確認しましたが、今回の「Join Codeを知っている人同士だけ」という要件には不要なため使用していません)。

### NetworkManagerとの統合(重要な発見)

`CreateSessionAsync`/`JoinSessionByCodeAsync`は、Projectに`com.unity.netcode.gameobjects`が導入されていることを検出し、**`NetworkManager.StartHost()`/`StartClient()`を自動的に呼び出します**。実機確認により、`CreateSessionAsync`完了直後に`NetworkManager.Singleton.IsHost == true`かつ`IsListening == true`になることを確認しました。開発者が明示的に`StartHost()`/`StartClient()`を呼ぶ必要はありません。

### API調査方法

Web Docs(docs.unity.com)は一部ページが404/内容欠落で不安定だったため、`unity_reflect`(Unity Editorへの実際のReflection)による現物確認を優先しました。`IMultiplayerService`/`SessionOptions`/`SessionOptionsExtensions`/`ISession`/`IHostSession`/`IAuthenticationService`/`UnityServices`の各メンバーをReflectionで直接確認し、本章のコード例はすべて実機のAPI Surfaceと一致していることを検証済みです。

---

## 2. Unity Services初期化・匿名認証

`OnlineSessionController.EnsureServicesInitializedAsync()`が、Online機能に初めて触れた瞬間(CREATE ROOM/JOIN ROOMボタンを押した時点)に遅延初期化します。

```csharp
if (UnityServices.State != ServicesInitializationState.Initialized)
    await UnityServices.InitializeAsync();

if (!AuthenticationService.Instance.IsSignedIn)
    await AuthenticationService.Instance.SignInAnonymouslyAsync();
```

匿名ログイン(`SignInAnonymouslyAsync`)のみを使用しており、User Account画面は作成していません。このProjectは既に`cloudProjectId`が設定済み(Unity Cloud Projectにリンク済み)であり、実機確認でRelay/Session機能が正しく利用できることを確認しています。

---

## 3. Create Room

`OnlineSessionController.OnCreateRoomButtonClicked()`:

```text
OnlineSelectPanel: CREATE ROOM押下
↓
OnlineCreatePanel表示("Creating room...")
↓
EnsureServicesInitializedAsync()
↓
CreateSessionAsync(MaxPlayers=4, WithRelayNetwork())
↓
OnlineRoomPanel表示 + Join Code等を反映
```

失敗した場合は`OnlineSelectPanel`へ戻り、詳細はConsoleへログ出力します(UIには詳細を出しません、第6章参照)。

---

## 4. Join Room

`OnlineSessionController.OnJoinButtonClicked()`。`TMP_InputField`に入力されたJoin Codeで`JoinSessionByCodeAsync`を呼びます。成功すれば`OnlineRoomPanel`へ、失敗すれば同じ`OnlineJoinPanel`上に短いエラーメッセージを表示します。

実機確認(意図的に無効なCode "ZZZZZZ" を入力): UI表示は`"Could not join the room.\nPlease check the Join Code."`のみ、Console側には`SessionException`の詳細("lobby code 'ZZZZZZ' contains an invalid character 'Z'..."などService側の実際の理由)が出力されることを確認しました。

---

## 5. Online Room

### Player Count

`ISession.PlayerCount` / `ISession.MaxPlayers`を`ISession.Changed`イベント(Player参加・退出等での発火)のたびに読み直し、`"1 / 4"`のように表示します。実機確認したJoin Codeの例: `6LM9H8`(実際にUnity Relayから発行された値をそのまま表示。6文字等のハードコードはしていません)。

### STAGE SELECT Button(Host-only, 2人以上)

```csharp
bool canStart = _session.IsHost && _session.PlayerCount >= 2;
stageSelectButton.interactable = canStart;
```

`interactable = false`はUnityの標準的な`Selectable`挙動により、Clickイベント自体を発火させません(実機で`ExecuteEvents.Execute`による疑似Clickでも反応しないことを確認済み)。Client側の表示は"Waiting for the host..."、Host側でPlayer不足の間は"Waiting for players..."です。

### Leave

`LeaveAsync()`→`NetworkManager.Shutdown()`→`OnlineSelectPanel`へ戻ります。**実機確認で`ISession.LeaveAsync()`が(Local PlayerがSessionの唯一のMemberだったケースで)応答なく停止する事象を1度観測しました**。原因はConsoleに後から出力された`"Received error message from Relay: player timed out due to inactivity"`から、Relay接続自体が検証環境で劣化していたことが示唆されますが、断定はできていません。原因の断定有無に関わらず、**外部Serviceへの呼び出しがUIを無期限にブロックしてはならない**という判断のもと、`Task.WhenAny(_session.LeaveAsync(), Task.Delay(5秒))`によるTimeout Guardを追加し、Timeoutした場合もLocal側のNetworkManager Shutdown・Panel遷移は必ず実行されるようにしました。再検証では正常に数秒以内で完了することを確認しています。

### Host Disconnect

`ISession.StateChanged`イベントで`SessionState.Deleted`/`SessionState.Disconnected`を検知した場合、Leaveと同じClean-up処理を行い、"Host disconnected."を表示して`OnlineSelectPanel`へ戻します(Host Migrationは実装していません、要件どおり)。

---

## 6. Error Handling方針

すべてのCreate/Join/Leave操作は`try/catch`で保護しています。UI側には常に短い一般的なMessageのみを表示し(例: `"Could not join the room.\nPlease check the Join Code."`)、実際の例外(`SessionException`等)は`Debug.LogError`でConsoleにのみ出力します。Invalid Join Code・Room Full・Host disconnected・Network failureのいずれについても、個別に判別可能なAPIをServiceが安定して提供していなかったため、共通の一般的なMessageで対応しています(詳細な原因の切り分けはConsoleログから可能です)。

---

## 7. Session Phase(Host Authority)

`NetworkSessionState`(MainMenu.unityにScene配置された`NetworkObject`、`GameFlowManager`と同じ「Scene-placed NetworkObject」パターン)が、`NetworkVariable<SessionPhase>`でRoom/StageSelect/StartingGame/InGameのPhaseをHost Authorityで同期します。

```csharp
public void HostAdvanceToStageSelect(int connectedPlayerCount)
{
    if (!IsServer) return;
    if (_phase.Value != SessionPhase.Room) return;
    if (connectedPlayerCount < 2) return;
    _phase.Value = SessionPhase.StageSelect;
}
```

Room画面でHostが`STAGE SELECT`を押すと、`OnlineSessionController.OnStageSelectButtonClicked()`がこのHost-only Methodを呼び出します。`_phase`の変化は`NetworkVariable.OnValueChanged`→`NetworkSessionState.PhaseChanged`イベント→**接続中の全Client**の`OnlineSessionController.HandlePhaseChanged()`→`MainMenuController.ShowState(MenuState.StageSelect)`という経路で、Host/Client問わず全員のLocal Menu UIを同時にStage Select Panelへ切り替えます。実機確認(Host自身の1接続で`HostAdvanceToStageSelect(2)`を直接呼び出し、Player数条件を満たした状態を再現)により、Phase変化から実際のPanel切り替えまで正しく動作することを確認済みです。

MainMenu Sceneの`NetworkSessionState`は、Host起動時にScene-placed NetworkObjectとして自動Spawnします(`GameFlowManager`と同じ仕組み)。Client側はNGOのScene同期完了後に`Instance`が利用可能になるため、`OnlineSessionController`はJoin直後に最大5秒のポーリングで`NetworkSessionState.Instance`の出現を待ってから購読します。

---

## 8. 未実装(Step 9Cで対応)

- Stage Select画面でのHost-only操作制限(Client側Stage Buttonの`interactable = false`)
- Selected StageのClientへの同期・表示
- START GAME(Server側Validation: Host確認・Player数2〜4・有効なStage ID)
- Late Join禁止(Connection Approval、Session Lock)
- Network Scene Transition(`NetworkManager.SceneManager.LoadScene`によるOnline Gameplay開始)
- Start Race Condition対策(Session State を先にLockしてからScene Loadする順序)

詳細は次のStep 9Cで`OnlineSession.md`に追記します。
