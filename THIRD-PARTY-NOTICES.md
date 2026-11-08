# 外部ソフトウェアと参照元

VOICEROID2操作層は [SimpleVoiceroid2Proxy](https://github.com/SlashNephy/SimpleVoiceroid2Proxy) の操作方式を基にしています。同リポジトリのMITライセンスを以下に記載します。

```text
MIT License

Copyright (c) 2020 Nep

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

使用ライブラリのバージョンと推移的依存関係は各プロジェクトの `packages.lock.json` に記録します。

- [Codeer.Friendly](https://github.com/Codeer-Software/Friendly)、[Codeer.Friendly.Windows](https://github.com/Codeer-Software/Friendly.Windows)、[Codeer.Friendly.Windows.Grasp](https://github.com/Codeer-Software/Friendly.Windows.Grasp)：VOICEROID2への接続。
- [RM.Friendly.WPFStandardControls](https://github.com/Roommetro/Friendly.WPFStandardControls)：WPFコントロールの操作。
- [Codeer.TestAssistant.GeneratorToolKit](https://github.com/Codeer-Software/TestAssistant.GeneratorToolKit)：WPF操作ライブラリの推移的依存関係。
- [Newtonsoft.Json](https://github.com/JamesNK/Newtonsoft.Json)：ヘルパー間のJSON通信。
- [EmbedIO](https://github.com/unosquare/embedio)、[Swan.Lite](https://github.com/unosquare/swan)：ローカルHTTPサーバー。
- [Dalamud](https://github.com/goatcorp/Dalamud)、[Dalamud.NET.Sdk](https://github.com/goatcorp/Dalamud.NET.Sdk)：プラグインAPIとビルド。

VOICEROID2本体、音声ライブラリ、Dalamud本体はこの配布物に含みません。

配布DLLに対応するMIT / Apache-2.0のライセンス原文を `licenses/` に同梱しています。参照実装の操作層はヘルパーへの分離、接続の後始末、エディターを終了しない処理、エラー通知を追加して変更しています。
