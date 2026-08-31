# MultiplayerTesting(2人プレイのテスト手順)

このドキュメントは、Unity Editorを開いてから実際に2人分のキャラクターを自分で動かして確認するまでの手順です。Multiplayer Play Mode(MPPM)というUnity Editorの機能を使い、1台のPC上で「Host役のEditor」と「Client役の別ウィンドウ」を同時に動かします。

---

## 1. 事前状態

このプロジェクトにはすでに以下が設定済みです(自分で毎回設定し直す必要はありません)。

- `com.unity.multiplayer.playmode` パッケージ導入済み
- Main Editor(自分が普段使っているEditor)に Tag: **`Host`**
- Player 2(Virtual Player)に Tag: **`Client`**
- `NetworkBootstrap` がこれらのTagを読み取り、Play開始時に自動的に `StartHost()` / `StartClient()` を呼ぶ

つまり、あなたがやることは「Player 2を有効にしてからPlayを押すだけ」です。

---

## 2. Multiplayer Play Modeの設定を開く

Unity Editor上部のツールバーにある **▶(Play)ボタンのすぐ右側**に、小さなドロップダウン矢印があります。これをクリックしてください。

> Unity 6ではこの機能は独立したメニュー項目ではなく、Playボタン横のドロップダウンからのみアクセスできます。「Window」メニューの中には出てきません。

開いたパネルに、以下のような一覧が表示されます。

- **Main Editor**(Player 1): Multiplayer Role / Tags の設定欄がある
- **Virtual Players**(Player 2, 3, 4): それぞれ有効/無効のトグルと、同じ設定欄がある

すでにMain Editorには `Host`、Player 2には `Client` というTagが設定されているはずです。表示されていない場合は、それぞれの「Tags」のドロップダウンから選び直してください。

---

## 3. Player 1 / Player 2の役割

| | 実体 | 役割 | 操作するウィンドウ |
|---|---|---|---|
| **Player 1** | 今使っているUnity Editor本体 | Host(Server+Client兼任) | いつものGame View |
| **Player 2** | 新しく開く別のウィンドウ(軽量なEditorインスタンス) | Client | 新しく開く「Player 2 [Client]」という名前のウィンドウ |

---

## 4. Player 2を有効化する

Player 2の行にあるトグル(チェックボックス)をONにします。

- ステータス表示が `Launching → Launched` に変わるまで待ちます。
- 初回や、しばらく使っていなかった後の起動は **20〜30秒程度** かかることがあります(専用の軽量プロジェクトを裏側で準備しているためです)。気長に待ってください。

---

## 5. 通常どおりPlayを押す

Main Editorの ▶ ボタンを押してPlay Modeに入ります。

- Main EditorはTag `Host` を読んで自動的にHostを起動します。
- Player 2もMain Editorと連動してPlay状態になり、Tag `Client` を読んで自動的にHostへ接続しにいきます。
- 数秒待つと、両方のキャラクターがSpawnされます。

---

## 6. 操作確認方法

- **Main Editor側**: いつものGame Viewで、WASD(移動)・Shift(ダッシュ)・Space(ジャンプ)・マウス(視点)を使い、Host側のキャラクターを操作します。
- **Player 2側**: 別ウィンドウとして開いている「Player 2 [Client]」を**マウスでクリックしてフォーカス(選択状態)にしてから**、同じ操作をします。

⚠️ **注意点**: Player 2のウィンドウはフォーカスが外れていると、画面の描画や中身の処理が一時的に止まって見えることがあります。「動いていない」「反応しない」と感じたら、まずそのウィンドウをクリックしてアクティブにしてください。

---

## 7. 正常時に何が見えるべきか

以下がすべて確認できれば、Step 2までの実装は正しく動作しています。

1. **Main EditorのGame View**に、Capsuleが **2体** 表示される(自分の分＋Player 2の分)。
2. **Player 2のウィンドウ**にも、同じように2体のCapsuleが表示される。
3. Main Editor側でWASDを押すと、**片方のCapsuleだけ**が動く(もう片方は動かない)。
4. Player 2のウィンドウ側でWASDを押すと、**先ほどとは逆のCapsuleだけ**が動く。
5. どちらの画面でも、それぞれのCapsuleの後ろに三人称カメラが追従している(マウスでカメラを回すと、自分側の画面のカメラだけが動く)。
6. どちらのウィンドウのConsoleにも、赤色のError表示が出ていない。

もし「両方の画面で同じキャラクターが動いてしまう」「片方の画面にキャラクターが1体しか見えない」「Consoleに赤いエラーが出る」といった状態になっている場合は、Step 2までの実装のどこかに問題がある可能性があるため、状況を教えてください。

---

## 8. テストを終了する

Main EditorのPlayボタン(■で停止)を押すだけで、Host/Client両方が停止します。Player 2のトグルは基本的にONのままで構いません(次回のテストでもすぐ使えるよう有効にしておく想定です)。ONのままEditorを閉じても問題ありません。
