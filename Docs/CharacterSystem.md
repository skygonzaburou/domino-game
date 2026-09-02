# Character System (Step 12 / Step 12.1 / Step 12.1.2 / Step 13.2)

Player Root(`Rigidbody`/`CapsuleCollider`/`PlayerMotor`/`NetworkObject`/`NetworkTransform`等)は一切変更せず、旧CapsuleのVisualをMeshy AI製Humanoidキャラクター「GreenBoy」へ差し替えたSystem(Step 12)、Playerが使用するCharacterをMain Menuで選べるようにしたCharacter Select機能(Step 12.1)、およびそのCharacter Selectionの導入によって顕在化したAnimation Network Sync/Jumpのバグ修正(Step 12.1.2)についてのドキュメント。Menu側のPanel/Flowは`MenuFlow.md`、Online同期は`OnlineSession.md`、Pause Menuからの遷移は`PauseSystem.md`、NetworkAnimatorの配置に関する詳細は`Networking.md`を参照してください。

## 責務分離

- **Player Root**(`Player.prefab`直下): Gameplay/Physics/Networkの唯一の権威。CapsuleCollider(radius=0.4, height=2, center=(0,1,0))、Rigidbody(mass=1)は無変更。**Step 12.1.2から`NetworkAnimator`もここに常駐**(下記「NetworkAnimatorの配置」参照)
- **`Player/Visual`**: 旧Capsule + Nose。**Disable(非Active)のみで削除していない** — 将来Debug Visualとして即座に復元可能
- **`Player/GreenBoyVisual`**: `Assets/_Project/Prefabs/Characters/GreenBoy.prefab`のInstance。Visual専用、Gameplay Logicを一切持たない
- **`PlayerAnimationController.cs`**(`Domino.Player`名前空間、Player Root上): 既存Gameplay State(`PlayerMotor.MoveAxis`/`IsDashHeld`/`IsSlowWalkHeld`/`IsGrounded`/`Jumped`イベント、`GameFlowManager.CurrentPhase`)を**読み取るだけ**でAnimator Parameterを書き込む。Gameplay Logicへの書き込みは一切なし

## GreenBoy.prefab構造

```
GreenBoy (root, empty)
└─ Model (Meshy_AI_..._Character_output.fbx のInstance, localPosition Y=-0.04)
    ├─ Animator (Avatar = Humanoid, Apply Root Motion = false)
    ├─ Armature (Humanoid Rig, Mixamo系標準Bone命名)
    └─ char1 (SkinnedMeshRenderer, Material = GreenBoy_Material)
```

`NetworkAnimator`はStep 12ではこの`Model`上に置いていましたが、Step 12.1.2でPlayer Rootへ移動しました(下記「NetworkAnimatorの配置(Step 12.1.2)」参照)。`Model`には引き続き`Animator`本体のみが残ります。

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

## Network同期(Section 20/21、Step 12.1.2で配置修正)

- `NetworkAnimator`(NGO 2.13.2, `Unity.Netcode.Components`)は**Player Root**に常駐(Step 12では`GreenBoy.prefab`の`Model`にあったが、Step 12.1.2で移動 — 理由は下記「NetworkAnimatorの配置(Step 12.1.2)」参照)。`Animator`参照は`GreenBoyVisual/Model`のAnimatorを指すCross-GameObject参照。`AuthorityMode = Owner` — 既存の`NetworkTransform`/`PlayerMotor`と同じOwner-Authoritativeパターンに合わせた設定
- `PlayerAnimationController.Update()`は`IsOwner`のClientでのみAnimator Parameterを書き込み、それ以外のClientでは実行しない(`PlayerMotor.ReadInput()`と同じGating)。NetworkAnimatorがOwnerのAnimator状態を他Clientへ自動的にReplicateする
- Jump TriggerはNetwork上で確実に一回のPulseとして同期させるため、`Animator.SetTrigger()`ではなく`NetworkAnimator.SetTrigger(string)`を経由
- Idle3→Idle8の3秒Timerは`PlayerAnimationController`内のLocal(per-Client)な`float`のみで管理。NetworkVariable/RPCは追加していない — 各Clientの見た目が数フレームずれても実害のないVisual-onlyな効果のため

