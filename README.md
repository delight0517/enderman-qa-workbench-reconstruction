# Enderman QA Workbench — reconstructed source preview

> **Recovery preview. This is not the original source release and is not an official build.** The original project source could not be recovered. These C# files were reconstructed from the installed Enderman QA Workbench 0.3.184 binary using ILSpy. Matching behavior and source equivalence have not been established. This repository and preview installer are published to make recovery work reviewable; do not use them for production QA or Jira submissions.

## Windows preview

Download [preview 2 for Windows x64](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases/download/v0.1.0-preview.2/EndermanQaWorkbench-Reconstructed-0.3.184-preview.2-win-x64.zip) or review the [release notes](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases/tag/v0.1.0-preview.2). SHA-256:

```text
875723C657A3AB50132B3A9928F9E6A4D1E66AD2BF51FB860BFD71DDA401C47D
```

It is a self-contained .NET 10 Windows x64 build. The Microsoft Edge WebView2 Evergreen Runtime must already be installed for embedded web features; get it from [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). The preview is unsigned and may trigger Windows SmartScreen. Verify the hash before extracting.

The preview writes to `%LOCALAPPDATA%\EndermanQaWorkbench-Candidate-v27`, separate from the installed Workbench and its user data. Do not copy preview data into the installed application. Startup diagnostics are written under that preview data root. The published package reached WPF content-rendered startup and a clean close in an isolated run; it has not received a human visual review or game-play qualification.

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
- No human visual review, complete isolated Minecraft gameplay run, complete 100-case QA execution, or Jira workflow has been verified.
- The original source, build pipeline, signing identity, and official release provenance remain unresolved.
- The Enderman 100-case QA dataset and evidence are separate user work products and are not included in this repository or release.

## Licensing and provenance

Repository source: MIT. See [LICENSE](LICENSE). This does not imply ownership or licensing of Minecraft, Microsoft, Mojang, Atlassian, or other third-party marks and services. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
