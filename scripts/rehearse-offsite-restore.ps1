param(
    [Parameter(Mandatory = $true)]
    [string] $BackupDir
)

$ErrorActionPreference = 'Stop'
$folder = (Resolve-Path -LiteralPath $BackupDir).Path
& (Join-Path $PSScriptRoot 'verify-offsite-backup.ps1') -BackupDir $folder

# Unique, disposable resources. No host ports and no connection to the running stacks.
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$postgresName = "gameserver-restore-pg-$suffix"
$redisName = "gameserver-restore-redis-$suffix"
$redisVolume = "gameserver-restore-redis-data-$suffix"
$postgresStarted = $false
$redisStarted = $false
$volumeCreated = $false

try {
    docker run --rm -d --network none --name $postgresName `
        --mount "type=bind,source=$folder,target=/backup,readonly" `
        -e POSTGRES_USER=restorecheck -e POSTGRES_DB=restorecheck `
        -e POSTGRES_HOST_AUTH_METHOD=trust postgres:16-alpine | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start isolated PostgreSQL container.' }
    $postgresStarted = $true

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        docker exec $postgresName pg_isready -U restorecheck -d restorecheck *> $null
        if ($LASTEXITCODE -eq 0) { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'Isolated PostgreSQL did not become ready.' }

    docker exec $postgresName pg_restore --no-owner --no-privileges `
        -U restorecheck -d restorecheck /backup/postgres.dump
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL restore failed.' }
    $tableCount = (& docker exec $postgresName psql -U restorecheck -d restorecheck -tAc `
        "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE';").Trim()
    if ($LASTEXITCODE -ne 0 -or [int]$tableCount -lt 1) {
        throw 'PostgreSQL restore produced no public tables.'
    }
    Write-Output "[OK] PostgreSQL restored: $tableCount public tables"

    docker volume create $redisVolume | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not create isolated Redis volume.' }
    $volumeCreated = $true
    docker run --rm --network none --user 0:0 `
        --mount "type=volume,source=$redisVolume,target=/data" `
        --mount "type=bind,source=$folder,target=/backup,readonly" `
        --entrypoint sh redis:7-alpine -c 'tar -xf /backup/redis-data.tar -C /data'
    if ($LASTEXITCODE -ne 0) { throw 'Could not extract Redis backup.' }

    docker run --rm -d --network none --name $redisName `
        --mount "type=volume,source=$redisVolume,target=/data" `
        redis:7-alpine redis-server --appendonly yes | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not start isolated Redis container.' }
    $redisStarted = $true

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        $ping = & docker exec $redisName redis-cli PING 2>$null
        if ($LASTEXITCODE -eq 0 -and $ping -eq 'PONG') { $ready = $true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $ready) { throw 'Isolated Redis did not become ready.' }
    $keyCount = (& docker exec $redisName redis-cli DBSIZE).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Could not query isolated Redis.' }
    Write-Output "[OK] Redis restored: $keyCount keys"
    Write-Output 'Offsite restore rehearsal passed; live staging was not touched.'
}
finally {
    if ($redisStarted) { docker stop $redisName *> $null }
    if ($postgresStarted) { docker stop $postgresName *> $null }
    if ($volumeCreated) { docker volume rm $redisVolume *> $null }
}
