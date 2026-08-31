# StageAuthoring(新しいStageの作り方)

このドキュメントは、**プログラムを書かずに**新しいStage(Start/Checkpoint/Goalの配置)をUnity Editor上で作成・調整するための実用的な手順書です。このドキュメントを見ながら操作すれば、Claudeへコード修正を依頼しなくても基本的なStageを組み立てられることを目標にしています。

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

## 9. よくある設定ミス

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

---

## 10. Step 3のテスト用Domino(DominoLine)について

Scene内には、Step 3で作成した`DominoLine`(20個のDomino)が非アクティブ状態で残っています。これはPhysicsの検証用テストコンテンツであり、Stage Authoringとは無関係です。誤って削除しないよう注意してください。
