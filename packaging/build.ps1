<#
.SYNOPSIS
Builds the distributable Audio Priority packages for one architecture.

.DESCRIPTION
Publishes the app self-contained (no .NET runtime needed on the target), then builds:
  - the per-user Inno Setup installer  -> artifacts\installer\AudioPrioritySetup-<ver>-<arch>.exe
  - the Microsoft Store MSIX (-Msix)   -> artifacts\msix\AudioPriority-<ver>.0-<arch>.msix

The MSIX is unsigned on purpose: Partner Center signs Store submissions. Its identity comes from
Partner Center > Product identity, passed as parameters or AUDIOPRIORITY_MSIX_* variables.

.EXAMPLE
./packaging/build.ps1 -Runtime win-x64
./packaging/build.ps1 -Runtime win-arm64 -Msix -IdentityName Publisher.AudioPriority -Publisher "CN=..." -PublisherDisplayName Publisher
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    # Three-part app version; defaults to <Version> in the app project.
    [string]$Version,

    [switch]$SkipInstaller,
    [switch]$Msix,

    [string]$IdentityName = $env:AUDIOPRIORITY_MSIX_IDENTITY_NAME,
    [string]$Publisher = $env:AUDIOPRIORITY_MSIX_PUBLISHER,
    [string]$PublisherDisplayName = $env:AUDIOPRIORITY_MSIX_PUBLISHER_DISPLAY_NAME,
    # Must equal the app name reserved in Partner Center.
    [string]$DisplayName = $(if ($env:AUDIOPRIORITY_MSIX_DISPLAY_NAME) { $env:AUDIOPRIORITY_MSIX_DISPLAY_NAME } else { 'Audio Priority' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/AudioPriorityTray/AudioPriorityTray.csproj'
$arch = $Runtime.Substring(4)
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish/$Runtime"

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File exited with $LASTEXITCODE" }
}

if (-not $Version) { $Version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must be major.minor.patch, got '$Version'." }

# Validate the Store identity before spending minutes on a build.
if ($Msix) {
    if ($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw "Invalid or missing -IdentityName (Partner Center > Product identity > Package/Identity/Name)." }
    if ($Publisher -notmatch '^CN=') { throw "Invalid or missing -Publisher; expected the Partner Center 'CN=...' value." }
    if ([string]::IsNullOrWhiteSpace($PublisherDisplayName)) { throw "Missing -PublisherDisplayName (Partner Center > Product identity)." }
}

Write-Host "Publishing $Runtime $Version (self-contained)..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Invoke-Checked dotnet @('publish', $project, '-c', 'Release', '-r', $Runtime, '--self-contained', 'true',
    "-p:Version=$Version", '-p:DebugType=none', '-o', $publishDir)
if (-not (Test-Path (Join-Path $publishDir 'AudioPriorityTray.exe'))) { throw "Publish produced no AudioPriorityTray.exe." }

if (-not $SkipInstaller) {
    $iscc = (Get-Command iscc -ErrorAction SilentlyContinue)?.Source
    $iscc ??= @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found; install it from https://jrsoftware.org/isinfo.php." }

    Write-Host "Compiling installer..."
    Invoke-Checked $iscc @('/Q', "/DMyAppVersion=$Version", "/DMyArch=$arch", "/DMyDistDir=$publishDir",
        (Join-Path $PSScriptRoot 'AudioPriorityTray.iss'))
    Write-Host (Join-Path $artifacts "installer/AudioPrioritySetup-$Version-$arch.exe")
}

if ($Msix) {
    # The Store reserves the fourth version field; submissions must leave it at 0.
    $packageVersion = "$Version.0"
    $layout = Join-Path $artifacts "msix-layout/$Runtime"
    if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
    Copy-Item $publishDir $layout -Recurse
    Copy-Item (Join-Path $PSScriptRoot 'Assets') (Join-Path $layout 'Assets') -Recurse

    $escape = [System.Security.SecurityElement]
    $manifest = (Get-Content (Join-Path $PSScriptRoot 'AppxManifest.xml') -Raw).
        Replace('{{IdentityName}}', $escape::Escape($IdentityName)).
        Replace('{{Publisher}}', $escape::Escape($Publisher)).
        Replace('{{PublisherDisplayName}}', $escape::Escape($PublisherDisplayName)).
        Replace('{{DisplayName}}', $escape::Escape($DisplayName)).
        Replace('{{Version}}', $packageVersion).
        Replace('{{Architecture}}', $arch)
    if ($manifest -match '\{\{\w+\}\}') { throw "Unfilled manifest placeholder: $($Matches[0])" }
    [xml]$manifest | Out-Null
    Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding utf8NoBOM

    $makeappx = (Get-Command makeappx.exe -ErrorAction SilentlyContinue)?.Source
    $makeappx ??= Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\10.*\x64\makeappx.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName | Select-Object -Last 1 -ExpandProperty FullName
    if (-not $makeappx) { throw "makeappx.exe not found; install the Windows SDK." }

    $msixDir = Join-Path $artifacts 'msix'
    New-Item -ItemType Directory -Force $msixDir | Out-Null
    $package = Join-Path $msixDir "AudioPriority-$packageVersion-$arch.msix"
    Write-Host "Packing MSIX..."
    Invoke-Checked $makeappx @('pack', '/d', $layout, '/p', $package, '/o')
    Write-Host $package
}
