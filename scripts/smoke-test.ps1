param(
    [string]$ComposeFile = "docker-compose.staging.yaml",
    [string]$EnvFile = ".env.staging",
    [string]$ApiUrl = "http://localhost:5080"
)

$ErrorActionPreference = "Stop"

function Write-Ok {
    param([string]$Message)
    Write-Host "[OK] $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Invoke-Compose {
    param([string[]]$ComposeArgs)

    $output = & docker compose --env-file $EnvFile -f $ComposeFile @ComposeArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($output -join [Environment]::NewLine)
    }

    return $output
}

function Read-EnvFile {
    param([string]$Path)

    $values = @{}
    Get-Content -LiteralPath $Path | ForEach-Object {
        $line = $_.Trim()
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
            return
        }

        $separatorIndex = $line.IndexOf("=")
        if ($separatorIndex -le 0) {
            return
        }

        $key = $line.Substring(0, $separatorIndex).Trim()
        $value = $line.Substring($separatorIndex + 1).Trim()
        $values[$key] = $value
    }

    return $values
}

try {
    $envValues = Read-EnvFile $EnvFile
    $postgresUser = $envValues["POSTGRES_USER"]
    $postgresDatabase = $envValues["POSTGRES_DB"]
    if ([string]::IsNullOrWhiteSpace($postgresUser) -or [string]::IsNullOrWhiteSpace($postgresDatabase)) {
        throw "POSTGRES_USER and POSTGRES_DB must be set in $EnvFile."
    }

    $pingUrl = "$($ApiUrl.TrimEnd('/'))/ping"
    $apiResponse = Invoke-RestMethod -Uri $pingUrl -Method Get -TimeoutSec 10
    if ($apiResponse -ne "pong") {
        throw "Expected 'pong' from $pingUrl, got '$apiResponse'."
    }
    Write-Ok "API /ping returned pong"

    $redisResponse = Invoke-Compose @("exec", "-T", "redis", "redis-cli", "ping")
    $redisLine = $redisResponse |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1
    if ([string]$redisLine -ne "PONG") {
        throw "Expected 'PONG' from Redis, got '$($redisResponse -join ' ')'."
    }
    Write-Ok "Redis returned PONG"

    $postgresResponse = Invoke-Compose @(
        "exec",
        "-T",
        "postgres",
        "psql",
        "-U",
        $postgresUser,
        "-d",
        $postgresDatabase,
        "-tAc",
        "select 1;"
    )
    $postgresLine = $postgresResponse |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -First 1
    if ([string]$postgresLine -ne "1") {
        throw "Expected '1' from Postgres, got '$($postgresResponse -join ' ')'."
    }
    Write-Ok "Postgres returned 1"

    Write-Host "Smoke test passed." -ForegroundColor Green
    exit 0
}
catch {
    Write-Fail $_.Exception.Message
    exit 1
}
