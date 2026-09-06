# Control Guide (Step 11)

Gameplay画面左下に常時表示される、現在の操作Contextに応じたControl Guide UIについてのドキュメント。

## 目的

既存の操作(Player Movement/Camera/Placement/Pause/Challenge Start)は一切変更しない。追加したのは「今どのKeyを押せば何ができるか」をGameplay画面上で分かるようにするUIのみ。

## 責務分離

- `ControlGuideHUD.cs`(`Domino.UI`名前空間、`SampleScene`の`GameplayHUD` Canvas配下`ControlGuidePanel`にアタッチ)
  - 責務: Current Context判定 / Hint表示切替 / Hold Key Pressed可視化
  - 持たないもの: Gameplay Logic、Input判定の書き込み、RPC送信、NetworkVariable
- 既存の`GameFlowManager.CurrentPhase` / `GameplayPauseManager.IsPaused` / `PlacementController.IsPlacing`(いずれも元から公開されているState)を読み取るだけで、Input System・既存Input処理コードには一切書き込まない(Second Source of Truthを避けるため、独自のKey Mapping定義は持たず、既存の`Keyboard.current`/`Mouse.current`をそのまま参照している)

## Context一覧と優先順位

```
Confirmation Dialog / Pause Menu / SUCCESS Result 表示中
  → Control Guide 非表示(既存のR/S/T等のShortcut表示と二重にしない)

Placement Mode (PlacementController.IsPlacing == true)
  → Placement Guide: A/D Rotate, LMB Place, Esc Cancel

READY (Placementしていない)
  → Normal Guide + F Start Challenge

RUNNING (Placementしていない)
  → Normal Guide + F Cancel Run (Step 14.3で追加、READYと同じRow/Keycapを共有しLabelのみ切替)
  → Overview Camera Pointが2個以上ある場合のみ、LMB Next Camera / RMB Previous Camera も追加表示(Step 14.4.1で追加)

BUILDING / FAILED (Placementしていない)
  → Normal Guide (BUILDINGのみ E Place Domino / RMB Lock Marker を追加表示)
```

Normal Guide = `W/A/S/D Move`, `Shift Run`, `Space Jump`, `Mouse Look`, `Esc Pause`

## Hold Key Pressed可視化

W/A/S/D/Shift(Normal Guide)、A/D(Placement Guide)は、実際に押している間だけKeycap Imageの色を`keycapNormalColor`から`keycapPressedColor`へ切り替える。E/F/Esc/LMB/RMB等のOne-shot入力はKeycap表示のみで、Pressed Animationは持たない。

Key Press Visualizationは完全にLocal Player専用のUI表示であり、NetworkVariable/RPCは一切追加していない(他のPlayerの画面には影響しない)。

## Current Controls Audit

Step 11実装前に、Gameplayで実際に使用されているInputをコードから棚卸しした結果。詳細は`Docs/ImplementationStatus.md`のStep 11の章を参照。

| Key | Action | 実装ファイル |
|---|---|---|
| W/A/S/D | Move | `PlayerMotor.cs` |
| Shift | Run(Dash) | `PlayerMotor.cs` |
| Space | Jump | `PlayerMotor.cs` |
| Mouse | Camera Look | `ThirdPersonCamera.cs` |
| RMB | Marker Lock/Re-lock | `PlacementController.cs` |
| E | Placement Mode突入 | `PlacementController.cs` |
| A/D(Placement中) | Domino Yaw回転 | `PlacementController.cs` |
| LMB | Domino確定配置 | `PlacementController.cs` |
| Esc | Cancel Placement / Pause(Context依存) | `PlacementController.cs` / `GameplayHUD.cs` |
| F | Start Challenge Request(READY) / Cancel Run Request(RUNNING、Step 14.3で追加) | `ChallengeInput.cs` |
| LMB(RUNNING中) | 次のOverview Camera Pointへ切替(Step 14.4で追加) | `ThirdPersonCamera.cs` |
| RMB(RUNNING中) | 前のOverview Camera Pointへ切替(Step 14.4で追加) | `ThirdPersonCamera.cs` |

