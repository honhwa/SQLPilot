param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$Thumbprint,
    [string]$SignToolPath = 'signtool.exe',
    [uri]$TimestampUrl = 'https://timestamp.digicert.com',
    [switch]$LocalMachine
)
$ErrorActionPreference = 'Stop'
$file = Get-Item -LiteralPath $Path
if ($file.PSIsContainer -or $file.Extension -ne '.exe') { throw 'Expected one installer EXE.' }
if (!$TimestampUrl.IsAbsoluteUri -or $TimestampUrl.Scheme -notin @('http', 'https')) { throw 'Expected an HTTP(S) RFC 3161 timestamp URL.' }
$store = if ($LocalMachine) { 'Cert:\LocalMachine\My' } else { 'Cert:\CurrentUser\My' }
$certificate = Get-Item -LiteralPath (Join-Path $store $Thumbprint) -ErrorAction SilentlyContinue
if (!$certificate -or !$certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'An accessible, currently valid code-signing certificate with a private key is required.' }
if (!($certificate.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } | ForEach-Object { $_.EnhancedKeyUsages } | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' })) { throw 'Certificate is not enabled for code signing.' }
$tool = (Get-Command $SignToolPath -CommandType Application -ErrorAction Stop).Source
$arguments = @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $TimestampUrl.AbsoluteUri, '/s', 'My', '/sha1', $Thumbprint)
if ($LocalMachine) { $arguments += '/sm' }
& $tool @arguments $file.FullName
if ($LASTEXITCODE -ne 0) { throw 'SignTool signing failed.' }
& $tool verify /pa $file.FullName
if ($LASTEXITCODE -ne 0) { throw 'SignTool trust verification failed.' }
$signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
if ($signature.Status -ne 'Valid' -or !$signature.TimeStamperCertificate -or $signature.SignerCertificate.Thumbprint -ne $Thumbprint) { throw 'Installer signature, timestamp or publisher verification failed.' }
Write-Host 'Installer signed and verified.'
