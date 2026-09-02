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

## 8. Online Stage Select(Step 9C)

`StageSelectPanel`は9A(Single Player)・9B/9C(Online)の両方から到達しますが、Scene/UI階層は完全に共有しています(Onlineだけの別Panelは作っていません)。`GameSessionContext.IsOnline`で挙動を分岐します。

### Host-only操作制限

`StageSelectController.RefreshOnlineUI()`が、生成済みの各Stage Cardの`Button.interactable`を`OnlineSessionController.Instance.IsHost`で一括制御します。

```csharp
card.interactable = isHost;
```

Clientは同じGridを見ますが、どのCardも押せません(`interactable = false`のため`ExecuteEvents`経由の疑似ClickですらHandlerに到達しないことを実機確認済み)。

### Selected StageのHost→全員同期・表示

`NetworkSessionState`に`NetworkVariable<int> SelectedStageId`を追加しました(Session Phaseと同じNetworkObject)。

```csharp
public void HostSelectStage(int stageId)
{
    if (!IsServer) return;
    if (_phase.Value != SessionPhase.StageSelect) return;
    _selectedStageId.Value = stageId;
}
```

Cardクリック(Host-only)で呼ばれ、`SelectedStageChanged`イベント経由で全Clientの`RefreshOnlineUI()`が再実行されます。選択中のCardは黄色にハイライトし(`Image.color`切り替え)、Host側は`"SELECTED: {Stage名}"`、Client側は`"Host selected: {Stage名}"`(未選択なら`"Waiting for the host to choose a stage..."`)を表示します。実機確認: Stage 5をHost視点で選択 → Cardが黄色にハイライトし、`SELECTED: STAGE 5`が表示されることを確認済み。

### START GAME(Server側Validation)

`StageSelectPanel`にHost専用の`START GAME`ボタンを追加しました(`GameSessionContext.IsOnline`の間だけ表示)。

```csharp
public void OnStartGameButtonClicked()
{
    if (_session == null || !_session.IsHost) return;
    ...
    bool started = NetworkSessionState.Instance.HostTryStartGame(_session.PlayerCount);
    if (!started) { ...reject... }

    if (_session is IHostSession hostSession) hostSession.IsLocked = true;

    GameSessionContext.SelectedStageId = stage.StageId;
    NetworkManager.Singleton.SceneManager.LoadScene(stage.GameplaySceneName, LoadSceneMode.Single);
}
```

`NetworkSessionState.HostTryStartGame(int connectedPlayerCount)`がServer側で独立に再検証します(呼び出し元がUIで既にGateしていても信用しません、第37章の要件どおり):

```csharp
public bool HostTryStartGame(int connectedPlayerCount)
{
    if (!IsServer) return false;
    if (_phase.Value != SessionPhase.StageSelect) return false;
    if (connectedPlayerCount < 2 || connectedPlayerCount > 4) return false;
    if (_selectedStageId.Value < 0) return false;

    _phase.Value = SessionPhase.StartingGame;
    return true;
}
```

**実機確認(重要な発見)**: 検証環境では実際に接続しているPlayerが1人(Host自身)のみだったため、`HostTryStartGame(_session.PlayerCount)`(実際の人数=1)を渡した通常経路は**意図通り拒否されました**(Console: `"server-side validation rejected the request"`)。これはバグではなく、Player数不足を正しく検知した証拠です。Server側Validationロジック自体は`HostTryStartGame(2)`のように条件を満たす人数を直接渡すことで独立して検証し、`Phase`が正しく`StartingGame`に遷移すること、続く`IHostSession.IsLocked = true`の設定、`NetworkManager.SceneManager.LoadScene()`によるGameplay Sceneへの遷移(Player Spawn・BUILD TIME開始まで)がすべて正しく動作することを、それぞれ個別に実機確認しました。

### Start Race Condition対策(第57章)

`HostTryStartGame()`内で`_phase.Value = SessionPhase.StartingGame`への変更が**先に**確定してから、呼び出し元が`IHostSession.IsLocked = true`とScene Loadを行う順序になっています。Phase変更はNetworkVariableとして即座に(Server側で)確定するため、「Startを決定した」時点と「実際にLockされた」時点の間に別Clientが割り込む余地を最小化しています。

---

## 9. Late Join禁止(Connection Approval)

`NetworkManager.NetworkConfig.ConnectionApproval = true` + `NetworkManager.ConnectionApprovalCallback`(Step 9, section 40)を、Services初期化と同じタイミング(`EnsureServicesInitializedInternal()`、Create/Join呼び出し前に必ず実行される経路)で設定しています。

