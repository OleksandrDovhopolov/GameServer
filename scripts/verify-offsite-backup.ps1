param(
    [Parameter(Mandatory = $true)]
    [string] $BackupDir
)

$ErrorActionPreference = 'Stop'
$folder = (Resolve-Path -LiteralPath $BackupDir).Path
$manifest = Join-Path $folder 'SHA256SUMS'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw 'SHA256SUMS is missing: the offsite backup is incomplete.'
}

$expectedFiles = @('postgres.dump', 'redis-data.tar')
$seen = @{}
foreach ($line in Get-Content -LiteralPath $manifest) {
    if ($line -notmatch '^([0-9a-fA-F]{64})  (postgres\.dump|redis-data\.tar)$') {
        throw "Unexpected SHA256SUMS entry: $line"
    }
    $expectedHash = $Matches[1].ToLowerInvariant()
    $name = $Matches[2]
    if ($seen.ContainsKey($name)) { throw "Duplicate entry: $name" }
    $seen[$name] = $true

    $file = Join-Path $folder $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing file: $name" }
    if ((Get-Item -LiteralPath $file).Length -eq 0) { throw "Empty file: $name" }
    $actualHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) { throw "SHA-256 mismatch: $name" }
    Write-Output "[OK] $name"
}

foreach ($name in $expectedFiles) {
    if (-not $seen.ContainsKey($name)) { throw "Missing manifest entry: $name" }
}
Write-Output 'Offsite backup files verified. Restore has not been tested yet.'
