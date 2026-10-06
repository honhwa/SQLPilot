$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
foreach ($project in @('tests/SqlPilot.Tests.csproj', 'tests/Workspace/WorkspaceSmoke.csproj')) {
    & dotnet run --project (Join-Path $repoRoot $project) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $project" }
}
