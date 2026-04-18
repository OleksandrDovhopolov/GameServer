param(
    [Parameter(Mandatory = $false)]
    [string]$AssetPath = "C:\Projects\Research\Assets\Game\Features\Rewards\Data\RewardSpecsConfig.asset",

    [Parameter(Mandatory = $false)]
    [string]$WheelRewardsPath = "",

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($WheelRewardsPath)) {
    $WheelRewardsPath = Join-Path $repoRoot "GameServer.Api\Data\rewards.json"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot "GameServer.Api\Data\reward_definitions.json"
}

if (!(Test-Path -LiteralPath $AssetPath)) {
    throw "RewardSpecsConfig asset file was not found: $AssetPath"
}

if (!(Test-Path -LiteralPath $WheelRewardsPath)) {
    throw "Wheel rewards file was not found: $WheelRewardsPath"
}

$assetLines = Get-Content -LiteralPath $AssetPath

$specs = New-Object System.Collections.Generic.List[object]
$currentSpec = $null
$currentResource = $null
$inResources = $false

foreach ($line in $assetLines) {
    if ($line -match '^\s*-\s*RewardId:\s*(.+?)\s*$') {
        if ($null -ne $currentResource) {
            $currentSpec.Resources.Add($currentResource) | Out-Null
            $currentResource = $null
        }

        if ($null -ne $currentSpec) {
            $specs.Add($currentSpec) | Out-Null
        }

        $currentSpec = [ordered]@{
            RewardId = $matches[1].Trim()
            Resources = (New-Object System.Collections.Generic.List[object])
        }
        $inResources = $false
        continue
    }

    if ($null -eq $currentSpec) {
        continue
    }

    if ($line -match '^\s*Resources:\s*$') {
        $inResources = $true
        continue
    }

    if (-not $inResources) {
        continue
    }

    if ($line -match '^\s*-\s*ResourceId:\s*(.+?)\s*$') {
        if ($null -ne $currentResource) {
            $currentSpec.Resources.Add($currentResource) | Out-Null
        }

        $currentResource = [ordered]@{
            ResourceId = $matches[1].Trim()
            Kind = $null
            Amount = $null
            Category = $null
        }
        continue
    }

    if ($null -eq $currentResource) {
        continue
    }

    if ($line -match '^\s*Amount:\s*(-?\d+)\s*$') {
        $currentResource.Amount = [int]$matches[1]
        continue
    }

    if ($line -match '^\s*Kind:\s*(-?\d+)\s*$') {
        $currentResource.Kind = [int]$matches[1]
        continue
    }

    if ($line -match '^\s*Category:\s*(.*?)\s*$') {
        $value = $matches[1].Trim()
        $currentResource.Category = if ([string]::IsNullOrWhiteSpace($value)) { $null } else { $value }
        continue
    }
}

if ($null -ne $currentResource) {
    $currentSpec.Resources.Add($currentResource) | Out-Null
}

if ($null -ne $currentSpec) {
    $specs.Add($currentSpec) | Out-Null
}

if ($specs.Count -eq 0) {
    throw "No RewardSpecs were parsed from asset: $AssetPath"
}

$rewardIds = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::Ordinal)
foreach ($spec in $specs) {
    $rewardId = [string]$spec.RewardId
    if ([string]::IsNullOrWhiteSpace($rewardId)) {
        throw "RewardSpec contains empty RewardId."
    }

    if (-not $rewardIds.Add($rewardId)) {
        throw "Duplicate RewardId found in asset: '$rewardId'"
    }

    if ($spec.Resources.Count -eq 0) {
        throw "RewardSpec '$rewardId' must contain at least one resource part."
    }

    foreach ($resource in $spec.Resources) {
        $targetId = [string]$resource.ResourceId
        $kind = [int]$resource.Kind
        $amount = [int]$resource.Amount

        if ([string]::IsNullOrWhiteSpace($targetId)) {
            throw "RewardSpec '$rewardId' contains a resource with empty ResourceId."
        }

        if ($kind -eq 0) {
            throw "RewardSpec '$rewardId' contains ResourceId '$targetId' with Kind=Unknown(0)."
        }

        if ($amount -le 0) {
            throw "RewardSpec '$rewardId' contains ResourceId '$targetId' with non-positive Amount='$amount'."
        }
    }
}

$wheelRewardsJson = Get-Content -LiteralPath $WheelRewardsPath -Raw | ConvertFrom-Json
$wheelIds = @(
    $wheelRewardsJson.rewards |
        ForEach-Object { $_.id } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique
)

$wheelIdSet = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::Ordinal)
foreach ($wheelId in $wheelIds) {
    [void]$wheelIdSet.Add([string]$wheelId)
}

$definitions = New-Object System.Collections.Generic.List[object]
foreach ($spec in $specs) {
    $rewardId = [string]$spec.RewardId
    $source = if ($wheelIdSet.Contains($rewardId)) { "wheel" } else { "grant" }

    $parts = New-Object System.Collections.Generic.List[object]
    foreach ($resource in $spec.Resources) {
        $part = [ordered]@{
            kind = [int]$resource.Kind
            targetId = [string]$resource.ResourceId
            amount = [int]$resource.Amount
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$resource.Category)) {
            $part.category = [string]$resource.Category
        }

        $parts.Add($part) | Out-Null
    }

    $definitions.Add([ordered]@{
            rewardId = $rewardId
            source = $source
            parts = $parts
        }) | Out-Null
}

$missingWheelRewardIds = @(
    $wheelIds |
        Where-Object { -not $rewardIds.Contains([string]$_) }
)
if ($missingWheelRewardIds.Count -gt 0) {
    throw ("Missing reward definitions for wheel reward ids: " + ($missingWheelRewardIds -join ", "))
}

$output = [ordered]@{
    rewards = $definitions
}

$json = $output | ConvertTo-Json -Depth 20
[System.IO.File]::WriteAllText($OutputPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))

Write-Host "Synchronized reward definitions to: $OutputPath"
Write-Host "Total definitions: $($definitions.Count)"
