param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$targetRoot = 'X:\Johnny5\OpenSaint'
$cleanConfig = Join-Path (Split-Path $sourceRoot -Parent) "Archive\ReleaseWork\Desktop\Repo-Config-$Version-final"
$backupRoot = Join-Path (Split-Path $sourceRoot -Parent) ("Archive\DesktopReleases\OpenSaint-$Version-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backupRoot | Out-Null
$git = Join-Path $PSScriptRoot 'Invoke-ReleaseGit.ps1'
& $git -C $targetRoot status --porcelain=v1 | Set-Content -LiteralPath (Join-Path $backupRoot 'status-before.txt')
$patchPath = Join-Path $backupRoot 'working-tree-before.patch'
& $git -C $targetRoot diff --binary "--output=$patchPath"
if ($LASTEXITCODE -ne 0) { throw 'Could not preserve the repository diff.' }
$changed = [Collections.Generic.List[string]]::new()
$verified = 0
function Copy-ReleaseFile([string]$source, [string]$relative) {
    $destination = [IO.Path]::GetFullPath((Join-Path $targetRoot $relative))
    if (-not $destination.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Destination escaped the repository.' }
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash
    if (Test-Path -LiteralPath $destination) {
        if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -eq $hash) { return }
        $backup = Join-Path $backupRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $destination -Destination $backup
    }
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force
    if ((Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $hash) { throw "Copy verification failed: $relative" }
    $changed.Add($relative.Replace('\', '/'))
}
foreach ($folder in @('ServoAnimator', 'ServoAnimator.Checks', 'FaceTracking', 'Build', 'Documentation', 'tools')) {
    $sourceFolder = if ($folder -eq 'Documentation') { Join-Path (Split-Path $sourceRoot -Parent) 'Documentation' } else { Join-Path $sourceRoot $folder }
    foreach ($file in Get-ChildItem -LiteralPath $sourceFolder -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($sourceFolder, $file.FullName)
        if ($relative -match '(^|[\\/])(bin|obj|\.vs|\.venv|__pycache__|test-data)([\\/]|$)' -or $file.Name -like '*_wpftmp.csproj' -or $file.Extension -eq '.pyc') { continue }
        if ($file.Name -like 'Sync-LocalRepo-*') { continue }
        Copy-ReleaseFile $file.FullName (Join-Path $folder $relative)
        $verified++
    }
}
foreach ($name in @('ServoAnimatorFaceTracking.slnx', 'README.md', 'Create-Deploy.cmd', 'Create-Deploy.ps1', 'TEST-RESULTS-2026-10-08.md')) {
    Copy-ReleaseFile (Join-Path $sourceRoot $name) $name
    $verified++
}
# Rename preserved originals rather than deleting obsolete source files.
foreach ($name in @('RobotHeadWindow.xaml', 'RobotHeadWindow.xaml.cs')) {
    $old = [IO.Path]::GetFullPath((Join-Path $targetRoot "ServoAnimator\$name"))
    $backup = Join-Path $backupRoot "ServoAnimator\$name"
    if (Test-Path -LiteralPath $old) {
        if (-not $old.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Obsolete source path escaped the repository.' }
        New-Item -ItemType Directory -Path (Split-Path $backup -Parent) -Force | Out-Null
        Move-Item -LiteralPath $old -Destination $backup
        $changed.Add("ServoAnimator/$name")
    }
}
# Preserve the whole existing repository Config; no library/user file is deleted.
$repositoryConfig = [IO.Path]::GetFullPath((Join-Path $targetRoot 'Config'))
if ($repositoryConfig -ne [IO.Path]::GetFullPath('X:\Johnny5\OpenSaint\Config')) { throw 'Unexpected repository Config path.' }
if (Test-Path -LiteralPath $repositoryConfig) {
    $configBackup = [IO.Path]::GetFullPath((Join-Path $backupRoot 'Config-original'))
    if (-not $configBackup.StartsWith($backupRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Config backup escaped the backup directory.' }
    Move-Item -LiteralPath $repositoryConfig -Destination $configBackup
}
foreach ($file in Get-ChildItem -LiteralPath $cleanConfig -Recurse -File) {
    Copy-ReleaseFile $file.FullName (Join-Path 'Config' ([IO.Path]::GetRelativePath($cleanConfig, $file.FullName)))
    $verified++
}
$manifest = Join-Path (Split-Path $sourceRoot -Parent) "Archive\ReleaseWork\Desktop\repo-release-$Version-paths.txt"
# Include every intended source directory so pre-existing synced release edits
# that are already identical to the live source are part of this release.
$paths = @('ServoAnimator', 'ServoAnimator.Checks', 'FaceTracking', 'tools', 'Config',
    'Build/PROJECT_VERSION.json', 'Documentation/Desktop/DOCUMENTATION_REFRESH_2026-10-08.md',
    'Documentation/Desktop/ANIMATION_EDITOR_HISTORY_2026-10-08.md',
    'Documentation/Desktop/RELEASE_2.0.0.md', 'ServoAnimatorFaceTracking.slnx', 'README.md',
    'Create-Deploy.cmd', 'Create-Deploy.ps1', 'TEST-RESULTS-2026-10-08.md')
[IO.File]::WriteAllBytes($manifest, [Text.Encoding]::UTF8.GetBytes(($paths -join [char]0) + [char]0))
$changed | Set-Content -LiteralPath (Join-Path (Split-Path $sourceRoot -Parent) "Archive\ReleaseWork\Desktop\repo-release-$Version-copied.txt")
Write-Output "Synchronized $($changed.Count) files; verified $verified source files. Preserved originals at $backupRoot"
Write-Output "Release path manifest: $manifest"
