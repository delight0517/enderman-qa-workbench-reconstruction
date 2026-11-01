# UI source checkpoint — task 20261101-F

Owner: Codex `/root`
Project: Enderman QA Workbench ILSpy reconstruction, Windows WPF candidate
Base commit: `aeee0695695a01489e4069c204207df126193c20`
Source commit: `5f433b9b119d916cc4295ef167834847c68432e6`

## Change

- `BamlHelper.cs` now supplies the recovered BAML reader with the application pack URI base and local assembly, so resource lookup uses the reconstructed application context.
- `EndermanQa.Desktop/MainStageNavigation.cs` replaces numeric stage tab captions with clear destinations while preserving stable stage IDs, the QA results position and the existing project folder tree.

## Verification

- .NET 10 linked application + renderer build: succeeded, 0 errors, 1,010 existing compiler warnings.
- Project home and 100-case results pages rendered offscreen at 1280×900 using isolated fixture data; both report `visibleWindow=False` and show 99 attempted, 18 full, 78 partial, 3 inconclusive and 1 not run.
- Candidate screenshots are review artifacts, not captures from the installed app or gameplay evidence.

## Release boundary

Preview 3 package: `EndermanQaWorkbench-Reconstructed-0.3.184-preview.3-win-x64.zip`, SHA-256 `217BEEE51930BD4754E9735F1B0D354E1A70CFCB1B8ECF4036230CF40CA817D4`. The packaged and extracted executable reached `main-window-constructed-only` with isolated data; the extracted process exited 0. Its diagnostic stages were `diagnostics-ready → create-application → construct-main-window → main-window-constructed-only`; `Run` and window display were not invoked. ZIP SHA-256: `217BEEE51930BD4754E9735F1B0D354E1A70CFCB1B8ECF4036230CF40CA817D4`. This is not an installed-app update or full interactive startup review. The 100-case game QA remains incomplete.