```csharp
private static void HandleConnectionApproval(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
{
    SessionPhase phase = NetworkSessionState.Instance != null ? NetworkSessionState.Instance.Phase : SessionPhase.Room;
    bool gameStarted = phase == SessionPhase.StartingGame || phase == SessionPhase.InGame;

    response.Approved = !gameStarted;
    response.CreatePlayerObject = false; // Step 9.2で変更(以前はtrue)。詳細下記
    if (gameStarted) response.Reason = "This game has already started.";
}
```

Room・Stage Select中のJoinは許可し、Host GAME START後(`StartingGame`/`InGame`)のみServer側で拒否します(第39章の仕様どおり)。**ClientのUIでJoin Buttonを隠すだけでなく、Server側でも独立して拒否する**という要件を満たしています。`NetworkConfig.ConnectionApproval`が`true`かつCallbackが正しく登録されていることは実機確認済みです。`NetworkManager.ConnectionApprovalRequest`/`ConnectionApprovalResponse`はNGOが内部的に生成する型のため、`HandleConnectionApproval()`自体を疑似的な引数で直接呼び出すテストは実施できませんでした(手動`new()`ではNGO内部の初期化が伴わずNullReferenceExceptionになるため) - ロジック自体はReflectionで確認した型定義どおりのシンプルなField設定のみです。

**`CreatePlayerObject = false`について(Step 9.2)**: 当初`true`だったこの値により、接続した瞬間(まだRoom/Stage Select中、`MainMenu.unity`がActive Scene)にGameplay Player(`Player.prefab`)がSpawnされ、Groundの無い空間へ落下し続けるバグが発生していました。現在は常に`false`にし、Gameplay SceneのLoadが完了したタイミングで別のコンポーネント(`GameplayPlayerSpawner`)が明示的にSpawnする方式に変更しています。Root Cause・Fixの詳細・実機確認結果は`Networking.md`「24. Player Spawn Lifecycle」を参照してください。Method自体も(Single Playerの`StageSelectController`から呼べるよう)`private instance`から`public static`/`private static`へ変更しました。

失敗時のUI表示(第41章)は、Service側のAllocation状態から「満員」等と正確に判別できない場合が多いため、既存のJoin失敗表示(`"Could not join the room.\nPlease check the Join Code."`)を流用しています(仕様上許容されている対応です)。

---

## 10. 未実装/確認できなかった事項(Step 9全体を通して)

- **実際の2人以上でのEnd-to-Endテスト**: これまでのStepと同じMPPM Virtual Player自動操作の制約に加え、同一Editorプロセスから2つの独立したRelay接続を同時に張ること自体が困難なため、Host自身の1接続のみで検証しました。Server側Validation(`HostAdvanceToStageSelect`/`HostTryStartGame`)は実際のPlayer数引数を意図的に差し替えることでロジック自体は検証済みですが、「本物の2人目のClientが実際にJoinしてConnection Approvalを通過し、Stage Selectで実際にHost選択を見る」という結合的な確認は開発者ご自身での実機確認を推奨します
- Late Join拒否(Connection Approval)のEnd-to-End確認(実際にGame開始後の接続試行)も同様の制約で未実施です

---

## 11. Character Select(Step 12.1)

Room → Character Select → Stage Selectという新しいOnline Flowと、各Playerが自分自身のCharacterを個別に選択できる仕組みを追加しました。Character Catalog自体・Player側のVisual切り替えは`Docs/CharacterSystem.md`を、Menu Panel構成は`Docs/MenuFlow.md`「11. Character Select」を参照してください。

### Session Phaseの拡張

```csharp
public enum SessionPhase
{
    Room,
    CharacterSelect,   // Step 12.1で追加
    StageSelect,
    StartingGame,
    InGame
}
```

Room画面の`STAGE SELECT`ボタンは、Step 12.1から`OnCharacterSelectButtonClicked()`(`HostAdvanceToCharacterSelect`)を呼ぶよう配線し直し、ラベルも`CHARACTER SELECT`に変更しました。Character Select画面自身が持つ新しい`STAGE SELECT`ボタンが、既存の`OnStageSelectButtonClicked()`(`HostAdvanceToStageSelect`)を引き継いで呼びます - **Method名・実装本体はStep 9Cから無変更**で、`HostAdvanceToStageSelect`の遷移元Phaseを`Room`から`CharacterSelect`へ変更しただけです。

```text
Room --(Host, 2人以上)--> CharacterSelect --(Host)--> StageSelect --(Host)--> StartingGame → InGame
```

