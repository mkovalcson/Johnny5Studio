param(
    [Parameter(Mandatory = $true)][ValidateSet('Prepare', 'Commit')][string]$Phase,
    [Parameter(Mandatory = $true)][string]$MetadataPath,
    [Parameter(Mandatory = $true)][string]$StampPath,
    [string]$VersionOverride = ''
)
$ErrorActionPreference = 'Stop'
$metadata = Get-Content -LiteralPath $MetadataPath -Raw | ConvertFrom-Json
if ($metadata.version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid project version; expected major.minor.patch.' }
$previousDate = [datetime]::ParseExact($metadata.generatedDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)

if ($Phase -eq 'Prepare') {
    $today = (Get-Date).Date
    if ($today -lt $previousDate) { throw 'The system date precedes the last build date. Correct the clock before building.' }
    $parts = $metadata.version.Split('.') | ForEach-Object { [int]$_ }
    if (-not [string]::IsNullOrWhiteSpace($VersionOverride)) {
        if ($VersionOverride -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be major.minor.patch.' }
        if ([version]$VersionOverride -lt [version]$metadata.version) { throw 'Release version cannot go backwards.' }
        $parts = $VersionOverride.Split('.') | ForEach-Object { [int]$_ }
    }
    elseif ($today -eq $previousDate) { $parts[2]++ }
    else { $parts[1]++; $parts[2] = 0 }
    if (($parts | Where-Object { $_ -ge 65535 }).Count -gt 0) { throw 'Version exceeds the assembly version range.' }
    $nextVersion = $parts -join '.'
    $date = $today.ToString('yyyy-MM-dd')
    $parent = Split-Path -Parent $StampPath
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $stamp = @"
<Project><PropertyGroup>
  <GeneratedVersion>$nextVersion</GeneratedVersion>
  <GeneratedDate>$date</GeneratedDate>
  <PreviousVersion>$($metadata.version)</PreviousVersion>
  <PreviousDate>$($metadata.generatedDate)</PreviousDate>
</PropertyGroup></Project>
"@
    [IO.File]::WriteAllText($StampPath, $stamp)
    Write-Output "J5 Animation Editor & Player build: $nextVersion ($date)"
}
else {
    [xml]$stamp = Get-Content -LiteralPath $StampPath -Raw
    $values = $stamp.Project.PropertyGroup
    if ($metadata.version -ne $values.PreviousVersion -or $metadata.generatedDate -ne $values.PreviousDate) {
        throw 'Another build changed the version record. Run editor builds sequentially.'
    }
    $metadata.version = [string]$values.GeneratedVersion
    $metadata.generatedDate = [string]$values.GeneratedDate
    $metadata.notes = 'Updated automatically after a successful J5 Animation Editor & Player build.'
    $temporaryPath = $MetadataPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    $backupPath = $temporaryPath + '.previous'
    try {
        [IO.File]::WriteAllText($temporaryPath, ($metadata | ConvertTo-Json -Depth 10) + [Environment]::NewLine)
        [IO.File]::Replace($temporaryPath, $MetadataPath, $backupPath)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) { Remove-Item -LiteralPath $temporaryPath }
        if (Test-Path -LiteralPath $backupPath) { Remove-Item -LiteralPath $backupPath }
    }
}
