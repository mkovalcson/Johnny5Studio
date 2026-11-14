$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetRoot = 'X:\Johnny5\OpenSaint'
$maps = @{
    'ServoAnimator' = 'ServoAnimator'
    'ServoAnimator.Checks' = 'ServoAnimator.Checks'
    'FaceTracking' = 'FaceTracking'
    'Build' = 'Build'
    'Documentation' = 'Documentation'
    'tools' = 'tools'
    'Config' = 'Config'
}
$copied = [Collections.Generic.List[string]]::new()
$verified = 0
foreach ($entry in $maps.GetEnumerator()) {
    $from = if ($entry.Key -in @('Config','Documentation')) { Join-Path (Split-Path $sourceRoot -Parent) $entry.Key } else { Join-Path $sourceRoot $entry.Key }
    foreach ($file in Get-ChildItem -LiteralPath $from -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($from, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj|\.vs|\.venv|__pycache__|test-data)([\\/]|$)' -or $file.Name -like '*_wpftmp.csproj' -or $file.Extension -eq '.pyc') { continue }
        if ($entry.Key -eq 'Config' -and ($relative -match '^(Projects[\\/]|EditorLayout|EditorRecovery|RecentFiles)')) { continue }
        if ($file.Name -like 'Sync-LocalRepo-*') { continue }
        $destination = Join-Path (Join-Path $targetRoot $entry.Value) $relative
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        if (!(Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) {
            New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
            if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw "Copy verification failed: $destination" }
            $copied.Add([IO.Path]::GetRelativePath($targetRoot, $destination))
        }
        $verified++
    }
}
foreach ($name in @('ServoAnimatorFaceTracking.slnx','README.md','Create-Deploy.cmd','Create-Deploy.ps1','TEST-RESULTS-2026-10-05.md')) {
    $source = Join-Path $sourceRoot $name
    $destination = Join-Path $targetRoot $name
    $hash = (Get-FileHash -LiteralPath $source).Hash
    if (!(Test-Path -LiteralPath $destination) -or (Get-FileHash -LiteralPath $destination).Hash -ne $hash) {
        Copy-Item -LiteralPath $source -Destination $destination -Force
        if ((Get-FileHash -LiteralPath $destination).Hash -ne $hash) { throw "Copy verification failed: $destination" }
        $copied.Add($name)
    }
    $verified++
}
$copied | Set-Content -LiteralPath (Join-Path $sourceRoot 'local-repo-sync-2026-10-05.txt')
"Copied $($copied.Count) changed/new files; verified $verified source files. No destination files deleted."
