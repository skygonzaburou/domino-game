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

BUILDING / RUNNING / FAILED (Placementしていない)
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
| F | Start Challenge Request | `ChallengeInput.cs` |

`Space`(Jump)は既存コードには実装済みだったが、今回のStepで初めて明示的に確認された。新規実装は行わず、既存機能としてControl Guideに含めている。
