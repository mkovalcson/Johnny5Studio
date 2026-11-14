param([switch] $Fixture)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root 'ConfigTemplate'
$target = Join-Path $root $(if ($Fixture) { 'TestFixtures/Work/Config' } else { 'Config' })
if (Test-Path -LiteralPath $target) {
    throw "Config already exists: $target. Existing data was not changed."
}
New-Item -ItemType Directory -Path $target -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $target -Recurse
Write-Output $target
