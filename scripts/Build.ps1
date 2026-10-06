param(
    [string]$Ssms20 = 'C:\Program Files (x86)\Microsoft SQL Server Management Studio 20\Common7\IDE',
    [string]$Ssms22 = 'C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE'
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $repoRoot 'artifacts'
[xml]$properties = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props') -Raw
$version = [string]$properties.Project.PropertyGroup.Version
& dotnet build (Join-Path $repoRoot 'src/SqlPilot.Core/SqlPilot.Core.csproj') -c Release -o (Join-Path $artifacts 'core')
if ($LASTEXITCODE -ne 0) { throw 'Core build failed.' }
$built = 0
foreach ($hostSpec in @(@('20', $Ssms20), @('22', $Ssms22))) {
    if (!(Test-Path -LiteralPath (Join-Path $hostSpec[1] 'ssms.exe'))) { continue }
    $destination = Join-Path $artifacts "packages/ssms$($hostSpec[0])"
    & dotnet build (Join-Path $repoRoot 'src/SqlPilot.Ssms/SqlPilot.Ssms.csproj') -c Release "-p:HostIde=$($hostSpec[1])" -o $destination
    if ($LASTEXITCODE -ne 0) { throw "SSMS $($hostSpec[0]) build failed." }
    $manifestName = if ($hostSpec[0] -eq '20') { 'extension20.vsixmanifest' } else { 'extension.vsixmanifest' }
    $template = Get-Content -LiteralPath (Join-Path $repoRoot $manifestName) -Raw
    $template.Replace('SSMS_VERSION', $hostSpec[0]).Replace('SQLPILOT_VERSION', $version) | Set-Content -LiteralPath (Join-Path $destination 'extension.vsixmanifest') -Encoding utf8
    foreach ($asset in @('SqlPilot.pkgdef', 'sqlpilot.png')) { Copy-Item -LiteralPath (Join-Path $repoRoot "assets/$asset") -Destination $destination -Force }
    Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination (Join-Path $destination 'LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') -Destination $destination -Force
    $noticeFolder = Join-Path $destination 'third-party'
    New-Item -ItemType Directory -Path $noticeFolder -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'third-party/licenses') -Destination $noticeFolder -Recurse -Force
    if ($hostSpec[0] -eq '22') { & (Join-Path $PSScriptRoot 'Pack-Vsix.ps1') -Version '22' }
    $built++
}
if ($built -eq 0) { throw 'No supported SSMS host found. Pass -Ssms20 or -Ssms22 with the installed Common7/IDE path.' }
