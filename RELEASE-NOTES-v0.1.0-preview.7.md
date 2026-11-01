# Enderman QA Workbench reconstructed preview 7 — Windows x64

This is a recovery preview built from an ILSpy reconstruction of the installed Enderman QA Workbench 0.3.184 binary. It is not the original source or an official build. Do not use it for production QA or Jira submissions.

## What's changed
- Simplified the Project QA home by removing a duplicate next-step panel.
- Renamed the collapsed settings disclosure to “게임 대상 및 버전”; retained the game target selector and version memo inside it. Project switching/introduction editing remains on Project Home.
- Product Release build completed with 0 warnings and 0 errors.
- Candidate reached `main-window-content-rendered` on a task-owned private Win32 desktop using a fresh isolated candidate data root. The process did not close after `WM_CLOSE`; after confirming its exact executable path and PID, the task-owned candidate process was force-stopped. Do not interpret this as graceful shutdown verification.
- A hidden synthetic fixture render at 1280×900 was visually reviewed. It used sanitized sample data and did not capture the installed app or real QA files.

## Verification limits
- Installed app and its user state were not modified. The installed EXE/DLL hashes remained unchanged. The real 100-case QA status SHA-256 remained `D7308E6478F1B08DC1753373EAA8D7F5711A4B6324A594117FA97FE4E4DE42AB`.
- No Minecraft client/gameplay or EULA action, Jira submission, Only Chat login/message, installation/upgrade, or code-signature verification was performed.
- Actual QA remains incomplete: 100 planned; 99 dispatched; 18 full, 78 partial, 3 inconclusive, 1 not run.
- Original source, official build pipeline, and signing identity remain unresolved. This package is unsigned; Windows SmartScreen may warn. WebView2 Evergreen Runtime is required for embedded web features.

## Package
- Self-contained .NET 10 Windows x64 ZIP.
- ZIP size: 68,381,961 bytes.
- ZIP SHA-256: `052AE3D34644813337714C103C7133B255273777AD98662D9D1F4712CEC74832`.