## NetworkAnimatorの配置(Step 12.1.2)

**Root Cause**: Step 12.1でCharacter Selectionを導入した際、`NetworkAnimator`は元々`GreenBoy.prefab`の`Model`(Character 1選択時は非Activeになる`GreenBoyVisual`の子)に置かれたままでした。NGOの`NetworkObject.InvokeBehaviourNetworkSpawn()`は、Spawnする各`NetworkBehaviour`について`gameObject.activeInHierarchy`を確認し、非Activeな場合は`OnNetworkSpawn()`の呼び出しを**Warningログのみで黙ってSkip**します(`ChildNetworkBehaviours`を辿る際、Error扱いにはならないため気づきにくい)。

さらに`PlayerCharacterVisual.OnNetworkSpawn()`は、`GameplayPlayerSpawner`が実際のCharacter IDを設定する**前**に、同じSpawnパス中で`NetworkVariable<int> _characterId`のDefault値(`FallbackCharacterId = 1` = Capsule)を使って`ApplyVisual(1)`を即座に実行します。これにより、Character 2〜20(GreenBoy)を選んでいても、Spawnの瞬間には一時的に`GreenBoyVisual`が非Activeとなり、その配下にあった`NetworkAnimator`の`OnNetworkSpawn()`が永久にSkipされていました。

結果、`NetworkAnimator`内部のPrivateフィールド`m_NetworkAnimatorStateChangeHandler`(`OnNetworkSpawn()`内でのみ初期化される)が`null`のままとなり、Jump時の`NetworkAnimator.SetTrigger()`が`NullReferenceException`を投げ(Single PlayerのBug A)、かつRemote Client向けのContinuous Animator Parameter同期も一切開始しない(Online RemoteのBug B)という2つの不具合が同時に発生していました。`NetworkAnimator.IsSpawned`は外側の`NetworkObject`のSpawn状態を反映するだけで、この個別`NetworkBehaviour`自身の`OnNetworkSpawn()`が実際に走ったかどうかは示さないため、表面的なチェックでは正常に見えてしまう点も発見を難しくしていました。

**Fix**: `NetworkAnimator`を**Player Root**(`Player.prefab`直下、Character Selectionに関わらず常にActive)へ移動し、`Animator`参照だけは引き続き`GreenBoyVisual/Model`のAnimatorを指すようにしました。これにより、`NetworkAnimator`自身の`OnNetworkSpawn()`は常に実行されることが保証されます。Animator Controller・Trigger送信方法(`NetworkAnimator.SetTrigger()`)・Jump Physics・Character Selection/Online Assignmentロジックは無変更です。

**設計ルール(今後Characterを追加する場合も同様)**: NetworkBehaviour(特に`NetworkAnimator`)は、Character Visual切り替えでActive/Inactiveが変化するGameObject配下に置いてはならない。常にActiveなPlayer Root(またはそれに準ずる常時Activeな階層)に置き、対象のAnimator/Componentへは通常のSerialized Field経由でCross-GameObject参照する。

## 色違いCharacter(Material Override、Step 12.1.2b)