Player数が2人未満に戻った場合の`HostReturnToRoom()`挙動(第9章の既存Gate)は、`CharacterSelect`/`StageSelect`のどちらのPhaseからでも同じように発動するよう`OnlineSessionController.RefreshRoomUI()`を拡張しています。

### 各Playerが自分自身のCharacterを選択(Section 16)

Stage Selectとの決定的な違いです。Stage SelectはHost-onlyの単一選択(`NetworkVariable<int> SelectedStageId`)ですが、Character Selectは**Player人数分の選択**を同時に扱う必要があるため、`NetworkSessionState`にServer-onlyの`Dictionary<ulong, int>`(ClientId → CharacterId)を追加しました。NetworkVariable/NetworkListではなく素朴なDictionaryを選んだ理由は、各Clientが必要とするのは「自分自身の選択」だけであり(表示用のOptimistic Local State、`GameSessionContext.SelectedCharacterId`側で保持)、Server側はGameplay Player Spawn時に全員分を1回だけ参照できれば十分なためです。

```csharp
[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
public void RequestSelectCharacterRpc(int characterId, RpcParams rpcParams = default)
{
    if (_phase.Value != SessionPhase.CharacterSelect) return;
    if (characterCatalog == null || characterCatalog.FindById(characterId) == null) return;

    ulong senderClientId = rpcParams.Receive.SenderClientId;
    _selectedCharacterByClient[senderClientId] = characterId;
}
```

`GameplayPauseManager.RequestPauseRpc`等と同じ`[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]`パターンで、**Host-onlyではなく任意の接続中Client**が呼べます。HostがClientのCharacterを選ぶ必要はありません(Section 16の要件どおり)。

### Server Validation(Section 17)

Clientから送るのは`int characterId`だけで、Prefab参照やCharacter名等は一切送受信しません。Server側は`characterCatalog.FindById(characterId)`でCatalog内に実在するIDかどうかだけを確認し(存在しなければ黙って無視)、`Phase != CharacterSelect`の間の呼び出しも無視します。Senderの識別は`rpcParams.Receive.SenderClientId`(NGOが検証済みの値)を使うため、「接続していないClient」からの呼び出しはそもそもRPCとして到達しません。

### Default Selection(Section 21)

```csharp
public int GetSelectedCharacterId(ulong clientId)
{
    return _selectedCharacterByClient.TryGetValue(clientId, out int characterId) ? characterId : DefaultCharacterId; // = 1
}
```

一度もCardを触っていないClientは、Dictionaryに存在しないため自動的にCharacter 1(Classic Capsule)として扱われます。専用のReady Systemは追加しておらず、Hostは任意のタイミングでStage Selectへ進めます(その時点の各PlayerのSelectionがそのまま使われます)。

### Gameplay Player Spawnへの反映

`GameplayPlayerSpawner`(`Docs/Networking.md`参照)が、Server側で各ClientをSpawnした直後に`NetworkSessionState.Instance.GetSelectedCharacterId(clientId)`を読み、`PlayerCharacterVisual.ServerSetCharacterId()`へ渡します。Character IDの割り当てはServer/Host側のみが行い、Clientはその結果(NetworkVariable経由)を受け取るだけです(Section 15、Client Local限定のVisual切り替えは行っていません)。

### Client切断時のCleanup

```csharp
private void HandleClientDisconnected(ulong clientId)
{
    _selectedCharacterByClient.Remove(clientId);
}
```

`NetworkManager.OnClientDisconnectCallback`を購読し、切断したClientのエントリを取り除きます(Server-only、`OnNetworkSpawn`/`OnNetworkDespawn`で購読/解除)。長時間のSessionでDictionaryが際限なく肥大化することを防ぐための、最小限の後始末です。

### 未実装/確認できなかった事項

- **実際の2人以上でのCharacter Select確認**: 第10章と同じ制約(同一Editorプロセスから2つの独立したRelay接続を同時に張ることが困難)に加え、本Step実装中の長時間Play Modeサイクルにより、`NetworkManager.Shutdown()`が完了しない(`ShutdownInProgress`のまま固まり、UDP Port 7799の再Bindに失敗する)という環境側の不安定化が実際に発生し、Single Player側の一部End-to-Endテストも影響を受けました。Server Validation自体のロジック(RPC受信・Catalog照合・Phase Gate)はコードレビューで確認済みですが、実際の2 Client間でのCross-Client Visual一致・Cross-Client Animation可視性の確認は未実施です。開発者ご自身での実機確認(Host + Client、MPPM可)を推奨します
