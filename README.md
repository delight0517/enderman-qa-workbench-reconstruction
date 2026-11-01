# Enderman QA Workbench — reconstructed source preview

> **Recovery preview. This is not the original source release and is not an official build.** The original project source could not be recovered. These C# files were reconstructed from the installed Enderman QA Workbench 0.3.184 binary using ILSpy. Matching behavior and source equivalence have not been established. This repository and preview installer are published to make recovery work reviewable; do not use them for production QA or Jira submissions.

See the [Enderman 100-case QA portfolio](PORTFOLIO.md) for the project brief and an evidence-led, privacy-reviewed case summary. The public summary excludes raw journals, local paths, account details and client screenshots; the dataset remains explicitly incomplete.

## Windows preview

Download [preview 3 for Windows x64](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases/download/v0.1.0-preview.3/EndermanQaWorkbench-Reconstructed-0.3.184-preview.3-win-x64.zip) or review the [release notes](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases/tag/v0.1.0-preview.3). SHA-256:

```text
217BEEE51930BD4754E9735F1B0D354E1A70CFCB1B8ECF4036230CF40CA817D4
```

It is a self-contained .NET 10 Windows x64 build. The Microsoft Edge WebView2 Evergreen Runtime must already be installed for embedded web features; get it from [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). The preview is unsigned and may trigger Windows SmartScreen. Verify the hash before extracting.

The preview writes to `%LOCALAPPDATA%\EndermanQaWorkbench-Candidate-v27`, separate from the installed Workbench and its user data. Do not copy preview data into the installed application. Startup diagnostics are written under that preview data root. Preview 3 was smoke-checked from the packaged and extracted ZIP with `--construct-only` (MainWindow constructed; no window shown). The source UI was rendered separately with isolated fixtures. Full interactive package startup, installed-app upgrade, game-play qualification and complete QA are not claimed.

## Build from source

Requirements: Windows 10/11 x64, .NET 10 SDK, and the WebView2 Runtime for embedded web features.

```powershell
dotnet restore .\EndermanQa.Desktop.csproj
dotnet build .\EndermanQa.Desktop.csproj -c Release
dotnet publish .\EndermanQa.Desktop.csproj -c Release -r win-x64 --self-contained true
```

The self-contained package includes .NET 10 and Windows Desktop Runtime. It does not include the Edge WebView2 Runtime. Package licenses are summarized in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Known limitations

- Reconstruction introduces compiler warnings and may contain behavior differences from the installed app.
- No full interactive review of the packaged app window, complete isolated Minecraft gameplay run, complete 100-case QA execution, or Jira workflow has been verified.
- The original source, build pipeline, signing identity, and official release provenance remain unresolved.
- The Enderman 100-case QA dataset and evidence are separate user work products and are not included in this repository or release.

## Licensing and provenance

Repository source: MIT. See [LICENSE](LICENSE). This does not imply ownership or licensing of Minecraft, Microsoft, Mojang, Atlassian, or other third-party marks and services. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
