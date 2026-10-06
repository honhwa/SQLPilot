param([string]$Version='22')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$repoRoot=Split-Path $PSScriptRoot -Parent
$packageRoot=Join-Path $repoRoot "artifacts\packages\ssms$Version"
$destination=Join-Path $repoRoot "artifacts\SqlPilot-SSMS$Version.vsix"
$manifest=Join-Path $packageRoot 'extension.vsixmanifest'
if (!(Test-Path -LiteralPath $manifest)) { throw 'Build the package before packing.' }
$file=[IO.File]::Open($destination,[IO.FileMode]::Create)
$archive=New-Object IO.Compression.ZipArchive($file,[IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($entryFile in Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Where-Object Extension -ne '.pdb') {
        $name=$entryFile.FullName.Substring($packageRoot.Length+1).Replace('\','/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$entryFile.FullName,$name,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    $entry=$archive.CreateEntry('[Content_Types].xml')
    $writer=New-Object IO.StreamWriter($entry.Open())
    try { $writer.Write('<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="pkgdef" ContentType="text/plain"/><Default Extension="png" ContentType="image/png"/><Default Extension="dll" ContentType="application/octet-stream"/><Default Extension="txt" ContentType="text/plain"/><Default Extension="md" ContentType="text/markdown"/><Default Extension="rtf" ContentType="application/rtf"/><Default Extension="json" ContentType="application/json"/><Default Extension="vsixmanifest" ContentType="text/xml"/></Types>') } finally { $writer.Dispose() }
} finally { $archive.Dispose(); $file.Dispose() }
Write-Host "Created $destination"
