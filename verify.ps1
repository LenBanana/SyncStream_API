<#
.SYNOPSIS
    Builds and tests everything. One command to answer "did I break it".

.DESCRIPTION
    The backend and the Angular frontend live in separate repositories, so the frontend is checked only
    when its path resolves. Pass -FrontendPath, or set SYNCSTREAM_FRONTEND, to point somewhere else.
    Skipping the frontend is reported, never silent.

.EXAMPLE
    ./verify.ps1
    ./verify.ps1 -SkipFrontend
    ./verify.ps1 -FrontendPath 'D:\code\Drecktube Angular'
#>
[CmdletBinding()]
param(
    [string] $FrontendPath = $(if ($env:SYNCSTREAM_FRONTEND) { $env:SYNCSTREAM_FRONTEND } else { 'C:\Angular Projects\Drecktube Angular' }),
    [switch] $SkipFrontend
)

# Deliberately not 'Stop'. Native tools such as ng write progress to stderr, and Windows PowerShell turns
# that into a terminating error even when the tool exits 0. Success is decided by $LASTEXITCODE below.
$ErrorActionPreference = 'Continue'

$root = $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]

function Invoke-Step {
    param(
        [string] $Name,
        [string] $WorkingDirectory,
        [scriptblock] $Action
    )

    Write-Host ''
    Write-Host ('--- {0} {1}' -f $Name, ('-' * [Math]::Max(1, 56 - $Name.Length)))

    Push-Location $WorkingDirectory
    try {
        $global:LASTEXITCODE = 0
        & $Action
        if ($LASTEXITCODE -ne 0) {
            $failures.Add(('{0} (exit {1})' -f $Name, $LASTEXITCODE))
            Write-Host ('FAILED: {0}' -f $Name) -ForegroundColor Red
        }
        else {
            Write-Host ('ok: {0}' -f $Name) -ForegroundColor Green
        }
    }
    finally {
        Pop-Location
    }
}

Invoke-Step 'backend build' $root { dotnet build --nologo -v q }
Invoke-Step 'backend tests' $root { dotnet test --nologo -v q }

if ($SkipFrontend) {
    Write-Host ''
    Write-Host 'SKIPPED: frontend (-SkipFrontend)' -ForegroundColor Yellow
}
elseif (-not (Test-Path (Join-Path $FrontendPath 'package.json'))) {
    Write-Host ''
    Write-Host ("SKIPPED: frontend, no package.json under '{0}'" -f $FrontendPath) -ForegroundColor Yellow
    Write-Host '         Pass -FrontendPath or set SYNCSTREAM_FRONTEND to check it.' -ForegroundColor Yellow
}
else {
    Invoke-Step 'frontend build' $FrontendPath {
        npx --no-install ng build --configuration production --progress=false
    }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host ('{0} step(s) failed:' -f $failures.Count) -ForegroundColor Red
    $failures | ForEach-Object { Write-Host ('  - {0}' -f $_) -ForegroundColor Red }
    exit 1
}

Write-Host 'All checks passed.' -ForegroundColor Green
exit 0