`Space`(Jump)は既存コードには実装済みだったが、今回のStepで初めて明示的に確認された。新規実装は行わず、既存機能としてControl Guideに含めている。

### Overview Camera切替(LMB/RMB)のControl Guide表示行追加(Step 14.4.1)

Step 14.4時点では上記の理由でGuide Row未追加だったが、Step 14.4.1でRuntime生成方式により追加した。Scene File(`SampleScene`/`StageScene_2`/`StageScene_3`のいずれも)には一切手を加えていない。

- 実装方式: `ControlGuideHUD.cs`が起動時に`runRow`(既存の`Shift Run` Row)を`Instantiate()`で複製し、`GetComponentsInChildren<TextMeshProUGUI>(true)`で見つけた子TextのうちKeycap文字を`"LMB"`/`"RMB"`、Label文字を`"Next Camera"`/`"Previous Camera"`へRuntimeで書き換えて`_nextCameraRow`/`_previousCameraRow`として保持する(`EnsureCameraGuideRows()`/`CreateCameraGuideRow()`)。複製後は`SetSiblingIndex()`で`startChallengeRow`の直前に挿入し、既存のNormal Guideと同じ縦並びに収まる。新しいSerialized FieldやScene上のGameObjectは一切追加していないため、Step 14.3の「F Keyの二重の役割」と同じく3 Scene全てで同一コードがそのまま動作する。
- 複製元に`runRow`(Wide Keycap、`Key_Shift`と同じ`MinWidth=60`)を選んだ理由: `"LMB"`/`"RMB"`は3文字であり、`E`/`F`用の`Key_F`(`MinWidth=26`、単一文字向け)では文字がKeycapからはみ出すため。
- 表示条件: `GameFlowManager.CurrentPhase == RUNNING`かつPlacementしていない(既存のF Cancel Run表示条件と同じ`canCancelChallenge`)に加えて、`OverviewCameraPoint.Points.Count > 1`(Section 14.4.1-23の推奨どおり、Overview Camera Pointが2個以上あるStageでのみ表示)。1個以下のStage(Step 14.4.1時点のStage 2/3)では現状Guide Rowは表示されない。
- Pressed Highlight: 既存のPlacement Guide LMB/RMB Keycapと全く同じ`SetPressed()`パターンで、`Mouse.current.leftButton.isPressed`/`rightButton.isPressed`をそのまま参照して押下中のみ`keycapPressedColor`に切り替える。
- Camera切替Logic自体(`ThirdPersonCamera.cs`のCycling実装、`OverviewCameraPoint`)はStep 14.4.1で一切変更していない。あくまで既存動作をHUD上に可視化しただけ。

## F Keyの二重の役割とLabel切替(Step 14.3)

`ChallengeInput.cs`は、F押下時に`GameFlowManager.CurrentPhase`(Local読み取り)を見て、READY中は`RequestStartChallengeRpc()`、RUNNING中は`RequestCancelChallengeRpc()`を送り分ける。`ControlGuideHUD.cs`側は、既存の`startChallengeRow`(Scene上に元からある唯一のRow)を両方のPhaseで共有し、`GetComponentInChildren<TextMeshProUGUI>()`でRuntimeに見つけた子TextのLabelだけを"Start Challenge"/"Cancel Run"へ切り替える(新しいRow GameObjectを追加していない)。これは`SampleScene`/`StageScene_2`/`StageScene_3`いずれもこの`ControlGuideHUD`を個別に持っている(Step 13でDuplicateされたScene)ため、新しいSerialized Field/新しいRow GameObjectを追加すると3 Scene全てへ同じWiringを繰り返す必要があり、Stage 2/3のScene Authoring凍結方針と両立しないための設計判断。
