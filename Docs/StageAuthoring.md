# StageAuthoring(新しいStageの作り方)

このドキュメントは、**プログラムを書かずに**新しいStage(Start/Checkpoint/Goalの配置)をUnity Editor上で作成・調整するための実用的な手順書です。このドキュメントを見ながら操作すれば、Claudeへコード修正を依頼しなくても基本的なStageを組み立てられることを目標にしています。

新しいStageを既存のGameplay Scene(SampleScene)の中に追加する場合は1章以降を、**Stageごとに別のGameplay Sceneを用意したい場合**は0章を参照してください。

---

## 0. 新しいStage Scene(独立したGameplay Scene)を追加する手順(Step 13)

Step 13で、Stage Selectの各Stageが「どのUnity Sceneを読み込むか」をStageごとに個別に指定できるようになりました(現在はStage 2だけが`StageScene_2`という専用Sceneを持ち、それ以外は全て`SampleScene`を共有しています)。**新しいStage Sceneの追加に、コード変更は一切不要です。**

### 手順

1. **既存のGameplay SceneをDuplicate**: Project WindowでDuplicate元にしたいScene(例: `Assets/Scenes/SampleScene.unity`、または既に作成済みの`Assets/_Project/Scenes/StageScene_2.unity`)を選択し、右クリック→`Duplicate`(または`Ctrl+D`)。**OS(エクスプローラー)上でのファイルCopy&Pasteは絶対に行わないでください** — `.meta`ファイルごとコピーされてしまい、元Sceneと同じGUIDを持つ壊れたAssetになります。必ずUnity Editor内のDuplicate機能を使ってください(新しいGUIDが自動的に発行されます)。
2. **Rename**: 複製されたSceneを`StageScene_3`のように分かりやすい名前にRenameします(Project Window上でF2、またはゆっくり2回クリック)。
3. **配置場所**: `Assets/_Project/Scenes/`直下に置いてください(既存の`StageScene_2.unity`もここにあります)。※歴史的経緯で`SampleScene.unity`だけは`Assets/Scenes/`という別フォルダにありますが、新しく作るSceneは`Assets/_Project/Scenes/`に統一してください。
4. **Build SettingsのScene Listへ登録**: `File > Build Profiles`(Unity 6)またはメニューの`File > Build Settings`を開き、`Scene List`に手順1〜3で作ったSceneをDrag & Dropで追加します。**これを忘れるとNetworkManager経由でSceneをLoadできず、Stage選択時に何も起こりません。**
5. **StageDefinitionにSceneを紐付ける**: `Assets/_Project/Data/Stages/Stage_0X.asset`(新しいStage用に既存のものを使うか、`Domino/Stage Definition`から新規作成)を選択し、Inspectorの`Gameplay Scene Name`フィールドへ、手順2で付けたScene名を**拡張子`.unity`なしで、大文字小文字を正確に**入力します(例: `StageScene_3`)。
6. **Play Modeで確認**: Stage Selectで対象Stageを選び、意図したSceneが読み込まれること(Console上のErrorが0件であること)、Player Spawn/Camera/HUD/Placementが正常に動作することを確認します。

これだけで、Stage Select画面・Character Select・Online同期・Player Spawnのいずれのコードにも変更は不要です(`GameplayPlayerSpawner`は「StageCatalogに登録されているいずれかのGameplay Scene名と一致するか」で判定しており、特定のScene名をHard-codeしていないため — 詳細は`Networking.md`参照)。

### Scene内部のContentを作り込む

手順1でDuplicateしたばかりのSceneの中身(Ground/Checkpoint/Goal/Domino配置等)を編集する方法は、このドキュメントの1章以降(既存のStage Authoring手順)がそのまま使えます。Duplicate元のSceneと全く同じ内容からスタートするので、まずは1章を参考に、Checkpointの位置やDomino配置を新しいStageらしく調整してください。

### よくある失敗

