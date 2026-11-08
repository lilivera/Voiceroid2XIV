# 配布の更新手順

Dalamudの登録URLは次のJSONです。GitHub ReleasesにZIPを公開した後、このJSONを更新します。

```text
https://raw.githubusercontent.com/lilivera/Voiceroid2XIV/main/pluginmaster.json
```

1. `src/Voiceroid2XIV/Voiceroid2XIV.csproj` の `Version` を上げ、READMEとリリースノートを更新する。
2. `pwsh -File scripts/build.ps1` を実行し、ビルドとテストが成功したことを確認する。
3. ソースをコミットして `main` へpushする。
4. そのコミットを対象にGitHub Releaseを作成し、`artifacts/Voiceroid2XIV-<AssemblyVersion>.zip` を添付する。タグは `v<AssemblyVersion>`（例: `v0.1.4.0`）とする。
5. 公開後、未認証のHTTPアクセスでZIPを取得できることを確認する。
6. `artifacts/pluginmaster.json` をリポジトリ直下の `pluginmaster.json` へコピーし、コミットして `main` へpushする。
7. 登録URLからJSONを取得し、インストール先・更新先・アイコンURLへアクセスできることを確認する。ZIP内のバージョンとJSONの `AssemblyVersion` が一致し、`bridge` と `images/icon.png` が含まれていることも確認する。

同じタグのZIPを差し替えず、変更時はバージョンとタグを上げます。JSONを先に更新すると、存在しないZIPがDalamudの一覧に出るため、必ずReleaseを先に公開してください。

`bin`、`obj`、`artifacts`、`.tools` はGit管理から除外しています。配布ZIPにVOICEROID2本体や音声ライブラリは含めません。

通常のリポジトリ配布では一覧のアイコンを `IconUrl` から取得します。開発用のローカル読み込みではDLLの隣にある `images/icon.png` を使用します。
