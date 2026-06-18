#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Provision a shared Cosmos DB Serverless account in diagramatica-001,
    assign RBAC to Legal and Edgar managed identities, and update both
    azd environments with COSMOS_ENDPOINT.

.DESCRIPTION
    - Creates the Cosmos account via Bicep (Serverless, managed-identity-only auth).
    - Grants "Cosmos DB Built-in Data Contributor" to Legal and Edgar MIs.
    - Sets COSMOS_ENDPOINT in both azd environments so Container Apps pick it up
      on the next deploy.
    - Does NOT run azd up — run deploy-two-webapps.ps1 (or az containerapp update)
      after this script to push the new env var.

.EXAMPLE
    # Provision Cosmos and update azd envs
    ./deployment/scripts/setup-cosmos.ps1

    # Then redeploy both apps to inject COSMOS_ENDPOINT
    ./deployment/scripts/deploy-two-webapps.ps1
#>

[CmdletBinding()]
param (
    # Central resource group where Cosmos lives
    [string]$CosmosResourceGroup = "diagramatica-001",

    # Cosmos account name — must be globally unique; default uses a stable hash
    [string]$CosmosAccountName = "cosmos-diagramatica",

    # Azure location (must match existing RG or be auto-detected)
    [string]$Location = "eastus",

    # Legal Container App details (read from azd env if not provided)
    [string]$LegalEnvName = "diagramatica-legal-webapp",
    [string]$EdgarEnvName = "diagramatica-edgar-webapp"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

Write-Host "`n== Cosmos DB Serverless Provision ==" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 1. Verify Azure login
# ---------------------------------------------------------------------------
$account = az account show --query "{sub:id,name:name}" -o json | ConvertFrom-Json
if (-not $account) { throw "Not logged into Azure. Run 'az login' first." }
Write-Host "Subscription: $($account.name)" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 2. Resolve managed identity principal IDs from azd env files
# ---------------------------------------------------------------------------
$repoRoot = (Resolve-Path "$PSScriptRoot\..\.." ).Path

function Get-AzdEnvValue([string]$EnvName, [string]$Key) {
    $envFile = "$repoRoot\.azure\$EnvName\.env"
    if (-not (Test-Path $envFile)) { throw "azd env file not found: $envFile" }
    $line = Get-Content $envFile | Where-Object { $_ -match "^$Key=" }
    if (-not $line) { throw "$Key not found in $envFile" }
    ($line -split "=", 2)[1].Trim('"')
}

$legalMiPrincipalId = Get-AzdEnvValue $LegalEnvName "WEB_IDENTITY_PRINCIPAL_ID"
$edgarMiPrincipalId = Get-AzdEnvValue $EdgarEnvName "WEB_IDENTITY_PRINCIPAL_ID"
$legalContainerApp  = Get-AzdEnvValue $LegalEnvName "AZURE_CONTAINER_APP_NAME"
$edgarContainerApp  = Get-AzdEnvValue $EdgarEnvName "AZURE_CONTAINER_APP_NAME"
$legalRg            = Get-AzdEnvValue $LegalEnvName "AZURE_RESOURCE_GROUP_NAME"
$edgarRg            = Get-AzdEnvValue $EdgarEnvName "AZURE_RESOURCE_GROUP_NAME"

Write-Host "Legal  MI: $legalMiPrincipalId"
Write-Host "Edgar  MI: $edgarMiPrincipalId"

# ---------------------------------------------------------------------------
# 3. Ensure Cosmos RG exists
# ---------------------------------------------------------------------------
$rgExists = az group exists --name $CosmosResourceGroup
if ($rgExists -eq "false") {
    Write-Host "Creating resource group $CosmosResourceGroup..." -ForegroundColor Yellow
    az group create --name $CosmosResourceGroup --location $Location --output none
}

# ---------------------------------------------------------------------------
# 4. Deploy Cosmos via Bicep
# ---------------------------------------------------------------------------
$bicepFile = "$repoRoot\infra\core\data\cosmos.bicep"
Write-Host "Deploying Cosmos DB Serverless via Bicep..." -ForegroundColor Yellow

$deployResult = az deployment group create `
    --resource-group $CosmosResourceGroup `
    --name "cosmos-$(Get-Date -Format 'yyyyMMddHHmmss')" `
    --template-file $bicepFile `
    --parameters `
        name=$CosmosAccountName `
        location=$Location `
        "dataContributorPrincipalIds=['$legalMiPrincipalId','$edgarMiPrincipalId']" `
    --query "properties.outputs" `
    --output json | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) { throw "Bicep deployment failed." }

$cosmosEndpoint = $deployResult.accountEndpoint.value
Write-Host "Cosmos endpoint: $cosmosEndpoint" -ForegroundColor Green

# ---------------------------------------------------------------------------
# 5. Update azd environments with COSMOS_ENDPOINT
# ---------------------------------------------------------------------------
foreach ($envName in @($LegalEnvName, $EdgarEnvName)) {
    Write-Host "Updating azd env $envName with COSMOS_ENDPOINT..." -ForegroundColor Yellow
    azd env set COSMOS_ENDPOINT $cosmosEndpoint --environment $envName
}

# ---------------------------------------------------------------------------
# 6. Update running Container Apps immediately (no full redeploy needed for env vars)
# ---------------------------------------------------------------------------
Write-Host "Injecting COSMOS_ENDPOINT into running Legal container app..." -ForegroundColor Yellow
az containerapp update `
    --name $legalContainerApp `
    --resource-group $legalRg `
    --set-env-vars "COSMOS_ENDPOINT=$cosmosEndpoint" `
    --only-show-errors | Out-Null
Write-Host "  Legal updated." -ForegroundColor Green

Write-Host "Injecting COSMOS_ENDPOINT into running Edgar container app..." -ForegroundColor Yellow
az containerapp update `
    --name $edgarContainerApp `
    --resource-group $edgarRg `
    --set-env-vars "COSMOS_ENDPOINT=$cosmosEndpoint" `
    --only-show-errors | Out-Null
Write-Host "  Edgar updated." -ForegroundColor Green

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
Write-Host "`n== Done ==" -ForegroundColor Cyan
Write-Host "Cosmos endpoint : $cosmosEndpoint"
Write-Host "Legal  container: $legalContainerApp ($legalRg)"
Write-Host "Edgar  container: $edgarContainerApp ($edgarRg)"
Write-Host ""
Write-Host "Both container apps have been updated. A new revision will roll out automatically." -ForegroundColor Green
Write-Host "Validate with:" -ForegroundColor Gray
Write-Host "  az containerapp logs show -n $legalContainerApp -g $legalRg --tail 50" -ForegroundColor Gray
Write-Host "  az containerapp logs show -n $edgarContainerApp -g $edgarRg --tail 50" -ForegroundColor Gray
