# Enderman QA Workbench — reconstructed source preview

> **Recovery preview. This is not the original source release and is not an official build.** The original project source could not be recovered. These C# files were reconstructed from the installed Enderman QA Workbench 0.3.184 binary using ILSpy. Matching behavior and source equivalence have not been established. This repository and preview installer are published to make recovery work reviewable; do not use them for production QA or Jira submissions.

See the [Enderman 100-case QA portfolio](PORTFOLIO.md) for the project brief and an evidence-led, privacy-reviewed case summary. The public summary excludes raw journals, local paths, account details and client screenshots; the dataset remains explicitly incomplete.

## Windows preview

Download [preview 6 for Windows x64](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases/download/v0.1.0-preview.6/EndermanQaWorkbench-Reconstructed-0.3.184-preview.6-win-x64.zip) or review the [release notes](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases/tag/v0.1.0-preview.6). SHA-256:

```text
C6B20D03E8671A55B29600F2C04C2714D33FBA58576C8B1654E763983B96D3E2
```

It is a self-contained .NET 10 Windows x64 build. The Microsoft Edge WebView2 Evergreen Runtime must already be installed for embedded web features; get it from [Microsoft](https://developer.microsoft.com/microsoft-edge/webview2/). The preview is unsigned and may trigger Windows SmartScreen. Verify the hash before extracting.

The preview writes to `%LOCALAPPDATA%\EndermanQaWorkbench-Candidate-v27`, separate from the installed Workbench and its user data. Do not copy preview data into the installed application. Startup diagnostics are written under that preview data root. Preview 6 reached `main-window-content-rendered` on a task-owned private Win32 desktop and exited normally; no physical display was used. The embedded Only Chat control connected to the verified local runtime using a fresh isolated WebView2 profile and rendered the loopback page; no page text was inspected, and no login or message was sent. A synthetic external URL was canceled by the QA navigation guard. Set `ENDERMAN_QA_WEBVIEW2_USER_DATA_ROOT` to isolate the WebView2 profile and `ENDERMAN_QA_BLOCK_EXTERNAL_NAVIGATION=1` to cancel external links and suppress native connection error dialogs during hidden QA. Without the QA guard, existing app behavior is preserved. Installed-app upgrade, gameplay qualification and complete QA are not claimed.

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
- Private-desktop startup and Only Chat loopback rendering were verified for preview 6. Screenshot-based visual review, login/chat-send behavior, complete isolated Minecraft gameplay, complete 100-case QA execution and Jira workflow have not been verified.
- The original source, build pipeline, signing identity, and official release provenance remain unresolved.
- The Enderman 100-case QA dataset and evidence are separate user work products and are not included in this repository or release.

## Licensing and provenance

Repository source: MIT. See [LICENSE](LICENSE). This does not imply ownership or licensing of Minecraft, Microsoft, Mojang, Atlassian, or other third-party marks and services. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
