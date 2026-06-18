#!/usr/bin/env pwsh
<#!
.SYNOPSIS
Genera dos paquetes de app de Teams (.zip), uno para Legal/Juris y otro para Edgar.

.DESCRIPTION
Este script crea artefactos listos para subir en Teams Admin Center:
- manifest.json
- color.png (192x192)
- outline.png (32x32)

Requiere las URLs publicas de cada webapp.

.EXAMPLE
./deployment/scripts/generate-teams-app-packages.ps1 \
  -LegalAppUrl "https://legal-app.contoso.com" \
  -EdgarAppUrl "https://edgar-app.contoso.com"
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$LegalAppUrl,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^https://')]
    [string]$EdgarAppUrl,

    [string]$LegalAppId = "a8af14e1-b1b6-4556-9c58-a44c0433ffe9",
    [string]$EdgarAppId = "ed3aa9c2-1e29-4112-b26b-b5cd0177f7c3",

    [string]$PublisherName = "Diagramatica",
    [string]$PublisherWebsite = "https://diagramatica.com",
    [string]$PrivacyUrl = "https://diagramatica.com/privacy",
    [string]$TermsOfUseUrl = "https://diagramatica.com/terms",

    [string]$AppPackageVersion = "1.0.0"
)

$ErrorActionPreference = "Stop"

function New-SolidIcon {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][int]$Size,
        [Parameter(Mandatory = $true)][string]$HexColor,
        [Parameter(Mandatory = $true)][string]$Text,
        [int]$FontSize = 64
    )

    Add-Type -AssemblyName System.Drawing

    $bmp = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $bg = [System.Drawing.ColorTranslator]::FromHtml($HexColor)
    $graphics.Clear($bg)

    $fontFamily = New-Object System.Drawing.FontFamily("Segoe UI")
    $font = New-Object System.Drawing.Font($fontFamily, $FontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center

    $rect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)
    $graphics.DrawString($Text, $font, $brush, $rect, $format)

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $brush.Dispose()
    $font.Dispose()
    $graphics.Dispose()
    $bmp.Dispose()
}

function New-OutlineIcon {
    param(
        [Parameter(Mandatory = $true)][string]$Path
    )

    Add-Type -AssemblyName System.Drawing

    $size = 32
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bmp)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, 2)
    $graphics.DrawRectangle($pen, 3, 3, 26, 26)

    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $pen.Dispose()
    $graphics.Dispose()
    $bmp.Dispose()
}

function New-TeamsPackage {
    param(
        [Parameter(Mandatory = $false)][string]$AppId,
        [Parameter(Mandatory = $true)][string]$AppSlug,
        [Parameter(Mandatory = $true)][string]$ShortName,
        [Parameter(Mandatory = $true)][string]$FullName,
        [Parameter(Mandatory = $true)][string]$DescriptionShort,
        [Parameter(Mandatory = $true)][string]$DescriptionFull,
        [Parameter(Mandatory = $true)][string]$AppUrl,
        [Parameter(Mandatory = $true)][string]$ColorHex,
        [Parameter(Mandatory = $true)][string]$BadgeText,
        [Parameter(Mandatory = $true)][string]$OutputRoot
    )

    if (-not $AppId) {
        $AppId = [guid]::NewGuid().ToString()
    }
    $appHost = ([System.Uri]$AppUrl).Host
    $pkgFolder = Join-Path $OutputRoot $AppSlug

    if (Test-Path $pkgFolder) {
        Remove-Item -Path $pkgFolder -Recurse -Force
    }
    New-Item -ItemType Directory -Path $pkgFolder | Out-Null

    $colorIconPath = Join-Path $pkgFolder "color.png"
    $outlineIconPath = Join-Path $pkgFolder "outline.png"
    New-SolidIcon -Path $colorIconPath -Size 192 -HexColor $ColorHex -Text $BadgeText -FontSize 96
    New-OutlineIcon -Path $outlineIconPath

    $manifest = @{
        '$schema' = 'https://developer.microsoft.com/json-schemas/teams/v1.19/MicrosoftTeams.schema.json'
        manifestVersion = '1.19'
        version = $AppPackageVersion
        id = $AppId
        developer = @{
            name = $PublisherName
            websiteUrl = $PublisherWebsite
            privacyUrl = $PrivacyUrl
            termsOfUseUrl = $TermsOfUseUrl
        }
        name = @{
            short = $ShortName
            full = $FullName
        }
        description = @{
            short = $DescriptionShort
            full = $DescriptionFull
        }
        icons = @{
            color = 'color.png'
            outline = 'outline.png'
        }
        accentColor = $ColorHex
        staticTabs = @(
            @{
                entityId = "$AppSlug-home"
                name = $ShortName
                contentUrl = $AppUrl
                websiteUrl = $AppUrl
                scopes = @('personal')
            }
        )
        permissions = @('identity', 'messageTeamMembers')
        validDomains = @($appHost)
    }

    $manifestPath = Join-Path $pkgFolder "manifest.json"
    ($manifest | ConvertTo-Json -Depth 15) | Set-Content -Path $manifestPath -Encoding UTF8

    $zipPath = Join-Path $OutputRoot ("teamsapp-{0}.zip" -f $AppSlug)
    if (Test-Path $zipPath) {
        Remove-Item -Path $zipPath -Force
    }

    Compress-Archive -Path @($manifestPath, $colorIconPath, $outlineIconPath) -DestinationPath $zipPath

    return @{
        AppId = $AppId
        ZipPath = $zipPath
    }
}

$repoRoot = Join-Path $PSScriptRoot "../.."
$outputRoot = Join-Path $repoRoot "teams/packages"
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

$legalResult = New-TeamsPackage `
    -AppId $LegalAppId `
    -AppSlug "legal-juris" `
    -ShortName "Diag Legal" `
    -FullName "Diagramatica Legal y Jurisprudencia" `
    -DescriptionShort "Consulta normativa y jurisprudencia" `
    -DescriptionFull "App de Teams para consultas legales con fuentes de legislacion y jurisprudencia." `
    -AppUrl $LegalAppUrl `
    -ColorHex "#0B5CAD" `
    -BadgeText "L" `
    -OutputRoot $outputRoot

$edgarResult = New-TeamsPackage `
    -AppId $EdgarAppId `
    -AppSlug "edgar" `
    -ShortName "Diag Edgar" `
    -FullName "Diagramatica Edgar" `
    -DescriptionShort "Consulta filings y reportes EDGAR" `
    -DescriptionFull "App de Teams para consultas financieras sobre filings y reportes EDGAR." `
    -AppUrl $EdgarAppUrl `
    -ColorHex "#007A4D" `
    -BadgeText "E" `
    -OutputRoot $outputRoot

Write-Host "" 
Write-Host "Paquetes generados:" -ForegroundColor Green
Write-Host "  Legal/Juris: $($legalResult.ZipPath)" -ForegroundColor Gray
Write-Host "  Edgar:       $($edgarResult.ZipPath)" -ForegroundColor Gray
Write-Host "" 
Write-Host "App IDs generados:" -ForegroundColor Green
Write-Host "  Legal/Juris: $($legalResult.AppId)" -ForegroundColor Gray
Write-Host "  Edgar:       $($edgarResult.AppId)" -ForegroundColor Gray
Write-Host "" 
Write-Host "Siguiente paso: subir ambos .zip en Teams Admin Center y asignar disponibilidad org-wide." -ForegroundColor Cyan
