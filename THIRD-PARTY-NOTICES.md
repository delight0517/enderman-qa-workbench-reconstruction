# Third-party notices

The reconstructed application references these packages:

- `DocumentFormat.OpenXml` 3.3.0 and `DocumentFormat.OpenXml.Framework` 3.3.0 — MIT License. Package metadata and license: https://www.nuget.org/packages/DocumentFormat.OpenXml/3.3.0
- `Microsoft.Web.WebView2` 1.0.3179.45, including Core/WPF/WinForms SDK assemblies and the x64 WebView2Loader — Microsoft license and notices are included in the NuGet package; see https://www.nuget.org/packages/Microsoft.Web.WebView2/1.0.3179.45 and https://aka.ms/MicrosoftWebView2ClientLicense
- .NET 10.0.14 and Windows Desktop Runtime 10.0.14 are bundled in the self-contained preview. Microsoft .NET licensing and notices: https://dotnet.microsoft.com/en-us/dotnet-library-license and https://github.com/dotnet/runtime/tree/v10.0.14/LICENSE.TXT

This package does not bundle the Microsoft Edge WebView2 Runtime. Features that embed web content require the Evergreen WebView2 Runtime, which can be installed from Microsoft's official distribution page: https://developer.microsoft.com/microsoft-edge/webview2/ . The app does not download or install it automatically.

The exact package versions above are pinned in `EndermanQa.Desktop.csproj`. Consult their upstream license and notice files before redistributing modified versions or adding dependencies.
