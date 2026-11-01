# UI source checkpoint — task 20261101-F

Owner: Codex `/root`
Project: Enderman QA Workbench ILSpy reconstruction, Windows WPF candidate
Base commit: `aeee0695695a01489e4069c204207df126193c20`

## Change

- `BamlHelper.cs` now supplies the recovered BAML reader with the application pack URI base and local assembly, so resource lookup uses the reconstructed application context.
- `EndermanQa.Desktop/MainStageNavigation.cs` replaces numeric stage tab captions with clear destinations while preserving stable stage IDs, the QA results position and the existing project folder tree.

## Verification

- .NET 10 linked application + renderer build: succeeded, 0 errors, 1,010 existing compiler warnings.
- Project home and 100-case results pages rendered offscreen at 1280×900 using isolated fixture data; both report `visibleWindow=False` and show 99 attempted, 18 full, 78 partial, 3 inconclusive and 1 not run.
- Candidate screenshots are review artifacts, not captures from the installed app or gameplay evidence.

## Release boundary

This source checkpoint has not been packaged as a Windows download, installed over the user's app, or verified against the installed app's data. The public `v0.1.0-preview.2` release remains an earlier build and does not include this change. The 100-case game QA remains incomplete.
