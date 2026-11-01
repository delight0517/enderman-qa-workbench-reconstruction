# Enderman QA Workbench Reconstruction Preview 3

This portable Windows x64 package is an experimental reconstruction from the installed 0.3.184 binary using ILSpy. It is not the original source release and is not a production QA tool.

Preview 3 includes clearer, non-numbered project navigation and a BAML resource-base fix. The 100-case test portfolio remains incomplete. The packaged and extracted ZIP executables were constructor-smoke checked with `--construct-only`; the extracted process exited 0, and the window was not shown. ZIP SHA-256: `217BEEE51930BD4754E9735F1B0D354E1A70CFCB1B8ECF4036230CF40CA817D4`. UI screenshots were rendered separately from isolated fixture data. No installed-app upgrade, full interactive startup review, Minecraft gameplay run, or Jira submission is claimed.

Requirements: Windows 10/11 x64 and Microsoft Edge WebView2 Evergreen Runtime for embedded web features. The package is self-contained for .NET 10 and unsigned; Windows SmartScreen may warn.

Data is stored separately at `%LOCALAPPDATA%\EndermanQaWorkbench-Candidate-v27`. Do not copy its data into another Workbench installation.
