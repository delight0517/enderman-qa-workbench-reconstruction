# Enderman QA Workbench reconstructed preview 5 — Windows x64

This is a recovery preview built from an ILSpy reconstruction of the installed Enderman QA Workbench 0.3.184 binary. It is not the original source or an official build. Do not use it for production QA or Jira submissions.

## What's changed
- Added optional `ENDERMAN_QA_WEBVIEW2_USER_DATA_ROOT` to direct the embedded Only Chat WebView2 profile to a chosen isolated folder. Unset, the previous profile location remains the default.
- The candidate's main window reached `main-window-content-rendered` during a run on a fresh task-owned private Win32 desktop. It exited normally after a task-owned close. No physical display was used.

## Verification limits
- Build: .NET 10 Release, 0 errors, 1,010 warnings.
- Private-desktop launch verifies interactive startup/content rendering only. No screenshot was taken/reviewed; Only Chat connect/login, game client/server, gameplay, Jira and exports were not exercised.
- Actual QA portfolio remains incomplete: 100 planned; 99 dispatched; 18 full, 78 partial, 3 inconclusive, 1 not run.
- Installed app and its user data were not changed. QA status file SHA-256 before/after: `D7308E6478F1B08DC1753373EAA8D7F5711A4B6324A594117FA97FE4E4DE42AB`.

## Package
- Self-contained .NET 10 Windows x64 ZIP; Microsoft Edge WebView2 Evergreen Runtime is required for embedded web features.
- Unsigned; Windows may show SmartScreen warnings.
- SHA-256: `328DCAB6A08F50B00638DFF0ED1CBF3B2A3EFB404561867DDDD0A1ADC228CED9`

See [README](README.md), [portfolio](PORTFOLIO.md), and [third-party notices](THIRD-PARTY-NOTICES.md) for provenance, QA context and licenses.
