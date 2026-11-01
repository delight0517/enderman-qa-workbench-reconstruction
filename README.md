# Enderman QA Workbench — reconstructed source preview

> **Recovery preview. This is not the original source release and is not an official build.** The original project source could not be recovered. These C# files were reconstructed from the installed Enderman QA Workbench 0.3.184 binary using ILSpy. Matching behavior and source equivalence have not been established. This repository and preview installer are published to make recovery work reviewable; do not use them for production QA or Jira submissions.

## Windows preview

Download the latest `win-x64` preview from [GitHub Releases](https://github.com/delight0517/enderman-qa-workbench-reconstruction/releases). It is a self-contained .NET 10 Windows x64 build. The Microsoft Edge WebView2 Runtime must already be installed for embedded web features; see [Microsoft's installer](https://developer.microsoft.com/microsoft-edge/webview2/). The current source compiles from a clean clone. A Windows preview ZIP is being prepared; no validated runnable binary is published yet.

The preview deliberately writes to `%LOCALAPPDATA%\EndermanQaWorkbench-Candidate-v27`, separate from the installed Workbench and its user data. Do not copy the preview's data into the installed application. The preview is unsigned and may trigger Windows SmartScreen.

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
- The preview has not passed human UI review or a complete isolated game-play QA run.
- The original source, build pipeline, signing identity, and official release provenance remain unresolved.
- This repository does not include the Enderman 100-case QA dataset or evidence; those remain separate user work products.

## Licensing and provenance

Repository source: MIT. See [LICENSE](LICENSE). This does not imply ownership or licensing of Minecraft, Microsoft, Mojang, Atlassian, or other third-party marks and services. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