| 症状 | 原因 | 対処 |
|---|---|---|
| Stage選択後、Sceneが切り替わらない(Consoleも無反応) | Build SettingsのScene Listに新Sceneが未登録 | 手順4を実施する |
| Stage選択後、SampleSceneのままになってしまう | `StageDefinition.Gameplay Scene Name`が空、またはScene名のTypo(大文字小文字含む) | 手順5を確認し、実際のScene名と完全に一致させる |
| Player Spawnしない/Consoleに何も出ない | 上記いずれかの設定ミスにより`GameplayPlayerSpawner`がそのSceneをGameplay Sceneとして認識できていない(StageCatalogのどのStageともScene名が一致していない) | Build Settings登録とStageDefinitionのScene名を再確認する |
| 新Sceneが元のSceneと同じGUIDになってしまった | OS上でファイルを直接Copy&Pasteした | 手順1をやり直す(Unity Editor内のDuplicate機能を使う) |

---

## 0.5. Stage Select Cardの見た目(Thumbnail画像)を変更する手順(Step 13.3)

Stage Select画面の各Card上部に表示されるThumbnail画像は、`StageDefinition.Thumbnail`(UI表示専用、`GameplaySceneName`とは完全に独立したField)から解決されます。**Code変更は一切不要**です。

1. `Assets/_Project/Data/Stages/Stage_XX.asset`をProject WindowからClick
2. Inspectorの`Thumbnail`スロットへ、用意した画像(`Texture Type = Sprite`でImport済みのもの、横長推奨)をDrag & Drop
3. Play Modeで確認 - **そのStageのCardだけ**画像が切り替わり、他のCardは共有Placeholder(`Assets/_Project/Art/UI/StagePortraits/StagePortrait_Placeholder.png`)のまま

`Thumbnail`が未設定(None)のStageは自動的にPlaceholderへFallbackするため、一部のStageだけ画像を用意して残りは後回しにしても、UIが壊れることはありません。`StageScene_2`のようなGameplay Scene自体の内容を変更しても、Thumbnailが自動生成されることはありません(Thumbnailは完全に別の、手動で用意する2D Sprite Assetです)。

---

## 1. 使用するPrefab

Stageを組み立てるための部品(Prefab)は3つ、いずれも `Assets/_Project/Prefabs/GameFlow/` にあります。

| Prefab | 役割 | 個数 |
|---|---|---|
| `StartArea.prefab` | Playerが集合し、Challengeを開始する場所 | Scene内に1つ |
| `Checkpoint.prefab` | 通過チェックポイント | 好きな数だけ配置可能 |
| `GoalArea.prefab` | ゴール地点 | Scene内に1つ |

これらはすべて、床の上に置くだけで機能する「半透明の色つきパネル」として表示されます(Start=青、Checkpoint=黄、Goal=緑)。見た目はPrototype用の簡易表示です。

---

## 2. 新しいStageを作る基本手順

1. Hierarchy上で `GameFlowZones` という空のGameObjectを作成します(既存Sceneに合わせて名前を揃えると分かりやすいです)。
2. `Assets/_Project/Prefabs/GameFlow/StartArea.prefab` をSceneへドラッグ&ドロップし、`GameFlowZones` の子にします。
3. 同様に `Checkpoint.prefab` を必要な数だけドラッグ&ドロップし、`GameFlowZones` の子にします。
4. `Assets/_Project/Prefabs/GameFlow/GoalArea.prefab` をドラッグ&ドロップします。
5. 各ZoneのTransform(位置)を、実際にDominoを並べたいコースに沿って配置します。
6. 各Checkpointの `Checkpoint Index` を、通過してほしい順番(0, 1, 2, …)に設定します(詳しくは4章)。
7. Hierarchy上で `GameFlowManager` を選択し、Inspectorの `Start Area` / `Checkpoints` / `Goal Area` に、今置いたZoneをドラッグ&ドロップで登録します(詳しくは6章)。
8. Playして、E→左クリックでDominoを並べ、Start Areaに入って `F` キーでChallenge開始、実際に倒して確認します。

