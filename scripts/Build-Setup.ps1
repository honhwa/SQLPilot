param([string]$OutputName)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
if (!$OutputName) { $OutputName = "SqlPilotSetup-$version.exe" }
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName) { throw 'OutputName must be a filename.' }
$payload = Join-Path $artifacts 'installer-payload.zip'
$stage = Join-Path $artifacts ('work/setup-' + [Guid]::NewGuid().ToString('N'))
foreach ($name in @('payload', 'source', 'assets', 'references')) { New-Item -ItemType Directory -Path (Join-Path $stage $name) -Force | Out-Null }
foreach ($sourceFolder in @('src/SqlPilot.Ssms', 'src/Shared')) { Copy-Item -Path (Join-Path $repoRoot "$sourceFolder/*.cs") -Destination (Join-Path $stage 'source') -Force }
Copy-Item -Path (Join-Path $repoRoot 'assets/*') -Destination (Join-Path $stage 'assets') -Force
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $stage 'assets/LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $stage 'assets')
Copy-Item -LiteralPath (Join-Path $repoRoot 'third-party/licenses') -Destination (Join-Path $stage 'assets') -Recurse
foreach ($name in @('SqlPilot.Core.dll', 'Microsoft.SqlServer.TransactSql.ScriptDom.dll')) { Copy-Item -LiteralPath (Join-Path $artifacts "core/$name") -Destination (Join-Path $stage 'payload') -Force }
$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$references = Join-Path $nugetRoot 'microsoft.netframework.referenceassemblies.net472/1.0.3/build/.NETFramework/v4.7.2'
if (!(Test-Path -LiteralPath $references)) { throw 'Run Build.ps1 first to restore the reference pack.' }
Copy-Item -Path (Join-Path $references '*.dll') -Destination (Join-Path $stage 'references') -Force
Copy-Item -LiteralPath (Join-Path $references 'Facades') -Destination (Join-Path $stage 'references') -Recurse
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $payload -Force
& dotnet publish (Join-Path $repoRoot 'src/SqlPilot.Setup/SqlPilot.Setup.csproj') -c Release -r win-x64 --self-contained true '-p:PublishSingleFile=true' '-p:IncludeNativeLibrariesForSelfExtract=true' '-p:EnableCompressionInSingleFile=true' '-p:DebugType=None' -o (Join-Path $artifacts 'work/publish')
if ($LASTEXITCODE -ne 0) { throw 'Setup publish failed.' }
$release = Join-Path $artifacts 'release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $artifacts 'work/publish/SqlPilotSetup.exe') -Destination (Join-Path $release $OutputName) -Force
Write-Host "Created $(Join-Path $release $OutputName)"
