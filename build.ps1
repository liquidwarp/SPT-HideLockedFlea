param(
    [string]$SPTPath = 'D:\SPT\4.1 Fika',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [switch]$InstallMod,

    [switch]$InstallClientMod,

    [switch]$InstallServerMod,

    [switch]$PackageRelease,

    [switch]$Test
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path "$SPTPath\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll")) {
    throw "SPT not found at '$SPTPath'"
}

$buildArgs = @(
    "$PSScriptRoot\HideLockedFlea.slnx"
    "-c", $Configuration
    "-p:SPTPath=$SPTPath"
    "-p:InstallClientMod=$($InstallMod -or $InstallClientMod)"
    "-p:InstallServerMod=$($InstallMod -or $InstallServerMod)"
    "-p:PackageRelease=$($PackageRelease.IsPresent)"
    "-tlp:verbosity=detailed"
)
dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

if ($Test) {
    dotnet test "$PSScriptRoot\Tests\HideLockedFlea.Tests.csproj" -c $Configuration "-p:SPTPath=$SPTPath"
    if ($LASTEXITCODE -ne 0) { throw "Mod tests failed ($LASTEXITCODE)" }
}