---

## 3. Start Areaの置き方

`StartArea.prefab` をSceneへ配置し、Transformの `Position` を移動するだけで場所を変えられます。

- **Scale**(大きさ)もTransformでそのまま調整できます。全Playerが同時に入れる広さにしてください。
- Colliderは自動的にTrigger(すり抜け可能)になっているため、Player・Domino双方の物理演算を妨げません。
- Start Area**内で最初にDominoを倒す**ことが「Startを切った」の合図になります。Dominoを1個は置けるスペースを確保してください。

Script内にワールド座標をハードコードしている箇所はありません。位置・大きさは完全にTransformだけで決まります。

---

## 4. Checkpointの追加方法・Checkpoint Indexの設定方法

1. `Checkpoint.prefab` をSceneへドラッグ&ドロップします。
2. 配置したCheckpointを選択し、InspectorでComponent `Checkpoint Zone` を開きます。
3. `Checkpoint Index` フィールドに、通過してほしい順番を入力します。**0始まり**です(最初に通れるCheckpointが `0`)。

```
1個目に通ってほしいCheckpoint → Checkpoint Index = 0
2個目に通ってほしいCheckpoint → Checkpoint Index = 1
3個目に通ってほしいCheckpoint → Checkpoint Index = 2
```

Hierarchy上の並び順(上から何番目にあるか)は判定に一切使われません。**必ず `Checkpoint Index` の値が正**です。名前を `Checkpoint_01`、`Checkpoint_02` のように付けておくと、後から見て分かりやすくなります。

`Fallen Angle Threshold`(既定60°)は、「Dominoが何度以上傾いたら『倒れた』とみなすか」の設定です。通常は変更不要です。

---

## 5. Checkpoint数を増減する方法

**Checkpointを増やす場合**:
1. 既存のCheckpointをコピー&ペースト(Ctrl+D)するか、新たに `Checkpoint.prefab` を配置します。
2. `Checkpoint Index` を、他のどのCheckpointとも重複しない値に設定します。
3. `GameFlowManager` のInspectorで、`Checkpoints` 配列にこの新しいCheckpointを追加登録します(6章参照)。

**Checkpointを減らす場合**:
1. 不要なCheckpointをHierarchyから削除します。
2. `GameFlowManager` の `Checkpoints` 配列から、削除したCheckpointへの参照を外します(Unityが自動的に `None` にする場合もありますが、配列のサイズを詰めておくと安全です)。
3. 残ったCheckpointの `Checkpoint Index` が `0` から連番になっているか確認してください(歯抜けがあっても動作はしますが、`0, 1, 2...` の連番にしておくのが分かりやすく安全です)。

**この作業に、C#スクリプトの変更は一切必要ありません。** `GameFlowManager.cs` は `Checkpoints` 配列の中身を実行時に `Checkpoint Index` の昇順で並べ替えて使うため、配列に何個登録されていても、また登録順がバラバラでも正しく動作します。

---

## 6. Goal Areaの置き方

`GoalArea.prefab` をSceneへ配置し、Start Areaと同様にTransformで位置・大きさを調整します。Goal Area内で、**すべてのCheckpointを正しい順序で通過した後に**Dominoが倒れると、Challengeがクリア(SUCCESS)になります。

---

## 7. GameFlowManagerへの登録が必要な場合、その方法

Zoneを新しく置いた・削除した場合は、必ず `GameFlowManager` のInspectorで参照を更新してください。

1. Hierarchyで `GameFlowManager` を選択します。
2. Inspectorに以下の3つのフィールドがあります。
   - `Start Area`: `StartAreaZone` を1つドラッグ&ドロップ
   - `Checkpoints`: `CheckpointZone` の配列。要素数を増減させてから、各要素にCheckpointをドラッグ&ドロップ
   - `Goal Area`: `GoalAreaZone` を1つドラッグ&ドロップ
