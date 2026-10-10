#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the HideLockedFlea server mod and produces Releases/Liquidwarp.HideLockedFlea.zip

.DESCRIPTION
    Server-only mod. It compiles against the SPT NuGet packages, so no SPT install
    is needed to build, and nothing is written into one. Everything the build
    produces stays in the repo, under bin/, obj/ and Releases/ — all gitignored.

    The archive unpacks over an SPT install as:
        SPT_Runtime/user/mods/Liquidwarp.HideLockedFlea/Liquidwarp.HideLockedFlea.Server.dll

.EXAMPLE
    ./build.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot

try {
    $modName = 'Liquidwarp.HideLockedFlea'

    Write-Host "==> Server (Release)" -ForegroundColor Cyan
    dotnet build Server/HideLockedFlea.Server.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Server build failed ($LASTEXITCODE)." }

    # Staging lives under obj\ so it stays gitignored.
    $staging = Join-Path $PSScriptRoot 'obj/package'
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    $serverMod = New-Item -ItemType Directory -Force (Join-Path $staging "SPT_Runtime/user/mods/$modName")
    Copy-Item "Server/bin/Release/net10.0/$modName.Server.dll" $serverMod
    Copy-Item LICENSE $serverMod

    $releases = New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'Releases')
    $zip = Join-Path $releases "$modName.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip

    $item = Get-Item $zip
    Write-Host "`n==> Archive" -ForegroundColor Green
    Write-Host ("    {0}" -f $item.FullName)
    Write-Host ("    {0:N0} bytes" -f $item.Length)
}
finally {
    Pop-Location
}
