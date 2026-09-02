# Character System (Step 12)

Player Root(`Rigidbody`/`CapsuleCollider`/`PlayerMotor`/`NetworkObject`/`NetworkTransform`等)は一切変更せず、旧CapsuleのVisualをMeshy AI製Humanoidキャラクター「GreenBoy」へ差し替えたSystemについてのドキュメント。

## 責務分離

- **Player Root**(`Player.prefab`直下): Gameplay/Physics/Networkの唯一の権威。CapsuleCollider(radius=0.4, height=2, center=(0,1,0))、Rigidbody(mass=1)は無変更
- **`Player/Visual`**: 旧Capsule + Nose。**Disable(非Active)のみで削除していない** — 将来Debug Visualとして即座に復元可能
- **`Player/GreenBoyVisual`**: `Assets/_Project/Prefabs/Characters/GreenBoy.prefab`のInstance。Visual専用、Gameplay Logicを一切持たない
- **`PlayerAnimationController.cs`**(`Domino.Player`名前空間、Player Root上): 既存Gameplay State(`PlayerMotor.MoveAxis`/`IsDashHeld`/`IsSlowWalkHeld`/`IsGrounded`/`Jumped`イベント、`GameFlowManager.CurrentPhase`)を**読み取るだけ**でAnimator Parameterを書き込む。Gameplay Logicへの書き込みは一切なし

## GreenBoy.prefab構造

```
GreenBoy (root, empty)
└─ Model (Meshy_AI_..._Character_output.fbx のInstance, localPosition Y=-0.04)
    ├─ Animator (Avatar = Humanoid, Apply Root Motion = false)
    ├─ NetworkAnimator (Authority Mode = Owner)
    ├─ Armature (Humanoid Rig, Mixamo系標準Bone命名)
    └─ char1 (SkinnedMeshRenderer, Material = GreenBoy_Material)
```

- Y = -0.04のOffsetは、FBXのBone座標(LeftToeBase ≈ Y0.04)から実測して、Feetが地面(Player root Y=0)に接するよう調整したもの
- Modelの回転・Scaleは共にIdentity — Character FBXの`headfront`ボーン(Head相対+Z方向)・`RightHand`(+X側)を実測した結果、Meshyの元Modelは既にUnityの`transform.forward`(+Z)と一致する向きでExportされており、Player Rootとの向き合わせのためのRotation補正は不要だった
- `head_end`ボーン(頭頂)実測High Y≈1.71m。既存CapsuleCollider(height=2)に対して自然に収まるため、Visual Scaleの変更も不要(等身大のまま使用)

## Humanoid Avatar

- Character FBX(`Meshy_AI_Green_Shirt_Explorer_biped_Character_output.fbx`): `Animation Type = Humanoid`, `Avatar Definition = Create From This Model`。Mixamo系の標準Bone命名(Hips/LeftUpLeg/.../Spine/LeftShoulder/.../neck/Head)により、22 Humanoid Boneすべてが手動介入なしで自動マッピングされ、Avatar.isValid = true
- Animation専用の6本のFBX(Idle_3/Idle_8/Walking/Running/Regular_Jump/FunnyDancing_01、いずれも`_withSkin`): `Avatar Definition = Copy From Other Avatar` → Character FBXのAvatarを共有。全6本ともRig互換で追加のBone修正なしに成功

## Animator Controller

`Assets/_Project/Art/Animations/Shared/GreenBoy.controller`

### Parameter
| Name | Type | 用途 |
|---|---|---|
| `Speed` | Float | 0=静止, 1=移動中(閾値0.1) |
| `IsRunning` | Bool | Shift Dash中(かつSlow Walk優先ではない) |
| `IsSlowWalk` | Bool | Ctrl Slow Walk中 |
| `IsIdleLong` | Bool | 3秒以上静止継続 |
| `Grounded` | Bool | 接地状態(現状Transition未使用、将来拡張用に公開) |
| `JumpTrigger` | Trigger | `PlayerMotor.Jumped`イベント発火時に`NetworkAnimator.SetTrigger()`経由でセット |
| `IsVictory` | Bool | `GameFlowManager.CurrentPhase == GamePhase.SUCCESS` |

### State / Transition概要

```
Idle3 (Default) ⇄ Idle8        [IsIdleLong]
Idle3/Idle8 → Walk/Run          [Speed > 0.1, IsRunning]
Walk ⇄ Run                      [IsRunning]
Walk/Run → Idle3                [Speed < 0.1]
Any State → Jump                [JumpTrigger]  (優先度: Dance > Jump > Run/Walk/Idle)
Jump → Idle3/Walk/Run           [Exit Time ≈0.9, 着地時のSpeed/IsRunningで分岐]
Any State → Dance               [IsVictory]    (最優先)
Dance → Idle3                   [!IsVictory]
```

全TransitionはCross Fade(0.1〜0.25秒)。Hard Snapは使用していない。

### Loop設定(Import時、Clip単位)
| Clip | Loop |
|---|---|
| Idle_3 / Idle_8 / Walking / Running | Loop |
| Regular_Jump | Non-Loop(one-shot) |
| FunnyDancing_01 | Loop(Victory Animationとして自然な継続再生を意図した判断) |

## Root Motion(Section 5, 最重要)

`Animator.applyRootMotion = false`をGreenBoy.prefab側で固定。Walking/Running/Jump等のClipがPlayer位置を動かすことは一切なく、移動は従来通り100% `PlayerMotor` → `Rigidbody` → `NetworkTransform`が担う。Play Modeでの検証時、Jump/Dance再生中もPlayer Root位置(`transform.position`)が完全に不変であることを確認済み。

## Network同期(Section 20/21)

- `NetworkAnimator`(NGO 2.13.2, `Unity.Netcode.Components`)を`GreenBoy.prefab`の`Model`に追加。`AuthorityMode = Owner` — 既存の`NetworkTransform`/`PlayerMotor`と同じOwner-Authoritativeパターンに合わせた設定
- `PlayerAnimationController.Update()`は`IsOwner`のClientでのみAnimator Parameterを書き込み、それ以外のClientでは実行しない(`PlayerMotor.ReadInput()`と同じGating)。NetworkAnimatorがOwnerのAnimator状態を他Clientへ自動的にReplicateする
- Jump TriggerはNetwork上で確実に一回のPulseとして同期させるため、`Animator.SetTrigger()`ではなく`NetworkAnimator.SetTrigger(string)`を経由
- Idle3→Idle8の3秒Timerは`PlayerAnimationController`内のLocal(per-Client)な`float`のみで管理。NetworkVariable/RPCは追加していない — 各Clientの見た目が数フレームずれても実害のないVisual-onlyな効果のため

## 既知の制約・意図的に対応しなかった事項(Section 30)

- Metallic/RoughnessのPBR精密変換は行わず、`Metallic = 0`, `Smoothness = 0.35`の簡易値を採用(URP/LitのMetallicGlossMapはAlpha=Smoothnessを要求するが、MeshyのRoughness Textureはこれと直接対応しないため)
- Character Select UI・複数キャラクター・Skin/装備・表情/IK・攻撃モーション・Emote・Animation Sound・Save/Unlock・Production VFXは本Step対象外
