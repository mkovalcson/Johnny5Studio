param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$solutionRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$configRoot = Join-Path (Split-Path $solutionRoot -Parent) 'Config'
if (-not (Test-Path -LiteralPath $configRoot)) { $configRoot = Join-Path $solutionRoot 'Config' }
$studioRoot = Split-Path $solutionRoot -Parent
$releaseRoot = Join-Path $studioRoot "Releases\Desktop\$Version"
$packageRoot = Join-Path $releaseRoot 'J5_Animator'
$zipPath = Join-Path $releaseRoot "J5-Studio-Desktop-$Version-$Runtime.zip"
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be major.minor.patch.' }
if (Test-Path -LiteralPath $releaseRoot) { throw "Release staging already exists: $releaseRoot" }
if (Test-Path -LiteralPath $zipPath) { throw "Release ZIP already exists: $zipPath" }
New-Item -ItemType Directory -Path $packageRoot | Out-Null
& dotnet publish (Join-Path $solutionRoot 'ServoAnimator\ServoAnimator.csproj') -c Release -r $Runtime --self-contained true -o $packageRoot -p:PublishSingleFile=false -p:PublishReadyToRun=true "-p:EditorReleaseVersion=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
if (Test-Path -LiteralPath (Join-Path $packageRoot 'Paths.json')) { throw 'Publish unexpectedly included workstation Paths.json.' }

# Runtime models are copied with Config below. Complete source archives remain
# in the workspace and are never swept into a release's model folders.
foreach ($model in @('Models\Robots\Johnny5FullBody\johnny5_head.urdf', 'Models\Scenery\MatrixConstruct\MatrixConstruct_backdrop.urdf')) {
    if (-not (Test-Path -LiteralPath (Join-Path $configRoot $model) -PathType Leaf)) {
        throw "Managed runtime model is missing: $model"
    }
}

$trackingTarget = Join-Path $packageRoot 'FaceTracking'
New-Item -ItemType Directory -Path $trackingTarget | Out-Null
foreach ($name in @('tracker.py', 'face_landmarker.task', 'requirements.txt', 'requirements-lock.txt', 'Setup.ps1')) {
    Copy-Item -LiteralPath (Join-Path $solutionRoot "FaceTracking\$name") -Destination $trackingTarget
}
[IO.File]::WriteAllText((Join-Path $trackingTarget 'STUDIO_VERSION.txt'), $Version + [Environment]::NewLine)
Copy-Item -LiteralPath (Join-Path $solutionRoot 'README.md') -Destination (Join-Path $packageRoot 'FaceTracking-README.md')
$python = Join-Path $solutionRoot 'FaceTracking\.venv\Scripts\python.exe'
& $python (Join-Path $PSScriptRoot 'Prepare-ReleaseConfig.py') --source $configRoot --output (Join-Path $packageRoot 'Config')
if ($LASTEXITCODE -ne 0) { throw 'Portable release configuration preparation failed.' }
$launcher = '@echo off' + "`r`n" + 'cd /d "%~dp0"' + "`r`n" + 'start "J5 Studio Desktop" "%~dp0AnimationEditorPlayer.exe"' + "`r`n"
[IO.File]::WriteAllText((Join-Path $packageRoot 'J5 Studio Desktop.cmd'), $launcher)
$readme = @"
J5 Animation Editor & Player $Version - $((Get-Date).ToString('yyyy-MM-dd'))
Self-contained Windows x64 deployment.

Extract the entire ZIP, then run J5 Studio Desktop.cmd.
Includes Help, robot/backdrop models and meshes, runtime dependencies, and Config.
The supplied Config retains the current libraries except the Sylogist scene and
its image. Model references are relative to Config; the startup background is
Studio and no Sylogist scene is assigned. The live source Config is unchanged.

Webcam tracking requires Python and FaceTracking/Setup.ps1 on the destination
computer. The machine-specific Python virtual environment is not bundled.
Hardware drivers must be installed separately.
"@
[IO.File]::WriteAllText((Join-Path $packageRoot 'RELEASE-README.txt'), $readme)
$versionInfo = (Get-Item -LiteralPath (Join-Path $packageRoot 'AnimationEditorPlayer.dll')).VersionInfo
if ($versionInfo.ProductVersion -ne $Version) { throw "Published version mismatch: $($versionInfo.ProductVersion)" }
if ($versionInfo.ProductName -ne 'J5 Studio Desktop') { throw 'Published product name mismatch.' }
Write-Output "Staged release: $packageRoot"
# ZIP creation follows validation, so an unverified package is never delivered.

& $python (Join-Path $studioRoot 'tools/maintain_artifacts.py') --apply
if ($LASTEXITCODE -ne 0) { throw 'Release was created but retention maintenance failed.' }