3. 登録し忘れると、そのZoneは判定に一切参加しません(倒しても何も起きません)。Console等にErrorは出ないため、**Playして実際に反応するか確認する**ことをおすすめします。

`GameFlowManager` はScene起動時に自動でZoneを検索する仕組み(`FindObjectOfType`等)を使っていません。これは、Sceneに複数のStageやテスト用オブジェクトが混在していても、意図しないZoneを拾ってしまわないようにするためです。

---

## 8. Player Spawn位置について

Player(`Player.prefab`)は、Scene上に直接配置されているわけではなく、`NetworkManager` が接続時に自動生成します。現在の初期スポーン地点はNetcode for GameObjectsの既定位置(おおよそ原点付近)です。Stageのレイアウトを大きく変える場合は、Playerが迷わずStart Areaへ向かえる距離にStart Areaを配置することをおすすめします(将来的にStage固有のSpawn Point機能を追加する場合は別途対応が必要です)。

---

## 9. Overview Camera Pointの置き方(Step 7.6で追加、Step 14.4で複数対応)

RUNNING/SUCCESS/FAILED中、各Playerの画面は自動的に「Overview Camera」(Stage全体を見渡す固定視点)へ切り替わります。この視点は`OverviewCameraPoint`というGameObjectのTransformとFOV設定だけで決まり、**スクリプトを一切書き換える必要がありません**。

1. Sceneに空のGameObjectを作成し、名前を`CameraPoints`とします(既存の`GameFlowZones`と同じ階層に置くと分かりやすいです)。
2. `CameraPoints`の子として空のGameObjectを作成し、名前を`OverviewCameraPoint`とします(2つ目以降を追加する場合は`OverviewCameraPoint_02`のように連番にすると分かりやすいです - 名前自体はCode上意味を持ちません、下記参照)。
3. 各`OverviewCameraPoint`に`Domino.CameraSystem.OverviewCameraPoint`コンポーネントを追加します。
4. 各`OverviewCameraPoint`のTransform(`Position`/`Rotation`)を、見せたい範囲が画面に収まるような位置・向きに調整します。Sceneビューで実際に見え方を確認しながら調整するのが確実です。
5. 各コンポーネントの`Field Of View`(既定60°)で画角を調整します。ルートが長い場合は数値を大きく、コンパクトな場合は小さくしてください。

```
Stage
├─ GameFlowZones
│   ├─ StartArea
│   ├─ Checkpoint_01
│   ├─ Checkpoint_02
│   └─ GoalArea
└─ CameraPoints
    ├─ OverviewCameraPoint     (Domino.CameraSystem.OverviewCameraPoint, Field Of View)
    ├─ OverviewCameraPoint_02  (同上、Step 14.4以降は複数配置可能)
    └─ OverviewCameraPoint_03  (同上)
```

### 複数配置した場合の挙動(Step 14.4)

`OverviewCameraPoint`は**複数配置できます**。`CameraPoints`配下の`Hierarchy Sibling Order`(Hierarchy上の並び順、ドラッグで並べ替え可能)がそのままCycle順序になります - Component自身は`CameraPoints`の直接の子である必要はなく、`OverviewCameraPoint`Componentさえ付いていれば、Scene内のどこにあっても自動的に集められます(名前のHard-codeやParentの厳密なチェックはしていません)。

RUNNING中、Player(Local Client)は`LMB`で次のPoint、`RMB`で前のPointへCycleできます(端まで行くと逆側へWrap Aroundします)。これは各Clientが自分の画面でだけ独立に選べるLocalな見た目の好みであり、Network同期は一切しません(HostがPoint 2を見ていても、ClientはPoint 1のままで構いません)。新しいChallengeが始まる(READY→RUNNINGへ突入する)たびに、最初のPoint(Sibling Order最小)へ自動的に戻ります。

