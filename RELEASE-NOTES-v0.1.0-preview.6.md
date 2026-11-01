# Enderman QA Workbench reconstructed preview 6 — Windows x64

This is a recovery preview built from an ILSpy reconstruction of the installed Enderman QA Workbench 0.3.184 binary. It is not the original source or an official build. Do not use it for production QA or Jira submissions.

## What's changed
- Added optional `ENDERMAN_QA_WEBVIEW2_USER_DATA_ROOT` to direct the embedded Only Chat WebView2 profile to an isolated folder. Unset, the previous profile location remains the default.
- Added hidden-QA-only `ENDERMAN_QA_BLOCK_EXTERNAL_NAVIGATION=1` to cancel external HTTPS/new-window navigation and suppress native connection failure dialogs. Unset, existing interactive behavior remains.
- The Workbench main window reached `main-window-content-rendered` on a task-owned private Win32 desktop and exited normally. No physical display was used.
- The embedded Only Chat control connected using a newly created isolated WebView2 profile. The loopback page navigation completed successfully. The QA guard canceled a synthetic external URL before it navigated; no external browser was opened.

## Verification limits
- .NET 10 Release build/publish completed with 0 errors and 1,010 existing reconstruction/compiler warnings.
- The Only Chat probe recorded navigation status and host only; it did not inspect page text, log in, send a message or inspect conversations. Existing Only Chat process IDs and user data were preserved.
- No screenshot-based UI review, Minecraft gameplay/client scenario, Jira submission, complete 100-case execution, installation/upgrade or signature verification is claimed.
- Actual QA portfolio remains incomplete: 100 planned; 99 dispatched; 18 full, 78 partial, 3 inconclusive, 1 not run. Source QA data was unchanged.

## Package
- Self-contained .NET 10 Windows x64 ZIP; Microsoft Edge WebView2 Evergreen Runtime is required for embedded web features.
- Unsigned; Windows may show SmartScreen warnings.
- SHA-256: `C6B20D03E8671A55B29600F2C04C2714D33FBA58576C8B1654E763983B96D3E2`

See [README](README.md), [portfolio](PORTFOLIO.md), and [third-party notices](THIRD-PARTY-NOTICES.md) for provenance, QA context and licenses.
