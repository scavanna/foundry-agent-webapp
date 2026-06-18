#!/usr/bin/env pwsh
<#!
.SYNOPSIS
Configura y despliega dos webapps separadas para dos agentes Foundry.

.DESCRIPTION
Wrapper de conveniencia sobre setup-two-webapps.ps1 con -Deploy activado.
#>

$ErrorActionPreference = "Stop"

& "$PSScriptRoot/setup-two-webapps.ps1" -Deploy