**1つしか配置しなかった場合(Step 14.4以前と同じ)は、LMB/RMBを押しても何も起こらず安全にNo-opです(Errorにはなりません)。** 既存のStage(SampleScene/Stage 2/Stage 3、いずれも1つのみ配置)は、この変更前と全く同じ見た目・挙動のまま動作します。

**Sceneに`OverviewCameraPoint`を1つも置かなかった場合、Overview Cameraへの切り替えは単純にスキップされ、Third Person Cameraのまま**になります(Errorにはなりません、Step 7.6から変更なし)。

### RUNNING中のPlayer移動可能範囲(Start Area Constraint)について

RUNNING/SUCCESS/FAILED中、Playerは`StartAreaZone`の範囲内でのみ移動できます。この範囲は`StartAreaZone`が既に持っているBoxColliderのサイズ(Position/Rotation/Scaleも含む)からそのまま自動的に決まるため、Overview Cameraと同様に**追加の設定は不要**です。`StartAreaZone`のInspectorにある`Player Radius Margin`(既定0.5m)は、Player CapsuleがCollider境界からはみ出さないための余白で、Playerモデルのサイズを変更した場合はここを調整してください。

---

## 10. よくある設定ミス

| 症状 | 原因 | 対処 |
|---|---|---|
| Checkpointを倒しても何も起きない | `GameFlowManager` の `Checkpoints` にそのZoneが未登録 | 7章の手順で登録する |
| 正しい順番で倒しているのにProgressが進まない | `Checkpoint Index` が重複している、または連番になっていない | 4章を参照し、0始まりの連番に修正する |
| BUILDING中にDominoを倒すとProgressが進んでしまう | あり得ません(Host側で`GamePhase == RUNNING`を必ず確認しています)。進んでいるように見える場合は、Playモードを抜けて入り直し、`GameFlowManager` の Inspector値(`Phase`が実際に`BUILDING`か)を確認してください | - |
| READYにならない | Playerの誰かがStart Areaの範囲外にいる。Start Areaの `Scale` が狭すぎて、複数Playerが同時に入りきらない可能性がある | Start AreaのScaleを広げる |
| Fキーを押してもRUNNINGにならない | READY状態になっていない(誰かがStart Areaの外にいる) | 全Playerが確実にStart Area内にいることを確認する |
| Goalを倒してもSUCCESSにならない | Checkpointを全て正しい順序で通過していない | 各Checkpointの`Checkpoint Index`と実際に倒した順序を確認する |
| Dominoを置いてもZoneが反応しない | Domino以外のオブジェクト(Ground等)を誤って配置している、またはDomino.prefab以外を使っている | `Domino.prefab`(Tag: `Domino`)を使用しているか確認する |
| Zoneの中に置いたのに反応しない | Dominoが倒れていない(角度が`Fallen Angle Threshold`未満) | Dominoをしっかり倒す。閾値を下げたい場合はCheckpoint/Start/GoalのInspectorで調整可能 |
| RUNNING中もThird Person Cameraのまま(Overview Cameraに切り替わらない) | Sceneに`OverviewCameraPoint`が1つも配置されていない | 9章の手順で`CameraPoints/OverviewCameraPoint`を配置する |
| RUNNING中、Playerが窮屈すぎて動けない/Start Dominoに届かない | Start Areaの`Scale`が小さすぎる、または`Player Radius Margin`が大きすぎる | Start AreaのScaleを広げるか、`StartAreaZone`の`Player Radius Margin`を小さくする |

---

## 11. Step 3のテスト用Domino(DominoLine)について

Scene内には、Step 3で作成した`DominoLine`(20個のDomino)が非アクティブ状態で残っています。これはPhysicsの検証用テストコンテンツであり、Stage Authoringとは無関係です。誤って削除しないよう注意してください。
