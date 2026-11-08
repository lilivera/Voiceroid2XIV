# Voiceroid2XIV

VOICEROID2をFFXIVのDalamudから操作するWindows用プラグインです。[SimpleVoiceroid2Proxy](https://github.com/SlashNephy/SimpleVoiceroid2Proxy)のHTTP読み上げAPIと制御コマンドを基に、ゲーム内の設定画面・コマンド・チャット読み上げを追加しています。

## 動作環境

- Windows x64、Dalamud API 15（プラグインは.NET 10）。
- インストール済みのVOICEROID2と、利用する音声ライブラリ。
- .NET Framework 4.8.1。VOICEROID2操作用ヘルパーはx86で動作します。
- ビルドする場合は.NET 10 SDKとDalamud開発用DLLが必要です。SDKは通常 `%APPDATA%\XIVLauncher\addon\Hooks\dev` を参照します。別の場所なら `DALAMUD_HOME` を指定します。

VOICEROID2の音声合成エンジン自体は同梱しません。VOICEROID+、A.I.VOICE、VOICEVOX、Linux/Wineは対象外です。

## 導入・最初の接続

1. Dalamudの設定（`/xlsettings`）→「試験的機能」→「カスタムプラグインリポジトリ」に、次のURLを追加して有効化し、保存します。

   ```text
   https://raw.githubusercontent.com/lilivera/Voiceroid2XIV/main/pluginmaster.json
   ```

2. `/xlplugins` で一覧を開き、必要なら「新しいプラグインを確認」を押します。`Voiceroid2XIV` を検索してインストールします。
3. 既存のSimpleVoiceroid2Proxyを終了します。VOICEROID2は起動したままで構いません。
4. `/v2xiv` で設定画面を開き、「接続」を押します。以前のProxyを管理者権限で使っていた場合は、先に「ヘルパーを管理者権限で起動」を有効にし、接続時のWindowsの確認画面で許可してください。実行ファイルの欄は空欄で自動検出します。見つからない場合は `VoiceroidEditor.exe` を指定します。
5. 「読み上げ」で発声を確認します。必要に応じて「プラグイン読込時に接続」「選択したチャットを読み上げる」を有効にします。
6. 外部ツールと連携する場合は「HTTP APIを有効にする」を選び、「再接続・設定を適用」を押します。

自動接続、チャット読み上げ、HTTP APIは初期状態では無効です。設定はDalamudのプラグイン設定に保存されます。接続失敗時は設定画面に理由を表示します。自動で無限に再接続せず、利用者が「接続」で再試行します。

管理者権限での起動はヘルパーだけに適用します。FFXIVを管理者権限で起動し直す必要はありません。UACをキャンセルした場合は設定画面に理由を表示し、再試行できます。自動接続と併用した場合も、接続のたびにWindowsの確認が必要です。UACで別ユーザーへ切り替える方式には対応していません。

開発版から切り替える場合は、開発版のVoiceroid2XIVを無効化し、Dalamud設定のDev Plugin Locationsにある登録を無効にしてから配布版をインストールしてください。既存のプラグイン設定ファイルは削除せずに引き継げます。

ソースから試す場合、ビルド後のDLLは `src/Voiceroid2XIV/bin/Release/Voiceroid2XIV.dll` です。このパスをDev Plugin Locationsに登録し、開発者向け一覧から読み込めます。手動配置には[ReleasesのZIP](https://github.com/lilivera/Voiceroid2XIV/releases)を専用フォルダーへ展開し、依存DLLと `bridge`、`images` フォルダーをまとめて配置してください。

カスタムリポジトリからのインストール時に「プラグインのインストールに失敗しました」と表示され、ログにZIPダウンロードの504エラーがある場合は、一覧の「新しいプラグインを確認」で配布情報を更新してから再試行してください。配布先をGitHub Releasesから直接配信のURLへ切り替えています。登録URLは変わりません。

## ゲーム内コマンド

| コマンド | 動作 |
| --- | --- |
| `/v2xiv` | 設定画面を開く |
| `/v2xiv connect` | 接続する。接続中の場合は設定を適用して再接続する |
| `/v2xiv disconnect` | ヘルパーとHTTPを終了する |
| `/v2xiv say こんにちは` | 文章を読み上げる |
| `/v2xiv say 結月ゆかり＞こんにちは` | VOICEROID2側の話者指定記号を利用する |
| `/v2xiv clear` | 待機中の文章を消去する |
| `/v2xiv pause` | 次の文章からキューの処理を一時停止する |
| `/v2xiv resume` | キューの処理を再開する |
| `/v2xiv stop` | 待機中の文章を消去し、現在の発声を止める |

話者名と区切り記号はVOICEROID2側の設定に従います。インストールされていない話者には切り替わりません。

## チャット読み上げ

パーティ、クロスワールドパーティ、アライアンス、FC、Tell、Say、Shout、Yell、各LS/CWLS、Echo、NPCの発言を個別に選べます。発言者名の読み上げも設定できます。初期状態ではすべてのチャンネルがOFFです。保存済みのチャンネル設定は引き継ぎます。

チャットの本文はDalamudの `SeString.TextValue` から取得します。チャット内の `<clear>` などは制御命令として実行しません。FFXIVの代名詞や独自読み辞書の展開、音量・速度の変更には対応していません。音量・速度・出力先はVOICEROID2側で調整してください。

## HTTP API

既定の接続先は `http://127.0.0.1:4532/talk` です。`localhost` も使用できます。プラグイン接続中かつHTTP設定が有効な間だけ動作します。

### GET /talk

```powershell
$text = [Uri]::EscapeDataString('結月ゆかり＞テストです。')
Invoke-RestMethod "http://127.0.0.1:4532/talk?text=$text"
```

### POST /talk（JSON、推奨）

```powershell
$body = @{ text = '結月ゆかり＞テストです。' } | ConvertTo-Json
Invoke-RestMethod 'http://127.0.0.1:4532/talk' -Method Post `
    -ContentType 'application/json; charset=utf-8' `
    -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

`application/x-www-form-urlencoded` の `text` フィールドにも対応します。成功応答はHTTP 200と `{"success":true,"message":"受け付けました。","text":"..."}` です。これはキューへの受け付けを表し、音声出力完了の通知ではありません。

| HTTPステータス | 意味 |
| --- | --- |
| 400 | textがない、空欄、JSONが不正 |
| 403 | ブラウザーのクロスオリジン要求、ローカル以外の接続 |
| 404 / 405 | パス、HTTPメソッドが未対応 |
| 413 | 文章・リクエストの上限を超過 |
| 415 | JSON・フォーム以外のContent-Type |
| 503 | 未接続、キュー満杯、ヘルパー異常、タイムアウト |

1件4096 UTF-16コード単位、待機128件、POST本文32 KiBを上限とします。GETはクライアント側のURL長制限にも従います。ローカル以外には公開しません。参照元のLAN公開と任意OriginへのCORS許可は実装していません。ACTなどのデスクトップアプリから利用できます。

### 互換制御文字列

HTTPの `text`、`/v2xiv say`、テスト入力では、文章中の次の文字列を認識します。認識した制御文字列は発声本文から除去します。

| 文字列 | 動作 |
| --- | --- |
| `<clear>` | 待機キューを消去。発声中の文章は継続 |
| `<pause>` / `<resume>` | 次の文章の開始を一時停止 / 再開 |
| `<interrupt_enable>` / `<interrupt_disable>` | 発声中でも次の文章を開始するかを切り替える |
| `<stop>` | 本プラグインの追加機能。発声を停止しキューも消去 |

参照元の実装に合わせ、`pause` は発声位置を保存する音声の一時停止ではありません。`stop` した文章の途中再開もできません。割り込みは初期状態で有効です。文章を最後まで順に読ませる場合は無効にしてください。割り込み有効時も、未処理の文章を自動で破棄せず、受信順に200 ms以上の間隔で再生開始します。

## 構成

```text
Dalamud (.NET 10 / x64)
  └─ Voiceroid2XIV
       ├─ 設定画面 / チャット / /v2xiv
       ├─ ローカルHTTP API (EmbedIO)
       └─ 同一利用者の名前付きパイプによる非同期JSON通信
            └─ Voiceroid2XIV.Bridge (.NET Framework 4.8.1 / x86)
                 ├─ 読み上げキュー
                 └─ Friendly → VOICEROID2
```

32bitの.NET Framework用ライブラリをゲーム内へ読み込まないため、専用ヘルパーを同梱します。別途SimpleVoiceroid2Proxyを用意する必要はありません。ヘルパーのコンソール画面は出さず、VOICEROID2の操作にキーボード入力や前面化を使いません。接続先は起動中の `VoiceroidEditor` の先頭1プロセスです。起動していなければ設定パス、レジストリ、標準インストール先の順に検出します。

接続ライブラリは、動作実績のあるSimpleVoiceroid2Proxyの配布DLLに合わせてFriendly 2.6.1、Friendly.Windows 2.17.0、Grasp 2.14.2、WPFStandardControls 1.59.1を使用します。FriendlyのDLLは接続先プロセスに残るため、異なるバージョンを使うクライアントへの切り替えで初期化に失敗する場合があります。依存関係を更新する際は、既存ProxyからVOICEROID2を再起動せずに切り替える動作も確認してください。

切断時は自分で起動したヘルパーだけを終了します。VOICEROID2本体と現在の発声はそのまま残します。発声を止める場合は先に「停止」を押してください。親との通信切断でもヘルパーは終了し、応答不能時はプラグイン側がヘルパーを終了します。

## ビルドとテスト

```powershell
pwsh -File scripts/build.ps1
```

固定済みの依存関係を復元し、ソリューションのReleaseビルド、テスト、配布ZIPの作成を行います。出力先は `artifacts/Voiceroid2XIV-0.1.4.0.zip` です。依存ライブラリとヘルパー、アイコン、ライセンス文書を含みます。合わせて `artifacts/pluginmaster.json` を生成します。公開手順は [docs/publishing.md](docs/publishing.md) を参照してください。

Dalamudの開発用プラグイン一覧のアイコンは、出力先の `images/icon.png` から読み込まれます。同梱画像は512×512の透過PNGです。生成元と制作情報は `assets/icon/` に保存しています。

既に一覧を開いていて画像が更新されない場合は、Dalamudの開発メニューにある `Plugins` → `Clear cached images/icons` で画像キャッシュを消去してください。

ゲームを起動せずテストだけ行う場合:

```powershell
dotnet test tests/Voiceroid2XIV.Tests/Voiceroid2XIV.Tests.csproj -c Release
```

テストはVOICEROID2に接続しません。キューの順序、割り込み、消去、一時停止、制限、HTTPの実ソケット通信、ポート競合、JSON通信、ヘルパーの異常終了・キャンセルを検証します。

## 実機確認の範囲

開発時にDalamud API 15のDLLを使用したビルドと自動テストを確認しています。2026-11-07に開発版のFFXIVへのロード、VOICEROID2への接続・読み上げ、一覧アイコンの表示を実機確認しました。すべての製品バージョンでの動作や、HTTP・チャットを含む全項目の実機確認は未完了です。特にVOICEROID2の画面構造は製品バージョンに依存します。「先頭」ボタンはBindingで取得し、取得できない版では参照元と同じ論理ツリー位置にフォールバックします。

実機確認の手順は [docs/manual-test.md](docs/manual-test.md) にあります。

## 接続できない場合

- **既存Proxyが起動中**: SimpleVoiceroid2Proxyを終了してから再接続します。プラグイン側では既存プロセスを終了しません。
- **既存Proxyからの切り替え後に「対象プロセスの操作に失敗」**: 0.1.2以降では接続ライブラリを既存Proxyと揃えています。プラグインと `bridge` フォルダーをまとめて更新し、再接続してください。旧版のDLLが残っているだけなら、VOICEROID2の再起動は不要です。
- **対象プロセスの操作に失敗／アクセス拒否**: 以前のProxyを管理者権限で使っていた場合は「ヘルパーを管理者権限で起動」を有効にして接続してください。FFXIVは通常権限のままで構いません。HTTP用の管理者権限やURL予約は不要です。
- **編集画面が見つからない**: VOICEROID2の起動直後のダイアログ、製品認証、モーダル画面を閉じ、編集画面が操作できる状態にして再接続します。
- **HTTPポート使用中**: 他のソフトの設定を確認するか、このプラグインのポートを変更して再接続します。HTTPの開始失敗時もVOICEROID2との接続が成功していれば手動読み上げは利用できます。
- **bridgeフォルダーがない**: DLL単体ではなくZIP全体を展開してください。

## 参照元・ライセンス

参照実装と依存ライブラリの情報は [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)、同梱ライブラリのライセンス原文は `licenses/` にあります。

本プロジェクトは [MITライセンス](LICENSE) で公開しています。