Character 3〜6(Blue/Purple/Red/Yellow Boy)は、**新しいVisual Root(GameObject)を追加せず**、GreenBoyの共有Model([責務分離](#責務分離)の`GreenBoyVisual`単体)のMaterialだけをCharacter選択に応じて差し替える方式で実装しています。GreenBoyの`Model`(Animator/Armature/SkinnedMeshRenderer)は**常に1つだけ**のまま — 前述の「NetworkAnimatorの配置」ルール(Player Rootに1つだけ・複製しない)と矛盾しないための設計です。

### 仕組み

- `CharacterDefinition.cs`に`visualMaterial`(`Material`型、Optional)フィールドを追加。未設定(None)ならDefault Materialが使われる
- `PlayerCharacterVisual.cs`に`greenBoyRenderer`(`GreenBoyVisual/Model/char1`の`SkinnedMeshRenderer`参照)と`greenBoyDefaultMaterial`(`GreenBoy_Material`)を追加。`ApplyVisual(int characterId)`は、VisualRootのActive切り替えに加えて、`greenBoyRenderer.sharedMaterial`を「そのCharacterの`VisualMaterial`、なければ`greenBoyDefaultMaterial`」に**毎回明示的に**設定する
- 「毎回明示的に」設定するのがポイントです。もし「`VisualMaterial`が設定されている時だけ上書きする」という条件にすると、一度Blue(Character 3)を選んだ後にGreen(Character 2/7〜20、`VisualMaterial`未設定)へ切り替えたとき、前回選択したBlueの色が残ってしまうバグになります。`ApplyVisual`は`_characterId`のNetworkVariable変更のたびに毎回全体を再解決するため、Onlineで他のCharacterから切り替わってきたRemote Clientでも正しく反映されます
- 既存の`_characterId`(NetworkVariable、Server-authoritative)による同期の仕組みにそのまま乗るため、**追加のNetworkVariable/RPCは不要**です

### 手動でCharacterを追加する手順(色違い版、Character 7〜20や将来の追加分に使えます)

新しいMeshy AI等のTexture/Materialを用意し、既存のGreenBoy Meshをそのまま使い回して色だけ変えるパターンの手順です。

1. **Materialを作る**: `Assets/_Project/Art/Characters/GreenBoy/Materials/`に新しい`.mat`を作成(既存の`GreenBoy_Material`を右クリック→`Duplicate`が確実 — Shader/Smoothness等の設定を丸ごと引き継げます)。新しいTextureを使う場合は`Assets/_Project/Art/Characters/GreenBoy/Textures/`に配置し、複製したMaterialの`Base Map`(Albedo)スロットへ差し替える
2. **CharacterDefinitionを編集**: `Assets/_Project/Data/Characters/Character_XX.asset`をProject WindowからClick(**新規作成は不要** — Character 7〜20は既に存在します)。Inspectorで`Display Name`を分かりやすい名前に、`Visual Material`スロットへ手順1で作ったMaterialをDrag & Drop。`Visual Key`は`GreenBoyVisual`のまま変更しない
3. **Play Modeで確認**: Character Selectで対象IDのCardを選び、色が正しく反映されること・Idle/Walk/Run/Jump Animationが問題なく動くこと(共有Animatorなので通常は無関係に動作するはず)を確認する

これだけで、`PlayerAnimationController`/`PlayerCharacterVisual`/`Player.prefab`側のC#・Prefab変更は一切不要です。

### 手動でCharacterを追加する手順(全く新しいMesh/Rigの場合、参考)

色違いではなく全く別のHumanoid Mesh(新しいFBX)を追加したい場合は、上記より一段階複雑になります。現状の`Player.prefab`は`NetworkAnimator`を1つだけ持ち、その`Animator`参照は`GreenBoyVisual/Model`の1つのAnimatorに固定されているため(Section 6「Exactly ONE NetworkAnimator per Player」)、そのままでは新しいMeshに別のAnimatorを追加しても同期されません。対応するには、`NetworkAnimator.Animator`参照を選択中のCharacterに応じて動的に切り替えるか、あるいは全CharacterでAvatar/Animator Controllerを共有できるようRigを揃えて`Animator`自体は1つのまま`avatar`だけ差し替える、といった設計変更が必要です。この対応はStep 12.1.2bの範囲外で、まだ実装していません。

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
    ? (PlayerCharacterSelectionStore.Instance != null ? PlayerCharacterSelectionStore.Instance.GetCharacterId(clientId) : 1)
    : GameSessionContext.SelectedCharacterId;

PlayerCharacterVisual characterVisual = spawned.GetComponent<PlayerCharacterVisual>();
characterVisual?.ServerSetCharacterId(characterId);
```

Single Playerは接続ClientがHost自身1人だけのため`GameSessionContext.SelectedCharacterId`(static, Local)を直接使用します。Onlineは`PlayerCharacterSelectionStore`(Step 12.1.1で追加、`Docs/OnlineSession.md`「11. Character Select」参照)から`clientId`単位で解決します。

**Step 12.1.1で修正した重要なバグ**: Step 12.1では、ここを`NetworkSessionState.Instance.GetSelectedCharacterId(clientId)`(MainMenu.unityにScene配置されたNetworkObject)から読んでいましたが、`GameplayPlayerSpawner`がPlayerをSpawnするのはGameplay Sceneのロードが完了した**後**であり、その時点で`NetworkSessionState`は既に破棄されて`Instance == null`でした。結果、Online接続時は常にFallbackの`1`(Capsule)が使われ、全PlayerがCapsuleになる不具合が発生していました。詳細は`Docs/OnlineSession.md`を参照してください。

## PlayerAnimationController側の対応

Character 1(Capsule)にはGreenBoy Animatorを表示する必要がないため(Step 12.1 Section 13)、`PlayerAnimationController.Update()`の先頭に1行のGuardを追加しました。

```csharp
if (!IsOwner || animator == null || !animator.gameObject.activeInHierarchy) return;
```

Character固有の分岐は一切なく、「参照しているAnimatorが現在Activeかどうか」だけを見ています。GreenBoy Animator Controller自体はStep 12から無変更です。

Step 12.1.2で`NetworkAnimator`をPlayer Rootへ移動したことに伴い、`PlayerAnimationController.Awake()`内の参照解決も`animator.GetComponent<NetworkAnimator>()`(Animator側の兄弟を探す)から`GetComponent<NetworkAnimator>()`(自分自身と同じPlayer Root上を探す)に変更しました。`animator`(GreenBoyのAnimator)というSerialized Field自体は変更していません。

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

`NetworkSessionState`(`OnlineSession.md`参照)がUI/RPCの入口として`RequestSelectCharacterRpc(int characterId)`を受け取ります。各Clientは**自分自身の**選択だけをServerへ送信でき(Host-onlyではない、Section 16)、Server側でCatalog存在チェックのみ行います(Prefab参照等は一切送受信しません、Section 17)。実際の`ClientId → CharacterId`データは、Step 12.1.1で`PlayerCharacterSelectionStore`(Persistent、`Docs/Networking.md`参照)へ移動しました - `NetworkSessionState`自身はMainMenu Sceneにのみ存在するため、そこに保持していたデータはGameplay Spawn時には既に消えていました(Step 12.1のBug、下記参照)。詳細は`OnlineSession.md`「11. Character Select」を参照してください。

---

# Character Select Portrait UI(Step 13.2、Layout刷新はStep 13.3)

Character Select Cardの見た目だけを、「Number/Nameのみ」から「Portrait画像 + Number/Name」へ変更しました。**Character Selection Logic(ID解決・Highlight・Online同期・All-player Gate等)には一切変更がなく、Networkで送受信されるのは引き続き`CharacterId`のみです。**

## Card Hierarchy(`CharacterCardTemplate`、`MainMenu.unity`)

Step 13.3で「左Image / 右Text」Layoutへ刷新しました(下記「Step 13.3」章参照)。現在の構造:

```
CharacterCardTemplate (280×128, Image[背景/Highlight色] + Button)
├─ Portrait (Image, Step 13.2で追加)
│   Anchor: 左端中央 / Size 100×100 / Preserve Aspect ON / Raycast Target OFF
└─ Text (TextMeshProUGUI, 既存)
    Anchor: 右側Stretch(Portraitの右、Card全高) / Font Size 14 / Raycast Target OFF
```

`Portrait`は表示専用のImageで、Card全体のClickは引き続きRoot(`CharacterCardTemplate`)の`Button`が担うため(Raycast TargetをOffにしているPortrait/TextはClickを妨げません)、既存のButton Click Handler(`CharacterSelectController.OnCharacterCardClicked`)は無変更です。

Grid(`CharacterGrid`、`GridLayoutGroup`)は`cellSize = (280, 128)`、`spacing = (16, 16)`で5列×4行=20 Cardsを維持しています。詳細な余白計算・Step 13.2時点で「余白が非常に少ない」としていた分析が実は`CharacterGrid`の`localScale`異常値(1.858664倍)による誤測定だったことの経緯は、下記「Step 13.3」章を参照してください。

## `CharacterDefinition.Portrait`(UI表示専用Data)

```csharp
[SerializeField] private Sprite portrait;
public Sprite Portrait => portrait;
```

`visualMaterial`(Step 12.1.2b)と同じ設計思想のOptional Fieldです。**Networkには一切使用しません** - 各Clientは`CharacterId`だけをNetwork越しに受け取り、Local側の`CharacterCatalog`(全Client共通のAsset)から`CharacterDefinition.Portrait`を解決するだけです(Sprite/Texture/GUID等をNetwork送信することは一切ありません)。

## Fallback(Section 13)

```csharp
// CharacterSelectController.Populate()
portraitImage.sprite = character.Portrait != null ? character.Portrait : placeholderPortrait;
```

`Portrait`が未設定(None)のCharacterは、`CharacterSelectController`にSerialize済みの`placeholderPortrait`(共有Sprite、20 Character全員で同一Asset参照 - Duplicateしていません)へ自動的にFallbackします。新しいCharacterを追加してPortrait設定を忘れても、UIが壊れる(白い四角・NullReference)ことはありません。

## Placeholder Sprite

`Assets/_Project/Art/UI/CharacterPortraits/CharacterPortrait_Placeholder.png`。Production Artではなく、C#で手続き的に生成したSimpleな人型シルエットPlaceholderです(外部Asset/Packageは一切使用していません)。

## 今後の手動差し替え手順(Code変更不要)

1. `Assets/_Project/Data/Characters/Character_XX.asset`をProject WindowからClick
2. Inspectorの`Portrait`スロットへ、用意した画像(`Texture Type = Sprite`でImport済みのもの)をDrag & Drop
3. Play Modeで確認 - **そのCharacterのCardだけ**画像が切り替わり、他のCardはPlaceholderのまま

`CharacterSelectController`・Prefab・Network関連のCode変更は一切不要です。Sprite未設定のCharacterは自動的にPlaceholderのままなので、20体すべてに同時にPortraitを用意する必要もありません。

---

# Selection Card Visual Polish(Step 13.3)

## 発見した既存Bug: `CharacterGrid`/`StageGrid`の`localScale`異常値

Character Cardを大きくする作業中、`CharacterGrid`(および同じ構造の`StageGrid`)の`RectTransform.localScale`が`(1.858664, 1.858664, 1.858664)`という、Scale=1であるべきUI Layout Containerとしては明らかに異常な値になっていることを発見しました。`git log`での調査の結果、この値はStep 13.2どころかStep 12.1.2b(`48d685e`)時点で既に存在していた、**Step 13以前からの既存Bug**であることを確認しています(Step 13.2/13.3で新たに作り込んだものではありません)。

この`localScale`は、`GetWorldCorners()`等でCard/Grid位置を実測する際に、実際のSize/Positionを約1.86倍に見せかけて計算を狂わせます。Step 13.2で「Header/Bottom UIとGridの間の余白が非常に少ない」と判断していたのは、**この異常なScaleによって実際より大きく描画されていたGridが、周囲のUI要素と誤って近接して見えていたため**でした。Step 13.3で`localScale`を`(1, 1, 1)`へ修正した結果、実際の余白は当初の想定よりずっと大きいことが判明し、Character Card自体を大幅に拡大することができました。

**教訓**: UI RectTransformの`localScale`が`(1,1,1)`以外になっている場合、GridLayoutGroup等のContainerとしては基本的に誤りです。将来同様の症状(要素同士が「近すぎる」「小さく見える」)に遭遇した場合は、まず`localScale`を疑ってください。

## Character Card Layout(左Image / 右Text)

```
CharacterCardTemplate (280×128)
├─ Portrait: 左端中央Anchor、100×100、Padding 14px
└─ Text: 右側Stretch(左Offset 130px = Portrait分を避ける、右Offset -14px)、Font Size 14
```

`CharacterGrid`: `cellSize = (280, 128)`、`spacing = (16, 16)`、`anchoredPosition = (0, 33)`。5列×4行=20 Cardsのまま、Header・Online専用UI(SelectionCountText等)・Back/Stage Select Buttonのいずれとも重ならないことを実測・Screenshotの両方で確認済みです(Online専用UIとの余白の方がSingle Player側より狭く、実質的なBinding Constraintでした)。

## Client向けPortraitが表示されないBugについて(Section 0-A)

開発者からOnline Host+Clientでの実機Screenshotにより、「Host側はPortrait付きUIが表示されるが、Client側(MPPM Virtual Player)は旧UI(Number/Nameのみ)のまま」という報告がありました。調査の結果、以下を確認しています。

- `CharacterSelectController.Populate()`にHost/Client(`IsServer`/`IsClient`等)で分岐するコードは一切なく、CardのInstantiate/Portrait解決ロジックはNetwork Roleに関わらず完全に同一です(Code Review確認済み)
- Portrait解決は`CharacterId → Local CharacterCatalog → CharacterDefinition.Portrait`という完全にLocalな処理で、Sprite自体をNetwork送信する経路はコード上どこにも存在しません
- `git diff`で確認した限り、MainMenu.unity/CharacterDefinition.cs/CharacterSelectController.csのいずれも、Host/Clientで挙動が分かれるような変更は含まれていません

一方、MPPM(Multiplayer Play Mode)のVirtual Playerは、`Library/VP/<個別ID>/`配下にMain Editorとは別の状態ディレクトリを持つ、Main Editorとは別プロセスのEditorインスタンスです。CharacterCardTemplateへPortrait子要素を追加した編集はMain Editor(Host)側でScene保存する形で行っており、その保存後にClient側のPlay Modeセッションが一度もScene再読み込みを経ていない場合、Client側は編集前の(Portraitを持たない)CharacterCardTemplateをまだ参照し続けている可能性があります。

**Root Causeとして最も可能性が高いと判断している仮説**: Client側のMPPM Virtual PlayerウィンドウがStep 13.2のScene編集より前からPlay Modeに入ったままで、一度もPlay Modeの完全な再起動(Stop→Play)を経ていない。Unityの一般的な仕様として、Script再Compileに伴うDomain Reloadは「既にPlay Mode中のScene Object階層」を保持したまま行われ、Sceneをdiskから再読み込みするものではないため、Client側であってもPlay Mode自体を再起動しない限り、新しいCharacterCardTemplateの内容(Portrait子要素)を拾わない可能性があります。

**この時点でCode変更は行っていません** - 上記の通りCode Review・Diff確認では実際にHost/Clientの分岐を一切見つけられておらず、開発者の明示的な指示(Section 4: Sprite RPC等のNetwork同期を追加しない)とも一致する形で、推測だけでのCode変更を避けています。

**次回オンライン確認時の確認手順**: Player 2(Client)のWindowでPlay Modeを完全に一度Stop → 再度Play → Room再Joinしてみて、Portraitが表示されるようになるか確認してください。表示されるようになれば上記仮説が確定し、以後Scene編集後は開発者ご自身でもClient側のPlay Mode再起動を習慣にすることで再発を防げます。もし再起動後も表示されない場合は、実際のCode Bugの可能性があるため、その際はLive Client Accessと共に追加調査が必要です。
