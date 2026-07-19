# Third-Party Notices

This file lists open-source components used by BiSheng. Licenses are those declared by upstream at the versions we reference; always verify on the project homepage when redistributing.

BiSheng itself is licensed under the [MIT License](LICENSE).

## Vendored

| Component | Location | Upstream | License |
|-----------|----------|----------|---------|
| AvalonEdit | `src/ICSharpCode.AvalonEdit/` | [icsharpcode/AvalonEdit](https://github.com/icsharpcode/AvalonEdit) | MIT |

## NuGet (runtime / product)

Versions are indicative of the tree at last update; see each `.csproj` for the pinned version.

| Package | Used by | Upstream / notes | License (declared) |
|---------|---------|------------------|--------------------|
| Markdig | Editor, Latte | [xoofx/markdig](https://github.com/xoofx/markdig) | BSD-2-Clause |
| Velopack | Latte | [velopack/velopack](https://github.com/velopack/velopack) | MIT |
| CommunityToolkit.Mvvm | Latte | [CommunityToolkit/dotnet](https://github.com/CommunityToolkit/dotnet) | MIT |
| Microsoft.EntityFrameworkCore.Sqlite | Latte, Server | [dotnet/efcore](https://github.com/dotnet/efcore) | MIT / Apache-2.0 |
| Microsoft.EntityFrameworkCore.Design | Latte, Server | same | MIT / Apache-2.0 |
| Microsoft.AspNetCore.SignalR.Client | Latte | ASP.NET Core | MIT |
| Microsoft.AspNetCore.OpenApi | Server | ASP.NET Core | MIT |
| Microsoft.Web.WebView2 | Latte | Microsoft Edge WebView2 | Microsoft Software License |
| DocumentFormat.OpenXml | Latte | [dotnet/Open-XML-SDK](https://github.com/dotnet/Open-XML-SDK) | MIT |
| NLog | Latte | [NLog/NLog](https://github.com/NLog/NLog) | BSD-3-Clause |
| Otp.NET | Server | [kspearrin/Otp.NET](https://github.com/kspearrin/Otp.NET) | MIT |
| QRCoder | Server | [codebude/QRCoder](https://github.com/codebude/QRCoder) | MIT |
| Swashbuckle.AspNetCore | Server | Swagger / OpenAPI UI | MIT |
| System.Text.Json | Shared | .NET | MIT |
| System.Security.Cryptography.ProtectedData | Latte | .NET | MIT |
| System.ComponentModel.Annotations | Shared | .NET | MIT |
| Microsoft.Extensions.DependencyInjection | Latte | .NET | MIT |

## Development / test only

| Package | Used by |
|---------|---------|
| xunit, xunit.runner.visualstudio, Xunit.StaFact | Latte.Tests, Server.Tests |
| Microsoft.NET.Test.Sdk | tests |
| Microsoft.AspNetCore.Mvc.Testing | Server.Tests |
| Microsoft.Data.Sqlite | tests |
| Microsoft.SourceLink.GitHub | AvalonEdit project |

## Acknowledgments

See also the [Acknowledgments](README.md#acknowledgments--鸣谢) section in the README.
