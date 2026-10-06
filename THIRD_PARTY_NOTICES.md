# Third-party notices

SqlPilot's custom license applies only to its original code and assets. It does not replace the licenses of the components below. Their owners retain their rights. Release packages include this document and the `third-party/licenses/` notice folder.

| Component | Version / use | License and notice |
| --- | --- | --- |
| Microsoft SQL ScriptDom | 180.117.0; SQL parser | [MIT](third-party/licenses/ScriptDom-MIT.txt), [upstream](https://github.com/microsoft/SqlScriptDOM) |
| Microsoft.CodeAnalysis.CSharp / Common | 4.14.0; installer adapter compiler | [MIT](third-party/licenses/Roslyn-MIT.txt), [additional notices](third-party/licenses/Roslyn-ThirdPartyNotices.rtf), [upstream](https://github.com/dotnet/roslyn) |
| .NET runtime and libraries | .NET 8; self-contained installer and transitive Microsoft System packages | [MIT](third-party/licenses/DotNet-MIT.txt), [upstream](https://github.com/dotnet/runtime) |
| Windows Presentation Foundation | .NET 8 installer UI | [MIT](third-party/licenses/Wpf-MIT.txt), [upstream](https://github.com/dotnet/wpf) |
| Microsoft.NETFramework.ReferenceAssemblies / net472 | 1.0.3; build and adapter compiler references | [Package repository license](third-party/licenses/ReferenceAssemblies-MIT.txt), [upstream](https://github.com/microsoft/dotnet/tree/master/releases/reference-assemblies) |

SSMS/Visual Studio host assemblies are referenced from the user's installed host and are not copied into this repository. Microsoft products retain their own terms. NuGet restores dependencies separately; package metadata records exact versions. The SVG, PNG and ICO in `assets/` are original SqlPilot artwork covered by LICENSE.
