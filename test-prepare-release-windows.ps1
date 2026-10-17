# Standalone behavior tests; every command is fake and every file is temporary.
$ErrorActionPreference = 'Stop'
$original = Get-Location
$sandbox = Join-Path $env:TEMP ('release-test-' + [guid]::NewGuid().ToString('N'))
$previousTestState = Get-Variable -Name 'releasePreparationTestState' -Scope Global -ErrorAction SilentlyContinue
$global:releasePreparationTestState = @{ Mode = 'ok'; Gates = 0 }
try {
    New-Item -ItemType Directory -Path $sandbox | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'src/AudioPriorityTray') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'prepare-release-windows.ps1') -Destination $sandbox
    '<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $sandbox 'src/AudioPriorityTray/AudioPriorityTray.csproj')

    function git {
        param([Parameter(ValueFromRemainingArguments)] $ArgumentList)
        $global:LASTEXITCODE = 0
        switch ($ArgumentList[0]) {
            'status' {
                if ($global:releasePreparationTestState.Mode -eq 'dirty' -or
                    ($global:releasePreparationTestState.Mode -eq 'gate-dirty' -and $global:releasePreparationTestState.Gates -gt 0)) {
                    ' M example'
                }
            }
            'branch' { if ($global:releasePreparationTestState.Mode -eq 'branch') { 'topic' } else { 'main' } }
            'fetch' { if ($global:releasePreparationTestState.Mode -eq 'fetch-fail') { $global:LASTEXITCODE = 9 } }
            'rev-parse' {
                if ($global:releasePreparationTestState.Mode -eq 'upstream' -and $ArgumentList[1] -eq 'origin/main') {
                    'different'
                } else { 'same' }
            }
            default { throw "Unexpected Git command: $ArgumentList" }
        }
    }

    function dotnet {
        $global:releasePreparationTestState.Gates += 1
        $global:LASTEXITCODE = if ($global:releasePreparationTestState.Mode -eq 'gate-fail') { 8 } else { 0 }
    }

    function cmd { throw 'A dry run must never invoke a package builder.' }

    # Same parameter contract as packaging/build.ps1; writes the installer the real one would.
    New-Item -ItemType Directory -Path (Join-Path $sandbox 'packaging') | Out-Null
    @'
param([Parameter(Mandatory)] [ValidateSet('win-x64', 'win-arm64')] [string] $Runtime, [string] $Version)
$installer = Join-Path $PSScriptRoot '../artifacts/installer'
New-Item -ItemType Directory -Force -Path $installer | Out-Null
Set-Content -LiteralPath (Join-Path $installer "AudioTraySetup-$Version-$($Runtime.Substring(4)).exe") -Value 'installer'
$global:releasePreparationTestState.Built = "$Runtime $Version"
'@ | Set-Content -LiteralPath (Join-Path $sandbox 'packaging/build.ps1')

    & (Join-Path $sandbox 'prepare-release-windows.ps1') -Tag v1.2.3 -DryRun
    if ($global:releasePreparationTestState.Gates -eq 0) { throw 'Dry run skipped native gates.' }
    if ($global:releasePreparationTestState.ContainsKey('Built')) { throw 'A dry run invoked the package builder.' }

    $cases = @{
        'dirty' = 'clean'
        'branch' = 'main'
        'upstream' = 'origin/main'
        'fetch-fail' = 'failed'
        'gate-fail' = 'failed'
        'gate-dirty' = 'clean'
    }
    foreach ($case in $cases.GetEnumerator()) {
        $global:releasePreparationTestState.Mode = $case.Key
        $global:releasePreparationTestState.Gates = 0
        $caught = $false
        try {
            & (Join-Path $sandbox 'prepare-release-windows.ps1') -Tag v1.2.3 -DryRun
        } catch {
            if ($_.Exception.Message -notmatch $case.Value) { throw }
            $caught = $true
        }
        if (-not $caught) { throw "Invalid release condition was accepted: $($case.Key)" }
        if ($case.Key -in @('dirty', 'branch', 'upstream', 'fetch-fail') -and $global:releasePreparationTestState.Gates -ne 0) {
            throw "A rejected checkout ran gates: $($case.Key)"
        }
    }
    $global:releasePreparationTestState.Mode = 'ok'
    & (Join-Path $sandbox 'prepare-release-windows.ps1') -Tag v1.2.3 -Runtime win-arm64 | Out-Null
    if ($global:releasePreparationTestState.Built -ne 'win-arm64 1.2.3') {
        throw "Real mode built '$($global:releasePreparationTestState.Built)' instead of 'win-arm64 1.2.3'."
    }

    Write-Host 'audio-tray: 8 release preparation scenarios passed'
}
finally {
    Set-Location $original
    if ($previousTestState) {
        Set-Variable -Name 'releasePreparationTestState' -Scope Global -Value $previousTestState.Value
    } else {
        Remove-Variable -Name 'releasePreparationTestState' -Scope Global
    }
    $resolved = [IO.Path]::GetFullPath($sandbox)
    $tempRoot = [IO.Path]::GetFullPath($env:TEMP) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempRoot) -or -not (Split-Path $resolved -Leaf).StartsWith('release-test-')) {
        throw 'Refusing unsafe test cleanup.'
    }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
$global:LASTEXITCODE = 0
