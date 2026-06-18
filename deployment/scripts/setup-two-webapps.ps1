#!/usr/bin/env pwsh
<#!
.SYNOPSIS
Configura dos entornos azd para desplegar dos webapps separadas, una por agente Foundry.

.DESCRIPTION
Este script crea/actualiza dos entornos azd en este repositorio:
- Entorno legal/jurisprudencia -> agente Diagramatica-LegalJuris
- Entorno EDGAR -> agente Diagramatica-Edgar

No despliega por defecto. Solo prepara variables para ejecutar azd up o azd deploy.

.EXAMPLE
./deployment/scripts/setup-two-webapps.ps1

.EXAMPLE
./deployment/scripts/setup-two-webapps.ps1 -Deploy
#>

param(
    [string]$LegalEnvironmentName = "diagramatica-legal-webapp",
    [string]$EdgarEnvironmentName = "diagramatica-edgar-webapp",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

$subscriptionId = "e7368471-6353-4c24-b4a7-61d25fe0f76e"
$tenantId = "5427c30a-5c75-45fc-8065-ec2043c8df9e"
$location = "eastus"
$foundryResourceGroup = "diagramatica-001"
$foundryResourceName = "aifoundry-diagramtica"
$projectEndpoint = "https://aifoundry-diagramtica.services.ai.azure.com/api/projects/firstProject"

function Ensure-Environment {
    param(
        [Parameter(Mandatory = $true)][string]$EnvironmentName,
        [Parameter(Mandatory = $true)][string]$AgentName
    )

    Write-Host "\n=== Configurando entorno: $EnvironmentName (agente: $AgentName) ===" -ForegroundColor Cyan

    $existingEnv = azd env list --output json | ConvertFrom-Json | Where-Object { $_.Name -eq $EnvironmentName }
    if (-not $existingEnv) {
        azd env new $EnvironmentName --subscription $subscriptionId --location $location --no-prompt | Out-Null
    } else {
        azd env select $EnvironmentName | Out-Null
    }

    azd env set AZURE_SUBSCRIPTION_ID $subscriptionId | Out-Null
    azd env set ENTRA_TENANT_ID $tenantId | Out-Null
    azd env set AZURE_LOCATION $location | Out-Null

    azd env set AI_FOUNDRY_RESOURCE_GROUP $foundryResourceGroup | Out-Null
    azd env set AI_FOUNDRY_RESOURCE_NAME $foundryResourceName | Out-Null
    azd env set AI_AGENT_ENDPOINT $projectEndpoint | Out-Null
    azd env set AI_AGENT_ID $AgentName | Out-Null

    Write-Host "[OK] Entorno $EnvironmentName listo" -ForegroundColor Green
    Write-Host "     AI_AGENT_ID=$AgentName" -ForegroundColor Gray
}

Set-Location (Join-Path $PSScriptRoot "../..")

az account set --subscription $subscriptionId

Ensure-Environment -EnvironmentName $LegalEnvironmentName -AgentName "Diagramatica-LegalJuris"
Ensure-Environment -EnvironmentName $EdgarEnvironmentName -AgentName "Diagramatica-Edgar"

if ($Deploy) {
    Write-Host "\n=== Desplegando entorno $LegalEnvironmentName ===" -ForegroundColor Yellow
    azd up -e $LegalEnvironmentName --no-prompt

    Write-Host "\n=== Desplegando entorno $EdgarEnvironmentName ===" -ForegroundColor Yellow
    azd up -e $EdgarEnvironmentName --no-prompt
}

Write-Host "\nListo. Siguientes comandos sugeridos:" -ForegroundColor Cyan
Write-Host "  azd up -e $LegalEnvironmentName --no-prompt" -ForegroundColor Gray
Write-Host "  azd up -e $EdgarEnvironmentName --no-prompt" -ForegroundColor Gray
