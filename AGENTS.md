# 開発時の指示

- ユーザーとのやり取り、コメント、README、PR、コミットメッセージは原則として日本語を使用する。
- Dalamud本体は.NET 10 / x64、VOICEROID2操作ヘルパーは.NET Framework 4.8.1 / x86として維持する。
- VOICEROID2操作や外部プロセスの応答待ちはゲームの描画スレッドで実行しない。
- テストでは起動中のFFXIV、VOICEROID2、既存Proxyを操作・終了しない。通信テストには専用のTestBridgeを使う。
- 検証と配布物の作成は `pwsh -File scripts/build.ps1` で行う。
