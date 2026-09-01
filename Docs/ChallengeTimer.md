# ChallengeTimer(Time Attack計測の仕組み)

Step 8Bで追加した、RUNNING開始からSUCCESSまでの経過時間を計測する仕組みについて説明します。対象読者はGameFlow.mdを一通り読んだ開発者を想定しています。

---

## 1. なぜGameFlowManagerに直接書かなかったか

`ChallengeTimer`は`GameFlowManager`とは別の`NetworkBehaviour`として、同じ`GameFlowManager` GameObject上に追加しています。GameFlowManager.cs自体は一切変更していません。

`ChallengeTimer`は`Update()`(Server限定)の中で`GameFlowManager.Instance.CurrentPhase`を毎フレーム読み、前回観測した値と比較して変化した瞬間だけ反応する、という**外側からのポーリング**方式を取っています。

```csharp
private void Update()
{
    if (!IsServer) return;
    if (GameFlowManager.Instance == null) return;

    GamePhase phase = GameFlowManager.Instance.CurrentPhase;
    if (phase == _lastObservedPhase) return;

    GamePhase previous = _lastObservedPhase;
    _lastObservedPhase = phase;

    if (phase == GamePhase.RUNNING) { /* Run Start */ }
    else if (phase == GamePhase.SUCCESS && previous == GamePhase.RUNNING) { /* Final Time確定 */ }
    else if (phase == GamePhase.BUILDING) { /* Reset */ }
}
```

この設計により、GameFlowManager側は「Timerというものが存在すること」を一切知る必要がありません。将来Timerの仕様を変えたくなっても、GameFlowManager.cs(Strict Clear判定・Failure・Ready Re-arm等、既に安定して動いている中核ロジック)には触れずに済みます。

---

## 2. Run Start時刻の同期(NGOのNetworkTime API)

TimerはHost/Server Authoritativeです。各Clientが自分の`Time.time`で独自に計測する構成は採用していません。

```csharp
private readonly NetworkVariable<double> _runStartServerTime = new NetworkVariable<double>(
    NotSet, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
```

RUNNINGへ遷移した瞬間、Server側で1回だけ

```csharp
_runStartServerTime.Value = NetworkManager.ServerTime.Time;
```

を実行します。`NetworkManager.ServerTime`(型`NetworkTime`、NGO 2.13.2)は、Host上では文字通りServerの現在時刻、Client上ではNGOが継続的に同期・補正している「Serverの時刻の推定値」です。Clientは自分自身の`NetworkManager.ServerTime.Time`を読むだけで、Server実時刻とほぼ一致した値を取得できます(NGOの標準的な時刻同期の仕組みをそのまま利用しているだけで、独自の時刻同期処理は実装していません)。

---

## 3. 毎Frame何も送らない設計

`RunStartServerTime`が同期された後、経過時間の表示自体はNetworkVariableを介さず、各Clientが完全にLocalで計算します。

```csharp
public double GetElapsedSeconds()
{
    if (HasFinalTime) return _finalElapsedSeconds.Value;
    if (_runStartServerTime.Value < 0.0 || NetworkManager == null) return 0.0;
    return NetworkManager.ServerTime.Time - _runStartServerTime.Value;
}
```

`GameplayHUD`はこの`GetElapsedSeconds()`を毎Frame呼びますが、これは完全にLocalな計算で、Network通信は一切発生しません。RUNNING中にNetwork越しにやり取りされるのは、Phase遷移(既存の`GamePhase` NetworkVariable、Step 6から変更なし)と、Run開始時の`RunStartServerTime`(1回)だけです。

---

## 4. Final Timeの確定(SUCCESS時に1回だけ)

```csharp
else if (phase == GamePhase.SUCCESS && previous == GamePhase.RUNNING)
{
    _finalElapsedSeconds.Value = NetworkManager.ServerTime.Time - _runStartServerTime.Value;
}
```

SUCCESSへ遷移した瞬間、Server側で1回だけ`FinalElapsedSeconds`(`NetworkVariable<double>`)を確定します。以降`GetElapsedSeconds()`は`HasFinalTime`(`_finalElapsedSeconds.Value >= 0`)を見て、常にこの確定値を返すため、SUCCESS後にタイマー表示が動き続けることはありません。Host/Client双方が同じ`NetworkVariable`の値を読むため、両者が表示するFinal Timeの数値は常に一致します。

---

## 5. FAILED / Resetの扱い

- **FAILED**: `ChallengeTimer`側には専用の分岐がありません。`_runStartServerTime`はRUNNING開始時の値のまま保持され、`_finalElapsedSeconds`も未確定(`NotSet`)のままです。`HasFinalTime`が`false`のままなので、そのAttemptがClear Timeとして扱われることはありません。表示自体は`GameplayHUD`側がPhase==FAILEDの間「FAILED」パネルへ切り替えるため、Timerの数値そのものは画面に出ません
- **FAILED → BUILDING(Failure Reset完了)**: `phase == GamePhase.BUILDING`の分岐で`_runStartServerTime`/`_finalElapsedSeconds`をともに`NotSet`(-1)へ戻します。次のREADY→RUNNINGでは`GetElapsedSeconds()`が確実に0から再スタートします

---

## 6. HUDとの関係

`GameplayHUD.cs`は`ChallengeTimer.Instance`を直接参照し、RUNNING中は`GetElapsedSeconds()`を、SUCCESS中は`FinalElapsedSeconds`(実質同じ値ですが、意図を明示するため別プロパティにしています)を`MM:SS.xx`形式へ整形して表示します。`ChallengeTimer`自体はUIについて一切関知しません(責務分離)。

```csharp
private static string FormatTime(double seconds)
{
    if (seconds < 0.0) seconds = 0.0;
    int minutes = (int)(seconds / 60.0);
    double remainder = seconds - minutes * 60.0;
    return $"{minutes:00}:{remainder:00.00}";
}
```

---

## 7. Single Player / Multiplayer両対応

`ChallengeTimer`はConnected Playerの人数を一切参照しません。Host 1人だけの状態でも、READY→RUNNING→SUCCESSが成立しさえすれば同じロジックで正しく動作します。Step 9で正式なSingle Player Modeが追加されても、Gameplay Scene側の仕組みとしてはこのまま変更なく使えることを想定しています。
