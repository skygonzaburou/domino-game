# Character System (Step 12 / Step 12.1)

Player Root(`Rigidbody`/`CapsuleCollider`/`PlayerMotor`/`NetworkObject`/`NetworkTransform`等)は一切変更せず、旧CapsuleのVisualをMeshy AI製Humanoidキャラクター「GreenBoy」へ差し替えたSystem(Step 12)、およびPlayerが使用するCharacterをMain Menuで選べるようにしたCharacter Select機能(Step 12.1)についてのドキュメント。Menu側のPanel/Flowは`MenuFlow.md`、Online同期は`OnlineSession.md`、Pause Menuからの遷移は`PauseSystem.md`を参照してください。

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

## 既知の制約・意図的に対応しなかった事項(Step 12 Section 30)

- Metallic/RoughnessのPBR精密変換は行わず、`Metallic = 0`, `Smoothness = 0.35`の簡易値を採用(URP/LitのMetallicGlossMapはAlpha=Smoothnessを要求するが、MeshyのRoughness Textureはこれと直接対応しないため)
- Character Select UI・複数キャラクター・Skin/装備・表情/IK・攻撃モーション・Emote・Animation Sound・Save/Unlock・Production VFXは本Step対象外(→ Character Select UI・複数キャラクターはStep 12.1で実装)

---

# Character Select / Character Catalog(Step 12.1)

Character 1体・GreenBoy Prefab 1つに固定だったStep 12へ、「Playerが使用するCharacterをMain Menuで選べる」仕組みを追加しました。**Player Gameplay Architecture(Rigidbody/CapsuleCollider/PlayerMotor/PlacementController/PlayerPushController/PlayerAnimationController/GreenBoy Animator/NetworkAnimator/`applyRootMotion = false`)は一切作り直していません** - Character Selectionが変えるのは「Player配下のどのVisual Rootが有効か」だけです。

## Data-driven Character Catalog

Stage Catalog(`StageDefinition`/`StageCatalog`)と全く同じ設計思想です。

```csharp
[CreateAssetMenu(menuName = "Domino/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    [SerializeField] private int characterId;
    [SerializeField] private string displayName;
    [SerializeField] private string visualKey;   // Player配下のVisual Root GameObject名と一致させる
}
```

```
Assets/_Project/Scripts/Menu/CharacterDefinition.cs
Assets/_Project/Scripts/Menu/CharacterCatalog.cs
Assets/_Project/Data/Characters/Character_01.asset 〜 Character_20.asset
Assets/_Project/Data/CharacterCatalog.asset
```

| Character ID | displayName | visualKey |
|---|---|---|
| 1 | Classic | `Visual`(既存Capsule) |
| 2〜20 | Green Boy | `GreenBoyVisual` |

**IDごとのVisualをHard-codeしたswitch/ifは存在しません。** `PlayerCharacterVisual.cs`(Player Root上)は、Catalogから解決した`visualKey`と一致する名前のGameObjectだけを`SetActive(true)`にし、他をすべて`false`にするだけです。

```csharp
private void ApplyVisual(int characterId)
{
    CharacterDefinition definition = catalog.FindById(characterId);
    string visualKey = definition != null ? definition.VisualKey : ResolveFallbackVisualKey();
    foreach (GameObject root in visualRoots)
        root.SetActive(root.name == visualKey);
}
```

`visualRoots`は`[Player/Visual, Player/GreenBoyVisual]`の2要素のみの配列です。Character ID 2〜20はすべて同じ`"GreenBoyVisual"`という文字列を指すため、**GreenBoy Visualは1つしか存在せず、19体分Prefabを複製するようなことはしていません**。次のStepで新しいCharacterを追加する場合は、Playerへ新しいVisual子オブジェクトを1つ追加し、対応する`CharacterDefinition`アセットで`visualKey`を指すだけで済み、この解決ロジック自体に変更は不要です。

## `PlayerCharacterVisual.cs`(Player Root、NetworkBehaviour)

```csharp
private readonly NetworkVariable<int> _characterId = new NetworkVariable<int>(
    FallbackCharacterId, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
```

Server-authoritative(Section 15)。`ServerSetCharacterId(int)`はServer-onlyで、`GameplayPlayerSpawner`がPlayer Spawn直後に1回だけ呼びます。Catalogに存在しないIDが渡された場合は自動的にCharacter 1(Classic Capsule)へFallbackし(Section 24)、Gameplay PlayerがInvisibleになることはありません。`_characterId.OnValueChanged`で全ClientがVisualを更新するため、Client Localだけの見た目切り替えは発生しません。

## Gameplay Character割り当て(`GameplayPlayerSpawner`)

```csharp
int characterId = GameSessionContext.IsOnline
    ? (NetworkSessionState.Instance != null ? NetworkSessionState.Instance.GetSelectedCharacterId(clientId) : 1)
    : GameSessionContext.SelectedCharacterId;

PlayerCharacterVisual characterVisual = spawned.GetComponent<PlayerCharacterVisual>();
characterVisual?.ServerSetCharacterId(characterId);
```

Single Playerは接続ClientがHost自身1人だけのため`GameSessionContext.SelectedCharacterId`(static, Local)を直接使用します。OnlineはClientごとに異なる選択を持つため、Server側の`NetworkSessionState`(後述)から`clientId`単位で解決します。

## PlayerAnimationController側の対応

Character 1(Capsule)にはGreenBoy Animatorを表示する必要がないため(Step 12.1 Section 13)、`PlayerAnimationController.Update()`の先頭に1行のGuardを追加しました。

```csharp
if (!IsOwner || animator == null || !animator.gameObject.activeInHierarchy) return;
```

Character固有の分岐は一切なく、「参照しているAnimatorが現在Activeかどうか」だけを見ています。GreenBoy Animator Controller自体はStep 12から無変更です。

## Placement / Camera / Physicsへの影響

Character Selectionは見た目(どのVisualが有効か)だけを変え、Rigidbody/CapsuleCollider/Mass/Movement Speed/Jump/Slow Walk/Run/Player Push/Domino Contact/Placement Marker/Camera Targetは一切変更していません(Section 25/26)。Player Root Forwardは常にPlacement Markerの基準のままです。

## Single Player Selection Lifecycle

```
GameSessionContext.SelectedCharacterId (static, default = 1 = Classic Capsule)
  ↑ CharacterSelectController: Card Click時に即座に更新(Sectionn 8, Stage Selectへは進まない)
  ↓
GameplayPlayerSpawner (Spawn時に読み取り、PlayerCharacterVisual.ServerSetCharacterId()へ)
```

Pause → Character Select → Confirm で戻った場合も、`GameSessionContext.SelectedCharacterId`はStatic Fieldのため保持されたままで、再訪したCharacter SelectパネルはSelection Highlightを正しく復元します(Section 30)。

## Online Per-player Selection

`NetworkSessionState`(`OnlineSession.md`参照)がServer-only Dictionaryとして`ClientId → CharacterId`を保持します。各Clientは`RequestSelectCharacterRpc(int characterId)`で**自分自身の**選択だけをServerへ送信でき(Host-onlyではない、Section 16)、Server側でCatalog存在チェックのみ行います(Prefab参照等は一切送受信しません、Section 17)。詳細は`OnlineSession.md`「11. Character Select」を参照してください。
